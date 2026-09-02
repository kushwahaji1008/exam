using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace StorageService.Services
{
    public class GoogleDriveStorageService : IStorageService
    {
        private readonly DriveService _driveService;
        private readonly string _folderId;

        public GoogleDriveStorageService(IConfiguration configuration)
        {
            // Apne appsettings.json se Drive Folder ID aur Credentials Path nikalna
            _folderId = configuration["GoogleDrive:FolderId"] 
                        ?? throw new ArgumentNullException("Google Drive Folder ID is missing.");
            
            var credentialsPath = configuration["GoogleDrive:CredentialsPath"] 
                        ?? throw new ArgumentNullException("Credentials Path is missing.");

            // Google Authentication setup
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
        }

        public async Task<string> UploadFileAsync(IFormFile file, string folderName)
        {
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