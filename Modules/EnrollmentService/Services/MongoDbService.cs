using MongoDB.Driver;
using EnrollmentService.Models;

namespace EnrollmentService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration)
        {
            // Fetch connection details from appsettings.json, with fallbacks
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:EnrollmentDatabase"] ?? "enrollment_db"; // Dedicated DB for Enrollments

            var client = new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);
        }

        // ==========================================
        // COLLECTIONS
        // ==========================================
        
        public IMongoCollection<Enrollment> Enrollments => _database.GetCollection<Enrollment>("enrollments");
    }
}