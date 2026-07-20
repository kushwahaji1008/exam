using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using QuestionBankService.Models;
using QuestionBankService.Services;

namespace QuestionBankService.Controllers
{
    [ApiController]
    [Route("api/questions")]
    public class QuestionsController : ControllerBase
    {
        private readonly QuestionService _questionService;
        private readonly ILogger<QuestionsController> _logger;

        public QuestionsController(QuestionService questionService, ILogger<QuestionsController> logger)
        {
            _questionService = questionService;
            _logger = logger;
        }

        [HttpPost]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateQuestion([FromBody] CreateQuestionRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var question = await _questionService.CreateQuestionAsync(request, userId);

            return Ok(new { message = "Question created successfully", question = QuestionService.ToQuestionDto(question) });
        }

        [HttpGet]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetAllQuestions([FromQuery] string? category, [FromQuery] DifficultyLevel? difficulty)
        {
            var questions = await _questionService.GetAllQuestionsAsync(category, difficulty);
            var questionDtos = questions.Select(QuestionService.ToQuestionDto).ToList();

            return Ok(questionDtos);
        }

        [HttpGet("{questionId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetQuestionById(string questionId)
        {
            var question = await _questionService.GetQuestionByIdAsync(questionId);
            if (question == null)
            {
                return NotFound(new { message = "Question not found" });
            }

            return Ok(QuestionService.ToQuestionWithAnswer(question));
        }

        [HttpPost("bulk")]
        [Authorize]
        public async Task<IActionResult> GetQuestionsByIds([FromBody] List<string> questionIds)
        {
            var questions = await _questionService.GetQuestionsByIdsAsync(questionIds);
            
            // For students during exam, don't include answers
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            if (role == "Student")
            {
                var questionDtos = questions.Select(QuestionService.ToQuestionDto).ToList();
                return Ok(questionDtos);
            }
            
            // For teachers/admins, include answers
            var questionsWithAnswers = questions.Select(QuestionService.ToQuestionWithAnswer).ToList();
            return Ok(questionsWithAnswers);
        }

        [HttpPut("{questionId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateQuestion(string questionId, [FromBody] Question updatedQuestion)
        {
            var userId = User.FindFirst("userId")?.Value;
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            var question = await _questionService.GetQuestionByIdAsync(questionId);
            if (question == null)
            {
                return NotFound(new { message = "Question not found" });
            }

            // Only creator or admin can update
            if (question.CreatedBy != userId && role != "Admin" && role != "SuperAdmin")
            {
                return Forbid();
            }

            updatedQuestion.Id = questionId;
            var success = await _questionService.UpdateQuestionAsync(questionId, updatedQuestion);

            if (!success)
            {
                return BadRequest(new { message = "Failed to update question" });
            }

            return Ok(new { message = "Question updated successfully" });
        }

        [HttpDelete("{questionId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteQuestion(string questionId)
        {
            var userId = User.FindFirst("userId")?.Value;
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            var question = await _questionService.GetQuestionByIdAsync(questionId);
            if (question == null)
            {
                return NotFound(new { message = "Question not found" });
            }

            // Only creator or admin can delete
            if (question.CreatedBy != userId && role != "Admin" && role != "SuperAdmin")
            {
                return Forbid();
            }

            var success = await _questionService.DeleteQuestionAsync(questionId);
            if (!success)
            {
                return BadRequest(new { message = "Failed to delete question" });
            }

            return Ok(new { message = "Question deleted successfully" });
        }

        [HttpGet("categories")]
        [Authorize]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _questionService.GetCategoriesAsync();
            return Ok(categories);
        }

        [HttpGet("tags")]
        [Authorize]
        public async Task<IActionResult> GetTags()
        {
            var tags = await _questionService.GetTagsAsync();
            return Ok(tags);
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", service = "QuestionBankService", timestamp = DateTime.UtcNow });
        }
    }
}