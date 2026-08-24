using Tag = QuestionBankService.Models.Tag;
using MongoDB.Driver;
using QuestionBankService.Models;

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
                Options = request.Options,
                CorrectAnswer = request.CorrectAnswer,
                CorrectOptions = request.CorrectOptions,
                Marks = request.Marks,
                NegativeMarks = request.NegativeMarks,
                DifficultyId = request.DifficultyId,
                CategoryId = request.CategoryId,
                SubjectId = request.SubjectId,
                TopicId = request.TopicId,
                Tags = request.Tags,
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

        // Note: Switched DifficultyLevel enum to string difficultyId to match the new dynamic model
        public async Task<List<Question>> GetAllQuestionsAsync(string? categoryId = null, string? difficultyId = null)
        {
            var filterBuilder = Builders<Question>.Filter;
            var filters = new List<FilterDefinition<Question>>
            {
                filterBuilder.Eq(q => q.IsActive, true)
            };

            if (!string.IsNullOrEmpty(categoryId))
            {
                filters.Add(filterBuilder.Eq(q => q.CategoryId, categoryId));
            }

            if (!string.IsNullOrEmpty(difficultyId))
            {
                filters.Add(filterBuilder.Eq(q => q.DifficultyId, difficultyId));
            }

            var filter = filterBuilder.And(filters);
            return await _mongoDb.Questions.Find(filter).SortByDescending(q => q.CreatedAt).ToListAsync();
        }

        public async Task<Question?> GetQuestionByIdAsync(string questionId)
        {
            return await _mongoDb.Questions.Find(q => q.Id == questionId).FirstOrDefaultAsync();
        }

        public async Task<bool> UpdateQuestionAsync(string questionId, Question updatedQuestion)
        {
            updatedQuestion.UpdatedAt = DateTime.UtcNow;
            var result = await _mongoDb.Questions.ReplaceOneAsync(q => q.Id == questionId, updatedQuestion);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> DeleteQuestionAsync(string questionId)
        {
            // Soft delete + Archive status
            var update = Builders<Question>.Update
                .Set(q => q.IsActive, false)
                .Set(q => q.Status, QuestionStatus.Archived)
                .Set(q => q.UpdatedAt, DateTime.UtcNow);

            var result = await _mongoDb.Questions.UpdateOneAsync(q => q.Id == questionId, update);
            return result.ModifiedCount > 0;
        }

        #endregion

        #region 2. Question Lifecycle

        public async Task<bool> UpdateQuestionStatusAsync(string questionId, QuestionStatus newStatus, string? updatedBy = null)
        {
            var update = Builders<Question>.Update
                .Set(q => q.Status, newStatus)
                .Set(q => q.UpdatedAt, DateTime.UtcNow);

            if (updatedBy != null)
                update = update.Set(q => q.UpdatedBy, updatedBy);

            var result = await _mongoDb.Questions.UpdateOneAsync(q => q.Id == questionId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<Question?> DuplicateQuestionAsync(string questionId, string createdBy)
        {
            var existing = await GetQuestionByIdAsync(questionId);
            if (existing == null) return null;

            existing.Id = Guid.NewGuid().ToString();
            existing.ParentQuestionId = questionId;
            existing.Status = QuestionStatus.Draft;
            existing.Version = 1;
            existing.CreatedBy = createdBy;
            existing.CreatedAt = DateTime.UtcNow;
            existing.UpdatedBy = null;
            existing.UpdatedAt = null;
            existing.ReviewHistory = new List<ReviewLog>();

            // Generate new IDs for options to prevent reference overlaps
            foreach(var opt in existing.Options) { opt.Id = Guid.NewGuid().ToString(); }

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

        public async Task<bool> CreateQuestionVersionAsync(string questionId, string userId)
        {
            var question = await GetQuestionByIdAsync(questionId);
            if (question == null) return false;

            var snapshot = new QuestionVersion
            {
                OriginalQuestionId = question.Id,
                VersionNumber = question.Version,
                SnapshotData = question,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };

            await _mongoDb.QuestionVersions.InsertOneAsync(snapshot);

            // Increment version on the main question
            var update = Builders<Question>.Update.Inc(q => q.Version, 1);
            await _mongoDb.Questions.UpdateOneAsync(q => q.Id == questionId, update);

            return true;
        }

        public async Task<bool> RestoreQuestionVersionAsync(string questionId, string versionId, string userId)
        {
            var version = await GetQuestionVersionByIdAsync(versionId);
            if (version == null || version.OriginalQuestionId != questionId) return false;

            var restoredQuestion = version.SnapshotData;
            restoredQuestion.UpdatedBy = userId;
            restoredQuestion.UpdatedAt = DateTime.UtcNow;
            
            // Keep the current incremented version number, just revert the data
            var currentQuestion = await GetQuestionByIdAsync(questionId);
            if(currentQuestion != null) restoredQuestion.Version = currentQuestion.Version + 1;

            var result = await _mongoDb.Questions.ReplaceOneAsync(q => q.Id == questionId, restoredQuestion);
            return result.ModifiedCount > 0;
        }

        #endregion

        #region 4. Question Options

        public async Task<bool> AddQuestionOptionAsync(string questionId, OptionRequest optionRequest)
        {
            var newOption = new QuestionOption
            {
                Text = optionRequest.Text,
                ImageUrl = optionRequest.ImageUrl
            };

            var update = Builders<Question>.Update.Push(q => q.Options, newOption);
            var result = await _mongoDb.Questions.UpdateOneAsync(q => q.Id == questionId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> UpdateQuestionOptionAsync(string questionId, string optionId, OptionRequest optionRequest)
        {
            var filter = Builders<Question>.Filter.And(
                Builders<Question>.Filter.Eq(q => q.Id, questionId),
                Builders<Question>.Filter.ElemMatch(q => q.Options, o => o.Id == optionId)
            );

            var update = Builders<Question>.Update
                .Set("Options.$.Text", optionRequest.Text)
                .Set("Options.$.ImageUrl", optionRequest.ImageUrl);

            var result = await _mongoDb.Questions.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> DeleteQuestionOptionAsync(string questionId, string optionId)
        {
            var update = Builders<Question>.Update.PullFilter(q => q.Options, o => o.Id == optionId);
            var result = await _mongoDb.Questions.UpdateOneAsync(q => q.Id == questionId, update);
            return result.ModifiedCount > 0;
        }

        #endregion

        #region 5. Categories

        public async Task<List<Category>> GetCategoriesAsync() => await _mongoDb.Categories.Find(_ => true).ToListAsync();
        public async Task<Category?> GetCategoryByIdAsync(string id) => await _mongoDb.Categories.Find(c => c.Id == id).FirstOrDefaultAsync();
        public async Task<Category> CreateCategoryAsync(Category category) { await _mongoDb.Categories.InsertOneAsync(category); return category; }
        public async Task<bool> UpdateCategoryAsync(string id, Category category) { var res = await _mongoDb.Categories.ReplaceOneAsync(c => c.Id == id, category); return res.ModifiedCount > 0; }
        public async Task<bool> DeleteCategoryAsync(string id) { var res = await _mongoDb.Categories.DeleteOneAsync(c => c.Id == id); return res.DeletedCount > 0; }

        #endregion

        #region 6. Subjects

        public async Task<List<Subject>> GetSubjectsAsync() => await _mongoDb.Subjects.Find(_ => true).ToListAsync();
        public async Task<Subject?> GetSubjectByIdAsync(string id) => await _mongoDb.Subjects.Find(s => s.Id == id).FirstOrDefaultAsync();
        public async Task<Subject> CreateSubjectAsync(Subject subject) { await _mongoDb.Subjects.InsertOneAsync(subject); return subject; }
        public async Task<bool> UpdateSubjectAsync(string id, Subject subject) { var res = await _mongoDb.Subjects.ReplaceOneAsync(s => s.Id == id, subject); return res.ModifiedCount > 0; }
        public async Task<bool> DeleteSubjectAsync(string id) { var res = await _mongoDb.Subjects.DeleteOneAsync(s => s.Id == id); return res.DeletedCount > 0; }

        #endregion

        #region 7. Topics

        public async Task<List<Topic>> GetTopicsAsync() => await _mongoDb.Topics.Find(_ => true).ToListAsync();
        public async Task<Topic?> GetTopicByIdAsync(string id) => await _mongoDb.Topics.Find(t => t.Id == id).FirstOrDefaultAsync();
        public async Task<Topic> CreateTopicAsync(Topic topic) { await _mongoDb.Topics.InsertOneAsync(topic); return topic; }
        public async Task<bool> UpdateTopicAsync(string id, Topic topic) { var res = await _mongoDb.Topics.ReplaceOneAsync(t => t.Id == id, topic); return res.ModifiedCount > 0; }
        public async Task<bool> DeleteTopicAsync(string id) { var res = await _mongoDb.Topics.DeleteOneAsync(t => t.Id == id); return res.DeletedCount > 0; }

        #endregion

        #region 8. Difficulty

        public async Task<List<Difficulty>> GetDifficultiesAsync() => await _mongoDb.Difficulties.Find(_ => true).ToListAsync();
        public async Task<Difficulty> CreateDifficultyAsync(Difficulty difficulty) { await _mongoDb.Difficulties.InsertOneAsync(difficulty); return difficulty; }
        public async Task<bool> UpdateDifficultyAsync(string id, Difficulty diff) { var res = await _mongoDb.Difficulties.ReplaceOneAsync(d => d.Id == id, diff); return res.ModifiedCount > 0; }
        public async Task<bool> DeleteDifficultyAsync(string id) { var res = await _mongoDb.Difficulties.DeleteOneAsync(d => d.Id == id); return res.DeletedCount > 0; }

        #endregion

        #region 9. Tags

        public async Task<List<Tag>> GetTagsAsync() => await _mongoDb.Tags.Find(_ => true).ToListAsync();
        public async Task<Tag> CreateTagAsync(Tag tag) { await _mongoDb.Tags.InsertOneAsync(tag); return tag; }
        public async Task<bool> UpdateTagAsync(string id, Tag tag) { var res = await _mongoDb.Tags.ReplaceOneAsync(t => t.Id == id, tag); return res.ModifiedCount > 0; }
        public async Task<bool> DeleteTagAsync(string id) { var res = await _mongoDb.Tags.DeleteOneAsync(t => t.Id == id); return res.DeletedCount > 0; }

        #endregion

        #region 10. Bulk Operations

        public async Task<List<Question>> GetQuestionsByIdsAsync(List<string> questionIds)
        {
            var filter = Builders<Question>.Filter.In(q => q.Id, questionIds);
            return await _mongoDb.Questions.Find(filter).ToListAsync();
        }

        public async Task<long> BulkUpdateStatusAsync(List<string> questionIds, QuestionStatus status)
        {
            var filter = Builders<Question>.Filter.In(q => q.Id, questionIds);
            var update = Builders<Question>.Update.Set(q => q.Status, status).Set(q => q.UpdatedAt, DateTime.UtcNow);
            
            if (status == QuestionStatus.Archived)
            {
                update = update.Set(q => q.IsActive, false);
            }

            var result = await _mongoDb.Questions.UpdateManyAsync(filter, update);
            return result.ModifiedCount;
        }

        public async Task<long> BulkDeleteAsync(List<string> questionIds)
        {
            var filter = Builders<Question>.Filter.In(q => q.Id, questionIds);
            var result = await _mongoDb.Questions.DeleteManyAsync(filter);
            return result.DeletedCount;
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
            return result.ModifiedCount > 0;
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