using examsolution.Middleware;
using examsolution.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// CORS CONFIGURATION
// ==========================================
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:3000", "http://localhost:5173" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)  // Only allow configured origins
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();           // Required for SignalR + auth cookies
    });
});

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);   // default v1
    options.AssumeDefaultVersionWhenUnspecified = true; // agar client version na de to default use ho
    options.ReportApiVersions = true;                   // response headers me supported versions show kare
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
});

builder.Services.AddVersionedApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV"; // v1, v2, v3
    options.SubstituteApiVersionInUrl = true;
});


// ==========================================
// 1. JWT AUTHENTICATION CONFIGURATION
// ==========================================
var jwtSettings = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSettings["Key"];
if (string.IsNullOrEmpty(jwtKey) || jwtKey.StartsWith("SET_VIA_ENV"))
    throw new InvalidOperationException("JWT Key is not configured. Set the Jwt__Key environment variable.");
var key = Encoding.UTF8.GetBytes(jwtKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };

    // Support SignalR WebSocket/SSE access_token query parameter on /hubs/*
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        if (!context.HttpContext.Response.HasStarted)
        {
            context.HttpContext.Response.ContentType = "application/json";
            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                message = "Too many requests. Please try again later."
            }, token);
        }
    };

    var authPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:AuthPermitLimit") ?? 30;
    options.AddPolicy("auth", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, authPermitLimit),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

// Shared MongoClient connection pool across modules
var sharedMongoConnectionString = builder.Configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(sharedMongoConnectionString));


// ==========================================
// 2. REGISTER CORE SERVICES & CONTROLLERS
// ==========================================
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // ==========================================
    // API VERSIONED SWAGGER DOCUMENTS
    // ==========================================

    // Swagger documents are configured using the
    // API version groups generated by ApiExplorer.
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Course Service API",
        Version = "v1",
        Description = "Course Service API - Version 1.0"
    });

    // If you add v2 later:
    
    options.SwaggerDoc("v2", new OpenApiInfo
    {
        Title = "Course Service API",
        Version = "v2",
        Description = "Course Service API - Version 2.0"
    });

    // ==========================================
    // SCHEMA ID
    // ==========================================

    options.CustomSchemaIds(type => type.FullName);

    // ==========================================
    // JWT AUTHENTICATION
    // ==========================================

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    // ==========================================
    // API VERSION GROUPING
    // ==========================================

    options.DocInclusionPredicate((documentName, apiDescription) =>
    {
        return apiDescription.GroupName == documentName;
    });
});

builder.Services.AddSignalR();
builder.Services.AddRazorPages();
builder.Services.AddHttpClient();
builder.Services.AddHealthChecks();


// ==========================================
// 3. REGISTER ALL MICROSERVICES DEPENDENCIES
// ==========================================

// --- AuthService ---
// FIX: Standardized to Singleton (MongoDB driver is thread-safe; singleton avoids per-request connection overhead)
builder.Services.AddTransient<AuthService.Services.IEmailService, AuthService.Services.EmailService>();
builder.Services.AddTransient<AuthService.Services.IPhoneService, AuthService.Services.PhoneService>();
builder.Services.AddSingleton<AuthService.Services.MongoDbService>();
builder.Services.AddScoped<AuthService.Services.IReferralRewardHandler, AuthReferralRewardAdapter>();
builder.Services.AddScoped<AuthService.Services.AuthenticationService>();
builder.Services.AddScoped<AuthService.Services.UserService>();
builder.Services.AddScoped<AuthService.Services.RoleService>();
builder.Services.AddScoped<AuthService.Services.PermissionService>();
builder.Services.AddScoped<AuthService.Services.AuditService>();

// --- ExamService ---
builder.Services.AddSingleton<ExamService.Services.MongoDbService>();
builder.Services.AddScoped<ExamService.Services.IExamAnalyticsProvider, ExamAnalyticsProviderAdapter>();
builder.Services.AddScoped<ExamService.Controllers.V1.IExamQuestionDetailResolver, ExamQuestionDetailResolver>();
builder.Services.AddScoped<ExamService.Services.IExamQuestionAutomation, ExamQuestionAutomationAdapter>();
builder.Services.AddScoped<ExamService.Services.ExamManagementService>();
builder.Services.AddScoped<ExamService.Services.ExamSeriesManagementService>();

// --- ResultService ---
builder.Services.AddSingleton<ResultService.Services.MongoDbService>();
builder.Services.AddScoped<ResultService.Services.IAttemptEvaluationSource, AttemptEvaluationSourceAdapter>();
builder.Services.AddScoped<ResultService.Services.EvaluationService>();

// --- ExamAttemptService ---
builder.Services.AddSingleton<ExamAttemptService.Services.MongoDbService>();
builder.Services.AddScoped<ExamAttemptService.Services.IAttemptExamProvider, AttemptExamProviderAdapter>();
builder.Services.AddScoped<ExamAttemptService.Services.IAttemptSubmittedHandler, AttemptSubmittedHandlerAdapter>();
builder.Services.AddScoped<ExamAttemptService.Services.AttemptManagementService>();

// --- NotificationService ---
builder.Services.AddSingleton<NotificationService.Services.MongoDbService>();
builder.Services.AddScoped<NotificationService.Services.NotificationManagementService>();
builder.Services.AddScoped<NotificationService.Services.EmailService>();

// --- QuestionBankService ---
builder.Services.AddSingleton<QuestionBankService.Services.MongoDbService>();
builder.Services.AddScoped<QuestionBankService.Services.QuestionService>();

// --- ProctoringService ---
builder.Services.AddSingleton<ProctoringService.Services.MongoDbService>();
builder.Services.AddScoped<ProctoringService.Services.IProctoringAttemptSuspender, ProctoringAttemptSuspenderAdapter>();
builder.Services.AddScoped<ProctoringService.Services.ProctoringManagementService>();
builder.Services.AddScoped<ProctoringService.Services.AIAnalysisService>();

// --- VideoClassesService ---
builder.Services.AddSingleton<VideoClassesService.Services.MongoDbService>();
builder.Services.AddScoped<VideoClassesService.Services.LiveClassService>();
builder.Services.AddScoped<VideoClassesService.Services.IVideoCoursePaymentHandler, VideoCoursePaymentAdapter>();
builder.Services.AddScoped<VideoClassesService.Services.ProgressTrackingService>();
builder.Services.AddScoped<VideoClassesService.Services.CommentService>();
builder.Services.AddScoped<VideoClassesService.Services.CourseManagementService>();

// --- CourseService ---
builder.Services.AddSingleton<CourseService.Services.MongoDbService>();
builder.Services.AddScoped<CourseService.Services.CourseManagementService>();

// --- AnalyticsService ---
builder.Services.AddSingleton<AnalyticsService.Services.MongoDbService>();
builder.Services.AddScoped<AnalyticsService.Services.AnalyticsManagementService>();

// --- EnrollmentService ---
builder.Services.AddSingleton<EnrollmentService.Services.MongoDbService>();
builder.Services.AddScoped<EnrollmentService.Services.IExamPurchaseCatalog, ExamPurchaseCatalogAdapter>();
builder.Services.AddScoped<EnrollmentService.Services.EnrollmentManager>();
builder.Services.AddScoped<CourseService.Services.ICourseEnrollmentLookup>(sp =>
    sp.GetRequiredService<EnrollmentService.Services.EnrollmentManager>());
builder.Services.AddScoped<EnrollmentService.Clients.WalletServiceClient>();

// --- WalletService ---
builder.Services.AddSingleton<WalletService.Services.MongoDbService>();
builder.Services.AddScoped<WalletService.Services.IWalletReferralService, WalletReferralServiceAdapter>();
builder.Services.AddScoped<WalletService.Services.WalletManager>();

// --- StorageService ---
builder.Services.AddSingleton<StorageService.Services.MediaTicketService>();
builder.Services.AddSingleton<StorageService.Services.LocalStorageService>();
builder.Services.AddScoped<StorageService.Services.GoogleDriveStorageService>();
builder.Services.AddScoped<StorageService.Services.IStorageService, StorageService.Services.SmartStorageService>();
builder.Services.AddScoped<CourseService.Services.ICourseMediaSigner, PlatformMediaSignerAdapter>();
builder.Services.AddScoped<VideoClassesService.Services.IVideoMediaSigner, PlatformMediaSignerAdapter>();

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

app.Use(async (context, next) =>
{
    Console.WriteLine($"[API HIT] {DateTime.Now:HH:mm:ss} {context.Request.Method} {context.Request.Path}");
    await next();
    Console.WriteLine($"[API DONE] Status: {context.Response.StatusCode}");
});

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
    context.Response.Headers["Permissions-Policy"] = "camera=(self), microphone=(self)";
    await next();
});

// ==========================================
// 4. HTTP REQUEST PIPELINE (MIDDLEWARES)
// ==========================================
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    
    // FIXED: Configured SwaggerUI to map the dropdown to the API Versions correctly
    app.UseSwaggerUI(options =>
    {
        var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
        foreach (var description in provider.ApiVersionDescriptions)
        {
            options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json", $"API {description.GroupName.ToUpperInvariant()}");
        }
    });
}

// Enable CORS middleware before Authentication and Authorization
app.UseCors("AllowFrontend");

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();


// ==========================================
// 5. MAP SIGNALR HUBS
// ==========================================
app.MapHub<ProctoringService.Hubs.ProctoringHub>("/hubs/proctoring");
app.MapHub<NotificationService.Hubs.NotificationHub>("/hubs/notifications");
app.MapHub<VideoClassesService.Hubs.LiveClassHub>("/hubs/liveclass");

// Add health check endpoint
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }