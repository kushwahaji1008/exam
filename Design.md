# Exam Platform - Design Decisions & Trade-offs

## Core Architecture Decision

### Decision: Microservices Architecture
**Status**: ✅ ADOPTED  
**Date**: Initial design

### Rationale
- **Scalability**: Each service can scale independently
- **Maintainability**: Clear separation of concerns
- **Team Organization**: Teams own individual services
- **Technology Flexibility**: Services can use different tech stacks (if needed)
- **Independent Deployment**: Services deployable without full rebuild

### Trade-offs
| Benefit | Cost |
|---------|------|
| Independent scaling | Operational complexity (9 services to manage) |
| Clear boundaries | Inter-service communication latency |
| Fault isolation | Distributed debugging complexity |
| Technology choice | Polyglot testing/deployment |
| Team autonomy | Data consistency challenges |

### Alternatives Considered
1. **Monolithic** - Simpler ops, harder to scale exam taking
2. **Serverless** - Auto-scaling, cold starts affect real-time proctoring
3. **Hybrid** - Monolith + serverless functions (selected: full microservices)

---

## Database Decision

### Decision: MongoDB with Separate Databases per Service
**Status**: ✅ ADOPTED  
**Date**: Initial design

### Rationale
- **Data Isolation**: Each service controls its data
- **Independent Scaling**: Each database can be optimized separately
- **Schema Flexibility**: MongoDB allows flexible schemas per service
- **Horizontal Scaling**: Easier to shard by service

### Trade-offs
| Benefit | Cost |
|---------|------|
| Service independence | No foreign key relationships |
| Schema flexibility | Data duplication (StudentName in multiple places) |
| Easier to scale | Complex joins require application-level logic |
| Lower consistency needs | Eventual consistency model |

### Data Duplication Issues
```
Example: StudentName stored in:
- User collection (AuthService)
- ExamAttempt record (ExamAttemptService)
- ProctoringSession (ProctoringService)

Problem: Name changes require updates in 3 places
Solution (Phase 2): Cache User data in Redis
```

### Alternatives Considered
1. **Single MongoDB Database** - Simpler joins, harder to scale
2. **PostgreSQL** - Strong consistency, requires multi-service transactions
3. **Data Lake** - Complex analytics, overkill for current scale
4. **Selected**: Separate DBs with Redis cache (best for microservices)

---

## Authentication Decision

### Decision: JWT + Bearer Tokens
**Status**: ✅ ADOPTED  
**Date**: Initial design

### Rationale
- **Stateless**: No session storage needed
- **Microservices-friendly**: Token validates across services
- **Mobile-friendly**: Works with app and web clients
- **Scalable**: No server state to replicate

### Implementation Concerns (Phase 1 fixes needed)
- ❌ Token expiration not implemented
- ❌ No logout mechanism (token blacklist)
- ❌ Refresh token not implemented
- ❌ Secret stored in appsettings.json

### Alternatives Considered
1. **OAuth 2.0** - More secure, complex to implement
2. **Session-based** - Works but doesn't scale to microservices
3. **API Keys** - Simple, less secure for user-facing APIs
4. **Selected**: JWT with planned improvements

### Recommended Improvements
```csharp
// Add token expiration
var token = new JwtSecurityToken(
    issuer: settings["Issuer"],
    audience: settings["Audience"],
    claims: claims,
    expires: DateTime.UtcNow.AddMinutes(15), // ← Add this
    signingCredentials: new SigningCredentials(key, SecurityAlgorithm.HmacSha256));

// Add refresh token
public async Task<RefreshTokenResponse> RefreshTokenAsync(string expiredToken, string refreshToken)
{
    // Validate refresh token from Redis/DB
    // Generate new access token
    // Return new token pair
}
```

---

## Real-time Communication Decision

### Decision: SignalR for Real-time Features
**Status**: ✅ ADOPTED  
**Date**: Initial design

### Rationale
- **Built-in to ASP.NET Core**: No external dependencies
- **Bidirectional Communication**: Perfect for live proctoring/classes
- **Automatic Fallback**: WebSocket → Server-Sent Events → Long polling
- **Hub-based Pattern**: Clean abstraction

### Current Implementation
```
- ProctoringHub (/hubs/proctoring)
- NotificationHub (/hubs/notifications)
- LiveClassHub (/hubs/liveclass)
```

### Scaling Challenge (Phase 2)
```
Current Issue:
┌─────────────┐         ┌─────────────┐
│  Student 1  │         │  Proctor    │
│ WebSocket   │────────▶│ (Connected  │
└─────────────┘         │  to Hub)    │
                        └─────────────┘

Bottleneck: Single SignalR instance handles all connections

Phase 2 Solution: Redis Backplane
┌─────────────┐         ┌──────────────────┐
│  Student 1  │         │  SignalR Hub 1   │
│ WebSocket   │────────▶│  (Instance A)    │
└─────────────┘         └────────┬─────────┘
                                 │
                           ▼─────┴──────▼
                           │ Redis      │
                           │ Backplane  │
                           └─────┬──────┘
                                 │
┌─────────────┐         ┌────────▼─────────┐
│  Student 2  │         │  SignalR Hub 2   │
│ WebSocket   │────────▶│  (Instance B)    │
└─────────────┘         └──────────────────┘
```

### Alternatives Considered
1. **WebSockets directly** - Lower-level, less abstraction
2. **gRPC** - Efficient but requires special client libraries
3. **MQTT** - IoT-focused, overkill for this use case
4. **Selected**: SignalR + Redis backplane for Phase 2

---

## Proctoring Implementation Decision

### Decision: AI-based Violation Detection + Manual Review
**Status**: ✅ ADOPTED  
**Date**: Initial design

### Rationale
- **Scalable**: AI handles volume, humans handle edge cases
- **Accurate**: Reduces false positives vs. pure AI
- **Audit Trail**: Manual review creates accountability

### Violation Types Tracked
```csharp
public enum ViolationType
{
    TabSwitch,        // Student switched browser tabs
    FaceDetection,    // Face not detected
    AbnormalBehavior, // Unusual mouse/keyboard patterns
    CopyAttempt,      // Clipboard activity detected
    SuspiciousActivity // AI flagged suspicious patterns
}
```

### Current Gaps
- ❌ AI Analysis service is minimal
- ⚠️  No external AI API integration
- ❌ No ML model deployment strategy

### Phase 2 Enhancement
- Integrate AWS Rekognition or Google Cloud Vision for face detection
- Implement keystroke pattern analysis
- Add behavioral biometrics

### Alternatives Considered
1. **Manual Proctoring Only** - High cost, doesn't scale
2. **Full AI** - Faster but high false positive rate
3. **Selected**: Hybrid (AI + human review)

---

## Question Types Support Decision

### Decision: MCQ, Subjective, Code, Match-the-Following
**Status**: ✅ ADOPTED  
**Date**: Initial design

### Supported Types
```csharp
public enum QuestionType
{
    SingleChoice,          // MCQ - one correct answer
    MultipleChoice,        // MCQ - multiple correct answers
    Subjective,            // Long-form text answer
    CodeEvaluation,        // Code written and tested
    FillInTheBlanks,       // Blanks to fill
    MatchTheFollowing,     // Pair matching
    TrueOrFalse            // T/F
}
```

### Evaluation Strategy
```
Auto-evaluated:
- SingleChoice, MultipleChoice, TrueOrFalse
- CodeEvaluation (if test cases provided)

Manual evaluation:
- Subjective (by instructor)
- MatchTheFollowing (by instructor)
```

### Alternatives Considered
1. **Only MCQ** - Easier to scale, less learning assessment
2. **All types auto-evaluated** - Impossible for subjective
3. **Selected**: Mix of auto and manual

---

## Result Calculation Decision

### Decision: Weighted Scoring System
**Status**: ✅ ADOPTED  
**Date**: Initial design

### Calculation Formula
```
Total Score = Sum of (Question Marks × Correctness Percentage)

Example:
Q1 (3 marks, correct): 3 × 100% = 3 points
Q2 (2 marks, partial): 2 × 50% = 1 point
Q3 (2 marks, wrong): 2 × 0% = 0 points
────────────────────────────────────
Total: 4 out of 7 marks = 57%

Pass Criteria:
- Score ≥ PassingPercentage (exam-configured)
- Default: 40%
```

### Result Status
```csharp
if (percentage >= passingPercentage)
    result = "Pass";
else
    result = "Fail";
```

### Alternatives Considered
1. **Fixed scoring** - All or nothing per question (less nuanced)
2. **Negative marking** - Penalize wrong answers (additional complexity)
3. **Curve grading** - Class-based scaling (unfair for individual exams)
4. **Selected**: Weighted scoring (fairest and most flexible)

---

## Notification Strategy

### Decision: Event-driven with Template System
**Status**: ✅ ADOPTED  
**Date**: Initial design

### Notification Channels
1. **Email** - Exam results, announcements
2. **In-app** - Real-time via SignalR
3. **SMS** - Critical alerts (Phase 2)
4. **Push** - Mobile app (Phase 4)

### Template System
```
Email Templates:
- ExamScheduled
- ExamStarted
- ExamSubmitted
- ResultsAnnounced
- ExamCancelled
- ViolationDetected
```

### Current Implementation
- Email provider set to "Log" (console output only)
- No SMTP configuration
- Phase 1: Add SMTP Email provider (SendGrid, AWS SES, etc.)

### Alternatives Considered
1. **Hard-coded messages** - Inflexible, hard to maintain
2. **Template system** - Selected (current approach)

---

## Caching Strategy (Phase 2)

### Decision: Redis with Service-specific TTLs
**Status**: 🔄 PLANNED (Phase 2)

### Caching Layers
```
Layer 1: Application Memory (In-process)
- Rarely changed config: 1 hour TTL

Layer 2: Redis (Shared cache)
- Questions: 1 hour TTL (invalidate on update)
- Exams: 30 min TTL
- User preferences: 5 min TTL
- Active sessions: 24 hour TTL

Layer 3: MongoDB (Source of truth)
- Original data with full ACID guarantees
```

### Cache Invalidation Strategy
```
On Update:
1. Update MongoDB
2. Invalidate Redis key
3. Next read fetches from DB and caches

Example:
public async Task UpdateQuestionAsync(Question question)
{
    // Update DB
    await _mongoDb.Questions.ReplaceOneAsync(...);
    
    // Invalidate cache
    await _redis.StringSetAsync(
        $"question:{question.Id}", 
        null, 
        TimeSpan.Zero // Immediate invalidation
    );
}
```

### Alternatives Considered
1. **No caching** - Simpler, slower
2. **Client-side caching** - Works for read-heavy, complex invalidation
3. **CDN caching** - Good for static assets, not live data
4. **Selected**: Redis with TTL-based expiration

---

## API Versioning Decision (Phase 1)

### Decision: URL Path Versioning (v1, v2)
**Status**: 🔄 PLANNED (Phase 1)

### Routing Pattern
```csharp
// Current: /api/auth/login
// After: /api/v1/auth/login

[ApiController]
[Route("api/[controller]")]
public class WeatherForecastController : ControllerBase

// Change to:
[ApiController]
[Route("api/v{version}/[controller]")]
public class WeatherForecastController : ControllerBase
```

### Versioning Strategy
```
v1 - Current version (Weeks 0-8)
v2 - Major changes (Phase 2+) - kept in parallel with v1 for 2 major versions
```

### Alternatives Considered
1. **Header versioning** - Cleaner URLs, harder to test manually
2. **Query parameter** - Ugly, easy to miss
3. **Subdomain** - Complex DNS management
4. **Selected**: URL path (most RESTful and testable)

---

## Deployment Strategy Decision

### Decision: Containerized Microservices on Kubernetes (Phase 3)
**Status**: ✅ PLANNED

### Deployment Progression
```
Phase 1: Local Dev + IIS/Docker
Phase 2: Docker Compose + Single cloud VM
Phase 3: Kubernetes (GKE/AKS/EKS) with auto-scaling
Phase 4: Multi-region deployment
```

### Container Strategy
```
Per-service Dockerfile (best practice)
├── AuthService.Dockerfile
├── ExamService.Dockerfile
├── ...
└── docker-compose.yml (local dev)

Kubernetes manifests
├── deployments/ (scaling configurations)
├── services/ (networking)
├── configmaps/ (configs)
└── secrets/ (credentials)
```

### Alternatives Considered
1. **Single container** - Simpler but doesn't scale per-service
2. **Serverless** - Auto-scaling but cold starts affect real-time
3. **Traditional VMs** - Manual scaling, higher overhead
4. **Selected**: Kubernetes (industry standard for microservices)

---

## Summary of Key Decisions

| Decision | Status | Impact | Phase |
|----------|--------|--------|-------|
| Microservices | ✅ Adopted | High complexity, good scalability | 1 |
| MongoDB separate DBs | ✅ Adopted | Operational flexibility, data sync issues | 1 |
| JWT Auth | ✅ Adopted | Stateless, needs refresh logic | 1→2 |
| SignalR real-time | ✅ Adopted | Good feature set, needs Redis backplane | 1→2 |
| Event-driven notifications | ✅ Adopted | Scalable, template-based | 1 |
| Weighted scoring | ✅ Adopted | Fair but complex | 1 |
| Redis caching | 🔄 Planned | Huge perf boost, operational burden | 2 |
| Kubernetes deployment | 🔄 Planned | Enterprise-grade, learning curve | 3 |
| URL versioning | 🔄 Planned | Clean URLs, backward compatible | 1 |

---

## When to Revisit These Decisions

- **After Phase 1**: Performance testing reveals bottlenecks
- **After Phase 2**: Real-time features tested at 10k concurrent users
- **Quarterly**: Security review, emerging tech evaluation
- **On Major Incident**: Post-mortem analysis may warrant architectural changes
