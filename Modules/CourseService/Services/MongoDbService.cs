using MongoDB.Driver;
using CourseService.Models.V1;

namespace CourseService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration)
        {
            // Fetch connection details from appsettings.json, with fallbacks
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:CourseDatabase"] ?? "course_db"; // Dedicated DB for Courses

            var client = new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);
        }

        // ==========================================
        // COLLECTIONS
        // ==========================================
        
        public IMongoCollection<Course> Courses => _database.GetCollection<Course>("courses");
        
        // As you expand your Course Service, you might add more collections here in the future:
        // public IMongoCollection<CourseEnrollment> Enrollments => _database.GetCollection<CourseEnrollment>("enrollments");
        // public IMongoCollection<CourseReview> Reviews => _database.GetCollection<CourseReview>("reviews");
    }
}