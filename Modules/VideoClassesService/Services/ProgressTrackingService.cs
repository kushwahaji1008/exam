using MongoDB.Driver;
using VideoClassesService.Models;

namespace VideoClassesService.Services
{
    public interface IVideoCoursePaymentHandler
    {
        Task<(bool Success, string Message)> DebitForVideoCourseAsync(
            string userId, string courseId, string courseTitle, decimal price);
    }

    public class ProgressTrackingService
    {
        private readonly MongoDbService _mongoDb;
        private readonly ILogger<ProgressTrackingService> _logger;
        private readonly IVideoCoursePaymentHandler? _paymentHandler;

        public ProgressTrackingService(
            MongoDbService mongoDb,
            ILogger<ProgressTrackingService> logger,
            IVideoCoursePaymentHandler? paymentHandler = null)
        {
            _mongoDb = mongoDb;
            _logger = logger;
            _paymentHandler = paymentHandler;
        }

        private async Task<int> CountCourseLessonsAsync(string courseId)
        {
            var chapterIds = await _mongoDb.Chapters
                .Find(c => c.CourseId == courseId)
                .Project(c => c.Id)
                .ToListAsync();

            if (chapterIds.Count == 0) return 0;

            return (int)await _mongoDb.Lessons
                .CountDocumentsAsync(l => chapterIds.Contains(l.ChapterId));
        }

        public async Task<StudentProgress> EnrollStudentAsync(string studentId, string courseId)
        {
            var existing = await GetStudentProgressAsync(studentId, courseId);
            if (existing != null)
            {
                return existing;
            }

            var course = await _mongoDb.Courses.Find(c => c.Id == courseId).FirstOrDefaultAsync();
            if (course != null && !course.IsFree && course.Price.HasValue && course.Price.Value > 0 && _paymentHandler != null)
            {
                var (paid, payMessage) = await _paymentHandler.DebitForVideoCourseAsync(
                    studentId, courseId, course.Title, course.Price.Value);
                if (!paid)
                    throw new InvalidOperationException(payMessage);
            }

            int totalLessons = await CountCourseLessonsAsync(courseId);

            var progress = new StudentProgress
            {
                StudentId = studentId,
                CourseId = courseId,
                TotalLessons = totalLessons,
                EnrolledAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow
            };

            await _mongoDb.StudentProgress.InsertOneAsync(progress);

            var update = Builders<Course>.Update.Inc(c => c.TotalStudentsEnrolled, 1);
            await _mongoDb.Courses.UpdateOneAsync(c => c.Id == courseId, update);

            return progress;
        }

        public async Task<StudentProgress?> GetStudentProgressAsync(string studentId, string courseId)
        {
            return await _mongoDb.StudentProgress
                .Find(p => p.StudentId == studentId && p.CourseId == courseId)
                .FirstOrDefaultAsync();
        }

        public async Task<List<StudentProgress>> GetStudentCoursesAsync(string studentId)
        {
            return await _mongoDb.StudentProgress
                .Find(p => p.StudentId == studentId)
                .SortByDescending(p => p.LastAccessedAt)
                .ToListAsync();
        }

        public async Task<bool> UpdateLessonProgressAsync(
            string studentId,
            string courseId,
            string lessonId,
            int watchedSeconds,
            int totalSeconds)
        {
            var progress = await GetStudentProgressAsync(studentId, courseId)
                           ?? await EnrollStudentAsync(studentId, courseId);

            int totalLessons = await CountCourseLessonsAsync(courseId);
            if (totalLessons > 0)
                progress.TotalLessons = totalLessons;

            var lessonProgress = progress.LessonProgress.GetValueOrDefault(lessonId, new LessonProgress
            {
                LessonId = lessonId,
                TotalSeconds = totalSeconds
            });

            lessonProgress.WatchedSeconds = Math.Max(lessonProgress.WatchedSeconds, watchedSeconds);
            lessonProgress.TotalSeconds = totalSeconds > 0 ? totalSeconds : lessonProgress.TotalSeconds;
            lessonProgress.ProgressPercentage = lessonProgress.TotalSeconds > 0
                ? Math.Min(100.0, (lessonProgress.WatchedSeconds / (double)lessonProgress.TotalSeconds) * 100)
                : 100.0;
            lessonProgress.LastWatchedAt = DateTime.UtcNow;

            if (lessonProgress.ProgressPercentage >= 90 && !lessonProgress.IsCompleted)
            {
                lessonProgress.IsCompleted = true;
                lessonProgress.CompletedAt = DateTime.UtcNow;
            }

            progress.LessonProgress[lessonId] = lessonProgress;
            progress.TotalLessonsCompleted = progress.LessonProgress.Values.Count(lp => lp.IsCompleted);
            progress.LastAccessedAt = DateTime.UtcNow;
            progress.CurrentLessonId = lessonId;

            if (progress.TotalLessons > 0)
            {
                progress.CompletionPercentage = Math.Min(100.0, Math.Round((progress.TotalLessonsCompleted / (double)progress.TotalLessons) * 100, 2));
            }
            else
            {
                progress.CompletionPercentage = lessonProgress.IsCompleted ? 100.0 : lessonProgress.ProgressPercentage;
            }

            var update = Builders<StudentProgress>.Update
                .Set(p => p.LessonProgress, progress.LessonProgress)
                .Set(p => p.TotalLessons, progress.TotalLessons)
                .Set(p => p.TotalLessonsCompleted, progress.TotalLessonsCompleted)
                .Set(p => p.CompletionPercentage, progress.CompletionPercentage)
                .Set(p => p.LastAccessedAt, progress.LastAccessedAt)
                .Set(p => p.CurrentLessonId, progress.CurrentLessonId);

            var result = await _mongoDb.StudentProgress.UpdateOneAsync(
                p => p.StudentId == studentId && p.CourseId == courseId,
                update
            );

            return result.MatchedCount > 0;
        }

        public async Task<bool> AddNoteAsync(string studentId, string courseId, string lessonId, Note note)
        {
            var progress = await GetStudentProgressAsync(studentId, courseId);
            if (progress == null) return false;

            if (!progress.LessonProgress.ContainsKey(lessonId))
                progress.LessonProgress[lessonId] = new LessonProgress { LessonId = lessonId };

            progress.LessonProgress[lessonId].Notes.Add(note);

            var update = Builders<StudentProgress>.Update.Set(p => p.LessonProgress, progress.LessonProgress);
            var result = await _mongoDb.StudentProgress.UpdateOneAsync(p => p.StudentId == studentId && p.CourseId == courseId, update);
            return result.MatchedCount > 0;
        }

        public async Task<bool> AddBookmarkAsync(string studentId, string courseId, string lessonId, int timestamp)
        {
            var progress = await GetStudentProgressAsync(studentId, courseId);
            if (progress == null) return false;

            if (!progress.LessonProgress.ContainsKey(lessonId))
                progress.LessonProgress[lessonId] = new LessonProgress { LessonId = lessonId };

            if (!progress.LessonProgress[lessonId].Bookmarks.Contains(timestamp))
                progress.LessonProgress[lessonId].Bookmarks.Add(timestamp);

            var update = Builders<StudentProgress>.Update.Set(p => p.LessonProgress, progress.LessonProgress);
            var result = await _mongoDb.StudentProgress.UpdateOneAsync(p => p.StudentId == studentId && p.CourseId == courseId, update);
            return result.MatchedCount > 0;
        }

        public async Task<bool> RateCourseAsync(string studentId, string courseId, int rating, string? review)
        {
            if (rating < 1 || rating > 5) return false;

            var progress = await GetStudentProgressAsync(studentId, courseId);
            if (progress == null) return false;

            var oldRating = progress.Rating;
            var update = Builders<StudentProgress>.Update
                .Set(p => p.Rating, rating)
                .Set(p => p.Review, review);

            await _mongoDb.StudentProgress.UpdateOneAsync(p => p.StudentId == studentId && p.CourseId == courseId, update);

            var course = await _mongoDb.Courses.Find(c => c.Id == courseId).FirstOrDefaultAsync();
            if (course != null)
            {
                if (oldRating.HasValue && course.TotalRatings > 0)
                {
                    course.AverageRating = ((course.AverageRating * course.TotalRatings) - oldRating.Value + rating) / course.TotalRatings;
                }
                else
                {
                    course.TotalRatings++;
                    course.AverageRating = ((course.AverageRating * (course.TotalRatings - 1)) + rating) / course.TotalRatings;
                }

                var courseUpdate = Builders<Course>.Update
                    .Set(c => c.AverageRating, Math.Round(course.AverageRating, 2))
                    .Set(c => c.TotalRatings, course.TotalRatings);

                await _mongoDb.Courses.UpdateOneAsync(c => c.Id == courseId, courseUpdate);
            }

            return true;
        }
    }
}
