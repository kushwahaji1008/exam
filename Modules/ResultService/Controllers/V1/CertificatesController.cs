using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ResultService.Models;
using ResultService.Services;

namespace ResultService.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/certificates")]
    public class CertificatesController : ControllerBase
    {
        private readonly EvaluationService _evalService;

        public CertificatesController(EvaluationService evalService)
        {
            _evalService = evalService;
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAll() => Ok(await _evalService.GetAllCertificatesAsync());

        [HttpGet("{certificateId}")]
        [Authorize]
        public async Task<IActionResult> GetById(string certificateId) => Ok(await _evalService.GetCertificateByIdAsync(certificateId));

        [HttpPost("generate")]
        [Authorize(Roles = "Admin,System")]
        public async Task<IActionResult> Generate([FromBody] GenerateCertificateRequest req)
        {
            try
            {
                var cert = await _evalService.GenerateCertificateAsync(req.ResultId);
                return Ok(cert);
            }
            catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpGet("{certificateId}/download")]
        [Authorize]
        public IActionResult Download(string certificateId)
        {
            // Simulating a file download
            byte[] fileBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // Mock PDF bytes
            return File(fileBytes, "application/pdf", $"Certificate_{certificateId}.pdf");
        }

        [HttpGet("{certificateCode}/verify")]
        [AllowAnonymous] // Verification is public!
        public async Task<IActionResult> Verify(string certificateCode)
        {
            var cert = await _evalService.VerifyCertificateAsync(certificateCode);
            if (cert == null) return NotFound(new { valid = false, message = "Invalid or fake certificate code." });
            return Ok(new { valid = true, certificate = cert });
        }
    }
}