using Microsoft.AspNetCore.Http;

namespace StorageService.Services
{
    public interface IStorageService
    {
        // Uploads a file and returns its Global Public URL
        Task<string> UploadFileAsync(IFormFile file, string folderName);
        
        // Deletes a file from Google Cloud Storage using its URL
        Task<bool> DeleteFileAsync(string fileUrl);
    }
}