using System.ComponentModel.DataAnnotations;

namespace VideoTube.Models
{
    public class Video
    {
        public int Id { get; set; }

        [Required]
        public string Title { get; set; } = "";

        public string Description { get; set; } = "";

        public string FileName { get; set; } = "";

        public DateTime UploadDate { get; set; }

        public int Views { get; set; }

        public string ThumbnailFileName { get; set; } = "";

        public int? CategoryId { get; set; }

        public Category? Category { get; set; }

        public string Duration { get; set; } = "";

        public string UploadedByUserId { get; set; } = "";
    }
}