using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AuthService.Models;
using AuthService.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace ExamPlatform.Tests;

public class ExamApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestInternalKey = "IntegrationTestInternalKey_987654321!";
    public string DbPrefix { get; } = $"itest_{Guid.NewGuid():N}"[..16];
    public string TempStorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"examsln_uploads_{Guid.NewGuid():N}");

    private static readonly string[] DbSuffixes =
    {
        "auth", "exam", "result", "attempt", "notification", "question",
        "proctoring", "video", "course", "enrollment", "wallet", "analytics"
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("MongoDb:AuthDatabase", $"{DbPrefix}_auth");
        builder.UseSetting("MongoDb:ExamDatabase", $"{DbPrefix}_exam");
        builder.UseSetting("MongoDb:ResultDatabase", $"{DbPrefix}_result");
        builder.UseSetting("MongoDb:ExamAttemptDatabase", $"{DbPrefix}_attempt");
        builder.UseSetting("MongoDb:NotificationDatabase", $"{DbPrefix}_notification");
        builder.UseSetting("MongoDb:QuestionBankDatabase", $"{DbPrefix}_question");
        builder.UseSetting("MongoDb:ProctoringDatabase", $"{DbPrefix}_proctoring");
        builder.UseSetting("MongoDb:VideoClassesDatabase", $"{DbPrefix}_video");
        builder.UseSetting("MongoDb:CourseDatabase", $"{DbPrefix}_course");
        builder.UseSetting("MongoDb:EnrollmentDatabase", $"{DbPrefix}_enrollment");
        builder.UseSetting("MongoDb:WalletDatabase", $"{DbPrefix}_wallet");
        builder.UseSetting("MongoDb:AnalyticsDatabase", $"{DbPrefix}_analytics");
        builder.UseSetting("Email:Provider", "Log");
        builder.UseSetting("Storage:Provider", "Local");
        builder.UseSetting("Storage:LocalRoot", TempStorageRoot);
        builder.UseSetting("InternalAuth:ServiceKey", TestInternalKey);
        builder.UseSetting("RateLimiting:AuthPermitLimit", "500");
    }

    public async Task InitializeAsync()
    {
        _ = CreateClient();
        await Task.CompletedTask;
    }

    public new async Task DisposeAsync()
    {
        try
        {
            using var scope = Services.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<IMongoClient>();
            foreach (var suffix in DbSuffixes)
            {
                await client.DropDatabaseAsync($"{DbPrefix}_{suffix}");
            }
        }
        catch
        {
            // Ignore cleanup failure if factory never initialized
        }

        try
        {
            if (Directory.Exists(TempStorageRoot))
                Directory.Delete(TempStorageRoot, recursive: true);
        }
        catch
        {
            // Ignore temp cleanup errors
        }

        await base.DisposeAsync();
    }
}

public class ApiIntegrationTests : IClassFixture<ExamApiFactory>
{
    private readonly ExamApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public ApiIntegrationTests(ExamApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<(string UserId, string Token, string RefreshToken)> SeedAndLoginUserAsync(
        string email, UserRole role, string password = "Password123!")
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var mongo = scope.ServiceProvider.GetRequiredService<AuthService.Services.MongoDbService>();
            var existing = await mongo.Users.Find(u => u.Email == email).FirstOrDefaultAsync();
            if (existing == null)
            {
                var user = new User
                {
                    UserId = Random.Shared.Next(100_000_000, 999_999_999).ToString(),
                    Email = email,
                    UserName = email.Split('@')[0],
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                    FullName = $"{role} Test User",
                    Role = role,
                    IsActive = true,
                    IsEmailVerified = true,
                    IsApproved = true
                };
                await mongo.Users.InsertOneAsync(user);
            }
        }

        using var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return (
            doc.GetProperty("user").GetProperty("userId").GetString()!,
            doc.GetProperty("token").GetString()!,
            doc.GetProperty("refreshToken").GetString()!
        );
    }

    private async Task<string?> ReadOtpFromDbAsync(string email, bool phoneOtp = false)
    {
        using var scope = _factory.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<AuthService.Services.MongoDbService>();
        var user = await mongo.Users.Find(u => u.Email == email.ToLowerInvariant()).FirstOrDefaultAsync();
        return phoneOtp ? user?.PhoneOtp : user?.Otp;
    }

    [Fact]
    public async Task Health_And_SecurityHeaders_WorkCorrectly()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(resp.Headers.Contains("X-Content-Type-Options"));
        Assert.Equal("nosniff", resp.Headers.GetValues("X-Content-Type-Options").First());
    }

    [Fact]
    public async Task Auth_RegisterApprovalMfaPhonePatchRbacAndAudit_EndToEndWorks()
    {
        using var client = _factory.CreateClient();

        // 1. Self-registration as Admin must be blocked
        var adminRegResp = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = $"badadmin_{Guid.NewGuid():N}@example.com",
            password = "Password123!",
            fullName = "Bad Admin",
            role = UserRole.Admin
        });
        Assert.Equal(HttpStatusCode.BadRequest, adminRegResp.StatusCode);

        // 2. Register Teacher -> Verify Email -> MUST NOT issue JWT until Admin approves
        var teacherEmail = $"teacher_{Guid.NewGuid():N}@example.com";
        var tReg = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = teacherEmail,
            password = "Password123!",
            fullName = "Pending Teacher",
            role = UserRole.Teacher
        });
        tReg.EnsureSuccessStatusCode();

        var teacherOtp = await ReadOtpFromDbAsync(teacherEmail);
        Assert.False(string.IsNullOrEmpty(teacherOtp));

        // Wrong OTP rejected and tracked, right OTP succeeds without issuing JWT
        var badVerify = await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { email = teacherEmail, otp = "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, badVerify.StatusCode);

        var tVerify = await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { email = teacherEmail, otp = teacherOtp });
        tVerify.EnsureSuccessStatusCode();
        var tVerifyBody = await tVerify.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(tVerifyBody.GetProperty("requiresApproval").GetBoolean());
        Assert.False(tVerifyBody.TryGetProperty("token", out var tokProp) && !string.IsNullOrEmpty(tokProp.GetString()));

        // Teacher login blocked until Admin approves
        var tLoginBlocked = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = teacherEmail, password = "Password123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, tLoginBlocked.StatusCode);

        // Admin seeds + approves teacher + checks Roles/Permissions/Audit DI endpoints
        var (_, adminToken, _) = await SeedAndLoginUserAsync($"admin_{Guid.NewGuid():N}@example.com", UserRole.Admin);
        var teacherUserId = tVerifyBody.GetProperty("user").GetProperty("userId").GetString()!;

        using var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var approveResp = await adminClient.PostAsync($"/api/v1/users/{teacherUserId}/approve", null);
        approveResp.EnsureSuccessStatusCode();

        // Create Permission & Role, Patch Role (tests JsonElement BSON fix), Assign Role to userId (tests u.UserId lookup fix)
        var createPerm = await adminClient.PostAsJsonAsync("/api/v1/permissions", new { name = "exams.grade", description = "Can grade exams" });
        createPerm.EnsureSuccessStatusCode();
        var permId = (await createPerm.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("permissionId").GetString()!;

        var createRole = await adminClient.PostAsJsonAsync("/api/v1/roles", new { name = "Grader", description = "Grader role" });
        createRole.EnsureSuccessStatusCode();
        var roleId = (await createRole.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("roleId").GetString()!;

        var patchRole = await adminClient.PatchAsJsonAsync($"/api/v1/roles/{roleId}", new { description = "Updated grader role" });
        patchRole.EnsureSuccessStatusCode();

        var assignPerm = await adminClient.PostAsJsonAsync($"/api/v1/roles/{roleId}/permissions", new { permissionIds = new[] { permId } });
        assignPerm.EnsureSuccessStatusCode();

        var assignRoleUser = await adminClient.PostAsync($"/api/v1/roles/{roleId}/users/{teacherUserId}", null);
        assignRoleUser.EnsureSuccessStatusCode();

        // Now Teacher can log in
        var tLoginOk = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = teacherEmail, password = "Password123!" });
        tLoginOk.EnsureSuccessStatusCode();
        var tLoginJson = await tLoginOk.Content.ReadFromJsonAsync<JsonElement>();
        var teacherToken = tLoginJson.GetProperty("token").GetString()!;
        var teacherRefresh = tLoginJson.GetProperty("refreshToken").GetString()!;

        // Refresh token rotates and rejects old token replay
        var refreshOk = await client.PostAsJsonAsync("/api/v1/auth/refresh-token", new { token = teacherToken, refreshToken = teacherRefresh });
        refreshOk.EnsureSuccessStatusCode();
        var replayFail = await client.PostAsJsonAsync("/api/v1/auth/refresh-token", new { token = teacherToken, refreshToken = teacherRefresh });
        Assert.Equal(HttpStatusCode.Unauthorized, replayFail.StatusCode);

        var rotatedToken = (await refreshOk.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        // Phone flow (tests PhoneService u.UserId fix) + Patch Me (tests JsonElement fix)
        using var teacherClient = _factory.CreateClient();
        teacherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotatedToken);

        var patchMe = await teacherClient.PatchAsJsonAsync("/api/v1/auth/me", new { fullName = "Approved Teacher Updated", gender = "Other" });
        patchMe.EnsureSuccessStatusCode();

        var addPhone = await teacherClient.PostAsJsonAsync("/api/v1/auth/phone/add", new { phoneNumber = "+15550199222" });
        addPhone.EnsureSuccessStatusCode();
        var phoneOtp = await ReadOtpFromDbAsync(teacherEmail, phoneOtp: true);
        Assert.False(string.IsNullOrEmpty(phoneOtp));
        var verifyPhone = await teacherClient.PostAsJsonAsync("/api/v1/auth/phone/verify", new { email = teacherEmail, otp = phoneOtp });
        verifyPhone.EnsureSuccessStatusCode();

        // Real TOTP MFA Setup -> Enable -> Login challenge
        var mfaSetup = await teacherClient.PostAsJsonAsync("/api/v1/auth/mfa/setup", new { method = "Authenticator" });
        mfaSetup.EnsureSuccessStatusCode();
        var secret = (await mfaSetup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!;

        // Wrong hardcoded "123456" is unlikely unless it matches time window; verify real computed TOTP succeeds
        var totpCode = TotpHelper.ComputeCode(secret, DateTime.UtcNow);
        var mfaEnable = await teacherClient.PostAsJsonAsync("/api/v1/auth/mfa/enable", new { code = totpCode });
        mfaEnable.EnsureSuccessStatusCode();

        var mfaLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = teacherEmail, password = "Password123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, mfaLogin.StatusCode);
        var mfaChallengePayload = await mfaLogin.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(mfaChallengePayload.GetProperty("requiresMfa").GetBoolean());
        Assert.Equal(teacherUserId, mfaChallengePayload.GetProperty("userId").GetString());

        var completeMfa = await client.PostAsJsonAsync("/api/v1/auth/mfa/challenge", new
        {
            userId = teacherUserId,
            code = TotpHelper.ComputeCode(secret, DateTime.UtcNow)
        });
        completeMfa.EnsureSuccessStatusCode();

        // Audit logs recorded and queryable by Admin (forbidden for Teacher)
        var teacherAuditForbidden = await teacherClient.GetAsync("/api/v1/audit-security/audit-logs");
        Assert.Equal(HttpStatusCode.Forbidden, teacherAuditForbidden.StatusCode);

        var adminAuditOk = await adminClient.GetAsync($"/api/v1/audit-security/users/{teacherUserId}/audit-logs");
        adminAuditOk.EnsureSuccessStatusCode();
        var userActivity = await adminClient.GetAsync($"/api/v1/users/{teacherUserId}/activity");
        userActivity.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Course_Wallet_Enrollment_Storage_Workflow_WorksSafely()
    {
        var (teacherId, teacherToken, _) = await SeedAndLoginUserAsync($"teach_crs_{Guid.NewGuid():N}@example.com", UserRole.Teacher);
        var (studentId, studentToken, _) = await SeedAndLoginUserAsync($"stud_crs_{Guid.NewGuid():N}@example.com", UserRole.Student);

        using var teacherClient = _factory.CreateClient();
        teacherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", teacherToken);

        using var studentClient = _factory.CreateClient();
        studentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", studentToken);

        // 1. Storage security: reject spoofed executable disguised as .jpg
        using (var badForm = new MultipartFormDataContent())
        {
            var fakeImage = new ByteArrayContent(Encoding.ASCII.GetBytes("<html><script>alert(1)</script></html>"));
            fakeImage.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            badForm.Add(fakeImage, "file", "evil.jpg");
            var uploadBad = await teacherClient.PostAsync("/api/v1/files/upload-profile-image", badForm);
            Assert.Equal(HttpStatusCode.BadRequest, uploadBad.StatusCode);
        }

        // Valid PDF upload -> Local storage fallback -> Download -> Delete
        string uploadedUrl;
        using (var pdfForm = new MultipartFormDataContent())
        {
            var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n%ValidTestPdf\n%%EOF");
            var pdfContent = new ByteArrayContent(pdfBytes);
            pdfContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            pdfForm.Add(pdfContent, "file", "syllabus.pdf");
            var uploadOk = await teacherClient.PostAsync("/api/v1/files/upload-course-pdf", pdfForm);
            uploadOk.EnsureSuccessStatusCode();
            uploadedUrl = (await uploadOk.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
            Assert.StartsWith("/api/v1/files/download/", uploadedUrl);

            var downloadOk = await studentClient.GetAsync(uploadedUrl);
            downloadOk.EnsureSuccessStatusCode();
            var dlBytes = await downloadOk.Content.ReadAsByteArrayAsync();
            Assert.Equal(pdfBytes, dlBytes);
        }

        // 2. Internal Wallet key enforcement & credit/debit validation
        using var unauthInternal = _factory.CreateClient();
        var missingKeyResp = await unauthInternal.PostAsJsonAsync("/api/internal/wallet/credit", new
        {
            userId = studentId,
            amount = 100m,
            idempotencyKey = $"cred_{Guid.NewGuid():N}"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, missingKeyResp.StatusCode);

        unauthInternal.DefaultRequestHeaders.Add("X-Internal-Service-Key", ExamApiFactory.TestInternalKey);
        var creditResp = await unauthInternal.PostAsJsonAsync("/api/internal/wallet/credit", new
        {
            userId = studentId,
            amount = 100m,
            idempotencyKey = $"cred_{Guid.NewGuid():N}",
            referenceId = "TEST_TOPUP",
            description = "Test coins"
        });
        creditResp.EnsureSuccessStatusCode();

        // 3. Create Course, Section, Item, Patch Section/Item, Publish -> Purchase -> Check Access & Balance
        var createCourse = await teacherClient.PostAsJsonAsync("/api/v1/courses", new
        {
            title = "Full Stack Architecture",
            description = "Deep backend engineering",
            level = 2,
            coursePrice = 40m
        });
        createCourse.EnsureSuccessStatusCode();
        var courseId = (await createCourse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("courseId").GetString()!;

        var addSection = await teacherClient.PostAsJsonAsync($"/api/v1/courses/{courseId}/sections", new { title = "Module 1" });
        addSection.EnsureSuccessStatusCode();
        var sectionId = (await addSection.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sectionId").GetString()!;

        var patchSection = await teacherClient.PatchAsJsonAsync($"/api/v1/courses/{courseId}/sections/{sectionId}", new { title = "Module 1 - Patched" });
        patchSection.EnsureSuccessStatusCode();

        var addItem = await teacherClient.PostAsJsonAsync($"/api/v1/courses/{courseId}/sections/{sectionId}/items", new
        {
            title = "Syllabus PDF",
            type = 1,
            contentUrl = uploadedUrl,
            isFreePreview = true,
            durationSeconds = 300
        });
        addItem.EnsureSuccessStatusCode();
        var itemId = (await addItem.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("itemId").GetString()!;

        var patchItem = await teacherClient.PatchAsJsonAsync($"/api/v1/courses/{courseId}/sections/{sectionId}/items/{itemId}", new { durationSeconds = 450 });
        patchItem.EnsureSuccessStatusCode();

        // Buying unpublished course fails
        var buyDraft = await studentClient.PostAsJsonAsync("/api/v1/enrollment/buy", new { courseId });
        Assert.Equal(HttpStatusCode.BadRequest, buyDraft.StatusCode);

        // Publish & Purchase (even if client tampers price=0, backend charges 40m!)
        await teacherClient.PostAsync($"/api/v1/courses/{courseId}/publish", null);
        var buyOk = await studentClient.PostAsJsonAsync("/api/v1/enrollment/buy", new { courseId, price = 0m });
        buyOk.EnsureSuccessStatusCode();

        var balanceResp = await studentClient.GetFromJsonAsync<JsonElement>("/api/v1/wallet/balance");
        Assert.Equal(60m, balanceResp.GetProperty("balance").GetDecimal());

        var txnsResp = await studentClient.GetFromJsonAsync<JsonElement>("/api/v1/wallet/transactions");
        Assert.True(txnsResp.GetProperty("totalRecords").GetInt64() >= 2);

        var accessResp = await studentClient.GetFromJsonAsync<JsonElement>($"/api/v1/courses/{courseId}/access");
        Assert.True(accessResp.GetProperty("hasAccess").GetBoolean());

        var searchResp = await studentClient.GetFromJsonAsync<JsonElement>("/api/v1/courses/search?query=Architecture");
        Assert.True(searchResp.GetProperty("total").GetInt64() >= 1);
    }

    [Fact]
    public async Task EndToEnd_QuestionBank_Exam_Attempt_Evaluation_Certificate_Proctoring_Analytics()
    {
        var (_, teacherToken, _) = await SeedAndLoginUserAsync($"teach_ex_{Guid.NewGuid():N}@example.com", UserRole.Teacher);
        var (studentAId, studentAToken, _) = await SeedAndLoginUserAsync($"studA_{Guid.NewGuid():N}@example.com", UserRole.Student);
        var (_, studentBToken, _) = await SeedAndLoginUserAsync($"studB_{Guid.NewGuid():N}@example.com", UserRole.Student);

        using var teacherClient = _factory.CreateClient();
        teacherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", teacherToken);

        using var studentAClient = _factory.CreateClient();
        studentAClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", studentAToken);

        using var studentBClient = _factory.CreateClient();
        studentBClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", studentBToken);

        // 1. Create Category + Questions (MCQ + MultipleCorrect) & test lifecycle/review queue
        var catResp = await teacherClient.PostAsJsonAsync("/api/v1/question-categories", new { name = "CS Fundamentals" });
        catResp.EnsureSuccessStatusCode();

        var q1Resp = await teacherClient.PostAsJsonAsync("/api/v1/questions", new
        {
            questionText = "What is 2 + 2?",
            type = 0, // MCQ
            options = new[] { new { id = "opt1", text = "4" }, new { id = "opt2", text = "5" } },
            correctAnswer = "opt1",
            marks = 5.0,
            negativeMarks = 1.0,
            difficultyId = "easy"
        });
        q1Resp.EnsureSuccessStatusCode();
        var q1Id = (await q1Resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("question").GetProperty("id").GetString()!;

        var q2Resp = await teacherClient.PostAsJsonAsync("/api/v1/questions", new
        {
            questionText = "Select prime numbers",
            type = 1, // MultipleCorrect
            options = new[] { new { id = "p2", text = "2" }, new { id = "p3", text = "3" }, new { id = "p4", text = "4" } },
            correctOptions = new[] { "p2", "p3" },
            marks = 5.0,
            difficultyId = "medium"
        });
        q2Resp.EnsureSuccessStatusCode();
        var q2Id = (await q2Resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("question").GetProperty("id").GetString()!;

        // Publish & verify Student bulk question lookup strips answers
        (await teacherClient.PostAsync($"/api/v1/questions/{q1Id}/publish", null)).EnsureSuccessStatusCode();
        (await teacherClient.PostAsync($"/api/v1/questions/{q2Id}/publish", null)).EnsureSuccessStatusCode();

        var studentBulkQ = await studentAClient.PostAsJsonAsync("/api/v1/questions/bulk", new[] { q1Id, q2Id });
        studentBulkQ.EnsureSuccessStatusCode();
        var studentBulkText = await studentBulkQ.Content.ReadAsStringAsync();
        Assert.DoesNotContain("correctAnswer", studentBulkText, StringComparison.OrdinalIgnoreCase);

        // 2. Create Exam with ShowResultsImmediately = true, TotalMarks = 10, PassingMarks = 6
        var examResp = await teacherClient.PostAsJsonAsync("/api/v1/exams", new
        {
            courseId = "CRS-100",
            title = "Midterm Evaluation",
            description = "Automated scoring test",
            durationMinutes = 45,
            type = 0,
            questionIds = new[] { q1Id, q2Id },
            totalMarks = 10,
            passingMarks = 6,
            settings = new { showResultsImmediately = true, requireProctoring = true, enableAutoSubmit = true },
            grading = new { enableNegativeMarking = true, negativeMarkingPercentage = 0.25 }
        });
        examResp.EnsureSuccessStatusCode();
        var examId = (await examResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("exam").GetProperty("examId").GetString()!;

        // Activate exam
        (await teacherClient.PostAsync($"/api/v1/exams/{examId}/activate", null)).EnsureSuccessStatusCode();

        // 3. Student A starts attempt, checks sanitized questions, timer, navigation, saves both correct answers -> Submits
        var startResp = await studentAClient.PostAsJsonAsync("/api/v1/attempts", new { examId });
        startResp.EnsureSuccessStatusCode();
        var attemptId = (await startResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("attempt").GetProperty("id").GetString()!;

        var attemptQs = await studentAClient.GetAsync($"/api/v1/attempts/{attemptId}/questions");
        attemptQs.EnsureSuccessStatusCode();
        var attemptQsText = await attemptQs.Content.ReadAsStringAsync();
        Assert.DoesNotContain("correctAnswer", attemptQsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("What is 2 + 2?", attemptQsText);

        var timerResp = await studentAClient.GetFromJsonAsync<JsonElement>($"/api/v1/attempts/{attemptId}/timer");
        Assert.True(timerResp.GetProperty("timeRemainingSeconds").GetInt32() > 2500);

        (await studentAClient.PostAsJsonAsync($"/api/v1/attempts/{attemptId}/questions/{q1Id}/answer", new { selectedOption = "opt1" })).EnsureSuccessStatusCode();
        (await studentAClient.PostAsJsonAsync($"/api/v1/attempts/{attemptId}/questions/{q2Id}/answer", new { selectedOptions = new[] { "p2", "p3" } })).EnsureSuccessStatusCode();

        var navResp = await studentAClient.GetFromJsonAsync<JsonElement>($"/api/v1/attempts/{attemptId}/navigation");
        Assert.True(navResp.GetProperty("totalTimeRemainingSeconds").GetInt32() > 0);

        // Student B cannot access Student A's attempt
        var hijackAttempt = await studentBClient.GetAsync($"/api/v1/attempts/{attemptId}");
        Assert.Equal(HttpStatusCode.Forbidden, hijackAttempt.StatusCode);

        // 4. Proctoring session + violation updates risk score accurately immediately
        var procStart = await studentAClient.PostAsJsonAsync("/api/v1/proctoring/start", new { attemptId, examId });
        procStart.EnsureSuccessStatusCode();
        var sessionId = (await procStart.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("session").GetProperty("id").GetString()!;

        var violResp = await studentAClient.PostAsJsonAsync("/api/v1/proctoring/violation", new
        {
            sessionId,
            type = 0, // TabSwitch
            description = "Switched tab",
            severity = 3 // Critical (+20 + 2 = 22)
        });
        violResp.EnsureSuccessStatusCode();

        var sessionAfter = await studentAClient.GetFromJsonAsync<JsonElement>($"/api/v1/proctoring/session/{sessionId}");
        Assert.True(sessionAfter.GetProperty("riskScore").GetDouble() >= 22.0);
        Assert.Equal(HttpStatusCode.Forbidden, (await studentBClient.GetAsync($"/api/v1/proctoring/session/{sessionId}")).StatusCode);

        // 5. Student A submits attempt -> Auto-evaluation creates 10/10 result + sends Notification!
        var submitResp = await studentAClient.PostAsync($"/api/v1/attempts/{attemptId}/submit", null);
        submitResp.EnsureSuccessStatusCode();

        var resultResp = await studentAClient.GetFromJsonAsync<JsonElement>($"/api/v1/queries/attempts/{attemptId}/result");
        var resultId = resultResp.GetProperty("id").GetString()!;
        Assert.Equal(10.0, resultResp.GetProperty("totalScore").GetDouble());
        Assert.True(resultResp.GetProperty("isPassed").GetBoolean());

        // Student B cannot read Student A's result or report
        Assert.Equal(HttpStatusCode.Forbidden, (await studentBClient.GetAsync($"/api/v1/results/{resultId}")).StatusCode);

        // Breakdown, Rank, Report PDF
        var rankResp = await studentAClient.GetFromJsonAsync<JsonElement>($"/api/v1/results/{resultId}/rank");
        Assert.Equal(1, rankResp.GetProperty("rank").GetInt32());

        var reportPdfBytes = await studentAClient.GetByteArrayAsync($"/api/v1/results/{resultId}/report");
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(reportPdfBytes));

        // Certificate generation is idempotent, generates real PDF, verifies publicly
        var cert1 = await studentAClient.PostAsJsonAsync("/api/v1/certificates/generate", new { resultId });
        cert1.EnsureSuccessStatusCode();
        var certJson = await cert1.Content.ReadFromJsonAsync<JsonElement>();
        var certId = certJson.GetProperty("id").GetString()!;
        var verCode = certJson.GetProperty("verificationCode").GetString()!;

        var cert2 = await studentAClient.PostAsJsonAsync("/api/v1/certificates/generate", new { resultId });
        Assert.Equal(certId, (await cert2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString());

        var certPdfBytes = await studentAClient.GetByteArrayAsync($"/api/v1/certificates/{certId}/download");
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(certPdfBytes));

        using var anonClient = _factory.CreateClient();
        var verifyResp = await anonClient.GetFromJsonAsync<JsonElement>($"/api/v1/certificates/{verCode}/verify");
        Assert.True(verifyResp.GetProperty("valid").GetBoolean());

        // Notification pushed & accessible by owner only
        var notifs = await studentAClient.GetFromJsonAsync<JsonElement>("/api/v1/notifications/my-notifications");
        Assert.True(notifs.GetArrayLength() >= 1);

        // Analytics endpoints return real DB stats (and forbid Student B reading Student A's analytics)
        var analyticsA = await studentAClient.GetFromJsonAsync<JsonElement>($"/api/v1/analytics/users/{studentAId}");
        Assert.Equal(10.0, analyticsA.GetProperty("totalScore").GetDouble());
        Assert.Equal(HttpStatusCode.Forbidden, (await studentBClient.GetAsync($"/api/v1/analytics/users/{studentAId}")).StatusCode);

        var examStats = await teacherClient.GetFromJsonAsync<JsonElement>($"/api/v1/exams/{examId}/statistics");
        Assert.Equal(1, examStats.GetProperty("totalAttempts").GetInt32());
        Assert.Equal(10.0, examStats.GetProperty("averageScore").GetDouble());

        // Offline Analytics Report create -> generate -> download valid PDF
        var repCreate = await teacherClient.PostAsJsonAsync("/api/v1/analytics/reports", new
        {
            name = "Midterm Summary",
            type = "ExamPerformance",
            filters = new Dictionary<string, string> { ["examId"] = examId }
        });
        repCreate.EnsureSuccessStatusCode();
        var repId = (await repCreate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        (await teacherClient.PostAsync($"/api/v1/analytics/reports/{repId}/generate", null)).EnsureSuccessStatusCode();
        var repPdfBytes = await teacherClient.GetByteArrayAsync($"/api/v1/analytics/reports/{repId}/download");
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(repPdfBytes));

        // 6. Test Proctor suspension terminates active attempt
        var attempt2Start = await studentBClient.PostAsJsonAsync("/api/v1/attempts", new { examId });
        attempt2Start.EnsureSuccessStatusCode();
        var attempt2Id = (await attempt2Start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("attempt").GetProperty("id").GetString()!;

        var proc2Start = await studentBClient.PostAsJsonAsync("/api/v1/proctoring/start", new { attemptId = attempt2Id, examId });
        proc2Start.EnsureSuccessStatusCode();
        var session2Id = (await proc2Start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("session").GetProperty("id").GetString()!;

        (await teacherClient.PostAsync($"/api/v1/proctoring/session/{session2Id}/suspend?reason=MultipleFaces", null)).EnsureSuccessStatusCode();

        var terminatedAttempt = await studentBClient.GetFromJsonAsync<JsonElement>($"/api/v1/attempts/{attempt2Id}");
        Assert.Equal(6, terminatedAttempt.GetProperty("status").GetInt32()); // AttemptStatus.Terminated == 6
    }

    [Fact]
    public async Task QuestionBank_ExamVersions_FreeEnrollment_And_RateLimiting_Work()
    {
        var (_, teacherToken, _) = await SeedAndLoginUserAsync($"teach_qb_{Guid.NewGuid():N}@example.com", UserRole.Teacher);
        var (studentId, studentToken, _) = await SeedAndLoginUserAsync($"stud_free_{Guid.NewGuid():N}@example.com", UserRole.Student);

        using var teacherClient = _factory.CreateClient();
        teacherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", teacherToken);

        using var studentClient = _factory.CreateClient();
        studentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", studentToken);

        // 1. Free course ($0) enrollment succeeds even without prior wallet credit
        var freeCrsResp = await teacherClient.PostAsJsonAsync("/api/v1/courses", new
        {
            title = "Free Orientation",
            description = "Free",
            level = 0,
            coursePrice = 0m
        });
        freeCrsResp.EnsureSuccessStatusCode();
        var freeCourseId = (await freeCrsResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("courseId").GetString()!;
        (await teacherClient.PostAsync($"/api/v1/courses/{freeCourseId}/publish", null)).EnsureSuccessStatusCode();

        var freeEnroll = await studentClient.PostAsJsonAsync("/api/v1/enrollment/buy", new { courseId = freeCourseId });
        freeEnroll.EnsureSuccessStatusCode();

        // 2. Question Bank import -> duplicate -> review queue -> approve -> CSV export
        var importResp = await teacherClient.PostAsJsonAsync("/api/v1/questions/import", new
        {
            questions = new[]
            {
                new { questionText = "Imported Q1", type = 0, correctAnswer = "A", marks = 2.0, difficultyId = "easy" }
            }
        });
        importResp.EnsureSuccessStatusCode();
        var importedId = (await importResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("questions")[0].GetProperty("id").GetString()!;

        var dupResp = await teacherClient.PostAsync($"/api/v1/questions/{importedId}/duplicate", null);
        dupResp.EnsureSuccessStatusCode();
        var dupId = (await dupResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("newQuestionId").GetString()!;
        Assert.NotEqual(importedId, dupId);

        (await teacherClient.PostAsync($"/api/v1/questions/{importedId}/submit-review", null)).EnsureSuccessStatusCode();
        var queue = await teacherClient.GetFromJsonAsync<JsonElement>("/api/v1/questions/review-queue");
        Assert.Contains(importedId, queue.GetRawText());

        (await teacherClient.PostAsJsonAsync($"/api/v1/questions/{importedId}/approve", new { comment = "LGTM" })).EnsureSuccessStatusCode();

        var exportResp = await teacherClient.PostAsJsonAsync("/api/v1/questions/export", new { format = "csv" });
        exportResp.EnsureSuccessStatusCode();
        var csv = await exportResp.Content.ReadAsStringAsync();
        Assert.Contains("Imported Q1", csv);

        // 3. Exam cloning, versions, sections & candidate blocking
        var exResp = await teacherClient.PostAsJsonAsync("/api/v1/exams", new
        {
            title = "Blocked Candidate Exam",
            durationMinutes = 30,
            questionIds = new[] { importedId }
        });
        exResp.EnsureSuccessStatusCode();
        var exId = (await exResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("exam").GetProperty("examId").GetString()!;
        (await teacherClient.PostAsync($"/api/v1/exams/{exId}/activate", null)).EnsureSuccessStatusCode();
        (await teacherClient.PostAsync($"/api/v1/exams/{exId}/candidates/{studentId}/block", null)).EnsureSuccessStatusCode();

        var blockedAttempt = await studentClient.PostAsJsonAsync("/api/v1/attempts", new { examId = exId });
        Assert.Equal(HttpStatusCode.BadRequest, blockedAttempt.StatusCode);

        var cloneResp = await teacherClient.PostAsync($"/api/v1/exams/{exId}/clone", null);
        cloneResp.EnsureSuccessStatusCode();
        var clonedId = (await cloneResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("newExamId").GetString()!;
        Assert.StartsWith("EXM-", clonedId);

        var verResp = await teacherClient.PostAsync($"/api/v1/exams/{exId}/versions", null);
        verResp.EnsureSuccessStatusCode();

        // 4. Rate limiter test on isolated low-limit factory
        using var rlFactory = _factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:AuthPermitLimit", "2"));
        using var rlClient = rlFactory.CreateClient();
        for (int i = 0; i < 2; i++)
        {
            await rlClient.PostAsJsonAsync("/api/v1/auth/login", new { email = "nobody@example.com", password = "x" });
        }
        var throttled = await rlClient.PostAsJsonAsync("/api/v1/auth/login", new { email = "nobody@example.com", password = "x" });
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
    }
}
