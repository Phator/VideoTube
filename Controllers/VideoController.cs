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

            var user = await _userManager.GetUserAsync(User);

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
                    Arguments = $"-i \"{filePath}\" -vf \"select='gt(scene,0.4)'\" -vframes 1 \"{thumbnailPath}\"",
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

            // --- Save to DB FIRST (fixes your build error) ---
            var video = new Video
            {
                Title = model.Title,
                Description = model.Description,
                Duration = duration,
                CategoryId = model.CategoryId,
                FileName = fileName,
                ThumbnailFileName = thumbnailFileName,
                UploadDate = DateTime.Now,
                Views = 0,
                UploadedByUserId = user!.Id
            };

            _context.Videos.Add(video);
            await _context.SaveChangesAsync();

            // --- Add Tags ---
            if (!string.IsNullOrWhiteSpace(model.Tags))
            {
                var tagNames = model.Tags
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().ToLower())
                    .Distinct()
                    .ToList();

                foreach (var tagName in tagNames)
                {
                    var tag = await _context.Tags
                        .FirstOrDefaultAsync(t => t.Name == tagName);

                    if (tag == null)
                    {
                        tag = new Tag { Name = tagName };
                        _context.Tags.Add(tag);
                        await _context.SaveChangesAsync();
                    }

                    _context.VideoTags.Add(new VideoTag
                    {
                        VideoId = video.Id,
                        TagId = tag.Id
                    });
                }

                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Index");
        }

        // ---------------------------------------------------------
        // WATCH VIDEO
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

            var user = await _userManager.GetUserAsync(User);

            ViewBag.Progress = 0;
            ViewBag.IsFavorited = false;

            if (user != null)
            {
                ViewBag.Progress = await _context.WatchProgress
                    .Where(p => p.UserId == user.Id && p.VideoId == id)
                    .Select(p => p.CurrentSeconds)
                    .FirstOrDefaultAsync();

                ViewBag.IsFavorited = await _context.FavoriteVideos
                    .AnyAsync(f => f.UserId == user.Id && f.VideoId == id);
            }

            ViewBag.RelatedVideos = await _context.Videos
                .Include(v => v.Category)
                .Where(v => v.Id != video.Id)
                .OrderByDescending(v => v.Views)
                .Take(5)
                .ToListAsync();

            return View(video);
        }




        // ---------------------------------------------------------
        // EDIT VIDEO
        // ---------------------------------------------------------
        public IActionResult Edit(int id)
        {
            var video = _context.Videos
                .Include(v => v.VideoTags)
                    .ThenInclude(vt => vt.Tag)
                .FirstOrDefault(v => v.Id == id);

            if (video == null)
                return NotFound();

            ViewBag.Categories = _context.Categories
                .OrderBy(c => c.Name)
                .ToList();

            // Convert tags to comma-separated string
            ViewBag.Tags = string.Join(", ", video.VideoTags.Select(vt => vt.Tag.Name));

            return View(video);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(Video model, string tags)
        {
            var video = _context.Videos.FirstOrDefault(v => v.Id == model.Id);
            if (video == null)
                return NotFound();

            video.Title = model.Title;
            video.Description = model.Description;
            video.CategoryId = model.CategoryId;

            // Remove old tags
            var oldTags = _context.VideoTags.Where(vt => vt.VideoId == video.Id);
            _context.VideoTags.RemoveRange(oldTags);
            await _context.SaveChangesAsync();

            // Add new tags
            if (!string.IsNullOrWhiteSpace(tags))
            {
                var tagNames = tags
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().ToLower())
                    .Distinct()
                    .ToList();

                foreach (var tagName in tagNames)
                {
                    var tag = await _context.Tags
                        .FirstOrDefaultAsync(t => t.Name == tagName);

                    if (tag == null)
                    {
                        tag = new Tag { Name = tagName };
                        _context.Tags.Add(tag);
                        await _context.SaveChangesAsync();
                    }

                    _context.VideoTags.Add(new VideoTag
                    {
                        VideoId = video.Id,
                        TagId = tag.Id
                    });
                }
            }

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

        // ---------------------------------------------------------
        // HISTORY
        // ---------------------------------------------------------
        public async Task<IActionResult> History()
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
                return RedirectToAction("Login", "Account");

            var history =
                _context.WatchHistory
                    .Where(h => h.UserId == user.Id)
                    .OrderByDescending(h => h.WatchedDate)
                    .Select(h => h.Video!)
                    .Distinct()
                    .ToList();

            return View(history);
        }

        // ---------------------------------------------------------
        // WATCH PROGRESS
        // ---------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> SaveProgress([FromBody] SaveProgressRequest request)
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
                return Unauthorized();

            var progress =
                _context.WatchProgress
                    .FirstOrDefault(p =>
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
            progress.LastUpdated = DateTime.Now;

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
            var user = await _userManager.GetUserAsync(User);

            var progress =
                _context.WatchProgress
                    .Include(p => p.Video)
                    .Where(p => p.UserId == user!.Id)
                    .OrderByDescending(p => p.LastUpdated)
                    .ToList();

            return View(progress);
        }

        // ---------------------------------------------------------
        // EDIT THUMBNAIL (GET)
        // ---------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> EditThumbnail(int id)
        {
            var video = await _context.Videos.FindAsync(id);
            if (video == null) return NotFound();

            var vm = new EditThumbnailViewModel
            {
                VideoId = id,
                CurrentThumbnail = video.ThumbnailFileName
            };

            return View(vm);
        }

        // ---------------------------------------------------------
        // EDIT THUMBNAIL (POST)
        // ---------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> EditThumbnail(EditThumbnailViewModel model)
        {
            var video = await _context.Videos.FindAsync(model.VideoId);
            if (video == null) return NotFound();

            string videoPath = Path.Combine(_environment.WebRootPath, "videos", video.FileName);
            string thumbnailFolder = Path.Combine(_environment.WebRootPath, "thumbnails");
            Directory.CreateDirectory(thumbnailFolder);

            string newThumb = Guid.NewGuid() + ".jpg";
            string newThumbPath = Path.Combine(thumbnailFolder, newThumb);

            // Get video duration
            var probe = new Process
            {
                StartInfo =
                {
                    FileName = @"C:\Users\andre\AppData\Local\Microsoft\WinGet\Links\ffprobe.exe",
                    Arguments = $"-v error -show_entries format=duration -of default:noprint_wrappers=1:nokey=1 \"{videoPath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            probe.Start();
            string durationOutput = probe.StandardOutput.ReadToEnd();
            probe.WaitForExit();

            double.TryParse(durationOutput.Trim(), CultureInfo.InvariantCulture, out double videoSeconds);

            // Custom thumbnail upload
            if (model.CustomThumbnail != null)
            {
                using var stream = new FileStream(newThumbPath, FileMode.Create);
                await model.CustomThumbnail.CopyToAsync(stream);
            }
            else if (model.TimestampSeconds.HasValue)
            {
                int requestedSeconds = model.TimestampSeconds.Value;

                if (requestedSeconds > videoSeconds)
                    requestedSeconds = (int)videoSeconds - 1;

                if (requestedSeconds < 0)
                    requestedSeconds = 0;

                TimeSpan ts = TimeSpan.FromSeconds(requestedSeconds);
                string timestamp = ts.ToString(@"hh\:mm\:ss");

                var process = new Process
                {
                    StartInfo =
                    {
                        FileName = @"C:\Users\andre\AppData\Local\Microsoft\WinGet\Links\ffmpeg.exe",
                        Arguments = $"-i \"{videoPath}\" -ss {timestamp} -vframes 1 \"{newThumbPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit();
            }
            else
            {
                var process = new Process
                {
                    StartInfo =
                    {
                        FileName = @"C:\Users\andre\AppData\Local\Microsoft\WinGet\Links\ffmpeg.exe",
                        Arguments = $"-i \"{videoPath}\" -vf \"select='gt(scene,0.4)'\" -vframes 1 \"{newThumbPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit();
            }

            video.ThumbnailFileName = newThumb;
            await _context.SaveChangesAsync();

            return RedirectToAction("Watch", new { id = video.Id });
        }

        // ---------------------------------------------------------
        // REBUILD ALL VIDEO DURATIONS
        // ---------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> RebuildDurations()
        {
            var videos = _context.Videos.ToList();

            foreach (var video in videos)
            {
                string videoPath = Path.Combine(_environment.WebRootPath, "videos", video.FileName);

                if (!System.IO.File.Exists(videoPath))
                    continue;

                var probeProcess = new Process
                {
                    StartInfo =
                    {
                        FileName = @"C:\Users\andre\AppData\Local\Microsoft\WinGet\Links\ffprobe.exe",
                        Arguments = $"-v error -show_entries format=duration -of default:noprint_wrappers=1:nokey=1 \"{videoPath}\"",
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
                    video.Duration = TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        // ---------------------------------------------------------
        // TAG SEARCH
        // ---------------------------------------------------------
        public async Task<IActionResult> Tag(string name)
        {
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
