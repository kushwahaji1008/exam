using MongoDB.Driver;
using NotificationService.Models;

namespace NotificationService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:NotificationDatabase"] ?? "exam_notifications_db";

            var client = new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);
        }

        public IMongoCollection<Notification> Notifications => _database.GetCollection<Notification>("notifications");
        public IMongoCollection<NotificationTemplate> Templates => _database.GetCollection<NotificationTemplate>("templates");
        public IMongoCollection<UserNotificationPreferences> Preferences => _database.GetCollection<UserNotificationPreferences>("preferences");
        
        // Access to Users database for bulk sending
        public IMongoDatabase UsersDatabase
        {
            get
            {
                var connectionString = _database.Client.Settings.ToString();
                var client = new MongoClient(connectionString);
                return client.GetDatabase("exam_auth_db");
            }
        }
    }
}
