using MongoDB.Driver;
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

        public async Task<Exam> CreateExamAsync(CreateExamRequest request, string createdBy)
        {
            var exam = new Exam
            {
                Title = request.Title,
                Description = request.Description,
                DurationMinutes = request.DurationMinutes,
                ScheduledStartTime = request.ScheduledStartTime,
                ScheduledEndTime = request.ScheduledStartTime?.AddMinutes(request.DurationMinutes),
                Type = request.Type,
                Status = ExamStatus.Draft,
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow,
                QuestionIds = request.QuestionIds,
                Settings = request.Settings,
                TotalMarks = request.TotalMarks,
                PassingMarks = request.PassingMarks,
                AllowedStudents = request.AllowedStudents,
                InstructionsHtml = request.InstructionsHtml
            };

            await _mongoDb.Exams.InsertOneAsync(exam);
            return exam;
        }

        public async Task<List<Exam>> GetAllExamsAsync(string? userId = null, string? role = null)
        {
            FilterDefinition<Exam> filter = Builders<Exam>.Filter.Empty;

            // If student, only show exams they're allowed to take
            if (role == "Student" && !string.IsNullOrEmpty(userId))
            {
                filter = Builders<Exam>.Filter.Or(
                    Builders<Exam>.Filter.Size(e => e.AllowedStudents, 0), // Public exams
                    Builders<Exam>.Filter.AnyEq(e => e.AllowedStudents, userId) // Assigned exams
                );
            }
            // If teacher, show exams they created
            else if (role == "Teacher" && !string.IsNullOrEmpty(userId))
            {
                filter = Builders<Exam>.Filter.Eq(e => e.CreatedBy, userId);
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
            var result = await _mongoDb.Exams.DeleteOneAsync(e => e.Id == examId);
            return result.DeletedCount > 0;
        }

        public async Task<bool> UpdateExamStatusAsync(string examId, ExamStatus newStatus)
        {
            var update = Builders<Exam>.Update
                .Set(e => e.Status, newStatus)
                .Set(e => e.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == examId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> ScheduleExamAsync(string examId, DateTime startTime)
        {
            var exam = await GetExamByIdAsync(examId);
            if (exam == null) return false;

            var endTime = startTime.AddMinutes(exam.DurationMinutes);

            var update = Builders<Exam>.Update
                .Set(e => e.ScheduledStartTime, startTime)
                .Set(e => e.ScheduledEndTime, endTime)
                .Set(e => e.Status, ExamStatus.Scheduled)
                .Set(e => e.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.Exams.UpdateOneAsync(e => e.Id == examId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> ActivateExamAsync(string examId)
        {
            return await UpdateExamStatusAsync(examId, ExamStatus.Active);
        }

        public async Task<bool> CompleteExamAsync(string examId)
        {
            return await UpdateExamStatusAsync(examId, ExamStatus.Completed);
        }

        public async Task<List<Exam>> GetUpcomingExamsAsync()
        {
            var now = DateTime.UtcNow;
            var filter = Builders<Exam>.Filter.And(
                Builders<Exam>.Filter.Eq(e => e.Status, ExamStatus.Scheduled),
                Builders<Exam>.Filter.Gte(e => e.ScheduledStartTime, now)
            );

            return await _mongoDb.Exams.Find(filter).SortBy(e => e.ScheduledStartTime).ToListAsync();
        }

        public async Task<List<Exam>> GetActiveExamsAsync()
        {
            var filter = Builders<Exam>.Filter.Eq(e => e.Status, ExamStatus.Active);
            return await _mongoDb.Exams.Find(filter).ToListAsync();
        }

        public static ExamDto ToExamDto(Exam exam)
        {
            return new ExamDto
            {
                Id = exam.Id,
                Title = exam.Title,
                Description = exam.Description,
                DurationMinutes = exam.DurationMinutes,
                ScheduledStartTime = exam.ScheduledStartTime,
                ScheduledEndTime = exam.ScheduledEndTime,
                Type = exam.Type,
                Status = exam.Status,
                CreatedBy = exam.CreatedBy,
                CreatedAt = exam.CreatedAt,
                QuestionCount = exam.QuestionIds.Count,
                TotalMarks = exam.TotalMarks,
                PassingMarks = exam.PassingMarks,
                Settings = exam.Settings
            };
        }
    }
}
