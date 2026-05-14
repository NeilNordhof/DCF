using DCF.ScoreScraper.Models;
using DCF.ScoreScraper.Services;
using HtmlAgilityPack;

namespace DCF.ScoreScraper.Tasks;

public class RecapScraperTask : IRecapScraperTask
{
    private readonly ICorpsService _corpsService;
    private readonly HttpClient _httpClient;

    // Ordered most-specific first so Contains() matches the right entry.
    // "Total" is last so it doesn't absorb "GE Total", "Visual Total", etc.
    private static readonly (string Key, Caption Caption, Action<Result, Score?> Setter)[] CaptionMap =
    [
        ("GE Music 1",          Caption.GeneralEffectMusic,  (r, s) => r.GeneralEffectMusic1 = s),
        ("GE Music 2",          Caption.GeneralEffectMusic,  (r, s) => r.GeneralEffectMusic2 = s),
        ("GE Visual 1",         Caption.GeneralEffectVisual, (r, s) => r.GeneralEffectVisual1 = s),
        ("GE Visual 2",         Caption.GeneralEffectVisual, (r, s) => r.GeneralEffectVisual2 = s),
        ("GE Total",            Caption.GeneralEffect,       (r, s) => r.GeneralEffect = s),
        ("General Effect",      Caption.GeneralEffect,       (r, s) => r.GeneralEffect = s),
        ("Visual Analysis",     Caption.VisualAnalysis,      (r, s) => r.VisualAnalysis = s),
        ("Visual Proficiency",  Caption.VisualProficiency,   (r, s) => r.VisualProficiency = s),
        ("Visual Total",        Caption.Visual,              (r, s) => r.Visual = s),
        ("Color Guard",         Caption.ColorGuard,          (r, s) => r.ColorGuard = s),
        ("Brass",               Caption.Brass,               (r, s) => r.Brass = s),
        ("Music Analysis",      Caption.MusicAnalysis,       (r, s) => r.MusicAnalysis = s),
        ("Percussion 1",        Caption.Percussion,          (r, s) => r.Percussion1 = s),
        ("Percussion 2",        Caption.Percussion,          (r, s) => r.Percussion2 = s),
        ("Music Total",         Caption.Music,               (r, s) => r.Music = s),
        ("Sub Total",           Caption.SubTotal,            (r, s) => r.SubTotal = s),
        // Generic labels — must follow all compound matches above
        ("Visual",              Caption.Visual,              (r, s) => r.Visual = s),
        ("Music",               Caption.Music,               (r, s) => r.Music = s),
        ("Penalty",             Caption.Penalty,             (r, s) => r.Penalty = s),
        // "Total" last — matches only after all more-specific "* Total" entries
        ("Total",               Caption.Total,               (r, s) => r.Total = s),
    ];

    public RecapScraperTask(ICorpsService corpsService, HttpClient httpClient)
    {
        _corpsService = corpsService;
        _httpClient = httpClient;
    }

    public async Task<List<Result>> ScrapeAsync(Show show)
    {
        if (!show.URL.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Show URL must use HTTPS: {show.URL}");

        var html = await _httpClient.GetStringAsync(show.URL);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var corpsHeaderCell = doc.DocumentNode
            .SelectNodes("//td[contains(@class,'sticky-td')]")
            ?.FirstOrDefault(n => n.InnerText.Trim().Equals("Corps", StringComparison.OrdinalIgnoreCase));

        if (corpsHeaderCell is null)
            return [];

        var headerRow = corpsHeaderCell.ParentNode;
        var table = FindTableAncestor(headerRow);
        if (table is null)
            return [];

        var captionColumns = BuildCaptionColumns(headerRow);
        if (captionColumns.Count == 0)
            return [];

        var allCorps = await _corpsService.GetCorpsAsync();
        var results = new List<Result>();
        int resultId = 1;

        var allRows = table.SelectNodes(".//tr");
        if (allRows is null)
            return [];

        bool pastHeader = false;
        foreach (var row in allRows)
        {
            if (row == headerRow)
            {
                pastHeader = true;
                continue;
            }

            if (!pastHeader)
                continue;

            var cells = row.SelectNodes("td");
            if (cells is null || cells.Count < 2)
                continue;

            var corpsName = HtmlEntity.DeEntitize(cells[0].InnerText.Trim());
            if (string.IsNullOrWhiteSpace(corpsName))
                continue;

            if (!allCorps.TryGetValue(corpsName, out var corpsObj))
                corpsObj = new Corps(corpsName);

            var result = new Result
            {
                Id = resultId++,
                Corps = corpsObj,
                Show = show
            };

            int cellIndex = 1;
            foreach (var col in captionColumns)
            {
                if (cellIndex + 5 >= cells.Count)
                    break;

                var score = ParseScore(cells, cellIndex, corpsObj, show, col.Caption, col.Judge);
                if (score is not null)
                    col.Setter(result, score);

                cellIndex += 6;
            }

            results.Add(result);
        }

        return results;
    }

    private static List<CaptionColumn> BuildCaptionColumns(HtmlNode headerRow)
    {
        var columns = new List<CaptionColumn>();
        var cells = headerRow.SelectNodes("td");
        if (cells is null)
            return columns;

        foreach (var cell in cells.Skip(1))
        {
            var (captionText, judgeText) = ExtractCaptionAndJudge(cell);
            if (string.IsNullOrWhiteSpace(captionText))
                continue;

            if (TryMapCaption(captionText, out var caption, out var setter))
                columns.Add(new CaptionColumn(caption, judgeText, setter));
        }

        return columns;
    }

    // Extracts caption name and optional judge from a header cell.
    // Judge may appear in a <small> child, in parentheses, or after a newline.
    private static (string CaptionText, string? JudgeText) ExtractCaptionAndJudge(HtmlNode cell)
    {
        var smallNode = cell.SelectSingleNode(".//small");
        if (smallNode is not null)
        {
            var judgeText = HtmlEntity.DeEntitize(smallNode.InnerText.Trim());
            // Remove the small node's text from the full inner text
            var captionText = HtmlEntity.DeEntitize(
                cell.InnerText.Replace(smallNode.InnerText, "").Trim());
            return (captionText, judgeText);
        }

        var fullText = HtmlEntity.DeEntitize(cell.InnerText.Trim());

        var parenStart = fullText.IndexOf('(');
        if (parenStart > 0)
        {
            var caption = fullText[..parenStart].Trim();
            var remainder = fullText[(parenStart + 1)..];
            var parenEnd = remainder.IndexOf(')');
            var judge = parenEnd >= 0 ? remainder[..parenEnd].Trim() : remainder.Trim();
            return (caption, judge);
        }

        var newline = fullText.IndexOfAny(['\n', '\r']);
        if (newline > 0)
            return (fullText[..newline].Trim(), fullText[(newline + 1)..].Trim());

        return (fullText, null);
    }

    private static bool TryMapCaption(
        string captionText,
        out Caption caption,
        out Action<Result, Score?> setter)
    {
        foreach (var (key, cap, set) in CaptionMap)
        {
            if (captionText.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                caption = cap;
                setter = set;
                return true;
            }
        }

        caption = default;
        setter = static (_, _) => { };
        return false;
    }

    private static Score? ParseScore(
        HtmlNodeCollection cells,
        int startIndex,
        Corps corps,
        Show show,
        Caption caption,
        string? judge)
    {
        if (!TryParseDouble(cells[startIndex].InnerText, out var repScore)) return null;
        if (!TryParseInt(cells[startIndex + 1].InnerText, out var repRank)) return null;
        if (!TryParseDouble(cells[startIndex + 2].InnerText, out var perfScore)) return null;
        if (!TryParseInt(cells[startIndex + 3].InnerText, out var perfRank)) return null;
        if (!TryParseDouble(cells[startIndex + 4].InnerText, out var totalScore)) return null;
        if (!TryParseInt(cells[startIndex + 5].InnerText, out var totalRank)) return null;

        return new Score
        {
            Id = Guid.NewGuid(),
            Corps = corps,
            Show = show,
            Caption = caption,
            Judge = judge,
            RepertoireScore = repScore,
            RepertoireRank = repRank,
            PerformanceScore = perfScore,
            PerformanceRank = perfRank,
            TotalScore = totalScore,
            TotalRank = totalRank
        };
    }

    private static bool TryParseDouble(string raw, out double value) =>
        double.TryParse(raw.Trim(), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out value);

    private static bool TryParseInt(string raw, out int value) =>
        int.TryParse(raw.Trim(), out value);

    private static HtmlNode? FindTableAncestor(HtmlNode node)
    {
        var current = node.ParentNode;
        while (current is not null)
        {
            if (current.Name.Equals("table", StringComparison.OrdinalIgnoreCase))
                return current;
            current = current.ParentNode;
        }
        return null;
    }

    private sealed record CaptionColumn(
        Caption Caption,
        string? Judge,
        Action<Result, Score?> Setter);
}
