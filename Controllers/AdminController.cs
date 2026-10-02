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
        private readonly ActivityLogger _logger;
        private readonly ILogger<AdminController> _appLogger;

        public AdminController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IWebHostEnvironment env,
            ActivityLogger logger,
            ILogger<AdminController> appLogger)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _env = env;
            _logger = logger;
            _appLogger = appLogger;
        }

        // ============================
        // DASHBOARD
        // ============================
        public IActionResult Dashboard()
        {
            try
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

                _appLogger.LogInformation($"Admin dashboard loaded");
                return View(model);
            }
            catch (Exception ex)
            {
                _appLogger.LogError($"Error loading admin dashboard: {ex.Message}");
                TempData["Error"] = "An error occurred while loading the dashboard.";
                return RedirectToAction("Index", "Home");
            }
        }

        // ============================
        // MANAGE USERS
        // ============================
        public async Task<IActionResult> Users()
        {
            try
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

                _appLogger.LogInformation($"Admin viewed users list");
                return View(model);
            }
            catch (Exception ex)
            {
                _appLogger.LogError($"Error loading users: {ex.Message}");
                TempData["Error"] = "An error occurred while loading users.";
                return RedirectToAction("Dashboard");
            }
        }

        // ============================
        // USER DETAILS 
        // ============================
        public async Task<IActionResult> Details(string id)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    _appLogger.LogWarning("User Details accessed with null/empty ID");
                    return NotFound();
                }

                var user = await _userManager.FindByIdAsync(id);
                if (user == null)
                {
                    _appLogger.LogWarning($"User Details accessed for non-existent user: {id}");
                    return NotFound();
                }

                var roles = await _userManager.GetRolesAsync(user);

                var model = new UserWithRolesViewModel
                {
                    User = user,
                    Roles = roles.ToList()
                };

                _appLogger.LogInformation($"Admin viewed details for user: {id}");
                return View(model);
            }
            catch (Exception ex)
            {
                _appLogger.LogError($"Error loading user details for {id}: {ex.Message}");
                TempData["Error"] = "An error occurred while loading user details.";
                return RedirectToAction("Users");
            }
        }

        // ============================
        // ENABLE USER (APPROVE)
        // ============================
        [HttpPost]
        public async Task<IActionResult> Enable(string id)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    _appLogger.LogWarning("Enable user called with null/empty ID");
                    return NotFound();
                }

                var user = await _userManager.FindByIdAsync(id);
                if (user == null)
                {
                    _appLogger.LogWarning($"Enable user called for non-existent user: {id}");
                    return NotFound();
                }

                user.IsDisabled = false;
                var result = await _userManager.UpdateAsync(user);

                if (!result.Succeeded)
                {
                    _appLogger.LogError($"Failed to enable user {id}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                    TempData["Error"] = "An error occurred while enabling the user.";
                    return RedirectToAction("Details", new { id });
                }

                await _logger.LogAsync(user.Id, user.Email, "User Approved", "Admin approved user account");
                _appLogger.LogInformation($"Admin enabled user: {id}");

                TempData["Success"] = $"User {user.Email} has been approved.";
                return RedirectToAction("Dashboard");
            }
            catch (Exception ex)
            {
                _appLogger.LogError($"Error enabling user {id}: {ex.Message}");
                TempData["Error"] = "An error occurred while approving the user.";
                return RedirectToAction("Dashboard");
            }
        }

        // ============================
        // ASSIGN ROLE
        // ============================
        [HttpPost]
        public async Task<IActionResult> AssignRole(string id, string role)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(role))
                {
                    _appLogger.LogWarning($"AssignRole called with invalid parameters: id={id}, role={role}");
                    return BadRequest("User ID and role are required.");
                }

                var user = await _userManager.FindByIdAsync(id);
                if (user == null)
                {
                    _appLogger.LogWarning($"AssignRole called for non-existent user: {id}");
                    return NotFound();
                }

                var currentRoles = await _userManager.GetRolesAsync(user);

                var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!removeResult.Succeeded)
                {
                    _appLogger.LogError($"Failed to remove roles from user {id}: {string.Join(", ", removeResult.Errors.Select(e => e.Description))}");
                    TempData["Error"] = "An error occurred while updating roles.";
                    return RedirectToAction("Details", new { id });
                }

                if (!await _roleManager.RoleExistsAsync(role))
                {
                    var createRoleResult = await _roleManager.CreateAsync(new IdentityRole(role));
                    if (!createRoleResult.Succeeded)
                    {
                        _appLogger.LogError($"Failed to create role {role}: {string.Join(", ", createRoleResult.Errors.Select(e => e.Description))}");
                        TempData["Error"] = "An error occurred while creating the role.";
                        return RedirectToAction("Details", new { id });
                    }
                }

                var addResult = await _userManager.AddToRoleAsync(user, role);
                if (!addResult.Succeeded)
                {
                    _appLogger.LogError($"Failed to add role {role} to user {id}: {string.Join(", ", addResult.Errors.Select(e => e.Description))}");
                    TempData["Error"] = "An error occurred while assigning the role.";
                    return RedirectToAction("Details", new { id });
                }

                await _logger.LogAsync(user.Id, user.Email, "Role Changed", $"New Role: {role}");
                _appLogger.LogInformation($"Admin assigned role '{role}' to user: {id}");

                TempData["Success"] = $"Role {role} assigned to {user.Email}.";
                return RedirectToAction("Users");
            }
            catch (Exception ex)
            {
                _appLogger.LogError($"Error assigning role {role} to user {id}: {ex.Message}");
                TempData["Error"] = "An error occurred while assigning the role.";
                return RedirectToAction("Users");
            }
        }

        // ============================
        // MANAGE ROLES
        // ============================
        public IActionResult Roles()
        {
            try
            {
                var roles = _roleManager.Roles
                    .OrderBy(r => r.Name)
                    .ToList();

                _appLogger.LogInformation("Admin viewed roles list");
                return View(roles);
            }
            catch (Exception ex)
            {
                _appLogger.LogError($"Error loading roles: {ex.Message}");
                TempData["Error"] = "An error occurred while loading roles.";
                return RedirectToAction("Dashboard");
            }
        }

        // ============================
        // Add Activity Logs
        // ============================
        public IActionResult ActivityLogs()
        {
            try
            {
                var logs = _context.ActivityLogs
                    .OrderByDescending(l => l.Timestamp)
                    .Take(200)
                    .ToList();

                _appLogger.LogInformation("Admin viewed activity logs");
                return View(logs);
            }
            catch (Exception ex)
            {
                _appLogger.LogError($"Error loading activity logs: {ex.Message}");
                TempData["Error"] = "An error occurred while loading activity logs.";
                return RedirectToAction("Dashboard");
            }
        }
    }
}
