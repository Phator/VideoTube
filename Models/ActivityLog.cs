using System.ComponentModel.DataAnnotations;

namespace VideoTube.Models
{
    public class ActivityLog
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = "";

        [Required]
        public string Email { get; set; } = "";

        [Required]
        public string Action { get; set; } = "";

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public string? Details { get; set; }
    }
}
