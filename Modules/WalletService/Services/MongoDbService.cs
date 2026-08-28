using MongoDB.Driver;
using WalletService.Models;

namespace WalletService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration)
        {
            // Fetch connection details from appsettings.json, with fallbacks
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:WalletDatabase"] ?? "wallet_db"; // Dedicated DB for Wallet

            var client = new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);
        }

        // ==========================================
        // COLLECTIONS
        // ==========================================
        
        public IMongoCollection<Wallet> Wallets => _database.GetCollection<Wallet>("wallets");
        public IMongoCollection<WalletTransaction> WalletTransactions => _database.GetCollection<WalletTransaction>("wallet_transactions");
    }
}