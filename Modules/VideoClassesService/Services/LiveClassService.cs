using MongoDB.Driver;
using VideoClassesService.Models;

namespace VideoClassesService.Services
{
    public class LiveClassService
    {
        private readonly MongoDbService _mongoDb;
        private readonly ILogger<LiveClassService> _logger;

        public LiveClassService(MongoDbService mongoDb, ILogger<LiveClassService> logger)
        {
            _mongoDb = mongoDb;
            _logger = logger;
        }

        public async Task<LiveClass> CreateLiveClassAsync(LiveClass liveClass)
        {
            await _mongoDb.LiveClasses.InsertOneAsync(liveClass);

            // Update lesson
            var update = Builders<Lesson>.Update
                .Set(l => l.ScheduledStartTime, liveClass.ScheduledStartTime)
                .Set(l => l.ScheduledEndTime, liveClass.ScheduledStartTime.AddMinutes(60)); // Default 1 hour

            await _mongoDb.Lessons.UpdateOneAsync(l => l.Id == liveClass.LessonId, update);

            return liveClass;
        }

        public async Task<LiveClass?> GetLiveClassAsync(string liveClassId)
        {
            return await _mongoDb.LiveClasses.Find(l => l.Id == liveClassId).FirstOrDefaultAsync();
        }

        public async Task<LiveClass?> GetLiveClassByLessonAsync(string lessonId)
        {
            return await _mongoDb.LiveClasses.Find(l => l.LessonId == lessonId).FirstOrDefaultAsync();
        }

        public async Task<List<LiveClass>> GetUpcomingLiveClassesAsync()
        {
            var now = DateTime.UtcNow;
            return await _mongoDb.LiveClasses
                .Find(l => l.Status == LiveClassStatus.Scheduled && l.ScheduledStartTime > now)
                .SortBy(l => l.ScheduledStartTime)
                .ToListAsync();
        }

        public async Task<List<LiveClass>> GetActiveLiveClassesAsync()
        {
            return await _mongoDb.LiveClasses
                .Find(l => l.Status == LiveClassStatus.Live)
                .ToListAsync();
        }

        public async Task<bool> StartLiveClassAsync(string liveClassId)
        {
            var update = Builders<LiveClass>.Update
                .Set(l => l.Status, LiveClassStatus.Live)
                .Set(l => l.ActualStartTime, DateTime.UtcNow);

            var result = await _mongoDb.LiveClasses.UpdateOneAsync(l => l.Id == liveClassId, update);

            if (result.ModifiedCount > 0)
            {
                var liveClass = await GetLiveClassAsync(liveClassId);
                if (liveClass != null)
                {
                    var lessonUpdate = Builders<Lesson>.Update.Set(l => l.IsLive, true);
                    await _mongoDb.Lessons.UpdateOneAsync(l => l.Id == liveClass.LessonId, lessonUpdate);
                }
            }

            return result.ModifiedCount > 0;
        }

        public async Task<bool> EndLiveClassAsync(string liveClassId, string? recordingUrl)
        {
            var update = Builders<LiveClass>.Update
                .Set(l => l.Status, LiveClassStatus.Completed)
                .Set(l => l.EndTime, DateTime.UtcNow)
                .Set(l => l.RecordingUrl, recordingUrl);

            var result = await _mongoDb.LiveClasses.UpdateOneAsync(l => l.Id == liveClassId, update);

            if (result.ModifiedCount > 0)
            {
                var liveClass = await GetLiveClassAsync(liveClassId);
                if (liveClass != null)
                {
                    var lessonUpdate = Builders<Lesson>.Update
                        .Set(l => l.IsLive, false)
                        .Set(l => l.RecordingUrl, recordingUrl);
                    await _mongoDb.Lessons.UpdateOneAsync(l => l.Id == liveClass.LessonId, lessonUpdate);
                }
            }

            return result.ModifiedCount > 0;
        }

        public async Task<bool> JoinLiveClassAsync(string liveClassId, string studentId)
        {
            var liveClass = await GetLiveClassAsync(liveClassId);
            if (liveClass == null) return false;

            if (liveClass.AttendeeIds.Count >= liveClass.MaxAttendees)
            {
                return false; // Full
            }

            if (liveClass.AttendeeIds.Contains(studentId))
            {
                return true; // Already joined
            }

            var update = Builders<LiveClass>.Update.Push(l => l.AttendeeIds, studentId);
            var result = await _mongoDb.LiveClasses.UpdateOneAsync(l => l.Id == liveClassId, update);

            return result.ModifiedCount > 0;
        }

        public async Task<bool> AddChatMessageAsync(string liveClassId, ChatMessage message)
        {
            var update = Builders<LiveClass>.Update.Push(l => l.ChatMessages, message);
            var result = await _mongoDb.LiveClasses.UpdateOneAsync(l => l.Id == liveClassId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> CreatePollAsync(string liveClassId, Poll poll)
        {
            var update = Builders<LiveClass>.Update.Push(l => l.Polls, poll);
            var result = await _mongoDb.LiveClasses.UpdateOneAsync(l => l.Id == liveClassId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> VotePollAsync(string liveClassId, string pollId, string optionId, string studentId)
        {
            var liveClass = await GetLiveClassAsync(liveClassId);
            if (liveClass == null) return false;

            var poll = liveClass.Polls.FirstOrDefault(p => p.Id == pollId);
            if (poll == null || !poll.IsActive) return false;

            var option = poll.Options.FirstOrDefault(o => o.Id == optionId);
            if (option == null) return false;

            // Check if already voted
            if (poll.Options.Any(o => o.VotedBy.Contains(studentId)))
            {
                // Remove previous vote
                foreach (var opt in poll.Options)
                {
                    if (opt.VotedBy.Contains(studentId))
                    {
                        opt.VotedBy.Remove(studentId);
                        opt.Votes--;
                    }
                }
            }

            option.VotedBy.Add(studentId);
            option.Votes++;

            var update = Builders<LiveClass>.Update.Set(l => l.Polls, liveClass.Polls);
            var result = await _mongoDb.LiveClasses.UpdateOneAsync(l => l.Id == liveClassId, update);

            return result.ModifiedCount > 0;
        }
    }
}