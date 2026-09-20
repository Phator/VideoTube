using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using VideoTube.Models;
using VideoTube.ViewModels;
using Microsoft.AspNetCore.Authorization;

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

        [AllowAnonymous]
        public IActionResult Setup()
        {
            bool hasUsers = _userManager.Users.Any();

            if (hasUsers)
                return RedirectToAction("Login");

            return RedirectToAction("Register");
        }

        [AllowAnonymous]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                IsDisabled = true
            };

            bool firstUser = !_userManager.Users.Any();

            var result = await _userManager.CreateAsync(user, model.Password);

            if (result.Succeeded)
            {
                // Log registration
                await _logger.LogAsync(user.Id, user.Email!, "User Registered");

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

        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
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
                await _logger.LogAsync(user!.Id, user.Email!, "User Logged In");
                return RedirectToAction("Index", "Video");
            }

            ModelState.AddModelError("", "Invalid login attempt.");
            return View(model);
        }

        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Video");
        }
    }
}
