# Exam Platform - Project Knowledge Base

## Quick Reference

### Service Ports
```
5000 - ExamSolution (Main gateway)
5001 - AuthService
5002 - ExamService
5003 - ExamAttemptService
5004 - ResultService
5005 - NotificationService
5006 - QuestionBankService
5007 - ProctoringService
5008 - VideoClassesService
5009 - AnalyticsService
```

### MongoDB Connection
```
ConnectionString: mongodb+srv://mnsingh:mnsingh@cluster0.jbxiod6.mongodb.net/?appName=Cluster0
⚠️  WARNING: Credentials hardcoded - move to environment variables
```

### JWT Configuration (appsettings.json)
```json
{
  "Jwt": {
    "Key": "YourSuperSecretKeyForJWTTokenGeneration12345678901234567890",
    "Issuer": "ExamSystem",
    "Audience": "ExamSystemUsers"
  }
}
```
⚠️  WARNING: Secret hardcoded - use secrets manager

### Framework Versions
- Main app: .NET 9.0
- Services: .NET 8.0
- MongoDB Driver: 3.0.0
- BCrypt.Net-Next: 4.2.0
- JWT Bearer: 9.0.0

---

## Architecture Diagrams

### Request Flow for Taking an Exam
```
1. User authenticates
   POST /api/auth/login
   Response: JWT token
   
2. User starts exam
   POST /api/v1/attempts/start
   Response: ExamAttempt object
   - Proctoring session starts in background
   
3. User answers questions
   POST /api/v1/attempts/{attemptId}/answer
   Request: Answer object
   - Answer saved to MongoDB
   - Real-time update via SignalR (proctor dashboard)
   
4. User submits exam
   POST /api/v1/attempts/{attemptId}/submit
   - Exam locked
   - Results calculated (ResultService)
   - Notification sent (NotificationService)
   - Analytics recorded (AnalyticsService)
   
5. User views result
   GET /api/v1/results/{attemptId}
   Response: Result object with score/pass-fail
```

### Real-time Proctoring Flow
```
Student Browser              Server                  Proctor Browser
     │                          │                          │
     │──WebSocket connect──────▶│                          │
     │  (/hubs/proctoring)      │                          │
     │                          │◀──WebSocket connect──────│
     │                          │  (/hubs/proctoring)      │
     │                          │                          │
     │──Snapshot sent──────────▶│                          │
     │  (webcam frame)          │──Broadcast snapshot─────▶│
     │                          │  (all connected proctors) │
     │                          │                          │
     │◀─Alert (violation)───────│◀─Report violation────────│
     │  (tab switch detected)   │  (manual flagging)       │
```

---

## Common Development Tasks

### Adding a New API Endpoint

1. **Create DTO in Models**
```csharp
// Models/CreateExamRequest.cs
public class CreateExamRequest
{
    [Required]
    public string Title { get; set; }
    
    [Required]
    public int TotalMarks { get; set; }
}
```

2. **Create Service Method**
```csharp
// Services/ExamManagementService.cs
public async Task<Exam> CreateExamAsync(CreateExamRequest request, string createdById)
{
    var exam = new Exam
    {
        Title = request.Title,
        TotalMarks = request.TotalMarks,
        CreatedBy = createdById,
        CreatedAt = DateTime.UtcNow
    };
    
    await _mongoDb.Exams.InsertOneAsync(exam);
    return exam;
}
```

3. **Create Controller Endpoint**
```csharp
// Controllers/ExamsController.cs
[HttpPost]
[Authorize]
public async Task<IActionResult> CreateExam([FromBody] CreateExamRequest request)
{
    var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var exam = await _examService.CreateExamAsync(request, userId);
    return CreatedAtAction(nameof(GetExam), new { id = exam.Id }, exam);
}
```

4. **Add to Swagger documentation**
```csharp
/// <summary>
/// Create a new exam
/// </summary>
/// <param name="request">Exam details</param>
/// <returns>Created exam object</returns>
```

### Adding a New Service Dependency

1. **In service Program.cs**
```csharp
builder.Services.AddScoped<YourNewService>();
```

2. **In main app Program.cs** (examsolution)
```csharp
builder.Services.AddScoped<YourService.Services.YourNewService>();
```

3. **Inject in controller**
```csharp
public class YourController : ControllerBase
{
    private readonly YourNewService _service;
    
    public YourController(YourNewService service)
    {
        _service = service;
    }
}
```

### Writing a Unit Test

```csharp
// AuthService.Tests/AuthenticationServiceTests.cs
public class AuthenticationServiceTests
{
    private readonly AuthenticationService _service;
    private readonly Mock<MongoDbService> _mongoDbMock;
    
    public AuthenticationServiceTests()
    {
        _mongoDbMock = new Mock<MongoDbService>();
        _service = new AuthenticationService(_mongoDbMock.Object, _configMock.Object);
    }
    
    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsToken()
    {
        // Arrange
        var request = new LoginRequest { Email = "test@test.com", Password = "Test123!" };
        var user = new User { Email = "test@test.com", PasswordHash = BCrypt.HashPassword("Test123!") };
        
        _mongoDbMock.Setup(m => m.Users.Find(...))
            .ReturnsAsync(user);
        
        // Act
        var result = await _service.LoginAsync(request);
        
        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.Token);
    }
}
```

---

## Troubleshooting

### Issue: JWT Token not validating
**Cause**: Secret key mismatch  
**Solution**: Ensure JWT_SECRET env var matches between services
```bash
# Set environment variable
export JWT_SECRET="your-secret-key"
```

### Issue: SignalR connections dropping
**Cause**: Single instance can't handle load, needs Redis backplane  
**Solution**: Phase 2 task - add Redis + configure backplane
```csharp
services.AddSignalR()
    .AddStackExchangeRedis(o => o.Configuration = "localhost:6379");
```

### Issue: MongoDB connection timeout
**Cause**: Connection string wrong or database down  
**Solution**: Verify connection string in appsettings.json
```
Check: mongodb+srv://user:pwd@cluster.mongodb.net/?appName=Cluster0
- User account exists
- IP whitelist includes server IP
- Network connectivity verified
```

### Issue: CORS errors from frontend
**Cause**: Wildcard CORS is too permissive  
**Solution**: Phase 1 fix - whitelist specific origins
```csharp
policy.WithOrigins("https://frontend.com")
      .AllowAnyMethod()
      .AllowAnyHeader();
```

### Issue: N+1 queries on VideoComments
**Cause**: Loading comments one-by-one in loop  
**Solution**: Load all at once
```csharp
// ❌ Bad: N+1 queries
foreach (var comment in commentIds)
{
    comments.Add(await GetCommentAsync(comment));
}

// ✅ Good: Single query
var comments = await _db.Comments
    .Find(c => commentIds.Contains(c.Id))
    .ToListAsync();
```

---

## Important Code Patterns

### Async Database Query Pattern
```csharp
public async Task<User?> GetUserByEmailAsync(string email)
{
    return await _mongoDb.Users
        .Find(u => u.Email == email)
        .FirstOrDefaultAsync();
}
```

### Update Pattern (MongoDB)
```csharp
var update = Builders<User>.Update.Set(u => u.LastLoginAt, DateTime.UtcNow);
await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, update);
```

### List with Filter
```csharp
public async Task<List<Exam>> GetExamsByInstructorAsync(string instructorId)
{
    return await _mongoDb.Exams
        .Find(e => e.CreatedBy == instructorId)
        .SortByDescending(e => e.CreatedAt)
        .ToListAsync();
}
```

### Service Dependency Injection
```csharp
public class ExamController : ControllerBase
{
    private readonly ExamManagementService _examService;
    private readonly ILogger<ExamController> _logger;
    
    public ExamController(ExamManagementService examService, ILogger<ExamController> logger)
    {
        _examService = examService;
        _logger = logger;
    }
}
```

---

## Git Workflow (Recommended)

### Branch Naming
- `feature/description` - New features
- `bugfix/description` - Bug fixes
- `docs/description` - Documentation
- `refactor/description` - Refactoring

### Commit Message Format
```
[PHASE-1] Short description

Longer description of changes made.
- Bullet point 1
- Bullet point 2

Closes #123
```

### Pull Request Process
1. Create feature branch
2. Make changes, commit with message format above
3. Push to GitHub
4. Create PR with description
5. Ensure CI/CD passes
6. Get code review approval
7. Squash and merge

---

## Performance Benchmarks

### Target Metrics
- **API Response**: <200ms (p95)
- **DB Query**: <50ms (p95)
- **SignalR Message**: <100ms (p95)

### Monitoring Commands
```bash
# Check MongoDB connection
mongo "mongodb+srv://..." --eval "db.adminCommand('ping')"

# Check service health
curl http://localhost:5000/health

# View real-time logs
tail -f logs/application.log | grep ERROR
```

---

## Security Checklist (Before Production)

- [ ] All secrets moved to environment variables
- [ ] JWT token expiration configured (15-30 min)
- [ ] Refresh token mechanism implemented
- [ ] CORS whitelist configured (remove AllowAnyOrigin)
- [ ] Input validation on all endpoints
- [ ] Rate limiting configured
- [ ] SSL/TLS enforced (HTTPS only)
- [ ] Database backups configured
- [ ] Audit logging enabled
- [ ] Penetration testing completed
- [ ] Security headers added (CSP, X-Frame-Options, etc.)
- [ ] API documentation does not expose secrets

---

## Useful Resources

### Internal
- [PRD.md](PRD.md) - Product requirements
- [Architecture.md](Architecture.md) - System design
- [Rules.md](Rules.md) - Development standards
- [Phases.md](Phases.md) - Implementation roadmap
- [Design.md](Design.md) - Design decisions

### External
- [ASP.NET Core Docs](https://docs.microsoft.com/en-us/aspnet/core/)
- [MongoDB C# Driver](https://www.mongodb.com/docs/drivers/csharp/)
- [SignalR Documentation](https://docs.microsoft.com/en-us/aspnet/core/signalr/)
- [JWT Handbook](https://auth0.com/resources/ebooks/jwt-handbook)
- [Microservices Patterns](https://microservices.io/patterns/index.html)

---

## Team Contacts & Responsibilities

| Role | Responsibility | Contact |
|------|-----------------|---------|
| Project Lead | Overall roadmap, decisions | TBD |
| Backend Lead | Architecture, code review | TBD |
| DevOps Lead | Deployment, infrastructure | TBD |
| QA Lead | Testing, quality assurance | TBD |

---

## Meeting Notes Archive

### Architecture Review - [Date TBD]
- Discussed Phase 2 real-time scaling
- Decided on Redis backplane for SignalR
- Assigned tasks to team

### Security Audit - [Date TBD]
- Identified hardcoded secrets
- Planned Phase 1 security hardening
- Set deadlines for fixes

*(Add meeting notes as discussions happen)*

---

## Known Issues & Workarounds

| Issue | Severity | Workaround | Timeline |
|-------|----------|-----------|----------|
| Hardcoded JWT secret | 🔴 Critical | Move to env var immediately | Phase 1 Week 1 |
| CORS too permissive | 🔴 Critical | Whitelist origins | Phase 1 Week 1 |
| No token expiration | 🟠 High | Implement TTL | Phase 1 Week 2 |
| Email as "Log" | 🟠 High | Configure SMTP | Phase 1 Week 4 |
| SignalR single instance | 🟠 High | Add Redis (Phase 2) | Phase 2 Week 3 |
| No caching | 🟡 Medium | Redis cache (Phase 2) | Phase 2 Week 1 |
| No monitoring | 🟡 Medium | OpenTelemetry (Phase 3) | Phase 3 Week 1 |
