using MongoDB.Driver;
using CourseService.Models.V1;
using System.Text.Json;

namespace CourseService.Services
{
    public class CourseManagementService
    {
        private readonly MongoDbService _mongoDb;

        public CourseManagementService(MongoDbService mongoDb)
        {
            _mongoDb = mongoDb;
        }

        // ==========================================
        // COURSE CRUD
        // ==========================================
        public async Task<Course> CreateCourseAsync(CreateCourseRequest request, string userId)
        {
            var course = new Course
            {
                // 👇 YAHAN CHANGE HUA HAI: Auto-generate secure ID instead of trusting frontend
                CourseId = $"{new Random().Next(100000, 999999)}", 
                Title = request.Title,
                Description = request.Description,
                Level = request.Level,
                CoursePrice = request.CoursePrice, // Updated to CoursePrice
                CreatedBy = userId,
                InstructorIds = new List<string> { userId } // Creator is default instructor
            };

            await _mongoDb.Courses.InsertOneAsync(course);
            return course;
        }

        public async Task<List<Course>> GetAllCoursesAsync() => 
            await _mongoDb.Courses.Find(_ => true).SortByDescending(c => c.CreatedAt).ToListAsync();

        public async Task<Course?> GetCourseByIdAsync(string courseId) => 
            await _mongoDb.Courses.Find(c => c.CourseId == courseId).FirstOrDefaultAsync();

        public async Task<bool> DeleteCourseAsync(string courseId)
        {
            var result = await _mongoDb.Courses.DeleteOneAsync(c => c.CourseId == courseId);
            return result.DeletedCount > 0;
        }

        public async Task<bool> ChangeCourseStatusAsync(string courseId, CourseStatus status)
        {
            var update = Builders<Course>.Update.Set(c => c.Status, status).Set(c => c.UpdatedAt, DateTime.UtcNow);
            var result = await _mongoDb.Courses.UpdateOneAsync(c => c.CourseId == courseId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> PatchCourseAsync(string courseId, object request)
        {
            try
            {
                var json = JsonSerializer.Serialize(request);
                var patchDict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (patchDict == null || !patchDict.Any()) return false;

                var updateBuilder = Builders<Course>.Update;
                var updates = new List<UpdateDefinition<Course>>();

                foreach (var kvp in patchDict)
                {
                    // 👇 YAHAN CHANGE HUA HAI: Strict Security for Patch Endpoint
                    if (kvp.Key.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                        kvp.Key.Equals("CourseId", StringComparison.OrdinalIgnoreCase) ||
                        kvp.Key.Equals("CreatedBy", StringComparison.OrdinalIgnoreCase) ||
                        kvp.Key.Equals("EnrollmentCount", StringComparison.OrdinalIgnoreCase)) 
                        continue; // Block these fields from being modified via Patch

                    updates.Add(updateBuilder.Set(kvp.Key, kvp.Value));
                }

                if (!updates.Any()) return false;

                var combinedUpdate = updateBuilder.Combine(updates).Set("UpdatedAt", DateTime.UtcNow);
                var result = await _mongoDb.Courses.UpdateOneAsync(c => c.CourseId == courseId, combinedUpdate);
                return result.ModifiedCount > 0;
            }
            catch { return false; }
        }

        // ==========================================
        // SECTIONS
        // ==========================================
        public async Task<string?> AddSectionAsync(string courseId, CreateSectionRequest request)
        {
            var course = await GetCourseByIdAsync(courseId);
            if (course == null) return null;

            var newSection = new CourseSection
            {
                Title = request.Title,
                OrderIndex = course.Sections.Count
            };

            var update = Builders<Course>.Update.Push(c => c.Sections, newSection);
            await _mongoDb.Courses.UpdateOneAsync(c => c.CourseId == courseId, update);
            return newSection.Id;
        }

        public async Task<bool> DeleteSectionAsync(string courseId, string sectionId)
        {
            var update = Builders<Course>.Update.PullFilter(c => c.Sections, s => s.Id == sectionId);
            var result = await _mongoDb.Courses.UpdateOneAsync(c => c.CourseId == courseId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> ReorderSectionsAsync(string courseId, List<string> orderedSectionIds)
        {
            var course = await GetCourseByIdAsync(courseId);
            if (course == null) return false;

            // Reorder the sections list based on the provided IDs array
            var orderedSections = orderedSectionIds
                .Select(id => course.Sections.FirstOrDefault(s => s.Id == id))
                .Where(s => s != null)
                .ToList();

            if (orderedSections.Count != course.Sections.Count) return false; // Missing sections in request

            // Re-assign OrderIndex
            for (int i = 0; i < orderedSections.Count; i++) orderedSections[i]!.OrderIndex = i;

            var update = Builders<Course>.Update.Set(c => c.Sections, orderedSections!);
            var result = await _mongoDb.Courses.UpdateOneAsync(c => c.CourseId == courseId, update);
            return result.ModifiedCount > 0;
        }

        // ==========================================
        // CURRICULUM ITEMS
        // ==========================================
        public async Task<string?> AddCurriculumItemAsync(string courseId, string sectionId, CreateCurriculumItemRequest request)
        {
            var course = await GetCourseByIdAsync(courseId);
            var section = course?.Sections.FirstOrDefault(s => s.Id == sectionId);
            if (section == null) return null;

            var newItem = new CurriculumItem
            {
                Title = request.Title,
                Type = request.Type,
                ContentUrl = request.ContentUrl,
                IsFreePreview = request.IsFreePreview,
                DurationSeconds = request.DurationSeconds,
                OrderIndex = section.Items.Count
            };

            section.Items.Add(newItem);
            
            var result = await _mongoDb.Courses.ReplaceOneAsync(c => c.CourseId == courseId, course!);
            return result.ModifiedCount > 0 ? newItem.Id : null;
        }

        public async Task<bool> DeleteCurriculumItemAsync(string courseId, string sectionId, string itemId)
        {
            var course = await GetCourseByIdAsync(courseId);
            var section = course?.Sections.FirstOrDefault(s => s.Id == sectionId);
            if (section == null) return false;

            section.Items.RemoveAll(i => i.Id == itemId);
            var result = await _mongoDb.Courses.ReplaceOneAsync(c => c.CourseId == courseId, course!);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> ReorderCurriculumItemsAsync(string courseId, string sectionId, List<string> orderedItemIds)
        {
            var course = await GetCourseByIdAsync(courseId);
            var section = course?.Sections.FirstOrDefault(s => s.Id == sectionId);
            if (section == null) return false;

            var orderedItems = orderedItemIds
                .Select(id => section.Items.FirstOrDefault(i => i.Id == id))
                .Where(i => i != null)
                .ToList();

            if (orderedItems.Count != section.Items.Count) return false;

            for (int i = 0; i < orderedItems.Count; i++) orderedItems[i]!.OrderIndex = i;
            section.Items = orderedItems!;

            var result = await _mongoDb.Courses.ReplaceOneAsync(c => c.CourseId == courseId, course!);
            return result.ModifiedCount > 0;
        }

        // ==========================================
        // INSTRUCTORS
        // ==========================================
        public async Task<bool> AddInstructorAsync(string courseId, string instructorId)
        {
            var update = Builders<Course>.Update.AddToSet(c => c.InstructorIds, instructorId);
            var result = await _mongoDb.Courses.UpdateOneAsync(c => c.CourseId == courseId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> RemoveInstructorAsync(string courseId, string instructorId)
        {
            var update = Builders<Course>.Update.Pull(c => c.InstructorIds, instructorId);
            var result = await _mongoDb.Courses.UpdateOneAsync(c => c.CourseId == courseId, update);
            return result.ModifiedCount > 0;
        }
    }
}