namespace VideoTube.Models
{
    public class ActivityLog
    {
        public int Id { get; set; }

        public string UserId { get; set; }
        public string Email { get; set; }

        public string Action { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public string? Details { get; set; }
    }
}
