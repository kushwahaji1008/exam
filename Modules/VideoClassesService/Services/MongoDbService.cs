using MongoDB.Driver;
using VideoClassesService.Models;

namespace VideoClassesService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:VideoClassesDatabase"] ?? "exam_videos_db";

            var client = new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);
        }

        public IMongoCollection<Course> Courses => _database.GetCollection<Course>("courses");
        public IMongoCollection<Chapter> Chapters => _database.GetCollection<Chapter>("chapters");
        public IMongoCollection<Lesson> Lessons => _database.GetCollection<Lesson>("lessons");
        public IMongoCollection<StudentProgress> StudentProgress => _database.GetCollection<StudentProgress>("student_progress");
        public IMongoCollection<VideoComment> Comments => _database.GetCollection<VideoComment>("comments");
        public IMongoCollection<LiveClass> LiveClasses => _database.GetCollection<LiveClass>("live_classes");
    }
}
