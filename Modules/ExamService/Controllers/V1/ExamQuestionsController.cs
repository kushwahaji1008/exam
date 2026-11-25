using System.Text.Json;
using ExamService.Models;
using ExamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamService.Controllers.V1
{
    public interface IExamQuestionDetailResolver
    {
        Task<List<object>> ResolveQuestionsByIdsAsync(IReadOnlyList<string> questionIds);
    }

    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/exams/{examId}/questions")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public class ExamQuestionsController : ControllerBase
    {
        private readonly ExamManagementService _examService;
        private readonly IExamQuestionDetailResolver? _questionResolver;

        public ExamQuestionsController(ExamManagementService examService, IExamQuestionDetailResolver? questionResolver = null)
        {
            _examService = examService;
            _questionResolver = questionResolver;
        }

        [HttpGet]
        public async Task<IActionResult> GetQuestions(string examId)
        {
            var exam = await _examService.GetExamByIdAsync(examId);
            if (exam == null) return NotFound(new { message = "Exam not found" });

            var allIds = exam.QuestionIds
                .Concat(exam.Sections.SelectMany(s => s.QuestionIds))
                .Distinct()
                .ToList();

            if (_questionResolver != null && allIds.Count > 0)
            {
                var detailed = await _questionResolver.ResolveQuestionsByIdsAsync(allIds);
                return Ok(new { questionIds = allIds, questions = detailed });
            }

            return Ok(new { questionIds = allIds, questions = allIds.Select(id => new { id }) });
        }

        [HttpPost]
        public async Task<IActionResult> AddQuestion(string examId, [FromBody] JsonElement body)
        {
            string? questionId = null;
            if (body.ValueKind == JsonValueKind.Object)
            {
                if (body.TryGetProperty("questionId", out var q1) && q1.ValueKind == JsonValueKind.String) questionId = q1.GetString();
                else if (body.TryGetProperty("QuestionId", out var q2) && q2.ValueKind == JsonValueKind.String) questionId = q2.GetString();
                else if (body.TryGetProperty("id", out var q3) && q3.ValueKind == JsonValueKind.String) questionId = q3.GetString();
            }

            if (string.IsNullOrWhiteSpace(questionId))
                return BadRequest(new { message = "questionId is required." });

            var success = await _examService.AddQuestionAsync(examId, questionId);
            return success ? Ok(new { message = "Question added", questionId }) : NotFound(new { message = "Exam not found" });
        }

        [HttpPut("{questionId}")]
        public async Task<IActionResult> UpdateQuestion(string examId, string questionId, [FromBody] JsonElement body)
        {
            var exam = await _examService.GetExamByIdAsync(examId);
            if (exam == null) return NotFound(new { message = "Exam not found" });

            string? newQuestionId = null;
            if (body.ValueKind == JsonValueKind.Object &&
                (body.TryGetProperty("questionId", out var q1) || body.TryGetProperty("newQuestionId", out q1)) &&
                q1.ValueKind == JsonValueKind.String)
            {
                newQuestionId = q1.GetString();
            }

            if (!string.IsNullOrWhiteSpace(newQuestionId) && newQuestionId != questionId)
            {
                var idx = exam.QuestionIds.IndexOf(questionId);
                if (idx >= 0) exam.QuestionIds[idx] = newQuestionId;
                else exam.QuestionIds.Add(newQuestionId);
                await _examService.ReorderQuestionsAsync(examId, exam.QuestionIds);
            }

            return Ok(new { message = "Question updated", questionId = newQuestionId ?? questionId });
        }

        [HttpDelete("{questionId}")]
        public async Task<IActionResult> DeleteQuestion(string examId, string questionId)
        {
            var success = await _examService.RemoveQuestionAsync(examId, questionId);
            return success ? Ok(new { message = "Question deleted" }) : NotFound(new { message = "Exam or question not found" });
        }

        [HttpPost("reorder")]
        public async Task<IActionResult> ReorderQuestions(string examId, [FromBody] ReorderRequest orderRequest)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var success = await _examService.ReorderQuestionsAsync(examId, orderRequest.OrderedIds);
            return success ? Ok(new { message = "Questions reordered" }) : NotFound(new { message = "Exam not found" });
        }

        [HttpPost("randomize")]
        public async Task<IActionResult> RandomizeQuestions(string examId)
        {
            var shuffled = await _examService.RandomizeQuestionsAsync(examId);
            return shuffled != null ? Ok(new { message = "Questions randomized", questionIds = shuffled }) : NotFound(new { message = "Exam not found" });
        }
    }
}
