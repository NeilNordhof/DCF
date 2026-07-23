using DCF.Api.Services;
using Xunit;

namespace DCF.Tests.Services;

public class EmailTemplateTests
{
    private static readonly Guid TestLeagueId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TestSeasonId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private const string FrontendUrl = "http://app.test";
    private const string Token = "test-token";

    [Fact]
    public void DraftTomorrow_SubjectAndHtmlContainLeagueName()
    {
        var (subject, html) = EmailTemplate.DraftTomorrow(
            "Test League", "Tuesday, June 16 at 7:00 PM EDT", TestLeagueId, FrontendUrl, Token);

        Assert.Equal("Draft tomorrow — Test League", subject);
        Assert.Contains("Test League", html);
        Assert.Contains("Tuesday, June 16 at 7:00 PM EDT", html);
        Assert.Contains($"/leagues/{TestLeagueId}/draft", html);
        Assert.Contains($"/unsubscribe?token={Token}", html);
        Assert.Contains("Go to Draft Room", html);
    }

    [Fact]
    public void DraftInOneHour_SubjectAndHtmlContainLeagueName()
    {
        var (subject, html) = EmailTemplate.DraftInOneHour(
            "Test League", "Tuesday, June 16 at 7:00 PM EDT", TestLeagueId, FrontendUrl, Token);

        Assert.Equal("Draft in 1 hour — Test League", subject);
        Assert.Contains("Test League", html);
        Assert.Contains("Tuesday, June 16 at 7:00 PM EDT", html);
        Assert.Contains("Go to Draft Room", html);
    }

    [Fact]
    public void DraftRoomOpen_SubjectAndHtmlContainLeagueNameAndMinutes()
    {
        var (subject, html) = EmailTemplate.DraftRoomOpen(
            "Test League", 10, TestLeagueId, FrontendUrl, Token);

        Assert.Equal("Draft room is open — Test League", subject);
        Assert.Contains("Test League", html);
        Assert.Contains("10", html);
        Assert.Contains("Go to Draft Room", html);
    }

    [Fact]
    public void DraftScheduled_SubjectAndHtmlContainActionAndTime()
    {
        var (subject, html) = EmailTemplate.DraftScheduled(
            "scheduled", "Test League", "Monday, June 16 at 7:00 PM UTC",
            TestLeagueId, FrontendUrl, Token);

        Assert.Equal("Draft scheduled — Test League", subject);
        Assert.Contains("Test League", html);
        Assert.Contains("Monday, June 16 at 7:00 PM UTC", html);
        Assert.Contains("View League", html);
    }

    [Fact]
    public void DraftUnscheduled_SubjectAndHtmlContainLeagueName()
    {
        var (subject, html) = EmailTemplate.DraftUnscheduled(
            "Test League", TestLeagueId, FrontendUrl, Token);

        Assert.Equal("Draft unscheduled — Test League", subject);
        Assert.Contains("Test League", html);
        Assert.Contains("View League", html);
    }

    [Fact]
    public void MemberJoined_SubjectAndHtmlContainMemberAndLeagueNames()
    {
        var (subject, html) = EmailTemplate.MemberJoined(
            "Alice", "Test League", TestLeagueId, FrontendUrl, Token);

        Assert.Equal("Alice joined Test League", subject);
        Assert.Contains("Alice", html);
        Assert.Contains("Test League", html);
        Assert.Contains("View League", html);
    }

    private static readonly Guid TestShowId = Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Fact]
    public void ScoresAvailable_SubjectAndHtmlContainShowNameAndRecapLink()
    {
        var (subject, html) = EmailTemplate.ScoresAvailable(
            "Drum Corps West", TestShowId, [], FrontendUrl, Token);

        Assert.Equal("New show scores available — Drum Corps West", subject);
        Assert.Contains("Drum Corps West", html);
        Assert.Contains($"/dci/recap/{TestShowId}", html);
        Assert.Contains("View Recap", html);
    }

    [Fact]
    public void ScoresAvailable_IncludesRankedScoresTable()
    {
        var results = new List<EmailScoreRow>
        {
            new(1, "Blue Devils", 96.85),
            new(2, "Bluecoats", 95.025),
        };

        var (_, html) = EmailTemplate.ScoresAvailable(
            "Drum Corps West", TestShowId, results, FrontendUrl, Token);

        Assert.Contains("Blue Devils", html);
        Assert.Contains("96.850", html);
        Assert.Contains("Bluecoats", html);
        Assert.Contains("95.025", html);
    }

    [Fact]
    public void ScoresAvailable_HtmlEncodesCorpsNameInScoresTable()
    {
        var results = new List<EmailScoreRow> { new(1, "<script>alert(1)</script>", 90.0) };

        var (_, html) = EmailTemplate.ScoresAvailable(
            "Drum Corps West", TestShowId, results, FrontendUrl, Token);

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void ScoresAvailable_NoResults_StillProducesValidEmail()
    {
        var (subject, html) = EmailTemplate.ScoresAvailable(
            "Drum Corps West", TestShowId, [], FrontendUrl, Token);

        Assert.Equal("New show scores available — Drum Corps West", subject);
        Assert.Contains("View Recap", html);
    }

    [Fact]
    public void EmailTemplate_HtmlEncodesUserContent()
    {
        var (_, html) = EmailTemplate.DraftTomorrow(
            "<script>alert(1)</script>", "Tuesday, June 16 at 7:00 PM EDT", TestLeagueId, FrontendUrl, Token);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void ScrapeFailed_SubjectAndHtmlContainShowNameAndError()
    {
        var (subject, html) = EmailTemplate.ScrapeFailed(
            "Drum Corps West", "HTTP request failed", TestSeasonId, FrontendUrl, Token);

        Assert.Equal("Scrape failed — Drum Corps West", subject);
        Assert.Contains("Drum Corps West", html);
        Assert.Contains("HTTP request failed", html);
        Assert.Contains($"/admin/seasons/{TestSeasonId}", html);
        Assert.Contains($"/unsubscribe?token={Token}", html);
    }
}
