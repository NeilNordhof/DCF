using System.Net;

namespace DCF.Api.Services;

public static class EmailTemplate
{
    public static (string subject, string html) DraftTomorrow(
        string leagueName,
        string timeStr,
        Guid leagueId,
        string frontendUrl,
        string unsubscribeToken)
    {
        var safe = WebUtility.HtmlEncode(leagueName);
        var safeTime = WebUtility.HtmlEncode(timeStr);

        return (
            $"Draft tomorrow — {leagueName}",
            Layout(
                heading: $"Draft tomorrow — {safe}",
                body: $"The <strong style=\"color: #f3f4f6;\">{safe}</strong> draft is tomorrow at <strong style=\"color: #f3f4f6;\">{safeTime}</strong>! Make sure you're ready to pick.",
                ctaText: "Go to Draft Room",
                ctaUrl: $"{frontendUrl}/leagues/{leagueId}/draft",
                unsubscribeUrl: $"{frontendUrl}/unsubscribe?token={unsubscribeToken}"));
    }

    public static (string subject, string html) DraftInOneHour(
        string leagueName,
        string timeStr,
        Guid leagueId,
        string frontendUrl,
        string unsubscribeToken)
    {
        var safe = WebUtility.HtmlEncode(leagueName);
        var safeTime = WebUtility.HtmlEncode(timeStr);

        return (
            $"Draft in 1 hour — {leagueName}",
            Layout(
                heading: $"Draft in 1 hour — {safe}",
                body: $"The <strong style=\"color: #f3f4f6;\">{safe}</strong> draft starts at <strong style=\"color: #f3f4f6;\">{safeTime}</strong> — that's in 1 hour!",
                ctaText: "Go to Draft Room",
                ctaUrl: $"{frontendUrl}/leagues/{leagueId}/draft",
                unsubscribeUrl: $"{frontendUrl}/unsubscribe?token={unsubscribeToken}"));
    }

    public static (string subject, string html) DraftRoomOpen(
        string leagueName,
        int openLeadMinutes,
        Guid leagueId,
        string frontendUrl,
        string unsubscribeToken)
    {
        var safe = WebUtility.HtmlEncode(leagueName);

        return (
            $"Draft room is open — {leagueName}",
            Layout(
                heading: $"Draft room is open — {safe}",
                body: $"The <strong style=\"color: #f3f4f6;\">{safe}</strong> draft room is now open! The draft starts in {openLeadMinutes} minutes.",
                ctaText: "Go to Draft Room",
                ctaUrl: $"{frontendUrl}/leagues/{leagueId}/draft",
                unsubscribeUrl: $"{frontendUrl}/unsubscribe?token={unsubscribeToken}"));
    }

    public static (string subject, string html) DraftScheduled(
        string action,
        string leagueName,
        string timeStr,
        Guid leagueId,
        string frontendUrl,
        string unsubscribeToken)
    {
        var safeName = WebUtility.HtmlEncode(leagueName);
        var safeAction = WebUtility.HtmlEncode(action);
        var safeTime = WebUtility.HtmlEncode(timeStr);

        return (
            $"Draft {action} — {leagueName}",
            Layout(
                heading: $"Draft {safeAction} — {safeName}",
                body: $"The <strong style=\"color: #f3f4f6;\">{safeName}</strong> draft has been {safeAction} for <strong style=\"color: #f3f4f6;\">{safeTime}</strong>.",
                ctaText: "View League",
                ctaUrl: $"{frontendUrl}/leagues/{leagueId}",
                unsubscribeUrl: $"{frontendUrl}/unsubscribe?token={unsubscribeToken}"));
    }

    public static (string subject, string html) DraftUnscheduled(
        string leagueName,
        Guid leagueId,
        string frontendUrl,
        string unsubscribeToken)
    {
        var safe = WebUtility.HtmlEncode(leagueName);

        return (
            $"Draft unscheduled — {leagueName}",
            Layout(
                heading: $"Draft unscheduled — {safe}",
                body: $"The <strong style=\"color: #f3f4f6;\">{safe}</strong> draft has been unscheduled. A new date will be set by the commissioner.",
                ctaText: "View League",
                ctaUrl: $"{frontendUrl}/leagues/{leagueId}",
                unsubscribeUrl: $"{frontendUrl}/unsubscribe?token={unsubscribeToken}"));
    }

    public static (string subject, string html) MemberJoined(
        string memberName,
        string leagueName,
        Guid leagueId,
        string frontendUrl,
        string unsubscribeToken)
    {
        var safeMember = WebUtility.HtmlEncode(memberName);
        var safeName = WebUtility.HtmlEncode(leagueName);

        return (
            $"{memberName} joined {leagueName}",
            Layout(
                heading: $"{safeMember} joined {safeName}",
                body: $"<strong style=\"color: #f3f4f6;\">{safeMember}</strong> has joined your league <strong style=\"color: #f3f4f6;\">{safeName}</strong>.",
                ctaText: "View League",
                ctaUrl: $"{frontendUrl}/leagues/{leagueId}",
                unsubscribeUrl: $"{frontendUrl}/unsubscribe?token={unsubscribeToken}"));
    }

    public static (string subject, string html) ScoresAvailable(
        string showName,
        string frontendUrl,
        string unsubscribeToken)
    {
        var safe = WebUtility.HtmlEncode(showName);

        return (
            $"New show scores available — {showName}",
            Layout(
                heading: "New scores available",
                body: $"Scores from <strong style=\"color: #f3f4f6;\">{safe}</strong> are now available. Check your standings!",
                ctaText: "View Standings",
                ctaUrl: $"{frontendUrl}/leagues",
                unsubscribeUrl: $"{frontendUrl}/unsubscribe?token={unsubscribeToken}"));
    }

    public static (string subject, string html) ScrapeFailed(
        string showName,
        string errorMessage,
        Guid seasonId,
        string frontendUrl,
        string unsubscribeToken)
    {
        var safeName = WebUtility.HtmlEncode(showName);
        var safeError = WebUtility.HtmlEncode(errorMessage);

        return (
            $"Scrape failed — {showName}",
            Layout(
                heading: "Scrape failed",
                body: $"Scraping scores for <strong style=\"color: #f3f4f6;\">{safeName}</strong> failed after multiple attempts: {safeError}. A manual re-trigger may be needed.",
                ctaText: "View Show",
                ctaUrl: $"{frontendUrl}/admin/seasons/{seasonId}",
                unsubscribeUrl: $"{frontendUrl}/unsubscribe?token={unsubscribeToken}"));
    }

    private static string Layout(
        string heading,
        string body,
        string ctaText,
        string ctaUrl,
        string unsubscribeUrl)
    {
        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="UTF-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            </head>
            <body style="margin: 0; padding: 20px; background-color: #0d0f14; font-family: Arial, Helvetica, sans-serif;">
              <table width="100%" cellpadding="0" cellspacing="0" role="presentation">
                <tr>
                  <td align="center">
                    <table cellpadding="0" cellspacing="0" role="presentation"
                           style="max-width: 560px; width: 100%; background-color: #161822; border: 1px solid #2a2d3a; border-radius: 8px; overflow: hidden;">
                      <tr>
                        <td style="padding: 24px 32px; border-bottom: 1px solid #2a2d3a;">
                          <p style="margin: 0; font-size: 14px; font-weight: bold; color: #c084fc; letter-spacing: 0.08em; text-transform: uppercase;">
                            Drum Corps Fantasy
                          </p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding: 32px;">
                          <h1 style="margin: 0 0 16px; font-size: 20px; font-weight: bold; color: #f3f4f6; line-height: 1.3;">
                            {heading}
                          </h1>
                          <p style="margin: 0 0 28px; font-size: 14px; line-height: 1.6; color: #9ca3af;">
                            {body}
                          </p>
                          <table width="100%" cellpadding="0" cellspacing="0" role="presentation">
                            <tr>
                              <td align="center">
                                <table cellpadding="0" cellspacing="0" role="presentation">
                                  <tr>
                                    <td style="border-radius: 6px; background-color: #c084fc;">
                                      <a href="{ctaUrl}"
                                         style="display: inline-block; padding: 12px 24px; font-size: 14px; font-weight: bold; color: #0d0f14; text-decoration: none; border-radius: 6px;">
                                        {ctaText}
                                      </a>
                                    </td>
                                  </tr>
                                </table>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding: 20px 32px; border-top: 1px solid #2a2d3a; text-align: center;">
                          <p style="margin: 0; font-size: 12px; color: #6b7280;">
                            <a href="{unsubscribeUrl}" style="color: #6b7280; text-decoration: underline;">Unsubscribe</a> from email notifications
                          </p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }
}
