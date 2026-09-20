using Microsoft.AspNetCore.Mvc;
using VideoTube.Data;
using VideoTube.Models;
using Microsoft.AspNetCore.Authorization;

namespace VideoTube.Controllers
{
    [Authorize]
    public class CategoryController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CategoryController(ApplicationDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            var categories = _context.Categories
                .Select(c => new CategoryWithCountViewModel
                {
                    Id = c.Id,
                    Name = c.Name,
                    VideoCount = _context.Videos.Count(v => v.CategoryId == c.Id)
                })
                .ToList();

            return View(categories);
        }

        [HttpPost]
        public async Task<IActionResult> Create(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                var category = new Category
                {
                    Name = name.Trim()
                };

                _context.Categories.Add(category);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Delete(int id)
        {
            var category = _context.Categories
                .FirstOrDefault(c => c.Id == id);

            if (category == null)
                return RedirectToAction("Index");

            int videoCount = _context.Videos
                .Count(v => v.CategoryId == id);

            if (videoCount > 0)
            {
                TempData["Error"] =
                    $"Cannot delete category '{category.Name}' because it is assigned to {videoCount} video(s).";

                return RedirectToAction("Index");
            }

            _context.Categories.Remove(category);

            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }
    }
}