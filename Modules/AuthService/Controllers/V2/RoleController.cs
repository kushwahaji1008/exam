using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AuthService.Models;
using AuthService.Services;

namespace AuthService.V2.Controllers
{
    [ApiController]
    [Route("api/v2/roles")]
    [Authorize(Roles = "Admin,SuperAdmin")] // Restrict to admin roles
    public class RoleController : ControllerBase
    {
        private readonly RoleService _roleService;
        private readonly ILogger<RoleController> _logger;

        public RoleController(RoleService roleService, ILogger<RoleController> logger)
        {
            _roleService = roleService;
            _logger = logger;
        }

        // ==========================================
        // 1. CORE ROLE CRUD OPERATIONS
        // ==========================================

        [HttpGet]
        public async Task<IActionResult> GetAllRoles()
        {
            var roles = await _roleService.GetAllRolesAsync();
            return Ok(roles);
        }

        [HttpPost]
        public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var (success, message, roleId) = await _roleService.CreateRoleAsync(request);
            if (!success) return BadRequest(new { message });

            return CreatedAtAction(nameof(GetRoleById), new { roleId }, new { message, roleId });
        }

        [HttpGet("{roleId}")]
        public async Task<IActionResult> GetRoleById(string roleId)
        {
            var role = await _roleService.GetRoleByIdAsync(roleId);
            if (role == null) return NotFound(new { message = "Role not found" });

            return Ok(role);
        }

        [HttpPut("{roleId}")]
        public async Task<IActionResult> UpdateRole(string roleId, [FromBody] UpdateRoleRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var success = await _roleService.UpdateRoleAsync(roleId, request);
            if (!success) return NotFound(new { message = "Role not found or update failed" });

            return Ok(new { message = "Role updated successfully" });
        }

        [HttpPatch("{roleId}")]
        public async Task<IActionResult> PatchRole(string roleId, [FromBody] object request)
        {
            // TODO: Implement partial update logic (e.g., using JSON Patch)
            var success = await _roleService.PatchRoleAsync(roleId, request);
            if (!success) return NotFound(new { message = "Role not found or patch failed" });

            return Ok(new { message = "Role patched successfully" });
        }

        [HttpDelete("{roleId}")]
        public async Task<IActionResult> DeleteRole(string roleId)
        {
            var success = await _roleService.DeleteRoleAsync(roleId);
            if (!success) return NotFound(new { message = "Role not found or cannot be deleted (e.g., in use)" });

            return Ok(new { message = "Role deleted successfully" });
        }

        // ==========================================
        // 2. USER-ROLE MAPPING
        // ==========================================

        [HttpPost("{roleId}/users/{userId}")]
        public async Task<IActionResult> AssignRoleToUser(string roleId, string userId)
        {
            var success = await _roleService.AssignRoleToUserAsync(roleId, userId);
            if (!success) return BadRequest(new { message = "Failed to assign role. User or Role may not exist, or assignment already exists." });

            return Ok(new { message = "Role assigned to user successfully" });
        }

        [HttpDelete("{roleId}/users/{userId}")]
        public async Task<IActionResult> RemoveRoleFromUser(string roleId, string userId)
        {
            var success = await _roleService.RemoveRoleFromUserAsync(roleId, userId);
            if (!success) return NotFound(new { message = "Assignment not found or removal failed" });

            return Ok(new { message = "Role removed from user successfully" });
        }

        // ==========================================
        // 3. ROLE-PERMISSION MAPPING
        // ==========================================

        [HttpGet("{roleId}/permissions")]
        public async Task<IActionResult> GetRolePermissions(string roleId)
        {
            var permissions = await _roleService.GetRolePermissionsAsync(roleId);
            if (permissions == null) return NotFound(new { message = "Role not found" });

            return Ok(new { permissions });
        }

        [HttpPost("{roleId}/permissions")]
        public async Task<IActionResult> AssignPermissionsToRole(string roleId, [FromBody] AssignPermissionsRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var success = await _roleService.AssignPermissionsToRoleAsync(roleId, request.PermissionIds);
            if (!success) return BadRequest(new { message = "Failed to assign permissions. Role may not exist." });

            return Ok(new { message = "Permissions assigned to role successfully" });
        }

        [HttpDelete("{roleId}/permissions/{permissionId}")]
        public async Task<IActionResult> RemovePermissionFromRole(string roleId, string permissionId)
        {
            var success = await _roleService.RemovePermissionFromRoleAsync(roleId, permissionId);
            if (!success) return NotFound(new { message = "Role or Permission mapping not found" });

            return Ok(new { message = "Permission removed from role successfully" });
        }
    }
}