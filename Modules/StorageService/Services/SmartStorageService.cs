using Microsoft.AspNetCore.Http;

namespace StorageService.Services
{
    public class SmartStorageService : IStorageService
    {
        private readonly IConfiguration _configuration;
        private readonly LocalStorageService _localStorage;
        private readonly GoogleDriveStorageService? _driveStorage;
        private readonly ILogger<SmartStorageService> _logger;

        public SmartStorageService(
            IConfiguration configuration,
            LocalStorageService localStorage,
            ILogger<SmartStorageService> logger,
            GoogleDriveStorageService? driveStorage = null)
        {
            _configuration = configuration;
            _localStorage = localStorage;
            _logger = logger;
            _driveStorage = driveStorage;
        }

        private bool IsGoogleDriveActive()
        {
            var provider = _configuration["Storage:Provider"];
            if (provider?.Equals("Local", StringComparison.OrdinalIgnoreCase) == true)
                return false;

            var folderId = _configuration["GoogleDrive:FolderId"];
            var credsPath = _configuration["GoogleDrive:CredentialsPath"];
            return _driveStorage != null &&
                   !string.IsNullOrWhiteSpace(folderId) &&
                   !folderId.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) &&
                   !folderId.StartsWith("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(credsPath) &&
                   File.Exists(credsPath);
        }

        public async Task<string> UploadFileAsync(IFormFile file, string folderName)
        {
            if (IsGoogleDriveActive())
            {
                return await _driveStorage!.UploadFileAsync(file, folderName);
            }

            _logger.LogDebug("Using LocalStorageService for upload in folder {Folder}", folderName);
            return await _localStorage.UploadFileAsync(file, folderName);
        }

        public async Task<bool> DeleteFileAsync(string fileUrl)
        {
            if (string.IsNullOrWhiteSpace(fileUrl)) return false;

            if (fileUrl.StartsWith("/api/v1/files/download/", StringComparison.OrdinalIgnoreCase))
                return await _localStorage.DeleteFileAsync(fileUrl);

            if (IsGoogleDriveActive())
                return await _driveStorage!.DeleteFileAsync(fileUrl);

            return await _localStorage.DeleteFileAsync(fileUrl);
        }
    }
}
