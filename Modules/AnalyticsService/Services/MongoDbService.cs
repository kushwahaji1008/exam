using MongoDB.Driver;
using AnalyticsService.Models;

namespace AnalyticsService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:AnalyticsDatabase"] ?? "analytics_db";

            var client = new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);
        }

        public IMongoCollection<AnalyticsReport> Reports => _database.GetCollection<AnalyticsReport>("reports");
    }
}