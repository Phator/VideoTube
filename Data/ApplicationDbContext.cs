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

        public DbSet<FavoriteVideo> FavoriteVideos => Set<FavoriteVideo>();
    }
}