using System;
using System.Globalization;

namespace Dafeiyu
{
    public static class ImageGalleryDateFilter
    {
        public static bool TryParseDate(string text, bool endOfDay, out DateTime? utc)
        {
            utc = null;
            if (String.IsNullOrWhiteSpace(text)) return true;
            DateTime parsed;
            if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) return false;
            if (endOfDay && parsed.Date == DateTime.MaxValue.Date) return false;
            utc = (endOfDay ? parsed.AddDays(1).AddTicks(-1) : parsed).ToUniversalTime();
            return true;
        }
    }
}
