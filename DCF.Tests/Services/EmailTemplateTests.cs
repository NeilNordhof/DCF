using DCF.Api.Services;
using Xunit;

namespace DCF.Tests.Services;

public class EmailTemplateTests
{
    private static readonly Guid TestLeagueId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string FrontendUrl = "http://app.test";
    private const string Token = "test-token";

    [Fact]
    public void DraftTomorrow_SubjectAndHtmlContainLeagueName()
    {
        var (subject, html) = EmailTemplate.DraftTomorrow(
            "Test League", TestLeagueId, FrontendUrl, Token);

        Assert.Equal("Draft tomorrow — Test League", subject);
        Assert.Contains("Test League", html);
        Assert.Contains($"/leagues/{TestLeagueId}/draft", html);
        Assert.Contains($"/unsubscribe?token={Token}", html);
        Assert.Contains("Go to Draft Room", html);
    }

    [Fact]
    public void DraftInOneHour_SubjectAndHtmlContainLeagueName()
    {
        var (subject, html) = EmailTemplate.DraftInOneHour(
            "Test League", TestLeagueId, FrontendUrl, Token);

        Assert.Equal("Draft in 1 hour — Test League", subject);
        Assert.Contains("Test League", html);
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

    [Fact]
    public void ScoresAvailable_SubjectAndHtmlContainShowName()
    {
        var (subject, html) = EmailTemplate.ScoresAvailable(
            "Drum Corps West", FrontendUrl, Token);

        Assert.Equal("New show scores available — Drum Corps West", subject);
        Assert.Contains("Drum Corps West", html);
        Assert.Contains("/leagues", html);
        Assert.Contains("View Standings", html);
    }

    [Fact]
    public void EmailTemplate_HtmlEncodesUserContent()
    {
        var (_, html) = EmailTemplate.DraftTomorrow(
            "<script>alert(1)</script>", TestLeagueId, FrontendUrl, Token);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}
