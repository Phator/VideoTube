namespace VideoTube.Models
{
    public class CategoryWithCountViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int VideoCount { get; set; }

        // REQUIRED for thumbnails
        public string? ThumbnailFileName { get; set; }
    }
}