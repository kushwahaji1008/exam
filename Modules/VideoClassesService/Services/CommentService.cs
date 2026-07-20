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
            var comment = await _mongoDb.Comments.Find(c => c.Id == commentId).FirstOrDefaultAsync();
            if (comment == null) return false;

            if (comment.LikedBy.Contains(studentId))
            {
                // Unlike
                comment.LikedBy.Remove(studentId);
                comment.Likes--;
            }
            else
            {
                // Like
                comment.LikedBy.Add(studentId);
                comment.Likes++;
            }

            var update = Builders<VideoComment>.Update
                .Set(c => c.Likes, comment.Likes)
                .Set(c => c.LikedBy, comment.LikedBy);

            var result = await _mongoDb.Comments.UpdateOneAsync(c => c.Id == commentId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> DeleteCommentAsync(string commentId)
        {
            var result = await _mongoDb.Comments.DeleteOneAsync(c => c.Id == commentId);
            return result.DeletedCount > 0;
        }
    }
}