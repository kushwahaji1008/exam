using System.Globalization;
using System.Text;
using MongoDB.Driver;
using ResultService.Models;

namespace ResultService.Services
{
    public interface IAttemptEvaluationSource
    {
        Task<EvaluationInputData?> LoadEvaluationDataAsync(string attemptId);
        Task UpdateEvaluatedAttemptAsync(string attemptId, double score, double percentage, bool passed, IReadOnlyList<QuestionGrade> breakdown);
        Task NotifyResultPublishedAsync(string userId, string examId, double score, double maxScore, bool passed);
    }

    public class EvaluationInputData
    {
        public string AttemptId { get; set; } = string.Empty;
        public string ExamId { get; set; } = string.Empty;
        public string ExamTitle { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public double PassingMarks { get; set; }
        public double TotalMarks { get; set; }
        public bool EnableNegativeMarking { get; set; }
        public double NegativeMarkingPercentage { get; set; }
        public bool ShowResultsImmediately { get; set; }
        public List<EvaluationQuestionInput> Questions { get; set; } = new();
    }

    public class EvaluationQuestionInput
    {
        public string QuestionId { get; set; } = string.Empty;
        public string QuestionType { get; set; } = "MCQ";
        public double Marks { get; set; } = 1.0;
        public double? NegativeMarks { get; set; }
        public string? CorrectAnswer { get; set; }
        public List<string> CorrectOptions { get; set; } = new();
        public string? SelectedOption { get; set; }
        public List<string> SelectedOptions { get; set; } = new();
        public string? TextAnswer { get; set; }
        public string? CodeAnswer { get; set; }
    }

    public class EvaluationService
    {
        private readonly MongoDbService _mongoDb;
        private readonly IAttemptEvaluationSource? _evaluationSource;

        public EvaluationService(MongoDbService mongoDb, IAttemptEvaluationSource? evaluationSource = null)
        {
            _mongoDb = mongoDb;
            _evaluationSource = evaluationSource;
        }

        // ==========================================
        // 1. AUTOMATIC ATTEMPT EVALUATION ENGINE
        // ==========================================
        public async Task<ExamResult?> EvaluateAttemptAsync(string attemptId, bool recalculate = false)
        {
            var existing = await GetResultByAttemptAsync(attemptId);
            if (existing != null && !recalculate)
                return existing;

            if (_evaluationSource == null) return existing;

            var input = await _evaluationSource.LoadEvaluationDataAsync(attemptId);
            if (input == null) return null;

            var breakdown = new List<QuestionGrade>(input.Questions.Count);
            bool anyNeedsManual = false;

            foreach (var q in input.Questions)
            {
                var grade = EvaluateQuestion(q, input.EnableNegativeMarking, input.NegativeMarkingPercentage);
                if (grade.NeedsManualGrading) anyNeedsManual = true;
                breakdown.Add(grade);
            }

            double totalScore = Math.Max(0, breakdown.Sum(b => b.Score));
            double maxScore = input.TotalMarks > 0 ? input.TotalMarks : breakdown.Sum(b => b.MaxScore);
            double passingScore = input.PassingMarks > 0 ? input.PassingMarks : (maxScore * 0.4);
            bool passed = !anyNeedsManual && totalScore >= passingScore;

            var status = anyNeedsManual
                ? ResultStatus.AwaitingManualGrading
                : (input.ShowResultsImmediately ? ResultStatus.Published : ResultStatus.Calculated);

            if (existing != null)
            {
                existing.Breakdown = breakdown;
                existing.TotalScore = Math.Round(totalScore, 2);
                existing.MaxScore = Math.Round(maxScore, 2);
                existing.PassingScore = Math.Round(passingScore, 2);
                existing.IsPassed = passed;
                existing.Status = status;
                existing.CalculatedAt = DateTime.UtcNow;
                if (status == ResultStatus.Published) existing.PublishedAt = DateTime.UtcNow;

                await _mongoDb.Results.ReplaceOneAsync(r => r.Id == existing.Id, existing);
            }
            else
            {
                existing = new ExamResult
                {
                    AttemptId = attemptId,
                    ExamId = input.ExamId,
                    UserId = input.UserId,
                    Breakdown = breakdown,
                    TotalScore = Math.Round(totalScore, 2),
                    MaxScore = Math.Round(maxScore, 2),
                    PassingScore = Math.Round(passingScore, 2),
                    IsPassed = passed,
                    Status = status,
                    CalculatedAt = DateTime.UtcNow,
                    PublishedAt = status == ResultStatus.Published ? DateTime.UtcNow : null
                };
                await _mongoDb.Results.InsertOneAsync(existing);
            }

            await _evaluationSource.UpdateEvaluatedAttemptAsync(
                attemptId,
                existing.TotalScore,
                Math.Round(existing.Percentage, 2),
                existing.IsPassed,
                breakdown);

            if (status == ResultStatus.Published)
            {
                await _evaluationSource.NotifyResultPublishedAsync(
                    input.UserId,
                    input.ExamId,
                    existing.TotalScore,
                    existing.MaxScore,
                    existing.IsPassed);
            }

            return existing;
        }

        public static QuestionGrade EvaluateQuestion(EvaluationQuestionInput q, bool examNegativeMarking, double examNegativePercentage)
        {
            var type = (q.QuestionType ?? "MCQ").Trim();
            bool hasTextOrCode = !string.IsNullOrWhiteSpace(q.TextAnswer) || !string.IsNullOrWhiteSpace(q.CodeAnswer);
            bool hasOption = !string.IsNullOrWhiteSpace(q.SelectedOption) || q.SelectedOptions.Count > 0;
            bool attempted = hasOption || hasTextOrCode;

            if (type.Equals("Subjective", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("LongAnswer", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("CodeEvaluation", StringComparison.OrdinalIgnoreCase))
            {
                return new QuestionGrade
                {
                    QuestionId = q.QuestionId,
                    MaxScore = q.Marks,
                    Score = 0,
                    IsCorrect = false,
                    NeedsManualGrading = true
                };
            }

            bool isCorrect = false;
            if (attempted)
            {
                if (type.Equals("MultipleCorrect", StringComparison.OrdinalIgnoreCase))
                {
                    var expected = q.CorrectOptions.Select(s => s.Trim()).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var actual = q.SelectedOptions.Select(s => s.Trim()).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    isCorrect = expected.Count > 0 && expected.SetEquals(actual);
                }
                else
                {
                    var candidate = !string.IsNullOrWhiteSpace(q.SelectedOption) ? q.SelectedOption.Trim() : (q.TextAnswer?.Trim() ?? string.Empty);
                    var expectedSingle = q.CorrectAnswer?.Trim();
                    if (!string.IsNullOrEmpty(expectedSingle))
                    {
                        isCorrect = string.Equals(candidate, expectedSingle, StringComparison.OrdinalIgnoreCase);
                    }
                    else if (q.CorrectOptions.Count > 0)
                    {
                        isCorrect = q.CorrectOptions.Any(c => string.Equals(candidate, c.Trim(), StringComparison.OrdinalIgnoreCase));
                    }
                }
            }

            double score = 0;
            if (isCorrect)
            {
                score = q.Marks;
            }
            else if (attempted && (q.NegativeMarks > 0 || examNegativeMarking))
            {
                double penalty = q.NegativeMarks ?? (q.Marks * examNegativePercentage);
                score = -Math.Abs(penalty);
            }

            return new QuestionGrade
            {
                QuestionId = q.QuestionId,
                MaxScore = q.Marks,
                Score = Math.Round(score, 2),
                IsCorrect = isCorrect,
                NeedsManualGrading = false
            };
        }

        // ==========================================
        // 2. RESULTS & GRADING
        // ==========================================
        public async Task<List<ExamResult>> GetAllResultsAsync() =>
            await _mongoDb.Results.Find(_ => true).SortByDescending(r => r.CalculatedAt).ToListAsync();

        public async Task<ExamResult?> GetResultByIdAsync(string resultId) =>
            await _mongoDb.Results.Find(r => r.Id == resultId).FirstOrDefaultAsync();

        public async Task<bool> ChangeStatusAsync(string resultId, ResultStatus status)
        {
            var result = await GetResultByIdAsync(resultId);
            if (result == null) return false;

            if (status == ResultStatus.Calculated)
            {
                await EvaluateAttemptAsync(result.AttemptId, recalculate: true);
            }

            var update = Builders<ExamResult>.Update.Set(r => r.Status, status);

            if (status == ResultStatus.Calculated)
            {
                update = update
                    .Set(r => r.CalculatedAt, DateTime.UtcNow)
                    .Set(r => r.PublishedAt, null);
            }
            else if (status == ResultStatus.Finalized)
            {
                update = update
                    .Set(r => r.FinalizedAt, DateTime.UtcNow)
                    .Set(r => r.PublishedAt, null);
            }
            else if (status == ResultStatus.Published)
            {
                update = update.Set(r => r.PublishedAt, DateTime.UtcNow);
            }

            var res = await _mongoDb.Results.UpdateOneAsync(r => r.Id == resultId, update);
            if (res.MatchedCount > 0 && status == ResultStatus.Published && _evaluationSource != null)
            {
                await _evaluationSource.NotifyResultPublishedAsync(result.UserId, result.ExamId, result.TotalScore, result.MaxScore, result.IsPassed);
            }
            return res.MatchedCount > 0;
        }

        public async Task<bool> BulkPublishAsync(List<string> resultIds)
        {
            if (resultIds == null || resultIds.Count == 0) return false;
            var update = Builders<ExamResult>.Update
                .Set(r => r.Status, ResultStatus.Published)
                .Set(r => r.PublishedAt, DateTime.UtcNow);
            var res = await _mongoDb.Results.UpdateManyAsync(r => resultIds.Contains(r.Id), update);
            return res.MatchedCount > 0;
        }

        public async Task<bool> GradeQuestionAsync(string resultId, string questionId, double score, string evaluatorId, string? overrideReason = null)
        {
            var result = await GetResultByIdAsync(resultId);
            if (result == null) return false;

            var q = result.Breakdown.FirstOrDefault(b => b.QuestionId == questionId);
            if (q == null) return false;

            q.Score = Math.Clamp(score, -q.MaxScore, q.MaxScore);
            q.IsCorrect = score > 0;
            q.NeedsManualGrading = false;
            q.EvaluatorId = evaluatorId;
            if (overrideReason != null) q.OverrideReason = overrideReason;

            result.TotalScore = Math.Round(Math.Max(0, result.Breakdown.Sum(x => x.Score)), 2);
            result.IsPassed = result.TotalScore >= result.PassingScore;
            if (!result.Breakdown.Any(b => b.NeedsManualGrading) && result.Status == ResultStatus.AwaitingManualGrading)
            {
                result.Status = ResultStatus.Calculated;
                result.CalculatedAt = DateTime.UtcNow;
            }

            var dbResult = await _mongoDb.Results.ReplaceOneAsync(r => r.Id == resultId, result);
            return dbResult.MatchedCount > 0;
        }

        public async Task<bool> FinalizeManualReviewAsync(string resultId)
        {
            var result = await GetResultByIdAsync(resultId);
            if (result == null) return false;

            result.TotalScore = Math.Round(Math.Max(0, result.Breakdown.Sum(x => x.Score)), 2);
            result.IsPassed = result.TotalScore >= result.PassingScore;
            result.Status = ResultStatus.Finalized;
            result.FinalizedAt = DateTime.UtcNow;

            var res = await _mongoDb.Results.ReplaceOneAsync(r => r.Id == resultId, result);
            return res.MatchedCount > 0;
        }

        // ==========================================
        // 3. CROSS-ENTITY QUERIES & RANKING
        // ==========================================
        public async Task<List<ExamResult>> GetResultsByUserAsync(string userId) =>
            await _mongoDb.Results.Find(r => r.UserId == userId).SortByDescending(r => r.CalculatedAt).ToListAsync();

        public async Task<List<ExamResult>> GetResultsByExamAsync(string examId) =>
            await _mongoDb.Results.Find(r => r.ExamId == examId).SortByDescending(r => r.TotalScore).ToListAsync();

        public async Task<ExamResult?> GetResultByAttemptAsync(string attemptId) =>
            await _mongoDb.Results.Find(r => r.AttemptId == attemptId).FirstOrDefaultAsync();

        public async Task<(int Rank, int TotalStudents, double Percentile)?> GetStudentRankAsync(string resultId)
        {
            var target = await GetResultByIdAsync(resultId);
            if (target == null) return null;

            var peers = await GetResultsByExamAsync(target.ExamId);
            if (peers.Count == 0) return (1, 1, 100.0);

            int higherCount = peers.Count(p => p.TotalScore > target.TotalScore);
            int rank = higherCount + 1;
            int belowOrEqual = peers.Count(p => p.TotalScore <= target.TotalScore);
            double percentile = Math.Round((belowOrEqual / (double)peers.Count) * 100.0, 2);
            return (rank, peers.Count, percentile);
        }

        public async Task<List<object>> GetExamLeaderboardAsync(string examId, int topN = 20)
        {
            var results = await _mongoDb.Results
                .Find(r => r.ExamId == examId && (r.Status == ResultStatus.Published || r.Status == ResultStatus.Finalized || r.Status == ResultStatus.Calculated))
                .SortByDescending(r => r.TotalScore)
                .ThenBy(r => r.CalculatedAt)
                .Limit(Math.Clamp(topN, 1, 100))
                .ToListAsync();

            return results.Select((r, idx) => (object)new
            {
                rank = idx + 1,
                resultId = r.Id,
                userId = r.UserId,
                totalScore = r.TotalScore,
                maxScore = r.MaxScore,
                percentage = Math.Round(r.Percentage, 2),
                isPassed = r.IsPassed
            }).ToList();
        }

        // ==========================================
        // 4. EXPORTS & PDF GENERATION
        // ==========================================
        public async Task<List<ExamResult>> QueryFilteredResultsAsync(ExportFilterRequest req)
        {
            var fb = Builders<ExamResult>.Filter;
            var filters = new List<FilterDefinition<ExamResult>>();

            if (!string.IsNullOrWhiteSpace(req.ExamId))
                filters.Add(fb.Eq(r => r.ExamId, req.ExamId));
            if (req.StartDate.HasValue)
                filters.Add(fb.Gte(r => r.CalculatedAt, req.StartDate.Value));
            if (req.EndDate.HasValue)
                filters.Add(fb.Lte(r => r.CalculatedAt, req.EndDate.Value));

            var filter = filters.Count > 0 ? fb.And(filters) : fb.Empty;
            return await _mongoDb.Results.Find(filter).SortByDescending(r => r.TotalScore).ToListAsync();
        }

        public async Task<byte[]> ExportResultsCsvAsync(ExportFilterRequest req)
        {
            var results = await QueryFilteredResultsAsync(req);
            var sb = new StringBuilder();
            sb.AppendLine("ResultId,AttemptId,ExamId,UserId,TotalScore,MaxScore,Percentage,IsPassed,Status,CalculatedAt");
            foreach (var r in results)
            {
                sb.AppendLine(string.Join(",",
                    r.Id,
                    r.AttemptId,
                    r.ExamId,
                    r.UserId,
                    r.TotalScore.ToString(CultureInfo.InvariantCulture),
                    r.MaxScore.ToString(CultureInfo.InvariantCulture),
                    Math.Round(r.Percentage, 2).ToString(CultureInfo.InvariantCulture),
                    r.IsPassed ? "true" : "false",
                    r.Status.ToString(),
                    r.CalculatedAt?.ToString("O") ?? string.Empty));
            }
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public async Task<byte[]> ExportResultsPdfAsync(ExportFilterRequest req)
        {
            var results = await QueryFilteredResultsAsync(req);
            var lines = new List<string>
            {
                $"Generated At (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}",
                $"Total Records: {results.Count}",
                $"Filter ExamId: {req.ExamId ?? "ALL"}",
                "----------------------------------------------------------------"
            };

            foreach (var r in results.Take(35))
            {
                lines.Add($"{r.UserId} | {r.ExamId} | Score: {r.TotalScore}/{r.MaxScore} ({Math.Round(r.Percentage, 1)}%) | {(r.IsPassed ? "PASS" : "FAIL")} | {r.Status}");
            }

            return PdfDocumentHelper.BuildDocument("Exam Results Export Report", lines);
        }

        public byte[] GenerateResultReportPdf(ExamResult result)
        {
            var lines = new List<string>
            {
                $"Result ID: {result.Id}",
                $"Attempt ID: {result.AttemptId}",
                $"Exam ID: {result.ExamId}",
                $"Candidate ID: {result.UserId}",
                $"Status: {result.Status}",
                $"Score: {result.TotalScore} / {result.MaxScore} ({Math.Round(result.Percentage, 2)}%)",
                $"Passing Score: {result.PassingScore}",
                $"Outcome: {(result.IsPassed ? "PASSED" : "FAILED")}",
                $"Calculated At: {result.CalculatedAt?.ToString("u") ?? "N/A"}",
                "----------------------------------------------------------------",
                "Question Breakdown:"
            };

            foreach (var b in result.Breakdown.Take(28))
            {
                lines.Add($"Q: {b.QuestionId} | Score: {b.Score}/{b.MaxScore} | {(b.NeedsManualGrading ? "PENDING MANUAL" : (b.IsCorrect ? "CORRECT" : "INCORRECT"))}");
            }

            return PdfDocumentHelper.BuildDocument("Official Exam Score Report", lines);
        }

        public byte[] GenerateCertificatePdf(Certificate cert, ExamResult? result)
        {
            var lines = new List<string>
            {
                "This is to certify that candidate:",
                $"  User ID: {cert.UserId}",
                $"has successfully passed the examination:",
                $"  Exam ID: {cert.ExamId}",
                $"  Score Achieved: {(result != null ? $"{result.TotalScore}/{result.MaxScore} ({Math.Round(result.Percentage, 2)}%)" : "Verified Passing Grade")}",
                $"Issued On (UTC): {cert.IssuedAt:yyyy-MM-dd HH:mm:ss}",
                $"Certificate ID: {cert.Id}",
                $"Verification Code: {cert.VerificationCode}"
            };

            return PdfDocumentHelper.BuildDocument("Certificate of Achievement", lines);
        }

        // ==========================================
        // 5. CERTIFICATES
        // ==========================================
        public async Task<Certificate> GenerateCertificateAsync(string resultId)
        {
            var existing = await _mongoDb.Certificates.Find(c => c.ResultId == resultId).FirstOrDefaultAsync();
            if (existing != null) return existing;

            var result = await GetResultByIdAsync(resultId);
            if (result == null || !result.IsPassed)
                throw new InvalidOperationException("Result not found or exam not passed.");

            var cert = new Certificate
            {
                ResultId = resultId,
                UserId = result.UserId,
                ExamId = result.ExamId
            };
            cert.CertificateUrl = $"/api/v1/certificates/{cert.Id}/download";

            await _mongoDb.Certificates.InsertOneAsync(cert);
            return cert;
        }

        public async Task<List<Certificate>> GetAllCertificatesAsync() =>
            await _mongoDb.Certificates.Find(_ => true).SortByDescending(c => c.IssuedAt).ToListAsync();

        public async Task<Certificate?> GetCertificateByIdAsync(string certIdOrResultId) =>
            await _mongoDb.Certificates.Find(c => c.Id == certIdOrResultId || c.ResultId == certIdOrResultId).FirstOrDefaultAsync();

        public async Task<List<Certificate>> GetCertificatesByUserAsync(string userId) =>
            await _mongoDb.Certificates.Find(c => c.UserId == userId).SortByDescending(c => c.IssuedAt).ToListAsync();

        public async Task<Certificate?> VerifyCertificateAsync(string code) =>
            await _mongoDb.Certificates.Find(c => c.VerificationCode == code).FirstOrDefaultAsync();
    }
}
