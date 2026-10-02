namespace VideoTube.Models
{
    public class UserWithRolesViewModel
    {
        public ApplicationUser User { get; set; } = null!;
        public List<string> Roles { get; set; } = new();
    }
}
