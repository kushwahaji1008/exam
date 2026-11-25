using ExamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamService.Controllers.V1
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/exams/{examId}/versions")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public class ExamVersionsController : ControllerBase
    {
        private readonly ExamManagementService _examService;

        public ExamVersionsController(ExamManagementService examService)
        {
            _examService = examService;
        }

        [HttpGet]
        public async Task<IActionResult> GetVersions(string examId)
        {
            var versions = await _examService.GetExamVersionsAsync(examId);
            return Ok(new { versions });
        }

        [HttpGet("{versionId}")]
        public async Task<IActionResult> GetVersionById(string examId, string versionId)
        {
            var version = await _examService.GetVersionAsync(examId, versionId);
            return version != null ? Ok(new { versionId = version.Version, data = version }) : NotFound(new { message = "Version not found" });
        }

        [HttpPost]
        public async Task<IActionResult> CreateNewVersion(string examId)
        {
            var userId = User.FindFirst("userId")?.Value ?? "SYSTEM";
            var newExamId = await _examService.CreateNewVersionAsync(examId, userId);
            if (newExamId == null) return NotFound(new { message = "Exam not found" });

            var created = await _examService.GetExamByIdAsync(newExamId);
            return Ok(new { message = "New version drafted", versionId = created?.Version ?? "1.1", newExamId });
        }

        [HttpPost("{versionId}/publish")]
        public async Task<IActionResult> PublishVersion(string examId, string versionId)
        {
            var success = await _examService.PublishVersionAsync(examId, versionId);
            return success ? Ok(new { message = $"Version {versionId} published as active" }) : NotFound(new { message = "Exam or version not found" });
        }

        [HttpPost("{versionId}/restore")]
        public async Task<IActionResult> RestoreVersion(string examId, string versionId)
        {
            var success = await _examService.RestoreVersionAsync(examId, versionId);
            return success ? Ok(new { message = $"Rolled back to version {versionId}" }) : NotFound(new { message = "Exam or version not found" });
        }
    }
}
