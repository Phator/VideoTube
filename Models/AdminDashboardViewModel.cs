using VideoTube.Models;

namespace VideoTube.Models
{
    public class AdminDashboardViewModel
    {
        public int TotalVideos { get; set; }
        public int TotalUsers { get; set; }
        public int TotalViews { get; set; }
        public double TotalDuration { get; set; }
        public double VideoStorageMB { get; set; }
        public double ThumbnailStorageMB { get; set; }

        public Video? MostViewed { get; set; }
        public Video? NewestVideo { get; set; }

        public List<string> UploadLabels { get; set; } = new();
        public List<int> UploadCounts { get; set; } = new();

        public List<string> CategoryLabels { get; set; } = new();
        public List<int> CategoryCounts { get; set; } = new();

        public List<ApplicationUser> PendingUsers { get; set; } = new();
    }
}
