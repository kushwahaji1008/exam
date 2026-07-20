using MongoDB.Driver;
using QuestionBankService.Models;

namespace QuestionBankService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:QuestionBankDatabase"] ?? "exam_questions_db";

            var client = new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);
        }

        public IMongoCollection<Question> Questions => _database.GetCollection<Question>("questions");
    }
}
