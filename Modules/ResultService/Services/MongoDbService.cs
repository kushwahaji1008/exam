using MongoDB.Driver;

namespace ResultService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:ResultDatabase"] ?? "exam_results_db";

            var client = new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);
        }

        // We'll query the attempts database for evaluation
        public IMongoDatabase AttemptsDatabase
        {
            get
            {
                var connectionString = _database.Client.Settings.ToString();
                var client = new MongoClient(connectionString);
                return client.GetDatabase("exam_attempts_db");
            }
        }

        public IMongoDatabase QuestionsDatabase
        {
            get
            {
                var connectionString = _database.Client.Settings.ToString();
                var client = new MongoClient(connectionString);
                return client.GetDatabase("exam_questions_db");
            }
        }

        public IMongoDatabase ExamsDatabase
        {
            get
            {
                var connectionString = _database.Client.Settings.ToString();
                var client = new MongoClient(connectionString);
                return client.GetDatabase("exam_exams_db");
            }
        }
    }
}
