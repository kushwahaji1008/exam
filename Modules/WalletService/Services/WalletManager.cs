using MongoDB.Driver;
using WalletService.Models;

namespace WalletService.Services
{
    public class WalletManager
    {
        private readonly IMongoCollection<Wallet> _wallets;
        private readonly IMongoCollection<WalletTransaction> _transactions;

        public WalletManager(MongoDbService mongoDb)
        {
            _wallets = mongoDb.Wallets;
            _transactions = mongoDb.WalletTransactions;
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

        public async Task<(List<WalletTransaction> Items, long Total)> GetTransactionsAsync(string userId, int page = 1, int pageSize = 20)
        {
            var safePage = Math.Max(1, page);
            var safeSize = Math.Clamp(pageSize, 1, 100);
            var filter = Builders<WalletTransaction>.Filter.Eq(t => t.UserId, userId);

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
        public async Task<(bool Success, string Message)> DebitAsync(string userId, decimal amount, string idempotencyKey, string referenceId, string sourceService)
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
                Description = $"Paid for {referenceId} via {sourceService}",
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
                return exists ? (false, "Insufficient balance.") : (false, "Wallet not found.");
            }

            return (true, "Payment successful.");
        }

        // ==========================================
        // CREDIT LOGIC (Validation + Idempotency-First + Atomic)
        // ==========================================
        public async Task<(bool Success, string Message)> CreditAsync(string userId, decimal amount, string idempotencyKey, string referenceId, string description)
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
                SourceService = "InternalSystem",
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
    }
}
