using MongoDB.Driver;
using EnrollmentService.Models;

namespace EnrollmentService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration, IMongoClient? mongoClient = null)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:EnrollmentDatabase"] ?? "enrollment_db";

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
            await TryCreateIndexAsync(Enrollments, new CreateIndexModel<Enrollment>(
                Builders<Enrollment>.IndexKeys.Ascending(e => e.UserId).Ascending(e => e.CourseId),
                new CreateIndexOptions { Name = "idx_enrollments_user_course" }));

            await TryCreateIndexAsync(Enrollments, new CreateIndexModel<Enrollment>(
                Builders<Enrollment>.IndexKeys.Ascending(e => e.CourseId).Ascending(e => e.Status),
                new CreateIndexOptions { Name = "idx_enrollments_course_status" }));

            await TryCreateIndexAsync(ExamEnrollments, new CreateIndexModel<ExamEnrollment>(
                Builders<ExamEnrollment>.IndexKeys.Ascending(e => e.UserId).Ascending(e => e.ExamId),
                new CreateIndexOptions { Name = "idx_exam_enrollments_user_exam" }));

            await TryCreateIndexAsync(ExamSeriesEnrollments, new CreateIndexModel<ExamSeriesEnrollment>(
                Builders<ExamSeriesEnrollment>.IndexKeys.Ascending(e => e.UserId).Ascending(e => e.SeriesId),
                new CreateIndexOptions { Name = "idx_series_enrollments_user_series" }));
        }

        public IMongoCollection<Enrollment> Enrollments => _database.GetCollection<Enrollment>("enrollments");
        public IMongoCollection<ExamEnrollment> ExamEnrollments => _database.GetCollection<ExamEnrollment>("exam_enrollments");
        public IMongoCollection<ExamSeriesEnrollment> ExamSeriesEnrollments => _database.GetCollection<ExamSeriesEnrollment>("exam_series_enrollments");
    }
}
