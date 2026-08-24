using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using QuestionBankService.Models;
using QuestionBankService.Services;
using System.Security.Claims;

namespace QuestionBankService.Controllers.V1
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/questions")]
    public class QuestionsController : ControllerBase
    {
        private readonly QuestionService _questionService;
        private readonly ILogger<QuestionsController> _logger;

        public QuestionsController(QuestionService questionService, ILogger<QuestionsController> logger)
        {
            _questionService = questionService;
            _logger = logger;
        }

        #region 1. Questions (CRUD)

        [HttpGet]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetAllQuestions([FromQuery] string? categoryId, [FromQuery] string? difficultyId)
        {
            // Now safely passing string IDs to the service
            var questions = await _questionService.GetAllQuestionsAsync(categoryId, difficultyId);
            var questionDtos = questions.Select(QuestionService.ToQuestionDto).ToList();
            return Ok(questionDtos);
        }

        [HttpPost]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateQuestion([FromBody] CreateQuestionRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var question = await _questionService.CreateQuestionAsync(request, userId);

            return Ok(new { message = "Question created successfully", question = QuestionService.ToQuestionDto(question) });
        }

        [HttpGet("{questionId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetQuestionById(string questionId)
        {
            var question = await _questionService.GetQuestionByIdAsync(questionId);
            if (question == null) return NotFound(new { message = "Question not found" });

            return Ok(QuestionService.ToQuestionWithAnswer(question));
        }

        [HttpPut("{questionId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateQuestion(string questionId, [FromBody] Question updatedQuestion)
        {
            var userId = User.FindFirst("userId")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var question = await _questionService.GetQuestionByIdAsync(questionId);
            if (question == null) return NotFound(new { message = "Question not found" });

            if (question.CreatedBy != userId && role != "Admin" && role != "SuperAdmin") return Forbid();

            updatedQuestion.Id = questionId;
            var success = await _questionService.UpdateQuestionAsync(questionId, updatedQuestion);

            if (!success) return BadRequest(new { message = "Failed to update question" });

            return Ok(new { message = "Question updated successfully" });
        }

        [HttpPatch("{questionId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> PatchQuestion(string questionId, [FromBody] object patchData)
        {
            // TODO: Implement partial update logic
            return Ok(new { message = "Question partially updated" });
        }

        [HttpDelete("{questionId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteQuestion(string questionId)
        {
            var userId = User.FindFirst("userId")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var question = await _questionService.GetQuestionByIdAsync(questionId);
            if (question == null) return NotFound(new { message = "Question not found" });

            if (question.CreatedBy != userId && role != "Admin" && role != "SuperAdmin") return Forbid();

            var success = await _questionService.DeleteQuestionAsync(questionId);
            if (!success) return BadRequest(new { message = "Failed to delete question" });

            return Ok(new { message = "Question deleted successfully" });
        }

        #endregion

        #region 2. Question Lifecycle

        [HttpPost("{questionId}/publish")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> PublishQuestion(string questionId)
        {
            // TODO: Change status to Published
            return Ok(new { message = "Question published" });
        }

        [HttpPost("{questionId}/unpublish")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UnpublishQuestion(string questionId)
        {
            // TODO: Change status to Draft/Unpublished
            return Ok(new { message = "Question unpublished" });
        }

        [HttpPost("{questionId}/archive")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ArchiveQuestion(string questionId)
        {
            // TODO: Move to archive state
            return Ok(new { message = "Question archived" });
        }

        [HttpPost("{questionId}/restore")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> RestoreQuestion(string questionId)
        {
            // TODO: Restore from archive
            return Ok(new { message = "Question restored" });
        }

        [HttpPost("{questionId}/duplicate")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DuplicateQuestion(string questionId)
        {
            // TODO: Clone question and return new ID
            return Ok(new { message = "Question duplicated", newQuestionId = "new-id" });
        }

        #endregion

        #region 3. Question Versions

        [HttpGet("{questionId}/versions")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetQuestionVersions(string questionId)
        {
            // TODO: Fetch version history
            return Ok(new { message = "Versions fetched" });
        }

        [HttpGet("{questionId}/versions/{versionId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetQuestionVersion(string questionId, string versionId)
        {
            // TODO: Fetch specific version detail
            return Ok(new { message = $"Fetched version {versionId}" });
        }

        [HttpPost("{questionId}/versions")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateQuestionVersion(string questionId, [FromBody] object versionData)
        {
            // TODO: explicitly save a new version snapshot
            return Ok(new { message = "New version created" });
        }

        [HttpPost("{questionId}/versions/{versionId}/restore")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> RestoreQuestionVersion(string questionId, string versionId)
        {
            // TODO: Roll back current question to specific version
            return Ok(new { message = $"Rolled back to version {versionId}" });
        }

        #endregion

        #region 4. Question Options (For MCQs)

        [HttpGet("{questionId}/options")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetQuestionOptions(string questionId)
        {
            // TODO: Fetch only the options for a question
            return Ok(new { message = "Options fetched" });
        }

        [HttpPost("{questionId}/options")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> AddQuestionOption(string questionId, [FromBody] OptionRequest request)
        {
            // TODO: Add a new option to a question
            return Ok(new { message = "Option added" });
        }

        [HttpPut("{questionId}/options/{optionId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateQuestionOption(string questionId, string optionId, [FromBody] OptionRequest request)
        {
            // TODO: Update a specific option
            return Ok(new { message = "Option updated" });
        }

        [HttpDelete("{questionId}/options/{optionId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteQuestionOption(string questionId, string optionId)
        {
            // TODO: Remove a specific option
            return Ok(new { message = "Option deleted" });
        }

        #endregion

        #region 5. Categories

        [HttpGet("~/api/v{version:apiVersion}/question-categories")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _questionService.GetCategoriesAsync();
            return Ok(categories);
        }

        [HttpPost("~/api/v{version:apiVersion}/question-categories")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateCategory([FromBody] object request)
        {
            // TODO: Create category
            return Ok(new { message = "Category created" });
        }

        [HttpGet("~/api/v{version:apiVersion}/question-categories/{categoryId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetCategory(string categoryId)
        {
            // TODO: Get single category
            return Ok(new { message = "Category fetched" });
        }

        [HttpPut("~/api/v{version:apiVersion}/question-categories/{categoryId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateCategory(string categoryId, [FromBody] object request)
        {
            // TODO: Update category
            return Ok(new { message = "Category updated" });
        }

        [HttpDelete("~/api/v{version:apiVersion}/question-categories/{categoryId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteCategory(string categoryId)
        {
            // TODO: Delete category
            return Ok(new { message = "Category deleted" });
        }

        #endregion

        #region 6. Subjects

        [HttpGet("~/api/v{version:apiVersion}/subjects")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetSubjects()
        {
            // TODO: Get all subjects
            return Ok(new { message = "Subjects fetched" });
        }

        [HttpPost("~/api/v{version:apiVersion}/subjects")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateSubject([FromBody] object request)
        {
            // TODO: Create subject
            return Ok(new { message = "Subject created" });
        }

        [HttpGet("~/api/v{version:apiVersion}/subjects/{subjectId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetSubject(string subjectId)
        {
            // TODO: Get subject by id
            return Ok(new { message = "Subject fetched" });
        }

        [HttpPut("~/api/v{version:apiVersion}/subjects/{subjectId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateSubject(string subjectId, [FromBody] object request)
        {
            // TODO: Update subject
            return Ok(new { message = "Subject updated" });
        }

        [HttpDelete("~/api/v{version:apiVersion}/subjects/{subjectId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteSubject(string subjectId)
        {
            // TODO: Delete subject
            return Ok(new { message = "Subject deleted" });
        }

        #endregion

        #region 7. Topics

        [HttpGet("~/api/v{version:apiVersion}/topics")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetTopics()
        {
            // TODO: Get all topics
            return Ok(new { message = "Topics fetched" });
        }

        [HttpPost("~/api/v{version:apiVersion}/topics")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateTopic([FromBody] object request)
        {
            // TODO: Create topic
            return Ok(new { message = "Topic created" });
        }

        [HttpGet("~/api/v{version:apiVersion}/topics/{topicId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetTopic(string topicId)
        {
            // TODO: Get topic
            return Ok(new { message = "Topic fetched" });
        }

        [HttpPut("~/api/v{version:apiVersion}/topics/{topicId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateTopic(string topicId, [FromBody] object request)
        {
            // TODO: Update topic
            return Ok(new { message = "Topic updated" });
        }

        [HttpDelete("~/api/v{version:apiVersion}/topics/{topicId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteTopic(string topicId)
        {
            // TODO: Delete topic
            return Ok(new { message = "Topic deleted" });
        }

        #endregion

        #region 8. Difficulty

        [HttpGet("~/api/v{version:apiVersion}/difficulties")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetDifficulties()
        {
            // TODO: List all difficulties
            return Ok(new { message = "Difficulties fetched" });
        }

        [HttpPost("~/api/v{version:apiVersion}/difficulties")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateDifficulty([FromBody] object request)
        {
            // TODO: Create difficulty rating
            return Ok(new { message = "Difficulty created" });
        }

        [HttpPut("~/api/v{version:apiVersion}/difficulties/{difficultyId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateDifficulty(string difficultyId, [FromBody] object request)
        {
            // TODO: Update difficulty
            return Ok(new { message = "Difficulty updated" });
        }

        [HttpDelete("~/api/v{version:apiVersion}/difficulties/{difficultyId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteDifficulty(string difficultyId)
        {
            // TODO: Delete difficulty
            return Ok(new { message = "Difficulty deleted" });
        }

        #endregion

        #region 9. Tags

        [HttpGet("~/api/v{version:apiVersion}/tags")]
        [Authorize]
        public async Task<IActionResult> GetTags()
        {
            var tags = await _questionService.GetTagsAsync();
            return Ok(tags);
        }

        [HttpPost("~/api/v{version:apiVersion}/tags")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateTag([FromBody] object request)
        {
            // TODO: Create a tag
            return Ok(new { message = "Tag created" });
        }

        [HttpPut("~/api/v{version:apiVersion}/tags/{tagId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateTag(string tagId, [FromBody] object request)
        {
            // TODO: Update a tag
            return Ok(new { message = "Tag updated" });
        }

        [HttpDelete("~/api/v{version:apiVersion}/tags/{tagId}")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteTag(string tagId)
        {
            // TODO: Delete a tag
            return Ok(new { message = "Tag deleted" });
        }

        #endregion

        #region 10. Bulk Operations

        [HttpPost("~/api/v{version:apiVersion}/questions/bulk")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin,0,Student")] // Need student for exam taking
        public async Task<IActionResult> GetQuestionsByIds([FromBody] List<string> questionIds)
        {
            var questions = await _questionService.GetQuestionsByIdsAsync(questionIds);

            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            if (role == "Student" || role == "0")
            {
                var questionDtos = questions.Select(QuestionService.ToQuestionDto).ToList();
                return Ok(questionDtos);
            }

            var questionsWithAnswers = questions.Select(QuestionService.ToQuestionWithAnswer).ToList();
            return Ok(questionsWithAnswers);
        }

        [HttpPost("~/api/v{version:apiVersion}/questions/import")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ImportQuestions([FromBody] object importData)
        {
            // TODO: Handle CSV/JSON import
            return Ok(new { message = "Questions imported successfully" });
        }

        [HttpPost("~/api/v{version:apiVersion}/questions/export")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ExportQuestions([FromBody] object exportFilters)
        {
            // TODO: Generate and return CSV/JSON file of questions
            return Ok(new { message = "Export initiated" });
        }

        [HttpPost("~/api/v{version:apiVersion}/questions/bulk-publish")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> BulkPublish([FromBody] List<string> questionIds)
        {
            // TODO: Publish multiple questions
            return Ok(new { message = $"{questionIds.Count} questions published" });
        }

        [HttpPost("~/api/v{version:apiVersion}/questions/bulk-archive")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> BulkArchive([FromBody] List<string> questionIds)
        {
            // TODO: Archive multiple questions
            return Ok(new { message = $"{questionIds.Count} questions archived" });
        }

        [HttpPost("~/api/v{version:apiVersion}/questions/bulk-delete")]
        [Authorize(Roles = "1,2,3,Admin,SuperAdmin")] // Generally restricted to higher roles
        public async Task<IActionResult> BulkDelete([FromBody] List<string> questionIds)
        {
            // TODO: Delete multiple questions
            return Ok(new { message = $"{questionIds.Count} questions deleted" });
        }

        #endregion

        #region 11. Question Review Queue

        [HttpGet("~/api/v{version:apiVersion}/questions/review-queue")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetReviewQueue()
        {
            // TODO: Fetch questions pending review
            return Ok(new { message = "Review queue fetched" });
        }

        [HttpPost("~/api/v{version:apiVersion}/questions/{questionId}/submit-review")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> SubmitForReview(string questionId)
        {
            // TODO: Change status to 'Pending Review'
            return Ok(new { message = "Question submitted for review" });
        }

        [HttpPost("~/api/v{version:apiVersion}/questions/{questionId}/approve")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ApproveQuestion(string questionId, [FromBody] ReviewCommentRequest request)
        {
            // TODO: Approve the question (making it ready for publishing)
            return Ok(new { message = "Question approved" });
        }

        [HttpPost("~/api/v{version:apiVersion}/questions/{questionId}/reject")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> RejectQuestion(string questionId, [FromBody] ReviewCommentRequest request)
        {
            // TODO: Reject and send back to drafter
            return Ok(new { message = "Question rejected" });
        }

        #endregion

        #region 12. Health

        [HttpGet("~/api/v{version:apiVersion}/questions/health")]
        [AllowAnonymous]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", service = "QuestionBankService", version = "v1", timestamp = DateTime.UtcNow });
        }

        #endregion
    }

    // Dummy classes added to ensure the controller compiles successfully.
    // Move these to your QuestionBankService.Models namespace/folder.
    public class OptionRequest
    {
        public string Text { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
    }

    public class ReviewCommentRequest
    {
        public string Comment { get; set; } = string.Empty;
    }
}