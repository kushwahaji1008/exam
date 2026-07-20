using MongoDB.Driver;
using ExamAttemptService.Models;

namespace ExamAttemptService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:ExamAttemptDatabase"] ?? "exam_attempts_db";

            var client = new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);
        }

        public IMongoCollection<ExamAttempt> ExamAttempts => _database.GetCollection<ExamAttempt>("exam_attempts");
    }
}
