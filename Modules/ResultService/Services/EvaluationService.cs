using MongoDB.Driver;
using ResultService.Models;

namespace ResultService.Services
{
    public class EvaluationService
    {
        private readonly MongoDbService _mongoDb;

        public EvaluationService(MongoDbService mongoDb)
        {
            _mongoDb = mongoDb;
        }

        // ==========================================
        // 1. RESULTS & GRADING
        // ==========================================
        public async Task<List<ExamResult>> GetAllResultsAsync() => 
            await _mongoDb.Results.Find(_ => true).ToListAsync();

        public async Task<ExamResult?> GetResultByIdAsync(string resultId) => 
            await _mongoDb.Results.Find(r => r.Id == resultId).FirstOrDefaultAsync();

        public async Task<bool> ChangeStatusAsync(string resultId, ResultStatus status)
        {
            var update = Builders<ExamResult>.Update.Set(r => r.Status, status);
            
            if (status == ResultStatus.Calculated) update = update.Set(r => r.CalculatedAt, DateTime.UtcNow);
            if (status == ResultStatus.Finalized) update = update.Set(r => r.FinalizedAt, DateTime.UtcNow);
            if (status == ResultStatus.Published) update = update.Set(r => r.PublishedAt, DateTime.UtcNow);
            if (status == ResultStatus.Calculated && status != ResultStatus.Published) 
                update = update.Set(r => r.PublishedAt, null); // Unpublish logic

            var res = await _mongoDb.Results.UpdateOneAsync(r => r.Id == resultId, update);
            return res.ModifiedCount > 0;
        }

        public async Task<bool> BulkPublishAsync(List<string> resultIds)
        {
            var update = Builders<ExamResult>.Update
                .Set(r => r.Status, ResultStatus.Published)
                .Set(r => r.PublishedAt, DateTime.UtcNow);
            var res = await _mongoDb.Results.UpdateManyAsync(r => resultIds.Contains(r.Id), update);
            return res.ModifiedCount > 0;
        }

        // MANUAL GRADING
        public async Task<bool> GradeQuestionAsync(string resultId, string questionId, double score, string evaluatorId, string? overrideReason = null)
        {
            var result = await GetResultByIdAsync(resultId);
            if (result == null) return false;

            var q = result.Breakdown.FirstOrDefault(b => b.QuestionId == questionId);
            if (q == null) return false;

            q.Score = score;
            q.IsCorrect = score > 0;
            q.NeedsManualGrading = false;
            q.EvaluatorId = evaluatorId;
            if (overrideReason != null) q.OverrideReason = overrideReason;

            // Recalculate Totals
            result.TotalScore = result.Breakdown.Sum(x => x.Score);
            result.IsPassed = result.TotalScore >= result.PassingScore;

            var dbResult = await _mongoDb.Results.ReplaceOneAsync(r => r.Id == resultId, result);
            return dbResult.ModifiedCount > 0;
        }

        // ==========================================
        // 2. CROSS-ENTITY QUERIES
        // ==========================================
        public async Task<List<ExamResult>> GetResultsByUserAsync(string userId) =>
            await _mongoDb.Results.Find(r => r.UserId == userId).ToListAsync();

        public async Task<List<ExamResult>> GetResultsByExamAsync(string examId) =>
            await _mongoDb.Results.Find(r => r.ExamId == examId).ToListAsync();

        public async Task<ExamResult?> GetResultByAttemptAsync(string attemptId) =>
            await _mongoDb.Results.Find(r => r.AttemptId == attemptId).FirstOrDefaultAsync();

        // ==========================================
        // 3. CERTIFICATES
        // ==========================================
        public async Task<Certificate> GenerateCertificateAsync(string resultId)
        {
            var result = await GetResultByIdAsync(resultId);
            if (result == null || !result.IsPassed) throw new Exception("Result not found or exam failed.");

            var cert = new Certificate
            {
                ResultId = resultId,
                UserId = result.UserId,
                ExamId = result.ExamId,
                CertificateUrl = $"https://s3.bucket/certs/{resultId}.pdf" // Mock URL
            };

            await _mongoDb.Certificates.InsertOneAsync(cert);
            return cert;
        }

        public async Task<List<Certificate>> GetAllCertificatesAsync() => 
            await _mongoDb.Certificates.Find(_ => true).ToListAsync();

        public async Task<Certificate?> GetCertificateByIdAsync(string certId) => 
            await _mongoDb.Certificates.Find(c => c.Id == certId).FirstOrDefaultAsync();

        public async Task<List<Certificate>> GetCertificatesByUserAsync(string userId) => 
            await _mongoDb.Certificates.Find(c => c.UserId == userId).ToListAsync();

        public async Task<Certificate?> VerifyCertificateAsync(string code) => 
            await _mongoDb.Certificates.Find(c => c.VerificationCode == code).FirstOrDefaultAsync();
    }
}