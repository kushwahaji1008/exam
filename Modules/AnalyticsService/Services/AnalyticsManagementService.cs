using MongoDB.Driver;
using AnalyticsService.Models;

namespace AnalyticsService.Services
{
    public class AnalyticsManagementService
    {
        private readonly MongoDbService _mongoDb;

        public AnalyticsManagementService(MongoDbService mongoDb)
        {
            _mongoDb = mongoDb;
        }

        // ==========================================
        // REPORTS MANAGEMENT
        // ==========================================
        public async Task<AnalyticsReport> CreateReportAsync(CreateReportRequest request, string userId)
        {
            var report = new AnalyticsReport
            {
                Name = request.Name,
                Type = request.Type,
                Filters = request.Filters,
                RequestedBy = userId,
                Status = ReportStatus.Pending
            };

            await _mongoDb.Reports.InsertOneAsync(report);
            return report;
        }

        public async Task<List<AnalyticsReport>> GetAllReportsAsync() => 
            await _mongoDb.Reports.Find(_ => true).SortByDescending(r => r.CreatedAt).ToListAsync();

        public async Task<AnalyticsReport?> GetReportByIdAsync(string reportId) => 
            await _mongoDb.Reports.Find(r => r.Id == reportId).FirstOrDefaultAsync();

        public async Task<bool> DeleteReportAsync(string reportId)
        {
            var result = await _mongoDb.Reports.DeleteOneAsync(r => r.Id == reportId);
            return result.DeletedCount > 0;
        }

        public async Task<bool> ProcessReportAsync(string reportId)
        {
            // MOCK LOGIC: Simulate background processing
            var update = Builders<AnalyticsReport>.Update
                .Set(r => r.Status, ReportStatus.Completed)
                .Set(r => r.GeneratedAt, DateTime.UtcNow)
                .Set(r => r.DownloadUrl, $"/exports/reports/{reportId}.pdf");

            var result = await _mongoDb.Reports.UpdateOneAsync(r => r.Id == reportId, update);
            return result.ModifiedCount > 0;
        }
    }
}