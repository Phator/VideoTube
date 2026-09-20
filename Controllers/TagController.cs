using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoTube.Data;
using VideoTube.Models;

namespace VideoTube.Controllers
{
    public class TagController : Controller
    {
        private readonly ApplicationDbContext _context;

        public TagController(ApplicationDbContext context)
        {
            _context = context;
        }

        // /Tag/Name?name=Something
        public async Task<IActionResult> Name(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return RedirectToAction("Index", "Video");

            var videos = await _context.VideoTags
                .Where(vt => vt.Tag.Name == name)
                .Select(vt => vt.Video)
                .Include(v => v.Category)
                .ToListAsync();

            ViewBag.TagName = name;

            return View(videos);
        }
    }
}
