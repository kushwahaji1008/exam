using MongoDB.Driver;
using VideoClassesService.Models;

namespace VideoClassesService.Services
{
    public class CommentService
    {
        private readonly MongoDbService _mongoDb;

        public CommentService(MongoDbService mongoDb)
        {
            _mongoDb = mongoDb;
        }

        public async Task<VideoComment> AddCommentAsync(VideoComment comment)
        {
            comment.CreatedAt = DateTime.UtcNow;
            await _mongoDb.Comments.InsertOneAsync(comment);
            return comment;
        }

        public async Task<List<VideoComment>> GetLessonCommentsAsync(string lessonId)
        {
            return await _mongoDb.Comments
                .Find(c => c.LessonId == lessonId && c.ParentCommentId == null)
                .SortByDescending(c => c.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<VideoComment>> GetCommentRepliesAsync(string parentCommentId)
        {
            return await _mongoDb.Comments
                .Find(c => c.ParentCommentId == parentCommentId)
                .SortBy(c => c.CreatedAt)
                .ToListAsync();
        }

        public async Task<bool> LikeCommentAsync(string commentId, string studentId)
        {
            // FIX: Use atomic MongoDB operators to avoid race conditions and prevent likes going negative.
            // Try to add like (only if not already liked)
            var addResult = await _mongoDb.Comments.UpdateOneAsync(
                c => c.Id == commentId && !c.LikedBy.Contains(studentId),
                Builders<VideoComment>.Update
                    .AddToSet(c => c.LikedBy, studentId)
                    .Inc(c => c.Likes, 1));

            if (addResult.ModifiedCount > 0)
                return true; // Like added successfully

            // User already liked — remove like (unlike)
            var removeResult = await _mongoDb.Comments.UpdateOneAsync(
                c => c.Id == commentId && c.LikedBy.Contains(studentId),
                Builders<VideoComment>.Update
                    .Pull(c => c.LikedBy, studentId)
                    .Inc(c => c.Likes, -1));

            // Safety net: ensure Likes never drops below 0 (defensive guard)
            await _mongoDb.Comments.UpdateOneAsync(
                c => c.Id == commentId && c.Likes < 0,
                Builders<VideoComment>.Update.Set(c => c.Likes, 0));

            return removeResult.ModifiedCount > 0 || removeResult.MatchedCount > 0;
        }

        public async Task<VideoComment?> GetCommentByIdAsync(string commentId) =>
            await _mongoDb.Comments.Find(c => c.Id == commentId).FirstOrDefaultAsync();

        public async Task<bool> DeleteCommentAsync(string commentId)
        {
            var result = await _mongoDb.Comments.DeleteOneAsync(c => c.Id == commentId);
            return result.DeletedCount > 0;
        }
    }
}