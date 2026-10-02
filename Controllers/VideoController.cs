using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
        private readonly IConfiguration _configuration;
        private readonly ILogger<VideoController> _logger;

        public VideoController(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            UserManager<ApplicationUser> userManager,
            IConfiguration configuration,
            ILogger<VideoController> logger)
        {
            _context = context;
            _environment = environment;
            _userManager = userManager;
            _configuration = configuration;
            _logger = logger;
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
            try
            {
                if (!ModelState.IsValid)
                    return View(model);

                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                    return RedirectToAction("Login", "Account");

                // --- Validate File ---
                if (model.VideoFile == null || model.VideoFile.Length == 0)
                {
                    ModelState.AddModelError("VideoFile", "Please select a video file.");
                    return View(model);
                }

                // Get configuration
                var maxFileSize = _configuration.GetValue<long>("VideoUpload:MaxFileSizeBytes");
                var allowedExtensions = _configuration.GetSection("VideoUpload:AllowedExtensions").Get<string[]>() ?? new[] { ".mp4" };
                var allowedMimeTypes = _configuration.GetSection("VideoUpload:AllowedMimeTypes").Get<string[]>() ?? new[] { "video/mp4" };

                // Validate file size
                if (model.VideoFile.Length > maxFileSize)
                {
                    ModelState.AddModelError("VideoFile", $"File size must not exceed {maxFileSize / (1024 * 1024 * 1024)} GB.");
                    _logger.LogWarning($"User {user.Id} attempted to upload file exceeding size limit: {model.VideoFile.Length} bytes");
                    return View(model);
                }

                // Validate file extension
                var fileExtension = Path.GetExtension(model.VideoFile.FileName).ToLower();
                if (!allowedExtensions.Contains(fileExtension))
                {
                    ModelState.AddModelError("VideoFile", $"File type '{fileExtension}' is not allowed. Allowed types: {string.Join(", ", allowedExtensions)}");
                    _logger.LogWarning($"User {user.Id} attempted to upload file with invalid extension: {fileExtension}");
                    return View(model);
                }

                // Validate MIME type
                if (!allowedMimeTypes.Contains(model.VideoFile.ContentType))
                {
                    ModelState.AddModelError("VideoFile", "Invalid video file type.");
                    _logger.LogWarning($"User {user.Id} attempted to upload file with invalid MIME type: {model.VideoFile.ContentType}");
                    return View(model);
                }

                // --- Save Video File ---
                var videoStoragePath = _configuration.GetValue<string>("VideoUpload:StoragePath") ?? "videos";
                string videoFolder = Path.Combine(_environment.WebRootPath, videoStoragePath);
                Directory.CreateDirectory(videoFolder);

                string fileName = Guid.NewGuid() + fileExtension;
                string filePath = Path.Combine(videoFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await model.VideoFile.CopyToAsync(stream);
                }
                _logger.LogInformation($"User {user.Id} uploaded video file: {fileName}");

                // --- Generate Thumbnail ---
                var thumbnailStoragePath = _configuration.GetValue<string>("VideoUpload:ThumbnailPath") ?? "thumbnails";
                var thumbnailFormat = _configuration.GetValue<string>("VideoUpload:ThumbnailFormat") ?? "jpg";
                string thumbnailFolder = Path.Combine(_environment.WebRootPath, thumbnailStoragePath);
                Directory.CreateDirectory(thumbnailFolder);

                string thumbnailFileName = Guid.NewGuid() + "." + thumbnailFormat;
                string thumbnailPath = Path.Combine(thumbnailFolder, thumbnailFileName);

                var ffmpegEnabled = _configuration.GetValue<bool>("FFmpeg:Enabled");
                var ffmpegPath = _configuration.GetValue<string>("FFmpeg:FfmpegPath") ?? "ffmpeg";
                var thumbnailArgs = _configuration.GetValue<string>("FFmpeg:ThumbnailExtractionArgs") 
                    ?? "-i \"{input}\" -vf \"select='gt(scene,0.4)'\" -vframes 1 \"{output}\"";

                if (ffmpegEnabled)
                {
                    try
                    {
                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = ffmpegPath,
                                Arguments = thumbnailArgs.Replace("{input}", filePath).Replace("{output}", thumbnailPath),
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardError = true,
                                RedirectStandardOutput = true
                            }
                        };

                        process.Start();
                        process.WaitForExit(30000); // 30 second timeout

                        if (process.ExitCode != 0)
                        {
                            _logger.LogWarning($"FFmpeg thumbnail extraction failed for video {fileName}. Exit code: {process.ExitCode}");
                            // Create placeholder thumbnail instead of failing
                            CreatePlaceholderThumbnail(thumbnailPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error generating thumbnail: {ex.Message}");
                        // Create placeholder thumbnail instead of failing
                        CreatePlaceholderThumbnail(thumbnailPath);
                    }
                }
                else
                {
                    _logger.LogInformation("FFmpeg is disabled. Creating placeholder thumbnail.");
                    CreatePlaceholderThumbnail(thumbnailPath);
                }

                // --- Get Duration ---
                string duration = "00:00";
                var ffprobePath = _configuration.GetValue<string>("FFmpeg:FfprobePath") ?? "ffprobe";

                if (ffmpegEnabled)
                {
                    try
                    {
                        var probeProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = ffprobePath,
                                Arguments = $"-v error -show_entries format=duration -of default:noprint_wrappers=1:nokey=1 \"{filePath}\"",
                                RedirectStandardOutput = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };

                        probeProcess.Start();
                        string output = probeProcess.StandardOutput.ReadToEnd();
                        probeProcess.WaitForExit(10000); // 10 second timeout

                        if (double.TryParse(output.Trim(), CultureInfo.InvariantCulture, out double seconds))
                        {
                            duration = TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Error getting video duration: {ex.Message}");
                        // Continue with default duration
                    }
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
                    UploadDate = DateTime.UtcNow,
                    Views = 0,
                    UploadedByUserId = user.Id
                };

                _context.Videos.Add(video);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Video uploaded successfully by user {user.Id}: {video.Title}");
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error uploading video: {ex.Message}");
                ModelState.AddModelError("", "An error occurred while uploading the video. Please try again.");
                return View(model);
            }
        }

        // Helper method to create a placeholder thumbnail
        private void CreatePlaceholderThumbnail(string thumbnailPath)
        {
            try
            {
                // Create a simple placeholder image (1x1 pixel PNG)
                byte[] placeholderPng = new byte[] 
                { 
                    0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
                    0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
                    0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, 0xDE, 0x00, 0x00, 0x00,
                    0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD3, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
                    0x00, 0x00, 0x03, 0x00, 0x01, 0x3B, 0xB6, 0xEE, 0x56, 0x00, 0x00, 0x00,
                    0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
                };

                if (!System.IO.File.Exists(thumbnailPath))
                {
                    System.IO.File.WriteAllBytes(thumbnailPath, placeholderPng);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating placeholder thumbnail: {ex.Message}");
            }
        }


        // ---------------------------------------------------------
        // WATCH VIDEO
        // ---------------------------------------------------------
        public async Task<IActionResult> Watch(int id)
        {
            try
            {
                var video = _context.Videos
                    .Include(v => v.Category)
                    .FirstOrDefault(v => v.Id == id);

                if (video == null)
                {
                    _logger.LogWarning($"Watch attempt on non-existent video: {id}");
                    return NotFound();
                }

                video.Views++;
                await _context.SaveChangesAsync();

                var user = await _userManager.GetUserAsync(User);

                // Progress
                if (user != null)
                {
                    ViewBag.Progress =
                        _context.WatchProgress
                            .Where(p => p.UserId == user.Id && p.VideoId == id)
                            .Select(p => p.CurrentSeconds)
                            .FirstOrDefault();
                }
                else
                {
                    ViewBag.Progress = 0;
                }

                // Favorites (FIXED)
                ViewBag.IsFavorited = false;
                if (user != null)
                {
                    ViewBag.IsFavorited = _context.FavoriteVideos
                        .Any(f => f.UserId == user.Id && f.VideoId == id);
                }

                // Related videos
                ViewBag.RelatedVideos = _context.Videos
                    .Include(v => v.Category)
                    .Where(v => v.Id != video.Id && v.CategoryId == video.CategoryId)
                    .OrderByDescending(v => v.Views)
                    .Take(5)
                    .ToList();

                return View(video);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading watch page for video {id}: {ex.Message}");
                return StatusCode(500, "An error occurred while loading the video.");
            }
        }

        // ---------------------------------------------------------
        // EDIT VIDEO
        // ---------------------------------------------------------
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var video = _context.Videos.FirstOrDefault(v => v.Id == id);
                if (video == null)
                {
                    _logger.LogWarning($"Edit attempt on non-existent video: {id}");
                    return NotFound();
                }

                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                    return RedirectToAction("Login", "Account");

                // Authorization check: Only video owner or admin can edit
                if (video.UploadedByUserId != user.Id && !User.IsInRole("Admin"))
                {
                    _logger.LogWarning($"Unauthorized edit attempt by user {user.Id} on video {id} (owner: {video.UploadedByUserId})");
                    return Forbid();
                }

                ViewBag.Categories = _context.Categories
                    .OrderBy(c => c.Name)
                    .ToList();

                return View(video);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading edit page for video {id}: {ex.Message}");
                return StatusCode(500, "An error occurred while loading the video.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> Edit(Video model)
        {
            try
            {
                var video = _context.Videos.FirstOrDefault(v => v.Id == model.Id);
                if (video == null)
                {
                    _logger.LogWarning($"Edit attempt on non-existent video: {model.Id}");
                    return NotFound();
                }

                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                    return RedirectToAction("Login", "Account");

                // Authorization check: Only video owner or admin can edit
                if (video.UploadedByUserId != user.Id && !User.IsInRole("Admin"))
                {
                    _logger.LogWarning($"Unauthorized edit attempt by user {user.Id} on video {model.Id} (owner: {video.UploadedByUserId})");
                    return Forbid();
                }

                // Validate input
                if (string.IsNullOrWhiteSpace(model.Title))
                {
                    ModelState.AddModelError("Title", "Title is required.");
                    ViewBag.Categories = _context.Categories.OrderBy(c => c.Name).ToList();
                    return View(model);
                }

                video.Title = model.Title;
                video.Description = model.Description ?? "";
                video.CategoryId = model.CategoryId;

                await _context.SaveChangesAsync();
                _logger.LogInformation($"Video {model.Id} edited by user {user.Id}");

                return RedirectToAction("Watch", new { id = video.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error editing video {model.Id}: {ex.Message}");
                ModelState.AddModelError("", "An error occurred while saving the video.");
                ViewBag.Categories = _context.Categories.OrderBy(c => c.Name).ToList();
                return View(model);
            }
        }

        // ---------------------------------------------------------
        // DELETE VIDEO
        // ---------------------------------------------------------
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var video = _context.Videos.FirstOrDefault(v => v.Id == id);
                if (video == null)
                {
                    _logger.LogWarning($"Delete attempt on non-existent video: {id}");
                    return NotFound();
                }

                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                    return RedirectToAction("Login", "Account");

                // Authorization check: Only video owner or admin can delete
                if (video.UploadedByUserId != user.Id && !User.IsInRole("Admin"))
                {
                    _logger.LogWarning($"Unauthorized delete attempt by user {user.Id} on video {id} (owner: {video.UploadedByUserId})");
                    return Forbid();
                }

                return View(video);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading delete page for video {id}: {ex.Message}");
                return StatusCode(500, "An error occurred while loading the video.");
            }
        }

        [HttpPost]
        [ActionName("Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var video = _context.Videos.FirstOrDefault(v => v.Id == id);
                if (video == null)
                {
                    _logger.LogWarning($"Delete attempt on non-existent video: {id}");
                    return RedirectToAction("Index");
                }

                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                    return RedirectToAction("Login", "Account");

                // Authorization check: Only video owner or admin can delete
                if (video.UploadedByUserId != user.Id && !User.IsInRole("Admin"))
                {
                    _logger.LogWarning($"Unauthorized delete attempt by user {user.Id} on video {id} (owner: {video.UploadedByUserId})");
                    return Forbid();
                }

                // Delete video file
                try
                {
                    string videoPath = Path.Combine(_environment.WebRootPath, "videos", video.FileName);
                    if (System.IO.File.Exists(videoPath))
                    {
                        System.IO.File.Delete(videoPath);
                        _logger.LogInformation($"Video file deleted: {video.FileName}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error deleting video file {video.FileName}: {ex.Message}");
                    // Continue with database deletion even if file deletion fails
                }

                // Delete thumbnail file
                if (!string.IsNullOrEmpty(video.ThumbnailFileName))
                {
                    try
                    {
                        string thumbnailPath = Path.Combine(_environment.WebRootPath, "thumbnails", video.ThumbnailFileName);
                        if (System.IO.File.Exists(thumbnailPath))
                        {
                            System.IO.File.Delete(thumbnailPath);
                            _logger.LogInformation($"Thumbnail file deleted: {video.ThumbnailFileName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error deleting thumbnail file {video.ThumbnailFileName}: {ex.Message}");
                        // Continue with database deletion even if file deletion fails
                    }
                }

                _context.Videos.Remove(video);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Video {id} deleted by user {user.Id}");

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error deleting video {id}: {ex.Message}");
                TempData["Error"] = "An error occurred while deleting the video.";
                return RedirectToAction("Index");
            }
        }

        // ---------------------------------------------------------
        // FAVORITES LIST
        // ---------------------------------------------------------
        public async Task<IActionResult> MyFavorites()
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                    return RedirectToAction("Login", "Account");

                var favorites = _context.FavoriteVideos
                    .Where(f => f.UserId == user.Id)
                    .Select(f => f.Video!)
                    .ToList();

                _logger.LogInformation($"User {user.Id} viewed their favorites ({favorites.Count} items)");
                return View(favorites);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading favorites: {ex.Message}");
                return StatusCode(500, "An error occurred while loading your favorites.");
            }
        }

        // ---------------------------------------------------------
        // FAVORITE TOGGLE (AJAX)
        // ---------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> ToggleFavorite([FromBody] FavoriteToggleRequest request)
        {
            try
            {
                if (request == null || request.Id <= 0)
                {
                    _logger.LogWarning("ToggleFavorite called with invalid request");
                    return BadRequest(new { error = "Invalid video ID" });
                }

                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    _logger.LogWarning("ToggleFavorite called by unauthenticated user");
                    return Unauthorized();
                }

                // Verify video exists
                var videoExists = await _context.Videos.AnyAsync(v => v.Id == request.Id);
                if (!videoExists)
                {
                    _logger.LogWarning($"ToggleFavorite called for non-existent video: {request.Id}");
                    return NotFound(new { error = "Video not found" });
                }

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
                    _logger.LogInformation($"User {user.Id} favorited video {request.Id}");
                }
                else
                {
                    _context.FavoriteVideos.Remove(fav);
                    nowFavorited = false;
                    _logger.LogInformation($"User {user.Id} unfavorited video {request.Id}");
                }

                await _context.SaveChangesAsync();
                return Json(new { favorited = nowFavorited });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error toggling favorite: {ex.Message}");
                return StatusCode(500, new { error = "An error occurred while updating your favorite." });
            }
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
            try
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

                _logger.LogInformation($"User {user.Id} viewed their watch history ({history.Count} items)");
                return View(history);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading watch history: {ex.Message}");
                return StatusCode(500, "An error occurred while loading your history.");
            }
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

            // ---------------------------------------------------------
            // Get video duration (for clamping timestamps)
            // ---------------------------------------------------------
            var probe = new Process
            {
                StartInfo =
        {
            FileName = @"C:\Users\andre\AppData\Local\Microsoft\WinGet\Links\ffprobe.exe",
            Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
            };

            probe.Start();
            string durationOutput = probe.StandardOutput.ReadToEnd();
            probe.WaitForExit();

            double.TryParse(durationOutput.Trim(), CultureInfo.InvariantCulture, out double videoSeconds);

            // ---------------------------------------------------------
            // 1. Custom thumbnail upload
            // ---------------------------------------------------------
            if (model.CustomThumbnail != null)
            {
                using var stream = new FileStream(newThumbPath, FileMode.Create);
                await model.CustomThumbnail.CopyToAsync(stream);
            }
            else if (model.TimestampSeconds.HasValue)
            {
                // ---------------------------------------------------------
                // 2. Timestamp-based FFmpeg (with clamping + HH:MM:SS)
                // ---------------------------------------------------------
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
                // ---------------------------------------------------------
                // 3. Scene-detect regeneration (default)
                // ---------------------------------------------------------
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

            // ---------------------------------------------------------
            // Update DB
            // ---------------------------------------------------------
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
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
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

        // GET + POST: Generate smart thumbnail candidates
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateSmartThumbnails(int id)
        {
            var video = await _context.Videos.FindAsync(id);
            if (video == null)
                return NotFound();

            string videoPath = Path.Combine(_environment.WebRootPath, "videos", video.FileName);
            if (!System.IO.File.Exists(videoPath))
                return NotFound();

            string thumbnailsDir = Path.Combine(_environment.WebRootPath, "thumbnails");
            Directory.CreateDirectory(thumbnailsDir);

            // Candidate timestamps
            int[] timestamps = { 5, 15, 30, 45 };
            var candidates = new List<string>();

            foreach (int t in timestamps)
            {
                string thumbFileName = $"{video.Id}_cand_{t}.jpg";
                string thumbPath = Path.Combine(thumbnailsDir, thumbFileName);

                var ffmpeg = new Process
                {
                    StartInfo =
            {
                FileName = @"C:\Users\andre\AppData\Local\Microsoft\WinGet\Links\ffmpeg.exe",
                Arguments = $"-ss {t} -i \"{videoPath}\" -vframes 1 -q:v 2 \"{thumbPath}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
                };

                ffmpeg.Start();

                // MUST read both streams to avoid deadlock
                string stderr = await ffmpeg.StandardError.ReadToEndAsync();
                string stdout = await ffmpeg.StandardOutput.ReadToEndAsync();

                await ffmpeg.WaitForExitAsync();

                // Optional logging (helps diagnose issues)
                Debug.WriteLine($"FFmpeg @ {t}s exit code: {ffmpeg.ExitCode}");
                if (!string.IsNullOrWhiteSpace(stderr))
                    Debug.WriteLine($"FFmpeg stderr: {stderr}");

                if (System.IO.File.Exists(thumbPath))
                    candidates.Add(thumbFileName);
            }

            // Auto-select the middle candidate
            string autoSelected = candidates.Count > 0
                ? candidates[candidates.Count / 2]
                : null;

            var vm = new SmartThumbnailViewModel
            {
                VideoId = video.Id,
                VideoTitle = video.Title,
                CandidateThumbnails = candidates,
                AutoSelectedThumbnail = autoSelected
            };

            return View("SmartThumbnails", vm);
        }


        // POST: Save chosen thumbnail
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveSmartThumbnail(int videoId, string thumbnailFileName)
        {
            var video = _context.Videos.Find(videoId);
            if (video == null)
                return NotFound();

            video.ThumbnailFileName = thumbnailFileName;
            _context.SaveChanges();

            return RedirectToAction("Watch", new { id = videoId });
        }
    }
}
