using Google.Cloud.Storage.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace StorageService.Services
{
    public class GcsStorageService : IStorageService
    {
        private readonly string _bucketName;
        private readonly StorageClient _storageClient;

        public GcsStorageService(IConfiguration configuration)
        {
            _bucketName = configuration["GoogleCloud:BucketName"] 
                          ?? throw new ArgumentNullException("BucketName missing in appsettings.json");
            
            // Ye automatically GOOGLE_APPLICATION_CREDENTIALS environment variable se login access le lega
            _storageClient = StorageClient.Create();
        }

        public async Task<string> UploadFileAsync(IFormFile file, string folderName)
        {
            // 1. Generate unique file name (e.g., profiles/guid_time.jpg)
            var fileExtension = Path.GetExtension(file.FileName);
            var objectName = $"{folderName}/{Guid.NewGuid()}_{DateTime.UtcNow.Ticks}{fileExtension}";

            // 2. Upload to GCS
            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            memoryStream.Position = 0; 

            // Note: Make sure your bucket is set to "Public" in GCP Console IAM settings
            var uploadedObject = await _storageClient.UploadObjectAsync(
                bucket: _bucketName,
                objectName: objectName,
                contentType: file.ContentType,
                source: memoryStream,
                options: new UploadObjectOptions { PredefinedAcl = PredefinedObjectAcl.PublicRead } 
            );

            // 3. Return Public CDN URL
            return uploadedObject.MediaLink ?? $"https://storage.googleapis.com/{_bucketName}/{objectName}";
        }

        public async Task<bool> DeleteFileAsync(string fileUrl)
        {
            try
            {
                var prefixToRemove = $"https://storage.googleapis.com/{_bucketName}/";
                if (!fileUrl.StartsWith(prefixToRemove)) return false;

                var objectName = fileUrl.Replace(prefixToRemove, "");

                await _storageClient.DeleteObjectAsync(_bucketName, objectName);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}