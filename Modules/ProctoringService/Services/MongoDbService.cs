using MongoDB.Driver;
using ProctoringService.Models;

namespace ProctoringService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration, IMongoClient? mongoClient = null)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:ProctoringDatabase"] ?? "exam_proctoring_db";

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
            await TryCreateIndexAsync(ProctoringSessions, new CreateIndexModel<ProctoringSession>(
                Builders<ProctoringSession>.IndexKeys.Ascending(s => s.AttemptId).Ascending(s => s.Status),
                new CreateIndexOptions { Name = "idx_proctoring_attemptId_status" }));

            await TryCreateIndexAsync(ProctoringSessions, new CreateIndexModel<ProctoringSession>(
                Builders<ProctoringSession>.IndexKeys.Ascending(s => s.ExamId),
                new CreateIndexOptions { Name = "idx_proctoring_examId" }));

            await TryCreateIndexAsync(ProctoringSessions, new CreateIndexModel<ProctoringSession>(
                Builders<ProctoringSession>.IndexKeys.Ascending(s => s.StudentId),
                new CreateIndexOptions { Name = "idx_proctoring_studentId" }));
        }

        public IMongoCollection<ProctoringSession> ProctoringSessions => _database.GetCollection<ProctoringSession>("proctoring_sessions");
    }
}
