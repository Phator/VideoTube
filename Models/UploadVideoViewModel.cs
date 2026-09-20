using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace VideoTube.Models
{
    public class UploadVideoViewModel
    {
        [Required]
        public string Title { get; set; } = "";

        public string Description { get; set; } = "";

        [Required]
        public int CategoryId { get; set; }

        [Required]
        public IFormFile VideoFile { get; set; } = null!;

        public string Tags { get; set; } = "";
    }
}
