using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using VideoClassesService.Models;

namespace VideoClassesService.Services
{
    public class CourseManagementService
    {
        private readonly MongoDbService _mongoDb;
        private readonly ILogger<CourseManagementService> _logger;

        public CourseManagementService(MongoDbService mongoDb, ILogger<CourseManagementService> logger)
        {
            _mongoDb = mongoDb;
            _logger = logger;
        }

        #region Course Management

        public async Task<Course> CreateCourseAsync(Course course)
        {
            course.CreatedAt = DateTime.UtcNow;
            await _mongoDb.Courses.InsertOneAsync(course);
            return course;
        }

        public async Task<List<Course>> GetAllCoursesAsync(CourseStatus? status = null, CourseCategory? category = null)
        {
            var filterBuilder = Builders<Course>.Filter;
            var filters = new List<FilterDefinition<Course>>();

            if (status.HasValue)
                filters.Add(filterBuilder.Eq(c => c.Status, status.Value));
            if (category.HasValue)
                filters.Add(filterBuilder.Eq(c => c.Category, category.Value));

            var filter = filters.Count > 0 ? filterBuilder.And(filters) : filterBuilder.Empty;
            return await _mongoDb.Courses.Find(filter).SortByDescending(c => c.CreatedAt).ToListAsync();
        }

        public async Task<Course?> GetCourseByIdAsync(string courseId) =>
            await _mongoDb.Courses.Find(c => c.Id == courseId).FirstOrDefaultAsync();

        public async Task<bool> UpdateCourseAsync(string courseId, Course updatedCourse)
        {
            var existing = await GetCourseByIdAsync(courseId);
            if (existing == null) return false;
            updatedCourse.Id = existing.Id;
            updatedCourse.CreatedAt = existing.CreatedAt;
            updatedCourse.UpdatedAt = DateTime.UtcNow;
            var result = await _mongoDb.Courses.ReplaceOneAsync(c => c.Id == courseId, updatedCourse);
            return result.MatchedCount > 0;
        }

        public async Task<bool> DeleteCourseAsync(string courseId)
        {
            var course = await GetCourseByIdAsync(courseId);
            if (course != null)
            {
                foreach (var chapterId in course.ChapterIds)
                {
                    await DeleteChapterAsync(chapterId);
                }
            }

            var result = await _mongoDb.Courses.DeleteOneAsync(c => c.Id == courseId);
            return result.DeletedCount > 0;
        }

        public async Task<bool> PublishCourseAsync(string courseId)
        {
            var update = Builders<Course>.Update.Set(c => c.Status, CourseStatus.Published).Set(c => c.UpdatedAt, DateTime.UtcNow);
            var result = await _mongoDb.Courses.UpdateOneAsync(c => c.Id == courseId, update);
            return result.MatchedCount > 0;
        }

        #endregion

        #region Chapter Management

        public async Task<Chapter> CreateChapterAsync(Chapter chapter)
        {
            chapter.CreatedAt = DateTime.UtcNow;
            await _mongoDb.Chapters.InsertOneAsync(chapter);

            var update = Builders<Course>.Update.AddToSet(c => c.ChapterIds, chapter.Id);
            await _mongoDb.Courses.UpdateOneAsync(c => c.Id == chapter.CourseId, update);

            return chapter;
        }

        public async Task<List<Chapter>> GetChaptersByCourseAsync(string courseId) =>
            await _mongoDb.Chapters.Find(c => c.CourseId == courseId).SortBy(c => c.OrderIndex).ToListAsync();

        public async Task<Chapter?> GetChapterByIdAsync(string chapterId) =>
            await _mongoDb.Chapters.Find(c => c.Id == chapterId).FirstOrDefaultAsync();

        public async Task<bool> UpdateChapterAsync(string chapterId, Chapter updatedChapter)
        {
            var existing = await GetChapterByIdAsync(chapterId);
            if (existing == null) return false;
            updatedChapter.Id = existing.Id;
            updatedChapter.CreatedAt = existing.CreatedAt;
            var result = await _mongoDb.Chapters.ReplaceOneAsync(c => c.Id == chapterId, updatedChapter);
            return result.MatchedCount > 0;
        }

        public async Task<bool> DeleteChapterAsync(string chapterId)
        {
            var chapter = await GetChapterByIdAsync(chapterId);
            if (chapter != null)
            {
                foreach (var lessonId in chapter.LessonIds)
                {
                    await DeleteLessonAsync(lessonId);
                }

                var update = Builders<Course>.Update.Pull(c => c.ChapterIds, chapterId);
                await _mongoDb.Courses.UpdateOneAsync(c => c.Id == chapter.CourseId, update);
            }

            var result = await _mongoDb.Chapters.DeleteOneAsync(c => c.Id == chapterId);
            return result.DeletedCount > 0;
        }

        #endregion

        #region Lesson Management

        public async Task<Lesson> CreateLessonAsync(Lesson lesson)
        {
            lesson.CreatedAt = DateTime.UtcNow;
            await _mongoDb.Lessons.InsertOneAsync(lesson);

            var update = Builders<Chapter>.Update.AddToSet(c => c.LessonIds, lesson.Id);
            await _mongoDb.Chapters.UpdateOneAsync(c => c.Id == lesson.ChapterId, update);

            return lesson;
        }

        public async Task<List<Lesson>> GetLessonsByChapterAsync(string chapterId) =>
            await _mongoDb.Lessons.Find(l => l.ChapterId == chapterId).SortBy(l => l.OrderIndex).ToListAsync();

        public async Task<Lesson?> GetLessonByIdAsync(string lessonId) =>
            await _mongoDb.Lessons.Find(l => l.Id == lessonId).FirstOrDefaultAsync();

        public async Task<bool> UpdateLessonAsync(string lessonId, Lesson updatedLesson)
        {
            var existing = await GetLessonByIdAsync(lessonId);
            if (existing == null) return false;
            updatedLesson.Id = existing.Id;
            updatedLesson.CreatedAt = existing.CreatedAt;
            var result = await _mongoDb.Lessons.ReplaceOneAsync(l => l.Id == lessonId, updatedLesson);
            return result.MatchedCount > 0;
        }

        public async Task<bool> DeleteLessonAsync(string lessonId)
        {
            var lesson = await GetLessonByIdAsync(lessonId);
            if (lesson != null)
            {
                var update = Builders<Chapter>.Update.Pull(c => c.LessonIds, lessonId);
                await _mongoDb.Chapters.UpdateOneAsync(c => c.Id == lesson.ChapterId, update);
            }

            var result = await _mongoDb.Lessons.DeleteOneAsync(l => l.Id == lessonId);
            return result.DeletedCount > 0;
        }

        public async Task<bool> IncrementViewCountAsync(string lessonId)
        {
            var update = Builders<Lesson>.Update.Inc(l => l.ViewCount, 1);
            var result = await _mongoDb.Lessons.UpdateOneAsync(l => l.Id == lessonId, update);
            return result.MatchedCount > 0;
        }

        #endregion

        #region Search

        public async Task<List<Course>> SearchCoursesAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return await GetAllCoursesAsync();

            var escaped = Regex.Escape(query.Trim());
            var filter = Builders<Course>.Filter.Or(
                Builders<Course>.Filter.Regex(c => c.Title, new BsonRegularExpression(escaped, "i")),
                Builders<Course>.Filter.Regex(c => c.Description, new BsonRegularExpression(escaped, "i")),
                Builders<Course>.Filter.AnyIn(c => c.Tags, new[] { query.Trim() })
            );

            return await _mongoDb.Courses.Find(filter).ToListAsync();
        }

        #endregion
    }
}
