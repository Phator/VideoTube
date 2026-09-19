public class EditThumbnailViewModel
{
    public int VideoId { get; set; }
    public string CurrentThumbnail { get; set; }

    public int? TimestampSeconds { get; set; } // optional
    public IFormFile? CustomThumbnail { get; set; } // optional
}
