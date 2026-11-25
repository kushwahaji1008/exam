using MongoDB.Driver;
using VideoClassesService.Models;

namespace VideoClassesService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration, IMongoClient? mongoClient = null)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:VideoClassesDatabase"] ?? "exam_videos_db";

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
            await TryCreateIndexAsync(Chapters, new CreateIndexModel<Chapter>(
                Builders<Chapter>.IndexKeys.Ascending(c => c.CourseId).Ascending(c => c.OrderIndex),
                new CreateIndexOptions { Name = "idx_vc_chapters_course" }));

            await TryCreateIndexAsync(Lessons, new CreateIndexModel<Lesson>(
                Builders<Lesson>.IndexKeys.Ascending(l => l.ChapterId).Ascending(l => l.OrderIndex),
                new CreateIndexOptions { Name = "idx_vc_lessons_chapter" }));

            await TryCreateIndexAsync(StudentProgress, new CreateIndexModel<StudentProgress>(
                Builders<StudentProgress>.IndexKeys.Ascending(p => p.StudentId).Ascending(p => p.CourseId),
                new CreateIndexOptions { Name = "idx_vc_progress_student_course" }));

            await TryCreateIndexAsync(Comments, new CreateIndexModel<VideoComment>(
                Builders<VideoComment>.IndexKeys.Ascending(c => c.LessonId).Descending(c => c.CreatedAt),
                new CreateIndexOptions { Name = "idx_vc_comments_lesson" }));

            await TryCreateIndexAsync(LiveClasses, new CreateIndexModel<LiveClass>(
                Builders<LiveClass>.IndexKeys.Ascending(lc => lc.Status).Descending(lc => lc.ScheduledStartTime),
                new CreateIndexOptions { Name = "idx_vc_liveclasses_status_schedule" }));
        }

        public IMongoCollection<Course> Courses => _database.GetCollection<Course>("courses");
        public IMongoCollection<Chapter> Chapters => _database.GetCollection<Chapter>("chapters");
        public IMongoCollection<Lesson> Lessons => _database.GetCollection<Lesson>("lessons");
        public IMongoCollection<StudentProgress> StudentProgress => _database.GetCollection<StudentProgress>("student_progress");
        public IMongoCollection<VideoComment> Comments => _database.GetCollection<VideoComment>("comments");
        public IMongoCollection<LiveClass> LiveClasses => _database.GetCollection<LiveClass>("live_classes");
    }
}
