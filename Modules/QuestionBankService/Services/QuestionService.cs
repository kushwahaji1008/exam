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
                Tags = request.Tags ?? new List<string>(),
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

        public async Task<List<Subject>> GetSubjectsAsync() => await _mongoDb.Subjects.Find(_ => true).ToListAsync();
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

        public async Task<List<Topic>> GetTopicsAsync() => await _mongoDb.Topics.Find(_ => true).ToListAsync();
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
                    var lines = (csvEl.GetString() ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var cols = SplitCsvLine(lines[i]);
                        if (i == 0 && cols.FirstOrDefault()?.Equals("QuestionText", StringComparison.OrdinalIgnoreCase) == true)
                            continue;
                        if (cols.Count < 1 || string.IsNullOrWhiteSpace(cols[0])) continue;

                        Enum.TryParse<QuestionType>(cols.ElementAtOrDefault(1), true, out var qType);
                        double.TryParse(cols.ElementAtOrDefault(2), out var marks);
                        toInsert.Add(new Question
                        {
                            QuestionText = cols[0],
                            Type = qType,
                            Marks = marks > 0 ? marks : 1.0,
                            CorrectAnswer = cols.ElementAtOrDefault(3),
                            DifficultyId = cols.ElementAtOrDefault(4) ?? string.Empty,
                            CreatedBy = createdBy,
                            Status = QuestionStatus.Draft,
                            IsActive = true
                        });
                    }
                }
            }

            if (toInsert.Count > 0)
            {
                await _mongoDb.Questions.InsertManyAsync(toInsert);
            }

            return (toInsert.Count, toInsert);
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
            Tags = req.Tags ?? new List<string>(),
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

        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else if (c == '"') inQuotes = false;
                    else current.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { result.Add(current.ToString()); current.Clear(); }
                    else current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result;
        }

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
