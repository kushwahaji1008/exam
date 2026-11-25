using ExamAttemptService.Models;
using MongoDB.Driver;

namespace ExamAttemptService.Services
{
    public interface IAttemptExamProvider
    {
        Task<AttemptExamMetadata?> GetExamMetadataAsync(string examId);
        Task<List<AttemptQuestionDto>> GetSanitizedQuestionsAsync(IReadOnlyList<string> questionIds);
    }

    public interface IAttemptSubmittedHandler
    {
        Task OnAttemptSubmittedAsync(string attemptId);
    }

    public class AttemptManagementService
    {
        private readonly MongoDbService _mongoDb;
        private readonly IAttemptExamProvider? _examProvider;
        private readonly IAttemptSubmittedHandler? _submittedHandler;

        public AttemptManagementService(
            MongoDbService mongoDb,
            IAttemptExamProvider? examProvider = null,
            IAttemptSubmittedHandler? submittedHandler = null)
        {
            _mongoDb = mongoDb;
            _examProvider = examProvider;
            _submittedHandler = submittedHandler;
        }

        #region 1. Attempts (CRUD & Validation)

        public async Task<List<ExamAttempt>> GetAllAttemptsAsync()
        {
            return await _mongoDb.ExamAttempts.Find(_ => true).SortByDescending(a => a.StartedAt).ToListAsync();
        }

        public async Task<(bool Success, string Message, ExamAttempt? Attempt)> CreateAndValidateAttemptAsync(
            string examId, string studentId, string studentName)
        {
            // Return existing in-progress/paused attempt idempotently
            var existingInProgress = await _mongoDb.ExamAttempts
                .Find(a => a.ExamId == examId && a.StudentId == studentId &&
                           (a.Status == AttemptStatus.InProgress || a.Status == AttemptStatus.Paused))
                .FirstOrDefaultAsync();
            if (existingInProgress != null)
                return (true, "Resuming existing active attempt.", existingInProgress);

            if (_examProvider != null)
            {
                var exam = await _examProvider.GetExamMetadataAsync(examId);
                if (exam == null)
                    return (false, "Exam not found.", null);

                var validStatuses = new[] { "Published", "Scheduled", "Active" };
                if (!validStatuses.Contains(exam.Status, StringComparer.OrdinalIgnoreCase))
                    return (false, $"Exam is currently {exam.Status} and cannot be started.", null);

                if (exam.BlockedStudents.Contains(studentId))
                    return (false, "You are blocked from taking this exam.", null);

                if (exam.AllowedStudents.Count > 0 && !exam.AllowedStudents.Contains(studentId))
                    return (false, "You are not registered for this exam.", null);

                if (exam.ScheduledStartTime.HasValue && DateTime.UtcNow < exam.ScheduledStartTime.Value &&
                    !exam.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                    return (false, $"Exam has not started yet. Scheduled for {exam.ScheduledStartTime.Value:u}.", null);

                if (exam.ScheduledEndTime.HasValue && DateTime.UtcNow > exam.ScheduledEndTime.Value)
                    return (false, "Exam window has already ended.", null);
            }

            var attempt = new ExamAttempt
            {
                ExamId = examId,
                StudentId = studentId,
                StudentName = studentName,
                StartedAt = DateTime.UtcNow,
                Status = AttemptStatus.InProgress
            };

            await _mongoDb.ExamAttempts.InsertOneAsync(attempt);
            await LogActivityEventAsync(attempt.Id, EventType.Started, "Exam attempt initialized");

            return (true, "Attempt created.", attempt);
        }

        public async Task<ExamAttempt> StartExamAsync(string examId, string studentId, string studentName)
        {
            var (success, message, attempt) = await CreateAndValidateAttemptAsync(examId, studentId, studentName);
            if (!success || attempt == null)
                throw new InvalidOperationException(message);
            return attempt;
        }

        public async Task<bool> MarkAttemptStartedAsync(string attemptId)
        {
            var attempt = await GetAttemptByIdAsync(attemptId);
            if (attempt == null) return false;
            if (attempt.Status == AttemptStatus.NotStarted)
            {
                var update = Builders<ExamAttempt>.Update
                    .Set(a => a.Status, AttemptStatus.InProgress)
                    .Set(a => a.StartedAt, DateTime.UtcNow);
                var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);
                if (result.MatchedCount > 0)
                    await LogActivityEventAsync(attemptId, EventType.Started, "Exam timer started");
                return result.MatchedCount > 0;
            }
            return attempt.Status is AttemptStatus.InProgress or AttemptStatus.Paused;
        }

        public async Task<ExamAttempt?> GetAttemptByIdAsync(string attemptId)
        {
            return await _mongoDb.ExamAttempts.Find(a => a.Id == attemptId).FirstOrDefaultAsync();
        }

        public async Task<bool> DeleteAttemptAsync(string attemptId)
        {
            var result = await _mongoDb.ExamAttempts.DeleteOneAsync(a => a.Id == attemptId);
            return result.DeletedCount > 0;
        }

        #endregion

        #region 2. Start/End Lifecycle

        public async Task<bool> PauseAttemptAsync(string attemptId)
        {
            var attempt = await GetAttemptByIdAsync(attemptId);
            if (attempt == null || attempt.Status != AttemptStatus.InProgress) return false;

            var update = Builders<ExamAttempt>.Update
                .Set(a => a.Status, AttemptStatus.Paused)
                .Set(a => a.LastPausedAt, DateTime.UtcNow);

            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);
            if (result.ModifiedCount > 0)
            {
                await LogActivityEventAsync(attemptId, EventType.Paused, "Exam paused");
                return true;
            }
            return false;
        }

        public async Task<bool> ResumeAttemptAsync(string attemptId)
        {
            var attempt = await GetAttemptByIdAsync(attemptId);
            if (attempt == null) return false;
            if (attempt.Status == AttemptStatus.InProgress) return true;
            if (attempt.Status != AttemptStatus.Paused) return false;

            var pauseDurationSeconds = attempt.LastPausedAt.HasValue
                ? Math.Max(0, (int)(DateTime.UtcNow - attempt.LastPausedAt.Value).TotalSeconds)
                : 0;

            var update = Builders<ExamAttempt>.Update
                .Set(a => a.Status, AttemptStatus.InProgress)
                .Set(a => a.LastPausedAt, null)
                .Inc(a => a.TotalPausedSeconds, pauseDurationSeconds);

            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);
            if (result.MatchedCount > 0)
            {
                await LogActivityEventAsync(attemptId, EventType.Resumed, $"Exam resumed after {pauseDurationSeconds}s pause");
                return true;
            }
            return false;
        }

        public async Task<bool> SubmitExamAsync(string attemptId, bool autoSubmit = false)
        {
            var attempt = await GetAttemptByIdAsync(attemptId);
            if (attempt == null) return false;

            if (attempt.Status is AttemptStatus.Submitted or AttemptStatus.Evaluated or AttemptStatus.Terminated or AttemptStatus.Invalidated)
                return false;

            var timeSpent = CalculateTimeSpentMinutes(attempt);

            // Atomic status transition to prevent concurrent double submission
            var filter = Builders<ExamAttempt>.Filter.And(
                Builders<ExamAttempt>.Filter.Eq(a => a.Id, attemptId),
                Builders<ExamAttempt>.Filter.In(a => a.Status, new[] { AttemptStatus.NotStarted, AttemptStatus.InProgress, AttemptStatus.Paused })
            );

            var update = Builders<ExamAttempt>.Update
                .Set(a => a.Status, AttemptStatus.Submitted)
                .Set(a => a.SubmittedAt, DateTime.UtcNow)
                .Set(a => a.ActualEndTime, DateTime.UtcNow)
                .Set(a => a.AutoSubmitted, autoSubmit)
                .Set(a => a.TimeSpentMinutes, timeSpent);

            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(filter, update);
            if (result.ModifiedCount > 0)
            {
                var eventType = autoSubmit ? EventType.ForceSubmitted : EventType.Submitted;
                await LogActivityEventAsync(attemptId, eventType, autoSubmit ? "Exam force/auto submitted" : "Exam submitted");

                if (_submittedHandler != null)
                {
                    await _submittedHandler.OnAttemptSubmittedAsync(attemptId);
                }
                return true;
            }
            return false;
        }

        public async Task<bool> TerminateAttemptAsync(string attemptId, string reason)
        {
            var update = Builders<ExamAttempt>.Update
                .Set(a => a.Status, AttemptStatus.Terminated)
                .Set(a => a.TerminationReason, reason)
                .Set(a => a.ActualEndTime, DateTime.UtcNow);

            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);
            if (result.ModifiedCount > 0)
            {
                await LogActivityEventAsync(attemptId, EventType.Terminated, $"Exam terminated: {reason}");
                return true;
            }
            return false;
        }

        #endregion

        #region 3. Questions & Answers

        public async Task<(bool Found, List<AttemptQuestionDto> Questions)> GetAttemptQuestionsAsync(string attemptId)
        {
            var attempt = await GetAttemptByIdAsync(attemptId);
            if (attempt == null) return (false, new List<AttemptQuestionDto>());
            if (_examProvider == null) return (true, new List<AttemptQuestionDto>());

            var exam = await _examProvider.GetExamMetadataAsync(attempt.ExamId);
            if (exam == null || exam.QuestionIds.Count == 0)
                return (true, new List<AttemptQuestionDto>());

            var questions = await _examProvider.GetSanitizedQuestionsAsync(exam.QuestionIds);
            // Preserve exam question order
            var byId = questions.ToDictionary(q => q.Id);
            var ordered = exam.QuestionIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
            return (true, ordered);
        }

        public async Task<AttemptQuestionDto?> GetAttemptQuestionAsync(string attemptId, string questionId)
        {
            var (found, questions) = await GetAttemptQuestionsAsync(attemptId);
            if (!found) return null;
            await RecordNavigationAsync(attemptId, questionId);
            return questions.FirstOrDefault(q => q.Id == questionId);
        }

        public async Task<string?> NavigateQuestionAsync(string attemptId, string currentQuestionId, int stepOffset)
        {
            await RecordNavigationAsync(attemptId, currentQuestionId);
            if (_examProvider == null) return null;

            var attempt = await GetAttemptByIdAsync(attemptId);
            var exam = attempt != null ? await _examProvider.GetExamMetadataAsync(attempt.ExamId) : null;
            if (exam == null || exam.QuestionIds.Count == 0) return null;

            int idx = exam.QuestionIds.IndexOf(currentQuestionId);
            if (idx < 0) return exam.QuestionIds.FirstOrDefault();

            int targetIdx = idx + stepOffset;
            if (targetIdx < 0 || targetIdx >= exam.QuestionIds.Count) return null;

            var nextId = exam.QuestionIds[targetIdx];
            await RecordNavigationAsync(attemptId, nextId);
            return nextId;
        }

        public async Task<bool> SaveAnswerAsync(string attemptId, Answer answer)
        {
            var attempt = await GetAttemptByIdAsync(attemptId);
            if (attempt == null || attempt.Status != AttemptStatus.InProgress) return false;

            attempt.Answers.RemoveAll(a => a.QuestionId == answer.QuestionId);
            answer.AnsweredAt = DateTime.UtcNow;
            answer.LastModifiedAt = DateTime.UtcNow;
            attempt.Answers.Add(answer);
            attempt.VisitedQuestions.Add(answer.QuestionId);

            var update = Builders<ExamAttempt>.Update
                .Set(a => a.Answers, attempt.Answers)
                .Set(a => a.VisitedQuestions, attempt.VisitedQuestions);
            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);

            if (result.MatchedCount > 0)
            {
                await LogActivityEventAsync(attemptId, EventType.AnswerSaved, "Answer saved", answer.QuestionId);
                return true;
            }
            return false;
        }

        public async Task<bool> SyncAnswersBulkAsync(string attemptId, List<SyncAnswerItem> items, string? currentQuestionId = null)
        {
            var attempt = await GetAttemptByIdAsync(attemptId);
            if (attempt == null || attempt.Status != AttemptStatus.InProgress) return false;

            foreach (var item in items)
            {
                attempt.Answers.RemoveAll(a => a.QuestionId == item.QuestionId);
                attempt.Answers.Add(new Answer
                {
                    QuestionId = item.QuestionId,
                    SelectedOption = item.SelectedOption,
                    SelectedOptions = item.SelectedOptions ?? new List<string>(),
                    TextAnswer = item.TextAnswer,
                    CodeAnswer = item.CodeAnswer,
                    AnsweredAt = DateTime.UtcNow,
                    LastModifiedAt = DateTime.UtcNow
                });
                attempt.VisitedQuestions.Add(item.QuestionId);
            }

            if (!string.IsNullOrEmpty(currentQuestionId))
                attempt.VisitedQuestions.Add(currentQuestionId);

            var update = Builders<ExamAttempt>.Update
                .Set(a => a.Answers, attempt.Answers)
                .Set(a => a.VisitedQuestions, attempt.VisitedQuestions)
                .Set(a => a.LastSyncedAt, DateTime.UtcNow);

            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);
            if (result.MatchedCount > 0)
                await LogActivityEventAsync(attemptId, EventType.Sync, $"Synchronized {items.Count} answers", currentQuestionId);

            return result.MatchedCount > 0;
        }

        public async Task<bool> ClearAnswerAsync(string attemptId, string questionId)
        {
            var attempt = await GetAttemptByIdAsync(attemptId);
            if (attempt == null || attempt.Status != AttemptStatus.InProgress) return false;

            var update = Builders<ExamAttempt>.Update.PullFilter(a => a.Answers, ans => ans.QuestionId == questionId);
            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);

            if (result.MatchedCount > 0)
            {
                await LogActivityEventAsync(attemptId, EventType.AnswerSaved, "Answer cleared", questionId);
                return true;
            }
            return false;
        }

        #endregion

        #region 4. Navigation & Timer

        public async Task<bool> ToggleFlagAsync(string attemptId, string questionId, bool flagged)
        {
            var attempt = await GetAttemptByIdAsync(attemptId);
            if (attempt == null) return false;

            attempt.QuestionFlags[questionId] = flagged;
            var update = Builders<ExamAttempt>.Update.Set(a => a.QuestionFlags, attempt.QuestionFlags);
            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);

            if (result.MatchedCount > 0)
            {
                await LogActivityEventAsync(attemptId, EventType.FlagToggled, $"Question flag set to {flagged}", questionId);
                return true;
            }
            return false;
        }

        public async Task<bool> RecordNavigationAsync(string attemptId, string questionId)
        {
            var update = Builders<ExamAttempt>.Update.AddToSet(a => a.VisitedQuestions, questionId);
            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);
            return result.MatchedCount > 0;
        }

        public async Task<int> CalculateTimeRemainingSecondsAsync(ExamAttempt attempt)
        {
            if (attempt.Status is AttemptStatus.Submitted or AttemptStatus.Evaluated or AttemptStatus.Expired or AttemptStatus.Terminated or AttemptStatus.Invalidated)
                return 0;

            int baseDurationMinutes = 60;
            if (_examProvider != null)
            {
                var exam = await _examProvider.GetExamMetadataAsync(attempt.ExamId);
                if (exam != null && exam.DurationMinutes > 0)
                    baseDurationMinutes = exam.DurationMinutes;
            }

            var totalAllottedSeconds = (baseDurationMinutes + attempt.ExtraTimeGrantedMinutes) * 60;
            var referenceEnd = attempt.Status == AttemptStatus.Paused && attempt.LastPausedAt.HasValue
                ? attempt.LastPausedAt.Value
                : DateTime.UtcNow;

            var activeElapsed = Math.Max(0, (int)(referenceEnd - attempt.StartedAt).TotalSeconds - attempt.TotalPausedSeconds);
            return Math.Max(0, totalAllottedSeconds - activeElapsed);
        }

        public async Task<NavigationStateDto?> GetNavigationStateAsync(string attemptId)
        {
            var attempt = await GetAttemptByIdAsync(attemptId);
            if (attempt == null) return null;

            var examQuestionIds = _examProvider != null
                ? (await _examProvider.GetExamMetadataAsync(attempt.ExamId))?.QuestionIds ?? new List<string>()
                : new List<string>();

            var remainingSeconds = await CalculateTimeRemainingSecondsAsync(attempt);
            var dto = new NavigationStateDto
            {
                AttemptId = attemptId,
                TotalTimeRemainingSeconds = remainingSeconds
            };

            var allQuestionIds = examQuestionIds
                .Concat(attempt.VisitedQuestions)
                .Concat(attempt.Answers.Select(a => a.QuestionId))
                .Concat(attempt.QuestionFlags.Keys)
                .Distinct();

            foreach (var qId in allQuestionIds)
            {
                dto.Questions.Add(new QuestionNavigationInfo
                {
                    QuestionId = qId,
                    IsVisited = attempt.VisitedQuestions.Contains(qId),
                    IsAnswered = attempt.Answers.Any(a => a.QuestionId == qId &&
                        (!string.IsNullOrEmpty(a.SelectedOption) || a.SelectedOptions.Count > 0 || !string.IsNullOrEmpty(a.TextAnswer) || !string.IsNullOrEmpty(a.CodeAnswer))),
                    IsFlagged = attempt.QuestionFlags.TryGetValue(qId, out bool flagged) && flagged
                });
            }

            return dto;
        }

        public async Task<bool> SyncTimerAsync(string attemptId, int clientRemainingSeconds, string? currentQuestionId)
        {
            var update = Builders<ExamAttempt>.Update.Set(a => a.LastSyncedAt, DateTime.UtcNow);
            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);

            if (!string.IsNullOrEmpty(currentQuestionId))
                await RecordNavigationAsync(attemptId, currentQuestionId);

            return result.MatchedCount > 0;
        }

        public async Task<bool> ExtendTimeAsync(string attemptId, int extraMinutes, string reason)
        {
            var update = Builders<ExamAttempt>.Update.Inc(a => a.ExtraTimeGrantedMinutes, extraMinutes);
            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);

            if (result.ModifiedCount > 0)
            {
                await LogActivityEventAsync(attemptId, EventType.TimeExtended, $"Granted {extraMinutes} mins extra. Reason: {reason}");
                return true;
            }
            return false;
        }

        #endregion

        #region 5. Candidate Attempts & Logs

        public async Task<ExamAttempt?> GetActiveAttemptAsync(string examId, string studentId)
        {
            return await _mongoDb.ExamAttempts
                .Find(a => a.ExamId == examId && a.StudentId == studentId && a.Status == AttemptStatus.InProgress)
                .FirstOrDefaultAsync();
        }

        public async Task<List<ExamAttempt>> GetStudentAttemptsAsync(string studentId)
        {
            return await _mongoDb.ExamAttempts
                .Find(a => a.StudentId == studentId)
                .SortByDescending(a => a.StartedAt)
                .ToListAsync();
        }

        public async Task<List<ExamAttempt>> GetExamAttemptsAsync(string examId)
        {
            return await _mongoDb.ExamAttempts
                .Find(a => a.ExamId == examId)
                .SortByDescending(a => a.StartedAt)
                .ToListAsync();
        }

        public async Task<List<AttemptEvent>?> GetAttemptEventsAsync(string attemptId)
        {
            var attempt = await GetAttemptByIdAsync(attemptId);
            return attempt?.ActivityLog;
        }

        public async Task<bool> LogActivityAsync(string attemptId, string activity, EventType? eventType = null, string? questionId = null)
        {
            return await LogActivityEventAsync(attemptId, eventType ?? EventType.WindowBlurred, activity, questionId);
        }

        public async Task<bool> LogActivityEventAsync(string attemptId, EventType type, string description, string? questionId = null)
        {
            var newEvent = new AttemptEvent
            {
                Type = type,
                Description = description,
                Timestamp = DateTime.UtcNow,
                QuestionId = questionId
            };

            var update = Builders<ExamAttempt>.Update.Push(a => a.ActivityLog, newEvent);
            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);
            return result.MatchedCount > 0;
        }

        #endregion

        #region 6. Admin Interventions

        public async Task<bool> InvalidateAttemptAsync(string attemptId, string reason)
        {
            var update = Builders<ExamAttempt>.Update
                .Set(a => a.Status, AttemptStatus.Invalidated)
                .Set(a => a.InvalidationReason, reason);

            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);
            if (result.ModifiedCount > 0)
            {
                await LogActivityEventAsync(attemptId, EventType.Invalidated, $"Attempt invalidated: {reason}");
                return true;
            }
            return false;
        }

        public async Task<bool> RestoreAttemptAsync(string attemptId)
        {
            var update = Builders<ExamAttempt>.Update
                .Set(a => a.Status, AttemptStatus.Submitted)
                .Set(a => a.InvalidationReason, null);

            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);
            if (result.ModifiedCount > 0)
            {
                await LogActivityEventAsync(attemptId, EventType.Restored, "Attempt restored from invalidation");
                return true;
            }
            return false;
        }

        public async Task<bool> ReopenAttemptAsync(string attemptId)
        {
            var update = Builders<ExamAttempt>.Update
                .Set(a => a.Status, AttemptStatus.InProgress)
                .Set(a => a.SubmittedAt, null)
                .Set(a => a.ActualEndTime, null)
                .Set(a => a.AutoSubmitted, false);

            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);
            if (result.ModifiedCount > 0)
            {
                await LogActivityEventAsync(attemptId, EventType.Resumed, "Admin reopened exam for modifications");
                return true;
            }
            return false;
        }

        #endregion

        #region Helper Methods

        private static int CalculateTimeSpentMinutes(ExamAttempt attempt)
        {
            var endTime = DateTime.UtcNow;
            var totalTimeSpan = endTime - attempt.StartedAt;
            var actualActiveSeconds = totalTimeSpan.TotalSeconds - attempt.TotalPausedSeconds;
            return (int)TimeSpan.FromSeconds(Math.Max(0, actualActiveSeconds)).TotalMinutes;
        }

        public static AttemptDto ToAttemptDto(ExamAttempt attempt)
        {
            return new AttemptDto
            {
                Id = attempt.Id,
                ExamId = attempt.ExamId,
                StudentId = attempt.StudentId,
                StudentName = attempt.StudentName,
                StartedAt = attempt.StartedAt,
                SubmittedAt = attempt.SubmittedAt,
                Status = attempt.Status,
                TimeSpentMinutes = attempt.TimeSpentMinutes,
                ExtraTimeGrantedMinutes = attempt.ExtraTimeGrantedMinutes,
                Score = attempt.Score,
                Percentage = attempt.Percentage,
                Result = attempt.Result,
                TotalQuestions = attempt.Answers.Count,
                FlaggedQuestions = attempt.QuestionFlags.Count(f => f.Value),
                AnsweredQuestions = attempt.Answers.Count(a =>
                    !string.IsNullOrEmpty(a.SelectedOption) ||
                    a.SelectedOptions.Any() ||
                    !string.IsNullOrEmpty(a.TextAnswer) ||
                    !string.IsNullOrEmpty(a.CodeAnswer))
            };
        }

        #endregion
    }
}
