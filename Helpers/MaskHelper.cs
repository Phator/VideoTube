using Microsoft.AspNetCore.Http;

namespace VideoTube.Helpers
{
    public static class MaskHelper
    {
        public static bool IsMasked(HttpContext context)
        {
            return context.User.IsInRole("Masked");
        }
    }
}
