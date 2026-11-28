using System.Text;
using AnalyticsService.Services;
using AuthService.Services;
using EnrollmentService.Services;
using ExamService.Models;
using Microsoft.Extensions.Configuration;
using ProctoringService.Models;
using ProctoringService.Services;
using QuestionBankService.Services;
using ResultService.Models;
using ResultService.Services;
using StorageService.Services;
using WalletService.Services;

namespace ExamPlatform.Tests;

public class UnitTests
{
    [Fact]
    public void TotpHelper_Rfc4648And6238_RoundTripsAndVerifiesValidCode()
    {
        // RFC 4648 known test vector: "12345678901234567890" -> "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"
        var raw = Encoding.ASCII.GetBytes("12345678901234567890");
        var base32 = TotpHelper.ToBase32(raw);
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", base32);
        Assert.Equal(raw, TotpHelper.FromBase32(base32));

        // RFC 6238 Appendix B test vector at t = 59s -> TOTP-SHA1 = 287082 (last 6 of 8 digits "94287082")
        var t59 = DateTimeOffset.FromUnixTimeSeconds(59).UtcDateTime;
        var code = TotpHelper.ComputeCode(base32, t59);
        Assert.Equal("287082", code);
        Assert.True(TotpHelper.VerifyCode(base32, code, t59));
        Assert.False(TotpHelper.VerifyCode(base32, "000000", t59));
        Assert.False(TotpHelper.VerifyCode(base32, "123456", DateTime.UtcNow));
    }

    [Fact]
    public void TotpHelper_GeneratesValidBase32SecretsAndHandlesDriftWindow()
    {
        var secret = TotpHelper.GenerateBase32Secret(20);
        Assert.Equal(32, secret.Length);

        var now = DateTime.UtcNow;
        var prevWindowCode = TotpHelper.ComputeCode(secret, now.AddSeconds(-30));
        Assert.True(TotpHelper.VerifyCode(secret, prevWindowCode, now, allowedDriftWindows: 1));
    }

    [Fact]
    public void EvaluationService_ScoresObjectiveAndManualAndNegativeMarkingAccurately()
    {
        // Correct MCQ
        var mcqCorrect = EvaluationService.EvaluateQuestion(new EvaluationQuestionInput
        {
            QuestionId = "q1",
            QuestionType = "MCQ",
            Marks = 4,
            CorrectAnswer = "opt-a",
            SelectedOption = "OPT-A"
        }, examNegativeMarking: true, examNegativePercentage: 0.25);

        Assert.True(mcqCorrect.IsCorrect);
        Assert.False(mcqCorrect.NeedsManualGrading);
        Assert.Equal(4.0, mcqCorrect.Score);

        // Wrong MCQ with exam negative marking percentage (25% of 4 = -1.0)
        var mcqWrong = EvaluationService.EvaluateQuestion(new EvaluationQuestionInput
        {
            QuestionId = "q2",
            QuestionType = "MCQ",
            Marks = 4,
            CorrectAnswer = "opt-a",
            SelectedOption = "opt-b"
        }, examNegativeMarking: true, examNegativePercentage: 0.25);

        Assert.False(mcqWrong.IsCorrect);
        Assert.Equal(-1.0, mcqWrong.Score);

        // Unattempted MCQ should NOT lose negative marks
        var mcqSkipped = EvaluationService.EvaluateQuestion(new EvaluationQuestionInput
        {
            QuestionId = "q3",
            QuestionType = "MCQ",
            Marks = 4,
            CorrectAnswer = "opt-a"
        }, examNegativeMarking: true, examNegativePercentage: 0.25);

        Assert.False(mcqSkipped.IsCorrect);
        Assert.Equal(0.0, mcqSkipped.Score);

        // MultipleCorrect set match
        var multi = EvaluationService.EvaluateQuestion(new EvaluationQuestionInput
        {
            QuestionId = "q4",
            QuestionType = "MultipleCorrect",
            Marks = 5,
            CorrectOptions = new List<string> { "o1", "o3" },
            SelectedOptions = new List<string> { "O3", "o1" }
        }, examNegativeMarking: false, examNegativePercentage: 0);

        Assert.True(multi.IsCorrect);
        Assert.Equal(5.0, multi.Score);

        // Subjective flagged for manual grading
        var subj = EvaluationService.EvaluateQuestion(new EvaluationQuestionInput
        {
            QuestionId = "q5",
            QuestionType = "Subjective",
            Marks = 10,
            TextAnswer = "Essay response"
        }, examNegativeMarking: false, examNegativePercentage: 0);

        Assert.True(subj.NeedsManualGrading);
        Assert.Equal(0.0, subj.Score);
    }

    [Fact]
    public void ProctoringRiskScore_AccountsForViolationsAndCapsAt100()
    {
        var session = new ProctoringSession
        {
            TabSwitchCount = 3,           // 6
            MultipleFacesDetected = 2,    // 20
            NoFaceDetectedCount = 1,      // 3
            Violations = new List<Violation>
            {
                new() { Severity = ViolationSeverity.Critical }, // 20
                new() { Severity = ViolationSeverity.High },     // 10
                new() { Severity = ViolationSeverity.Medium }    // 5
            }
        };

        var score = ProctoringManagementService.CalculateRiskScore(session);
        Assert.Equal(64.0, score);
        Assert.Equal(RiskLevel.High, ProctoringManagementService.GetRiskLevel(score));

        session.Violations.Add(new Violation { Severity = ViolationSeverity.Critical });
        session.Violations.Add(new Violation { Severity = ViolationSeverity.Critical });
        var capped = ProctoringManagementService.CalculateRiskScore(session);
        Assert.Equal(100.0, capped);
        Assert.Equal(RiskLevel.Critical, ProctoringManagementService.GetRiskLevel(capped));
    }

    [Fact]
    public void PdfHelpers_GenerateValidSpecCompliantPdfHeaderAndEof()
    {
        var certPdf = PdfDocumentHelper.BuildDocument("Certificate (Test)", new[] { "Line 1 \\ (test)", "Score: 95%" });
        var certText = Encoding.ASCII.GetString(certPdf);
        Assert.StartsWith("%PDF-1.4", certText);
        Assert.Contains("%%EOF", certText);
        Assert.Contains("xref", certText);

        var analyticsPdf = AnalyticsPdfHelper.BuildReportPdf("Analytics Report", new[] { "Users: 10" });
        var analyticsText = Encoding.ASCII.GetString(analyticsPdf);
        Assert.StartsWith("%PDF-1.4", analyticsText);
        Assert.Contains("%%EOF", analyticsText);
    }

    [Fact]
    public void QuestionService_ParseCsvRecords_HandlesBomEscapedQuotesAndMultilineFields()
    {
        const string csv = "\uFEFFQuestionText,Type,CodeSnippet\r\n" +
                           "\"What is \"\"Big-O\"\", really?\",CodeEvaluation,\"int f() {\n  return 1, 2;\n}\"\r\n\r\n" +
                           "Simple Q,Subjective,";

        var rows = QuestionService.ParseCsvRecords(csv);
        Assert.Equal(3, rows.Count);
        Assert.Equal("QuestionText", rows[0][0]);
        Assert.Equal("What is \"Big-O\", really?", rows[1][0]);
        Assert.Equal("CodeEvaluation", rows[1][1]);
        Assert.Equal("int f() {\n  return 1, 2;\n}", rows[1][2]);
        Assert.Equal("Simple Q", rows[2][0]);

        Assert.Throws<InvalidOperationException>(() =>
            QuestionService.ParseCsvRecords("QuestionText\n\"unterminated"));
    }

    [Fact]
    public void QuestionService_GenerateCsvTemplate_IsParseableAndContainsAllSamples()
    {
        var (bytes, contentType, fileName) = QuestionService.GenerateCsvTemplate();
        Assert.Contains("text/csv", contentType);
        Assert.Equal("questions_import_template.csv", fileName);

        var text = Encoding.UTF8.GetString(bytes);
        var records = QuestionService.ParseCsvRecords(text);
        Assert.True(records.Count >= 6); // Header + 5 sample question types
        Assert.Equal("QuestionText", records[0][0]);
        Assert.Contains("Category", records[0]);
        Assert.Contains("Subject", records[0]);
        Assert.Contains("Topic", records[0]);
        Assert.DoesNotContain("Tags", records[0]);
        Assert.Contains(records.Skip(1), r => r[1] == "MCQ");
        Assert.Contains(records.Skip(1), r => r[1] == "MultipleCorrect");
        Assert.Contains(records.Skip(1), r => r[1] == "TrueFalse");
    }

    [Fact]
    public void MediaTicketService_SignsAndVerifiesTicketsPerFileAndExpiry()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = "SuperSecretUnitTestKey_1234567890!" })
            .Build();

        var service = new MediaTicketService(config);
        var rawUrl = "/api/v1/files/download/course-videos/lecture1.mp4";
        var signed = service.CreateSignedStreamUrl(rawUrl, "user-42", TimeSpan.FromMinutes(10));

        Assert.Contains("?ticket=", signed);
        var ticket = Uri.UnescapeDataString(signed.Split("?ticket=")[1]);

        Assert.True(service.ValidateTicket(ticket, "course-videos", "lecture1.mp4", out var userId));
        Assert.Equal("user-42", userId);

        // Cannot reuse ticket on a different folder or file
        Assert.False(service.ValidateTicket(ticket, "course-materials", "lecture1.mp4", out _));
        Assert.False(service.ValidateTicket(ticket, "course-videos", "other.mp4", out _));
        Assert.False(service.ValidateTicket(ticket + "tamper", "course-videos", "lecture1.mp4", out _));

        // Expired ticket must be rejected
        var expired = service.CreateSignedStreamUrl(rawUrl, "user-42", TimeSpan.FromSeconds(-5));
        var expiredTicket = Uri.UnescapeDataString(expired.Split("?ticket=")[1]);
        Assert.False(service.ValidateTicket(expiredTicket, "course-videos", "lecture1.mp4", out _));
    }

    [Fact]
    public void QuestionService_GenerateExamSectionCsvTemplate_ContainsSectionColumnAndSamples()
    {
        var (bytes, contentType, fileName) = QuestionService.GenerateExamSectionCsvTemplate();
        Assert.Equal("text/csv; charset=utf-8", contentType);
        Assert.Equal("exam_section_questions_template.csv", fileName);
        Assert.True(bytes.Length > 3);

        var text = Encoding.UTF8.GetString(bytes);
        var records = QuestionService.ParseCsvRecords(text);
        Assert.True(records.Count >= 5); // Header + 4 section sample rows
        Assert.Equal("Section", records[0][0]);
        Assert.Equal("QuestionText", records[0][1]);
        Assert.DoesNotContain("Tags", records[0]);
        Assert.Contains(records.Skip(1), r => r[0] == "Section A - Core Concepts" && r[2] == "MCQ");
        Assert.Contains(records.Skip(1), r => r[0] == "Section A - Core Concepts" && r[2] == "MultipleCorrect");
        Assert.Contains(records.Skip(1), r => r[0] == "Section B - Aptitude & Code" && r[2] == "TrueFalse");
        Assert.Contains(records.Skip(1), r => r[0] == "Section B - Aptitude & Code" && r[2] == "CodeEvaluation");
    }

    [Fact]
    public void WalletAndPricing_ReferralRewardIs100_AndEffectivePriceHonorsValidDiscounts()
    {
        Assert.Equal(100m, WalletManager.ReferralRewardCoins);

        var freeExam = new Exam { Price = 0m, DiscountPrice = 0m };
        Assert.Equal(0m, freeExam.GetEffectivePrice());

        var discountedExam = new Exam { Price = 150m, DiscountPrice = 90m };
        Assert.Equal(90m, discountedExam.GetEffectivePrice());

        var invalidHigherDiscount = new Exam { Price = 80m, DiscountPrice = 120m };
        Assert.Equal(80m, invalidHigherDiscount.GetEffectivePrice());

        var seriesInfo = new PurchaseableSeriesInfo { Price = 200m, DiscountPrice = 140m };
        Assert.Equal(140m, seriesInfo.EffectivePrice);
    }
}
