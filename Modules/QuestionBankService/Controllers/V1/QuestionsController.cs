using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuestionBankService.Models;
using QuestionBankService.Services;

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

        private string? GetUserId() => User.FindFirst("userId")?.Value;
        private bool IsAdmin() => User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

        #region 1. Questions (CRUD)

        [HttpGet]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetAllQuestions(
            [FromQuery] string? categoryId,
            [FromQuery] string? difficultyId,
            [FromQuery] string? subjectId = null,
            [FromQuery] string? topicId = null,
            [FromQuery] QuestionStatus? status = null,
            [FromQuery] QuestionType? type = null,
            [FromQuery] string? search = null,
            [FromQuery] int? page = null,
            [FromQuery] int? pageSize = null)
        {
            var questions = await _questionService.GetAllQuestionsAsync(
                categoryId, difficultyId, subjectId, topicId, status, type, search,
                includeArchived: status == QuestionStatus.Archived, page: page, pageSize: pageSize);
            return Ok(questions.Select(QuestionService.ToQuestionDto).ToList());
        }

        [HttpPost]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateQuestion([FromBody] CreateQuestionRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var question = await _questionService.CreateQuestionAsync(request, GetUserId() ?? string.Empty);
            return Ok(new { message = "Question created successfully", question = QuestionService.ToQuestionDto(question) });
        }

        [HttpGet("{questionId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetQuestionById(string questionId)
        {
            var question = await _questionService.GetQuestionByIdAsync(questionId);
            if (question == null) return NotFound(new { message = "Question not found" });
            return Ok(QuestionService.ToQuestionWithAnswer(question));
        }

        [HttpPut("{questionId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateQuestion(string questionId, [FromBody] Question updatedQuestion)
        {
            var question = await _questionService.GetQuestionByIdAsync(questionId);
            if (question == null) return NotFound(new { message = "Question not found" });
            if (question.CreatedBy != GetUserId() && !IsAdmin()) return Forbid();

            var success = await _questionService.UpdateQuestionAsync(questionId, updatedQuestion, GetUserId());
            if (!success) return BadRequest(new { message = "Failed to update question" });
            return Ok(new { message = "Question updated successfully" });
        }

        [HttpPatch("{questionId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> PatchQuestion(string questionId, [FromBody] object patchData)
        {
            var question = await _questionService.GetQuestionByIdAsync(questionId);
            if (question == null) return NotFound(new { message = "Question not found" });
            if (question.CreatedBy != GetUserId() && !IsAdmin()) return Forbid();

            var success = await _questionService.PatchQuestionAsync(questionId, patchData, GetUserId());
            return success ? Ok(new { message = "Question partially updated" }) : BadRequest(new { message = "Invalid patch payload" });
        }

        [HttpDelete("{questionId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteQuestion(string questionId)
        {
            var question = await _questionService.GetQuestionByIdAsync(questionId);
            if (question == null) return NotFound(new { message = "Question not found" });
            if (question.CreatedBy != GetUserId() && !IsAdmin()) return Forbid();

            var success = await _questionService.DeleteQuestionAsync(questionId);
            if (!success) return BadRequest(new { message = "Failed to delete question" });
            return Ok(new { message = "Question deleted successfully" });
        }

        #endregion

        #region 2. Question Lifecycle

        [HttpPost("{questionId}/publish")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> PublishQuestion(string questionId)
        {
            var success = await _questionService.UpdateQuestionStatusAsync(questionId, QuestionStatus.Published, GetUserId());
            return success ? Ok(new { message = "Question published successfully" }) : NotFound(new { message = "Question not found." });
        }

        [HttpPost("{questionId}/unpublish")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UnpublishQuestion(string questionId)
        {
            var success = await _questionService.UpdateQuestionStatusAsync(questionId, QuestionStatus.Draft, GetUserId());
            return success ? Ok(new { message = "Question unpublished and reverted to Draft" }) : NotFound(new { message = "Question not found." });
        }

        [HttpPost("{questionId}/archive")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ArchiveQuestion(string questionId)
        {
            var success = await _questionService.ArchiveQuestionAsync(questionId, GetUserId());
            return success ? Ok(new { message = "Question archived" }) : NotFound(new { message = "Question not found." });
        }

        [HttpPost("{questionId}/restore")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> RestoreQuestion(string questionId)
        {
            var success = await _questionService.RestoreQuestionAsync(questionId, GetUserId());
            return success ? Ok(new { message = "Question restored" }) : NotFound(new { message = "Question not found." });
        }

        [HttpPost("{questionId}/duplicate")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DuplicateQuestion(string questionId)
        {
            var copy = await _questionService.DuplicateQuestionAsync(questionId, GetUserId() ?? "SYSTEM");
            return copy != null ? Ok(new { message = "Question duplicated", newQuestionId = copy.Id, question = QuestionService.ToQuestionDto(copy) }) : NotFound(new { message = "Question not found." });
        }

        #endregion

        #region 3. Question Versions

        [HttpGet("{questionId}/versions")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetQuestionVersions(string questionId)
        {
            var versions = await _questionService.GetQuestionVersionsAsync(questionId);
            return Ok(versions);
        }

        [HttpGet("{questionId}/versions/{versionId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetQuestionVersion(string questionId, string versionId)
        {
            var version = await _questionService.GetQuestionVersionByIdAsync(versionId);
            if (version == null || version.OriginalQuestionId != questionId)
                return NotFound(new { message = "Version not found" });
            return Ok(version);
        }

        [HttpPost("{questionId}/versions")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateQuestionVersion(string questionId, [FromBody] object? versionData = null)
        {
            var snapshot = await _questionService.CreateQuestionVersionAsync(questionId, GetUserId() ?? "SYSTEM");
            if (snapshot == null) return NotFound(new { message = "Question not found" });
            return Ok(new { message = "New version created", version = snapshot });
        }

        [HttpPost("{questionId}/versions/{versionId}/restore")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> RestoreQuestionVersion(string questionId, string versionId)
        {
            var success = await _questionService.RestoreQuestionVersionAsync(questionId, versionId, GetUserId() ?? "SYSTEM");
            return success ? Ok(new { message = $"Rolled back to version {versionId}" }) : NotFound(new { message = "Question or version not found" });
        }

        #endregion

        #region 4. Question Options (For MCQs)

        [HttpGet("{questionId}/options")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetQuestionOptions(string questionId)
        {
            var question = await _questionService.GetQuestionByIdAsync(questionId);
            if (question == null) return NotFound(new { message = "Question not found" });
            return Ok(new { options = question.Options, correctOptions = question.CorrectOptions });
        }

        [HttpPost("{questionId}/options")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> AddQuestionOption(string questionId, [FromBody] OptionRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var added = await _questionService.AddQuestionOptionAsync(questionId, request);
            return added != null ? Ok(new { message = "Option added", option = added }) : NotFound(new { message = "Question not found" });
        }

        [HttpPut("{questionId}/options/{optionId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateQuestionOption(string questionId, string optionId, [FromBody] OptionRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var success = await _questionService.UpdateQuestionOptionAsync(questionId, optionId, request);
            return success ? Ok(new { message = "Option updated" }) : NotFound(new { message = "Question or option not found" });
        }

        [HttpDelete("{questionId}/options/{optionId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteQuestionOption(string questionId, string optionId)
        {
            var success = await _questionService.DeleteQuestionOptionAsync(questionId, optionId);
            return success ? Ok(new { message = "Option deleted" }) : NotFound(new { message = "Question or option not found" });
        }

        #endregion

        #region 5. Categories

        [HttpGet("~/api/v{version:apiVersion}/question-categories")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetCategories() => Ok(await _questionService.GetCategoriesAsync());

        [HttpPost("~/api/v{version:apiVersion}/question-categories")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateCategory([FromBody] Category request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { message = "Category name is required." });

            request.Name = request.Name.Trim();
            var category = await _questionService.CreateCategoryAsync(request);
            return Ok(new { message = "Category created", category });
        }

        [HttpGet("~/api/v{version:apiVersion}/question-categories/{categoryId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetCategory(string categoryId)
        {
            var cat = await _questionService.GetCategoryByIdAsync(categoryId);
            return cat != null ? Ok(cat) : NotFound(new { message = "Category not found" });
        }

        [HttpPut("~/api/v{version:apiVersion}/question-categories/{categoryId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateCategory(string categoryId, [FromBody] Category request)
        {
            var success = await _questionService.UpdateCategoryAsync(categoryId, request);
            return success ? Ok(new { message = "Category updated" }) : NotFound(new { message = "Category not found" });
        }

        [HttpDelete("~/api/v{version:apiVersion}/question-categories/{categoryId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteCategory(string categoryId)
        {
            var success = await _questionService.DeleteCategoryAsync(categoryId);
            return success ? Ok(new { message = "Category deleted" }) : NotFound(new { message = "Category not found" });
        }

        #endregion

        #region 6. Subjects

        [HttpGet("~/api/v{version:apiVersion}/subjects")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetSubjects([FromQuery] string? categoryId = null) =>
            Ok(await _questionService.GetSubjectsAsync(categoryId));

        [HttpPost("~/api/v{version:apiVersion}/subjects")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateSubject([FromBody] Subject request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { message = "Subject name is required." });

            request.Name = request.Name.Trim();
            var subject = await _questionService.CreateSubjectAsync(request);
            return Ok(new { message = "Subject created", subject });
        }

        [HttpGet("~/api/v{version:apiVersion}/subjects/{subjectId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetSubject(string subjectId)
        {
            var subject = await _questionService.GetSubjectByIdAsync(subjectId);
            return subject != null ? Ok(subject) : NotFound(new { message = "Subject not found" });
        }

        [HttpPut("~/api/v{version:apiVersion}/subjects/{subjectId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateSubject(string subjectId, [FromBody] Subject request)
        {
            var success = await _questionService.UpdateSubjectAsync(subjectId, request);
            return success ? Ok(new { message = "Subject updated" }) : NotFound(new { message = "Subject not found" });
        }

        [HttpDelete("~/api/v{version:apiVersion}/subjects/{subjectId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteSubject(string subjectId)
        {
            var success = await _questionService.DeleteSubjectAsync(subjectId);
            return success ? Ok(new { message = "Subject deleted" }) : NotFound(new { message = "Subject not found" });
        }

        #endregion

        #region 7. Topics

        [HttpGet("~/api/v{version:apiVersion}/topics")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetTopics([FromQuery] string? subjectId = null) =>
            Ok(await _questionService.GetTopicsAsync(subjectId));

        [HttpPost("~/api/v{version:apiVersion}/topics")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateTopic([FromBody] Topic request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { message = "Topic name is required." });

            request.Name = request.Name.Trim();
            var topic = await _questionService.CreateTopicAsync(request);
            return Ok(new { message = "Topic created", topic });
        }

        [HttpGet("~/api/v{version:apiVersion}/topics/{topicId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetTopic(string topicId)
        {
            var topic = await _questionService.GetTopicByIdAsync(topicId);
            return topic != null ? Ok(topic) : NotFound(new { message = "Topic not found" });
        }

        [HttpPut("~/api/v{version:apiVersion}/topics/{topicId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateTopic(string topicId, [FromBody] Topic request)
        {
            var success = await _questionService.UpdateTopicAsync(topicId, request);
            return success ? Ok(new { message = "Topic updated" }) : NotFound(new { message = "Topic not found" });
        }

        [HttpDelete("~/api/v{version:apiVersion}/topics/{topicId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteTopic(string topicId)
        {
            var success = await _questionService.DeleteTopicAsync(topicId);
            return success ? Ok(new { message = "Topic deleted" }) : NotFound(new { message = "Topic not found" });
        }

        #endregion

        #region 8. Difficulty

        [HttpGet("~/api/v{version:apiVersion}/difficulties")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetDifficulties() => Ok(await _questionService.GetDifficultiesAsync());

        [HttpPost("~/api/v{version:apiVersion}/difficulties")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateDifficulty([FromBody] Difficulty request)
        {
            var difficulty = await _questionService.CreateDifficultyAsync(request);
            return Ok(new { message = "Difficulty created", difficulty });
        }

        [HttpPut("~/api/v{version:apiVersion}/difficulties/{difficultyId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateDifficulty(string difficultyId, [FromBody] Difficulty request)
        {
            var success = await _questionService.UpdateDifficultyAsync(difficultyId, request);
            return success ? Ok(new { message = "Difficulty updated" }) : NotFound(new { message = "Difficulty not found" });
        }

        [HttpDelete("~/api/v{version:apiVersion}/difficulties/{difficultyId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteDifficulty(string difficultyId)
        {
            var success = await _questionService.DeleteDifficultyAsync(difficultyId);
            return success ? Ok(new { message = "Difficulty deleted" }) : NotFound(new { message = "Difficulty not found" });
        }

        #endregion

        #region 9. Tags

        [HttpGet("~/api/v{version:apiVersion}/tags")]
        [Authorize]
        public async Task<IActionResult> GetTags() => Ok(await _questionService.GetTagsAsync());

        [HttpPost("~/api/v{version:apiVersion}/tags")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateTag([FromBody] QuestionBankService.Models.Tag request)
        {
            var tag = await _questionService.CreateTagAsync(request);
            return Ok(new { message = "Tag created", tag });
        }

        [HttpPut("~/api/v{version:apiVersion}/tags/{tagId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateTag(string tagId, [FromBody] QuestionBankService.Models.Tag request)
        {
            var success = await _questionService.UpdateTagAsync(tagId, request);
            return success ? Ok(new { message = "Tag updated" }) : NotFound(new { message = "Tag not found" });
        }

        [HttpDelete("~/api/v{version:apiVersion}/tags/{tagId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteTag(string tagId)
        {
            var success = await _questionService.DeleteTagAsync(tagId);
            return success ? Ok(new { message = "Tag deleted" }) : NotFound(new { message = "Tag not found" });
        }

        #endregion

        #region 10. Bulk Operations

        [HttpPost("~/api/v{version:apiVersion}/questions/bulk")]
        [Authorize]
        public async Task<IActionResult> GetQuestionsByIds([FromBody] List<string> questionIds)
        {
            if (questionIds == null || questionIds.Count > 200)
                return BadRequest(new { message = "Provide between 0 and 200 question IDs." });

            var questions = await _questionService.GetQuestionsByIdsAsync(questionIds);
            if (User.IsInRole("Teacher") || User.IsInRole("Admin") || User.IsInRole("SuperAdmin"))
                return Ok(questions.Select(QuestionService.ToQuestionWithAnswer).ToList());

            return Ok(questions.Select(QuestionService.ToQuestionDto).ToList());
        }

        private const long MaxCsvUploadBytes = 10 * 1024 * 1024; // 10 MB

        [HttpGet("~/api/v{version:apiVersion}/questions/import/template")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public IActionResult DownloadCsvTemplate()
        {
            var (bytes, contentType, fileName) = QuestionService.GenerateCsvTemplate();
            return File(bytes, contentType, fileName);
        }

        [HttpPost("~/api/v{version:apiVersion}/questions/import/csv")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(MaxCsvUploadBytes)]
        public async Task<IActionResult> ImportQuestionsCsv(
            IFormFile? file,
            [FromQuery] bool dryRun = false,
            [FromQuery] bool skipInvalidRows = true,
            [FromQuery] string? defaultCategoryId = null,
            [FromQuery] string? defaultSubjectId = null,
            [FromQuery] string? defaultTopicId = null,
            [FromQuery] QuestionStatus? defaultStatus = null)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Please upload a non-empty CSV file." });

            if (file.Length > MaxCsvUploadBytes)
                return BadRequest(new { message = "CSV file exceeds the 10 MB upload limit." });

            var ext = Path.GetExtension(file.FileName);
            if (!string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Only .csv files are supported." });

            string csvContent;
            using (var stream = file.OpenReadStream())
            using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                csvContent = await reader.ReadToEndAsync();
            }

            if (csvContent.Contains('\0'))
                return BadRequest(new { message = "Invalid CSV file: binary content detected." });

            var result = await _questionService.ImportQuestionsFromCsvAsync(
                csvContent,
                GetUserId() ?? "SYSTEM",
                dryRun,
                skipInvalidRows,
                defaultCategoryId,
                defaultSubjectId,
                defaultTopicId,
                defaultStatus);

            if (result.TotalRows == 0 && result.Errors.Count == 0)
                return BadRequest(result);

            if (!dryRun && ((!skipInvalidRows && result.Errors.Count > 0) || (result.ImportedCount == 0 && result.FailedCount > 0)))
                return BadRequest(result);

            return Ok(result);
        }

        [HttpPost("~/api/v{version:apiVersion}/questions/import")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ImportQuestions([FromBody] JsonElement importData)
        {
            var (count, questions) = await _questionService.ImportQuestionsAsync(importData, GetUserId() ?? "SYSTEM");
            if (count == 0)
                return BadRequest(new { message = "No valid questions found in import payload." });

            return Ok(new { message = $"{count} questions imported successfully", count, questions = questions.Select(QuestionService.ToQuestionDto) });
        }

        [HttpPost("~/api/v{version:apiVersion}/questions/export")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ExportQuestions([FromBody] ExportQuestionsRequest? exportFilters = null)
        {
            var req = exportFilters ?? new ExportQuestionsRequest();
            var (bytes, contentType, fileName) = await _questionService.ExportQuestionsAsync(req);
            return File(bytes, contentType, fileName);
        }

        [HttpPost("~/api/v{version:apiVersion}/questions/bulk-publish")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> BulkPublish([FromBody] List<string> questionIds)
        {
            var modified = await _questionService.BulkUpdateStatusAsync(questionIds, QuestionStatus.Published);
            return Ok(new { message = $"{modified} questions published", count = modified });
        }

        [HttpPost("~/api/v{version:apiVersion}/questions/bulk-archive")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> BulkArchive([FromBody] List<string> questionIds)
        {
            var modified = await _questionService.BulkUpdateStatusAsync(questionIds, QuestionStatus.Archived);
            return Ok(new { message = $"{modified} questions archived", count = modified });
        }

        [HttpPost("~/api/v{version:apiVersion}/questions/bulk-delete")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> BulkDelete([FromBody] List<string> questionIds)
        {
            var deleted = await _questionService.BulkDeleteAsync(questionIds);
            return Ok(new { message = $"{deleted} questions deleted", count = deleted });
        }

        #endregion

        #region 11. Question Review Queue

        [HttpGet("~/api/v{version:apiVersion}/questions/review-queue")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetReviewQueue()
        {
            var queue = await _questionService.GetReviewQueueAsync();
            return Ok(queue.Select(QuestionService.ToQuestionWithAnswer).ToList());
        }

        [HttpPost("~/api/v{version:apiVersion}/questions/{questionId}/submit-review")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> SubmitForReview(string questionId)
        {
            var success = await _questionService.AddReviewLogAsync(questionId, GetUserId() ?? "SYSTEM", QuestionStatus.PendingReview, "Submitted for review");
            return success ? Ok(new { message = "Question submitted for review" }) : NotFound(new { message = "Question not found" });
        }

        [HttpPost("~/api/v{version:apiVersion}/questions/{questionId}/approve")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ApproveQuestion(string questionId, [FromBody] ReviewCommentRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var success = await _questionService.AddReviewLogAsync(questionId, GetUserId() ?? "SYSTEM", QuestionStatus.Approved, request.Comment);
            return success ? Ok(new { message = "Question approved" }) : NotFound(new { message = "Question not found" });
        }

        [HttpPost("~/api/v{version:apiVersion}/questions/{questionId}/reject")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> RejectQuestion(string questionId, [FromBody] ReviewCommentRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var success = await _questionService.AddReviewLogAsync(questionId, GetUserId() ?? "SYSTEM", QuestionStatus.Rejected, request.Comment);
            return success ? Ok(new { message = "Question rejected" }) : NotFound(new { message = "Question not found" });
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
}
