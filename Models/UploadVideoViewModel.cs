using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace VideoTube.Models
{
    public class UploadVideoViewModel
    {
        [Required]
        public string Title { get; set; } = "";

        public string Description { get; set; } = "";

        public int? CategoryId { get; set; }

        [Required]
        public IFormFile? VideoFile { get; set; }
    }
}