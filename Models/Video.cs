using System.ComponentModel.DataAnnotations;

namespace VideoTube.Models
{
    public class Video
    {
        public int Id { get; set; }

        [Required]
        public string Title { get; set; } = "";

        public string Description { get; set; } = "";

        public string Duration { get; set; } = "00:00";

        public int? CategoryId { get; set; }
        public Category? Category { get; set; }

        public string FileName { get; set; } = "";
        public string ThumbnailFileName { get; set; } = "";

        public DateTime UploadDate { get; set; } = DateTime.Now;

        public int Views { get; set; } = 0;

        public string UploadedByUserId { get; set; } = "";
        public ApplicationUser? UploadedByUser { get; set; }

        public ICollection<VideoTag> VideoTags { get; set; } = new List<VideoTag>();
    }
}
