using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace StorageService.Services
{
    public class LocalStorageService : IStorageService
    {
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".pdf", ".mp4", ".webm", ".mov", ".m4v"
        };

        private readonly string _rootPath;
        private readonly ILogger<LocalStorageService> _logger;

        public LocalStorageService(IConfiguration configuration, IHostEnvironment environment, ILogger<LocalStorageService> logger)
        {
            _logger = logger;
            var configuredRoot = configuration["Storage:LocalRoot"];
            _rootPath = string.IsNullOrWhiteSpace(configuredRoot)
                ? Path.GetFullPath(Path.Combine(environment.ContentRootPath, "storage", "uploads"))
                : Path.GetFullPath(configuredRoot);

            Directory.CreateDirectory(_rootPath);
        }

        public string RootPath => _rootPath;

        public async Task<string> UploadFileAsync(IFormFile file, string folderName)
        {
            var safeFolder = SanitizeSegment(folderName, "general");
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
                throw new InvalidOperationException("Unsupported file extension.");

            var targetFolder = Path.GetFullPath(Path.Combine(_rootPath, safeFolder));
            if (!targetFolder.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid target folder.");

            Directory.CreateDirectory(targetFolder);

            var uniqueName = $"{Guid.NewGuid():N}_{DateTime.UtcNow.Ticks}{ext}";
            var fullPath = Path.GetFullPath(Path.Combine(targetFolder, uniqueName));
            if (!fullPath.StartsWith(targetFolder, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid file path.");

            await using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await file.CopyToAsync(stream);
            }

            _logger.LogInformation("Stored file locally: {Folder}/{FileName} ({Bytes} bytes)", safeFolder, uniqueName, file.Length);
            return $"/api/v1/files/download/{safeFolder}/{uniqueName}";
        }

        public Task<bool> DeleteFileAsync(string fileUrl)
        {
            if (string.IsNullOrWhiteSpace(fileUrl)) return Task.FromResult(false);

            const string prefix = "/api/v1/files/download/";
            var pathPart = fileUrl.Trim();
            var idx = pathPart.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return Task.FromResult(false);

            var relative = pathPart[(idx + prefix.Length)..].TrimStart('/');
            var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return Task.FromResult(false);

            var safeFolder = SanitizeSegment(parts[0], string.Empty);
            var safeFileName = parts[1];
            if (string.IsNullOrEmpty(safeFolder) || safeFileName.Contains("..") || Path.GetFileName(safeFileName) != safeFileName)
                return Task.FromResult(false);

            var fullPath = Path.GetFullPath(Path.Combine(_rootPath, safeFolder, safeFileName));
            if (!fullPath.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(false);

            if (!File.Exists(fullPath)) return Task.FromResult(false);

            File.Delete(fullPath);
            return Task.FromResult(true);
        }

        public (bool Found, string FullPath, string ContentType) ResolveDownloadPath(string folder, string fileName)
        {
            var safeFolder = SanitizeSegment(folder, string.Empty);
            if (string.IsNullOrEmpty(safeFolder) ||
                string.IsNullOrWhiteSpace(fileName) ||
                fileName.Contains("..") ||
                Path.GetFileName(fileName) != fileName)
            {
                return (false, string.Empty, string.Empty);
            }

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
                return (false, string.Empty, string.Empty);

            var fullPath = Path.GetFullPath(Path.Combine(_rootPath, safeFolder, fileName));
            if (!fullPath.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return (false, string.Empty, string.Empty);

            if (!File.Exists(fullPath))
                return (false, string.Empty, string.Empty);

            var contentType = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".pdf" => "application/pdf",
                ".mp4" or ".m4v" => "video/mp4",
                ".webm" => "video/webm",
                ".mov" => "video/quicktime",
                _ => "application/octet-stream"
            };

            return (true, fullPath, contentType);
        }

        private static string SanitizeSegment(string value, string fallback)
        {
            var cleaned = Regex.Replace(value ?? string.Empty, "[^a-zA-Z0-9_-]", "");
            return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
        }
    }
}
