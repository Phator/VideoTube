namespace VideoTube.Models
{
    public class Tag
    {
        public int Id { get; set; }

        // Always initialized, never null
        public string Name { get; set; } = "";
    }
}
