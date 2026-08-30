using MongoDB.Driver;
using MongoDB.Bson;
using ExamService.Models;

namespace ExamService.Services
{
    public class ExamManagementService
    {
        private readonly MongoDbService _mongoDb;

        public ExamManagementService(MongoDbService mongoDb)
        {
            _mongoDb = mongoDb;
        }

        #region 1. CORE CRUD OPERATIONS

        public async Task<Exam> CreateExamAsync(CreateExamRequest request, string userId)
        {
            var exam = new Exam
            {
                CourseId = request.CourseId,
                Title = request.Title,
                Description = request.Description,
                DurationMinutes = request.DurationMinutes,
                ScheduledStartTime = request.ScheduledStartTime,
                Type = request.Type,
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

            // If not an Admin, only show exams they created or active exams they are allowed to take
            if (role != "Admin" && role != "SuperAdmin")
            {
                filter = builder.Or(
                    builder.Eq(e => e.CreatedBy, userId),
                    builder.And(
                        builder.Eq(e => e.Status, ExamStatus.Active),
                        builder.Or(
                            builder.Size(e => e.AllowedStudents, 0), // Public exam
                            builder.AnyEq(e => e.AllowedStudents, userId) // Specifically allowed
                        )
                    )
                );
            }

            return await _mongoDb.Exams.Find(filter).SortByDescending(e => e.CreatedAt).ToListAsync();
        }

        public async Task<Exam?> GetExamByIdAsync(string examId)
        {
            return await _mongoDb.Exams.Find(e => e.Id == examId).FirstOrDefaultAsync();
        }

        public async Task<bool> UpdateExamAsync(string examId, Exam updatedExam)
        {
            updatedExam.UpdatedAt = DateTime.UtcNow;
            var result = await _mongoDb.Exams.ReplaceOneAsync(e => e.Id == examId, updatedExam);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> DeleteExamAsync(string examId)
        {
            // Soft delete by archiving, or hard delete based on your preference
            var result = await _mongoDb.Exams.DeleteOneAsync(e => e.Id == examId);
            return result.DeletedCount > 0;
        }

        #endregion

        #region 2. EXAM LIFECYCLE (Status & Cloning)

        public async Task<bool> ChangeExamStatusAsync(string examId, ExamStatus newStatus)
        {
            var update = Builders<Exam>.Update
                .Set(e => e.Status, newStatus)
                .Set(e => e.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == examId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<string?> CloneExamAsync(string examId, string userId)
        {
            var original = await GetExamByIdAsync(examId);
            if (original == null) return null;

            original.Id = Guid.NewGuid().ToString(); // Generate new ID
            original.Title = $"{original.Title} (Clone)";
            original.Status = ExamStatus.Draft;
            original.CreatedBy = userId;
            original.CreatedAt = DateTime.UtcNow;
            original.UpdatedAt = null;
            original.ParentExamId = null; // It's a clone, not a version

            await _mongoDb.Exams.InsertOneAsync(original);
            return original.Id;
        }

        public async Task<bool> ScheduleExamAsync(string examId, DateTime startTime)
        {
            var update = Builders<Exam>.Update
                .Set(e => e.ScheduledStartTime, startTime)
                .Set(e => e.Status, ExamStatus.Scheduled)
                .Set(e => e.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == examId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> ActivateExamAsync(string examId)
        {
            var update = Builders<Exam>.Update
                .Set(e => e.Status, ExamStatus.Active)
                .Set(e => e.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == examId, update);
            return result.ModifiedCount > 0;
        }

        #endregion

        #region 3. EXAM CONFIGURATION

        public async Task<bool> UpdateExamSettingsAsync(string examId, ExamSettings settings)
        {
            var update = Builders<Exam>.Update.Set(e => e.Settings, settings).Set(e => e.UpdatedAt, DateTime.UtcNow);
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == examId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> UpdateExamScheduleAsync(string examId, ScheduleRequest request)
        {
            var update = Builders<Exam>.Update
                .Set(e => e.ScheduledStartTime, request.StartTime)
                .Set(e => e.ScheduledEndTime, request.EndTime)
                .Set(e => e.Status, ExamStatus.Scheduled)
                .Set(e => e.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == examId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> UpdateExamInstructionsAsync(string examId, string instructionsHtml)
        {
            var update = Builders<Exam>.Update.Set(e => e.InstructionsHtml, instructionsHtml);
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == examId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> UpdateExamGradingAsync(string examId, ExamGrading grading)
        {
            var update = Builders<Exam>.Update.Set(e => e.Grading, grading);
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == examId, update);
            return result.ModifiedCount > 0;
        }

        #endregion

        #region 4. SECTIONS & QUESTIONS MANAGEMENT

        public async Task<bool> ReorderQuestionsAsync(string examId, List<string> orderedIds)
        {
            var update = Builders<Exam>.Update.Set(e => e.QuestionIds, orderedIds);
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == examId, update);
            return result.ModifiedCount > 0;
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

            var update = Builders<Exam>.Update.Push(e => e.Sections, newSection);
            await _mongoDb.Exams.UpdateOneAsync(e => e.Id == examId, update);
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

            var result = await _mongoDb.Exams.ReplaceOneAsync(e => e.Id == examId, exam);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> DeleteSectionAsync(string examId, string sectionId)
        {
            var update = Builders<Exam>.Update.PullFilter(e => e.Sections, s => s.Id == sectionId);
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == examId, update);
            return result.ModifiedCount > 0;
        }

        #endregion

        #region 5. CANDIDATES & ACCESS CONTROL

        public async Task<bool> AddCandidatesBulkAsync(string examId, List<string> userIds)
        {
            var update = Builders<Exam>.Update.AddToSetEach(e => e.AllowedStudents, userIds);
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == examId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> RemoveCandidateAsync(string examId, string userId)
        {
            var update = Builders<Exam>.Update.Pull(e => e.AllowedStudents, userId);
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == examId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> BlockCandidateAsync(string examId, string userId)
        {
            // Remove from allowed, add to blocked
            var update = Builders<Exam>.Update
                .Pull(e => e.AllowedStudents, userId)
                .AddToSet(e => e.BlockedStudents, userId);
            
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == examId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> AllowCandidateAsync(string examId, string userId)
        {
            // Remove from blocked, add to allowed
            var update = Builders<Exam>.Update
                .Pull(e => e.BlockedStudents, userId)
                .AddToSet(e => e.AllowedStudents, userId);
            
            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == examId, update);
            return result.ModifiedCount > 0;
        }

        #endregion

        #region 6. VERSIONING

        public async Task<List<ExamVersionDto>> GetExamVersionsAsync(string parentExamId)
        {
            var versions = await _mongoDb.Exams
                .Find(e => e.Id == parentExamId || e.ParentExamId == parentExamId)
                .SortByDescending(e => e.CreatedAt)
                .ToListAsync();

            return versions.Select(v => new ExamVersionDto
            {
                VersionId = v.Version,
                ExamId = v.Id,
                CreatedAt = v.CreatedAt,
                CreatedBy = v.CreatedBy,
                IsActiveVersion = v.IsLatestVersion
            }).ToList();
        }

        public async Task<string?> CreateNewVersionAsync(string examId, string userId)
        {
            var original = await GetExamByIdAsync(examId);
            if (original == null) return null;

            // Mark old version as not latest
            var updateOld = Builders<Exam>.Update.Set(e => e.IsLatestVersion, false);
            await _mongoDb.Exams.UpdateOneAsync(e => e.Id == examId, updateOld);

            // Create new version
            var newVersionValue = (double.Parse(original.Version) + 0.1).ToString("0.0");
            
            original.ParentExamId = original.ParentExamId ?? original.Id; // Keep track of the root exam
            original.Id = Guid.NewGuid().ToString(); 
            original.Version = newVersionValue;
            original.IsLatestVersion = true;
            original.Status = ExamStatus.Draft;
            original.CreatedBy = userId;
            original.CreatedAt = DateTime.UtcNow;
            original.UpdatedAt = null;

            await _mongoDb.Exams.InsertOneAsync(original);
            return original.Id;
        }

        #endregion

        #region 7. ANALYTICS (Mocked Logic - Connect to Attempts DB later)

        public async Task<ExamStatisticsDto?> GetExamStatisticsAsync(string examId)
        {
            var exam = await GetExamByIdAsync(examId);
            if (exam == null) return null;

            // NOTE: In a real scenario, you would query an 'ExamAttempts' collection here.
            // Returning mocked aggregations based on the requested DTO structure.
            return new ExamStatisticsDto
            {
                ExamId = examId,
                TotalAttempts = 150,
                TotalCompleted = 142,
                AverageScore = 78.5,
                HighestScore = 98.0,
                LowestScore = 32.5,
                PassRatePercentage = 85.0
            };
        }

        public async Task<QuestionAnalysisDto?> GetQuestionAnalysisAsync(string examId)
        {
            // MOCK: Query attempt data to find which questions students got wrong most often
            return new QuestionAnalysisDto
            {
                ToughestQuestions = new List<QuestionMetricDto>
                {
                    new QuestionMetricDto { QuestionId = "Q-104", AttemptCount = 142, CorrectCount = 20, SuccessRatePercentage = 14.1 }
                },
                EasiestQuestions = new List<QuestionMetricDto>
                {
                    new QuestionMetricDto { QuestionId = "Q-001", AttemptCount = 142, CorrectCount = 139, SuccessRatePercentage = 97.8 }
                }
            };
        }

        #endregion

        #region MAPPING UTILITIES

        public static ExamDto ToExamDto(Exam exam)
        {
            return new ExamDto
            {
                Id = exam.Id,
                ExamId = $"{new Random().Next(100000, 999999)}",
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

        public async Task<bool> CompleteExamAsync(string examId)
        {
            var update = Builders<Exam>.Update
                .Set(e => e.Status, ExamStatus.Completed)
                .Set(e => e.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == examId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<List<Exam>> GetUpcomingExamsAsync()
        {
            // Gets exams that are scheduled and in the future
            var filter = Builders<Exam>.Filter.And(
                Builders<Exam>.Filter.Eq(e => e.Status, ExamStatus.Scheduled),
                Builders<Exam>.Filter.Gt(e => e.ScheduledStartTime, DateTime.UtcNow)
            );

            return await _mongoDb.Exams.Find(filter).SortBy(e => e.ScheduledStartTime).ToListAsync();
        }

        public async Task<List<Exam>> GetActiveExamsAsync()
        {
            // Gets exams that are currently active
            var filter = Builders<Exam>.Filter.Eq(e => e.Status, ExamStatus.Active);
            return await _mongoDb.Exams.Find(filter).SortByDescending(e => e.CreatedAt).ToListAsync();
        }
    }
}