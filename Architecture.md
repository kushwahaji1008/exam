# Exam Platform - System Architecture

## Architecture Style
**Microservices Architecture** with API Gateway Pattern (planned)

```
┌─────────────────────────────────────────────────────────────────┐
│                      Client Applications                         │
│              (Web, Mobile, Admin Dashboard)                     │
└────────────────┬────────────────────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────────────────┐
│                     API Gateway (Planned)                        │
│         (Rate Limiting, Auth, Routing, Request Validation)      │
└────────────────┬────────────────────────────────────────────────┘
                 │
        ┌────────┴─────────┬──────────────┬───────────┬──────────┐
        ▼                  ▼              ▼           ▼          ▼
┌──────────────┐  ┌──────────────┐ ┌──────────────┐ ... ┌──────────────┐
│   Auth       │  │    Exam      │ │   Attempt    │     │  Analytics   │
│   Service    │  │   Service    │ │   Service    │     │  Service     │
│ (Port 5001)  │  │ (Port 5002)  │ │ (Port 5003)  │     │ (Port 5009)  │
└──────┬───────┘  └──────┬───────┘ └──────┬───────┘     └──────┬───────┘
       │                 │               │                    │
   ┌───▼────────────────────────────────▼────────────────────▼──┐
   │              MongoDB (8 Separate Databases)                 │
   │  auth_db | exams_db | attempts_db | results_db | ... etc   │
   └─────────────────────────────────────────────────────────────┘

   Real-time Layer (SignalR):
   ├─ ProctoringHub (/hubs/proctoring)
   ├─ NotificationHub (/hubs/notifications)
   └─ LiveClassHub (/hubs/liveclass)
```

## Service Inventory

| Service | Port | Framework | Responsibility |
|---------|------|-----------|-----------------|
| ExamSolution (Gateway) | 5000 | .NET 9.0 | Central hub, routing, auth middleware |
| AuthService | 5001 | .NET 8.0 | User registration, login, JWT generation |
| ExamService | 5002 | .NET 8.0 | Exam CRUD, settings management |
| ExamAttemptService | 5003 | .NET 8.0 | Exam sessions, answer submission |
| ResultService | 5004 | .NET 8.0 | Result calculation, score evaluation |
| NotificationService | 5005 | .NET 8.0 | Email, in-app notifications |
| QuestionBankService | 5006 | .NET 8.0 | Question management, storage |
| ProctoringService | 5007 | .NET 8.0 | Session monitoring, violation detection |
| VideoClassesService | 5008 | .NET 8.0 | Live classes, video management |
| AnalyticsService | 5009 | .NET 8.0 | Platform analytics, reporting |

## Data Flow

### Exam Taking Flow
```
1. Student logs in (AuthService) → JWT Token
2. Student requests exam (ExamService)
3. Student starts attempt (ExamAttemptService) → Proctoring session starts (ProctoringService)
4. Student submits answers (ExamAttemptService)
5. Exam submitted → Results calculated (ResultService)
6. Notification sent (NotificationService)
7. Analytics recorded (AnalyticsService)
```

### Real-time Features
```
- Proctoring: Student client → SignalR ProctoringHub → Proctor/AI Analysis
- Notifications: NotificationService → SignalR NotificationHub → Student client
- Live Class: Instructor → SignalR LiveClassHub → All connected students
```

## Communication Patterns

### Current Implementation
- **REST API** over HTTP/HTTPS (request-response)
- **SignalR** (real-time bidirectional communication)
- **Direct Dependency Injection** (services coupled in main app)

### Gaps & Future Improvements
- ❌ No Inter-service HTTP calls (services can't communicate)
- ❌ No asynchronous messaging (event bus/message queue)
- ❌ No API Gateway for centralized routing
- ⚠️  Tight coupling of services in main application

## Technology Stack

| Layer | Technology | Version |
|-------|----------|---------|
| API Runtime | .NET | 9.0 (gateway), 8.0 (services) |
| Web Framework | ASP.NET Core | Latest |
| Authentication | JWT (Bearer tokens) | Standard |
| Real-time | SignalR | Built-in |
| Database | MongoDB | 6.0+ |
| Password Hashing | BCrypt.Net-Next | 4.2.0 |
| Logging | Console | (Should upgrade to Serilog) |

## Security Architecture

### Authentication Flow
```
1. User sends credentials (email, password) → AuthService
2. AuthService validates password (BCrypt comparison)
3. JWT token generated with claims (userId, role)
4. Token sent to client
5. Client includes token in Authorization header for subsequent requests
6. JwtBearer middleware validates token on each request
```

### Current Security Issues (See Rules.md for fixes)
- Hardcoded JWT secret in appsettings.json
- Hardcoded MongoDB credentials
- No token expiration visible
- CORS allows all origins

## Scalability Considerations

### Current State (Phase 1)
- Single instance deployment
- MongoDB connection pooling (default)
- No caching layer
- Single SignalR instance (no scale-out)

### Phase 2 Improvements (Recommended)
- Redis caching layer (question bank, exam metadata)
- Redis backplane for SignalR (scale-out real-time)
- Horizontal scaling of services (Kubernetes)
- Database read replicas

### Bottlenecks Identified
1. **Real-time at scale**: Single SignalR instance can't handle 10k+ concurrent
2. **Database queries**: No caching causes repeated MongoDB hits
3. **File storage**: Local filesystem limits to single server
4. **Cross-service calls**: No mechanism for services to communicate

## Deployment Model (Current & Planned)

### Current
- Local development or IIS deployment
- Monolithic appsettings.json with all secrets

### Phase 2 (Recommended)
- Docker containerization per service
- Kubernetes orchestration
- Environment-specific configurations
- Secrets managed via Azure Key Vault / AWS Secrets Manager
- CD/CI pipeline (GitHub Actions, Azure DevOps)

## Monitoring & Observability (Planned)

Currently missing:
- Structured logging (Serilog integration)
- Distributed tracing (OpenTelemetry)
- Metrics collection (Prometheus)
- Health check endpoints
- Performance monitoring (APM)

## Database Design

### MongoDB Structure
8 separate databases (one per service):
- `exam_auth_db` - Users
- `exam_exams_db` - Exams
- `exam_attempts_db` - Attempt records
- `exam_results_db` - Results
- `exam_questions_db` - Questions
- `exam_notifications_db` - Notifications
- `exam_proctoring_db` - Proctoring sessions
- `exam_videos_db` - Video classes

### Current Design Issues
- ❌ No foreign key relationships (separate databases)
- ❌ Data duplication (StudentName in multiple services)
- ⚠️  No apparent indexes (N+1 queries possible)

### Recommended Improvements
- Add indexes on frequently queried fields
- Consider document references where possible
- Implement query optimization

## Integration Points

### External Services (Planned)
- Email provider (currently "Log" provider)
- AI Proctoring analysis (external API)
- Video streaming service (currently local storage)
- Payment gateway (for premium features)

### Third-party APIs
- SMS provider for notifications
- Biometric authentication
- Video conferencing APIs
