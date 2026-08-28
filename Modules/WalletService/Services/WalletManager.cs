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
            
            // Create Index for Idempotency to make checks super fast
            var indexOptions = new CreateIndexOptions { Unique = true };
            var indexKeys = Builders<WalletTransaction>.IndexKeys.Ascending(t => t.IdempotencyKey);
            var indexModel = new CreateIndexModel<WalletTransaction>(indexKeys, indexOptions);
            _transactions.Indexes.CreateOne(indexModel);
        }

        public async Task<Wallet> GetBalanceAsync(string userId)
        {
            var wallet = await _wallets.Find(w => w.UserId == userId).FirstOrDefaultAsync();
            if (wallet == null)
            {
                wallet = new Wallet { UserId = userId, Balance = 0 };
                await _wallets.InsertOneAsync(wallet);
            }
            return wallet;
        }

        // ==========================================
        // DEBIT LOGIC (Strict Security + Atomic)
        // ==========================================
        public async Task<(bool Success, string Message)> DebitAsync(string userId, decimal amount, string idempotencyKey, string referenceId, string sourceService)
        {
            // 1. CHECK IDEMPOTENCY (Agar pehle hi transaction ho chuki hai, toh return success)
            var existingTxn = await _transactions.Find(t => t.IdempotencyKey == idempotencyKey).FirstOrDefaultAsync();
            if (existingTxn != null) return (true, "Transaction already processed successfully.");

            // 2. ATOMIC DEBIT (Balance check and deduct in a single database hit)
            var filter = Builders<Wallet>.Filter.And(
                Builders<Wallet>.Filter.Eq(w => w.UserId, userId),
                Builders<Wallet>.Filter.Gte(w => w.Balance, amount) // Must have enough balance!
            );

            var update = Builders<Wallet>.Update
                .Inc(w => w.Balance, -amount)
                .Set(w => w.UpdatedAt, DateTime.UtcNow);

            var result = await _wallets.FindOneAndUpdateAsync(filter, update);

            // Agar result null hai matlab filter fail hua (ya toh user nahi hai ya balance kam hai)
            if (result == null)
            {
                // Check if wallet exists at all
                var exists = await _wallets.Find(w => w.UserId == userId).AnyAsync();
                if (!exists) return (false, "Wallet not found.");

                return (false, "Insufficient balance.");
            }

            // 3. LOG TRANSACTION (Ledger Entry)
            var txn = new WalletTransaction
            {
                UserId = userId,
                IdempotencyKey = idempotencyKey,
                Amount = amount,
                Type = "DEBIT",
                ReferenceId = referenceId,
                SourceService = sourceService,
                Description = $"Paid for {referenceId} via {sourceService}"
            };
            await _transactions.InsertOneAsync(txn);

            return (true, "Payment successful.");
        }

        // ==========================================
        // CREDIT LOGIC (Add coins securely)
        // ==========================================
        public async Task<(bool Success, string Message)> CreditAsync(string userId, decimal amount, string idempotencyKey, string referenceId, string description)
        {
            var existingTxn = await _transactions.Find(t => t.IdempotencyKey == idempotencyKey).FirstOrDefaultAsync();
            if (existingTxn != null) return (true, "Transaction already processed.");

            await GetBalanceAsync(userId); // Ensure wallet exists

            var update = Builders<Wallet>.Update
                .Inc(w => w.Balance, amount)
                .Set(w => w.UpdatedAt, DateTime.UtcNow);

            await _wallets.UpdateOneAsync(w => w.UserId == userId, update);

            var txn = new WalletTransaction
            {
                UserId = userId,
                IdempotencyKey = idempotencyKey,
                Amount = amount,
                Type = "CREDIT",
                ReferenceId = referenceId,
                SourceService = "InternalSystem",
                Description = description
            };
            await _transactions.InsertOneAsync(txn);

            return (true, "Coins added successfully.");
        }
    }
}