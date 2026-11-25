using MongoDB.Driver;
using ResultService.Models;

namespace ResultService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration, IMongoClient? mongoClient = null)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:ResultDatabase"] ?? "exam_results_db";

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
            await TryCreateIndexAsync(Results, new CreateIndexModel<ExamResult>(
                Builders<ExamResult>.IndexKeys.Ascending(r => r.UserId),
                new CreateIndexOptions { Name = "idx_results_userId" }));

            await TryCreateIndexAsync(Results, new CreateIndexModel<ExamResult>(
                Builders<ExamResult>.IndexKeys.Ascending(r => r.ExamId),
                new CreateIndexOptions { Name = "idx_results_examId" }));

            await TryCreateIndexAsync(Results, new CreateIndexModel<ExamResult>(
                Builders<ExamResult>.IndexKeys.Ascending(r => r.AttemptId),
                new CreateIndexOptions { Unique = true, Name = "idx_results_attemptId" }));

            await TryCreateIndexAsync(Certificates, new CreateIndexModel<Certificate>(
                Builders<Certificate>.IndexKeys.Ascending(c => c.ResultId),
                new CreateIndexOptions { Name = "idx_certificates_resultId" }));

            await TryCreateIndexAsync(Certificates, new CreateIndexModel<Certificate>(
                Builders<Certificate>.IndexKeys.Ascending(c => c.VerificationCode),
                new CreateIndexOptions { Name = "idx_certificates_code" }));

            await TryCreateIndexAsync(Certificates, new CreateIndexModel<Certificate>(
                Builders<Certificate>.IndexKeys.Ascending(c => c.UserId),
                new CreateIndexOptions { Name = "idx_certificates_userId" }));
        }

        public IMongoCollection<ExamResult> Results => _database.GetCollection<ExamResult>("results");
        public IMongoCollection<Certificate> Certificates => _database.GetCollection<Certificate>("certificates");
    }
}
