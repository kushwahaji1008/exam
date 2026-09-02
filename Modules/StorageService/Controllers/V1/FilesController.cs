using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StorageService.Services;

namespace StorageService.Controllers.V1
{
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize] // Sirf logged-in users hi files upload kar sakte hain
    public class FilesController : ControllerBase
    {
        private readonly IStorageService _storageService;

        // Security Configurations
        private readonly string[] _allowedImageExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
        private readonly string[] _allowedDocumentExtensions = { ".pdf" };
        
        private const long MaxImageSize = 5 * 1024 * 1024; // 5 MB
        private const long MaxPdfSize = 20 * 1024 * 1024;  // 20 MB

        public FilesController(IStorageService storageService)
        {
            _storageService = storageService;
        }

        [HttpPost("upload-profile-image")]
        public async Task<IActionResult> UploadProfileImage(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest(new { message = "No file uploaded." });
            
            if (file.Length > MaxImageSize) return BadRequest(new { message = "Image size exceeds 5MB limit." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!_allowedImageExtensions.Contains(ext)) return BadRequest(new { message = "Only JPG, PNG and WEBP are allowed." });

            var url = await _storageService.UploadFileAsync(file, "profiles");
            
            return Ok(new { url, message = "Profile image uploaded successfully." });
        }

        [HttpPost("upload-course-pdf")]
        public async Task<IActionResult> UploadCoursePdf(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest(new { message = "No file uploaded." });
            
            if (file.Length > MaxPdfSize) return BadRequest(new { message = "PDF size exceeds 20MB limit." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!_allowedDocumentExtensions.Contains(ext)) return BadRequest(new { message = "Only PDF files are allowed." });

            var url = await _storageService.UploadFileAsync(file, "course-materials");
            
            return Ok(new { url, message = "PDF uploaded successfully." });
        }

        [HttpDelete("delete-file")]
        public async Task<IActionResult> DeleteFile([FromBody] DeleteFileRequest request)
        {
            var success = await _storageService.DeleteFileAsync(request.FileUrl);
            if (!success) return BadRequest(new { message = "Failed to delete file or file not found." });
            
            return Ok(new { message = "File deleted successfully." });
        }
    }

    public class DeleteFileRequest
    {
        public string FileUrl { get; set; } = string.Empty;
    }
}