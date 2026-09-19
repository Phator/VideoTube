namespace VideoTube.Models
{
    public class WatchProgress
    {
        public int Id { get; set; }

        public string UserId { get; set; } = "";

        public ApplicationUser? User { get; set; }

        public int VideoId { get; set; }

        public Video? Video { get; set; }

        public int CurrentSeconds { get; set; }

        public DateTime LastUpdated { get; set; }
            = DateTime.Now;
    }
}