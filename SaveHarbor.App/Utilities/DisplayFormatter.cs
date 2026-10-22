namespace SaveHarbor.App.Utilities;

public static class DisplayFormatter
{
    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }

    public static string FormatAge(DateTimeOffset timestamp)
    {
        var elapsed = DateTimeOffset.Now - timestamp;
        if (elapsed.TotalMinutes < 1)
        {
            return "just now";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"{(int)elapsed.TotalMinutes} min ago";
        }

        if (elapsed.TotalDays < 1)
        {
            return $"{(int)elapsed.TotalHours} h ago";
        }

        if (elapsed.TotalDays < 14)
        {
            return $"{(int)elapsed.TotalDays} d ago";
        }

        return timestamp.ToString("yyyy-MM-dd HH:mm");
    }
}
