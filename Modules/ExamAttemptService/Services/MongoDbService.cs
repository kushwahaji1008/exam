using MongoDB.Driver;
using ExamAttemptService.Models;

namespace ExamAttemptService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration, IMongoClient? mongoClient = null)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:ExamAttemptDatabase"] ?? "exam_attempts_db";

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
            await TryCreateIndexAsync(ExamAttempts, new CreateIndexModel<ExamAttempt>(
                Builders<ExamAttempt>.IndexKeys.Ascending(a => a.ExamId).Ascending(a => a.StudentId).Ascending(a => a.Status),
                new CreateIndexOptions { Name = "idx_attempts_exam_student_status" }));

            await TryCreateIndexAsync(ExamAttempts, new CreateIndexModel<ExamAttempt>(
                Builders<ExamAttempt>.IndexKeys.Ascending(a => a.StudentId).Descending(a => a.StartedAt),
                new CreateIndexOptions { Name = "idx_attempts_studentId" }));
        }

        public IMongoCollection<ExamAttempt> ExamAttempts => _database.GetCollection<ExamAttempt>("exam_attempts");
    }
}
