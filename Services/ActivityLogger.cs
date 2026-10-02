using VideoTube.Data;
using VideoTube.Models;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

public class ActivityLogger
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ActivityLogger> _logger;

    public ActivityLogger(ApplicationDbContext context, ILogger<ActivityLogger> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Logs a user activity asynchronously.
    /// </summary>
    /// <param name="userId">The ID of the user performing the action (required)</param>
    /// <param name="email">The email of the user performing the action</param>
    /// <param name="action">The action being performed (required)</param>
    /// <param name="details">Additional details about the action</param>
    /// <returns>True if logging was successful, false otherwise</returns>
    public async Task<bool> LogAsync(string userId, string? email, string action, string? details = null)
    {
        try
        {
            // Validate required parameters
            if (string.IsNullOrWhiteSpace(userId))
            {
                _logger.LogWarning("Activity log failed: UserId is required");
                return false;
            }

            if (string.IsNullOrWhiteSpace(action))
            {
                _logger.LogWarning($"Activity log failed for user {userId}: Action is required");
                return false;
            }

            // Sanitize inputs
            userId = userId.Trim();
            email = email?.Trim() ?? "unknown";
            action = action.Trim();
            details = details?.Trim();

            // Create the log entry
            var log = new ActivityLog
            {
                UserId = userId,
                Email = email,
                Action = action,
                Details = details,
                Timestamp = DateTime.UtcNow
            };

            _context.ActivityLogs.Add(log);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Activity logged: User={userId}, Action={action}");
            return true;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError($"Database error while logging activity for user {userId}: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error logging activity for user {userId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets the count of activities for a specific user.
    /// </summary>
    public async Task<int> GetUserActivityCountAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return 0;

        try
        {
            return await _context.ActivityLogs
                .CountAsync(l => l.UserId == userId.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting activity count for user {userId}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Gets recent activities for a specific user.
    /// </summary>
    public async Task<List<ActivityLog>> GetUserActivitiesAsync(string userId, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return new List<ActivityLog>();

        try
        {
            return await _context.ActivityLogs
                .Where(l => l.UserId == userId.Trim())
                .OrderByDescending(l => l.Timestamp)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting activities for user {userId}: {ex.Message}");
            return new List<ActivityLog>();
        }
    }
}

