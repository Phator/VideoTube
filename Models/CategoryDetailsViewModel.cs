using VideoTube.Models;

namespace VideoTube.Models
{
    public class CategoryDetailsViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        public List<Video> Videos { get; set; } = new();
    }
}