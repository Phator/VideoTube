using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoTube.Data;
using VideoTube.Models;

namespace VideoTube.Controllers
{
    [Authorize]
    public class VideoController : Controller
    {
        private const string VideosFolder = "videos";
        private const string ThumbnailsFolder = "thumbnails";

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

        private async Task<ApplicationUser?> GetCurrentUserAsync()
        {
            return await _userManager.GetUserAsync(User);
        }

        private async Task LoadCategoriesAsync()
        {
            ViewBag.Categories = await _context.Categories
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        private static void DeleteFileIfExists(string path)
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }

        // ---------------------------------------------------------
        // FAVORITE
        // ---------------------------------------------------------

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Favorite(int id)
        {
            var user = await GetCurrentUserAsync();

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

            return RedirectToAction(nameof(Watch), new { id });
        }

        // ---------------------------------------------------------
        // INDEX
        // ---------------------------------------------------------

        public async Task<IActionResult> Index(string? search, string? category)
        {
            IQueryable<Video> videos = _context.Videos
                .Include(v => v.Category);

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

            ViewBag.PopularVideos = await _context.Videos
                .OrderByDescending(v => v.Views)
                .Take(5)
                .ToListAsync();

            ViewBag.RecentVideos = await _context.Videos
                .OrderByDescending(v => v.UploadDate)
                .Take(5)
                .ToListAsync();

            var list = await videos
                .OrderByDescending(v => v.UploadDate)
                .ToListAsync();

            return View(list);
        }

        // ---------------------------------------------------------
        // UPLOAD GET
        // ---------------------------------------------------------

        public async Task<IActionResult> Upload()
        {
            await LoadCategoriesAsync();
            return View();
        }

        // ---------------------------------------------------------
        // UPLOAD POST
        // ---------------------------------------------------------

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(UploadVideoViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await LoadCategoriesAsync();
                return View(model);
            }

            var user = await GetCurrentUserAsync();

            string videoFolder =
                Path.Combine(_environment.WebRootPath, VideosFolder);

            Directory.CreateDirectory(videoFolder);

            string fileName =
                $"{Guid.NewGuid()}{Path.GetExtension(model.VideoFile.FileName)}";

            string filePath =
                Path.Combine(videoFolder, fileName);

            await using (var stream =
                new FileStream(filePath, FileMode.Create))
            {
                await model.VideoFile.CopyToAsync(stream);
            }

            string thumbnailFolder =
                Path.Combine(_environment.WebRootPath, ThumbnailsFolder);

            Directory.CreateDirectory(thumbnailFolder);

            string thumbnailFileName = $"{Guid.NewGuid()}.jpg";

            string thumbnailPath =
                Path.Combine(thumbnailFolder, thumbnailFileName);

            try
            {
                using var process = new Process
                {
                    StartInfo =
                    {
                        FileName = "ffmpeg",
                        Arguments =
                            $"-i \"{filePath}\" -vf \"select='gt(scene,0.4)'\" -vframes 1 \"{thumbnailPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit();
            }
            catch
            {
                thumbnailFileName = string.Empty;
            }

            string duration = "00:00";

            try
            {
                using var probeProcess = new Process
                {
                    StartInfo =
                    {
                        FileName = "ffprobe",
                        Arguments =
                            $"-v error -show_entries format=duration -of default:noprint_wrappers=1:nokey=1 \"{filePath}\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                probeProcess.Start();

                string output =
                    probeProcess.StandardOutput.ReadToEnd();

                probeProcess.WaitForExit();

                if (double.TryParse(
                    output.Trim(),
                    CultureInfo.InvariantCulture,
                    out double seconds))
                {
                    duration = TimeSpan
                        .FromSeconds(seconds)
                        .ToString(@"hh\:mm\:ss");
                }
            }
            catch
            {
            }

            var video = new Video
            {
                Title = model.Title,
                Description = model.Description,
                Duration = duration,
                CategoryId = model.CategoryId == 0
                    ? null
                    : model.CategoryId,
                FileName = fileName,
                ThumbnailFileName = thumbnailFileName,
                UploadDate = DateTime.UtcNow,
                Views = 0,
                UploadedByUserId = user?.Id ?? string.Empty
            };

            _context.Videos.Add(video);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // ---------------------------------------------------------
        // WATCH
        // ---------------------------------------------------------

        public async Task<IActionResult> Watch(int id)
        {
            var video = await _context.Videos
                .Include(v => v.Category)
                .FirstOrDefaultAsync(v => v.Id == id);

            if (video == null)
                return NotFound();

            video.Views++;
            await _context.SaveChangesAsync();

            var user = await GetCurrentUserAsync();

            ViewBag.Progress = 0;
            ViewBag.IsFavorited = false;

            if (user != null)
            {
                ViewBag.Progress = await _context.WatchProgress
                    .Where(p =>
                        p.UserId == user.Id &&
                        p.VideoId == id)
                    .Select(p => p.CurrentSeconds)
                    .FirstOrDefaultAsync();

                ViewBag.IsFavorited = await _context.FavoriteVideos
                    .AnyAsync(f =>
                        f.UserId == user.Id &&
                        f.VideoId == id);
            }

            ViewBag.RelatedVideos = await _context.Videos
                .Include(v => v.Category)
                .Where(v => v.Id != id)
                .OrderByDescending(v => v.Views)
                .Take(5)
                .ToListAsync();

            return View(video);
        }

        // ---------------------------------------------------------
        // EDIT GET
        // ---------------------------------------------------------

        public async Task<IActionResult> Edit(int id)
        {
            var video = await _context.Videos
                .FirstOrDefaultAsync(v => v.Id == id);

            if (video == null)
                return NotFound();

            await LoadCategoriesAsync();

            return View(video);
        }

        // ---------------------------------------------------------
        // EDIT POST
        // ---------------------------------------------------------

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Video model)
        {
            var video = await _context.Videos
                .FirstOrDefaultAsync(v => v.Id == model.Id);

            if (video == null)
                return NotFound();

            video.Title = model.Title;
            video.Description = model.Description;
            video.CategoryId =
                model.CategoryId == 0
                    ? null
                    : model.CategoryId;

            await _context.SaveChangesAsync();

            return RedirectToAction(
                nameof(Watch),
                new { id = video.Id });
        }

        // ---------------------------------------------------------
        // DELETE GET
        // ---------------------------------------------------------

        public async Task<IActionResult> Delete(int id)
        {
            var video = await _context.Videos
                .FirstOrDefaultAsync(v => v.Id == id);

            if (video == null)
                return NotFound();

            return View(video);
        }

        // ---------------------------------------------------------
        // DELETE POST
        // ---------------------------------------------------------

        [HttpPost]
        [ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var video = await _context.Videos
                .FirstOrDefaultAsync(v => v.Id == id);

            if (video == null)
            {
                return RedirectToAction(nameof(Index));
            }

            DeleteFileIfExists(
                Path.Combine(
                    _environment.WebRootPath,
                    VideosFolder,
                    video.FileName));

            if (!string.IsNullOrWhiteSpace(
                video.ThumbnailFileName))
            {
                DeleteFileIfExists(
                    Path.Combine(
                        _environment.WebRootPath,
                        ThumbnailsFolder,
                        video.ThumbnailFileName));
            }

            _context.Videos.Remove(video);

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // ---------------------------------------------------------
        // FAVORITES
        // ---------------------------------------------------------

        public async Task<IActionResult> MyFavorites()
        {
            var user = await GetCurrentUserAsync();

            if (user == null)
                return RedirectToAction("Login", "Account");

            var favorites = await _context.FavoriteVideos
                .Where(f => f.UserId == user.Id)
                .Select(f => f.Video!)
                .ToListAsync();

            return View(favorites);
        }

        // ---------------------------------------------------------
        // SAVE PROGRESS
        // ---------------------------------------------------------

        [HttpPost]
        public async Task<IActionResult> SaveProgress(
            [FromBody] SaveProgressRequest request)
        {
            var user = await GetCurrentUserAsync();

            if (user == null)
                return Unauthorized();

            var progress = await _context.WatchProgress
                .FirstOrDefaultAsync(p =>
                    p.UserId == user.Id &&
                    p.VideoId == request.VideoId);

            if (progress == null)
            {
                progress = new WatchProgress
                {
                    UserId = user.Id,
                    VideoId = request.VideoId
                };

                _context.WatchProgress.Add(progress);
            }

            progress.CurrentSeconds = request.CurrentSeconds;
            progress.LastUpdated = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok();
        }

        public class SaveProgressRequest
        {
            public int VideoId { get; set; }
            public int CurrentSeconds { get; set; }
        }

        // ---------------------------------------------------------
        // CONTINUE WATCHING
        // ---------------------------------------------------------

        public async Task<IActionResult> ContinueWatching()
        {
            var user = await GetCurrentUserAsync();

            if (user == null)
                return RedirectToAction("Login", "Account");

            var progress = await _context.WatchProgress
                .Include(p => p.Video)
                .Where(p => p.UserId == user.Id)
                .OrderByDescending(p => p.LastUpdated)
                .ToListAsync();

            return View(progress);
        }

        // ---------------------------------------------------------
        // STREAM VIDEO
        // ---------------------------------------------------------

        [AllowAnonymous]
        public IActionResult StreamVideo(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequest("fileName required");

            string path = Path.Combine(
                _environment.WebRootPath,
                VideosFolder,
                fileName);

            if (!System.IO.File.Exists(path))
                return NotFound();

            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            return File(
                stream,
                "video/mp4",
                enableRangeProcessing: true);
        }

        // ---------------------------------------------------------
        // DEBUG VIDEO FILE
        // ---------------------------------------------------------

        [AllowAnonymous]
        public IActionResult DebugVideoFile(int id)
        {
            var video = _context.Videos
                .FirstOrDefault(v => v.Id == id);

            if (video == null)
                return Content("Video not found");

            string path = Path.Combine(
                _environment.WebRootPath,
                VideosFolder,
                video.FileName);

            bool exists = System.IO.File.Exists(path);

            long size = exists
                ? new FileInfo(path).Length
                : 0;

            return Content(
                $"File exists: {exists}\n" +
                $"Size: {size}\n" +
                $"DB FileName: {video.FileName}\n" +
                $"Full Path: {path}");
        }

        // ---------------------------------------------------------
        // Tags
        // ---------------------------------------------------------
        public IActionResult Tags()
        {
            return RedirectToAction("All", "Tag");
        }

        public async Task<IActionResult> All()
        {
            var tags = await _context.Tags
                .OrderBy(t => t.Name)
                .ToListAsync();

            return View(tags);
        }

    }
}