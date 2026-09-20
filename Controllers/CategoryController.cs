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
                    VideoCount = _context.Videos.Count(v => v.CategoryId == c.Id),

                    ThumbnailFileName = _context.Videos
                        .Where(v => v.CategoryId == c.Id)
                        .OrderByDescending(v => v.UploadDate)
                        .Select(v => v.ThumbnailFileName)
                        .FirstOrDefault()
                })
                .ToList();

            return View(categories);
        }

        public IActionResult Details(int id)
        {
            var category = _context.Categories.FirstOrDefault(c => c.Id == id);
            if (category == null)
                return NotFound();

            var videos = _context.Videos
                .Where(v => v.CategoryId == id)
                .OrderByDescending(v => v.UploadDate)
                .ToList();

            var vm = new CategoryDetailsViewModel
            {
                Id = category.Id,
                Name = category.Name,
                Description = category.Description,
                Videos = videos
            };

            return View(vm);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(Category model)
        {
            if (!ModelState.IsValid)
                return View(model);

            _context.Categories.Add(model);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        public IActionResult Edit(int id)
        {
            var category = _context.Categories.FirstOrDefault(c => c.Id == id);
            if (category == null)
                return NotFound();

            return View(category);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(Category model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var category = _context.Categories.FirstOrDefault(c => c.Id == model.Id);
            if (category == null)
                return NotFound();

            category.Name = model.Name;
            category.Description = model.Description;

            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Delete(int id)
        {
            var category = _context.Categories.FirstOrDefault(c => c.Id == id);
            if (category == null)
                return RedirectToAction("Index");

            int videoCount = _context.Videos.Count(v => v.CategoryId == id);

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
