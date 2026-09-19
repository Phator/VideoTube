using VideoTube.Data;
using VideoTube.Models;

public class ActivityLogger
{
    private readonly ApplicationDbContext _context;

    public ActivityLogger(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task LogAsync(string userId, string email, string action, string? details = null)
    {
        var log = new ActivityLog
        {
            UserId = userId,
            Email = email,
            Action = action,
            Details = details
        };

        _context.ActivityLogs.Add(log);
        await _context.SaveChangesAsync();
    }
}
