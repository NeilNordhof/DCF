using DCF.Api.Scraping;
using DCF.Api.Services;
using DCF.Data.Models;
using Xunit;

namespace DCF.Tests.Scraping;

public class RecapScraperTaskTests
{
    // ---- Stubs ----

    private sealed class StubCorpsService : ICorpsService
    {
        private readonly IReadOnlyDictionary<string, Corps> _corps;

        public StubCorpsService(IReadOnlyDictionary<string, Corps>? corps = null)
        {
            _corps = corps ?? new Dictionary<string, Corps>();
        }

        public Task<IReadOnlyDictionary<string, Corps>> GetCorpsAsync(CancellationToken ct = default)
        {
            return Task.FromResult(_corps);
        }
    }

    private sealed class FakeHtmlFetcher : IHtmlFetcher
    {
        private readonly string _html;

        public FakeHtmlFetcher(string html)
        {
            _html = html;
        }

        public Task<string> FetchAsync(string url)
        {
            return Task.FromResult(_html);
        }
    }

    // ---- Factory ----

    private static RecapScraperTask CreateScraper(string html, IReadOnlyDictionary<string, Corps>? corps = null)
    {
        var fetcher = new FakeHtmlFetcher(html);
        var service = new StubCorpsService(corps);

        return new RecapScraperTask(service, fetcher);
    }

    private static Show MakeShow()
    {
        return new Show("Test Show", "https://example.com/recap", new DateOnly(2025, 7, 10));
    }

    // ---- HTML builders ----

    // A table-head cell for one sub-caption in the header row (type + judge + column headers).
    private static string TableHead(string type, string judge)
    {
        return $"""<td><table class="table-head"><tbody><tr><td class="type">{type}</td></tr><tr><td class="judge">{judge}</td></tr><tr class="head"><td>Rep</td><td>Perf</td><td>TOT</td></tr></tbody></table></td>""";
    }

    // A data cell for one sub-caption in a corps row: Rep/Perf/TOT each as score+rank spans.
    private static string DataCell(double rep, int repRk, double perf, int perfRk, double tot, int totRk)
    {
        return $"""<td><table class="data"><tbody><tr><td><span>{rep}</span><span>{repRk}</span></td><td><span>{perf}</span><span>{perfRk}</span></td><td><span>{tot}</span><span>{totRk}</span></td></tr></tbody></table></td>""";
    }

    // A section cell (GE, Visual, Music) for the header row.
    private static string SectionHeaderCell(string title, string tableHeadCells)
    {
        return $"""
<td><table class="main-sec-table"><tbody>
<tr><td class="main-title"><h2>{title}</h2></td></tr>
<tr>{tableHeadCells}<td class="total-data-head data-total">TOT</td></tr>
</tbody></table></td>
""";
    }

    // A section cell (GE, Visual, Music) for a corps row.
    private static string SectionDataCell(string dataCells, double sectionTot, int sectionRk)
    {
        return $"""
<td><table class="main-sec-table"><tbody>
<tr>{dataCells}<td class="data data-total"><span>{sectionTot}</span><span>{sectionRk}</span></td></tr>
</tbody></table></td>
""";
    }

    // Wraps one or more corps rows in an outer table with the given header sections.
    private static string BuildPage(
        string headerSections,
        string headerStandalone,
        string corpsRows)
    {
        return $"""
<table>
<tr>
  <td class="sticky-td">Corps</td>
  {headerSections}
  {headerStandalone}
</tr>
{corpsRows}
</table>
""";
    }

    private static string CorpsRow(string name, string sectionCells, string standaloneCells)
    {
        return $"""
<tr>
  <td class="sticky-td">{name}</td>
  {sectionCells}
  {standaloneCells}
</tr>
""";
    }

    // Builds a complete 2025-format recap page for one corps.
    private static string FullPage2025(string corpsName, string penaltyCell = "--")
    {
        var geHeader = SectionHeaderCell("General Effect",
            TableHead("General Effect 1", "Judge A") +
            TableHead("General Effect 2", "Judge B"));

        var visualHeader = SectionHeaderCell("Visual",
            TableHead("Visual Proficiency", "Judge C") +
            TableHead("Visual - Analysis", "Judge D") +
            TableHead("Color Guard", "Judge E"));

        var musicHeader = SectionHeaderCell("Music",
            TableHead("Music - Brass", "Judge F") +
            TableHead("Music - Analysis", "Judge G") +
            TableHead("Music - Percussion", "Judge H"));

        var standaloneHeader = """
<td class="main-title recap-td-border-2 data-total"><h2>Sub Total</h2></td>
<td class="main-title recap-td-border-2 penalties-td"><h2>Penalties</h2></td>
<td class="main-title data-total"><h2>Total</h2></td>
""";

        var geData = SectionDataCell(
            DataCell(7.9, 1, 7.4, 2, 15.3, 1) + DataCell(7.8, 1, 7.2, 2, 15.0, 1),
            30.3, 1);

        var visualData = SectionDataCell(
            DataCell(8.0, 1, 7.5, 2, 15.5, 1) +
            DataCell(7.9, 1, 7.6, 2, 15.5, 1) +
            DataCell(8.1, 1, 7.7, 2, 15.8, 1),
            46.8, 1);

        var musicData = SectionDataCell(
            DataCell(8.2, 1, 7.8, 2, 16.0, 1) +
            DataCell(7.7, 1, 7.3, 2, 15.0, 1) +
            DataCell(7.6, 1, 7.4, 2, 15.0, 1),
            46.0, 1);

        var standaloneData = $"""
<td class="data recap-td-border-2 data-total"><span>123.1</span><span>1</span></td>
<td class="data recap-td-border-2 penalties-td">{penaltyCell}</td>
<td class="data data-total"><span>123.1</span><span>1</span></td>
""";

        var headerSections = geHeader + visualHeader + musicHeader;
        var corpsSections = geData + visualData + musicData;

        return BuildPage(
            headerSections,
            standaloneHeader,
            CorpsRow(corpsName, corpsSections, standaloneData));
    }

    // ---- Tests ----

    [Fact]
    public async Task ScrapeAsync_Extracts_GE1_Score_With_Correct_Fields()
    {
        var scraper = CreateScraper(FullPage2025("Blue Devils"));

        var results = await scraper.ScrapeAsync(MakeShow());

        var score = results[0].GeneralEffectVisual1;
        Assert.NotNull(score);
        Assert.Equal(Caption.GeneralEffectVisual, score.Caption);
        Assert.Equal("Judge A", score.Judge);
        Assert.Equal(7.9, score.RepertoireScore, precision: 5);
        Assert.Equal(1, score.RepertoireRank);
        Assert.Equal(7.4, score.PerformanceScore, precision: 5);
        Assert.Equal(2, score.PerformanceRank);
        Assert.Equal(15.3, score.TotalScore, precision: 5);
        Assert.Equal(1, score.TotalRank);
    }

    [Fact]
    public async Task ScrapeAsync_Extracts_GE2_Score()
    {
        var scraper = CreateScraper(FullPage2025("Blue Devils"));

        var results = await scraper.ScrapeAsync(MakeShow());

        var score = results[0].GeneralEffectMusic1;
        Assert.NotNull(score);
        Assert.Equal(Caption.GeneralEffectMusic, score.Caption);
        Assert.Equal("Judge B", score.Judge);
        Assert.Equal(7.8, score.RepertoireScore, precision: 5);
        Assert.Equal(15.0, score.TotalScore, precision: 5);
    }

    [Fact]
    public async Task ScrapeAsync_Extracts_GeneralEffect_Section_Total()
    {
        var scraper = CreateScraper(FullPage2025("Blue Devils"));

        var results = await scraper.ScrapeAsync(MakeShow());

        var score = results[0].GeneralEffect;
        Assert.NotNull(score);
        Assert.Equal(Caption.GeneralEffect, score.Caption);
        Assert.Equal(30.3, score.TotalScore, precision: 5);
        Assert.Equal(1, score.TotalRank);
    }

    [Fact]
    public async Task ScrapeAsync_Maps_VisualProficiency()
    {
        var scraper = CreateScraper(FullPage2025("Blue Devils"));

        var results = await scraper.ScrapeAsync(MakeShow());

        var score = results[0].VisualProficiency;
        Assert.NotNull(score);
        Assert.Equal(Caption.VisualProficiency, score.Caption);
        Assert.Equal("Judge C", score.Judge);
        Assert.Equal(15.5, score.TotalScore, precision: 5);
    }

    [Fact]
    public async Task ScrapeAsync_Maps_VisualAnalysis_With_Dash_In_TypeName()
    {
        // "Visual - Analysis" (with dash) must map to VisualAnalysis, not be silently dropped.
        var scraper = CreateScraper(FullPage2025("Blue Devils"));

        var results = await scraper.ScrapeAsync(MakeShow());

        var score = results[0].VisualAnalysis;
        Assert.NotNull(score);
        Assert.Equal(Caption.VisualAnalysis, score.Caption);
        Assert.Equal("Judge D", score.Judge);
        Assert.Equal(15.5, score.TotalScore, precision: 5);
    }

    [Fact]
    public async Task ScrapeAsync_Maps_ColorGuard()
    {
        var scraper = CreateScraper(FullPage2025("Blue Devils"));

        var results = await scraper.ScrapeAsync(MakeShow());

        var score = results[0].ColorGuard;
        Assert.NotNull(score);
        Assert.Equal(Caption.ColorGuard, score.Caption);
        Assert.Equal(15.8, score.TotalScore, precision: 5);
    }

    [Fact]
    public async Task ScrapeAsync_Extracts_Visual_Section_Total()
    {
        var scraper = CreateScraper(FullPage2025("Blue Devils"));

        var results = await scraper.ScrapeAsync(MakeShow());

        var score = results[0].Visual;
        Assert.NotNull(score);
        Assert.Equal(Caption.Visual, score.Caption);
        Assert.Equal(46.8, score.TotalScore, precision: 5);
    }

    [Fact]
    public async Task ScrapeAsync_Maps_Brass_Via_Music_Brass_Prefix()
    {
        // "Music - Brass" must map to Brass via the "Brass" Contains match.
        var scraper = CreateScraper(FullPage2025("Blue Devils"));

        var results = await scraper.ScrapeAsync(MakeShow());

        var score = results[0].Brass;
        Assert.NotNull(score);
        Assert.Equal(Caption.Brass, score.Caption);
        Assert.Equal(16.0, score.TotalScore, precision: 5);
    }

    [Fact]
    public async Task ScrapeAsync_Maps_MusicAnalysis_With_Dash_In_TypeName()
    {
        // "Music - Analysis" (with dash) must map to MusicAnalysis, not fallback to Music.
        var scraper = CreateScraper(FullPage2025("Blue Devils"));

        var results = await scraper.ScrapeAsync(MakeShow());

        var score = results[0].MusicAnalysis1;
        Assert.NotNull(score);
        Assert.Equal(Caption.MusicAnalysis, score.Caption);
        Assert.Equal("Judge G", score.Judge);
        Assert.Equal(15.0, score.TotalScore, precision: 5);
    }

    [Fact]
    public async Task ScrapeAsync_Maps_MusicPercussion_To_Percussion1()
    {
        // "Music - Percussion" must populate Percussion1, not be silently dropped.
        var scraper = CreateScraper(FullPage2025("Blue Devils"));

        var results = await scraper.ScrapeAsync(MakeShow());

        var score = results[0].Percussion;
        Assert.NotNull(score);
        Assert.Equal(Caption.Percussion, score.Caption);
        Assert.Equal("Judge H", score.Judge);
        Assert.Equal(15.0, score.TotalScore, precision: 5);
    }

    [Fact]
    public async Task ScrapeAsync_Extracts_Music_Section_Total()
    {
        var scraper = CreateScraper(FullPage2025("Blue Devils"));

        var results = await scraper.ScrapeAsync(MakeShow());

        var score = results[0].Music;
        Assert.NotNull(score);
        Assert.Equal(Caption.Music, score.Caption);
        Assert.Equal(46.0, score.TotalScore, precision: 5);
    }

    [Fact]
    public async Task ScrapeAsync_Extracts_SubTotal_From_Standalone_Section()
    {
        var scraper = CreateScraper(FullPage2025("Blue Devils"));

        var results = await scraper.ScrapeAsync(MakeShow());

        var score = results[0].SubTotal;
        Assert.NotNull(score);
        Assert.Equal(Caption.SubTotal, score.Caption);
        Assert.Equal(123.1, score.TotalScore, precision: 5);
        Assert.Equal(1, score.TotalRank);
    }

    [Fact]
    public async Task ScrapeAsync_Extracts_Total_From_Standalone_Section()
    {
        var scraper = CreateScraper(FullPage2025("Blue Devils"));

        var results = await scraper.ScrapeAsync(MakeShow());

        var score = results[0].Total;
        Assert.NotNull(score);
        Assert.Equal(Caption.Total, score.Caption);
        Assert.Equal(123.1, score.TotalScore, precision: 5);
    }

    [Fact]
    public async Task ScrapeAsync_Penalty_Cell_With_Text_Dashes_Produces_No_Score()
    {
        // "--" in the penalties cell means no penalty; Penalty should remain null.
        var scraper = CreateScraper(FullPage2025("Blue Devils", penaltyCell: "--"));

        var results = await scraper.ScrapeAsync(MakeShow());

        Assert.Null(results[0].Penalty);
    }

    [Fact]
    public async Task ScrapeAsync_Penalty_Cell_With_Score_Produces_Penalty_Score()
    {
        // When a corps receives a penalty, the cell holds spans rather than "--".
        var scraper = CreateScraper(FullPage2025("Blue Devils", penaltyCell: "<span>-0.1</span><span>1</span>"));

        var results = await scraper.ScrapeAsync(MakeShow());

        var score = results[0].Penalty;
        Assert.NotNull(score);
        Assert.Equal(Caption.Penalty, score.Caption);
        Assert.Equal(-0.1, score.TotalScore, precision: 5);
        Assert.Equal(1, score.TotalRank);
    }

    [Fact]
    public async Task ScrapeAsync_Returns_Result_For_Each_Corps_Row()
    {
        var geHeader = SectionHeaderCell("General Effect", TableHead("General Effect 1", "J"));
        var standaloneHeader = """<td class="main-title data-total"><h2>Total</h2></td>""";

        var geData1 = SectionDataCell(DataCell(7.9, 1, 7.4, 2, 15.3, 1), 15.3, 1);
        var geData2 = SectionDataCell(DataCell(7.5, 2, 7.0, 1, 14.5, 2), 14.5, 2);

        var standaloneData1 = """<td class="data data-total"><span>15.3</span><span>1</span></td>""";
        var standaloneData2 = """<td class="data data-total"><span>14.5</span><span>2</span></td>""";

        var html = BuildPage(
            geHeader,
            standaloneHeader,
            CorpsRow("Blue Devils", geData1, standaloneData1) +
            CorpsRow("Cavaliers", geData2, standaloneData2));

        var scraper = CreateScraper(html);

        var results = await scraper.ScrapeAsync(MakeShow());

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Corps.Name == "Blue Devils");
        Assert.Contains(results, r => r.Corps.Name == "Cavaliers");
    }

    [Fact]
    public async Task ScrapeAsync_Uses_Existing_Corps_Object_When_Name_Matches()
    {
        var existingCorps = new Corps(Guid.Parse("11111111-0000-0000-0000-000000000000"), "Blue Devils");
        var corpsDict = new Dictionary<string, Corps> { ["Blue Devils"] = existingCorps };

        var html = FullPage2025("Blue Devils");
        var scraper = CreateScraper(html, corpsDict);

        var results = await scraper.ScrapeAsync(MakeShow());

        Assert.Equal(existingCorps.Id, results[0].Corps.Id);
    }

    [Fact]
    public async Task ScrapeAsync_Creates_New_Corps_When_Name_Not_In_Service()
    {
        // Empty service — any corps name should produce a newly constructed Corps.
        var html = FullPage2025("Phantom Regiment");
        var scraper = CreateScraper(html, corps: null);

        var results = await scraper.ScrapeAsync(MakeShow());

        Assert.Single(results);
        Assert.Equal("Phantom Regiment", results[0].Corps.Name);
    }

    [Fact]
    public async Task ScrapeAsync_Returns_Empty_List_When_Corps_Header_Not_Found()
    {
        // No sticky-td with text "Corps" — scraper should return empty, not throw.
        const string html = "<table><tr><td>Not a recap page</td></tr></table>";
        var scraper = CreateScraper(html);

        var results = await scraper.ScrapeAsync(MakeShow());

        Assert.Empty(results);
    }

    [Fact]
    public async Task ScrapeAsync_Handles_Outer_Table_With_Explicit_Tbody()
    {
        // Verify the "tbody/tr" path in "tr | tbody/tr" works when the outer table has an explicit tbody.
        var geHeader = SectionHeaderCell("General Effect", TableHead("General Effect 1", "J"));
        var standaloneHeader = """<td class="main-title data-total"><h2>Total</h2></td>""";
        var geData = SectionDataCell(DataCell(7.9, 1, 7.4, 2, 15.3, 1), 15.3, 1);
        var standaloneData = """<td class="data data-total"><span>15.3</span><span>1</span></td>""";

        var html = $"""
<table>
<tbody>
<tr>
  <td class="sticky-td">Corps</td>
  {geHeader}
  {standaloneHeader}
</tr>
{CorpsRow("Blue Devils", geData, standaloneData)}
</tbody>
</table>
""";

        var scraper = CreateScraper(html);

        var results = await scraper.ScrapeAsync(MakeShow());

        Assert.Single(results);
        Assert.Equal("Blue Devils", results[0].Corps.Name);
    }
}
