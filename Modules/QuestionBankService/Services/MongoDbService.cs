using MongoDB.Driver;
using QuestionBankService.Models;

namespace QuestionBankService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration, IMongoClient? mongoClient = null)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:QuestionBankDatabase"] ?? "exam_questions_db";

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
            await TryCreateIndexAsync(Questions, new CreateIndexModel<Question>(
                Builders<Question>.IndexKeys.Ascending(q => q.SubjectId).Ascending(q => q.TopicId).Ascending(q => q.DifficultyId),
                new CreateIndexOptions { Name = "idx_questions_subject_topic_difficulty" }));

            await TryCreateIndexAsync(Questions, new CreateIndexModel<Question>(
                Builders<Question>.IndexKeys.Ascending(q => q.IsActive).Ascending(q => q.Status),
                new CreateIndexOptions { Name = "idx_questions_active_status" }));

            await TryCreateIndexAsync(Questions, new CreateIndexModel<Question>(
                Builders<Question>.IndexKeys.Text(q => q.QuestionText),
                new CreateIndexOptions { Name = "idx_questions_text_search" }));

            await TryCreateIndexAsync(QuestionVersions, new CreateIndexModel<QuestionVersion>(
                Builders<QuestionVersion>.IndexKeys.Ascending(v => v.OriginalQuestionId).Descending(v => v.VersionNumber),
                new CreateIndexOptions { Name = "idx_question_versions_orig" }));
        }

        public IMongoCollection<Question> Questions => _database.GetCollection<Question>("questions");
        public IMongoCollection<QuestionVersion> QuestionVersions => _database.GetCollection<QuestionVersion>("question_versions");
        public IMongoCollection<Category> Categories => _database.GetCollection<Category>("categories");
        public IMongoCollection<Subject> Subjects => _database.GetCollection<Subject>("subjects");
        public IMongoCollection<Topic> Topics => _database.GetCollection<Topic>("topics");
        public IMongoCollection<Difficulty> Difficulties => _database.GetCollection<Difficulty>("difficulties");
        public IMongoCollection<QuestionBankService.Models.Tag> Tags => _database.GetCollection<QuestionBankService.Models.Tag>("tags");
    }
}
