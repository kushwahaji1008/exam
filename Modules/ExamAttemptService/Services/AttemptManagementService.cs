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

        public async Task<ExamAttempt> StartExamAsync(string examId, string studentId, string studentName)
        {
            // Check if student has already started this exam
            var existingAttempt = await _mongoDb.ExamAttempts
                .Find(a => a.ExamId == examId && a.StudentId == studentId && a.Status == AttemptStatus.InProgress)
                .FirstOrDefaultAsync();

            if (existingAttempt != null)
            {
                return existingAttempt;
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
            return attempt;
        }

        public async Task<ExamAttempt?> GetAttemptByIdAsync(string attemptId)
        {
            return await _mongoDb.ExamAttempts.Find(a => a.Id == attemptId).FirstOrDefaultAsync();
        }

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

        public async Task<bool> SaveAnswerAsync(string attemptId, Answer answer)
        {
            var attempt = await GetAttemptByIdAsync(attemptId);
            if (attempt == null || attempt.Status != AttemptStatus.InProgress)
            {
                return false;
            }

            // Remove existing answer for this question if any
            attempt.Answers.RemoveAll(a => a.QuestionId == answer.QuestionId);
            
            // Add new answer
            answer.AnsweredAt = DateTime.UtcNow;
            attempt.Answers.Add(answer);

            var update = Builders<ExamAttempt>.Update.Set(a => a.Answers, attempt.Answers);
            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);

            return result.ModifiedCount > 0;
        }

        public async Task<bool> ToggleFlagAsync(string attemptId, string questionId, bool flagged)
        {
            var attempt = await GetAttemptByIdAsync(attemptId);
            if (attempt == null) return false;

            attempt.QuestionFlags[questionId] = flagged;

            var update = Builders<ExamAttempt>.Update.Set(a => a.QuestionFlags, attempt.QuestionFlags);
            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);

            return result.ModifiedCount > 0;
        }

        public async Task<bool> LogActivityAsync(string attemptId, string activity)
        {
            var logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {activity}";
            var update = Builders<ExamAttempt>.Update.Push(a => a.ActivityLog, logEntry);
            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);

            return result.ModifiedCount > 0;
        }

        public async Task<bool> SubmitExamAsync(string attemptId, bool autoSubmit = false)
        {
            var timeSpent = await CalculateTimeSpentAsync(attemptId);

            var update = Builders<ExamAttempt>.Update
                .Set(a => a.Status, AttemptStatus.Submitted)
                .Set(a => a.SubmittedAt, DateTime.UtcNow)
                .Set(a => a.ActualEndTime, DateTime.UtcNow)
                .Set(a => a.AutoSubmitted, autoSubmit)
                .Set(a => a.TimeSpentMinutes, timeSpent);

            var result = await _mongoDb.ExamAttempts.UpdateOneAsync(a => a.Id == attemptId, update);
            return result.ModifiedCount > 0;
        }

        private async Task<int> CalculateTimeSpentAsync(string attemptId)
        {
            var attempt = await GetAttemptByIdAsync(attemptId);
            if (attempt == null) return 0;

            var endTime = DateTime.UtcNow;
            var timeSpan = endTime - attempt.StartedAt;
            return (int)timeSpan.TotalMinutes;
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
                Score = attempt.Score,
                Percentage = attempt.Percentage,
                Result = attempt.Result,
                TotalQuestions = attempt.Answers.Count,
                AnsweredQuestions = attempt.Answers.Count(a => !string.IsNullOrEmpty(a.SelectedOption) || 
                                                              a.SelectedOptions.Any() || 
                                                              !string.IsNullOrEmpty(a.TextAnswer) || 
                                                              !string.IsNullOrEmpty(a.CodeAnswer))
            };
        }
    }
}