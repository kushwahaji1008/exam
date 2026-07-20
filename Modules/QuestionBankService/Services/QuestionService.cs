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
                Difficulty = request.Difficulty,
                Category = request.Category,
                Tags = request.Tags,
                Explanation = request.Explanation,
                ImageUrl = request.ImageUrl,
                CodeSnippet = request.CodeSnippet,
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            await _mongoDb.Questions.InsertOneAsync(question);
            return question;
        }

        public async Task<List<Question>> GetAllQuestionsAsync(string? category = null, DifficultyLevel? difficulty = null)
        {
            var filterBuilder = Builders<Question>.Filter;
            var filters = new List<FilterDefinition<Question>>
            {
                filterBuilder.Eq(q => q.IsActive, true)
            };

            if (!string.IsNullOrEmpty(category))
            {
                filters.Add(filterBuilder.Eq(q => q.Category, category));
            }

            if (difficulty.HasValue)
            {
                filters.Add(filterBuilder.Eq(q => q.Difficulty, difficulty.Value));
            }

            var filter = filterBuilder.And(filters);
            return await _mongoDb.Questions.Find(filter).SortByDescending(q => q.CreatedAt).ToListAsync();
        }

        public async Task<Question?> GetQuestionByIdAsync(string questionId)
        {
            return await _mongoDb.Questions.Find(q => q.Id == questionId).FirstOrDefaultAsync();
        }

        public async Task<List<Question>> GetQuestionsByIdsAsync(List<string> questionIds)
        {
            var filter = Builders<Question>.Filter.In(q => q.Id, questionIds);
            return await _mongoDb.Questions.Find(filter).ToListAsync();
        }

        public async Task<bool> UpdateQuestionAsync(string questionId, Question updatedQuestion)
        {
            updatedQuestion.UpdatedAt = DateTime.UtcNow;
            var result = await _mongoDb.Questions.ReplaceOneAsync(q => q.Id == questionId, updatedQuestion);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> DeleteQuestionAsync(string questionId)
        {
            // Soft delete
            var update = Builders<Question>.Update.Set(q => q.IsActive, false);
            var result = await _mongoDb.Questions.UpdateOneAsync(q => q.Id == questionId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<List<string>> GetCategoriesAsync()
        {
            var categories = await _mongoDb.Questions
                .Distinct(q => q.Category, q => q.IsActive == true)
                .ToListAsync();
            
            return categories.Where(c => !string.IsNullOrEmpty(c)).ToList()!;
        }

        public async Task<List<string>> GetTagsAsync()
        {
            var questions = await _mongoDb.Questions.Find(q => q.IsActive == true).ToListAsync();
            var allTags = questions.SelectMany(q => q.Tags).Distinct().ToList();
            return allTags;
        }

        public static QuestionDto ToQuestionDto(Question question)
        {
            return new QuestionDto
            {
                Id = question.Id,
                QuestionText = question.QuestionText,
                Type = question.Type,
                Options = question.Options,
                Marks = question.Marks,
                Difficulty = question.Difficulty,
                Category = question.Category,
                Tags = question.Tags,
                ImageUrl = question.ImageUrl,
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
                Difficulty = question.Difficulty,
                Category = question.Category,
                Tags = question.Tags,
                ImageUrl = question.ImageUrl,
                CreatedAt = question.CreatedAt,
                CorrectAnswer = question.CorrectAnswer,
                CorrectOptions = question.CorrectOptions,
                Explanation = question.Explanation
            };
        }
    }
}