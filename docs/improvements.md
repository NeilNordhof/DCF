# Improvements Backlog

Items noted for future implementation.

## UX / Frontend

- **FD-1** — **Countdown to draft time on LeagueDetail** — if the draft is scheduled (status = `Scheduled` and `draftStartTime` is set), show a live countdown on the league details page.

- **FD-2** — **Minimum player gate before draft open/start** — consider preventing the draft from opening if there aren't enough players (e.g. at least 2, or some configurable minimum).

- **FD-3** — **Auto-scroll DraftRoom on pick** — when a corps is fully drafted (all captions filled), automatically scroll the draft board down to keep the active row in view.

- **FD-4** — **Custom time picker for datetime fields** — replace the browser-native `datetime-local` input with a styled time picker component for any datetime settings (e.g. draft start time, show start time) to ensure consistent UX across browsers.

## Larger Features

- **FD-5** — **Email notifications** — notify users of relevant draft/league events via email (e.g. draft starting soon, it's your turn to pick, draft complete, scores updated). Requires an email provider integration (e.g. SendGrid, Mailgun, or SMTP), user notification preferences, and backend triggers wired into the existing draft and scoring flows.

- **FD-6** — **Timed draft picks** — league option to set a per-pick time limit (e.g. 60s, 2 min). If the timer expires, auto-skip the player (or auto-pick their top-ranked available corps). Requires a countdown visible in DraftRoom, server-side enforcement, and integration with the existing skip/makeup-pick logic.

## Testing

- **TI-1** — **Show creating and editing** — verify creating a new show and editing an existing show (name, start time, timezone, scores announced time, URL) works correctly end-to-end.

- **TI-2** — **Auto score scraping** — verify scores are automatically scraped after a show's scores announced time passes (including the configured delay buffer).

- **TI-3** — **Manual score scraping** — verify the admin `POST /api/admin/shows/{id}/scrape` trigger correctly fetches and populates score rows.

- **TI-4** — **Score computation** — verify standings are computed correctly from scraped scores across captions and drafted corps.

- **TI-5** — **Computed scores display** — verify the scores tab in LeagueDetail displays correct per-player breakdowns and rankings.
