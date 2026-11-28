using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CourseService.Models.V1;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CourseService.Services
{
    public interface ICourseEnrollmentLookup
    {
        Task<bool> IsUserEnrolledAsync(string userId, string courseId);
        Task<List<string>> GetEnrolledCourseIdsAsync(string userId);
    }

    public interface ICourseMediaSigner
    {
        string SignMediaUrl(string rawUrl, string userId);
        bool IsLocalPlatformMedia(string rawUrl);
    }

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
            string courseId = string.Empty;
            for (int i = 0; i < 15; i++)
            {
                var candidate = $"CRS-{RandomNumberGenerator.GetInt32(100_000, 1_000_000)}";
                if (!await _mongoDb.Courses.Find(c => c.CourseId == candidate).AnyAsync())
                {
                    courseId = candidate;
                    break;
                }
            }
            if (string.IsNullOrEmpty(courseId))
                courseId = $"CRS-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

            var course = new Course
            {
                CourseId = courseId,
                Title = request.Title,
                Description = request.Description,
                Level = request.Level,
                CoursePrice = request.CoursePrice,
                CreatedBy = userId,
                InstructorIds = new List<string> { userId }
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
            return result.MatchedCount > 0;
        }

        public async Task<bool> IncrementEnrollmentCountAsync(string courseId)
        {
            var update = Builders<Course>.Update
                .Inc(c => c.EnrollmentCount, 1)
                .Set(c => c.UpdatedAt, DateTime.UtcNow);
            var result = await _mongoDb.Courses.UpdateOneAsync(c => c.CourseId == courseId, update);
            return result.MatchedCount > 0;
        }

        public async Task<bool> PatchCourseAsync(string courseId, object request)
        {
            if (request == null) return false;

            try
            {
                var json = JsonSerializer.Serialize(request);
                var patchDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (patchDict == null || patchDict.Count == 0) return false;

                var updates = new List<UpdateDefinition<Course>>();
                foreach (var kvp in patchDict)
                {
                    var key = kvp.Key.ToLowerInvariant();
                    var val = kvp.Value;

                    switch (key)
                    {
                        case "title" when val.ValueKind == JsonValueKind.String:
                            var title = val.GetString();
                            if (!string.IsNullOrWhiteSpace(title))
                                updates.Add(Builders<Course>.Update.Set(c => c.Title, title));
                            break;
                        case "description" when val.ValueKind == JsonValueKind.String:
                            updates.Add(Builders<Course>.Update.Set(c => c.Description, val.GetString() ?? string.Empty));
                            break;
                        case "thumbnailurl" when val.ValueKind == JsonValueKind.String:
                            updates.Add(Builders<Course>.Update.Set(c => c.ThumbnailUrl, val.GetString() ?? string.Empty));
                            break;
                        case "courseprice" when val.ValueKind == JsonValueKind.Number && val.TryGetDecimal(out var price) && price >= 0:
                            updates.Add(Builders<Course>.Update.Set(c => c.CoursePrice, price));
                            break;
                        case "discountcourseprice" when val.ValueKind == JsonValueKind.Number && val.TryGetDecimal(out var discount) && discount >= 0:
                            updates.Add(Builders<Course>.Update.Set(c => c.DiscountCoursePrice, discount));
                            break;
                        case "level":
                            if (val.ValueKind == JsonValueKind.String && Enum.TryParse<CourseLevel>(val.GetString(), true, out var lvlStr))
                                updates.Add(Builders<Course>.Update.Set(c => c.Level, lvlStr));
                            else if (val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out var lvlInt) && Enum.IsDefined(typeof(CourseLevel), lvlInt))
                                updates.Add(Builders<Course>.Update.Set(c => c.Level, (CourseLevel)lvlInt));
                            break;
                        case "status":
                            if (val.ValueKind == JsonValueKind.String && Enum.TryParse<CourseStatus>(val.GetString(), true, out var stStr))
                                updates.Add(Builders<Course>.Update.Set(c => c.Status, stStr));
                            else if (val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out var stInt) && Enum.IsDefined(typeof(CourseStatus), stInt))
                                updates.Add(Builders<Course>.Update.Set(c => c.Status, (CourseStatus)stInt));
                            break;
                        case "instructorids" when val.ValueKind == JsonValueKind.Array:
                            var instructors = val.EnumerateArray()
                                .Where(e => e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString()))
                                .Select(e => e.GetString()!)
                                .Distinct()
                                .ToList();
                            updates.Add(Builders<Course>.Update.Set(c => c.InstructorIds, instructors));
                            break;
                    }
                }

                if (updates.Count == 0) return false;

                updates.Add(Builders<Course>.Update.Set(c => c.UpdatedAt, DateTime.UtcNow));
                var result = await _mongoDb.Courses.UpdateOneAsync(c => c.CourseId == courseId, Builders<Course>.Update.Combine(updates));
                return result.MatchedCount > 0;
            }
            catch
            {
                return false;
            }
        }

        // ==========================================
        // DISCOVERY, SEARCH & STATS
        // ==========================================
        public async Task<List<Course>> GetFeaturedCoursesAsync(int limit = 10)
        {
            return await _mongoDb.Courses
                .Find(c => c.Status == CourseStatus.Published)
                .SortByDescending(c => c.EnrollmentCount)
                .ThenByDescending(c => c.CreatedAt)
                .Limit(Math.Clamp(limit, 1, 50))
                .ToListAsync();
        }

        public async Task<List<Course>> GetPopularCoursesAsync(int limit = 10)
        {
            return await _mongoDb.Courses
                .Find(c => c.Status == CourseStatus.Published)
                .SortByDescending(c => c.EnrollmentCount)
                .Limit(Math.Clamp(limit, 1, 50))
                .ToListAsync();
        }

        public async Task<List<Course>> GetRecommendedCoursesAsync(IReadOnlyCollection<string>? enrolledCourseIds = null, int limit = 10)
        {
            var builder = Builders<Course>.Filter;
            var filter = builder.Eq(c => c.Status, CourseStatus.Published);
            if (enrolledCourseIds != null && enrolledCourseIds.Count > 0)
                filter &= builder.Nin(c => c.CourseId, enrolledCourseIds);

            return await _mongoDb.Courses
                .Find(filter)
                .SortByDescending(c => c.EnrollmentCount)
                .ThenByDescending(c => c.CreatedAt)
                .Limit(Math.Clamp(limit, 1, 50))
                .ToListAsync();
        }

        public async Task<(List<Course> Results, long Total)> SearchCoursesAsync(string? query, CourseLevel? level = null, int page = 1, int pageSize = 20)
        {
            var safePage = Math.Max(1, page);
            var safeSize = Math.Clamp(pageSize, 1, 100);

            var builder = Builders<Course>.Filter;
            var filter = builder.Eq(c => c.Status, CourseStatus.Published);

            if (!string.IsNullOrWhiteSpace(query))
            {
                var escaped = Regex.Escape(query.Trim());
                filter &= builder.Or(
                    builder.Regex(c => c.Title, new BsonRegularExpression(escaped, "i")),
                    builder.Regex(c => c.Description, new BsonRegularExpression(escaped, "i")),
                    builder.Regex(c => c.CourseId, new BsonRegularExpression(escaped, "i"))
                );
            }

            if (level.HasValue)
                filter &= builder.Eq(c => c.Level, level.Value);

            var total = await _mongoDb.Courses.CountDocumentsAsync(filter);
            var results = await _mongoDb.Courses.Find(filter)
                .SortByDescending(c => c.EnrollmentCount)
                .ThenByDescending(c => c.CreatedAt)
                .Skip((safePage - 1) * safeSize)
                .Limit(safeSize)
                .ToListAsync();

            return (results, total);
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

            var update = Builders<Course>.Update.Push(c => c.Sections, newSection).Set(c => c.UpdatedAt, DateTime.UtcNow);
            await _mongoDb.Courses.UpdateOneAsync(c => c.CourseId == courseId, update);
            return newSection.Id;
        }

        public async Task<bool> PatchSectionAsync(string courseId, string sectionId, object request)
        {
            var course = await GetCourseByIdAsync(courseId);
            var section = course?.Sections.FirstOrDefault(s => s.Id == sectionId);
            if (course == null || section == null || request == null) return false;

            try
            {
                var json = JsonSerializer.Serialize(request);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (data == null || data.Count == 0) return false;

                bool changed = false;
                if (data.TryGetValue("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String)
                {
                    var t = titleEl.GetString();
                    if (!string.IsNullOrWhiteSpace(t)) { section.Title = t; changed = true; }
                }
                else if (data.TryGetValue("Title", out var titlePascal) && titlePascal.ValueKind == JsonValueKind.String)
                {
                    var t = titlePascal.GetString();
                    if (!string.IsNullOrWhiteSpace(t)) { section.Title = t; changed = true; }
                }

                if ((data.TryGetValue("orderIndex", out var idxEl) || data.TryGetValue("OrderIndex", out idxEl)) &&
                    idxEl.ValueKind == JsonValueKind.Number && idxEl.TryGetInt32(out var idx))
                {
                    section.OrderIndex = idx;
                    changed = true;
                }

                if (!changed) return false;

                course.UpdatedAt = DateTime.UtcNow;
                var result = await _mongoDb.Courses.ReplaceOneAsync(c => c.CourseId == courseId, course);
                return result.MatchedCount > 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeleteSectionAsync(string courseId, string sectionId)
        {
            var update = Builders<Course>.Update.PullFilter(c => c.Sections, s => s.Id == sectionId).Set(c => c.UpdatedAt, DateTime.UtcNow);
            var result = await _mongoDb.Courses.UpdateOneAsync(c => c.CourseId == courseId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> ReorderSectionsAsync(string courseId, List<string> orderedSectionIds)
        {
            var course = await GetCourseByIdAsync(courseId);
            if (course == null) return false;

            var orderedSections = orderedSectionIds
                .Select(id => course.Sections.FirstOrDefault(s => s.Id == id))
                .Where(s => s != null)
                .ToList();

            if (orderedSections.Count != course.Sections.Count) return false;

            for (int i = 0; i < orderedSections.Count; i++) orderedSections[i]!.OrderIndex = i;

            var update = Builders<Course>.Update.Set(c => c.Sections, orderedSections!).Set(c => c.UpdatedAt, DateTime.UtcNow);
            var result = await _mongoDb.Courses.UpdateOneAsync(c => c.CourseId == courseId, update);
            return result.MatchedCount > 0;
        }

        // ==========================================
        // CURRICULUM ITEMS
        // ==========================================
        public async Task<string?> AddCurriculumItemAsync(string courseId, string sectionId, CreateCurriculumItemRequest request)
        {
            var course = await GetCourseByIdAsync(courseId);
            var section = course?.Sections.FirstOrDefault(s => s.Id == sectionId);
            if (section == null || course == null) return null;

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
            course.UpdatedAt = DateTime.UtcNow;

            var result = await _mongoDb.Courses.ReplaceOneAsync(c => c.CourseId == courseId, course);
            return result.MatchedCount > 0 ? newItem.Id : null;
        }

        public async Task<bool> PatchCurriculumItemAsync(string courseId, string sectionId, string itemId, object request)
        {
            var course = await GetCourseByIdAsync(courseId);
            var section = course?.Sections.FirstOrDefault(s => s.Id == sectionId);
            var item = section?.Items.FirstOrDefault(i => i.Id == itemId);
            if (course == null || item == null || request == null) return false;

            try
            {
                var json = JsonSerializer.Serialize(request);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (data == null || data.Count == 0) return false;

                bool changed = false;
                foreach (var kvp in data)
                {
                    switch (kvp.Key.ToLowerInvariant())
                    {
                        case "title" when kvp.Value.ValueKind == JsonValueKind.String:
                            var t = kvp.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(t)) { item.Title = t; changed = true; }
                            break;
                        case "contenturl" when kvp.Value.ValueKind == JsonValueKind.String:
                            item.ContentUrl = kvp.Value.GetString() ?? string.Empty;
                            changed = true;
                            break;
                        case "isfreepreview" when kvp.Value.ValueKind == JsonValueKind.True || kvp.Value.ValueKind == JsonValueKind.False:
                            item.IsFreePreview = kvp.Value.GetBoolean();
                            changed = true;
                            break;
                        case "durationseconds" when kvp.Value.ValueKind == JsonValueKind.Number && kvp.Value.TryGetInt32(out var dur) && dur >= 0:
                            item.DurationSeconds = dur;
                            changed = true;
                            break;
                        case "orderindex" when kvp.Value.ValueKind == JsonValueKind.Number && kvp.Value.TryGetInt32(out var ord):
                            item.OrderIndex = ord;
                            changed = true;
                            break;
                        case "type":
                            if (kvp.Value.ValueKind == JsonValueKind.String && Enum.TryParse<CurriculumType>(kvp.Value.GetString(), true, out var ctStr))
                            { item.Type = ctStr; changed = true; }
                            else if (kvp.Value.ValueKind == JsonValueKind.Number && kvp.Value.TryGetInt32(out var ctInt) && Enum.IsDefined(typeof(CurriculumType), ctInt))
                            { item.Type = (CurriculumType)ctInt; changed = true; }
                            break;
                    }
                }

                if (!changed) return false;
                course.UpdatedAt = DateTime.UtcNow;
                var result = await _mongoDb.Courses.ReplaceOneAsync(c => c.CourseId == courseId, course);
                return result.MatchedCount > 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeleteCurriculumItemAsync(string courseId, string sectionId, string itemId)
        {
            var course = await GetCourseByIdAsync(courseId);
            var section = course?.Sections.FirstOrDefault(s => s.Id == sectionId);
            if (section == null || course == null) return false;

            int removed = section.Items.RemoveAll(i => i.Id == itemId);
            if (removed == 0) return false;

            course.UpdatedAt = DateTime.UtcNow;
            var result = await _mongoDb.Courses.ReplaceOneAsync(c => c.CourseId == courseId, course);
            return result.MatchedCount > 0;
        }

        public async Task<bool> ReorderCurriculumItemsAsync(string courseId, string sectionId, List<string> orderedItemIds)
        {
            var course = await GetCourseByIdAsync(courseId);
            var section = course?.Sections.FirstOrDefault(s => s.Id == sectionId);
            if (section == null || course == null) return false;

            var orderedItems = orderedItemIds
                .Select(id => section.Items.FirstOrDefault(i => i.Id == id))
                .Where(i => i != null)
                .ToList();

            if (orderedItems.Count != section.Items.Count) return false;

            for (int i = 0; i < orderedItems.Count; i++) orderedItems[i]!.OrderIndex = i;
            section.Items = orderedItems!;
            course.UpdatedAt = DateTime.UtcNow;

            var result = await _mongoDb.Courses.ReplaceOneAsync(c => c.CourseId == courseId, course);
            return result.MatchedCount > 0;
        }

        // ==========================================
        // INSTRUCTORS
        // ==========================================
        public async Task<bool> AddInstructorAsync(string courseId, string instructorId)
        {
            var update = Builders<Course>.Update.AddToSet(c => c.InstructorIds, instructorId).Set(c => c.UpdatedAt, DateTime.UtcNow);
            var result = await _mongoDb.Courses.UpdateOneAsync(c => c.CourseId == courseId, update);
            return result.MatchedCount > 0;
        }

        public async Task<bool> RemoveInstructorAsync(string courseId, string instructorId)
        {
            var update = Builders<Course>.Update.Pull(c => c.InstructorIds, instructorId).Set(c => c.UpdatedAt, DateTime.UtcNow);
            var result = await _mongoDb.Courses.UpdateOneAsync(c => c.CourseId == courseId, update);
            return result.ModifiedCount > 0;
        }
    }
}
