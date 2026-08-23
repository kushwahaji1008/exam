using MongoDB.Driver;
using ExamAttemptService.Models;

namespace ExamAttemptService.Services
{
    public class AttemptManagementService
    {
        private readonly MongoDbService _mongoDb;

        public AttemptManagementService(MongoDbService mongoDb)
        {
            _mongoDb = mongoDb;
        }

        #region 1. Attempts (CRUD)

        public async Task<List<ExamAttempt>> GetAllAttemptsAsync()
        {
            return await _mongoDb.ExamAttempts.Find(_ => true).SortByDescending(a => a.StartedAt).ToListAsync();
        }

        public async Task<ExamAttempt> StartExamAsync(string examId, string studentId, string studentName)
        {
            var existingAttempt = await _mongoDb.ExamAttempts
                .Find(a => a.ExamId == examId && a.StudentId == studentId && a.Status == AttemptStatus.InProgress)
                .FirstOrDefaultAsync();

            if (existingAttempt != null) return existingAttempt;

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
            
            return attempt;
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
            if (attempt == null || attempt.Status != AttemptStatus.Paused) return false;

            var pauseDurationSeconds = attempt.LastPausedAt.HasValue 
                ? (int)(DateTime.UtcNow - attempt.LastPausedAt.Value).TotalSeconds 
                : 0;

            var update = Builders<ExamAttempt>.Update
                .Set(a => a.Status, AttemptStatus.InProgress)
                .Set(a => a.LastPausedAt, null)
                .Inc(a => a.TotalPausedSeconds, pauseDurationSeconds);

            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);
            if (result.ModifiedCount > 0)
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

            var timeSpent = CalculateTimeSpentMinutes(attempt);

            var update = Builders<ExamAttempt>.Update
                .Set(a => a.Status, AttemptStatus.Submitted)
                .Set(a => a.SubmittedAt, DateTime.UtcNow)
                .Set(a => a.ActualEndTime, DateTime.UtcNow)
                .Set(a => a.AutoSubmitted, autoSubmit)
                .Set(a => a.TimeSpentMinutes, timeSpent);

            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);
            
            if (result.ModifiedCount > 0)
            {
                var eventType = autoSubmit ? EventType.ForceSubmitted : EventType.Submitted;
                await LogActivityEventAsync(attemptId, eventType, "Exam submitted");
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

        public async Task<bool> SaveAnswerAsync(string attemptId, Answer answer)
        {
            var attempt = await GetAttemptByIdAsync(attemptId);
            if (attempt == null || attempt.Status != AttemptStatus.InProgress) return false;

            // Remove existing answer for this question to replace it
            attempt.Answers.RemoveAll(a => a.QuestionId == answer.QuestionId);
            
            answer.AnsweredAt = DateTime.UtcNow;
            answer.LastModifiedAt = DateTime.UtcNow;
            attempt.Answers.Add(answer);

            var update = Builders<ExamAttempt>.Update.Set(a => a.Answers, attempt.Answers);
            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);

            if (result.ModifiedCount > 0)
            {
                await LogActivityEventAsync(attemptId, EventType.AnswerSaved, "Answer saved", answer.QuestionId);
                return true;
            }
            return false;
        }

        public async Task<bool> ClearAnswerAsync(string attemptId, string questionId)
        {
            var update = Builders<ExamAttempt>.Update.PullFilter(a => a.Answers, ans => ans.QuestionId == questionId);
            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);
            
            if (result.ModifiedCount > 0)
            {
                await LogActivityEventAsync(attemptId, EventType.AnswerSaved, "Answer cleared", questionId);
                return true;
            }
            return false;
        }

        #endregion

        #region 4. Navigation

        public async Task<bool> ToggleFlagAsync(string attemptId, string questionId, bool flagged)
        {
            var attempt = await GetAttemptByIdAsync(attemptId);
            if (attempt == null) return false;

            attempt.QuestionFlags[questionId] = flagged;

            var update = Builders<ExamAttempt>.Update.Set(a => a.QuestionFlags, attempt.QuestionFlags);
            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);

            if (result.ModifiedCount > 0)
            {
                await LogActivityEventAsync(attemptId, EventType.FlagToggled, $"Question flag set to {flagged}", questionId);
                return true;
            }
            return false;
        }

        public async Task<bool> RecordNavigationAsync(string attemptId, string questionId)
        {
            // Add the question to the VisitedQuestions HashSet
            var update = Builders<ExamAttempt>.Update.AddToSet(a => a.VisitedQuestions, questionId);
            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);
            
            // We usually don't log a full ActivityEvent for every navigation tick to save DB space, 
            // but the VisitedQuestions HashSet tracks where they've been.
            return result.ModifiedCount > 0 || result.MatchedCount > 0;
        }

        public async Task<NavigationStateDto?> GetNavigationStateAsync(string attemptId)
        {
            var attempt = await GetAttemptByIdAsync(attemptId);
            if (attempt == null) return null;

            var dto = new NavigationStateDto
            {
                AttemptId = attemptId,
                TotalTimeRemainingSeconds = 0 // Calculate this based on your exam config service if needed
            };

            // Combine all known question IDs from answers, flags, and visits
            var allQuestionIds = attempt.VisitedQuestions
                .Union(attempt.Answers.Select(a => a.QuestionId))
                .Union(attempt.QuestionFlags.Keys)
                .Distinct();

            foreach (var qId in allQuestionIds)
            {
                dto.Questions.Add(new QuestionNavigationInfo
                {
                    QuestionId = qId,
                    IsVisited = attempt.VisitedQuestions.Contains(qId),
                    IsAnswered = attempt.Answers.Any(a => a.QuestionId == qId),
                    IsFlagged = attempt.QuestionFlags.TryGetValue(qId, out bool flagged) && flagged
                });
            }

            return dto;
        }

        #endregion

        #region 5. Timer

        public async Task<bool> SyncTimerAsync(string attemptId, int clientRemainingSeconds, string? currentQuestionId)
        {
            var update = Builders<ExamAttempt>.Update.Set(a => a.LastSyncedAt, DateTime.UtcNow);
            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);
            
            if (currentQuestionId != null)
            {
                await RecordNavigationAsync(attemptId, currentQuestionId);
            }
            return result.ModifiedCount > 0;
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

        #region 6. Candidate Attempts & Logs

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

        public async Task<List<AttemptEvent>> GetAttemptEventsAsync(string attemptId)
        {
            var attempt = await GetAttemptByIdAsync(attemptId);
            return attempt?.ActivityLog ?? new List<AttemptEvent>();
        }

        // Overload for backward compatibility with old log controller
        public async Task<bool> LogActivityAsync(string attemptId, string activity)
        {
            return await LogActivityEventAsync(attemptId, EventType.WindowBlurred, activity); // Default to blurred/generic
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

            return result.ModifiedCount > 0;
        }

        #endregion

        #region 8. Admin Interventions

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
                .Set(a => a.Status, AttemptStatus.Evaluated) // Or back to Submitted depending on your flow
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

        private int CalculateTimeSpentMinutes(ExamAttempt attempt)
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
                TotalQuestions = attempt.Answers.Count, // Could be adjusted if Exam schema determines total
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