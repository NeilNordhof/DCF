using DCF.Api.Services;
using DCF.Data.Models;
using HtmlAgilityPack;

namespace DCF.Api.Scraping;

public class RecapScraperTask : IRecapScraperTask
{
    private readonly ICorpsService _corpsService;
    private readonly HttpClient _httpClient;

    // Ordered most-specific first so Contains() matches the right entry.
    // 2025 DCI format uses "General Effect 1/2", "Music - X", "Visual - X" naming.
    // Legacy format uses "GE Music 1", "GE Visual 1" etc.
    // "Total" is last so it doesn't absorb "GE Total", "Visual Total", etc.
    private static readonly (string Key, Caption Caption, Action<Result, Score?> Setter)[] CaptionMap =
    [
        // GE sub-captions — 2025 format first, then legacy
        ("General Effect 1",    Caption.GeneralEffectMusic,  (r, s) => r.GeneralEffectMusic1 = s),
        ("General Effect 2",    Caption.GeneralEffectMusic,  (r, s) => r.GeneralEffectMusic2 = s),
        ("GE Music 1",          Caption.GeneralEffectMusic,  (r, s) => r.GeneralEffectMusic1 = s),
        ("GE Music 2",          Caption.GeneralEffectMusic,  (r, s) => r.GeneralEffectMusic2 = s),
        ("GE Visual 1",         Caption.GeneralEffectVisual, (r, s) => r.GeneralEffectVisual1 = s),
        ("GE Visual 2",         Caption.GeneralEffectVisual, (r, s) => r.GeneralEffectVisual2 = s),
        ("GE Total",            Caption.GeneralEffect,       (r, s) => r.GeneralEffect = s),
        ("General Effect",      Caption.GeneralEffect,       (r, s) => r.GeneralEffect = s),
        // Visual sub-captions — 2025 uses dashes; legacy doesn't
        ("Visual - Analysis",   Caption.VisualAnalysis,      (r, s) => r.VisualAnalysis = s),
        ("Visual Analysis",     Caption.VisualAnalysis,      (r, s) => r.VisualAnalysis = s),
        ("Visual Proficiency",  Caption.VisualProficiency,   (r, s) => r.VisualProficiency = s),
        ("Visual Total",        Caption.Visual,              (r, s) => r.Visual = s),
        ("Color Guard",         Caption.ColorGuard,          (r, s) => r.ColorGuard = s),
        // Music sub-captions — 2025 uses "Music - X" prefix
        ("Music - Analysis",    Caption.MusicAnalysis,       (r, s) => r.MusicAnalysis = s),
        ("Music Analysis",      Caption.MusicAnalysis,       (r, s) => r.MusicAnalysis = s),
        ("Music - Percussion",  Caption.Percussion,          (r, s) => r.Percussion1 = s),
        ("Music - Brass",       Caption.Brass,               (r, s) => r.Brass = s),
        ("Brass",               Caption.Brass,               (r, s) => r.Brass = s),
        ("Percussion 1",        Caption.Percussion,          (r, s) => r.Percussion1 = s),
        ("Percussion 2",        Caption.Percussion,          (r, s) => r.Percussion2 = s),
        ("Music Total",         Caption.Music,               (r, s) => r.Music = s),
        ("Sub Total",           Caption.SubTotal,            (r, s) => r.SubTotal = s),
        // Generic labels — must follow all compound matches above
        ("Visual",              Caption.Visual,              (r, s) => r.Visual = s),
        ("Music",               Caption.Music,               (r, s) => r.Music = s),
        ("Penalties",           Caption.Penalty,             (r, s) => r.Penalty = s),
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
        {
            throw new ArgumentException($"Show URL must use HTTPS: {show.URL}");
        }

        var html = await _httpClient.GetStringAsync(show.URL);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var corpsHeaderCell = doc.DocumentNode
            .SelectNodes("//td[contains(@class,'sticky-td')]")
            ?.FirstOrDefault(n => n.InnerText.Trim().Equals("Corps", StringComparison.OrdinalIgnoreCase));

        if (corpsHeaderCell is null)
        {
            return [];
        }

        var headerRow = corpsHeaderCell.ParentNode;
        var table = FindTableAncestor(headerRow);

        if (table is null)
        {
            return [];
        }

        // Use "tr | tbody/tr" instead of ".//tr" to get only outer rows,
        // not the rows from the deeply nested section and score tables.
        var outerRows = table.SelectNodes("tr | tbody/tr");

        if (outerRows is null)
        {
            return [];
        }

        // Skip the first cell (Corps sticky-td); remaining are section cells.
        var headerSectionCells = headerRow.SelectNodes("td")?.Skip(1).ToList();

        if (headerSectionCells is null || headerSectionCells.Count == 0)
        {
            return [];
        }

        var allCorps = await _corpsService.GetCorpsAsync();
        var results = new List<Result>();
        int resultId = 1;

        foreach (var row in outerRows)
        {
            if (row == headerRow)
            {
                continue;
            }

            var rowCells = row.SelectNodes("td");

            if (rowCells is null || rowCells.Count < 2)
            {
                continue;
            }

            var corpsName = HtmlEntity.DeEntitize(rowCells[0].InnerText.Trim());

            if (string.IsNullOrWhiteSpace(corpsName))
            {
                continue;
            }

            if (!allCorps.TryGetValue(corpsName, out var corpsObj))
            {
                corpsObj = new Corps(corpsName);
            }

            var result = new Result
            {
                Id = resultId++,
                Corps = corpsObj,
                Show = show
            };

            // Skip the first cell (corps sticky-td); remaining parallel the header section cells.
            var corpsSectionCells = rowCells.Skip(1).ToList();
            int sectionCount = Math.Min(headerSectionCells.Count, corpsSectionCells.Count);

            for (int s = 0; s < sectionCount; s++)
            {
                ProcessSection(headerSectionCells[s], corpsSectionCells[s], result, corpsObj, show);
            }

            results.Add(result);
        }

        return results;
    }

    private static void ProcessSection(
        HtmlNode headerCell,
        HtmlNode corpsCell,
        Result result,
        Corps corps,
        Show show)
    {
        var hasMainSecTable = headerCell
            .SelectSingleNode("table[contains(@class,'main-sec-table')]") is not null;

        if (hasMainSecTable)
        {
            ProcessSubCaptionSection(headerCell, corpsCell, result, corps, show);
        }
        else
        {
            ProcessStandaloneSection(headerCell, corpsCell, result, corps, show);
        }
    }

    // Handles GE, Visual, Music sections — each contains table-head sub-tables in the
    // header and table.data sub-tables in corps rows, plus a section total td.
    private static void ProcessSubCaptionSection(
        HtmlNode headerCell,
        HtmlNode corpsCell,
        Result result,
        Corps corps,
        Show show)
    {
        var tableHeads = headerCell.SelectNodes(".//table[contains(@class,'table-head')]");
        var dataTables = corpsCell.SelectNodes(".//table[contains(@class,'data')]");

        if (tableHeads is not null && dataTables is not null)
        {
            int count = Math.Min(tableHeads.Count, dataTables.Count);

            for (int i = 0; i < count; i++)
            {
                var typeCell = tableHeads[i].SelectSingleNode(".//td[contains(@class,'type')]");
                var judgeCell = tableHeads[i].SelectSingleNode(".//td[contains(@class,'judge')]");

                if (typeCell is null)
                {
                    continue;
                }

                var captionText = HtmlEntity.DeEntitize(typeCell.InnerText.Trim());
                var judgeText = judgeCell is not null
                    ? HtmlEntity.DeEntitize(judgeCell.InnerText.Trim())
                    : null;

                if (!TryMapCaption(captionText, out var caption, out var setter))
                {
                    continue;
                }

                var score = ParseSubCaptionScore(dataTables[i], corps, show, caption, judgeText);

                if (score is not null)
                {
                    setter(result, score);
                }
            }
        }

        var headerTotalCell = headerCell.SelectSingleNode(".//td[contains(@class,'total-data-head')]");
        var corpsTotalCell = corpsCell.SelectSingleNode(".//td[contains(@class,'data-total')]");

        if (headerTotalCell is null || corpsTotalCell is null)
        {
            return;
        }

        var titleNode = headerCell.SelectSingleNode(".//td[contains(@class,'main-title')]//h2");

        if (titleNode is null)
        {
            return;
        }

        var sectionName = HtmlEntity.DeEntitize(titleNode.InnerText.Trim());

        if (!TryMapCaption(sectionName + " Total", out var totalCaption, out var totalSetter) &&
            !TryMapCaption(sectionName, out totalCaption, out totalSetter))
        {
            return;
        }

        var totalScore = ParseTotalScore(corpsTotalCell, corps, show, totalCaption, null);

        if (totalScore is not null)
        {
            totalSetter(result, totalScore);
        }
    }

    // Handles Sub Total, Penalties, and Total — standalone cells with no nested section tables.
    private static void ProcessStandaloneSection(
        HtmlNode headerCell,
        HtmlNode corpsCell,
        Result result,
        Corps corps,
        Show show)
    {
        var h2 = headerCell.SelectSingleNode(".//h2");
        var captionText = h2 is not null
            ? HtmlEntity.DeEntitize(h2.InnerText.Trim())
            : HtmlEntity.DeEntitize(headerCell.InnerText.Trim());

        if (!TryMapCaption(captionText, out var caption, out var setter))
        {
            return;
        }

        var judgeCell = headerCell.SelectSingleNode(".//td[contains(@class,'judge')]");
        var judgeText = judgeCell is not null
            ? HtmlEntity.DeEntitize(judgeCell.InnerText.Trim())
            : null;

        var score = ParseTotalScore(corpsCell, corps, show, caption, judgeText);

        if (score is not null)
        {
            setter(result, score);
        }
    }

    // Extracts a score from a table.data element: 3 cells (Rep, Perf, TOT),
    // each containing two <span> elements (score and rank).
    private static Score? ParseSubCaptionScore(
        HtmlNode dataTable,
        Corps corps,
        Show show,
        Caption caption,
        string? judge)
    {
        var dataRow = dataTable.SelectSingleNode(".//tr");

        if (dataRow is null)
        {
            return null;
        }

        var cells = dataRow.SelectNodes("td");

        if (cells is null || cells.Count < 3)
        {
            return null;
        }

        if (!TryExtractSpans(cells[0], out var repScore, out var repRank)) return null;
        if (!TryExtractSpans(cells[1], out var perfScore, out var perfRank)) return null;
        if (!TryExtractSpans(cells[2], out var totalScore, out var totalRank)) return null;

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

    // Extracts a total-only score from a cell with two <span> elements (score and rank).
    // Used for section totals (GE Total etc.) and standalone cells (Sub Total, Total).
    private static Score? ParseTotalScore(
        HtmlNode cell,
        Corps corps,
        Show show,
        Caption caption,
        string? judge)
    {
        if (!TryExtractSpans(cell, out var totalScore, out var totalRank))
        {
            return null;
        }

        return new Score
        {
            Id = Guid.NewGuid(),
            Corps = corps,
            Show = show,
            Caption = caption,
            Judge = judge,
            TotalScore = totalScore,
            TotalRank = totalRank
        };
    }

    private static bool TryExtractSpans(HtmlNode cell, out double score, out int rank)
    {
        score = 0;
        rank = 0;

        var spans = cell.SelectNodes(".//span");

        if (spans is null || spans.Count < 2)
        {
            return false;
        }

        if (!TryParseDouble(spans[0].InnerText, out score)) return false;
        if (!TryParseInt(spans[1].InnerText, out rank)) return false;

        return true;
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

    private static bool TryParseDouble(string raw, out double value)
    {
        return double.TryParse(raw.Trim(), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseInt(string raw, out int value)
    {
        return int.TryParse(raw.Trim(), out value);
    }

    private static HtmlNode? FindTableAncestor(HtmlNode node)
    {
        var current = node.ParentNode;

        while (current is not null)
        {
            if (current.Name.Equals("table", StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            current = current.ParentNode;
        }

        return null;
    }
}
