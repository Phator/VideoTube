namespace VideoTube.Models
{
    public class VideoTag
    {
        public int VideoId { get; set; }
        public Video Video { get; set; }

        public int TagId { get; set; }
        public Tag Tag { get; set; }
    }
}
