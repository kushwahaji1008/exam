using MongoDB.Driver;
using WalletService.Models;

namespace WalletService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration, IMongoClient? mongoClient = null)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:WalletDatabase"] ?? "wallet_db";

            var client = mongoClient ?? new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);

            EnsureIndexesAsync().GetAwaiter().GetResult();
        }

        private static async Task TryCreateIndexAsync<T>(IMongoCollection<T> collection, CreateIndexModel<T> model)
        {
            try { await collection.Indexes.CreateOneAsync(model); }
            catch (MongoCommandException ex) when (ex.Code is 85 or 86 or 11000) { }
        }

        private async Task EnsureIndexesAsync()
        {
            await TryCreateIndexAsync(Wallets, new CreateIndexModel<Wallet>(
                Builders<Wallet>.IndexKeys.Ascending(w => w.UserId),
                new CreateIndexOptions { Name = "idx_wallets_userId" }));

            await TryCreateIndexAsync(WalletTransactions, new CreateIndexModel<WalletTransaction>(
                Builders<WalletTransaction>.IndexKeys.Ascending(t => t.IdempotencyKey),
                new CreateIndexOptions { Unique = true, Name = "idx_wallet_txns_idempotency" }));

            await TryCreateIndexAsync(WalletTransactions, new CreateIndexModel<WalletTransaction>(
                Builders<WalletTransaction>.IndexKeys.Ascending(t => t.UserId).Descending(t => t.Timestamp),
                new CreateIndexOptions { Name = "idx_wallet_txns_user_ts" }));

            await TryCreateIndexAsync(Referrals, new CreateIndexModel<ReferralRecord>(
                Builders<ReferralRecord>.IndexKeys.Ascending(r => r.ReferredUserId),
                new CreateIndexOptions { Unique = true, Name = "idx_referrals_referred_unique" }));

            await TryCreateIndexAsync(Referrals, new CreateIndexModel<ReferralRecord>(
                Builders<ReferralRecord>.IndexKeys.Ascending(r => r.ReferrerUserId).Descending(r => r.CreatedAt),
                new CreateIndexOptions { Name = "idx_referrals_referrer" }));
        }

        public IMongoCollection<Wallet> Wallets => _database.GetCollection<Wallet>("wallets");
        public IMongoCollection<WalletTransaction> WalletTransactions => _database.GetCollection<WalletTransaction>("wallet_transactions");
        public IMongoCollection<ReferralRecord> Referrals => _database.GetCollection<ReferralRecord>("referrals");
    }
}
