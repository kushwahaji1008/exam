using MongoDB.Driver;
using WalletService.Models;

namespace WalletService.Services
{
    public interface IWalletReferralService
    {
        Task<(string ReferralCode, bool CanApplyReferralCode, string? ReferredByCode, bool ReferralRewarded)> GetReferralStatusAsync(string userId);
        Task<(bool Success, string Message, string? AppliedCode)> ApplyReferralCodeAsync(string userId, string referralCode);
    }

    public class WalletManager
    {
        public const decimal ReferralRewardCoins = 100m;

        private readonly IMongoCollection<Wallet> _wallets;
        private readonly IMongoCollection<WalletTransaction> _transactions;
        private readonly IMongoCollection<ReferralRecord> _referrals;

        public WalletManager(MongoDbService mongoDb)
        {
            _wallets = mongoDb.Wallets;
            _transactions = mongoDb.WalletTransactions;
            _referrals = mongoDb.Referrals;
        }

        public async Task<Wallet> GetBalanceAsync(string userId)
        {
            var filter = Builders<Wallet>.Filter.Eq(w => w.UserId, userId);
            var update = Builders<Wallet>.Update
                .SetOnInsert(w => w.Id, Guid.NewGuid().ToString())
                .SetOnInsert(w => w.UserId, userId)
                .SetOnInsert(w => w.Balance, 0m)
                .SetOnInsert(w => w.CreatedAt, DateTime.UtcNow)
                .SetOnInsert(w => w.UpdatedAt, DateTime.UtcNow);

            return await _wallets.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<Wallet>
                {
                    IsUpsert = true,
                    ReturnDocument = ReturnDocument.After
                });
        }

        public async Task<(List<WalletTransaction> Items, long Total)> GetTransactionsAsync(
            string userId,
            int page = 1,
            int pageSize = 20,
            string? type = null)
        {
            var safePage = Math.Max(1, page);
            var safeSize = Math.Clamp(pageSize, 1, 100);
            var fb = Builders<WalletTransaction>.Filter;
            var filter = fb.Eq(t => t.UserId, userId);

            if (!string.IsNullOrWhiteSpace(type) &&
                (type.Equals("CREDIT", StringComparison.OrdinalIgnoreCase) ||
                 type.Equals("DEBIT", StringComparison.OrdinalIgnoreCase)))
            {
                filter = fb.And(filter, fb.Eq(t => t.Type, type.Trim().ToUpperInvariant()));
            }

            var total = await _transactions.CountDocumentsAsync(filter);
            var items = await _transactions.Find(filter)
                .SortByDescending(t => t.Timestamp)
                .Skip((safePage - 1) * safeSize)
                .Limit(safeSize)
                .ToListAsync();

            return (items, total);
        }

        // ==========================================
        // DEBIT LOGIC (Strict Amount Validation + Idempotency-First + Atomic)
        // ==========================================
        public async Task<(bool Success, string Message)> DebitAsync(
            string userId,
            decimal amount,
            string idempotencyKey,
            string referenceId,
            string sourceService,
            string? description = null)
        {
            if (string.IsNullOrWhiteSpace(userId)) return (false, "User ID is required.");
            if (amount < 0) return (false, "Debit amount cannot be negative.");
            if (amount == 0) return (true, "No payment required.");
            if (string.IsNullOrWhiteSpace(idempotencyKey)) return (false, "Idempotency key is required.");

            // Claim idempotency key FIRST to eliminate concurrent double-spend races
            var txn = new WalletTransaction
            {
                UserId = userId,
                IdempotencyKey = idempotencyKey,
                Amount = amount,
                Type = "DEBIT",
                ReferenceId = referenceId,
                SourceService = sourceService,
                Description = string.IsNullOrWhiteSpace(description)
                    ? $"Paid for {referenceId} via {sourceService}"
                    : description.Trim(),
                Timestamp = DateTime.UtcNow
            };

            try
            {
                await _transactions.InsertOneAsync(txn);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                return (true, "Transaction already processed successfully.");
            }

            // Atomic balance check & deduction
            var filter = Builders<Wallet>.Filter.And(
                Builders<Wallet>.Filter.Eq(w => w.UserId, userId),
                Builders<Wallet>.Filter.Gte(w => w.Balance, amount)
            );

            var update = Builders<Wallet>.Update
                .Inc(w => w.Balance, -amount)
                .Set(w => w.UpdatedAt, DateTime.UtcNow);

            var result = await _wallets.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<Wallet> { ReturnDocument = ReturnDocument.After });

            if (result == null)
            {
                // Compensate: release idempotency lock so user can retry after topping up
                await _transactions.DeleteOneAsync(t => t.Id == txn.Id);
                var exists = await _wallets.Find(w => w.UserId == userId).AnyAsync();
                return exists ? (false, "Insufficient coin balance. Earn 100 coins per referral or top up your wallet.") : (false, "Wallet has 0 coins. Earn 100 coins per referral before purchasing.");
            }

            return (true, "Payment successful.");
        }

        // ==========================================
        // CREDIT LOGIC (Validation + Idempotency-First + Atomic)
        // ==========================================
        public async Task<(bool Success, string Message)> CreditAsync(
            string userId,
            decimal amount,
            string idempotencyKey,
            string referenceId,
            string description,
            string sourceService = "InternalSystem")
        {
            if (string.IsNullOrWhiteSpace(userId)) return (false, "User ID is required.");
            if (amount <= 0) return (false, "Credit amount must be greater than zero.");
            if (string.IsNullOrWhiteSpace(idempotencyKey)) return (false, "Idempotency key is required.");

            var txn = new WalletTransaction
            {
                UserId = userId,
                IdempotencyKey = idempotencyKey,
                Amount = amount,
                Type = "CREDIT",
                ReferenceId = referenceId,
                SourceService = sourceService,
                Description = description,
                Timestamp = DateTime.UtcNow
            };

            try
            {
                await _transactions.InsertOneAsync(txn);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                return (true, "Transaction already processed.");
            }

            await GetBalanceAsync(userId); // Ensure wallet exists atomically

            var update = Builders<Wallet>.Update
                .Inc(w => w.Balance, amount)
                .Set(w => w.UpdatedAt, DateTime.UtcNow);

            await _wallets.UpdateOneAsync(w => w.UserId == userId, update);
            return (true, "Coins added successfully.");
        }

        // ==========================================
        // REFERRAL REWARD LOGIC (100 Coins to BOTH Users)
        // ==========================================
        public async Task<(bool Success, string Message)> CreditReferralBonusPairAsync(
            string referrerUserId,
            string referrerName,
            string referredUserId,
            string referredName,
            string referredEmail,
            string referralCode)
        {
            if (string.IsNullOrWhiteSpace(referrerUserId) || string.IsNullOrWhiteSpace(referredUserId))
                return (false, "Referrer and referred user IDs are required.");
            if (referrerUserId == referredUserId)
                return (false, "Self-referral is not permitted.");

            var safeCode = string.IsNullOrWhiteSpace(referralCode) ? "REFERRAL" : referralCode.Trim().ToUpperInvariant();
            var friendlyReferred = string.IsNullOrWhiteSpace(referredName) ? referredUserId : referredName.Trim();
            var friendlyReferrer = string.IsNullOrWhiteSpace(referrerName) ? referrerUserId : referrerName.Trim();

            var referrerResult = await CreditAsync(
                referrerUserId,
                ReferralRewardCoins,
                $"REF_BONUS_REFERRER_{referredUserId}",
                safeCode,
                $"Referral reward: {friendlyReferred} joined using your code {safeCode} (+{ReferralRewardCoins} coins)",
                "ReferralProgram");

            if (!referrerResult.Success)
                return referrerResult;

            var refereeResult = await CreditAsync(
                referredUserId,
                ReferralRewardCoins,
                $"REF_BONUS_REFEREE_{referredUserId}",
                safeCode,
                $"Referral welcome bonus from {friendlyReferrer} using code {safeCode} (+{ReferralRewardCoins} coins)",
                "ReferralProgram");

            if (!refereeResult.Success)
                return refereeResult;

            var record = new ReferralRecord
            {
                ReferrerUserId = referrerUserId,
                ReferrerName = friendlyReferrer,
                ReferredUserId = referredUserId,
                ReferredUserName = friendlyReferred,
                ReferredUserEmail = referredEmail ?? string.Empty,
                ReferralCode = safeCode,
                ReferrerCoins = ReferralRewardCoins,
                ReferredCoins = ReferralRewardCoins,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                await _referrals.InsertOneAsync(record);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Already recorded idempotently
            }

            return (true, $"Credited {ReferralRewardCoins} coins to both referrer and new user.");
        }

        public async Task<(List<ReferralRecord> ReferralsAsReferrer, ReferralRecord? ReceivedReferral)> GetReferralDataAsync(string userId)
        {
            var asReferrer = await _referrals
                .Find(r => r.ReferrerUserId == userId)
                .SortByDescending(r => r.CreatedAt)
                .ToListAsync();

            var asReferred = await _referrals
                .Find(r => r.ReferredUserId == userId)
                .FirstOrDefaultAsync();

            return (asReferrer, asReferred);
        }
    }
}
