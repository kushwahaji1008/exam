using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace VideoClassesService.Models
{
    // Hierarchical structure: Course → Chapter → Lesson (Video)
    
    public class Course
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string? ThumbnailUrl { get; set; }

        public string? InstructorId { get; set; }

        public string InstructorName { get; set; } = string.Empty;

        public CourseCategory Category { get; set; } = CourseCategory.General;

        public List<string> Tags { get; set; } = new();

        public CourseLevel Level { get; set; } = CourseLevel.Beginner;

        public List<string> ChapterIds { get; set; } = new();

        public CourseStatus Status { get; set; } = CourseStatus.Draft;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public int TotalStudentsEnrolled { get; set; } = 0;

        public double AverageRating { get; set; } = 0;

        public int TotalRatings { get; set; } = 0;

        public int TotalDurationMinutes { get; set; } = 0;

        public bool IsFeatured { get; set; } = false;

        public bool IsFree { get; set; } = true;

        public decimal? Price { get; set; }
    }

    public class Chapter
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string CourseId { get; set; } = string.Empty;

        [Required]
        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public int OrderIndex { get; set; } = 0;

        public List<string> LessonIds { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Lesson
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string ChapterId { get; set; } = string.Empty;

        [Required]
        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public int OrderIndex { get; set; } = 0;

        public LessonType Type { get; set; } = LessonType.Video;

        // Video specific
        public string? VideoUrl { get; set; }

        public string? VideoFileName { get; set; }

        public int DurationSeconds { get; set; } = 0;

        public string? ThumbnailUrl { get; set; }

        public List<VideoQuality> AvailableQualities { get; set; } = new();

        // Live class specific
        public DateTime? ScheduledStartTime { get; set; }

        public DateTime? ScheduledEndTime { get; set; }

        public string? LiveStreamUrl { get; set; }

        public bool IsLive { get; set; } = false;

        public string? RecordingUrl { get; set; }

        // Resources
        public List<LessonResource> Resources { get; set; } = new();

        public bool HasQuiz { get; set; } = false;

        public string? QuizId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int ViewCount { get; set; } = 0;

        public bool IsFree { get; set; } = false;
    }

    public class LessonResource
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public ResourceType Type { get; set; }
    }

    public class StudentProgress
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string StudentId { get; set; } = string.Empty;

        [Required]
        public string CourseId { get; set; } = string.Empty;

        public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;

        public Dictionary<string, LessonProgress> LessonProgress { get; set; } = new();

        public int TotalLessonsCompleted { get; set; } = 0;

        public int TotalLessons { get; set; } = 0;

        public double CompletionPercentage { get; set; } = 0;

        public int TotalWatchTimeMinutes { get; set; } = 0;

        public DateTime? LastAccessedAt { get; set; }

        public string? CurrentLessonId { get; set; }

        public bool IsCompleted { get; set; } = false;

        public DateTime? CompletedAt { get; set; }

        public int? Rating { get; set; }

        public string? Review { get; set; }
    }

    public class LessonProgress
    {
        public string LessonId { get; set; } = string.Empty;
        public int WatchedSeconds { get; set; } = 0;
        public int TotalSeconds { get; set; } = 0;
        public double ProgressPercentage { get; set; } = 0;
        public bool IsCompleted { get; set; } = false;
        public DateTime? CompletedAt { get; set; }
        public DateTime LastWatchedAt { get; set; } = DateTime.UtcNow;
        public List<Note> Notes { get; set; } = new();
        public List<int> Bookmarks { get; set; } = new(); // Timestamps in seconds
    }

    public class Note
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public int Timestamp { get; set; } // Seconds
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class VideoComment
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string LessonId { get; set; } = string.Empty;

        [Required]
        public string StudentId { get; set; } = string.Empty;

        public string StudentName { get; set; } = string.Empty;

        [Required]
        public string Content { get; set; } = string.Empty;

        public int? Timestamp { get; set; } // Optional timestamp for time-specific comments

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string? ParentCommentId { get; set; } // For replies

        public int Likes { get; set; } = 0;

        public List<string> LikedBy { get; set; } = new();
    }

    public class LiveClass
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string LessonId { get; set; } = string.Empty;

        [Required]
        public string InstructorId { get; set; } = string.Empty;

        public string InstructorName { get; set; } = string.Empty;

        public DateTime ScheduledStartTime { get; set; }

        public DateTime? ActualStartTime { get; set; }

        public DateTime? EndTime { get; set; }

        public LiveClassStatus Status { get; set; } = LiveClassStatus.Scheduled;

        public List<string> AttendeeIds { get; set; } = new();

        public int MaxAttendees { get; set; } = 100;

        public bool RecordSession { get; set; } = true;

        public string? RecordingUrl { get; set; }

        public bool EnableChat { get; set; } = true;

        public bool EnableQA { get; set; } = true;

        public List<ChatMessage> ChatMessages { get; set; } = new();

        public List<Poll> Polls { get; set; } = new();
    }

    public class ChatMessage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string SenderId { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public MessageType Type { get; set; } = MessageType.Text;
    }

    public class Poll
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Question { get; set; } = string.Empty;
        public List<PollOption> Options { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
    }

    public class PollOption
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Text { get; set; } = string.Empty;
        public int Votes { get; set; } = 0;
        public List<string> VotedBy { get; set; } = new();
    }

    public enum CourseCategory
    {
        General,
        Mathematics,
        Science,
        Programming,
        Engineering,
        Business,
        Arts,
        Language,
        TestPreparation,
        Other
    }

    public enum CourseLevel
    {
        Beginner,
        Intermediate,
        Advanced,
        Expert
    }

    public enum CourseStatus
    {
        Draft,
        Published,
        Archived
    }

    public enum LessonType
    {
        Video,
        LiveClass,
        Document,
        Quiz,
        Assignment
    }

    public enum ResourceType
    {
        PDF,
        Document,
        Presentation,
        Link,
        Code,
        Other
    }

    public enum VideoQuality
    {
        SD_360p,
        SD_480p,
        HD_720p,
        FullHD_1080p
    }

    public enum LiveClassStatus
    {
        Scheduled,
        Live,
        Completed,
        Cancelled
    }

    public enum MessageType
    {
        Text,
        RaisedHand,
        System,
        Poll
    }
}