using MongoDB.Driver;
using CourseService.Models.V1;

namespace CourseService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration, IMongoClient? mongoClient = null)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:CourseDatabase"] ?? "course_db";

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
            await TryCreateIndexAsync(Courses, new CreateIndexModel<Course>(
                Builders<Course>.IndexKeys.Ascending(c => c.CourseId),
                new CreateIndexOptions { Unique = true, Name = "idx_courses_courseId" }));

            await TryCreateIndexAsync(Courses, new CreateIndexModel<Course>(
                Builders<Course>.IndexKeys.Ascending(c => c.Status).Descending(c => c.EnrollmentCount),
                new CreateIndexOptions { Name = "idx_courses_status_popular" }));

            await TryCreateIndexAsync(Courses, new CreateIndexModel<Course>(
                Builders<Course>.IndexKeys.Ascending(c => c.CreatedBy),
                new CreateIndexOptions { Name = "idx_courses_createdBy" }));
        }

        public IMongoCollection<Course> Courses => _database.GetCollection<Course>("courses");
    }
}
