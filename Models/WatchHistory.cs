using System.ComponentModel.DataAnnotations;

namespace VideoTube.Models
{
    public class WatchHistory
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = "";

        public ApplicationUser? User { get; set; }

        [Required]
        public int VideoId { get; set; }

        public Video? Video { get; set; }

        public DateTime WatchedDate { get; set; }
            = DateTime.UtcNow;
    }
}