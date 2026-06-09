# Improvements Backlog

Items noted for future implementation.

## UX / Frontend

- **FD-1** — **Countdown to draft time on LeagueDetail** — if the draft is scheduled (status = `Scheduled` and `draftStartTime` is set), show a live countdown on the league details page.

- **FD-2** — **Minimum player gate before draft open/start** — consider preventing the draft from opening if there aren't enough players (e.g. at least 2, or some configurable minimum).

- **FD-3** — **Auto-scroll DraftRoom on pick** — when a corps is fully drafted (all captions filled), automatically scroll the draft board down to keep the active row in view.

- **FD-4** — **Custom time picker for datetime fields** — replace the browser-native `datetime-local` input with a styled time picker component for any datetime settings (e.g. draft start time, show start time) to ensure consistent UX across browsers.

- **FD-9** - **User Corps Names in Leagues** - allow users to define names for their "corps" in each league they are in, similar to users naming their team in fantasy football

- **FD-10** - **League Continuation** - Once a season ends and a new one begins, let comisioners continue their past season's league for the new season with the same users.

- **FD-11** - **League User Managament** - Allow a commissioner to remove users from their league, and allow naming of co-commisioners or passing commisioner to another user.

- **FD-12** — **Sort and search My Leagues** — add sorting (e.g. by name, draft date, status) and a search/filter input to the My Leagues tab so users can quickly find a league when they belong to many.

- **FD-13** — **Sort and search Season Shows (admin)** — add sorting (e.g. by date, name) and a search/filter input to the shows list on the admin Season Detail page.

## Larger Features

- **FD-5** — **Email notifications** — notify users of relevant draft/league events via email (e.g. draft starting soon, it's your turn to pick, draft complete, scores updated). Requires an email provider integration (e.g. SendGrid, Mailgun, or SMTP), user notification preferences, and backend triggers wired into the existing draft and scoring flows.

- **FD-6** — **Timed draft picks** — league option to set a per-pick time limit (e.g. 60s, 2 min). If the timer expires, auto-skip the player (or auto-pick their top-ranked available corps). Requires a countdown visible in DraftRoom, server-side enforcement, and integration with the existing skip/makeup-pick logic.

- **FD-7** — **Historical computed scores** — allow users to view season-long progress of scores and ranking. A chart or table showing how each player's total fantasy score and league rank changed over time as shows were scored throughout the season.

- **FD-8** — **DCI schedule, scores, and rankings viewer** — a public page (no league required) showing the actual DCI competition schedule, official scores, and corps rankings for the current season, plus season-long progress charts mirroring the league historical view (FD-7) but for real DCI standings.

## Bugs

- **BF-2** — **Show status stays "Started" after scores announced** — on the admin page, a show's status does not advance to reflect that scores have been announced; it remains stuck on "Started".

- **BF-3** — **Successful scrape displays as "Scrape trigger failed"** — manually triggering a scrape from the admin page shows a failure message even when the scrape completes successfully.

## Infrastructure

- **IF-1** — **Third-party logging integration** — ship structured logs to an external provider (e.g. Datadog, Seq, or similar) for production observability. Requires wiring a Serilog sink (or equivalent) into the ASP.NET Core logging pipeline and configuring log levels, enrichers (request ID, user ID, environment), and any sensitive-field redaction.

## Testing

- **TI-6** - **Auth0 Lock flow** = Verify Auth0 lock flow works with strict mode disabled (akin to production environment).