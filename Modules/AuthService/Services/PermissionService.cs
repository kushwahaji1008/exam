using MongoDB.Driver;
using AuthService.Models;

namespace AuthService.Services
{
    public class PermissionService
    {
        private readonly MongoDbService _mongoDb;

        public PermissionService(MongoDbService mongoDb)
        {
            _mongoDb = mongoDb;
        }

        // ==========================================
        // PERMISSION CRUD OPERATIONS
        // ==========================================

        public async Task<List<Permission>> GetAllPermissionsAsync()
        {
            return await _mongoDb.Permissions.Find(_ => true).ToListAsync();
        }

        public async Task<(bool Success, string Message, string? PermissionId)> CreatePermissionAsync(CreatePermissionRequest request)
        {
            // Check if permission name already exists
            var exists = await _mongoDb.Permissions.Find(p => p.Name.ToLower() == request.Name.ToLower()).AnyAsync();
            if (exists) return (false, "A permission with this name already exists.", null);

            var permission = new Permission
            {
                Name = request.Name,
                Description = request.Description
            };

            await _mongoDb.Permissions.InsertOneAsync(permission);
            return (true, "Permission created successfully.", permission.Id);
        }

        public async Task<Permission?> GetPermissionByIdAsync(string permissionId)
        {
            return await _mongoDb.Permissions.Find(p => p.Id == permissionId).FirstOrDefaultAsync();
        }

        public async Task<bool> UpdatePermissionAsync(string permissionId, UpdatePermissionRequest request)
        {
            // Check if another permission is already using this name
            var existingPermission = await _mongoDb.Permissions.Find(p => p.Name.ToLower() == request.Name.ToLower() && p.Id != permissionId).AnyAsync();
            if (existingPermission) return false; // Name conflict

            var update = Builders<Permission>.Update
                .Set(p => p.Name, request.Name)
                .Set(p => p.Description, request.Description);

            var result = await _mongoDb.Permissions.UpdateOneAsync(p => p.Id == permissionId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> DeletePermissionAsync(string permissionId)
        {
            // Safety Check: Prevent deletion if this permission is currently assigned to any Role
            var isPermissionInUse = await _mongoDb.Roles.Find(r => r.PermissionIds.Contains(permissionId)).AnyAsync();
            if (isPermissionInUse)
            {
                // Refuse to delete because it would break existing role constraints
                return false; 
            }

            var result = await _mongoDb.Permissions.DeleteOneAsync(p => p.Id == permissionId);
            return result.DeletedCount > 0;
        }
    }
}