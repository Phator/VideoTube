using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VideoTube.Models;

namespace VideoTube.Data
{
    public class ApplicationDbContext
        : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Video> Videos => Set<Video>();

        public DbSet<Category> Categories => Set<Category>();

        public DbSet<FavoriteVideo> FavoriteVideos
            => Set<FavoriteVideo>();

        public DbSet<WatchHistory> WatchHistory
            => Set<WatchHistory>();

        public DbSet<WatchProgress> WatchProgress
            => Set<WatchProgress>();

        public DbSet<ActivityLog> ActivityLogs { get; set; }

        public DbSet<Tag> Tags { get; set; }
        public DbSet<VideoTag> VideoTags { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<VideoTag>()
                .HasKey(vt => new { vt.VideoId, vt.TagId });
        }
    }
}