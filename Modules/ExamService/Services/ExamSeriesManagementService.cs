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
            var series = new ExamSeries
            {
                // Auto-generate secure ID
                SeriesId = $"EXS-{new Random().Next(100000, 999999)}",
                Title = request.Title,
                Description = request.Description,
                Price = request.Price,
                CreatedBy = userId
            };

            await _mongoDb.ExamSeries.InsertOneAsync(series); // Note: Ensure 'ExamSeries' collection is in your MongoDbService
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

        public async Task<(bool Success, string Message)> AddExamToSeriesAsync(string seriesId, string examId)
        {
            var series = await GetSeriesByIdAsync(seriesId);
            if (series == null) return (false, "Exam Series not found.");

            // Basic check to avoid duplicates
            if (series.ExamIds.Contains(examId)) 
                return (false, "Exam is already added to this series.");

            var update = Builders<ExamSeries>.Update
                .Push(s => s.ExamIds, examId)
                .Set(s => s.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.ExamSeries.UpdateOneAsync(s => s.SeriesId == seriesId, update);
            
            return (result.ModifiedCount > 0, result.ModifiedCount > 0 ? "Exam added successfully." : "Failed to add exam.");
        }
    }
}