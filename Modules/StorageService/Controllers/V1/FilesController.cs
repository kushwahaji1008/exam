using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StorageService.Services;

namespace StorageService.Controllers.V1
{
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class FilesController : ControllerBase
    {
        private readonly IStorageService _storageService;
        private readonly LocalStorageService? _localStorage;
        private readonly MediaTicketService _ticketService;

        private static readonly string[] AllowedImageExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
        private static readonly string[] AllowedDocumentExtensions = { ".pdf" };
        private static readonly string[] AllowedVideoExtensions = { ".mp4", ".webm", ".mov", ".m4v" };

        private const long MaxImageSize = 5 * 1024 * 1024; // 5 MB
        private const long MaxPdfSize = 20 * 1024 * 1024;  // 20 MB
        private const long MaxVideoSize = 200 * 1024 * 1024; // 200 MB

        public FilesController(
            IStorageService storageService,
            MediaTicketService ticketService,
            LocalStorageService? localStorage = null)
        {
            _storageService = storageService;
            _ticketService = ticketService;
            _localStorage = localStorage;
        }

        [HttpPost("upload-profile-image")]
        [RequestSizeLimit(MaxImageSize)]
        public async Task<IActionResult> UploadProfileImage(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest(new { message = "No file uploaded." });
            if (file.Length > MaxImageSize) return BadRequest(new { message = "Image size exceeds 5MB limit." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedImageExtensions.Contains(ext))
                return BadRequest(new { message = "Only JPG, PNG and WEBP are allowed." });

            if (!await HasValidImageSignatureAsync(file, ext))
                return BadRequest(new { message = "File content does not match declared image format." });

            var url = await _storageService.UploadFileAsync(file, "profiles");
            return Ok(new { url, message = "Profile image uploaded successfully." });
        }

        [HttpPost("upload-course-pdf")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        [RequestSizeLimit(MaxPdfSize)]
        public async Task<IActionResult> UploadCoursePdf(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest(new { message = "No file uploaded." });
            if (file.Length > MaxPdfSize) return BadRequest(new { message = "PDF size exceeds 20MB limit." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedDocumentExtensions.Contains(ext))
                return BadRequest(new { message = "Only PDF files are allowed." });

            if (!await HasValidPdfSignatureAsync(file))
                return BadRequest(new { message = "Invalid PDF file signature." });

            var url = await _storageService.UploadFileAsync(file, "course-materials");
            return Ok(new { url, message = "PDF uploaded successfully." });
        }

        [HttpPost("upload-course-video")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        [RequestSizeLimit(MaxVideoSize)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxVideoSize)]
        public async Task<IActionResult> UploadCourseVideo(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest(new { message = "No file uploaded." });
            if (file.Length > MaxVideoSize) return BadRequest(new { message = "Video size exceeds 200MB limit." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedVideoExtensions.Contains(ext))
                return BadRequest(new { message = "Only MP4, WebM, MOV, and M4V videos are allowed." });

            if (!await HasValidVideoSignatureAsync(file, ext))
                return BadRequest(new { message = "File content does not match declared video format." });

            var url = await _storageService.UploadFileAsync(file, "course-videos");
            return Ok(new { url, message = "Video uploaded successfully." });
        }

        [HttpGet("download/{folder}/{fileName}")]
        [AllowAnonymous]
        public IActionResult DownloadLocalFile(string folder, string fileName, [FromQuery] string? ticket = null)
        {
            if (_localStorage == null) return NotFound();

            bool isPublicAvatarFolder = string.Equals(folder, "profiles", StringComparison.OrdinalIgnoreCase);
            if (!isPublicAvatarFolder)
            {
                bool hasJwtAuth = User.Identity?.IsAuthenticated == true;
                bool hasValidTicket = _ticketService.ValidateTicket(ticket, folder, fileName, out _);
                if (!hasJwtAuth && !hasValidTicket)
                {
                    return Unauthorized(new { message = "Authentication or valid platform stream ticket is required to access protected media." });
                }

                // Block opening protected course PDFs/videos directly in an external browser tab.
                var fetchDest = Request.Headers["Sec-Fetch-Dest"].FirstOrDefault();
                var fetchMode = Request.Headers["Sec-Fetch-Mode"].FirstOrDefault();
                if (string.Equals(fetchDest, "document", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fetchMode, "navigate", StringComparison.OrdinalIgnoreCase))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new
                    {
                        message = "Direct browser navigation is disabled. Please view PDFs and videos inside the platform."
                    });
                }
            }

            var (found, fullPath, contentType) = _localStorage.ResolveDownloadPath(folder, fileName);
            if (!found) return NotFound(new { message = "File not found." });

            if (!isPublicAvatarFolder)
            {
                Response.Headers["Content-Disposition"] = "inline";
                Response.Headers["Cache-Control"] = "private, no-store, no-cache, must-revalidate, max-age=0";
                Response.Headers["Pragma"] = "no-cache";
                Response.Headers["X-Content-Type-Options"] = "nosniff";
            }

            return PhysicalFile(fullPath, contentType, enableRangeProcessing: true);
        }

        [HttpDelete("delete-file")]
        public async Task<IActionResult> DeleteFile([FromBody] DeleteFileRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.FileUrl))
                return BadRequest(new { message = "FileUrl is required." });

            var success = await _storageService.DeleteFileAsync(request.FileUrl);
            if (!success) return BadRequest(new { message = "Failed to delete file or file not found." });

            return Ok(new { message = "File deleted successfully." });
        }

        private static async Task<bool> HasValidImageSignatureAsync(IFormFile file, string ext)
        {
            var header = new byte[12];
            using var stream = file.OpenReadStream();
            int read = await stream.ReadAsync(header.AsMemory(0, header.Length));

            return ext switch
            {
                ".jpg" or ".jpeg" => read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
                ".png" => read >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                                    && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A,
                ".webp" => read >= 12 && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
                                      && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P',
                _ => false
            };
        }

        private static async Task<bool> HasValidPdfSignatureAsync(IFormFile file)
        {
            var header = new byte[5];
            using var stream = file.OpenReadStream();
            int read = await stream.ReadAsync(header.AsMemory(0, header.Length));
            return read >= 5 && header[0] == '%' && header[1] == 'P' && header[2] == 'D' && header[3] == 'F' && header[4] == '-';
        }

        private static async Task<bool> HasValidVideoSignatureAsync(IFormFile file, string ext)
        {
            var header = new byte[12];
            using var stream = file.OpenReadStream();
            int read = await stream.ReadAsync(header.AsMemory(0, header.Length));
            if (read < 8) return false;

            if (ext == ".webm")
            {
                // EBML header: 1A 45 DF A3
                return header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3;
            }

            // MP4 / M4V / MOV (ISO Base Media File Format: 'ftyp' box at bytes 4..7)
            return header[4] == (byte)'f' && header[5] == (byte)'t' && header[6] == (byte)'y' && header[7] == (byte)'p';
        }
    }

    public class DeleteFileRequest
    {
        [Required] public string FileUrl { get; set; } = string.Empty;
    }
}
