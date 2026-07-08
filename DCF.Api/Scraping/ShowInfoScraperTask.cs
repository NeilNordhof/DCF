using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace DCF.Api.Scraping;

public class ShowInfoScraperTask(IHtmlFetcher fetcher) : IShowInfoScraperTask
{
    private static readonly Regex TimePattern =
        new(@"(\d{1,2}):(\d{2})\s*(AM|PM)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TzPattern =
        new(@"All times\s+(ET|CT|MT|PT)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LatLngQPattern =
        new(@"[?&]q=(-?\d+\.?\d*),(-?\d+\.?\d*)", RegexOptions.Compiled);

    private static readonly Regex LatLngQueryPattern =
        new(@"query=(-?\d+\.?\d*)%2C(-?\d+\.?\d*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LatLngAtPattern =
        new(@"@(-?\d+\.\d+),(-?\d+\.\d+)", RegexOptions.Compiled);

    private static readonly Regex WhitespacePattern =
        new(@"\s{2,}", RegexOptions.Compiled);

    private static readonly Regex LatLngDirPattern =
        new(@"maps/dir/[^/]+/(-?\d{1,3}\.\d+),(-?\d{1,3}\.\d+)", RegexOptions.Compiled);

    private static readonly Regex DatePattern =
        new(@"\w+,\s+\w+\s+\d{1,2},\s+\d{4}", RegexOptions.Compiled);

    public async Task<ShowPrefillData?> ScrapeAsync(string url)
    {
        string html;

        try
        {
            html = await fetcher.FetchAsync(url);
        }
        catch
        {
            return null;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var isExhibition = doc.DocumentNode.InnerText.Contains(
            "NON-COMPETITION FORMAT", StringComparison.OrdinalIgnoreCase);

        var location = ParseHeroLocation(doc);
        var date = ParseHeroDate(doc);
        var (lat, lng) = ParseLatLng(doc);
        var (timezone, allEntries) = ParseScheduleEntries(doc);

        var filteredEntries = allEntries
            .Where(e => !e.Label.Equals("Gates Open", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var startTime = filteredEntries.FirstOrDefault()?.Time24h;

        var scoresAnnouncedTime = filteredEntries
            .FirstOrDefault(e =>
                e.Label.Contains("score", StringComparison.OrdinalIgnoreCase) ||
                e.Label.Contains("recap", StringComparison.OrdinalIgnoreCase) ||
                e.Label.Contains("conclude", StringComparison.OrdinalIgnoreCase))
            ?.Time24h;

        return new ShowPrefillData(
            isExhibition,
            location,
            lat,
            lng,
            startTime,
            scoresAnnouncedTime,
            timezone,
            filteredEntries.AsReadOnly(),
            date);
    }

    private static string? ParseHeroLocation(HtmlDocument doc)
    {
        var locationNode = doc.DocumentNode
            .SelectSingleNode("//div[contains(@class,'inner-hero-inner')]//span[contains(@class,'location')]");

        if (locationNode is null)
        {
            return null;
        }

        return WhitespacePattern.Replace(locationNode.InnerText, " ").Trim();
    }

    private static string? ParseHeroDate(HtmlDocument doc)
    {
        var pNode = doc.DocumentNode
            .SelectSingleNode("//div[contains(@class,'inner-hero-inner')]//p[1]");

        if (pNode is null)
        {
            return null;
        }

        var text = pNode.InnerText.Trim();
        var match = DatePattern.Match(text);

        if (!match.Success)
        {
            return null;
        }

        if (!DateTime.TryParseExact(
                match.Value,
                "dddd, MMMM d, yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return null;
        }

        return date.ToString("yyyy-MM-dd");
    }

    private static (double? Lat, double? Lng) ParseLatLng(HtmlDocument doc)
    {
        var mapsNodes = doc.DocumentNode.SelectNodes(
            "//a[contains(@href,'maps.google.com') or contains(@href,'google.com/maps')]");

        if (mapsNodes is null)
        {
            return (null, null);
        }

        foreach (var node in mapsNodes)
        {
            var href = node.GetAttributeValue("href", string.Empty);

            var m = LatLngQPattern.Match(href);

            if (!m.Success)
            {
                m = LatLngQueryPattern.Match(href);
            }

            if (!m.Success)
            {
                m = LatLngAtPattern.Match(href);
            }

            if (!m.Success)
            {
                m = LatLngDirPattern.Match(href);
            }

            if (m.Success &&
                double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var lat) &&
                double.TryParse(m.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var lng))
            {
                return (lat, lng);
            }
        }

        return (null, null);
    }

    private static (string? Timezone, List<ShowPrefillScheduleEntry> Entries) ParseScheduleEntries(HtmlDocument doc)
    {
        var containerNode = doc.DocumentNode
            .SelectSingleNode("//div[contains(@class,'lineup-times-table')]");

        if (containerNode is null)
        {
            return (null, []);
        }

        string? timezone = null;
        var tzMatch = TzPattern.Match(containerNode.InnerText);

        if (tzMatch.Success)
        {
            timezone = tzMatch.Groups[1].Value.ToUpper();
        }

        var tableNode = containerNode.SelectSingleNode(".//table");

        if (tableNode is null)
        {
            return (timezone, []);
        }

        var rows = tableNode.SelectNodes(".//tr");

        if (rows is null)
        {
            return (timezone, []);
        }

        var entries = new List<ShowPrefillScheduleEntry>();

        foreach (var row in rows)
        {
            var cells = row.SelectNodes("td");

            if (cells is null || cells.Count < 2)
            {
                continue;
            }

            var rawTime = cells[0].InnerText.Trim();
            var rawLabel = HtmlEntity.DeEntitize(cells[1].InnerText).Trim();
            var label = StripCity(rawLabel);
            var time24h = ConvertTo24h(rawTime);

            entries.Add(new ShowPrefillScheduleEntry(time24h, label));
        }

        return (timezone, entries);
    }

    private static string? ConvertTo24h(string raw)
    {
        var m = TimePattern.Match(raw);

        if (!m.Success)
        {
            return null;
        }

        var hour = int.Parse(m.Groups[1].Value);
        var minute = int.Parse(m.Groups[2].Value);
        var isPm = m.Groups[3].Value.Equals("PM", StringComparison.OrdinalIgnoreCase);

        if (isPm && hour != 12)
        {
            hour += 12;
        }
        else if (!isPm && hour == 12)
        {
            hour = 0;
        }

        return $"{hour:D2}:{minute:D2}";
    }

    private static string StripCity(string label)
    {
        var dashIndex = label.IndexOf(" - ", StringComparison.Ordinal);

        return dashIndex >= 0 ? label[..dashIndex].Trim() : label.Trim();
    }
}
