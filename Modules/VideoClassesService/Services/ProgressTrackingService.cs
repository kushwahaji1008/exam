using MongoDB.Driver;
using VideoClassesService.Models;

namespace VideoClassesService.Services
{
    public class ProgressTrackingService
    {
        private readonly MongoDbService _mongoDb;
        private readonly ILogger<ProgressTrackingService> _logger;

        public ProgressTrackingService(MongoDbService mongoDb, ILogger<ProgressTrackingService> logger)
        {
            _mongoDb = mongoDb;
            _logger = logger;
        }

        public async Task<StudentProgress> EnrollStudentAsync(string studentId, string courseId)
        {
            // Check if already enrolled
            var existing = await GetStudentProgressAsync(studentId, courseId);
            if (existing != null)
            {
                return existing;
            }

            var progress = new StudentProgress
            {
                StudentId = studentId,
                CourseId = courseId,
                EnrolledAt = DateTime.UtcNow
            };

            await _mongoDb.StudentProgress.InsertOneAsync(progress);

            // Increment course enrollment count
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
            var progress = await GetStudentProgressAsync(studentId, courseId);
            if (progress == null)
            {
                progress = await EnrollStudentAsync(studentId, courseId);
            }

            var lessonProgress = progress.LessonProgress.GetValueOrDefault(lessonId, new LessonProgress
            {
                LessonId = lessonId,
                TotalSeconds = totalSeconds
            });

            lessonProgress.WatchedSeconds = Math.Max(lessonProgress.WatchedSeconds, watchedSeconds);
            lessonProgress.TotalSeconds = totalSeconds;
            lessonProgress.ProgressPercentage = totalSeconds > 0 ? (lessonProgress.WatchedSeconds / (double)totalSeconds) * 100 : 0;
            lessonProgress.LastWatchedAt = DateTime.UtcNow;

            // Mark as completed if watched >= 90%
            if (lessonProgress.ProgressPercentage >= 90 && !lessonProgress.IsCompleted)
            {
                lessonProgress.IsCompleted = true;
                lessonProgress.CompletedAt = DateTime.UtcNow;
                progress.TotalLessonsCompleted++;
            }

            progress.LessonProgress[lessonId] = lessonProgress;
            progress.LastAccessedAt = DateTime.UtcNow;
            progress.CurrentLessonId = lessonId;

            // Calculate overall progress
            if (progress.TotalLessons > 0)
            {
                progress.CompletionPercentage = (progress.TotalLessonsCompleted / (double)progress.TotalLessons) * 100;
            }

            var update = Builders<StudentProgress>.Update
                .Set(p => p.LessonProgress, progress.LessonProgress)
                .Set(p => p.TotalLessonsCompleted, progress.TotalLessonsCompleted)
                .Set(p => p.CompletionPercentage, progress.CompletionPercentage)
                .Set(p => p.LastAccessedAt, progress.LastAccessedAt)
                .Set(p => p.CurrentLessonId, progress.CurrentLessonId);

            var result = await _mongoDb.StudentProgress.UpdateOneAsync(
                p => p.StudentId == studentId && p.CourseId == courseId,
                update
            );

            return result.ModifiedCount > 0;
        }

        public async Task<bool> AddNoteAsync(string studentId, string courseId, string lessonId, Note note)
        {
            var progress = await GetStudentProgressAsync(studentId, courseId);
            if (progress == null) return false;

            if (!progress.LessonProgress.ContainsKey(lessonId))
            {
                progress.LessonProgress[lessonId] = new LessonProgress { LessonId = lessonId };
            }

            progress.LessonProgress[lessonId].Notes.Add(note);

            var update = Builders<StudentProgress>.Update
                .Set(p => p.LessonProgress, progress.LessonProgress);

            var result = await _mongoDb.StudentProgress.UpdateOneAsync(
                p => p.StudentId == studentId && p.CourseId == courseId,
                update
            );

            return result.ModifiedCount > 0;
        }

        public async Task<bool> AddBookmarkAsync(string studentId, string courseId, string lessonId, int timestamp)
        {
            var progress = await GetStudentProgressAsync(studentId, courseId);
            if (progress == null) return false;

            if (!progress.LessonProgress.ContainsKey(lessonId))
            {
                progress.LessonProgress[lessonId] = new LessonProgress { LessonId = lessonId };
            }

            if (!progress.LessonProgress[lessonId].Bookmarks.Contains(timestamp))
            {
                progress.LessonProgress[lessonId].Bookmarks.Add(timestamp);
            }

            var update = Builders<StudentProgress>.Update
                .Set(p => p.LessonProgress, progress.LessonProgress);

            var result = await _mongoDb.StudentProgress.UpdateOneAsync(
                p => p.StudentId == studentId && p.CourseId == courseId,
                update
            );

            return result.ModifiedCount > 0;
        }

        public async Task<bool> RateCourseAsync(string studentId, string courseId, int rating, string? review)
        {
            var progress = await GetStudentProgressAsync(studentId, courseId);
            if (progress == null) return false;

            var oldRating = progress.Rating;
            progress.Rating = rating;
            progress.Review = review;

            var update = Builders<StudentProgress>.Update
                .Set(p => p.Rating, rating)
                .Set(p => p.Review, review);

            await _mongoDb.StudentProgress.UpdateOneAsync(
                p => p.StudentId == studentId && p.CourseId == courseId,
                update
            );

            // Update course average rating
            var course = await _mongoDb.Courses.Find(c => c.Id == courseId).FirstOrDefaultAsync();
            if (course != null)
            {
                if (oldRating.HasValue)
                {
                    // Update existing rating
                    course.AverageRating = ((course.AverageRating * course.TotalRatings) - oldRating.Value + rating) / course.TotalRatings;
                }
                else
                {
                    // New rating
                    course.TotalRatings++;
                    course.AverageRating = ((course.AverageRating * (course.TotalRatings - 1)) + rating) / course.TotalRatings;
                }

                var courseUpdate = Builders<Course>.Update
                    .Set(c => c.AverageRating, course.AverageRating)
                    .Set(c => c.TotalRatings, course.TotalRatings);

                await _mongoDb.Courses.UpdateOneAsync(c => c.Id == courseId, courseUpdate);
            }

            return true;
        }
    }
}