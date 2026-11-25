using MongoDB.Driver;
using AuthService.Models;

namespace AuthService.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;

        public MongoDbService(IConfiguration configuration, IMongoClient? mongoClient = null)
        {
            var connectionString = configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
            var databaseName = configuration["MongoDb:AuthDatabase"] ?? "exam_auth_db";

            var client = mongoClient ?? new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);

            // FIX: Create indexes at startup to prevent full-collection scans
            EnsureIndexesAsync().GetAwaiter().GetResult();
        }

        private static async Task TryCreateIndexAsync<T>(IMongoCollection<T> collection, CreateIndexModel<T> model)
        {
            try { await collection.Indexes.CreateOneAsync(model); }
            catch (MongoCommandException ex) when (ex.Code is 85 or 86 or 11000) { }
        }

        private async Task EnsureIndexesAsync()
        {
            await TryCreateIndexAsync(Users, new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(u => u.Email),
                new CreateIndexOptions { Unique = true, Name = "idx_users_email" }));

            await TryCreateIndexAsync(Users, new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(u => u.UserId),
                new CreateIndexOptions { Unique = true, Name = "idx_users_userId" }));

            await TryCreateIndexAsync(Users, new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(u => u.IsDeleted).Ascending(u => u.IsActive),
                new CreateIndexOptions { Name = "idx_users_active" }));

            await TryCreateIndexAsync(Users, new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(u => u.Phone),
                new CreateIndexOptions { Sparse = true, Name = "idx_users_phone" }));

            await TryCreateIndexAsync(Roles, new CreateIndexModel<Role>(
                Builders<Role>.IndexKeys.Ascending(r => r.Name),
                new CreateIndexOptions { Name = "idx_roles_name" }));

            await TryCreateIndexAsync(Permissions, new CreateIndexModel<Permission>(
                Builders<Permission>.IndexKeys.Ascending(p => p.Name),
                new CreateIndexOptions { Name = "idx_permissions_name" }));

            await TryCreateIndexAsync(AuditLogs, new CreateIndexModel<AuditLog>(
                Builders<AuditLog>.IndexKeys.Ascending(a => a.UserId).Descending(a => a.Timestamp),
                new CreateIndexOptions { Name = "idx_audit_user_ts" }));

            await TryCreateIndexAsync(SecurityEvents, new CreateIndexModel<SecurityEvent>(
                Builders<SecurityEvent>.IndexKeys.Ascending(s => s.UserId).Descending(s => s.Timestamp),
                new CreateIndexOptions { Name = "idx_security_user_ts" }));
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