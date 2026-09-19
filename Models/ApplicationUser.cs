using Microsoft.AspNetCore.Identity;

namespace VideoTube.Models
{
    public class ApplicationUser : IdentityUser
    {
        public DateTime CreatedDate { get; set; }
            = DateTime.UtcNow;

        public bool IsDisabled { get; set; }
    }
}