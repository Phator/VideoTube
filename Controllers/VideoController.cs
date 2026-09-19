using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Diagnostics;
using System.Globalization;
using VideoTube.Data;
using VideoTube.Models;

namespace VideoTube.Controllers
{
    [Authorize]
    public class VideoController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly UserManager<ApplicationUser> _userManager;

        public VideoController(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _environment = environment;
            _userManager = userManager;
        }

        // ---------------------------------------------------------
        // FAVORITE (Legacy Add-Only)
        // ---------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> Favorite(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            bool exists = await _context.FavoriteVideos
                .AnyAsync(f => f.UserId == user.Id && f.VideoId == id);

            if (!exists)
            {
                _context.FavoriteVideos.Add(new FavoriteVideo
                {
                    UserId = user.Id,
                    VideoId = id
                });

                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Watch", new { id });
        }

        // ---------------------------------------------------------
        // VIDEO LISTING
        // ---------------------------------------------------------
        public IActionResult Index(string? search, string? category)
        {
            var videos = _context.Videos.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                videos = videos.Where(v =>
                    v.Title.Contains(search) ||
                    v.Description.Contains(search));
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                videos = videos.Where(v =>
                    v.Category != null &&
                    v.Category.Name == category);
            }

            ViewBag.PopularVideos = _context.Videos
                .OrderByDescending(v => v.Views)
                .Take(5)
                .ToList();

            ViewBag.RecentVideos = _context.Videos
                .OrderByDescending(v => v.UploadDate)
                .Take(5)
                .ToList();

            return View(videos
                .OrderByDescending(v => v.UploadDate)
                .ToList());
        }

        // ---------------------------------------------------------
        // UPLOAD VIDEO (GET)
        // ---------------------------------------------------------
        public IActionResult Upload()
        {
            ViewBag.Categories = _context.Categories
                .OrderBy(c => c.Name)
                .ToList();

            return View();
        }

        // ---------------------------------------------------------
        // UPLOAD VIDEO (POST)
        // ---------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> Upload(UploadVideoViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // --- Save Video File ---
            string videoFolder = Path.Combine(_environment.WebRootPath, "videos");
            Directory.CreateDirectory(videoFolder);

            string fileName = Guid.NewGuid() + Path.GetExtension(model.VideoFile.FileName);
            string filePath = Path.Combine(videoFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await model.VideoFile.CopyToAsync(stream);
            }

            // --- Generate Thumbnail ---
            string thumbnailFolder = Path.Combine(_environment.WebRootPath, "thumbnails");
            Directory.CreateDirectory(thumbnailFolder);

            string thumbnailFileName = Guid.NewGuid() + ".jpg";
            string thumbnailPath = Path.Combine(thumbnailFolder, thumbnailFileName);

            var process = new Process
            {
                StartInfo =
                {
                    FileName = @"C:\Users\andre\AppData\Local\Microsoft\WinGet\Links\ffmpeg.exe",
                    Arguments = $"-i \"{filePath}\" -ss 00:00:05 -vframes 1 \"{thumbnailPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit();

            // --- Get Duration ---
            string duration = "00:00";

            var probeProcess = new Process
            {
                StartInfo =
                {
                    FileName = @"C:\Users\andre\AppData\Local\Microsoft\WinGet\Links\ffprobe.exe",
                    Arguments = $"-v error -show_entries format=duration -of default:noprint_wrappers=1:nokey=1 \"{filePath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            probeProcess.Start();
            string output = probeProcess.StandardOutput.ReadToEnd();
            probeProcess.WaitForExit();

            if (double.TryParse(output.Trim(), CultureInfo.InvariantCulture, out double seconds))
            {
                duration = TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
            }

            // --- Save to DB ---
            var video = new Video
            {
                Title = model.Title,
                Description = model.Description,
                Duration = duration,
                CategoryId = model.CategoryId,
                FileName = fileName,
                ThumbnailFileName = thumbnailFileName,
                UploadDate = DateTime.Now,
                Views = 0
            };

            _context.Videos.Add(video);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        // ---------------------------------------------------------
        // WATCH VIDEO
        // ---------------------------------------------------------
        public async Task<IActionResult> Watch(int id)
        {
            var video = _context.Videos
                .Include(v => v.Category)
                .FirstOrDefault(v => v.Id == id);

            if (video == null)
                return NotFound();

            video!.Views++; // FIXED WARNING
            await _context.SaveChangesAsync();

            var user = await _userManager.GetUserAsync(User);

            ViewBag.IsFavorited = _context.FavoriteVideos
                .Any(f => f.UserId == user!.Id && f.VideoId == id); // FIXED WARNING

            ViewBag.RelatedVideos = _context.Videos
                .Include(v => v.Category)
                .Where(v => v.Id != video.Id && v.CategoryId == video.CategoryId)
                .OrderByDescending(v => v.Views)
                .Take(5)
                .ToList();

            return View(video);
        }

        // ---------------------------------------------------------
        // EDIT VIDEO
        // ---------------------------------------------------------
        public IActionResult Edit(int id)
        {
            var video = _context.Videos.FirstOrDefault(v => v.Id == id);
            if (video == null)
                return NotFound();

            ViewBag.Categories = _context.Categories
                .OrderBy(c => c.Name)
                .ToList();

            return View(video);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(Video model)
        {
            var video = _context.Videos.FirstOrDefault(v => v.Id == model.Id);
            if (video == null)
                return NotFound();

            video.Title = model.Title;
            video.Description = model.Description;
            video.CategoryId = model.CategoryId;

            await _context.SaveChangesAsync();
            return RedirectToAction("Index");
        }

        // ---------------------------------------------------------
        // DELETE VIDEO
        // ---------------------------------------------------------
        public IActionResult Delete(int id)
        {
            var video = _context.Videos.FirstOrDefault(v => v.Id == id);
            if (video == null)
                return NotFound();

            return View(video);
        }

        [HttpPost]
        [ActionName("Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var video = _context.Videos.FirstOrDefault(v => v.Id == id);
            if (video == null)
                return RedirectToAction("Index");

            string videoPath = Path.Combine(_environment.WebRootPath, "videos", video.FileName);
            if (System.IO.File.Exists(videoPath))
                System.IO.File.Delete(videoPath);

            if (!string.IsNullOrEmpty(video.ThumbnailFileName))
            {
                string thumbnailPath = Path.Combine(_environment.WebRootPath, "thumbnails", video.ThumbnailFileName);
                if (System.IO.File.Exists(thumbnailPath))
                    System.IO.File.Delete(thumbnailPath);
            }

            _context.Videos.Remove(video);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        // ---------------------------------------------------------
        // FAVORITES LIST
        // ---------------------------------------------------------
        public async Task<IActionResult> MyFavorites()
        {
            var user = await _userManager.GetUserAsync(User);

            var favorites = _context.FavoriteVideos
                .Where(f => f.UserId == user!.Id)
                .Select(f => f.Video!)
                .ToList();

            return View(favorites);
        }

        // ---------------------------------------------------------
        // FAVORITE TOGGLE (AJAX)
        // ---------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> ToggleFavorite([FromBody] FavoriteToggleRequest request)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            var fav = await _context.FavoriteVideos
                .FirstOrDefaultAsync(f => f.UserId == user.Id && f.VideoId == request.Id);

            bool nowFavorited;

            if (fav == null)
            {
                _context.FavoriteVideos.Add(new FavoriteVideo
                {
                    UserId = user.Id,
                    VideoId = request.Id
                });

                nowFavorited = true;
            }
            else
            {
                _context.FavoriteVideos.Remove(fav);
                nowFavorited = false;
            }

            await _context.SaveChangesAsync();

            return Json(new { favorited = nowFavorited });
        }

        public class FavoriteToggleRequest
        {
            public int Id { get; set; }
        }

    }
}
