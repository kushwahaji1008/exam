using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.AspNetCore.Authorization;
using ExamService.Services;

namespace ExamService.V2.Controllers
{
    [ApiVersion("2.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/exams/{examId}/questions")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public class ExamQuestionsController : ControllerBase
    {
        private readonly ExamManagementService _examService;

        public ExamQuestionsController(ExamManagementService examService)
        {
            _examService = examService;
        }

        [HttpGet]
        public async Task<IActionResult> GetQuestions(string examId) => Ok(new { questions = new[] { 1, 2, 3 } });

        [HttpPost]
        public async Task<IActionResult> AddQuestion(string examId, [FromBody] object question) => Ok(new { message = "Question added", questionId = "Q1" });

        [HttpPut("{questionId}")]
        public async Task<IActionResult> UpdateQuestion(string examId, string questionId, [FromBody] object question) => Ok(new { message = "Question updated" });

        [HttpDelete("{questionId}")]
        public async Task<IActionResult> DeleteQuestion(string examId, string questionId) => Ok(new { message = "Question deleted" });

        [HttpPost("reorder")]
        public async Task<IActionResult> ReorderQuestions(string examId, [FromBody] object orderRequest) => Ok(new { message = "Questions reordered" });

        [HttpPost("randomize")]
        public async Task<IActionResult> RandomizeQuestions(string examId) => Ok(new { message = "Questions randomized" });
    }
}