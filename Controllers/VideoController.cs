using Microsoft.AspNetCore.Mvc;
using VideoTube.Data;
using VideoTube.Models;
using System.Diagnostics;

namespace VideoTube.Controllers
{
    public class VideoController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public VideoController(
            ApplicationDbContext context,
            IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

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

            return View(
                videos.OrderByDescending(v => v.UploadDate)
                      .ToList());
        }

        public IActionResult Upload()
        {
            ViewBag.Categories =
                _context.Categories
                        .OrderBy(c => c.Name)
                        .ToList();

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Upload(
            UploadVideoViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            string videoFolder =
                Path.Combine(_environment.WebRootPath, "videos");

            Directory.CreateDirectory(videoFolder);

            string fileName =
                Guid.NewGuid().ToString() +
                Path.GetExtension(model.VideoFile.FileName);

            string filePath =
                Path.Combine(videoFolder, fileName);

            using (var stream =
                new FileStream(filePath, FileMode.Create))
            {
                await model.VideoFile.CopyToAsync(stream);
            }

            string thumbnailFolder =
                Path.Combine(_environment.WebRootPath, "thumbnails");

            Directory.CreateDirectory(thumbnailFolder);

            string thumbnailFileName =
                Guid.NewGuid() + ".jpg";

            string thumbnailPath =
                Path.Combine(
                    thumbnailFolder,
                    thumbnailFileName);

            var process = new Process();

            process.StartInfo.FileName =
                @"C:\Users\andre\AppData\Local\Microsoft\WinGet\Links\ffmpeg.exe";

            process.StartInfo.Arguments =
                $"-i \"{filePath}\" -ss 00:00:05 -vframes 1 \"{thumbnailPath}\"";

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();

            process.WaitForExit();

            var video = new Video
            {
                Title = model.Title,
                Description = model.Description,
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

        public IActionResult Watch(int id)
        {
            var video =
                _context.Videos.FirstOrDefault(
                    v => v.Id == id);

            if (video == null)
                return NotFound();

            video.Views++;

            _context.SaveChanges();

            return View(video);
        }

        public IActionResult Edit(int id)
        {
            var video = _context.Videos.FirstOrDefault(
                v => v.Id == id);

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
            var video = _context.Videos.FirstOrDefault(
                v => v.Id == model.Id);

            if (video == null)
                return NotFound();

            video.Title = model.Title;
            video.Description = model.Description;
            video.CategoryId = model.CategoryId;

            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        public IActionResult Delete(int id)
        {
            var video = _context.Videos.FirstOrDefault(
                v => v.Id == id);

            if (video == null)
                return NotFound();

            return View(video);
        }

        [HttpPost]
        [ActionName("Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var video = _context.Videos.FirstOrDefault(
                v => v.Id == id);

            if (video == null)
                return RedirectToAction("Index");

            string videoPath =
                Path.Combine(
                    _environment.WebRootPath,
                    "videos",
                    video.FileName);

            if (System.IO.File.Exists(videoPath))
            {
                System.IO.File.Delete(videoPath);
            }

            if (!string.IsNullOrEmpty(video.ThumbnailFileName))
            {
                string thumbnailPath =
                    Path.Combine(
                        _environment.WebRootPath,
                        "thumbnails",
                        video.ThumbnailFileName);

                if (System.IO.File.Exists(thumbnailPath))
                {
                    System.IO.File.Delete(thumbnailPath);
                }
            }

            _context.Videos.Remove(video);

            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }
    }
}