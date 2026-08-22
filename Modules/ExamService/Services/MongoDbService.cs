using MongoDB.Driver;
using ExamService.Models;

namespace ExamService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:ExamDatabase"] ?? "exam_db"; // Separate DB for Exams

            var client = new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);
        }

        public IMongoCollection<Exam> Exams => _database.GetCollection<Exam>("exams");
        
        // Eventually you will also add:
        // public IMongoCollection<ExamAttempt> ExamAttempts => _database.GetCollection<ExamAttempt>("exam_attempts");
    }
}