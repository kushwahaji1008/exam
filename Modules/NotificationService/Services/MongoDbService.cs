using MongoDB.Driver;
using NotificationService.Models;

namespace NotificationService.Services
{
    public class MongoDbService
    {
        private readonly IMongoClient _client;
        private readonly IMongoDatabase _database;
        private readonly string _authDbName;

        public MongoDbService(IConfiguration configuration, IMongoClient? mongoClient = null)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:NotificationDatabase"] ?? "exam_notifications_db";
            _authDbName = configuration["MongoDb:AuthDatabase"] ?? "exam_auth_db";

            _client = mongoClient ?? new MongoClient(connectionString);
            _database = _client.GetDatabase(databaseName);

            EnsureIndexesAsync().GetAwaiter().GetResult();
        }

        private static async Task TryCreateIndexAsync<T>(IMongoCollection<T> collection, CreateIndexModel<T> model)
        {
            try { await collection.Indexes.CreateOneAsync(model); }
            catch (MongoCommandException ex) when (ex.Code is 85 or 86 or 11000) { }
        }

        private async Task EnsureIndexesAsync()
        {
            await TryCreateIndexAsync(Notifications, new CreateIndexModel<Notification>(
                Builders<Notification>.IndexKeys.Ascending(n => n.UserId).Descending(n => n.CreatedAt),
                new CreateIndexOptions { Name = "idx_notifications_user_ts" }));

            await TryCreateIndexAsync(Notifications, new CreateIndexModel<Notification>(
                Builders<Notification>.IndexKeys.Ascending(n => n.UserId).Ascending(n => n.IsRead),
                new CreateIndexOptions { Name = "idx_notifications_user_unread" }));

            await TryCreateIndexAsync(Preferences, new CreateIndexModel<UserNotificationPreferences>(
                Builders<UserNotificationPreferences>.IndexKeys.Ascending(p => p.UserId),
                new CreateIndexOptions { Name = "idx_notif_prefs_userId" }));
        }

        public IMongoCollection<Notification> Notifications => _database.GetCollection<Notification>("notifications");
        public IMongoCollection<NotificationTemplate> Templates => _database.GetCollection<NotificationTemplate>("templates");
        public IMongoCollection<UserNotificationPreferences> Preferences => _database.GetCollection<UserNotificationPreferences>("preferences");

        public IMongoDatabase UsersDatabase => _client.GetDatabase(_authDbName);
    }
}
