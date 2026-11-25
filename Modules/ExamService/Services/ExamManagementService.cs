using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ExamService.Models;
using MongoDB.Driver;

namespace ExamService.Services
{
    public interface IExamAnalyticsProvider
    {
        Task<ExamStatisticsDto> GetExamStatisticsAsync(Exam exam);
        Task<PerformanceMetricsDto> GetPerformanceMetricsAsync(Exam exam);
        Task<CompletionStatsDto> GetCompletionStatsAsync(Exam exam);
        Task<QuestionAnalysisDto> GetQuestionAnalysisAsync(Exam exam);
    }

    public class ExamManagementService
    {
        private readonly MongoDbService _mongoDb;
        private readonly IExamAnalyticsProvider? _analyticsProvider;

        public ExamManagementService(MongoDbService mongoDb, IExamAnalyticsProvider? analyticsProvider = null)
        {
            _mongoDb = mongoDb;
            _analyticsProvider = analyticsProvider;
        }

        private async Task<string> GenerateUniqueExamIdAsync()
        {
            for (int i = 0; i < 15; i++)
            {
                var candidate = $"EXM-{RandomNumberGenerator.GetInt32(100_000, 1_000_000)}";
                if (!await _mongoDb.Exams.Find(e => e.ExamId == candidate).AnyAsync())
                    return candidate;
            }
            return $"EXM-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        }

        #region 1. CORE CRUD OPERATIONS

        public async Task<Exam> CreateExamAsync(CreateExamRequest request, string userId)
        {
            var exam = new Exam
            {
                ExamId = await GenerateUniqueExamIdAsync(),
                CourseId = request.CourseId,
                Title = request.Title,
                Description = request.Description,
                DurationMinutes = request.DurationMinutes,
                ScheduledStartTime = request.ScheduledStartTime,
                ScheduledEndTime = request.ScheduledEndTime,
                Type = request.Type,
                QuestionIds = request.QuestionIds ?? new List<string>(),
                Settings = request.Settings ?? new ExamSettings(),
                Grading = request.Grading ?? new ExamGrading(),
                TotalMarks = request.TotalMarks,
                PassingMarks = request.PassingMarks,
                AllowedStudents = request.AllowedStudents ?? new List<string>(),
                InstructionsHtml = request.InstructionsHtml,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow,
                Status = ExamStatus.Draft,
                Version = "1.0",
                IsLatestVersion = true
            };

            await _mongoDb.Exams.InsertOneAsync(exam);
            return exam;
        }

        public async Task<List<Exam>> GetAllExamsAsync(string? userId, string? role)
        {
            var builder = Builders<Exam>.Filter;
            var filter = builder.Empty;

            if (role is "Admin" or "SuperAdmin")
            {
                return await _mongoDb.Exams.Find(filter).SortByDescending(e => e.CreatedAt).ToListAsync();
            }

            if (role == "Teacher")
            {
                filter = builder.Or(
                    builder.Eq(e => e.CreatedBy, userId),
                    builder.Ne(e => e.Status, ExamStatus.Draft)
                );
                return await _mongoDb.Exams.Find(filter).SortByDescending(e => e.CreatedAt).ToListAsync();
            }

            // Student view: published/scheduled/active/completed exams where student is allowed & not blocked
            filter = builder.And(
                builder.In(e => e.Status, new[] { ExamStatus.Published, ExamStatus.Scheduled, ExamStatus.Active, ExamStatus.Completed }),
                builder.Not(builder.AnyEq(e => e.BlockedStudents, userId ?? string.Empty)),
                builder.Or(
                    builder.Size(e => e.AllowedStudents, 0),
                    builder.AnyEq(e => e.AllowedStudents, userId ?? string.Empty)
                )
            );

            return await _mongoDb.Exams.Find(filter).SortByDescending(e => e.CreatedAt).ToListAsync();
        }

        public async Task<Exam?> GetExamByIdAsync(string examId)
        {
            return await _mongoDb.Exams.Find(e => e.ExamId == examId || e.Id == examId).FirstOrDefaultAsync();
        }

        public async Task<bool> UpdateExamAsync(string examId, Exam updatedExam)
        {
            var existing = await GetExamByIdAsync(examId);
            if (existing == null) return false;

            updatedExam.Id = existing.Id;
            updatedExam.ExamId = existing.ExamId;
            updatedExam.CreatedBy = existing.CreatedBy;
            updatedExam.CreatedAt = existing.CreatedAt;
            updatedExam.UpdatedAt = DateTime.UtcNow;

            var result = await _mongoDb.Exams.ReplaceOneAsync(e => e.Id == existing.Id, updatedExam);
            return result.MatchedCount > 0;
        }

        public async Task<bool> PatchExamAsync(string examId, object patchData)
        {
            var existing = await GetExamByIdAsync(examId);
            if (existing == null || patchData == null) return false;

            try
            {
                var json = JsonSerializer.Serialize(patchData);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (data == null || data.Count == 0) return false;

                var updates = new List<UpdateDefinition<Exam>>();
                foreach (var kvp in data)
                {
                    var v = kvp.Value;
                    switch (kvp.Key.ToLowerInvariant())
                    {
                        case "title" when v.ValueKind == JsonValueKind.String:
                            var t = v.GetString();
                            if (!string.IsNullOrWhiteSpace(t))
                                updates.Add(Builders<Exam>.Update.Set(e => e.Title, t));
                            break;
                        case "description" when v.ValueKind == JsonValueKind.String:
                            updates.Add(Builders<Exam>.Update.Set(e => e.Description, v.GetString() ?? string.Empty));
                            break;
                        case "courseid" when v.ValueKind == JsonValueKind.String:
                            updates.Add(Builders<Exam>.Update.Set(e => e.CourseId, v.GetString() ?? string.Empty));
                            break;
                        case "durationminutes" when v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var dur) && dur > 0:
                            updates.Add(Builders<Exam>.Update.Set(e => e.DurationMinutes, dur));
                            break;
                        case "totalmarks" when v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var tm) && tm >= 0:
                            updates.Add(Builders<Exam>.Update.Set(e => e.TotalMarks, tm));
                            break;
                        case "passingmarks" when v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var pm) && pm >= 0:
                            updates.Add(Builders<Exam>.Update.Set(e => e.PassingMarks, pm));
                            break;
                        case "instructionshtml":
                            updates.Add(Builders<Exam>.Update.Set(e => e.InstructionsHtml, v.ValueKind == JsonValueKind.Null ? null : v.GetString()));
                            break;
                        case "scheduledstarttime":
                            if (v.ValueKind == JsonValueKind.Null)
                                updates.Add(Builders<Exam>.Update.Set(e => e.ScheduledStartTime, null));
                            else if (v.ValueKind == JsonValueKind.String && v.TryGetDateTime(out var sst))
                                updates.Add(Builders<Exam>.Update.Set(e => e.ScheduledStartTime, sst));
                            break;
                        case "scheduledendtime":
                            if (v.ValueKind == JsonValueKind.Null)
                                updates.Add(Builders<Exam>.Update.Set(e => e.ScheduledEndTime, null));
                            else if (v.ValueKind == JsonValueKind.String && v.TryGetDateTime(out var set))
                                updates.Add(Builders<Exam>.Update.Set(e => e.ScheduledEndTime, set));
                            break;
                        case "type":
                            if (v.ValueKind == JsonValueKind.String && Enum.TryParse<ExamType>(v.GetString(), true, out var etStr))
                                updates.Add(Builders<Exam>.Update.Set(e => e.Type, etStr));
                            else if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var etInt) && Enum.IsDefined(typeof(ExamType), etInt))
                                updates.Add(Builders<Exam>.Update.Set(e => e.Type, (ExamType)etInt));
                            break;
                        case "status":
                            if (v.ValueKind == JsonValueKind.String && Enum.TryParse<ExamStatus>(v.GetString(), true, out var esStr))
                                updates.Add(Builders<Exam>.Update.Set(e => e.Status, esStr));
                            else if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var esInt) && Enum.IsDefined(typeof(ExamStatus), esInt))
                                updates.Add(Builders<Exam>.Update.Set(e => e.Status, (ExamStatus)esInt));
                            break;
                    }
                }

                if (updates.Count == 0) return false;
                updates.Add(Builders<Exam>.Update.Set(e => e.UpdatedAt, DateTime.UtcNow));

                var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == existing.Id, Builders<Exam>.Update.Combine(updates));
                return result.MatchedCount > 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeleteExamAsync(string examId)
        {
            var result = await _mongoDb.Exams.DeleteOneAsync(e => e.ExamId == examId || e.Id == examId);
            return result.DeletedCount > 0;
        }

        #endregion

        #region 2. EXAM LIFECYCLE (Status & Cloning)

        public async Task<bool> ChangeExamStatusAsync(string examId, ExamStatus newStatus)
        {
            var update = Builders<Exam>.Update
                .Set(e => e.Status, newStatus)
                .Set(e => e.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.ExamId == examId || e.Id == examId, update);
            return result.MatchedCount > 0;
        }

        public async Task<string?> CloneExamAsync(string examId, string userId)
        {
            var original = await GetExamByIdAsync(examId);
            if (original == null) return null;

            original.Id = Guid.NewGuid().ToString();
            original.ExamId = await GenerateUniqueExamIdAsync();
            original.Title = $"{original.Title} (Clone)";
            original.Status = ExamStatus.Draft;
            original.CreatedBy = userId;
            original.CreatedAt = DateTime.UtcNow;
            original.UpdatedAt = null;
            original.ParentExamId = null;
            original.Version = "1.0";
            original.IsLatestVersion = true;

            await _mongoDb.Exams.InsertOneAsync(original);
            return original.ExamId;
        }

        public async Task<bool> ScheduleExamAsync(string examId, DateTime startTime)
        {
            var update = Builders<Exam>.Update
                .Set(e => e.ScheduledStartTime, startTime)
                .Set(e => e.Status, ExamStatus.Scheduled)
                .Set(e => e.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.ExamId == examId, update);
            return result.MatchedCount > 0;
        }

        public async Task<bool> ActivateExamAsync(string examId) =>
            await ChangeExamStatusAsync(examId, ExamStatus.Active);

        #endregion

        #region 3. EXAM CONFIGURATION

        public async Task<bool> UpdateExamSettingsAsync(string examId, ExamSettings settings)
        {
            var update = Builders<Exam>.Update.Set(e => e.Settings, settings).Set(e => e.UpdatedAt, DateTime.UtcNow);
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.ExamId == examId, update);
            return result.MatchedCount > 0;
        }

        public async Task<bool> UpdateExamScheduleAsync(string examId, ScheduleRequest request)
        {
            var update = Builders<Exam>.Update
                .Set(e => e.ScheduledStartTime, request.StartTime)
                .Set(e => e.ScheduledEndTime, request.EndTime)
                .Set(e => e.Status, ExamStatus.Scheduled)
                .Set(e => e.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.ExamId == examId, update);
            return result.MatchedCount > 0;
        }

        public async Task<bool> UpdateExamInstructionsAsync(string examId, string instructionsHtml)
        {
            var update = Builders<Exam>.Update.Set(e => e.InstructionsHtml, instructionsHtml).Set(e => e.UpdatedAt, DateTime.UtcNow);
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.ExamId == examId, update);
            return result.MatchedCount > 0;
        }

        public async Task<bool> UpdateExamGradingAsync(string examId, ExamGrading grading)
        {
            var update = Builders<Exam>.Update.Set(e => e.Grading, grading).Set(e => e.UpdatedAt, DateTime.UtcNow);
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.ExamId == examId, update);
            return result.MatchedCount > 0;
        }

        #endregion

        #region 4. SECTIONS & QUESTIONS MANAGEMENT

        public async Task<bool> AddQuestionAsync(string examId, string questionId)
        {
            if (string.IsNullOrWhiteSpace(questionId)) return false;
            var update = Builders<Exam>.Update
                .AddToSet(e => e.QuestionIds, questionId)
                .Set(e => e.UpdatedAt, DateTime.UtcNow);
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.ExamId == examId, update);
            return result.MatchedCount > 0;
        }

        public async Task<bool> RemoveQuestionAsync(string examId, string questionId)
        {
            var exam = await GetExamByIdAsync(examId);
            if (exam == null) return false;

            bool removed = exam.QuestionIds.RemoveAll(q => q == questionId) > 0;
            foreach (var sec in exam.Sections)
            {
                if (sec.QuestionIds.RemoveAll(q => q == questionId) > 0)
                    removed = true;
            }
            if (!removed) return false;

            exam.UpdatedAt = DateTime.UtcNow;
            var result = await _mongoDb.Exams.ReplaceOneAsync(e => e.Id == exam.Id, exam);
            return result.MatchedCount > 0;
        }

        public async Task<bool> ReorderQuestionsAsync(string examId, List<string> orderedIds)
        {
            var update = Builders<Exam>.Update.Set(e => e.QuestionIds, orderedIds).Set(e => e.UpdatedAt, DateTime.UtcNow);
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.ExamId == examId, update);
            return result.MatchedCount > 0;
        }

        public async Task<List<string>?> RandomizeQuestionsAsync(string examId)
        {
            var exam = await GetExamByIdAsync(examId);
            if (exam == null) return null;

            var list = exam.QuestionIds.ToArray();
            RandomNumberGenerator.Shuffle(list.AsSpan());
            exam.QuestionIds = list.ToList();
            exam.UpdatedAt = DateTime.UtcNow;

            await _mongoDb.Exams.ReplaceOneAsync(e => e.Id == exam.Id, exam);
            return exam.QuestionIds;
        }

        public async Task<string?> AddSectionAsync(string examId, CreateSectionRequest request)
        {
            var exam = await GetExamByIdAsync(examId);
            if (exam == null) return null;

            var newSection = new ExamSection
            {
                Title = request.Title,
                Description = request.Description,
                OrderIndex = exam.Sections.Count
            };

            var update = Builders<Exam>.Update.Push(e => e.Sections, newSection).Set(e => e.UpdatedAt, DateTime.UtcNow);
            await _mongoDb.Exams.UpdateOneAsync(e => e.ExamId == examId, update);
            return newSection.Id;
        }

        public async Task<bool> UpdateSectionAsync(string examId, string sectionId, UpdateSectionRequest request)
        {
            var exam = await GetExamByIdAsync(examId);
            if (exam == null) return false;

            var section = exam.Sections.FirstOrDefault(s => s.Id == sectionId);
            if (section == null) return false;

            section.Title = request.Title;
            section.Description = request.Description;
            section.QuestionIds = request.QuestionIds;
            exam.UpdatedAt = DateTime.UtcNow;

            var result = await _mongoDb.Exams.ReplaceOneAsync(e => e.ExamId == examId, exam);
            return result.MatchedCount > 0;
        }

        public async Task<bool> DeleteSectionAsync(string examId, string sectionId)
        {
            var update = Builders<Exam>.Update.PullFilter(e => e.Sections, s => s.Id == sectionId).Set(e => e.UpdatedAt, DateTime.UtcNow);
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.ExamId == examId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> ReorderSectionsAsync(string examId, List<string> orderedIds)
        {
            var exam = await GetExamByIdAsync(examId);
            if (exam == null) return false;

            var reordered = orderedIds
                .Select(id => exam.Sections.FirstOrDefault(s => s.Id == id))
                .Where(s => s != null)
                .ToList();

            if (reordered.Count != exam.Sections.Count) return false;
            for (int i = 0; i < reordered.Count; i++) reordered[i]!.OrderIndex = i;

            var update = Builders<Exam>.Update.Set(e => e.Sections, reordered!).Set(e => e.UpdatedAt, DateTime.UtcNow);
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.ExamId == examId, update);
            return result.MatchedCount > 0;
        }

        #endregion

        #region 5. CANDIDATES & ACCESS CONTROL

        public async Task<bool> AddCandidatesBulkAsync(string examId, List<string> userIds)
        {
            if (userIds == null || userIds.Count == 0) return false;
            var update = Builders<Exam>.Update.AddToSetEach(e => e.AllowedStudents, userIds).Set(e => e.UpdatedAt, DateTime.UtcNow);
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.ExamId == examId, update);
            return result.MatchedCount > 0;
        }

        public async Task<bool> RemoveCandidateAsync(string examId, string userId)
        {
            var update = Builders<Exam>.Update.Pull(e => e.AllowedStudents, userId).Set(e => e.UpdatedAt, DateTime.UtcNow);
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.ExamId == examId, update);
            return result.MatchedCount > 0;
        }

        public async Task<bool> BlockCandidateAsync(string examId, string userId)
        {
            var update = Builders<Exam>.Update
                .Pull(e => e.AllowedStudents, userId)
                .AddToSet(e => e.BlockedStudents, userId)
                .Set(e => e.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.ExamId == examId, update);
            return result.MatchedCount > 0;
        }

        public async Task<bool> AllowCandidateAsync(string examId, string userId)
        {
            var update = Builders<Exam>.Update
                .Pull(e => e.BlockedStudents, userId)
                .AddToSet(e => e.AllowedStudents, userId)
                .Set(e => e.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.ExamId == examId, update);
            return result.MatchedCount > 0;
        }

        #endregion

        #region 6. VERSIONING

        public async Task<List<ExamVersionDto>> GetExamVersionsAsync(string examId)
        {
            var current = await GetExamByIdAsync(examId);
            var rootId = current?.ParentExamId ?? current?.ExamId ?? examId;

            var versions = await _mongoDb.Exams
                .Find(e => e.ExamId == rootId || e.ParentExamId == rootId || e.ExamId == examId)
                .SortByDescending(e => e.CreatedAt)
                .ToListAsync();

            return versions.Select(v => new ExamVersionDto
            {
                VersionId = v.Version,
                ExamId = v.ExamId,
                CreatedAt = v.CreatedAt,
                CreatedBy = v.CreatedBy,
                IsActiveVersion = v.IsLatestVersion
            }).ToList();
        }

        public async Task<Exam?> GetVersionAsync(string examId, string versionId)
        {
            var versions = await _mongoDb.Exams
                .Find(e => (e.ExamId == examId || e.ParentExamId == examId) && (e.Version == versionId || e.ExamId == versionId))
                .FirstOrDefaultAsync();
            if (versions != null) return versions;

            var current = await GetExamByIdAsync(examId);
            if (current?.ParentExamId != null)
            {
                return await _mongoDb.Exams
                    .Find(e => (e.ExamId == current.ParentExamId || e.ParentExamId == current.ParentExamId) && (e.Version == versionId || e.ExamId == versionId))
                    .FirstOrDefaultAsync();
            }
            return null;
        }

        public async Task<string?> CreateNewVersionAsync(string examId, string userId)
        {
            var original = await GetExamByIdAsync(examId);
            if (original == null) return null;

            var rootId = original.ParentExamId ?? original.ExamId;

            await _mongoDb.Exams.UpdateManyAsync(
                e => e.ExamId == rootId || e.ParentExamId == rootId,
                Builders<Exam>.Update.Set(e => e.IsLatestVersion, false));

            double currentVersion = double.TryParse(original.Version, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 1.0;
            var newVersionValue = (currentVersion + 0.1).ToString("0.0", CultureInfo.InvariantCulture);

            original.ParentExamId = rootId;
            original.Id = Guid.NewGuid().ToString();
            original.ExamId = await GenerateUniqueExamIdAsync();
            original.Version = newVersionValue;
            original.IsLatestVersion = true;
            original.Status = ExamStatus.Draft;
            original.CreatedBy = userId;
            original.CreatedAt = DateTime.UtcNow;
            original.UpdatedAt = null;

            await _mongoDb.Exams.InsertOneAsync(original);
            return original.ExamId;
        }

        public async Task<bool> PublishVersionAsync(string examId, string versionId)
        {
            var versionExam = await GetVersionAsync(examId, versionId);
            if (versionExam == null) return false;

            var rootId = versionExam.ParentExamId ?? versionExam.ExamId;
            await _mongoDb.Exams.UpdateManyAsync(
                e => e.ExamId == rootId || e.ParentExamId == rootId,
                Builders<Exam>.Update.Set(e => e.IsLatestVersion, false));

            var update = Builders<Exam>.Update
                .Set(e => e.IsLatestVersion, true)
                .Set(e => e.Status, ExamStatus.Published)
                .Set(e => e.UpdatedAt, DateTime.UtcNow);
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == versionExam.Id, update);
            return result.MatchedCount > 0;
        }

        public async Task<bool> RestoreVersionAsync(string examId, string versionId)
        {
            var targetVersion = await GetVersionAsync(examId, versionId);
            var currentExam = await GetExamByIdAsync(examId);
            if (targetVersion == null || currentExam == null) return false;

            var update = Builders<Exam>.Update
                .Set(e => e.Title, targetVersion.Title)
                .Set(e => e.Description, targetVersion.Description)
                .Set(e => e.DurationMinutes, targetVersion.DurationMinutes)
                .Set(e => e.QuestionIds, targetVersion.QuestionIds)
                .Set(e => e.Sections, targetVersion.Sections)
                .Set(e => e.Settings, targetVersion.Settings)
                .Set(e => e.Grading, targetVersion.Grading)
                .Set(e => e.InstructionsHtml, targetVersion.InstructionsHtml)
                .Set(e => e.TotalMarks, targetVersion.TotalMarks)
                .Set(e => e.PassingMarks, targetVersion.PassingMarks)
                .Set(e => e.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == currentExam.Id, update);
            return result.MatchedCount > 0;
        }

        #endregion

        #region 7. ANALYTICS

        public async Task<ExamStatisticsDto?> GetExamStatisticsAsync(string examId)
        {
            var exam = await GetExamByIdAsync(examId);
            if (exam == null) return null;

            if (_analyticsProvider != null)
                return await _analyticsProvider.GetExamStatisticsAsync(exam);

            return new ExamStatisticsDto { ExamId = exam.ExamId };
        }

        public async Task<PerformanceMetricsDto?> GetPerformanceMetricsAsync(string examId)
        {
            var exam = await GetExamByIdAsync(examId);
            if (exam == null) return null;

            if (_analyticsProvider != null)
                return await _analyticsProvider.GetPerformanceMetricsAsync(exam);

            return new PerformanceMetricsDto();
        }

        public async Task<CompletionStatsDto?> GetCompletionStatsAsync(string examId)
        {
            var exam = await GetExamByIdAsync(examId);
            if (exam == null) return null;

            if (_analyticsProvider != null)
                return await _analyticsProvider.GetCompletionStatsAsync(exam);

            return new CompletionStatsDto { AverageTimeToComplete = "0m 0s" };
        }

        public async Task<QuestionAnalysisDto?> GetQuestionAnalysisAsync(string examId)
        {
            var exam = await GetExamByIdAsync(examId);
            if (exam == null) return null;

            if (_analyticsProvider != null)
                return await _analyticsProvider.GetQuestionAnalysisAsync(exam);

            return new QuestionAnalysisDto();
        }

        #endregion

        #region MAPPING UTILITIES

        public static ExamDto ToExamDto(Exam exam)
        {
            return new ExamDto
            {
                Id = exam.Id,
                ExamId = exam.ExamId,
                CourseId = exam.CourseId,
                Title = exam.Title,
                Description = exam.Description,
                DurationMinutes = exam.DurationMinutes,
                ScheduledStartTime = exam.ScheduledStartTime,
                ScheduledEndTime = exam.ScheduledEndTime,
                Type = exam.Type,
                Status = exam.Status,
                CreatedBy = exam.CreatedBy,
                CreatedAt = exam.CreatedAt,
                Version = exam.Version,
                QuestionCount = exam.QuestionIds.Count + exam.Sections.Sum(s => s.QuestionIds.Count),
                SectionCount = exam.Sections.Count,
                TotalMarks = exam.TotalMarks,
                PassingMarks = exam.PassingMarks,
                Settings = exam.Settings,
                Grading = exam.Grading
            };
        }

        #endregion

        public async Task<bool> CompleteExamAsync(string examId) =>
            await ChangeExamStatusAsync(examId, ExamStatus.Completed);

        public async Task<List<Exam>> GetUpcomingExamsAsync()
        {
            var filter = Builders<Exam>.Filter.And(
                Builders<Exam>.Filter.In(e => e.Status, new[] { ExamStatus.Scheduled, ExamStatus.Published }),
                Builders<Exam>.Filter.Gt(e => e.ScheduledStartTime, DateTime.UtcNow)
            );

            return await _mongoDb.Exams.Find(filter).SortBy(e => e.ScheduledStartTime).ToListAsync();
        }

        public async Task<List<Exam>> GetActiveExamsAsync()
        {
            var filter = Builders<Exam>.Filter.Eq(e => e.Status, ExamStatus.Active);
            return await _mongoDb.Exams.Find(filter).SortByDescending(e => e.CreatedAt).ToListAsync();
        }
    }
}
