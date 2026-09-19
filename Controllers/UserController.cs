using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using VideoTube.Models;
using VideoTube.Data;

namespace VideoTube.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UserController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public UserController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager)
        {
            _context = context; 
            _userManager = userManager;
            _roleManager = roleManager;
        }

        // ---------------------------------------------------------
        // USER LIST
        // ---------------------------------------------------------
        public IActionResult Index()
        {
            var users = _userManager.Users
                .OrderBy(u => u.Email)
                .ToList();

            return View(users);
        }

        // ---------------------------------------------------------
        // USER DETAILS
        // ---------------------------------------------------------
        public async Task<IActionResult> Details(string id)
        {
            var user =
                await _userManager.FindByIdAsync(id);

            if (user == null)
                return NotFound();

            ViewBag.Roles =
                await _userManager.GetRolesAsync(user);

            return View(user);
        }

        // ---------------------------------------------------------
        // MAKE ADMIN
        // ---------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> MakeAdmin(string id)
        {
            var user =
                await _userManager.FindByIdAsync(id);

            if (user == null)
                return NotFound();

            if (!await _roleManager.RoleExistsAsync("Admin"))
            {
                await _roleManager.CreateAsync(
                    new IdentityRole("Admin"));
            }

            if (!await _userManager.IsInRoleAsync(user, "Admin"))
            {
                await _userManager.AddToRoleAsync(
                    user,
                    "Admin");
            }

            return RedirectToAction(
                nameof(Details),
                new { id });
        }

        // ---------------------------------------------------------
        // REMOVE ADMIN
        // ---------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> RemoveAdmin(string id)
        {
            var user =
                await _userManager.FindByIdAsync(id);

            if (user == null)
                return NotFound();

            if (await _userManager.IsInRoleAsync(user, "Admin"))
            {
                await _userManager.RemoveFromRoleAsync(
                    user,
                    "Admin");
            }

            return RedirectToAction(
                nameof(Details),
                new { id });
        }

        // ---------------------------------------------------------
        // Disable User
        // ---------------------------------------------------------

        [HttpPost]
        public async Task<IActionResult> Disable(string id)
        {
            var user =
                await _userManager.FindByIdAsync(id);

            if (user == null)
                return NotFound();

            user.IsDisabled = true;

            await _userManager.UpdateAsync(user);

            return RedirectToAction(
                nameof(Details),
                new { id });
        }

        // ---------------------------------------------------------
        // Enable User
        // ---------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> Enable(string id)
        {
            var user =
                await _userManager.FindByIdAsync(id);

            if (user == null)
                return NotFound();

            user.IsDisabled = false;

            await _userManager.UpdateAsync(user);

            return RedirectToAction(
                nameof(Details),
                new { id });
        }

        // ---------------------------------------------------------
        // Reset Password
        // ---------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> ResetPassword(
    string id,
    string password)
        {
            var user =
                await _userManager.FindByIdAsync(id);

            if (user == null)
                return NotFound();

            var token =
                await _userManager
                    .GeneratePasswordResetTokenAsync(user);

            await _userManager
                .ResetPasswordAsync(
                    user,
                    token,
                    password);

            return RedirectToAction(
                nameof(Details),
                new { id });
        }

        // ---------------------------------------------------------
        // View User Favorites
        // ---------------------------------------------------------
        public IActionResult Favorites(string id)
        {
            var favorites =
                _context.FavoriteVideos
                    .Where(f => f.UserId == id)
                    .Select(f => f.Video!)
                    .ToList();

            return View(favorites);
        }

        // ---------------------------------------------------------
        // View User Uploads
        // ---------------------------------------------------------
        public IActionResult Uploads(string id)
        {
            var uploads =
                _context.Videos
                    .Where(v =>
                        v.UploadedByUserId == id)
                    .ToList();

            return View(uploads);
        }

    }
}