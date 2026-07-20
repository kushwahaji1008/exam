using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ResultService.Services;

namespace ResultService.Controllers
{
    [ApiController]
    [Route("api/results")]
    public class ResultsController : ControllerBase
    {
        private readonly EvaluationService _evaluationService;
        private readonly ILogger<ResultsController> _logger;

        public ResultsController(EvaluationService evaluationService, ILogger<ResultsController> logger)
        {
            _evaluationService = evaluationService;
            _logger = logger;
        }

        [HttpPost("evaluate/{attemptId}")]
        [Authorize]
        public async Task<IActionResult> EvaluateAttempt(string attemptId)
        {
            try
            {
                var result = await _evaluationService.EvaluateAttemptAsync(attemptId);
                return Ok(new { message = "Evaluation completed", result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating attempt {AttemptId}", attemptId);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", service = "ResultService", timestamp = DateTime.UtcNow });
        }
    }
}