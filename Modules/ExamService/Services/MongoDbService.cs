using MongoDB.Driver;
using ExamService.Models;

namespace ExamService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration, IMongoClient? mongoClient = null)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:ExamDatabase"] ?? "exam_exams_db";

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
            await TryCreateIndexAsync(Exams, new CreateIndexModel<Exam>(
                Builders<Exam>.IndexKeys.Ascending(e => e.ExamId),
                new CreateIndexOptions { Name = "idx_exams_examId" }));

            await TryCreateIndexAsync(Exams, new CreateIndexModel<Exam>(
                Builders<Exam>.IndexKeys.Ascending(e => e.CreatedBy).Descending(e => e.CreatedAt),
                new CreateIndexOptions { Name = "idx_exams_createdBy" }));

            await TryCreateIndexAsync(Exams, new CreateIndexModel<Exam>(
                Builders<Exam>.IndexKeys.Ascending(e => e.Status),
                new CreateIndexOptions { Name = "idx_exams_status" }));

            await TryCreateIndexAsync(ExamSeries, new CreateIndexModel<ExamSeries>(
                Builders<ExamSeries>.IndexKeys.Ascending(s => s.SeriesId),
                new CreateIndexOptions { Name = "idx_exam_series_id" }));
        }

        public IMongoCollection<Exam> Exams => _database.GetCollection<Exam>("exams");
        public IMongoCollection<ExamSeries> ExamSeries => _database.GetCollection<ExamSeries>("exam_series");
    }
}
