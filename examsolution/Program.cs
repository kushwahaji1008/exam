using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// CORS CONFIGURATION (ALLOW ANYWHERE)
// ==========================================
builder.Services.AddCors(options => {
    options.AddPolicy("AllowFrontend", policy => {
        policy.AllowAnyOrigin()   // Allows requests from any origin
              .AllowAnyMethod()   // Allows any HTTP method (GET, POST, etc.)
              .AllowAnyHeader();  // Allows any HTTP headers
    });
});

// ==========================================
// 1. JWT AUTHENTICATION CONFIGURATION
// ==========================================
var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? "YourSuperSecretKeyForJWTTokenGeneration12345678901234567890");

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
});

// ==========================================
// 2. REGISTER CORE SERVICES & CONTROLLERS
// ==========================================
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Added JWT Bearer support to Swagger UI
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token directly below (no 'Bearer' prefix needed)."
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
});

builder.Services.AddSignalR();
builder.Services.AddRazorPages();
builder.Services.AddHttpClient();

// ==========================================
// 3. REGISTER ALL MICROSERVICES DEPENDENCIES
// ==========================================

// --- THE FIX: Map the Interface to the Implementation ---
builder.Services.AddTransient<AuthService.Services.IEmailService, AuthService.Services.EmailService>();
builder.Services.AddTransient<AuthService.Services.IPhoneService, AuthService.Services.PhoneService>();

// --- AuthService ---
builder.Services.AddScoped<AuthService.Services.MongoDbService>();
builder.Services.AddScoped<AuthService.Services.AuthenticationService>();

// --- ExamService ---
builder.Services.AddScoped<ExamService.Services.MongoDbService>();
builder.Services.AddScoped<ExamService.Services.ExamManagementService>();

// --- ResultService ---
builder.Services.AddScoped<ResultService.Services.MongoDbService>();
builder.Services.AddScoped<ResultService.Services.EvaluationService>();

// --- ExamAttemptService ---
builder.Services.AddScoped<ExamAttemptService.Services.MongoDbService>();
builder.Services.AddScoped<ExamAttemptService.Services.AttemptManagementService>();

// --- NotificationService ---
builder.Services.AddScoped<NotificationService.Services.MongoDbService>();
builder.Services.AddScoped<NotificationService.Services.NotificationManagementService>();
builder.Services.AddScoped<NotificationService.Services.EmailService>();

// --- QuestionBankService ---
builder.Services.AddScoped<QuestionBankService.Services.MongoDbService>();
builder.Services.AddScoped<QuestionBankService.Services.QuestionService>();

// --- ProctoringService ---
builder.Services.AddScoped<ProctoringService.Services.MongoDbService>();
builder.Services.AddScoped<ProctoringService.Services.ProctoringManagementService>();
builder.Services.AddScoped<ProctoringService.Services.AIAnalysisService>();

// --- VideoClassesService ---
builder.Services.AddScoped<VideoClassesService.Services.MongoDbService>();
builder.Services.AddScoped<VideoClassesService.Services.LiveClassService>();
builder.Services.AddScoped<VideoClassesService.Services.ProgressTrackingService>();
builder.Services.AddScoped<VideoClassesService.Services.CommentService>();
builder.Services.AddScoped<VideoClassesService.Services.CourseManagementService>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    Console.WriteLine($"[API HIT] {DateTime.Now:HH:mm:ss} {context.Request.Method} {context.Request.Path}");
    await next();
    Console.WriteLine($"[API DONE] Status: {context.Response.StatusCode}");
}); 

// ==========================================
// 4. HTTP REQUEST PIPELINE (MIDDLEWARES)
// ==========================================
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Enable CORS middleware before Authentication and Authorization
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

// ==========================================
// 5. MAP SIGNALR HUBS (Routing for older hubs)
// ==========================================
app.MapHub<ProctoringService.Hubs.ProctoringHub>("/hubs/proctoring");
app.MapHub<NotificationService.Hubs.NotificationHub>("/hubs/notifications");
app.MapHub<VideoClassesService.Hubs.LiveClassHub>("/hubs/liveclass");

app.Run();
