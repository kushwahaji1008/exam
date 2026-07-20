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
            {
                filters.Add(filterBuilder.Eq(c => c.Status, status.Value));
            }

            if (category.HasValue)
            {
                filters.Add(filterBuilder.Eq(c => c.Category, category.Value));
            }

            var filter = filters.Count > 0 ? filterBuilder.And(filters) : filterBuilder.Empty;
            return await _mongoDb.Courses.Find(filter).SortByDescending(c => c.CreatedAt).ToListAsync();
        }

        public async Task<Course?> GetCourseByIdAsync(string courseId)
        {
            return await _mongoDb.Courses.Find(c => c.Id == courseId).FirstOrDefaultAsync();
        }

        public async Task<bool> UpdateCourseAsync(string courseId, Course updatedCourse)
        {
            updatedCourse.UpdatedAt = DateTime.UtcNow;
            var result = await _mongoDb.Courses.ReplaceOneAsync(c => c.Id == courseId, updatedCourse);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> DeleteCourseAsync(string courseId)
        {
            // Delete associated chapters and lessons
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
            var update = Builders<Course>.Update.Set(c => c.Status, CourseStatus.Published);
            var result = await _mongoDb.Courses.UpdateOneAsync(c => c.Id == courseId, update);
            return result.ModifiedCount > 0;
        }

        #endregion

        #region Chapter Management

        public async Task<Chapter> CreateChapterAsync(Chapter chapter)
        {
            chapter.CreatedAt = DateTime.UtcNow;
            await _mongoDb.Chapters.InsertOneAsync(chapter);

            // Add chapter to course
            var update = Builders<Course>.Update.Push(c => c.ChapterIds, chapter.Id);
            await _mongoDb.Courses.UpdateOneAsync(c => c.Id == chapter.CourseId, update);

            return chapter;
        }

        public async Task<List<Chapter>> GetChaptersByCourseAsync(string courseId)
        {
            return await _mongoDb.Chapters.Find(c => c.CourseId == courseId)
                .SortBy(c => c.OrderIndex)
                .ToListAsync();
        }

        public async Task<Chapter?> GetChapterByIdAsync(string chapterId)
        {
            return await _mongoDb.Chapters.Find(c => c.Id == chapterId).FirstOrDefaultAsync();
        }

        public async Task<bool> UpdateChapterAsync(string chapterId, Chapter updatedChapter)
        {
            var result = await _mongoDb.Chapters.ReplaceOneAsync(c => c.Id == chapterId, updatedChapter);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> DeleteChapterAsync(string chapterId)
        {
            var chapter = await GetChapterByIdAsync(chapterId);
            if (chapter != null)
            {
                // Delete all lessons in this chapter
                foreach (var lessonId in chapter.LessonIds)
                {
                    await DeleteLessonAsync(lessonId);
                }

                // Remove from course
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

            // Add lesson to chapter
            var update = Builders<Chapter>.Update.Push(c => c.LessonIds, lesson.Id);
            await _mongoDb.Chapters.UpdateOneAsync(c => c.Id == lesson.ChapterId, update);

            return lesson;
        }

        public async Task<List<Lesson>> GetLessonsByChapterAsync(string chapterId)
        {
            return await _mongoDb.Lessons.Find(l => l.ChapterId == chapterId)
                .SortBy(l => l.OrderIndex)
                .ToListAsync();
        }

        public async Task<Lesson?> GetLessonByIdAsync(string lessonId)
        {
            return await _mongoDb.Lessons.Find(l => l.Id == lessonId).FirstOrDefaultAsync();
        }

        public async Task<bool> UpdateLessonAsync(string lessonId, Lesson updatedLesson)
        {
            var result = await _mongoDb.Lessons.ReplaceOneAsync(l => l.Id == lessonId, updatedLesson);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> DeleteLessonAsync(string lessonId)
        {
            var lesson = await GetLessonByIdAsync(lessonId);
            if (lesson != null)
            {
                // Remove from chapter
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
            return result.ModifiedCount > 0;
        }

        #endregion

        #region Search

        public async Task<List<Course>> SearchCoursesAsync(string query)
        {
            var filter = Builders<Course>.Filter.Or(
                Builders<Course>.Filter.Regex(c => c.Title, new MongoDB.Bson.BsonRegularExpression(query, "i")),
                Builders<Course>.Filter.Regex(c => c.Description, new MongoDB.Bson.BsonRegularExpression(query, "i")),
                Builders<Course>.Filter.AnyIn(c => c.Tags, new[] { query })
            );

            return await _mongoDb.Courses.Find(filter).ToListAsync();
        }

        #endregion
    }
}