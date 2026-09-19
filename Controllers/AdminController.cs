using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using VideoTube.Data;
using VideoTube.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;

namespace VideoTube.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AdminController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IWebHostEnvironment env)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _env = env;
        }

        // ============================
        // DASHBOARD
        // ============================
        public IActionResult Dashboard()
        {
            var videos = _context.Videos
                .Include(v => v.Category)
                .ToList();

            var users = _context.Users.ToList();

            var model = new AdminDashboardViewModel();

            // Pending Approval
            model.PendingUsers = _userManager.Users
                .Where(u => u.IsDisabled)
                .OrderBy(u => u.Email)
                .ToList();

            // Storage
            string videoDir = Path.Combine(_env.WebRootPath, "videos");
            string thumbDir = Path.Combine(_env.WebRootPath, "thumbnails");

            long videoBytes = Directory.Exists(videoDir)
                ? Directory.GetFiles(videoDir).Sum(f => new FileInfo(f).Length)
                : 0;

            long thumbBytes = Directory.Exists(thumbDir)
                ? Directory.GetFiles(thumbDir).Sum(f => new FileInfo(f).Length)
                : 0;

            model.TotalVideos = videos.Count;
            model.TotalUsers = users.Count;
            model.TotalViews = videos.Sum(v => v.Views);

            double totalSeconds = 0;
            foreach (var v in videos)
            {
                if (!string.IsNullOrWhiteSpace(v.Duration) &&
                    TimeSpan.TryParse(v.Duration, out var ts))
                {
                    totalSeconds += ts.TotalSeconds;
                }
            }

            model.TotalDuration = totalSeconds;
            model.VideoStorageMB = videoBytes / (1024.0 * 1024.0);
            model.ThumbnailStorageMB = thumbBytes / (1024.0 * 1024.0);

            model.MostViewed = videos.OrderByDescending(v => v.Views).FirstOrDefault();
            model.NewestVideo = videos.OrderByDescending(v => v.UploadDate).FirstOrDefault();

            // Uploads per month
            var uploads = videos
                .GroupBy(v => v.UploadDate.ToString("MMM yyyy"))
                .OrderBy(g => DateTime.Parse("01 " + g.Key))
                .ToList();

            model.UploadLabels = uploads.Select(g => g.Key).ToList();
            model.UploadCounts = uploads.Select(g => g.Count()).ToList();

            // Category distribution
            var categories = _context.Categories
                .Select(c => new
                {
                    c.Name,
                    Count = _context.Videos.Count(v => v.CategoryId == c.Id)
                })
                .ToList();

            model.CategoryLabels = categories.Select(c => c.Name).ToList();
            model.CategoryCounts = categories.Select(c => c.Count).ToList();

            return View(model);
        }

        // ============================
        // MANAGE USERS
        // ============================
        public async Task<IActionResult> Users()
        {
            var users = _userManager.Users
                .OrderBy(u => u.Email)
                .ToList();

            var model = new List<UserWithRolesViewModel>();

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);

                model.Add(new UserWithRolesViewModel
                {
                    User = user,
                    Roles = roles.ToList()
                });
            }

            return View(model);
        }

        // ============================
        // USER DETAILS 
        // ============================
        public async Task<IActionResult> Details(string id)
        {
            if (id == null)
                return NotFound();

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return NotFound();

            var roles = await _userManager.GetRolesAsync(user);

            var model = new UserWithRolesViewModel
            {
                User = user,
                Roles = roles.ToList()
            };

            return View(model);
        }

        // ============================
        // ENABLE USER (APPROVE)
        // ============================
        [HttpPost]
        public async Task<IActionResult> Enable(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return NotFound();

            user.IsDisabled = false;
            await _userManager.UpdateAsync(user);

            return RedirectToAction("Dashboard");
        }

        // ============================
        // ASSIGN ROLE
        // ============================
        [HttpPost]
        public async Task<IActionResult> AssignRole(string id, string role)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return NotFound();

            var currentRoles = await _userManager.GetRolesAsync(user);

            await _userManager.RemoveFromRolesAsync(user, currentRoles);

            if (!await _roleManager.RoleExistsAsync(role))
                await _roleManager.CreateAsync(new IdentityRole(role));

            await _userManager.AddToRoleAsync(user, role);

            return RedirectToAction("Users");
        }

        // ============================
        // MANAGE ROLES
        // ============================
        public IActionResult Roles()
        {
            var roles = _roleManager.Roles
                .OrderBy(r => r.Name)
                .ToList();

            return View(roles);
        }
    }
}
