using System.Globalization;

namespace Zapper.Application.Time;

public static class SouthAfricanTime
{
    public const string TimeZoneId = "Africa/Johannesburg";
    private static readonly TimeZoneInfo Zone = ResolveTimeZone();

    public static string FormatProviderTimestamp(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, Zone).ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);

    public static DateTimeOffset? ParseProviderTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var hasOffset = value.EndsWith('Z') || value.EndsWith('z')
            || value.Contains('+', StringComparison.Ordinal)
            || value.LastIndexOf('-') > 9;
        if (hasOffset)
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset)
                ? withOffset.ToUniversalTime()
                : null;

        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return null;
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) GetBusinessDateUtcRange(DateOnly date)
    {
        var startLocal = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var endLocal = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(startLocal, Zone), TimeSpan.Zero),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(endLocal, Zone), TimeSpan.Zero));
    }

    private static TimeZoneInfo ResolveTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time"); }
    }
}