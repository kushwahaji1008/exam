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

        public IMongoCollection<Question> Questions => 
            _database.GetCollection<Question>("questions");

        public IMongoCollection<QuestionVersion> QuestionVersions => 
            _database.GetCollection<QuestionVersion>("question_versions");

        public IMongoCollection<Category> Categories => 
            _database.GetCollection<Category>("categories");

        public IMongoCollection<Subject> Subjects => 
            _database.GetCollection<Subject>("subjects");

        public IMongoCollection<Topic> Topics => 
            _database.GetCollection<Topic>("topics");

        public IMongoCollection<Difficulty> Difficulties => 
            _database.GetCollection<Difficulty>("difficulties");

        // Explicit namespace for Tag to avoid collision with MongoDB.Driver.Tag
        public IMongoCollection<QuestionBankService.Models.Tag> Tags => 
            _database.GetCollection<QuestionBankService.Models.Tag>("tags");
    }
}