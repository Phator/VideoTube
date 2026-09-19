using System.ComponentModel.DataAnnotations;

namespace VideoTube.Models
{
    public class FavoriteVideo
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = "";

        public ApplicationUser? User { get; set; }

        public int VideoId { get; set; }

        public Video? Video { get; set; }

        public DateTime CreatedDate { get; set; }
            = DateTime.UtcNow;
    }
}