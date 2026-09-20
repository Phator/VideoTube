using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoTube.Data;
using VideoTube.Models;
using VideoTube.ViewModels;

namespace VideoTube.Controllers
{
    [Authorize]
    public class VideoController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<VideoController> _logger;

        public VideoController(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            UserManager<ApplicationUser> userManager,
            ILogger<VideoController> logger)
        {
            _context = context;
            _environment = environment;
            _userManager = userManager;
            _logger = logger;
        }

        // ---------------------------------------------------------
        // FAVORITE (Add-only legacy)
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
        // INDEX / LISTING
        // ---------------------------------------------------------
        public async Task<IActionResult> Index(string? search, string? category)
        {
            var videos = _context.Videos
                .Include(v => v.Category)
                .AsQueryable();

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
        // UPLOAD (GET)
        // ---------------------------------------------------------
        public IActionResult Upload()
        {
            ViewBag.Categories = _context.Categories
                .OrderBy(c => c.Name)
                .ToList();

            return View();
        }

        // ---------------------------------------------------------
        // UPLOAD (POST)
        // ---------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> Upload(UploadVideoViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Categories = _context.Categories
                    .OrderBy(c => c.Name)
                    .ToList();

                return View(model);
            }

            var user = await _userManager.GetUserAsync(User);

            // --- Save Video File ---
            string videoFolder = Path.Combine(_environment.WebRootPath, "videos");
            Directory.CreateDirectory(videoFolder);

            string extension = Path.GetExtension(model.VideoFile.FileName) ?? "";
            string fileName = Guid.NewGuid().ToString("N") + extension;
            string filePath = Path.Combine(videoFolder, fileName);

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await model.VideoFile.CopyToAsync(stream);
                }

                // Temporary debug: ensure file exists after save
                if (!System.IO.File.Exists(filePath))
                {
                    _logger.LogError("Upload saved DB row but file not found at {FilePath}", filePath);
                    throw new Exception($"Upload failed, file not found at {filePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving uploaded video file to {FilePath}", filePath);
                ModelState.AddModelError("", "Failed to save uploaded file.");
                ViewBag.Categories = _context.Categories.OrderBy(c => c.Name).ToList();
                return View(model);
            }

            // --- Generate Thumbnail (best-effort) ---
            string thumbnailFolder = Path.Combine(_environment.WebRootPath, "thumbnails");
            Directory.CreateDirectory(thumbnailFolder);

            string thumbnailFileName = "";
            string thumbnailPath = "";

            try
            {
                thumbnailFileName = Guid.NewGuid().ToString("N") + ".jpg";
                thumbnailPath = Path.Combine(thumbnailFolder, thumbnailFileName);

                // Attempt to run ffmpeg if available; swallow errors
                var process = new Process
                {
                    StartInfo =
                    {
                        FileName = @"ffmpeg", // assume ffmpeg is on PATH in production; adjust if needed
                        Arguments = $"-i \"{filePath}\" -vf \"select='gt(scene,0.4)'\" -vframes 1 \"{thumbnailPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit();

                if (!System.IO.File.Exists(thumbnailPath))
                {
                    thumbnailFileName = "";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Thumbnail generation failed for {FilePath}", filePath);
                thumbnailFileName = "";
            }

            // --- Get Duration (best-effort) ---
            string duration = "00:00:00";
            try
            {
                var probe = new Process
                {
                    StartInfo =
                    {
                        FileName = @"ffprobe",
                        Arguments = $"-v error -show_entries format=duration -of default:noprint_wrappers=1:nokey=1 \"{filePath}\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                probe.Start();
                string output = probe.StandardOutput.ReadToEnd();
                probe.WaitForExit();

                if (double.TryParse(output.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double seconds))
                {
                    duration = TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ffprobe failed for {FilePath}", filePath);
            }

            // --- Save DB record ---
            var video = new Video
            {
                Title = model.Title,
                Description = model.Description,
                Duration = duration,
                CategoryId = model.CategoryId == 0 ? null : model.CategoryId,
                FileName = fileName,
                ThumbnailFileName = thumbnailFileName,
                UploadDate = DateTime.Now,
                Views = 0,
                UploadedByUserId = user?.Id ?? ""
            };

            _context.Videos.Add(video);
            await _context.SaveChangesAsync();

            // --- Tags (safe) ---
            if (!string.IsNullOrWhiteSpace(model.Tags))
            {
                var tagNames = model.Tags
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().ToLowerInvariant())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct()
                    .ToList();

                foreach (var tagName in tagNames)
                {
                    var tag = await _context.Tags.FirstOrDefaultAsync(t => t.Name == tagName);
                    if (tag == null)
                    {
                        tag = new Tag { Name = tagName };
                        _context.Tags.Add(tag);
                        await _context.SaveChangesAsync();
                    }

                    bool vtExists = await _context.VideoTags
                        .AnyAsync(vt => vt.VideoId == video.Id && vt.TagId == tag.Id);

                    if (!vtExists)
                    {
                        _context.VideoTags.Add(new VideoTag
                        {
                            VideoId = video.Id,
                            TagId = tag.Id
                        });
                    }
                }

                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Index");
        }

        // ---------------------------------------------------------
        // WATCH
        // ---------------------------------------------------------
        public async Task<IActionResult> Watch(int id)
        {
            var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            try
            {
                var video = await _context.Videos
                    .Include(v => v.Category)
                    .Include(v => v.VideoTags)
                        .ThenInclude(vt => vt.Tag)
                    .FirstOrDefaultAsync(v => v.Id == id);

                if (video == null)
                    return NotFound();

                // Defensive check: ensure FileName exists
                if (string.IsNullOrWhiteSpace(video.FileName))
                {
                    _logger.LogWarning("Video {VideoId} has empty FileName. RequestId: {RequestId}", id, requestId);
                    ViewBag.RequestId = requestId;
                    return View("Error");
                }

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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in Watch action for VideoId {VideoId}. RequestId: {RequestId}", id, requestId);
                ViewBag.RequestId = requestId;
                return View("Error");
            }
        }

        // ---------------------------------------------------------
        // EDIT (GET)
        // ---------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> Edit(Video model)
        {
            var video = await _context.Videos.FirstOrDefaultAsync(v => v.Id == model.Id);
            if (video == null)
                return NotFound();

            video.Title = model.Title;
            video.Description = model.Description;
            video.CategoryId = model.CategoryId == 0 ? null : model.CategoryId;

            await _context.SaveChangesAsync();

            return RedirectToAction("Watch", new { id = video.Id });
        }


        // ---------------------------------------------------------
        // EDIT (POST)
        // ---------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> Edit(Video model, string tags)
        {
            var video = await _context.Videos.FirstOrDefaultAsync(v => v.Id == model.Id);
            if (video == null)
                return NotFound();

            video.Title = model.Title;
            video.Description = model.Description;
            video.CategoryId = model.CategoryId == 0 ? null : model.CategoryId;

            // Remove old tags safely
            var oldTags = _context.VideoTags.Where(vt => vt.VideoId == video.Id);
            _context.VideoTags.RemoveRange(oldTags);
            await _context.SaveChangesAsync();

            // Add new tags safely
            if (!string.IsNullOrWhiteSpace(tags))
            {
                var tagNames = tags
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().ToLowerInvariant())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct()
                    .ToList();

                foreach (var tagName in tagNames)
                {
                    var tag = await _context.Tags.FirstOrDefaultAsync(t => t.Name == tagName);

                    if (tag == null)
                    {
                        tag = new Tag { Name = tagName };
                        _context.Tags.Add(tag);
                        await _context.SaveChangesAsync();
                    }

                    bool vtExists = await _context.VideoTags
                        .AnyAsync(vt => vt.VideoId == video.Id && vt.TagId == tag.Id);

                    if (!vtExists)
                    {
                        _context.VideoTags.Add(new VideoTag
                        {
                            VideoId = video.Id,
                            TagId = tag.Id
                        });
                    }
                }

                await _context.SaveChangesAsync();
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Watch", new { id = video.Id });
        }

        // ---------------------------------------------------------
        // DELETE (GET)
        // ---------------------------------------------------------
        public async Task<IActionResult> Delete(int id)
        {
            var video = await _context.Videos.FirstOrDefaultAsync(v => v.Id == id);
            if (video == null)
                return NotFound();

            return View(video);
        }

        // ---------------------------------------------------------
        // DELETE (POST)
        // ---------------------------------------------------------
        [HttpPost, ActionName("Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var video = await _context.Videos.FirstOrDefaultAsync(v => v.Id == id);
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

            var relatedTags = _context.VideoTags.Where(vt => vt.VideoId == video.Id);
            _context.VideoTags.RemoveRange(relatedTags);

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

            if (user == null)
                return RedirectToAction("Login", "Account");

            var favorites = await _context.FavoriteVideos
                .Where(f => f.UserId == user.Id)
                .Select(f => f.Video!)
                .ToListAsync();

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

            var history = await _context.WatchHistory
                .Where(h => h.UserId == user.Id)
                .OrderByDescending(h => h.WatchedDate)
                .Select(h => h.Video!)
                .Distinct()
                .ToListAsync();

            return View(history);
        }

        // ---------------------------------------------------------
        // SAVE PROGRESS
        // ---------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> SaveProgress([FromBody] SaveProgressRequest request)
        {
            var user = await _userManager.GetUserAsync(User);

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

            string newThumb = Guid.NewGuid().ToString("N") + ".jpg";
            string newThumbPath = Path.Combine(thumbnailFolder, newThumb);

            double.TryParse("", out double videoSeconds);

            try
            {
                var probe = new Process
                {
                    StartInfo =
                    {
                        FileName = @"ffprobe",
                        Arguments = $"-v error -show_entries format=duration -of default:noprint_wrappers=1:nokey=1 \"{videoPath}\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                probe.Start();
                string durationOutput = probe.StandardOutput.ReadToEnd();
                probe.WaitForExit();

                double.TryParse(durationOutput.Trim(), CultureInfo.InvariantCulture, out videoSeconds);
            }
            catch
            {
                // ignore probe errors
            }

            if (model.CustomThumbnail != null)
            {
                using var stream = new FileStream(newThumbPath, FileMode.Create);
                await model.CustomThumbnail.CopyToAsync(stream);
            }
            else if (model.TimestampSeconds.HasValue)
            {
                int requestedSeconds = model.TimestampSeconds.Value;

                if (requestedSeconds > videoSeconds)
                    requestedSeconds = (int)Math.Max(0, videoSeconds - 1);

                if (requestedSeconds < 0)
                    requestedSeconds = 0;

                TimeSpan ts = TimeSpan.FromSeconds(requestedSeconds);
                string timestamp = ts.ToString(@"hh\:mm\:ss");

                try
                {
                    var process = new Process
                    {
                        StartInfo =
                        {
                            FileName = @"ffmpeg",
                            Arguments = $"-i \"{videoPath}\" -ss {timestamp} -vframes 1 \"{newThumbPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    process.WaitForExit();
                }
                catch
                {
                    // ignore ffmpeg errors
                }
            }
            else
            {
                try
                {
                    var process = new Process
                    {
                        StartInfo =
                        {
                            FileName = @"ffmpeg",
                            Arguments = $"-i \"{videoPath}\" -vf \"select='gt(scene,0.4)'\" -vframes 1 \"{newThumbPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    process.WaitForExit();
                }
                catch
                {
                    // ignore ffmpeg errors
                }
            }

            video.ThumbnailFileName = newThumb;
            await _context.SaveChangesAsync();

            return RedirectToAction("Watch", new { id = video.Id });
        }

        // ---------------------------------------------------------
        // REBUILD DURATIONS
        // ---------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> RebuildDurations()
        {
            var videos = await _context.Videos.ToListAsync();

            foreach (var video in videos)
            {
                string videoPath = Path.Combine(_environment.WebRootPath, "videos", video.FileName);

                if (!System.IO.File.Exists(videoPath))
                    continue;

                try
                {
                    var probeProcess = new Process
                    {
                        StartInfo =
                        {
                            FileName = @"ffprobe",
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
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ffprobe failed while rebuilding duration for {VideoPath}", videoPath);
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        // ---------------------------------------------------------
        // TAG SEARCH
        // ---------------------------------------------------------
        [AllowAnonymous]
        public async Task<IActionResult> Tag(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return RedirectToAction("Index");

            var videos = await _context.Videos
                .Where(v => v.VideoTags.Any(vt => vt.Tag != null && vt.Tag.Name == name))
                .Include(v => v.Category)
                .ToListAsync();

            ViewBag.TagName = name;

            return View(videos);
        }

        // ---------------------------------------------------------
        // LIST ALL TAGS
        // ---------------------------------------------------------
        [AllowAnonymous]
        public async Task<IActionResult> Tags()
        {
            var tags = await _context.Tags
                .OrderBy(t => t.Name)
                .ToListAsync();

            return View(tags);
        }

        // ---------------------------------------------------------
        // DIAGNOSTIC: Debug file presence
        // ---------------------------------------------------------
        [AllowAnonymous]
        public IActionResult DebugVideoFile(int id)
        {
            var video = _context.Videos.FirstOrDefault(v => v.Id == id);
            if (video == null) return NotFound("Video not found in DB");

            string fileName = video.FileName ?? "";
            string path = Path.Combine(_environment.WebRootPath, "videos", fileName);

            if (!System.IO.File.Exists(path))
                return NotFound($"File missing on disk: {path}");

            var fi = new FileInfo(path);
            return Content($"File exists: {path}\nSize: {fi.Length} bytes\nDB FileName: {fileName}");
        }

        // ---------------------------------------------------------
        // STREAM VIDEO (temporary, supports range requests)
        // ---------------------------------------------------------
        [AllowAnonymous]
        public IActionResult StreamVideo(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequest("fileName required");

            string path = Path.Combine(_environment.WebRootPath, "videos", fileName);
            if (!System.IO.File.Exists(path))
                return NotFound();

            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "video/mp4", enableRangeProcessing: true);
        }
    }
}
