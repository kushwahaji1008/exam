using MongoDB.Driver;
using MongoDB.Bson;
using AuthService.Models;
using System.Text.Json;

namespace AuthService.Services
{
    public class RoleService
    {
        private readonly MongoDbService _mongoDb;

        public RoleService(MongoDbService mongoDb)
        {
            _mongoDb = mongoDb;
        }

        // ==========================================
        // 1. CORE ROLE CRUD OPERATIONS
        // ==========================================

        public async Task<List<Role>> GetAllRolesAsync()
        {
            return await _mongoDb.Roles.Find(_ => true).ToListAsync();
        }

        public async Task<(bool Success, string Message, string? RoleId)> CreateRoleAsync(CreateRoleRequest request)
        {
            // Check if role name already exists
            var exists = await _mongoDb.Roles.Find(r => r.Name.ToLower() == request.Name.ToLower()).AnyAsync();
            if (exists) return (false, "A role with this name already exists.", null);

            var role = new Role
            {
                Name = request.Name,
                Description = request.Description,
                PermissionIds = new List<string>()
            };

            await _mongoDb.Roles.InsertOneAsync(role);
            return (true, "Role created successfully.", role.Id);
        }

        public async Task<Role?> GetRoleByIdAsync(string roleId)
        {
            return await _mongoDb.Roles.Find(r => r.Id == roleId).FirstOrDefaultAsync();
        }

        public async Task<bool> UpdateRoleAsync(string roleId, UpdateRoleRequest request)
        {
            // Check if another role is already using this name
            var existingRole = await _mongoDb.Roles.Find(r => r.Name.ToLower() == request.Name.ToLower() && r.Id != roleId).AnyAsync();
            if (existingRole) return false; // Name conflict

            var update = Builders<Role>.Update
                .Set(r => r.Name, request.Name)
                .Set(r => r.Description, request.Description);

            var result = await _mongoDb.Roles.UpdateOneAsync(r => r.Id == roleId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> PatchRoleAsync(string roleId, object request)
        {
            var role = await _mongoDb.Roles.Find(r => r.Id == roleId).FirstOrDefaultAsync();
            if (role == null) return false;

            try
            {
                var json = JsonSerializer.Serialize(request);
                var patchDict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                
                if (patchDict == null || !patchDict.Any()) return false;

                var updateBuilder = Builders<Role>.Update;
                var updates = new List<UpdateDefinition<Role>>();

                foreach (var kvp in patchDict)
                {
                    // Prevent modification of ID or Permissions via this endpoint
                    if (kvp.Key.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                        kvp.Key.Equals("PermissionIds", StringComparison.OrdinalIgnoreCase)) 
                        continue;

                    updates.Add(updateBuilder.Set(kvp.Key, kvp.Value));
                }

                if (updates.Any())
                {
                    var combinedUpdate = updateBuilder.Combine(updates);
                    var result = await _mongoDb.Roles.UpdateOneAsync(r => r.Id == roleId, combinedUpdate);
                    return result.ModifiedCount > 0;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeleteRoleAsync(string roleId)
        {
            // Optional Safety Check: Ensure no users currently hold this role before deleting
            var usersWithRole = await _mongoDb.Users.Find(u => u.AssignedRoleIds.Contains(roleId) && !u.IsDeleted).AnyAsync();
            if (usersWithRole)
            {
                // Refuse to delete if it's currently assigned to active users
                return false; 
            }

            var result = await _mongoDb.Roles.DeleteOneAsync(r => r.Id == roleId);
            return result.DeletedCount > 0;
        }

        // ==========================================
        // 2. USER-ROLE MAPPING
        // ==========================================

        public async Task<bool> AssignRoleToUserAsync(string roleId, string userId)
        {
            var roleExists = await _mongoDb.Roles.Find(r => r.Id == roleId).AnyAsync();
            if (!roleExists) return false;

            // AddToSet ensures we don't add duplicate role IDs to the array
            var update = Builders<User>.Update.AddToSet(u => u.AssignedRoleIds, roleId);
            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId && !u.IsDeleted, update);
            
            return result.ModifiedCount > 0;
        }

        public async Task<bool> RemoveRoleFromUserAsync(string roleId, string userId)
        {
            // Pull removes the specific roleId from the array
            var update = Builders<User>.Update.Pull(u => u.AssignedRoleIds, roleId);
            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId && !u.IsDeleted, update);
            
            return result.ModifiedCount > 0;
        }

        // ==========================================
        // 3. ROLE-PERMISSION MAPPING
        // ==========================================

        public async Task<List<Permission>?> GetRolePermissionsAsync(string roleId)
        {
            var role = await _mongoDb.Roles.Find(r => r.Id == roleId).FirstOrDefaultAsync();
            if (role == null) return null;

            if (role.PermissionIds == null || !role.PermissionIds.Any())
                return new List<Permission>();

            // Fetch the actual permission objects based on the IDs stored in the role
            var permissions = await _mongoDb.Permissions
                .Find(p => role.PermissionIds.Contains(p.Id))
                .ToListAsync();

            return permissions;
        }

        public async Task<bool> AssignPermissionsToRoleAsync(string roleId, List<string> permissionIds)
        {
            var roleExists = await _mongoDb.Roles.Find(r => r.Id == roleId).AnyAsync();
            if (!roleExists) return false;

            // Verify that the permission IDs actually exist in the DB (optional but recommended)
            var existingPermissions = await _mongoDb.Permissions
                .Find(p => permissionIds.Contains(p.Id))
                .Project(p => p.Id)
                .ToListAsync();

            if (!existingPermissions.Any()) return false;

            // AddToSetEach ensures no duplicates are added to the PermissionIds array
            var update = Builders<Role>.Update.AddToSetEach(r => r.PermissionIds, existingPermissions);
            var result = await _mongoDb.Roles.UpdateOneAsync(r => r.Id == roleId, update);
            
            return result.ModifiedCount > 0;
        }

        public async Task<bool> RemovePermissionFromRoleAsync(string roleId, string permissionId)
        {
            // Pull removes the specific permissionId from the array
            var update = Builders<Role>.Update.Pull(r => r.PermissionIds, permissionId);
            var result = await _mongoDb.Roles.UpdateOneAsync(r => r.Id == roleId, update);
            
            return result.ModifiedCount > 0;
        }
    }
}