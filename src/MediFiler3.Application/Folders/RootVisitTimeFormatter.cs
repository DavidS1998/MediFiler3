namespace MediFiler3.Application.Folders;

public static class RootVisitTimeFormatter
{
    public static string Relative(DateTimeOffset? visited, DateTimeOffset now)
    {
        if (visited is null) return "Never visited";
        var seconds = Math.Max(0, (now - visited.Value).TotalSeconds);
        var (count, unit) = seconds switch
        {
            < 60 => ((long)seconds, "second"),
            < 3600 => ((long)(seconds / 60), "minute"),
            < 86400 => ((long)(seconds / 3600), "hour"),
            < 2592000 => ((long)(seconds / 86400), "day"),
            < 31536000 => ((long)(seconds / 2592000), "month"), // 30 days
            _ => ((long)(seconds / 31536000), "year") // 365 days
        };
        return $"{count} {unit}{(count == 1 ? "" : "s")} ago";
    }
}
