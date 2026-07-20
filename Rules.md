# Exam Platform - Development Rules & Standards

## Code Organization

### Naming Conventions
- **Namespaces**: `{ServiceName}.{Layer}`
  - `AuthService.Controllers`, `AuthService.Models`, `AuthService.Services`
- **Classes**: PascalCase
  - Controllers: `{Entity}Controller` (e.g., `AuthController`)
  - Services: `{Entity}Service` (e.g., `AuthenticationService`)
  - Models: Descriptive noun (e.g., `User`, `ExamAttempt`)
- **Methods**: PascalCase
- **Variables**: camelCase
- **Constants**: UPPER_SNAKE_CASE

### File Structure
```
ServiceName/
├── Controllers/
│   └── {Entity}Controller.cs
├── Models/
│   ├── {Entity}.cs (domain model)
│   ├── {Entity}Request.cs (DTOs)
│   ├── {Entity}Response.cs (DTOs)
│   └── Enums.cs
├── Services/
│   ├── MongoDbService.cs
│   ├── {Entity}Service.cs (business logic)
│   └── ILogger<T> injected
├── Program.cs
├── appsettings.json
└── {ServiceName}.csproj
```

## Security Standards

### Authentication
- ✅ Use JWT Bearer tokens (already implemented)
- ❌ **TODO**: Remove hardcoded JWT secret from appsettings.json
- ❌ **TODO**: Add token expiration (recommended: 15-30 minutes)
- ❌ **TODO**: Implement refresh token mechanism
- ❌ **TODO**: Add logout endpoint (token blacklist with Redis)
- ✅ Use BCrypt for password hashing (verified)

### Configuration & Secrets
- ❌ **Never** commit secrets to version control
- ✅ Use environment variables for sensitive data:
  ```csharp
  var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET") 
    ?? builder.Configuration["Jwt:Key"];
  ```
- ✅ Use secrets manager in production:
  - Azure: Azure Key Vault
  - AWS: AWS Secrets Manager
  - Local dev: user-secrets CLI

### API Security
- ✅ Implement CORS (currently: AllowAnyOrigin) 
- ❌ **TODO**: Whitelist specific frontend URLs:
  ```csharp
  policy.WithOrigins("https://yourfrontend.com", "https://app.yourfrontend.com")
        .AllowAnyMethod()
        .AllowAnyHeader();
  ```
- ✅ Add input validation on all DTOs
- ❌ **TODO**: Implement rate limiting (AspNetCoreRateLimit)
- ❌ **TODO**: Add API versioning (v1, v2 in routes)

### Data Protection
- ✅ Hash passwords (BCrypt)
- ❌ **TODO**: Encrypt sensitive data fields (PII)
- ❌ **TODO**: Implement field-level encryption for exam answers
- ❌ **TODO**: Add audit logging for security events

## Error Handling Standards

### Exception Handling
- ✅ Use try-catch for async operations
- ❌ **TODO**: Create custom exception types:
  ```csharp
  public class UserNotFoundException : Exception { }
  public class ExamExpiredException : Exception { }
  public class InvalidCredentialsException : Exception { }
  ```

### API Response Format
- ✅ Return standardized response objects:
  ```csharp
  public class ApiResponse<T>
  {
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public List<string>? Errors { get; set; }
  }
  ```

### HTTP Status Codes
- `200 OK` - Successful request
- `201 Created` - Resource created
- `400 Bad Request` - Validation error
- `401 Unauthorized` - Auth required
- `403 Forbidden` - Auth successful but not authorized
- `404 Not Found` - Resource doesn't exist
- `409 Conflict` - Resource already exists
- `500 Internal Server Error` - Server error

## Logging Standards

### Current State
- ❌ Only basic Console logging
- ⚠️  No structured logging

### Required Implementation
- ✅ Use `ILogger<T>` (already injected in services)
- ❌ **TODO**: Integrate Serilog for structured logging:
  ```csharp
  // Log with context
  _logger.LogInformation("User {UserId} logged in at {LoginTime}", 
    userId, DateTime.UtcNow);
  
  _logger.LogError(ex, "Error processing exam attempt {AttemptId}", 
    attemptId);
  ```

### Logging Levels
- `Information` - User actions, successful operations
- `Warning` - Unexpected but recoverable issues
- `Error` - Errors that need investigation
- `Debug` - Development debugging only
- `Trace` - Detailed flow information

### PII Handling
- ❌ Never log passwords, email addresses
- ✅ Log user IDs, role IDs instead
- Example: ❌ `"User john@example.com logged in"` 
- Example: ✅ `"User 12345 (role: Student) logged in"`

## Database Standards

### MongoDB Best Practices
- ✅ Use GUID for `_id` (currently implemented)
- ✅ Use `UpdateDefinitionBuilder` for updates (avoid document replacement)
- ❌ **TODO**: Add indexes on frequently queried fields
  ```csharp
  var indexModel = new CreateIndexModel<User>(
    Builders<User>.IndexKeys.Ascending(u => u.Email));
  _collection.Indexes.CreateOneAsync(indexModel);
  ```

### Query Optimization
- ❌ Currently: No visible pagination
- ✅ **TODO**: Implement pagination:
  ```csharp
  var page = (request.Page - 1) * request.PageSize;
  var results = await collection
    .Find(filter)
    .Skip(page)
    .Limit(request.PageSize)
    .ToListAsync();
  ```

### Connection Management
- ✅ MongoDB driver handles connection pooling
- ⚠️  Verify connection string in production uses connection pooling

## Async/Await Standards
- ✅ All I/O operations use async/await
- ✅ Avoid `.Result` or `.Wait()` (causes deadlocks)
- ✅ Use `Task` for fire-and-forget scenarios (but log errors):
  ```csharp
  _ = NotifyUserAsync(userId); // Fire and forget
  ```

## Testing Standards

### Current State
- ❌ No tests found

### Required Implementation
- ✅ Unit tests for all services (target: 80% coverage)
- ✅ Integration tests for API endpoints
- ✅ Setup test database (separate MongoDB instance)

### Test Structure
```csharp
// Using xUnit
[Fact]
public async Task LoginAsync_WithValidCredentials_ReturnsToken()
{
  // Arrange
  var request = new LoginRequest { Email = "test@test.com", Password = "Test123!" };
  
  // Act
  var result = await _service.LoginAsync(request);
  
  // Assert
  Assert.True(result.Success);
  Assert.NotEmpty(result.Token);
}
```

## API Documentation Standards

### Swagger/OpenAPI
- ✅ Add XML comments to controllers:
  ```csharp
  /// <summary>
  /// Login a user with email and password
  /// </summary>
  /// <param name="request">Login credentials</param>
  /// <returns>JWT token and user info</returns>
  [HttpPost("login")]
  public async Task<IActionResult> Login([FromBody] LoginRequest request)
  ```

- ✅ Document DTOs:
  ```csharp
  /// <summary>
  /// User registration request
  /// </summary>
  public class RegisterRequest
  {
    /// <summary>User email address (must be unique)</summary>
    [Required, EmailAddress]
    public string Email { get; set; }
  }
  ```

## Deployment Standards

### Configuration Management
- ✅ Separate `appsettings.json` per environment:
  - `appsettings.Development.json`
  - `appsettings.Staging.json`
  - `appsettings.Production.json`

### Environment Variables
- ✅ Use environment-specific configs:
  ```csharp
  var config = builder.Configuration;
  var mongoConnection = config["MongoDb:ConnectionString"] 
    ?? throw new InvalidOperationException("Missing MongoDB connection");
  ```

### Health Checks
- ❌ **TODO**: Add health check endpoints:
  ```csharp
  app.MapHealthChecks("/health");
  
  builder.Services.AddHealthChecks()
    .AddMongoDb(mongoConnection)
    .AddCheck("ApiCheck", () => HealthCheckResult.Healthy());
  ```

## Performance Standards

### Response Time Targets
- API Endpoints: < 200ms (p95)
- Database Queries: < 50ms (p95)
- Real-time Updates: < 100ms (p95)

### Caching Strategy (To be implemented)
- Cache questions for 1 hour
- Cache exams for 30 minutes
- Cache user preferences for 5 minutes
- Use Redis with TTL-based expiration

### N+1 Query Prevention
- ⚠️  Verify VideoClassesService doesn't load comments one-by-one
- Use `.ToListAsync()` only on final queries
- Batch operations where possible

## Code Review Checklist
- [ ] Code follows naming conventions
- [ ] All methods have XML comments
- [ ] Async/await used correctly
- [ ] No hardcoded secrets
- [ ] Input validation present
- [ ] Error handling implemented
- [ ] Logging statements added
- [ ] Unit tests pass
- [ ] No N+1 queries
- [ ] CORS/Security checks pass
