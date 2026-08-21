using MongoDB.Driver;
using AuthService.Models;

namespace AuthService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:AuthDatabase"] ?? "exam_auth_db";

            var client = new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);
        }

        // Core Authentication & Users
        public IMongoCollection<User> Users => _database.GetCollection<User>("users");
        
        // Roles & Permissions Mapping
        public IMongoCollection<Role> Roles => _database.GetCollection<Role>("roles");
        public IMongoCollection<Permission> Permissions => _database.GetCollection<Permission>("permissions");
        
        // Audit & Security Logging
        public IMongoCollection<AuditLog> AuditLogs => _database.GetCollection<AuditLog>("audit_logs");
        public IMongoCollection<SecurityEvent> SecurityEvents => _database.GetCollection<SecurityEvent>("security_events");
    }
}