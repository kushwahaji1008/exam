using System.Security.Cryptography;
using MongoDB.Driver;
using ExamService.Models;

namespace ExamService.Services
{
    public class ExamSeriesManagementService
    {
        private readonly MongoDbService _mongoDb;

        public ExamSeriesManagementService(MongoDbService mongoDb)
        {
            _mongoDb = mongoDb;
        }

        public async Task<ExamSeries> CreateSeriesAsync(CreateExamSeriesRequest request, string userId)
        {
            string seriesId = string.Empty;
            for (int i = 0; i < 15; i++)
            {
                var candidate = $"EXS-{RandomNumberGenerator.GetInt32(100_000, 1_000_000)}";
                if (!await _mongoDb.ExamSeries.Find(s => s.SeriesId == candidate).AnyAsync())
                {
                    seriesId = candidate;
                    break;
                }
            }
            if (string.IsNullOrEmpty(seriesId))
                seriesId = $"EXS-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

            var series = new ExamSeries
            {
                SeriesId = seriesId,
                Title = request.Title,
                Description = request.Description,
                Price = request.Price,
                DiscountPrice = request.DiscountPrice,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };

            await _mongoDb.ExamSeries.InsertOneAsync(series);
            return series;
        }

        public async Task<List<ExamSeries>> GetAllSeriesAsync()
        {
            return await _mongoDb.ExamSeries
                .Find(s => s.SeriesId != null)
                .SortByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<ExamSeries?> GetSeriesByIdAsync(string seriesId)
        {
            return await _mongoDb.ExamSeries
                .Find(s => s.SeriesId == seriesId)
                .FirstOrDefaultAsync();
        }

        public async Task<bool> UpdateSeriesAsync(string seriesId, UpdateExamSeriesRequest request)
        {
            var update = Builders<ExamSeries>.Update
                .Set(s => s.Title, request.Title)
                .Set(s => s.Description, request.Description)
                .Set(s => s.Price, request.Price)
                .Set(s => s.DiscountPrice, request.DiscountPrice)
                .Set(s => s.IsPublished, request.IsPublished)
                .Set(s => s.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.ExamSeries.UpdateOneAsync(s => s.SeriesId == seriesId, update);
            return result.MatchedCount > 0;
        }

        public async Task<bool> DeleteSeriesAsync(string seriesId)
        {
            var result = await _mongoDb.ExamSeries.DeleteOneAsync(s => s.SeriesId == seriesId);
            return result.DeletedCount > 0;
        }

        public async Task<(bool Success, string Message)> AddExamToSeriesAsync(string seriesId, string examId)
        {
            var series = await GetSeriesByIdAsync(seriesId);
            if (series == null) return (false, "Exam Series not found.");

            var examExists = await _mongoDb.Exams.Find(e => e.ExamId == examId).AnyAsync();
            if (!examExists) return (false, "Exam not found.");

            if (series.ExamIds.Contains(examId))
                return (false, "Exam is already added to this series.");

            var update = Builders<ExamSeries>.Update
                .AddToSet(s => s.ExamIds, examId)
                .Set(s => s.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.ExamSeries.UpdateOneAsync(s => s.SeriesId == seriesId, update);
            return (result.MatchedCount > 0, result.MatchedCount > 0 ? "Exam added successfully." : "Failed to add exam.");
        }

        public async Task<(bool Success, string Message)> RemoveExamFromSeriesAsync(string seriesId, string examId)
        {
            var update = Builders<ExamSeries>.Update
                .Pull(s => s.ExamIds, examId)
                .Set(s => s.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.ExamSeries.UpdateOneAsync(s => s.SeriesId == seriesId, update);
            if (result.MatchedCount == 0) return (false, "Exam Series not found.");
            return (result.ModifiedCount > 0, result.ModifiedCount > 0 ? "Exam removed from series." : "Exam was not in this series.");
        }
    }
}
