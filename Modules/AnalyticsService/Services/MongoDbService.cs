using MongoDB.Bson;
using MongoDB.Driver;
using AnalyticsService.Models;

namespace AnalyticsService.Services
{
    public class MongoDbService
    {
        private readonly IMongoClient _client;
        private readonly IMongoDatabase _database;
        private readonly IConfiguration _configuration;

        public MongoDbService(IConfiguration configuration, IMongoClient? mongoClient = null)
        {
            _configuration = configuration;
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:AnalyticsDatabase"] ?? "analytics_db";

            _client = mongoClient ?? new MongoClient(connectionString);
            _database = _client.GetDatabase(databaseName);

            EnsureIndexesAsync().GetAwaiter().GetResult();
        }

        private static async Task TryCreateIndexAsync<T>(IMongoCollection<T> collection, CreateIndexModel<T> model)
        {
            try { await collection.Indexes.CreateOneAsync(model); }
            catch (MongoCommandException ex) when (ex.Code is 85 or 86 or 11000) { }
        }

        private async Task EnsureIndexesAsync()
        {
            await TryCreateIndexAsync(Reports, new CreateIndexModel<AnalyticsReport>(
                Builders<AnalyticsReport>.IndexKeys.Ascending(r => r.RequestedBy).Descending(r => r.CreatedAt),
                new CreateIndexOptions { Name = "idx_analytics_reports_user_ts" }));
        }

        public IMongoCollection<AnalyticsReport> Reports => _database.GetCollection<AnalyticsReport>("reports");

        public IMongoDatabase GetModuleDatabase(string configKey, string defaultDbName) =>
            _client.GetDatabase(_configuration[configKey] ?? defaultDbName);

        public IMongoCollection<BsonDocument> AuthUsers => GetModuleDatabase("MongoDb:AuthDatabase", "exam_auth_db").GetCollection<BsonDocument>("users");
        public IMongoCollection<BsonDocument> Exams => GetModuleDatabase("MongoDb:ExamDatabase", "exam_exams_db").GetCollection<BsonDocument>("exams");
        public IMongoCollection<BsonDocument> Attempts => GetModuleDatabase("MongoDb:ExamAttemptDatabase", "exam_attempts_db").GetCollection<BsonDocument>("exam_attempts");
        public IMongoCollection<BsonDocument> Results => GetModuleDatabase("MongoDb:ResultDatabase", "exam_results_db").GetCollection<BsonDocument>("results");
        public IMongoCollection<BsonDocument> Questions => GetModuleDatabase("MongoDb:QuestionBankDatabase", "exam_questions_db").GetCollection<BsonDocument>("questions");
        public IMongoCollection<BsonDocument> Courses => GetModuleDatabase("MongoDb:CourseDatabase", "course_db").GetCollection<BsonDocument>("courses");
        public IMongoCollection<BsonDocument> Enrollments => GetModuleDatabase("MongoDb:EnrollmentDatabase", "enrollment_db").GetCollection<BsonDocument>("enrollments");
        public IMongoCollection<BsonDocument> WalletTransactions => GetModuleDatabase("MongoDb:WalletDatabase", "wallet_db").GetCollection<BsonDocument>("wallet_transactions");
    }
}
