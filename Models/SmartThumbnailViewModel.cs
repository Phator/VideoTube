public class SmartThumbnailViewModel
{
	public int VideoId { get; set; }
	public string VideoTitle { get; set; }
	public List<string> CandidateThumbnails { get; set; } = new();
	public string AutoSelectedThumbnail { get; set; }
}
