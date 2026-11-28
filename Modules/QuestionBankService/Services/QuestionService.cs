using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using QuestionBankService.Models;
using Tag = QuestionBankService.Models.Tag;

namespace QuestionBankService.Services
{
    public class QuestionService
    {
        private readonly MongoDbService _mongoDb;

        public QuestionService(MongoDbService mongoDb)
        {
            _mongoDb = mongoDb;
        }

        #region 1. Questions (CRUD)

        public async Task<Question> CreateQuestionAsync(CreateQuestionRequest request, string createdBy)
        {
            var question = new Question
            {
                QuestionText = request.QuestionText,
                Type = request.Type,
                Options = request.Options ?? new List<QuestionOption>(),
                CorrectAnswer = request.CorrectAnswer,
                CorrectOptions = request.CorrectOptions ?? new List<string>(),
                Marks = request.Marks,
                NegativeMarks = request.NegativeMarks,
                DifficultyId = request.DifficultyId,
                CategoryId = request.CategoryId,
                SubjectId = request.SubjectId,
                TopicId = request.TopicId,
                Tags = new List<string>(),
                Explanation = request.Explanation,
                ImageUrl = request.ImageUrl,
                CodeSnippet = request.CodeSnippet,
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow,
                Status = QuestionStatus.Draft,
                Version = 1,
                IsActive = true
            };

            await _mongoDb.Questions.InsertOneAsync(question);
            return question;
        }

        public async Task<List<Question>> GetAllQuestionsAsync(
            string? categoryId = null,
            string? difficultyId = null,
            string? subjectId = null,
            string? topicId = null,
            QuestionStatus? status = null,
            QuestionType? type = null,
            string? search = null,
            bool includeArchived = false,
            int? page = null,
            int? pageSize = null)
        {
            var fb = Builders<Question>.Filter;
            var filters = new List<FilterDefinition<Question>>();

            if (!includeArchived)
                filters.Add(fb.Eq(q => q.IsActive, true));
            filters.Add(fb.Ne(q => q.IsExamExclusive, true));
            if (!string.IsNullOrEmpty(categoryId))
                filters.Add(fb.Eq(q => q.CategoryId, categoryId));
            if (!string.IsNullOrEmpty(difficultyId))
                filters.Add(fb.Eq(q => q.DifficultyId, difficultyId));
            if (!string.IsNullOrEmpty(subjectId))
                filters.Add(fb.Eq(q => q.SubjectId, subjectId));
            if (!string.IsNullOrEmpty(topicId))
                filters.Add(fb.Eq(q => q.TopicId, topicId));
            if (status.HasValue)
                filters.Add(fb.Eq(q => q.Status, status.Value));
            if (type.HasValue)
                filters.Add(fb.Eq(q => q.Type, type.Value));
            if (!string.IsNullOrWhiteSpace(search))
            {
                var escaped = Regex.Escape(search.Trim());
                filters.Add(fb.Regex(q => q.QuestionText, new BsonRegularExpression(escaped, "i")));
            }

            var filter = filters.Count > 0 ? fb.And(filters) : fb.Empty;
            var query = _mongoDb.Questions.Find(filter).SortByDescending(q => q.CreatedAt);

            if (page.HasValue || pageSize.HasValue)
            {
                int p = Math.Max(1, page ?? 1);
                int ps = Math.Clamp(pageSize ?? 20, 1, 200);
                return await query.Skip((p - 1) * ps).Limit(ps).ToListAsync();
            }

            return await query.ToListAsync();
        }

        public async Task<Question?> GetQuestionByIdAsync(string questionId)
        {
            return await _mongoDb.Questions.Find(q => q.Id == questionId).FirstOrDefaultAsync();
        }

        public async Task<bool> UpdateQuestionAsync(string questionId, Question updatedQuestion, string? updatedBy = null)
        {
            var existing = await GetQuestionByIdAsync(questionId);
            if (existing == null) return false;

            updatedQuestion.Id = existing.Id;
            updatedQuestion.CreatedBy = existing.CreatedBy;
            updatedQuestion.CreatedAt = existing.CreatedAt;
            updatedQuestion.UpdatedBy = updatedBy ?? existing.UpdatedBy;
            updatedQuestion.UpdatedAt = DateTime.UtcNow;

            var result = await _mongoDb.Questions.ReplaceOneAsync(q => q.Id == questionId, updatedQuestion);
            return result.MatchedCount > 0;
        }

        public async Task<bool> PatchQuestionAsync(string questionId, object patchData, string? updatedBy = null)
        {
            if (patchData == null) return false;
            var existing = await GetQuestionByIdAsync(questionId);
            if (existing == null) return false;

            try
            {
                var json = JsonSerializer.Serialize(patchData);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (data == null || data.Count == 0) return false;

                var updates = new List<UpdateDefinition<Question>>();
                foreach (var kvp in data)
                {
                    var v = kvp.Value;
                    switch (kvp.Key.ToLowerInvariant())
                    {
                        case "questiontext" when v.ValueKind == JsonValueKind.String:
                            var qt = v.GetString();
                            if (!string.IsNullOrWhiteSpace(qt))
                                updates.Add(Builders<Question>.Update.Set(q => q.QuestionText, qt));
                            break;
                        case "marks" when v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var m) && m >= 0:
                            updates.Add(Builders<Question>.Update.Set(q => q.Marks, m));
                            break;
                        case "negativemarks":
                            if (v.ValueKind == JsonValueKind.Null) updates.Add(Builders<Question>.Update.Set(q => q.NegativeMarks, null));
                            else if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var nm) && nm >= 0) updates.Add(Builders<Question>.Update.Set(q => q.NegativeMarks, nm));
                            break;
                        case "correctanswer":
                            updates.Add(Builders<Question>.Update.Set(q => q.CorrectAnswer, v.ValueKind == JsonValueKind.Null ? null : v.GetString()));
                            break;
                        case "correctoptions" when v.ValueKind == JsonValueKind.Array:
                            updates.Add(Builders<Question>.Update.Set(q => q.CorrectOptions,
                                v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()));
                            break;
                        case "explanation":
                            updates.Add(Builders<Question>.Update.Set(q => q.Explanation, v.ValueKind == JsonValueKind.Null ? null : v.GetString()));
                            break;
                        case "imageurl":
                            updates.Add(Builders<Question>.Update.Set(q => q.ImageUrl, v.ValueKind == JsonValueKind.Null ? null : v.GetString()));
                            break;
                        case "codesnippet":
                            updates.Add(Builders<Question>.Update.Set(q => q.CodeSnippet, v.ValueKind == JsonValueKind.Null ? null : v.GetString()));
                            break;
                        case "difficultyid" when v.ValueKind == JsonValueKind.String:
                            updates.Add(Builders<Question>.Update.Set(q => q.DifficultyId, v.GetString() ?? string.Empty));
                            break;
                        case "categoryid":
                            updates.Add(Builders<Question>.Update.Set(q => q.CategoryId, v.ValueKind == JsonValueKind.Null ? null : v.GetString()));
                            break;
                        case "subjectid":
                            updates.Add(Builders<Question>.Update.Set(q => q.SubjectId, v.ValueKind == JsonValueKind.Null ? null : v.GetString()));
                            break;
                        case "topicid":
                            updates.Add(Builders<Question>.Update.Set(q => q.TopicId, v.ValueKind == JsonValueKind.Null ? null : v.GetString()));
                            break;
                        case "tags" when v.ValueKind == JsonValueKind.Array:
                            updates.Add(Builders<Question>.Update.Set(q => q.Tags,
                                v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()));
                            break;
                        case "type":
                            if (v.ValueKind == JsonValueKind.String && Enum.TryParse<QuestionType>(v.GetString(), true, out var tStr))
                                updates.Add(Builders<Question>.Update.Set(q => q.Type, tStr));
                            else if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var tInt) && Enum.IsDefined(typeof(QuestionType), tInt))
                                updates.Add(Builders<Question>.Update.Set(q => q.Type, (QuestionType)tInt));
                            break;
                        case "status":
                            if (v.ValueKind == JsonValueKind.String && Enum.TryParse<QuestionStatus>(v.GetString(), true, out var sStr))
                                updates.Add(Builders<Question>.Update.Set(q => q.Status, sStr));
                            else if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var sInt) && Enum.IsDefined(typeof(QuestionStatus), sInt))
                                updates.Add(Builders<Question>.Update.Set(q => q.Status, (QuestionStatus)sInt));
                            break;
                    }
                }

                if (updates.Count == 0) return false;
                updates.Add(Builders<Question>.Update.Set(q => q.UpdatedAt, DateTime.UtcNow));
                if (updatedBy != null)
                    updates.Add(Builders<Question>.Update.Set(q => q.UpdatedBy, updatedBy));

                var result = await _mongoDb.Questions.UpdateOneAsync(q => q.Id == questionId, Builders<Question>.Update.Combine(updates));
                return result.MatchedCount > 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeleteQuestionAsync(string questionId)
        {
            var update = Builders<Question>.Update
                .Set(q => q.IsActive, false)
                .Set(q => q.Status, QuestionStatus.Archived)
                .Set(q => q.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.Questions.UpdateOneAsync(q => q.Id == questionId, update);
            return result.MatchedCount > 0;
        }

        #endregion

        #region 2. Question Lifecycle

        public async Task<bool> UpdateQuestionStatusAsync(string questionId, QuestionStatus newStatus, string? updatedBy = null)
        {
            bool active = newStatus != QuestionStatus.Archived;
            var update = Builders<Question>.Update
                .Set(q => q.Status, newStatus)
                .Set(q => q.IsActive, active)
                .Set(q => q.UpdatedAt, DateTime.UtcNow);

            if (updatedBy != null)
                update = update.Set(q => q.UpdatedBy, updatedBy);

            var result = await _mongoDb.Questions.UpdateOneAsync(q => q.Id == questionId, update);
            return result.MatchedCount > 0;
        }

        public Task<bool> ArchiveQuestionAsync(string questionId, string? updatedBy = null) =>
            UpdateQuestionStatusAsync(questionId, QuestionStatus.Archived, updatedBy);

        public Task<bool> RestoreQuestionAsync(string questionId, string? updatedBy = null) =>
            UpdateQuestionStatusAsync(questionId, QuestionStatus.Draft, updatedBy);

        public async Task<Question?> DuplicateQuestionAsync(string questionId, string createdBy)
        {
            var existing = await GetQuestionByIdAsync(questionId);
            if (existing == null) return null;

            var oldToNewOptionIds = new Dictionary<string, string>();
            foreach (var opt in existing.Options)
            {
                var newId = Guid.NewGuid().ToString();
                oldToNewOptionIds[opt.Id] = newId;
                opt.Id = newId;
            }

            existing.CorrectOptions = existing.CorrectOptions
                .Select(id => oldToNewOptionIds.TryGetValue(id, out var mapped) ? mapped : id)
                .ToList();
            if (!string.IsNullOrEmpty(existing.CorrectAnswer) && oldToNewOptionIds.TryGetValue(existing.CorrectAnswer, out var mappedCorrect))
                existing.CorrectAnswer = mappedCorrect;

            existing.Id = Guid.NewGuid().ToString();
            existing.ParentQuestionId = questionId;
            existing.Status = QuestionStatus.Draft;
            existing.IsActive = true;
            existing.Version = 1;
            existing.CreatedBy = createdBy;
            existing.CreatedAt = DateTime.UtcNow;
            existing.UpdatedBy = null;
            existing.UpdatedAt = null;
            existing.ReviewHistory = new List<ReviewLog>();

            await _mongoDb.Questions.InsertOneAsync(existing);
            return existing;
        }

        #endregion

        #region 3. Question Versions

        public async Task<List<QuestionVersion>> GetQuestionVersionsAsync(string questionId)
        {
            return await _mongoDb.QuestionVersions
                .Find(v => v.OriginalQuestionId == questionId)
                .SortByDescending(v => v.VersionNumber)
                .ToListAsync();
        }

        public async Task<QuestionVersion?> GetQuestionVersionByIdAsync(string versionId)
        {
            return await _mongoDb.QuestionVersions.Find(v => v.Id == versionId).FirstOrDefaultAsync();
        }

        public async Task<QuestionVersion?> CreateQuestionVersionAsync(string questionId, string userId)
        {
            var question = await GetQuestionByIdAsync(questionId);
            if (question == null) return null;

            var snapshot = new QuestionVersion
            {
                OriginalQuestionId = question.Id,
                VersionNumber = question.Version,
                SnapshotData = question,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };

            await _mongoDb.QuestionVersions.InsertOneAsync(snapshot);

            var update = Builders<Question>.Update.Inc(q => q.Version, 1).Set(q => q.UpdatedAt, DateTime.UtcNow);
            await _mongoDb.Questions.UpdateOneAsync(q => q.Id == questionId, update);

            return snapshot;
        }

        public async Task<bool> RestoreQuestionVersionAsync(string questionId, string versionId, string userId)
        {
            var version = await GetQuestionVersionByIdAsync(versionId);
            if (version == null || version.OriginalQuestionId != questionId) return false;

            var restoredQuestion = version.SnapshotData;
            restoredQuestion.Id = questionId;
            restoredQuestion.UpdatedBy = userId;
            restoredQuestion.UpdatedAt = DateTime.UtcNow;

            var currentQuestion = await GetQuestionByIdAsync(questionId);
            if (currentQuestion != null) restoredQuestion.Version = currentQuestion.Version + 1;

            var result = await _mongoDb.Questions.ReplaceOneAsync(q => q.Id == questionId, restoredQuestion);
            return result.MatchedCount > 0;
        }

        #endregion

        #region 4. Question Options

        public async Task<QuestionOption?> AddQuestionOptionAsync(string questionId, OptionRequest optionRequest)
        {
            var question = await GetQuestionByIdAsync(questionId);
            if (question == null) return null;

            var newOption = new QuestionOption
            {
                Text = optionRequest.Text,
                ImageUrl = optionRequest.ImageUrl
            };

            var update = Builders<Question>.Update
                .Push(q => q.Options, newOption)
                .Set(q => q.UpdatedAt, DateTime.UtcNow);

            if (optionRequest.IsCorrect)
            {
                update = update.AddToSet(q => q.CorrectOptions, newOption.Id);
                if (question.Type == QuestionType.MCQ && string.IsNullOrEmpty(question.CorrectAnswer))
                {
                    update = update.Set(q => q.CorrectAnswer, newOption.Id);
                }
            }

            var result = await _mongoDb.Questions.UpdateOneAsync(q => q.Id == questionId, update);
            return result.MatchedCount > 0 ? newOption : null;
        }

        public async Task<bool> UpdateQuestionOptionAsync(string questionId, string optionId, OptionRequest optionRequest)
        {
            var question = await GetQuestionByIdAsync(questionId);
            if (question == null) return false;
            var opt = question.Options.FirstOrDefault(o => o.Id == optionId);
            if (opt == null) return false;

            opt.Text = optionRequest.Text;
            opt.ImageUrl = optionRequest.ImageUrl;
            if (optionRequest.IsCorrect && !question.CorrectOptions.Contains(optionId))
                question.CorrectOptions.Add(optionId);
            else if (!optionRequest.IsCorrect)
                question.CorrectOptions.Remove(optionId);
            question.UpdatedAt = DateTime.UtcNow;

            var result = await _mongoDb.Questions.ReplaceOneAsync(q => q.Id == questionId, question);
            return result.MatchedCount > 0;
        }

        public async Task<bool> DeleteQuestionOptionAsync(string questionId, string optionId)
        {
            var question = await GetQuestionByIdAsync(questionId);
            if (question == null) return false;

            int removed = question.Options.RemoveAll(o => o.Id == optionId);
            if (removed == 0) return false;

            question.CorrectOptions.RemoveAll(id => id == optionId);
            if (question.CorrectAnswer == optionId) question.CorrectAnswer = null;
            question.UpdatedAt = DateTime.UtcNow;

            var result = await _mongoDb.Questions.ReplaceOneAsync(q => q.Id == questionId, question);
            return result.MatchedCount > 0;
        }

        #endregion

        #region 5. Categories

        public async Task<List<Category>> GetCategoriesAsync() => await _mongoDb.Categories.Find(_ => true).ToListAsync();
        public async Task<Category?> GetCategoryByIdAsync(string id) => await _mongoDb.Categories.Find(c => c.Id == id).FirstOrDefaultAsync();
        public async Task<Category> CreateCategoryAsync(Category category) { await _mongoDb.Categories.InsertOneAsync(category); return category; }

        public async Task<bool> UpdateCategoryAsync(string id, Category category)
        {
            var existing = await GetCategoryByIdAsync(id);
            if (existing == null) return false;
            category.Id = id;
            category.CreatedAt = existing.CreatedAt;
            var res = await _mongoDb.Categories.ReplaceOneAsync(c => c.Id == id, category);
            return res.MatchedCount > 0;
        }

        public async Task<bool> DeleteCategoryAsync(string id) { var res = await _mongoDb.Categories.DeleteOneAsync(c => c.Id == id); return res.DeletedCount > 0; }

        #endregion

        #region 6. Subjects

        public async Task<List<Subject>> GetSubjectsAsync(string? categoryId = null) =>
            string.IsNullOrWhiteSpace(categoryId)
                ? await _mongoDb.Subjects.Find(_ => true).SortBy(s => s.Name).ToListAsync()
                : await _mongoDb.Subjects.Find(s => s.CategoryId == categoryId).SortBy(s => s.Name).ToListAsync();

        public async Task<Subject?> GetSubjectByIdAsync(string id) => await _mongoDb.Subjects.Find(s => s.Id == id).FirstOrDefaultAsync();
        public async Task<Subject> CreateSubjectAsync(Subject subject) { await _mongoDb.Subjects.InsertOneAsync(subject); return subject; }

        public async Task<bool> UpdateSubjectAsync(string id, Subject subject)
        {
            var existing = await GetSubjectByIdAsync(id);
            if (existing == null) return false;
            subject.Id = id;
            subject.CreatedAt = existing.CreatedAt;
            var res = await _mongoDb.Subjects.ReplaceOneAsync(s => s.Id == id, subject);
            return res.MatchedCount > 0;
        }

        public async Task<bool> DeleteSubjectAsync(string id) { var res = await _mongoDb.Subjects.DeleteOneAsync(s => s.Id == id); return res.DeletedCount > 0; }

        #endregion

        #region 7. Topics

        public async Task<List<Topic>> GetTopicsAsync(string? subjectId = null) =>
            string.IsNullOrWhiteSpace(subjectId)
                ? await _mongoDb.Topics.Find(_ => true).SortBy(t => t.Name).ToListAsync()
                : await _mongoDb.Topics.Find(t => t.SubjectId == subjectId).SortBy(t => t.Name).ToListAsync();
        public async Task<Topic?> GetTopicByIdAsync(string id) => await _mongoDb.Topics.Find(t => t.Id == id).FirstOrDefaultAsync();
        public async Task<Topic> CreateTopicAsync(Topic topic) { await _mongoDb.Topics.InsertOneAsync(topic); return topic; }

        public async Task<bool> UpdateTopicAsync(string id, Topic topic)
        {
            var existing = await GetTopicByIdAsync(id);
            if (existing == null) return false;
            topic.Id = id;
            topic.CreatedAt = existing.CreatedAt;
            var res = await _mongoDb.Topics.ReplaceOneAsync(t => t.Id == id, topic);
            return res.MatchedCount > 0;
        }

        public async Task<bool> DeleteTopicAsync(string id) { var res = await _mongoDb.Topics.DeleteOneAsync(t => t.Id == id); return res.DeletedCount > 0; }

        #endregion

        #region 8. Difficulty

        public async Task<List<Difficulty>> GetDifficultiesAsync() => await _mongoDb.Difficulties.Find(_ => true).ToListAsync();
        public async Task<Difficulty?> GetDifficultyByIdAsync(string id) => await _mongoDb.Difficulties.Find(d => d.Id == id).FirstOrDefaultAsync();
        public async Task<Difficulty> CreateDifficultyAsync(Difficulty difficulty) { await _mongoDb.Difficulties.InsertOneAsync(difficulty); return difficulty; }

        public async Task<bool> UpdateDifficultyAsync(string id, Difficulty diff)
        {
            diff.Id = id;
            var res = await _mongoDb.Difficulties.ReplaceOneAsync(d => d.Id == id, diff);
            return res.MatchedCount > 0;
        }

        public async Task<bool> DeleteDifficultyAsync(string id) { var res = await _mongoDb.Difficulties.DeleteOneAsync(d => d.Id == id); return res.DeletedCount > 0; }

        #endregion

        #region 9. Tags

        public async Task<List<Tag>> GetTagsAsync() => await _mongoDb.Tags.Find(_ => true).ToListAsync();
        public async Task<Tag?> GetTagByIdAsync(string id) => await _mongoDb.Tags.Find(t => t.Id == id).FirstOrDefaultAsync();
        public async Task<Tag> CreateTagAsync(Tag tag) { await _mongoDb.Tags.InsertOneAsync(tag); return tag; }

        public async Task<bool> UpdateTagAsync(string id, Tag tag)
        {
            tag.Id = id;
            var res = await _mongoDb.Tags.ReplaceOneAsync(t => t.Id == id, tag);
            return res.MatchedCount > 0;
        }

        public async Task<bool> DeleteTagAsync(string id) { var res = await _mongoDb.Tags.DeleteOneAsync(t => t.Id == id); return res.DeletedCount > 0; }

        #endregion

        #region 10. Bulk, Import & Export Operations

        public async Task<List<Question>> GetQuestionsByIdsAsync(List<string> questionIds)
        {
            if (questionIds == null || questionIds.Count == 0) return new List<Question>();
            var filter = Builders<Question>.Filter.In(q => q.Id, questionIds);
            return await _mongoDb.Questions.Find(filter).ToListAsync();
        }

        public async Task<long> BulkUpdateStatusAsync(List<string> questionIds, QuestionStatus status)
        {
            if (questionIds == null || questionIds.Count == 0) return 0;
            var filter = Builders<Question>.Filter.In(q => q.Id, questionIds);
            var update = Builders<Question>.Update
                .Set(q => q.Status, status)
                .Set(q => q.IsActive, status != QuestionStatus.Archived)
                .Set(q => q.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.Questions.UpdateManyAsync(filter, update);
            return result.MatchedCount;
        }

        public async Task<long> BulkDeleteAsync(List<string> questionIds)
        {
            if (questionIds == null || questionIds.Count == 0) return 0;
            var filter = Builders<Question>.Filter.In(q => q.Id, questionIds);
            var result = await _mongoDb.Questions.DeleteManyAsync(filter);
            return result.DeletedCount;
        }

        private const int MaxCsvDataRows = 2000;

        public async Task<(int ImportedCount, List<Question> Questions)> ImportQuestionsAsync(JsonElement importData, string createdBy)
        {
            var toInsert = new List<Question>();
            var serializerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            if (importData.ValueKind == JsonValueKind.Array)
            {
                var items = JsonSerializer.Deserialize<List<CreateQuestionRequest>>(importData.GetRawText(), serializerOptions) ?? new();
                foreach (var req in items.Where(r => !string.IsNullOrWhiteSpace(r.QuestionText)))
                    toInsert.Add(MapImportRequest(req, createdBy));
            }
            else if (importData.ValueKind == JsonValueKind.Object)
            {
                if (importData.TryGetProperty("questions", out var qArr) && qArr.ValueKind == JsonValueKind.Array)
                {
                    var items = JsonSerializer.Deserialize<List<CreateQuestionRequest>>(qArr.GetRawText(), serializerOptions) ?? new();
                    foreach (var req in items.Where(r => !string.IsNullOrWhiteSpace(r.QuestionText)))
                        toInsert.Add(MapImportRequest(req, createdBy));
                }
                else if (importData.TryGetProperty("csv", out var csvEl) && csvEl.ValueKind == JsonValueKind.String)
                {
                    var (_, entities, _) = await ImportQuestionsFromCsvInternalAsync(
                        csvEl.GetString() ?? string.Empty,
                        createdBy,
                        dryRun: false,
                        skipInvalidRows: true);
                    return (entities.Count, entities);
                }
            }

            if (toInsert.Count > 0)
            {
                await _mongoDb.Questions.InsertManyAsync(toInsert);
            }

            return (toInsert.Count, toInsert);
        }

        public static (byte[] Bytes, string ContentType, string FileName) GenerateCsvTemplate()
        {
            var sb = new StringBuilder();
            sb.AppendLine("QuestionText,Type,OptionA,OptionB,OptionC,OptionD,OptionE,OptionF,CorrectAnswer,CorrectOptions,Marks,NegativeMarks,Difficulty,Category,Subject,Topic,Explanation,CodeSnippet,Status");
            sb.AppendLine(string.Join(",",
                CsvEscape("What is the time complexity of binary search on a sorted array of n elements?"),
                CsvEscape("MCQ"),
                CsvEscape("O(n)"),
                CsvEscape("O(log n)"),
                CsvEscape("O(n log n)"),
                CsvEscape("O(1)"),
                string.Empty,
                string.Empty,
                CsvEscape("B"),
                string.Empty,
                "2",
                "0.5",
                CsvEscape("Easy"),
                CsvEscape("Computer Science"),
                CsvEscape("Data Structures & Algorithms"),
                CsvEscape("Searching"),
                CsvEscape("Binary search halves the search space on every comparison."),
                string.Empty,
                CsvEscape("Draft")));
            sb.AppendLine(string.Join(",",
                CsvEscape("Which of the following HTTP status codes indicate a client error?"),
                CsvEscape("MultipleCorrect"),
                CsvEscape("400 Bad Request"),
                CsvEscape("201 Created"),
                CsvEscape("404 Not Found"),
                CsvEscape("500 Internal Server Error"),
                string.Empty,
                string.Empty,
                string.Empty,
                CsvEscape("A|C"),
                "4",
                "1",
                CsvEscape("Medium"),
                CsvEscape("Web Development"),
                CsvEscape("Backend Systems"),
                CsvEscape("HTTP & REST"),
                CsvEscape("4xx status codes represent client errors; 2xx is success and 5xx is server error."),
                string.Empty,
                CsvEscape("Published")));
            sb.AppendLine(string.Join(",",
                CsvEscape("In MongoDB, B-tree indexes speed up equality and range queries at the cost of slightly slower writes."),
                CsvEscape("TrueFalse"),
                CsvEscape("True"),
                CsvEscape("False"),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                CsvEscape("True"),
                string.Empty,
                "1",
                "0",
                CsvEscape("Easy"),
                CsvEscape("Databases"),
                CsvEscape("NoSQL"),
                CsvEscape("Indexing"),
                CsvEscape("Indexes speed up reads but must also be maintained during inserts and updates."),
                string.Empty,
                CsvEscape("Published")));
            sb.AppendLine(string.Join(",",
                CsvEscape("The _______ keyword in C# ensures deterministic disposal of IDisposable resources."),
                CsvEscape("FillInTheBlanks"),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                CsvEscape("using"),
                string.Empty,
                "2",
                "0",
                CsvEscape("Easy"),
                CsvEscape("Programming"),
                CsvEscape("C#"),
                CsvEscape("Resource Management"),
                CsvEscape("The using statement/declaration guarantees Dispose() is called when scope exits."),
                string.Empty,
                CsvEscape("Draft")));
            sb.AppendLine(string.Join(",",
                CsvEscape("Analyze the function below and state its time complexity in Big-O notation."),
                CsvEscape("CodeEvaluation"),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                CsvEscape("O(n)"),
                string.Empty,
                "5",
                "0",
                CsvEscape("Hard"),
                CsvEscape("Computer Science"),
                CsvEscape("Algorithms"),
                CsvEscape("Complexity Analysis"),
                CsvEscape("A single pass loop visits each element once."),
                CsvEscape("int Sum(int[] arr)\n{\n    int total = 0;\n    foreach (var x in arr) total += x;\n    return total;\n}"),
                CsvEscape("Draft")));

            var payloadBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return (payloadBytes, "text/csv; charset=utf-8", "questions_import_template.csv");
        }

        public static (byte[] Bytes, string ContentType, string FileName) GenerateExamSectionCsvTemplate()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Section,QuestionText,Type,OptionA,OptionB,OptionC,OptionD,OptionE,OptionF,CorrectAnswer,CorrectOptions,Marks,NegativeMarks,Difficulty,Explanation,CodeSnippet");
            sb.AppendLine(string.Join(",",
                CsvEscape("Section A - Core Concepts"),
                CsvEscape("Which data structure provides O(1) average lookup time by key?"),
                CsvEscape("MCQ"),
                CsvEscape("Linked List"),
                CsvEscape("Hash Table"),
                CsvEscape("Binary Tree"),
                CsvEscape("Stack"),
                string.Empty,
                string.Empty,
                CsvEscape("B"),
                string.Empty,
                "2",
                "0.5",
                CsvEscape("Easy"),
                CsvEscape("Hash tables compute array bucket indices directly from keys."),
                string.Empty));
            sb.AppendLine(string.Join(",",
                CsvEscape("Section A - Core Concepts"),
                CsvEscape("Select all valid HTTP idempotent methods."),
                CsvEscape("MultipleCorrect"),
                CsvEscape("GET"),
                CsvEscape("POST"),
                CsvEscape("PUT"),
                CsvEscape("DELETE"),
                string.Empty,
                string.Empty,
                string.Empty,
                CsvEscape("A|C|D"),
                "4",
                "1",
                CsvEscape("Medium"),
                CsvEscape("GET, PUT, and DELETE are idempotent in REST semantics; POST is not."),
                string.Empty));
            sb.AppendLine(string.Join(",",
                CsvEscape("Section B - Aptitude & Code"),
                CsvEscape("Binary search requires the input array to be sorted."),
                CsvEscape("TrueFalse"),
                CsvEscape("True"),
                CsvEscape("False"),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                CsvEscape("True"),
                string.Empty,
                "1",
                "0",
                CsvEscape("Easy"),
                CsvEscape("Sorted ordering allows discarding half of the elements each step."),
                string.Empty));
            sb.AppendLine(string.Join(",",
                CsvEscape("Section B - Aptitude & Code"),
                CsvEscape("What does the following C# snippet output?"),
                CsvEscape("CodeEvaluation"),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                CsvEscape("6"),
                string.Empty,
                "5",
                "0",
                CsvEscape("Medium"),
                CsvEscape("Sums elements 1 + 2 + 3 = 6."),
                CsvEscape("int[] a = { 1, 2, 3 };\nConsole.WriteLine(a.Sum());")));

            var payloadBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return (payloadBytes, "text/csv; charset=utf-8", "exam_section_questions_template.csv");
        }

        public async Task<(BulkQuestionImportResult Result, List<ExamCsvSectionQuestions> Sections)> ImportExamQuestionsFromCsvAsync(
            string csvContent,
            string createdBy,
            string? examId = null,
            string? targetSectionTitle = null,
            bool dryRun = false,
            bool skipInvalidRows = true)
        {
            var fallbackSection = string.IsNullOrWhiteSpace(targetSectionTitle) ? "Section 1" : targetSectionTitle.Trim();
            var (result, _, groupedSections) = await ImportQuestionsFromCsvInternalAsync(
                csvContent,
                createdBy,
                dryRun,
                skipInvalidRows,
                defaultCategoryId: null,
                defaultSubjectId: null,
                defaultTopicId: null,
                defaultStatus: QuestionStatus.Published,
                isExamExclusive: true,
                exclusiveExamId: examId,
                defaultSectionTitle: fallbackSection);

            return (result, groupedSections);
        }

        public async Task BindExamExclusiveQuestionsAsync(IEnumerable<string> questionIds, string examId)
        {
            var ids = questionIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
            if (ids.Count == 0 || string.IsNullOrWhiteSpace(examId)) return;

            var filter = Builders<Question>.Filter.And(
                Builders<Question>.Filter.In(q => q.Id, ids),
                Builders<Question>.Filter.Eq(q => q.IsExamExclusive, true));
            var update = Builders<Question>.Update.Set(q => q.ExclusiveExamId, examId);
            await _mongoDb.Questions.UpdateManyAsync(filter, update);
        }

        public async Task<RandomSelectionResult> SelectRandomBankQuestionsAsync(
            IReadOnlyList<RandomBankQuestionRule> rules,
            bool onlyPublished = false,
            bool allowPartial = false)
        {
            var response = new RandomSelectionResult { Success = true };
            if (rules == null || rules.Count == 0)
            {
                return new RandomSelectionResult
                {
                    Success = false,
                    Message = "At least one Subject/Topic rule is required."
                };
            }

            var categories = await _mongoDb.Categories.Find(_ => true).ToListAsync();
            var subjects = await _mongoDb.Subjects.Find(_ => true).ToListAsync();
            var topics = await _mongoDb.Topics.Find(_ => true).ToListAsync();

            // Phase 1: Resolve matching Question Bank pools per rule before allocation
            var prepared = new List<(int Index, RandomBankQuestionRule Rule, string RuleLabel, List<Question> Pool)>(rules.Count);

            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (rule.Count <= 0)
                {
                    return new RandomSelectionResult
                    {
                        Success = false,
                        Message = $"Rule #{i + 1}: Number of questions must be at least 1."
                    };
                }

                var fb = Builders<Question>.Filter;
                var filters = new List<FilterDefinition<Question>>
                {
                    fb.Eq(q => q.IsActive, true),
                    fb.Ne(q => q.IsExamExclusive, true),
                    fb.Ne(q => q.Status, QuestionStatus.Archived)
                };

                if (onlyPublished)
                    filters.Add(fb.Eq(q => q.Status, QuestionStatus.Published));

                HashSet<string>? matchedCategoryIds = null;
                if (!string.IsNullOrWhiteSpace(rule.Category))
                {
                    var catQuery = rule.Category.Trim();
                    matchedCategoryIds = categories
                        .Where(c => c.Id.Equals(catQuery, StringComparison.OrdinalIgnoreCase) ||
                                    c.Name.Trim().Equals(catQuery, StringComparison.OrdinalIgnoreCase))
                        .Select(c => c.Id)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    if (matchedCategoryIds.Count == 0)
                    {
                        return new RandomSelectionResult
                        {
                            Success = false,
                            Message = $"No category matching '{catQuery}' found in Question Bank."
                        };
                    }

                    filters.Add(fb.In(q => q.CategoryId, matchedCategoryIds));
                }

                HashSet<string>? matchedSubjectIds = null;
                if (!string.IsNullOrWhiteSpace(rule.Subject))
                {
                    var subQuery = rule.Subject.Trim();
                    matchedSubjectIds = subjects
                        .Where(s =>
                            (matchedCategoryIds == null || matchedCategoryIds.Contains(s.CategoryId)) &&
                            (s.Id.Equals(subQuery, StringComparison.OrdinalIgnoreCase) ||
                             s.Name.Trim().Equals(subQuery, StringComparison.OrdinalIgnoreCase)))
                        .Select(s => s.Id)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    if (matchedSubjectIds.Count == 0 && matchedCategoryIds != null)
                    {
                        // Fallback if Subject exists under another or empty category
                        matchedSubjectIds = subjects
                            .Where(s => s.Id.Equals(subQuery, StringComparison.OrdinalIgnoreCase) ||
                                        s.Name.Trim().Equals(subQuery, StringComparison.OrdinalIgnoreCase))
                            .Select(s => s.Id)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    }

                    if (matchedSubjectIds.Count == 0)
                    {
                        return new RandomSelectionResult
                        {
                            Success = false,
                            Message = $"No subject matching '{subQuery}' found in Question Bank."
                        };
                    }

                    filters.Add(fb.In(q => q.SubjectId, matchedSubjectIds));
                }

                if (!string.IsNullOrWhiteSpace(rule.Topic))
                {
                    var topQuery = rule.Topic.Trim();
                    var matchedTopicIds = topics
                        .Where(t =>
                            (matchedSubjectIds == null || matchedSubjectIds.Contains(t.SubjectId)) &&
                            (t.Id.Equals(topQuery, StringComparison.OrdinalIgnoreCase) ||
                             t.Name.Trim().Equals(topQuery, StringComparison.OrdinalIgnoreCase)))
                        .Select(t => t.Id)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    if (matchedTopicIds.Count == 0 && matchedSubjectIds != null)
                    {
                        matchedTopicIds = topics
                            .Where(t => t.Id.Equals(topQuery, StringComparison.OrdinalIgnoreCase) ||
                                        t.Name.Trim().Equals(topQuery, StringComparison.OrdinalIgnoreCase))
                            .Select(t => t.Id)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    }

                    if (matchedTopicIds.Count == 0)
                    {
                        return new RandomSelectionResult
                        {
                            Success = false,
                            Message = $"No topic matching '{topQuery}' found in Question Bank."
                        };
                    }

                    filters.Add(fb.In(q => q.TopicId, matchedTopicIds));
                }

                if (!string.IsNullOrWhiteSpace(rule.Difficulty))
                {
                    var escapedDiff = Regex.Escape(rule.Difficulty.Trim());
                    filters.Add(fb.Regex(q => q.DifficultyId, new BsonRegularExpression($"^{escapedDiff}$", "i")));
                }

                if (rule.QuestionType.HasValue && Enum.IsDefined(typeof(QuestionType), rule.QuestionType.Value))
                {
                    filters.Add(fb.Eq(q => q.Type, (QuestionType)rule.QuestionType.Value));
                }

                var candidates = await _mongoDb.Questions.Find(fb.And(filters)).ToListAsync();
                var labelParts = new[] { rule.Topic?.Trim(), rule.Subject?.Trim(), rule.Category?.Trim(), rule.Difficulty?.Trim() }
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
                var ruleLabel = labelParts.Count > 0 ? string.Join(" / ", labelParts) : "Question Bank";

                if (candidates.Count == 0)
                {
                    return new RandomSelectionResult
                    {
                        Success = false,
                        Message = $"No matching questions found in Question Bank for '{ruleLabel}'."
                    };
                }

                prepared.Add((i, rule, ruleLabel, candidates));
            }

            // Phase 2: Allocate most-constrained rules first (fewest spare questions) so broad rules don't starve narrow topic rules
            var allocationOrder = prepared
                .OrderBy(p => p.Pool.Count - p.Rule.Count)
                .ThenBy(p => p.Pool.Count)
                .ThenBy(p => p.Index)
                .ToList();

            var usedIds = new HashSet<string>();
            var sectionResults = new RandomSelectionSectionResult[rules.Count];

            foreach (var item in allocationOrder)
            {
                var remainingCandidates = item.Pool.Where(q => !usedIds.Contains(q.Id)).ToList();

                if (remainingCandidates.Count < item.Rule.Count && !allowPartial)
                {
                    return new RandomSelectionResult
                    {
                        Success = false,
                        Message = $"Requested {item.Rule.Count} question(s) for '{item.RuleLabel}', but only {remainingCandidates.Count} available in Question Bank."
                    };
                }

                var shuffled = remainingCandidates.ToArray();
                RandomNumberGenerator.Shuffle(shuffled.AsSpan());
                var picked = shuffled.Take(Math.Min(item.Rule.Count, shuffled.Length)).ToList();

                foreach (var q in picked)
                    usedIds.Add(q.Id);

                var secTitle = !string.IsNullOrWhiteSpace(item.Rule.SectionTitle)
                    ? item.Rule.SectionTitle.Trim()
                    : (!string.IsNullOrWhiteSpace(item.Rule.Topic)
                        ? item.Rule.Topic.Trim()
                        : (!string.IsNullOrWhiteSpace(item.Rule.Subject)
                            ? item.Rule.Subject.Trim()
                            : $"Section {item.Index + 1}"));

                sectionResults[item.Index] = new RandomSelectionSectionResult
                {
                    SectionTitle = secTitle,
                    RequestedCount = item.Rule.Count,
                    Questions = picked
                };
            }

            // Phase 3: Restore user-specified rule order
            response.Sections.AddRange(sectionResults);

            int totalPicked = response.Sections.Sum(s => s.Questions.Count);
            response.Message = $"Selected {totalPicked} random question(s) across {response.Sections.Count} section(s).";
            return response;
        }

        public async Task<BulkQuestionImportResult> ImportQuestionsFromCsvAsync(
            string csvContent,
            string createdBy,
            bool dryRun = false,
            bool skipInvalidRows = true,
            string? defaultCategoryId = null,
            string? defaultSubjectId = null,
            string? defaultTopicId = null,
            QuestionStatus? defaultStatus = null)
        {
            var (result, _, _) = await ImportQuestionsFromCsvInternalAsync(
                csvContent, createdBy, dryRun, skipInvalidRows, defaultCategoryId, defaultSubjectId, defaultTopicId, defaultStatus);
            return result;
        }

        private async Task<(BulkQuestionImportResult Result, List<Question> Entities, List<ExamCsvSectionQuestions> Sections)> ImportQuestionsFromCsvInternalAsync(
            string csvContent,
            string createdBy,
            bool dryRun,
            bool skipInvalidRows,
            string? defaultCategoryId = null,
            string? defaultSubjectId = null,
            string? defaultTopicId = null,
            QuestionStatus? defaultStatus = null,
            bool isExamExclusive = false,
            string? exclusiveExamId = null,
            string defaultSectionTitle = "Section 1")
        {
            var result = new BulkQuestionImportResult { DryRun = dryRun };

            List<List<string>> rows;
            try
            {
                rows = ParseCsvRecords(csvContent);
            }
            catch (InvalidOperationException ex)
            {
                result.TotalRows = 0;
                result.FailedCount = 1;
                result.Errors.Add(new CsvImportErrorDto { RowNumber = 1, Error = ex.Message });
                result.Message = ex.Message;
                return (result, new List<Question>(), new List<ExamCsvSectionQuestions>());
            }

            if (rows.Count == 0)
            {
                result.Message = "CSV file contains no rows.";
                return (result, new List<Question>(), new List<ExamCsvSectionQuestions>());
            }

            bool hasHeader = LooksLikeCsvHeader(rows[0]);
            var colMap = hasHeader
                ? BuildCsvColumnMap(rows[0])
                : BuildLegacyColumnMap();

            int startRowIdx = hasHeader ? 1 : 0;
            int dataRowCount = rows.Count - startRowIdx;
            if (dataRowCount <= 0)
            {
                result.Message = "CSV file contains headers but no question rows.";
                return (result, new List<Question>(), new List<ExamCsvSectionQuestions>());
            }

            if (dataRowCount > MaxCsvDataRows)
            {
                var limitMsg = $"CSV exceeds maximum limit of {MaxCsvDataRows} questions per upload (found {dataRowCount}).";
                result.TotalRows = dataRowCount;
                result.FailedCount = 1;
                result.Errors.Add(new CsvImportErrorDto { RowNumber = 1, Error = limitMsg });
                result.Message = limitMsg;
                return (result, new List<Question>(), new List<ExamCsvSectionQuestions>());
            }

            if (hasHeader && !colMap.ContainsKey("questiontext"))
            {
                const string missingColMsg = "Missing required 'QuestionText' column in CSV header.";
                result.TotalRows = dataRowCount;
                result.FailedCount = 1;
                result.Errors.Add(new CsvImportErrorDto { RowNumber = 1, Error = missingColMsg });
                result.Message = missingColMsg;
                return (result, new List<Question>(), new List<ExamCsvSectionQuestions>());
            }

            // Preload taxonomy collections to prevent N+1 queries during bulk import.
            var existingCategories = await _mongoDb.Categories.Find(_ => true).ToListAsync();
            var existingSubjects = await _mongoDb.Subjects.Find(_ => true).ToListAsync();
            var existingTopics = await _mongoDb.Topics.Find(_ => true).ToListAsync();
            var existingDifficulties = await _mongoDb.Difficulties.Find(_ => true).ToListAsync();

            var categoriesById = existingCategories.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
            var categoriesByName = existingCategories
                .GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var pendingCategories = new List<Category>();

            var subjectsById = existingSubjects.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
            var subjectsByName = existingSubjects
                .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var subjectsByCategoryAndName = existingSubjects
                .GroupBy(s => $"{s.CategoryId ?? string.Empty}|{s.Name.Trim()}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var pendingSubjects = new List<Subject>();

            var topicsById = existingTopics.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
            var topicsByName = existingTopics
                .GroupBy(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var topicsBySubjectAndName = existingTopics
                .GroupBy(t => $"{t.SubjectId ?? string.Empty}|{t.Name.Trim()}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var pendingTopics = new List<Topic>();

            var difficultiesById = existingDifficulties.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
            var difficultiesByName = existingDifficulties
                .GroupBy(d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var validQuestions = new List<Question>();
            var groupedSections = new List<ExamCsvSectionQuestions>();
            var sectionLookup = new Dictionary<string, ExamCsvSectionQuestions>(StringComparer.OrdinalIgnoreCase);

            for (int r = startRowIdx; r < rows.Count; r++)
            {
                int csvRowNumber = r + 1;
                var row = rows[r];
                var rawQuestionText = GetCsvField(row, colMap, "questiontext").Trim();
                var preview = rawQuestionText.Length > 80
                    ? rawQuestionText[..80] + "..."
                    : rawQuestionText;

                var (question, error, newCategory, newSubject, newTopic) = ParseCsvQuestionRow(
                    row,
                    colMap,
                    createdBy,
                    defaultCategoryId,
                    defaultSubjectId,
                    defaultTopicId,
                    defaultStatus,
                    categoriesById,
                    categoriesByName,
                    subjectsById,
                    subjectsByName,
                    subjectsByCategoryAndName,
                    topicsById,
                    topicsByName,
                    topicsBySubjectAndName,
                    difficultiesById,
                    difficultiesByName);

                if (error != null || question == null)
                {
                    result.Errors.Add(new CsvImportErrorDto
                    {
                        RowNumber = csvRowNumber,
                        QuestionPreview = preview,
                        Error = error ?? "Invalid row."
                    });
                    continue;
                }

                if (isExamExclusive)
                {
                    question.IsExamExclusive = true;
                    question.ExclusiveExamId = exclusiveExamId;
                }
                else
                {
                    if (newCategory != null)
                    {
                        categoriesById[newCategory.Id] = newCategory;
                        categoriesByName[newCategory.Name.Trim()] = newCategory;
                        pendingCategories.Add(newCategory);
                    }

                    if (newSubject != null)
                    {
                        subjectsById[newSubject.Id] = newSubject;
                        subjectsByName.TryAdd(newSubject.Name.Trim(), newSubject);
                        subjectsByCategoryAndName[$"{newSubject.CategoryId ?? string.Empty}|{newSubject.Name.Trim()}"] = newSubject;
                        pendingSubjects.Add(newSubject);
                    }

                    if (newTopic != null)
                    {
                        topicsById[newTopic.Id] = newTopic;
                        topicsByName.TryAdd(newTopic.Name.Trim(), newTopic);
                        topicsBySubjectAndName[$"{newTopic.SubjectId ?? string.Empty}|{newTopic.Name.Trim()}"] = newTopic;
                        pendingTopics.Add(newTopic);
                    }
                }

                var rowSection = GetCsvField(row, colMap, "section").Trim();
                var resolvedSectionTitle = string.IsNullOrWhiteSpace(rowSection) ? defaultSectionTitle : rowSection;
                if (!sectionLookup.TryGetValue(resolvedSectionTitle, out var secGroup))
                {
                    secGroup = new ExamCsvSectionQuestions { SectionTitle = resolvedSectionTitle };
                    sectionLookup[resolvedSectionTitle] = secGroup;
                    groupedSections.Add(secGroup);
                }
                secGroup.Questions.Add(question);

                validQuestions.Add(question);
            }

            result.TotalRows = dataRowCount;
            result.FailedCount = result.Errors.Count;

            if (!skipInvalidRows && result.Errors.Count > 0)
            {
                result.ImportedCount = 0;
                result.Message = $"Import aborted due to {result.Errors.Count} validation error(s). Fix errors or enable 'Skip Invalid Rows'.";
                return (result, new List<Question>(), new List<ExamCsvSectionQuestions>());
            }

            if (dryRun)
            {
                result.ImportedCount = validQuestions.Count;
                result.Questions = validQuestions.Take(50).Select(ToQuestionDto).ToList();
                result.Message = result.Errors.Count == 0
                    ? $"Validation passed for all {validQuestions.Count} question(s)."
                    : $"Validation completed: {validQuestions.Count} valid, {result.Errors.Count} invalid.";
                return (result, validQuestions, groupedSections);
            }

            if (validQuestions.Count > 0)
            {
                if (!isExamExclusive)
                {
                    if (pendingCategories.Count > 0)
                        await _mongoDb.Categories.InsertManyAsync(pendingCategories);
                    if (pendingSubjects.Count > 0)
                        await _mongoDb.Subjects.InsertManyAsync(pendingSubjects);
                    if (pendingTopics.Count > 0)
                        await _mongoDb.Topics.InsertManyAsync(pendingTopics);
                }

                await _mongoDb.Questions.InsertManyAsync(validQuestions);
            }

            result.ImportedCount = validQuestions.Count;
            result.Questions = validQuestions.Select(ToQuestionDto).ToList();
            result.Message = validQuestions.Count > 0
                ? (result.Errors.Count > 0
                    ? $"Imported {validQuestions.Count} question(s); skipped {result.Errors.Count} invalid row(s)."
                    : $"{validQuestions.Count} question(s) imported successfully.")
                : "No valid questions found in CSV.";

            return (result, validQuestions, groupedSections);
        }

        private static (Question? Question, string? Error, Category? NewCategory, Subject? NewSubject, Topic? NewTopic) ParseCsvQuestionRow(
            List<string> row,
            Dictionary<string, int> colMap,
            string createdBy,
            string? defaultCategoryId,
            string? defaultSubjectId,
            string? defaultTopicId,
            QuestionStatus? defaultStatus,
            Dictionary<string, Category> categoriesById,
            Dictionary<string, Category> categoriesByName,
            Dictionary<string, Subject> subjectsById,
            Dictionary<string, Subject> subjectsByName,
            Dictionary<string, Subject> subjectsByCategoryAndName,
            Dictionary<string, Topic> topicsById,
            Dictionary<string, Topic> topicsByName,
            Dictionary<string, Topic> topicsBySubjectAndName,
            Dictionary<string, Difficulty> difficultiesById,
            Dictionary<string, Difficulty> difficultiesByName)
        {
            var questionText = GetCsvField(row, colMap, "questiontext").Trim();
            if (string.IsNullOrWhiteSpace(questionText))
                return (null, "QuestionText is required.", null, null, null);
            if (questionText.Length > 20000)
                return (null, "QuestionText exceeds maximum length of 20,000 characters.", null, null, null);

            var typeRaw = GetCsvField(row, colMap, "type").Trim();
            if (!TryParseQuestionType(typeRaw, out var questionType))
                return (null, $"Invalid Question Type '{typeRaw}'. Expected MCQ, MultipleCorrect, TrueFalse, Subjective, LongAnswer, CodeEvaluation, or FillInTheBlanks.", null, null, null);

            var marksRaw = GetCsvField(row, colMap, "marks").Trim();
            double marks = 1.0;
            if (!string.IsNullOrEmpty(marksRaw))
            {
                if (!double.TryParse(marksRaw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out marks)
                    || marks <= 0 || marks > 1000)
                {
                    return (null, $"Marks '{marksRaw}' must be a positive number up to 1000.", null, null, null);
                }
            }

            var negMarksRaw = GetCsvField(row, colMap, "negativemarks").Trim();
            double? negativeMarks = null;
            if (!string.IsNullOrEmpty(negMarksRaw))
            {
                if (!double.TryParse(negMarksRaw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedNeg)
                    || parsedNeg < 0 || parsedNeg > 1000)
                {
                    return (null, $"NegativeMarks '{negMarksRaw}' must be a non-negative number.", null, null, null);
                }
                negativeMarks = parsedNeg;
            }

            var statusRaw = GetCsvField(row, colMap, "status").Trim();
            var status = defaultStatus ?? QuestionStatus.Draft;
            if (!string.IsNullOrEmpty(statusRaw))
            {
                if (!Enum.TryParse<QuestionStatus>(statusRaw, ignoreCase: true, out status) || !Enum.IsDefined(typeof(QuestionStatus), status))
                {
                    return (null, $"Invalid Status '{statusRaw}'. Expected Draft, PendingReview, Approved, Rejected, Published, or Archived.", null, null, null);
                }
            }

            var imageUrl = NullIfWhiteSpace(GetCsvField(row, colMap, "imageurl"));
            if (imageUrl != null && !IsSafeUrlOrRelativePath(imageUrl))
            {
                return (null, $"ImageUrl '{imageUrl}' must start with http://, https://, or '/'.", null, null, null);
            }

            // Build option list and letter/index lookup tables.
            var (options, optionAliasMap) = ExtractOptionsFromRow(row, colMap);
            var correctAnswerRaw = GetCsvField(row, colMap, "correctanswer").Trim();
            var correctOptionsRaw = GetCsvField(row, colMap, "correctoptions").Trim();

            string? resolvedCorrectAnswer = null;
            var resolvedCorrectOptions = new List<string>();

            switch (questionType)
            {
                case QuestionType.MCQ:
                {
                    if (options.Count < 2)
                        return (null, "MCQ question requires at least 2 options (OptionA, OptionB, ...).", null, null, null);

                    var rawToken = !string.IsNullOrEmpty(correctAnswerRaw) ? correctAnswerRaw : correctOptionsRaw;
                    if (string.IsNullOrWhiteSpace(rawToken))
                        return (null, "MCQ question requires CorrectAnswer (e.g. A, B, 1, or exact option text).", null, null, null);

                    var matched = ResolveSingleOption(rawToken, options, optionAliasMap);
                    if (matched == null)
                    {
                        var tokens = SplitCorrectOptionTokens(rawToken, options);
                        if (tokens.Count > 1)
                            return (null, "MCQ supports only one correct answer; use Type=MultipleCorrect for multiple correct options.", null, null, null);
                        return (null, $"CorrectAnswer '{rawToken}' did not match any provided option.", null, null, null);
                    }

                    resolvedCorrectOptions.Add(matched.Id);
                    resolvedCorrectAnswer = matched.Id;
                    break;
                }

                case QuestionType.TrueFalse:
                {
                    if (options.Count == 0)
                    {
                        var trueOpt = new QuestionOption { Id = Guid.NewGuid().ToString(), Text = "True" };
                        var falseOpt = new QuestionOption { Id = Guid.NewGuid().ToString(), Text = "False" };
                        options.Add(trueOpt);
                        options.Add(falseOpt);
                        optionAliasMap["A"] = trueOpt;
                        optionAliasMap["OPTIONA"] = trueOpt;
                        optionAliasMap["1"] = trueOpt;
                        optionAliasMap["T"] = trueOpt;
                        optionAliasMap["YES"] = trueOpt;
                        optionAliasMap["B"] = falseOpt;
                        optionAliasMap["OPTIONB"] = falseOpt;
                        optionAliasMap["2"] = falseOpt;
                        optionAliasMap["F"] = falseOpt;
                        optionAliasMap["NO"] = falseOpt;
                        optionAliasMap["0"] = falseOpt;
                    }
                    else if (options.Count < 2)
                    {
                        return (null, "TrueFalse question must provide 2 options or omit options to auto-generate True/False.", null, null, null);
                    }

                    var rawToken = !string.IsNullOrEmpty(correctAnswerRaw) ? correctAnswerRaw : correctOptionsRaw;
                    if (string.IsNullOrWhiteSpace(rawToken))
                        return (null, "TrueFalse question requires CorrectAnswer (True or False).", null, null, null);

                    var matched = ResolveSingleOption(rawToken, options, optionAliasMap);
                    if (matched == null)
                    {
                        if (rawToken.Equals("t", StringComparison.OrdinalIgnoreCase) || rawToken.Equals("yes", StringComparison.OrdinalIgnoreCase))
                            matched = options.FirstOrDefault(o => o.Text.Equals("True", StringComparison.OrdinalIgnoreCase)) ?? options[0];
                        else if (rawToken.Equals("f", StringComparison.OrdinalIgnoreCase) || rawToken.Equals("no", StringComparison.OrdinalIgnoreCase) || rawToken == "0")
                            matched = options.FirstOrDefault(o => o.Text.Equals("False", StringComparison.OrdinalIgnoreCase)) ?? options[1];
                    }

                    if (matched == null)
                        return (null, $"CorrectAnswer '{rawToken}' did not match True/False options.", null, null, null);

                    resolvedCorrectOptions.Add(matched.Id);
                    resolvedCorrectAnswer = matched.Id;
                    break;
                }

                case QuestionType.MultipleCorrect:
                {
                    if (options.Count < 2)
                        return (null, "MultipleCorrect question requires at least 2 options.", null, null, null);

                    var rawTokensStr = !string.IsNullOrEmpty(correctOptionsRaw) ? correctOptionsRaw : correctAnswerRaw;
                    if (string.IsNullOrWhiteSpace(rawTokensStr))
                        return (null, "MultipleCorrect question requires CorrectOptions (e.g. A|C or option texts separated by '|').", null, null, null);

                    var tokens = SplitCorrectOptionTokens(rawTokensStr, options);
                    if (tokens.Count == 0)
                        return (null, "MultipleCorrect question requires at least 1 correct option.", null, null, null);

                    foreach (var tok in tokens)
                    {
                        var matched = ResolveSingleOption(tok, options, optionAliasMap);
                        if (matched == null)
                            return (null, $"CorrectOptions entry '{tok}' did not match any provided option.", null, null, null);
                        if (!resolvedCorrectOptions.Contains(matched.Id))
                            resolvedCorrectOptions.Add(matched.Id);
                    }
                    break;
                }

                case QuestionType.FillInTheBlanks:
                {
                    var textAnswer = !string.IsNullOrEmpty(correctAnswerRaw) ? correctAnswerRaw : correctOptionsRaw;
                    if (string.IsNullOrWhiteSpace(textAnswer))
                        return (null, "FillInTheBlanks question requires CorrectAnswer.", null, null, null);
                    resolvedCorrectAnswer = textAnswer;
                    options.Clear();
                    break;
                }

                default:
                {
                    // Subjective, LongAnswer, CodeEvaluation
                    resolvedCorrectAnswer = !string.IsNullOrEmpty(correctAnswerRaw)
                        ? correctAnswerRaw
                        : (!string.IsNullOrEmpty(correctOptionsRaw) ? correctOptionsRaw : null);
                    options.Clear();
                    break;
                }
            }

            // Difficulty lookup/canonicalization
            var diffRaw = GetCsvField(row, colMap, "difficultyid");
            if (string.IsNullOrWhiteSpace(diffRaw))
                diffRaw = GetCsvField(row, colMap, "difficulty");
            var difficultyId = ResolveDifficulty(diffRaw, difficultiesById, difficultiesByName);

            // Category lookup/creation
            Category? createdCategory = null;
            var catIdRaw = NullIfWhiteSpace(GetCsvField(row, colMap, "categoryid"));
            var catNameRaw = NullIfWhiteSpace(GetCsvField(row, colMap, "category"));
            string? resolvedCategoryId = NullIfWhiteSpace(defaultCategoryId);
            if (catIdRaw != null)
            {
                if (categoriesById.TryGetValue(catIdRaw, out var catById))
                    resolvedCategoryId = catById.Id;
                else if (categoriesByName.TryGetValue(catIdRaw, out var catByName))
                    resolvedCategoryId = catByName.Id;
                else
                    resolvedCategoryId = catIdRaw;
            }
            else if (catNameRaw != null)
            {
                if (categoriesByName.TryGetValue(catNameRaw, out var existingCat))
                {
                    resolvedCategoryId = existingCat.Id;
                }
                else if (categoriesById.TryGetValue(catNameRaw, out var existingCatById))
                {
                    resolvedCategoryId = existingCatById.Id;
                }
                else
                {
                    createdCategory = new Category
                    {
                        Name = catNameRaw,
                        Description = "Created automatically during CSV question import",
                        CreatedAt = DateTime.UtcNow
                    };
                    resolvedCategoryId = createdCategory.Id;
                }
            }

            // Subject lookup/creation (scoped under resolved Category when specified)
            Subject? createdSubject = null;
            var subjectIdRaw = NullIfWhiteSpace(GetCsvField(row, colMap, "subjectid"));
            var subjectNameRaw = NullIfWhiteSpace(GetCsvField(row, colMap, "subject"));
            string? resolvedSubjectId = NullIfWhiteSpace(defaultSubjectId);
            if (subjectIdRaw != null)
            {
                if (subjectsById.TryGetValue(subjectIdRaw, out var subById))
                    resolvedSubjectId = subById.Id;
                else if (subjectsByName.TryGetValue(subjectIdRaw, out var subByName))
                    resolvedSubjectId = subByName.Id;
                else
                    resolvedSubjectId = subjectIdRaw;
            }
            else if (subjectNameRaw != null)
            {
                var catScopeKey = $"{resolvedCategoryId ?? string.Empty}|{subjectNameRaw}";
                if (subjectsByCategoryAndName.TryGetValue(catScopeKey, out var scopedSubject))
                {
                    resolvedSubjectId = scopedSubject.Id;
                }
                else if (subjectsById.TryGetValue(subjectNameRaw, out var existingSubById))
                {
                    resolvedSubjectId = existingSubById.Id;
                }
                else if (subjectsByName.TryGetValue(subjectNameRaw, out var existingSubByName) &&
                         (resolvedCategoryId == null || string.IsNullOrEmpty(existingSubByName.CategoryId) || existingSubByName.CategoryId == resolvedCategoryId))
                {
                    resolvedSubjectId = existingSubByName.Id;
                }
                else
                {
                    createdSubject = new Subject
                    {
                        Name = subjectNameRaw,
                        CategoryId = resolvedCategoryId ?? string.Empty,
                        CreatedAt = DateTime.UtcNow
                    };
                    resolvedSubjectId = createdSubject.Id;
                }
            }

            // Topic lookup/creation (scoped under resolved Subject when specified)
            Topic? createdTopic = null;
            var topicIdRaw = NullIfWhiteSpace(GetCsvField(row, colMap, "topicid"));
            var topicNameRaw = NullIfWhiteSpace(GetCsvField(row, colMap, "topic"));
            string? resolvedTopicId = NullIfWhiteSpace(defaultTopicId);
            if (topicIdRaw != null)
            {
                if (topicsById.TryGetValue(topicIdRaw, out var topById))
                    resolvedTopicId = topById.Id;
                else if (topicsByName.TryGetValue(topicIdRaw, out var topByName))
                    resolvedTopicId = topByName.Id;
                else
                    resolvedTopicId = topicIdRaw;
            }
            else if (topicNameRaw != null)
            {
                var subScopeKey = $"{resolvedSubjectId ?? string.Empty}|{topicNameRaw}";
                if (topicsBySubjectAndName.TryGetValue(subScopeKey, out var scopedTopic))
                {
                    resolvedTopicId = scopedTopic.Id;
                }
                else if (topicsById.TryGetValue(topicNameRaw, out var existingTopById))
                {
                    resolvedTopicId = existingTopById.Id;
                }
                else if (topicsByName.TryGetValue(topicNameRaw, out var existingTopByName) &&
                         (resolvedSubjectId == null || string.IsNullOrEmpty(existingTopByName.SubjectId) || existingTopByName.SubjectId == resolvedSubjectId))
                {
                    resolvedTopicId = existingTopByName.Id;
                }
                else
                {
                    createdTopic = new Topic
                    {
                        Name = topicNameRaw,
                        SubjectId = resolvedSubjectId ?? string.Empty,
                        CreatedAt = DateTime.UtcNow
                    };
                    resolvedTopicId = createdTopic.Id;
                }
            }

            var explanation = NullIfWhiteSpace(GetCsvField(row, colMap, "explanation"));
            var codeSnippet = NullIfWhiteSpace(GetCsvField(row, colMap, "codesnippet"));

            var question = new Question
            {
                QuestionText = questionText,
                Type = questionType,
                Options = options,
                CorrectAnswer = resolvedCorrectAnswer,
                CorrectOptions = resolvedCorrectOptions,
                Marks = marks,
                NegativeMarks = negativeMarks,
                DifficultyId = difficultyId,
                CategoryId = resolvedCategoryId,
                SubjectId = resolvedSubjectId,
                TopicId = resolvedTopicId,
                Tags = new List<string>(),
                Explanation = explanation,
                ImageUrl = imageUrl,
                CodeSnippet = codeSnippet,
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow,
                Status = status,
                Version = 1,
                IsActive = true
            };

            return (question, null, createdCategory, createdSubject, createdTopic);
        }

        private static string ResolveDifficulty(
            string? raw,
            Dictionary<string, Difficulty> byId,
            Dictionary<string, Difficulty> byName)
        {
            var trimmed = (raw ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed)) return "Medium";
            if (byId.TryGetValue(trimmed, out var dById)) return dById.Name;
            if (byName.TryGetValue(trimmed, out var dByName)) return dByName.Name;

            return trimmed.ToLowerInvariant() switch
            {
                "easy" => "Easy",
                "medium" => "Medium",
                "hard" => "Hard",
                "expert" => "Expert",
                _ => trimmed
            };
        }

        private static bool TryParseQuestionType(string raw, out QuestionType result)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                result = QuestionType.MCQ;
                return true;
            }

            if (int.TryParse(raw, out var num) && Enum.IsDefined(typeof(QuestionType), num))
            {
                result = (QuestionType)num;
                return true;
            }

            if (Enum.TryParse<QuestionType>(raw, ignoreCase: true, out result) && Enum.IsDefined(typeof(QuestionType), result))
                return true;

            var normalized = NormalizeCsvHeader(raw);
            switch (normalized)
            {
                case "mcq":
                case "multiplechoice":
                case "singlechoice":
                case "single":
                    result = QuestionType.MCQ;
                    return true;
                case "multiplecorrect":
                case "multicorrect":
                case "multiselect":
                case "checkbox":
                case "multipleanswers":
                    result = QuestionType.MultipleCorrect;
                    return true;
                case "truefalse":
                case "tf":
                case "boolean":
                case "bool":
                    result = QuestionType.TrueFalse;
                    return true;
                case "subjective":
                case "shortanswer":
                case "short":
                    result = QuestionType.Subjective;
                    return true;
                case "longanswer":
                case "essay":
                case "long":
                    result = QuestionType.LongAnswer;
                    return true;
                case "codeevaluation":
                case "code":
                case "coding":
                case "programming":
                    result = QuestionType.CodeEvaluation;
                    return true;
                case "fillintheblanks":
                case "fillintheblank":
                case "fillblank":
                case "blank":
                case "fib":
                    result = QuestionType.FillInTheBlanks;
                    return true;
                default:
                    result = QuestionType.MCQ;
                    return false;
            }
        }

        private static (List<QuestionOption> Options, Dictionary<string, QuestionOption> AliasMap) ExtractOptionsFromRow(
            List<string> row,
            Dictionary<string, int> colMap)
        {
            var options = new List<QuestionOption>();
            var aliases = new Dictionary<string, QuestionOption>(StringComparer.OrdinalIgnoreCase);

            var letterKeys = new[]
            {
                ("optiona", "option1", "A", "1"),
                ("optionb", "option2", "B", "2"),
                ("optionc", "option3", "C", "3"),
                ("optiond", "option4", "D", "4"),
                ("optione", "option5", "E", "5"),
                ("optionf", "option6", "F", "6")
            };

            foreach (var (letterKey, numKey, letterLabel, numLabel) in letterKeys)
            {
                var text = GetCsvField(row, colMap, letterKey).Trim();
                if (string.IsNullOrEmpty(text))
                    text = GetCsvField(row, colMap, numKey).Trim();
                if (string.IsNullOrEmpty(text))
                    continue;

                var opt = new QuestionOption { Id = Guid.NewGuid().ToString(), Text = text };
                options.Add(opt);
                aliases[letterLabel] = opt;
                aliases[$"OPTION{letterLabel}"] = opt;
                aliases[numLabel] = opt;
                aliases[$"OPTION{numLabel}"] = opt;
            }

            if (options.Count == 0 && colMap.ContainsKey("options"))
            {
                var rawOpts = GetCsvField(row, colMap, "options").Trim();
                if (!string.IsNullOrEmpty(rawOpts))
                {
                    IEnumerable<string> parsed = Array.Empty<string>();
                    if (rawOpts.StartsWith("[", StringComparison.Ordinal))
                    {
                        try
                        {
                            parsed = JsonSerializer.Deserialize<List<string>>(rawOpts) ?? new List<string>();
                        }
                        catch
                        {
                            parsed = rawOpts.Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries);
                        }
                    }
                    else
                    {
                        parsed = rawOpts.Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    }

                    int idx = 0;
                    foreach (var item in parsed.Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)))
                    {
                        var opt = new QuestionOption { Id = Guid.NewGuid().ToString(), Text = item };
                        options.Add(opt);
                        if (idx < 26)
                        {
                            var letter = ((char)('A' + idx)).ToString();
                            aliases[letter] = opt;
                            aliases[$"OPTION{letter}"] = opt;
                        }
                        var oneBased = (idx + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        aliases[oneBased] = opt;
                        aliases[$"OPTION{oneBased}"] = opt;
                        idx++;
                    }
                }
            }

            return (options, aliases);
        }

        private static QuestionOption? ResolveSingleOption(
            string rawToken,
            List<QuestionOption> options,
            Dictionary<string, QuestionOption> aliasMap)
        {
            var token = rawToken.Trim();
            if (string.IsNullOrEmpty(token)) return null;

            // 1. Exact option text match (highest priority if option text happens to be e.g. "A" or "1")
            var exactText = options.FirstOrDefault(o => o.Text.Trim().Equals(token, StringComparison.OrdinalIgnoreCase));
            if (exactText != null) return exactText;

            // 2. Option ID match
            var byId = options.FirstOrDefault(o => o.Id.Equals(token, StringComparison.OrdinalIgnoreCase));
            if (byId != null) return byId;

            // 3. Normalized letter/index alias ("A", "Option A", "OptionA", "1", "Option 1")
            var compact = NormalizeCsvHeader(token).ToUpperInvariant();
            if (aliasMap.TryGetValue(compact, out var byAlias))
                return byAlias;

            return null;
        }

        private static List<string> SplitCorrectOptionTokens(string raw, List<QuestionOption> options)
        {
            var trimmed = raw.Trim();
            if (string.IsNullOrEmpty(trimmed)) return new List<string>();

            // If entire string matches a single option text literally (e.g. option contains commas/pipes), keep as 1 token
            if (options.Any(o => o.Text.Trim().Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
                return new List<string> { trimmed };

            var primary = trimmed.Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            if (primary.Count == 1 && primary[0].Contains(',') &&
                !options.Any(o => o.Text.Trim().Equals(primary[0], StringComparison.OrdinalIgnoreCase)))
            {
                return primary[0].Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }

            return primary;
        }

        private static bool LooksLikeCsvHeader(List<string> firstRow)
        {
            foreach (var cell in firstRow)
            {
                var norm = NormalizeCsvHeader(cell);
                if (norm is "section" or "sectiontitle" or "sectionname" or "questiontext" or "question" or "optiona" or "option1" or "correctanswer" or "correctoptions" or "marks" or "difficulty" or "difficultyid" or "category" or "categoryid" or "explanation" or "tags")
                    return true;
            }
            return false;
        }

        private static Dictionary<string, int> BuildCsvColumnMap(List<string> headerRow)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headerRow.Count; i++)
            {
                var norm = NormalizeCsvHeader(headerRow[i]);
                if (string.IsNullOrEmpty(norm)) continue;

                var canonical = norm switch
                {
                    "section" or "sectiontitle" or "sectionname" => "section",
                    "questiontext" or "question" or "prompt" or "text" => "questiontext",
                    "type" or "questiontype" => "type",
                    "optiona" => "optiona",
                    "optionb" => "optionb",
                    "optionc" => "optionc",
                    "optiond" => "optiond",
                    "optione" => "optione",
                    "optionf" => "optionf",
                    "option1" => "option1",
                    "option2" => "option2",
                    "option3" => "option3",
                    "option4" => "option4",
                    "option5" => "option5",
                    "option6" => "option6",
                    "options" => "options",
                    "correctanswer" or "answer" or "correct" => "correctanswer",
                    "correctoptions" or "correctoption" or "answers" => "correctoptions",
                    "marks" or "mark" or "points" or "score" => "marks",
                    "negativemarks" or "penalty" => "negativemarks",
                    "difficulty" or "difficultylevel" => "difficulty",
                    "difficultyid" => "difficultyid",
                    "category" => "category",
                    "categoryid" => "categoryid",
                    "subject" => "subject",
                    "subjectid" => "subjectid",
                    "topic" => "topic",
                    "topicid" => "topicid",
                    "tags" or "tag" => "tags",
                    "explanation" => "explanation",
                    "codesnippet" or "code" => "codesnippet",
                    "imageurl" or "image" => "imageurl",
                    "status" => "status",
                    _ => norm
                };

                map.TryAdd(canonical, i);
            }
            return map;
        }

        private static Dictionary<string, int> BuildLegacyColumnMap() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["questiontext"] = 0,
            ["type"] = 1,
            ["marks"] = 2,
            ["correctanswer"] = 3,
            ["difficultyid"] = 4
        };

        private static string GetCsvField(List<string> row, Dictionary<string, int> colMap, string key)
        {
            if (colMap.TryGetValue(key, out int idx) && idx >= 0 && idx < row.Count)
                return row[idx];
            return string.Empty;
        }

        private static string NormalizeCsvHeader(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw)
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        private static string? NullIfWhiteSpace(string? val) =>
            string.IsNullOrWhiteSpace(val) ? null : val.Trim();

        private static bool IsSafeUrlOrRelativePath(string url) =>
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || (url.StartsWith("/", StringComparison.Ordinal) && !url.StartsWith("//", StringComparison.Ordinal));

        /// <summary>
        /// Parses RFC 4180 CSV text including UTF-8 BOM stripping, escaped quotes, and multi-line quoted fields.
        /// </summary>
        public static List<List<string>> ParseCsvRecords(string csvText)
        {
            var records = new List<List<string>>();
            if (string.IsNullOrWhiteSpace(csvText)) return records;

            var text = csvText.TrimStart('\uFEFF');
            var currentRow = new List<string>();
            var currentField = new StringBuilder();
            bool inQuotes = false;
            bool fieldStarted = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            currentField.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        currentField.Append(c);
                    }
                    continue;
                }

                if (c == '"' && !fieldStarted)
                {
                    inQuotes = true;
                    fieldStarted = true;
                }
                else if (c == ',')
                {
                    currentRow.Add(currentField.ToString());
                    currentField.Clear();
                    fieldStarted = false;
                }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        i++;

                    currentRow.Add(currentField.ToString());
                    currentField.Clear();
                    fieldStarted = false;

                    if (currentRow.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                        records.Add(currentRow);
                    currentRow = new List<string>();
                }
                else
                {
                    currentField.Append(c);
                    fieldStarted = true;
                }
            }

            if (inQuotes)
                throw new InvalidOperationException("Malformed CSV: unterminated quoted field.");

            if (currentField.Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(currentField.ToString());
                if (currentRow.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                    records.Add(currentRow);
            }

            return records;
        }

        public async Task<(byte[] Bytes, string ContentType, string FileName)> ExportQuestionsAsync(ExportQuestionsRequest filterRequest)
        {
            var fb = Builders<Question>.Filter;
            var filters = new List<FilterDefinition<Question>> { fb.Eq(q => q.IsActive, true) };

            if (filterRequest.CategoryIds != null && filterRequest.CategoryIds.Count > 0)
                filters.Add(fb.In(q => q.CategoryId, filterRequest.CategoryIds));
            if (filterRequest.SubjectIds != null && filterRequest.SubjectIds.Count > 0)
                filters.Add(fb.In(q => q.SubjectId, filterRequest.SubjectIds));
            if (filterRequest.Status.HasValue)
                filters.Add(fb.Eq(q => q.Status, filterRequest.Status.Value));

            var questions = await _mongoDb.Questions.Find(fb.And(filters)).ToListAsync();
            var dtos = questions.Select(ToQuestionWithAnswer).ToList();

            if (filterRequest.Format.Equals("csv", StringComparison.OrdinalIgnoreCase))
            {
                var sb = new StringBuilder();
                sb.AppendLine("Id,QuestionText,Type,Marks,CorrectAnswer,DifficultyId,Status");
                foreach (var q in dtos)
                {
                    sb.AppendLine(string.Join(",",
                        CsvEscape(q.Id),
                        CsvEscape(q.QuestionText),
                        CsvEscape(q.Type.ToString()),
                        q.Marks.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        CsvEscape(q.CorrectAnswer ?? string.Join("|", q.CorrectOptions)),
                        CsvEscape(q.DifficultyId),
                        CsvEscape(q.Status.ToString())));
                }
                return (Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"questions_export_{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
            }

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(dtos, new JsonSerializerOptions { WriteIndented = true });
            return (jsonBytes, "application/json", $"questions_export_{DateTime.UtcNow:yyyyMMddHHmmss}.json");
        }

        private static Question MapImportRequest(CreateQuestionRequest req, string createdBy) => new()
        {
            QuestionText = req.QuestionText,
            Type = req.Type,
            Options = req.Options ?? new List<QuestionOption>(),
            CorrectAnswer = req.CorrectAnswer,
            CorrectOptions = req.CorrectOptions ?? new List<string>(),
            Marks = req.Marks > 0 ? req.Marks : 1.0,
            NegativeMarks = req.NegativeMarks,
            DifficultyId = req.DifficultyId,
            CategoryId = req.CategoryId,
            SubjectId = req.SubjectId,
            TopicId = req.TopicId,
            Tags = new List<string>(),
            Explanation = req.Explanation,
            ImageUrl = req.ImageUrl,
            CodeSnippet = req.CodeSnippet,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            Status = QuestionStatus.Draft,
            Version = 1,
            IsActive = true
        };

        private static string CsvEscape(string? val)
        {
            var s = val ?? string.Empty;
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
                return $"\"{s.Replace("\"", "\"\"")}\"";
            return s;
        }

        private static List<string> SplitCsvLine(string line) =>
            ParseCsvRecords(line).FirstOrDefault() ?? new List<string>();

        #endregion

        #region 11. Question Review Queue

        public async Task<List<Question>> GetReviewQueueAsync()
        {
            return await _mongoDb.Questions
                .Find(q => q.Status == QuestionStatus.PendingReview && q.IsActive)
                .SortBy(q => q.UpdatedAt)
                .ToListAsync();
        }

        public async Task<bool> AddReviewLogAsync(string questionId, string reviewerId, QuestionStatus action, string comment)
        {
            var log = new ReviewLog
            {
                ReviewerId = reviewerId,
                Action = action,
                Comment = comment,
                Timestamp = DateTime.UtcNow
            };

            var update = Builders<Question>.Update
                .Set(q => q.Status, action)
                .Set(q => q.UpdatedAt, DateTime.UtcNow)
                .Push(q => q.ReviewHistory, log);

            var result = await _mongoDb.Questions.UpdateOneAsync(q => q.Id == questionId, update);
            return result.MatchedCount > 0;
        }

        #endregion

        #region Mappers

        public static QuestionDto ToQuestionDto(Question question)
        {
            return new QuestionDto
            {
                Id = question.Id,
                QuestionText = question.QuestionText,
                Type = question.Type,
                Options = question.Options,
                Marks = question.Marks,
                DifficultyId = question.DifficultyId,
                CategoryId = question.CategoryId,
                SubjectId = question.SubjectId,
                TopicId = question.TopicId,
                Tags = question.Tags,
                ImageUrl = question.ImageUrl,
                CodeSnippet = question.CodeSnippet,
                Status = question.Status,
                Version = question.Version,
                IsExamExclusive = question.IsExamExclusive,
                ExclusiveExamId = question.ExclusiveExamId,
                CreatedAt = question.CreatedAt
            };
        }

        public static QuestionWithAnswer ToQuestionWithAnswer(Question question)
        {
            return new QuestionWithAnswer
            {
                Id = question.Id,
                QuestionText = question.QuestionText,
                Type = question.Type,
                Options = question.Options,
                Marks = question.Marks,
                DifficultyId = question.DifficultyId,
                CategoryId = question.CategoryId,
                SubjectId = question.SubjectId,
                TopicId = question.TopicId,
                Tags = question.Tags,
                ImageUrl = question.ImageUrl,
                CodeSnippet = question.CodeSnippet,
                Status = question.Status,
                Version = question.Version,
                IsExamExclusive = question.IsExamExclusive,
                ExclusiveExamId = question.ExclusiveExamId,
                CreatedAt = question.CreatedAt,
                CorrectAnswer = question.CorrectAnswer,
                CorrectOptions = question.CorrectOptions,
                Explanation = question.Explanation,
                ReviewHistory = question.ReviewHistory
            };
        }

        #endregion
    }
}
