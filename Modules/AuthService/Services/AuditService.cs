using MongoDB.Driver;
using MongoDB.Bson;
using AuthService.Models;

namespace AuthService.Services
{
    public class AuditService
    {
        private readonly MongoDbService _mongoDb;

        public AuditService(MongoDbService mongoDb)
        {
            _mongoDb = mongoDb;
        }

        // ==========================================
        // 1. AUDIT LOGS
        // ==========================================

        public async Task<object> GetAllAuditLogsAsync(AuditFilterRequest filter)
        {
            var page = Math.Max(1, filter.Page);
            var pageSize = Math.Clamp(filter.PageSize, 1, 100);

            var builder = Builders<AuditLog>.Filter;
            var mongoFilter = builder.Empty;

            if (!string.IsNullOrEmpty(filter.UserId))
                mongoFilter &= builder.Eq(a => a.UserId, filter.UserId);

            if (!string.IsNullOrEmpty(filter.ActionType))
                mongoFilter &= builder.Regex(a => a.ActionType, new BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(filter.ActionType), "i"));

            if (filter.StartDate.HasValue)
                mongoFilter &= builder.Gte(a => a.Timestamp, filter.StartDate.Value);

            if (filter.EndDate.HasValue)
                mongoFilter &= builder.Lte(a => a.Timestamp, filter.EndDate.Value);

            var totalRecords = await _mongoDb.AuditLogs.CountDocumentsAsync(mongoFilter);
            
            var logs = await _mongoDb.AuditLogs.Find(mongoFilter)
                .SortByDescending(a => a.Timestamp)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();

            return new
            {
                TotalRecords = totalRecords,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalRecords / (double)pageSize),
                Data = logs
            };
        }

        public async Task<AuditLog?> GetAuditLogByIdAsync(string auditId)
        {
            return await _mongoDb.AuditLogs.Find(a => a.Id == auditId).FirstOrDefaultAsync();
        }

        public async Task<object?> GetUserAuditLogsAsync(string userId, AuditFilterRequest filter)
        {
            // Force the filter to only apply to the requested user
            filter.UserId = userId;
            return await GetAllAuditLogsAsync(filter);
        }

        // Helper method to write an Audit Log (Call this from other services like UserService)
        public async Task LogAuditAsync(string userId, string actionType, string details, string? ipAddress = null, string? userAgent = null)
        {
            var log = new AuditLog
            {
                UserId = userId,
                ActionType = actionType,
                Details = details,
                IpAddress = ipAddress,
                UserAgent = userAgent
            };
            await _mongoDb.AuditLogs.InsertOneAsync(log);
        }

        // ==========================================
        // 2. SECURITY EVENTS
        // ==========================================

        public async Task<object> GetAllSecurityEventsAsync(SecurityEventFilterRequest filter)
        {
            var page = Math.Max(1, filter.Page);
            var pageSize = Math.Clamp(filter.PageSize, 1, 100);

            var builder = Builders<SecurityEvent>.Filter;
            var mongoFilter = builder.Empty;

            if (!string.IsNullOrEmpty(filter.UserId))
                mongoFilter &= builder.Eq(s => s.UserId, filter.UserId);

            if (!string.IsNullOrEmpty(filter.ActionType)) // Mapping ActionType to EventType
                mongoFilter &= builder.Regex(s => s.EventType, new BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(filter.ActionType), "i"));

            if (!string.IsNullOrEmpty(filter.EventLevel))
                mongoFilter &= builder.Eq(s => s.EventLevel, filter.EventLevel);

            if (filter.StartDate.HasValue)
                mongoFilter &= builder.Gte(s => s.Timestamp, filter.StartDate.Value);

            if (filter.EndDate.HasValue)
                mongoFilter &= builder.Lte(s => s.Timestamp, filter.EndDate.Value);

            var totalRecords = await _mongoDb.SecurityEvents.CountDocumentsAsync(mongoFilter);
            
            var events = await _mongoDb.SecurityEvents.Find(mongoFilter)
                .SortByDescending(s => s.Timestamp)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();

            return new
            {
                TotalRecords = totalRecords,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalRecords / (double)pageSize),
                Data = events
            };
        }

        public async Task<SecurityEvent?> GetSecurityEventByIdAsync(string eventId)
        {
            return await _mongoDb.SecurityEvents.Find(s => s.Id == eventId).FirstOrDefaultAsync();
        }

        // Helper method to write a Security Event (Call this during failed logins, password changes, etc.)
        public async Task LogSecurityEventAsync(string userId, string eventType, string eventLevel, string details, string? ipAddress = null)
        {
            var securityEvent = new SecurityEvent
            {
                UserId = userId,
                EventType = eventType,
                EventLevel = eventLevel,
                Details = details,
                IpAddress = ipAddress
            };
            await _mongoDb.SecurityEvents.InsertOneAsync(securityEvent);
        }
    }
}