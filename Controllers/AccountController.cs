using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using VideoTube.Models;

namespace VideoTube.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ActivityLogger _logger;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleManager<IdentityRole> roleManager,
            ActivityLogger logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _logger = logger;
        }

        // ---------------------------------------------------------
        // INITIAL SETUP — FIRST USER BECOMES ADMIN
        // ---------------------------------------------------------
        [AllowAnonymous]
        public IActionResult Setup()
        {
            bool hasUsers = _userManager.Users.Any();

            if (hasUsers)
                return RedirectToAction("Login");

            return RedirectToAction("Register");
        }

        // ---------------------------------------------------------
        // REGISTER (GET)
        // ---------------------------------------------------------
        [AllowAnonymous]
        public IActionResult Register()
        {
            return View();
        }

        // ---------------------------------------------------------
        // REGISTER (POST)
        // ---------------------------------------------------------
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                IsDisabled = true  // NEW: all new accounts disabled until approved
            };

            bool firstUser = !_userManager.Users.Any();

            var result = await _userManager.CreateAsync(user, model.Password);

            if (result.Succeeded)
            {
                await _logger.LogAsync(user.Id, user.Email, "User Registered");

                // Assign default role
                await _userManager.AddToRoleAsync(user, "User");

                // First user becomes Admin
                if (firstUser)
                {
                    if (!await _roleManager.RoleExistsAsync("Admin"))
                        await _roleManager.CreateAsync(new IdentityRole("Admin"));

                    await _userManager.AddToRoleAsync(user, "Admin");
                }

                TempData["PendingApproval"] = true;
                return RedirectToAction("Register");
            }

            foreach (var error in result.Errors)
                ModelState.AddModelError("", error.Description);

            return View(model);
        }

        // ---------------------------------------------------------
        // LOGIN (GET)
        // ---------------------------------------------------------
        [AllowAnonymous]
        public IActionResult Login()
        {
            return View();
        }

        // ---------------------------------------------------------
        // LOGIN (POST)
        // ---------------------------------------------------------
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.FindByEmailAsync(model.Email);

            if (user?.IsDisabled == true)
            {
                ModelState.AddModelError("", "This account has been disabled.");
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(
                model.Email,
                model.Password,
                false,
                false);

            if (result.Succeeded)
            {
                await _logger.LogAsync(user.Id, user.Email, "User Logged In");
                return RedirectToAction("Index", "Video");
            }

            ModelState.AddModelError("", "Invalid login attempt.");
            return View(model);
        }

        // ---------------------------------------------------------
        // LOGOUT
        // ---------------------------------------------------------
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Video");
        }
    }
}
