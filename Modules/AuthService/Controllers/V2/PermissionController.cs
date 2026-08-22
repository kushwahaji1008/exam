using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AuthService.Models;
using AuthService.Services;

namespace AuthService.V2.Controllers
{
    [ApiVersion("2.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/permissions")]
    [Authorize(Roles = "Admin,SuperAdmin")] // Restrict to admin roles
    public class PermissionController : ControllerBase
    {
        private readonly PermissionService _permissionService;
        private readonly ILogger<PermissionController> _logger;

        public PermissionController(PermissionService permissionService, ILogger<PermissionController> logger)
        {
            _permissionService = permissionService;
            _logger = logger;
        }

        // ==========================================
        // PERMISSION CRUD OPERATIONS
        // ==========================================

        [HttpGet]
        public async Task<IActionResult> GetAllPermissions()
        {
            var permissions = await _permissionService.GetAllPermissionsAsync();
            return Ok(permissions);
        }

        [HttpPost]
        public async Task<IActionResult> CreatePermission([FromBody] CreatePermissionRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var (success, message, permissionId) = await _permissionService.CreatePermissionAsync(request);
            if (!success) return BadRequest(new { message });

            return CreatedAtAction(nameof(GetPermissionById), new { permissionId }, new { message, permissionId });
        }

        [HttpGet("{permissionId}")]
        public async Task<IActionResult> GetPermissionById(string permissionId)
        {
            var permission = await _permissionService.GetPermissionByIdAsync(permissionId);
            if (permission == null) return NotFound(new { message = "Permission not found" });

            return Ok(permission);
        }

        [HttpPut("{permissionId}")]
        public async Task<IActionResult> UpdatePermission(string permissionId, [FromBody] UpdatePermissionRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var success = await _permissionService.UpdatePermissionAsync(permissionId, request);
            if (!success) return NotFound(new { message = "Permission not found or update failed" });

            return Ok(new { message = "Permission updated successfully" });
        }

        [HttpDelete("{permissionId}")]
        public async Task<IActionResult> DeletePermission(string permissionId)
        {
            var success = await _permissionService.DeletePermissionAsync(permissionId);
            if (!success) return NotFound(new { message = "Permission not found or cannot be deleted (e.g., in use)" });

            return Ok(new { message = "Permission deleted successfully" });
        }
    }
}