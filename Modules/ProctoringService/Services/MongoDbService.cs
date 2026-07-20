using MongoDB.Driver;
using ProctoringService.Models;

namespace ProctoringService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:ProctoringDatabase"] ?? "exam_proctoring_db";

            var client = new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);
        }

        public IMongoCollection<ProctoringSession> ProctoringSessions => _database.GetCollection<ProctoringSession>("proctoring_sessions");
    }
}
