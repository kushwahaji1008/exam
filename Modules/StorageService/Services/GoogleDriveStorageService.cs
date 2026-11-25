using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace StorageService.Services
{
    public class GoogleDriveStorageService : IStorageService
    {
        private readonly DriveService? _driveService;
        private readonly string _folderId;
        private readonly ILogger<GoogleDriveStorageService> _logger;
        private readonly bool _isConfigured;

        public GoogleDriveStorageService(IConfiguration configuration, ILogger<GoogleDriveStorageService> logger)
        {
            _logger = logger;

            _folderId = configuration["GoogleDrive:FolderId"] ?? string.Empty;
            var credentialsPath = configuration["GoogleDrive:CredentialsPath"] ?? string.Empty;

            // FIX: Don't throw at startup — log a warning instead, fail gracefully at call time.
            if (string.IsNullOrEmpty(_folderId) || _folderId.StartsWith("SET_VIA_ENV") ||
                string.IsNullOrEmpty(credentialsPath) || credentialsPath.StartsWith("SET_VIA_ENV") ||
                !File.Exists(credentialsPath))
            {
                _logger.LogWarning("GoogleDriveStorageService: Credentials not configured or file not found at '{Path}'. File upload/delete will return errors.", credentialsPath);
                _isConfigured = false;
                _driveService = null;
                return;
            }

            try
            {
                GoogleCredential credential;
                using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
                {
                    credential = GoogleCredential.FromStream(stream)
                                    .CreateScoped(DriveService.Scope.Drive);
                }

                _driveService = new DriveService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "LMS Platform"
                });
                _isConfigured = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GoogleDriveStorageService: Failed to initialize Google Drive client.");
                _isConfigured = false;
                _driveService = null;
            }
        }

        public async Task<string> UploadFileAsync(IFormFile file, string folderName)
        {
            if (!_isConfigured || _driveService == null)
                throw new InvalidOperationException("Google Drive storage is not configured. Set GoogleDrive__FolderId and GoogleDrive__CredentialsPath environment variables.");

            var fileExtension = Path.GetExtension(file.FileName);
            var uniqueFileName = $"{folderName}_{Guid.NewGuid()}_{DateTime.UtcNow.Ticks}{fileExtension}";

            // 1. Setup file metadata for Google Drive
            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = uniqueFileName,
                Parents = new List<string> { _folderId } // Main folder jahan save hoga
            };

            string fileId = "";

            // 2. Upload file stream
            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                var request = _driveService.Files.Create(fileMetadata, memoryStream, file.ContentType);
                request.Fields = "id, webViewLink, webContentLink"; // Return public URLs
                
                var response = await request.UploadAsync();
                
                if (response.Status == Google.Apis.Upload.UploadStatus.Failed)
                    throw new Exception("Google Drive upload failed.");

                fileId = request.ResponseBody.Id;
            }

            // 3. Make the uploaded file "Public" so frontend can view/download it
            var permission = new Google.Apis.Drive.v3.Data.Permission
            {
                Type = "anyone",
                Role = "reader"
            };
            await _driveService.Permissions.Create(permission, fileId).ExecuteAsync();

            // 4. Return the direct access URL
            // Google Drive normally returns a viewer link, we modify it to act as a direct image/PDF link
            return $"https://drive.google.com/uc?id={fileId}";
        }

        public async Task<bool> DeleteFileAsync(string fileUrl)
        {
            if (!_isConfigured || _driveService == null)
                return false;

            try
            {
                // Extract File ID from the URL (e.g., https://drive.google.com/uc?id=XXXXXX)
                var fileId = fileUrl.Split("id=").LastOrDefault();
                if (string.IsNullOrEmpty(fileId)) return false;

                // Delete from Drive
                await _driveService.Files.Delete(fileId).ExecuteAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}