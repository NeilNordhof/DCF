using TimeZoneConverter;

namespace DCF.Api.Services;

public static class DraftTimeFormatter
{
    public static string Format(DateTimeOffset utcTime, string? ianaTimezone)
    {
        if (string.IsNullOrEmpty(ianaTimezone))
        {

            return utcTime.ToString("dddd, MMMM d 'at' h:mm tt 'UTC'");
        }

        try
        {
            var tz = TZConvert.GetTimeZoneInfo(ianaTimezone);
            var localTime = TimeZoneInfo.ConvertTime(utcTime, tz);
            var formatted = localTime.ToString("dddd, MMMM d 'at' h:mm tt");
            var abbr = GetAbbreviation(tz, utcTime);

            return $"{formatted} {abbr}";
        }
        catch
        {

            return utcTime.ToString("dddd, MMMM d 'at' h:mm tt 'UTC'");
        }
    }

    private static string GetAbbreviation(TimeZoneInfo tz, DateTimeOffset utcTime)
    {
        var name = tz.IsDaylightSavingTime(utcTime) ? tz.DaylightName : tz.StandardName;

        return string.Concat(name.Split(' ').Select(w => w[0]));
    }
}
