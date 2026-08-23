using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.AspNetCore.Authorization;
using ExamService.Services;

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
        public async Task<IActionResult> GetVersions(string examId) => Ok(new { versions = new[] { "v1.0", "v1.1" } });

        [HttpGet("{versionId}")]
        public async Task<IActionResult> GetVersionById(string examId, string versionId) => Ok(new { versionId, data = new { } });

        [HttpPost]
        public async Task<IActionResult> CreateNewVersion(string examId) => Ok(new { message = "New version drafted", versionId = "v1.2" });

        [HttpPost("{versionId}/publish")]
        public async Task<IActionResult> PublishVersion(string examId, string versionId) => Ok(new { message = $"Version {versionId} published as active" });

        [HttpPost("{versionId}/restore")]
        public async Task<IActionResult> RestoreVersion(string examId, string versionId) => Ok(new { message = $"Rolled back to version {versionId}" });
    }
}