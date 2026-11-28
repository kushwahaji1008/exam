using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        private string? GetUserId() => User.FindFirst("userId")?.Value;
        private bool IsStaff() => User.IsInRole("Teacher") || User.IsInRole("Admin") || User.IsInRole("SuperAdmin") || User.IsInRole("System");

        [HttpGet]
        [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
        public async Task<IActionResult> GetAll() => Ok(await _evalService.GetAllCertificatesAsync());

        [HttpGet("{certificateId}")]
        [Authorize]
        public async Task<IActionResult> GetById(string certificateId)
        {
            var cert = await _evalService.GetCertificateByIdAsync(certificateId);
            if (cert == null) return NotFound(new { message = "Certificate not found" });
            if (!IsStaff() && cert.UserId != GetUserId()) return Forbid();

            return Ok(cert);
        }

        [HttpPost("generate")]
        [Authorize]
        public async Task<IActionResult> Generate([FromBody] GenerateCertificateRequest req)
        {
            var result = await _evalService.GetResultByIdAsync(req.ResultId);
            if (result == null) return NotFound(new { message = "Result not found." });

            if (!IsStaff())
            {
                if (result.UserId != GetUserId()) return Forbid();
                if (result.Status is not (ResultStatus.Published or ResultStatus.Finalized or ResultStatus.Calculated))
                    return BadRequest(new { message = "Result is not ready for certificate generation." });
            }

            try
            {
                var cert = await _evalService.GenerateCertificateAsync(req.ResultId);
                return Ok(cert);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{certificateId}/download")]
        [Authorize]
        public async Task<IActionResult> Download(string certificateId)
        {
            var cert = await _evalService.GetCertificateByIdAsync(certificateId);
            if (cert == null) return NotFound(new { message = "Certificate not found." });
            if (!IsStaff() && cert.UserId != GetUserId()) return Forbid();

            var result = await _evalService.GetResultByIdAsync(cert.ResultId);
            var pdfBytes = _evalService.GenerateCertificatePdf(cert, result);
            return File(pdfBytes, "application/pdf", $"Certificate_{cert.Id}.pdf");
        }

        [HttpGet("{certificateCode}/verify")]
        [AllowAnonymous]
        public async Task<IActionResult> Verify(string certificateCode)
        {
            var cert = await _evalService.VerifyCertificateAsync(certificateCode);
            if (cert == null) return NotFound(new { valid = false, message = "Invalid or fake certificate code." });
            return Ok(new { valid = true, certificate = cert });
        }
    }
}
