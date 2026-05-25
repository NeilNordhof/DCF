# DCF Fantasy — Site Design Spec

## Overview

DCF Fantasy is a fantasy league app for Drum Corps International fans. The design direction is a **dark sports app** — deep dark backgrounds, a purple accent, stat-forward typography, and real-time draft interactions. The closest analogues are DraftKings and ESPN's dark mode.

All pages share a single top nav bar and a fixed max-width container. Light/dark mode toggling is not a feature — the app is always dark.

---

## Design System

### Colour Tokens

| Token | Light value | Usage |
|---|---|---|
| `--bg` | `#0d0f14` | Page background |
| `--surface` | `#161822` | Cards, nav, panels |
| `--surface-2` | `#0f1117` | Inset panels, side panels |
| `--border` | `#2a2d3a` | All borders |
| `--border-subtle` | `#1e2030` | Subtle separators |
| `--text` | `#9ca3af` | Body / secondary text |
| `--text-h` | `#f3f4f6` | Headings, primary text |
| `--text-muted` | `#6b7280` | Labels, metadata |
| `--text-faint` | `#4b5563` | Placeholder, disabled |
| `--accent` | `#c084fc` | Purple — primary accent |
| `--accent-bg` | `#3b0764` | Purple tinted backgrounds |
| `--accent-border` | `#c084fc55` | Purple borders |
| `--green` | `#4ade80` | Available cells, joined indicators |
| `--green-bg` | `#052e16` | Green tinted backgrounds |
| `--green-border` | `#166534` | Green borders |

### Typography

System-ui stack throughout (`system-ui, 'Segoe UI', Roboto, sans-serif`). No external font dependency for now — revisit later if desired.

- **Page headings**: 13–15px, weight 800, `--text-h`
- **Section labels**: 8–9px, uppercase, letter-spacing 0.5–1px, `--text-faint`
- **Body**: 10–11px, `--text`
- **Stats / scores**: 12–16px, weight 700–900, accent or `--text-h`
- **Monospace** (invite codes, URLs): `ui-monospace, Consolas`

### Spacing

4px base unit. Common values: 4, 6, 8, 10, 12, 14, 16, 20, 24, 32px.

### Component Patterns

**Buttons**
- Primary: `bg: --accent`, `color: #0d0f14`, `border-radius: 5–6px`, `font-weight: 800`, `letter-spacing: 0.5px`, uppercase label optional
- Secondary / ghost: `bg: --surface`, `border: 1px solid --border`, `color: --text`
- Disabled: `bg: --border`, `color: --text-faint`, `cursor: not-allowed`

**Inputs**
- `bg: --bg`, `border: 1px solid #3d3f4e`, `border-radius: 5px`, `color: --text-h`, `padding: 7–8px 10px`
- Focus: `border-color: --accent`

**Cards / rows**
- `bg: --surface`, `border: 1px solid --border`, `border-radius: 5–6px`
- Highlighted (yours / active): `bg: --accent-bg`, `border: 1px solid --accent-border`
- Muted / completed: `bg: #0f1117`, opacity 0.65, `border: 1px solid --border-subtle`

**Status badges**
- Live / Active: green bg + green border, uppercase, weight 700
- Scheduled / Open: `border: 1px solid --border`, `color: --text-muted`
- Completed: `bg: --surface`, `color: --text-faint`

**Toggle chips** (captions, corps)
- Selected: `bg: --accent-bg`, `border: 1px solid --accent`, `color: --text-h`, weight 600
- Unselected: `bg: --surface`, `border: 1px solid --border`, `color: --text-muted`

**Section labels**
- Always uppercase, 8px, letter-spacing 0.5–1px, `--text-faint`

**Tabs** (reused across League Detail, Admin, Draft Room side panel)
- Active: `color: --accent`, `border-bottom: 2px solid --accent`
- Inactive: `color: --text-muted`
- Container: `bg: --surface`, `border-bottom: 1px solid --border`

---

## Navigation

A single top nav bar sits at the top of every authenticated page.

```
[ DCF FANTASY ]          [ LEAGUES ]  [ PROFILE ]  [ avatar ]
```

- Bar: `bg: --surface`, `border-bottom: 1px solid --border`, height 40–44px
- Logo: `color: --accent`, weight 700, letter-spacing 0.5px
- Nav links: 11px, `color: --text-muted`; active link: `color: --accent`, `border-bottom: 2px solid --accent`
- Avatar: 28px circle, `bg: --accent`, initials in `#0d0f14`
- Admin users see an `ADMIN` badge (small, `bg: #374151`, uppercase) next to the logo when on admin pages

The landing page (unauthenticated) has its own minimal nav — just the logo and a Sign In button. No persistent nav is shown.

---

## Pages

### 1. Landing (unauthenticated) — `/`

**Layout:** centred split card floating on a dark page with a subtle purple radial glow behind it. Not full-width — max ~780px wide, centred with auto margins, vertically centred on the viewport.

**Left panel** (brand pitch):
- Background: `linear-gradient(135deg, #1a0e2e, --surface)`
- DCF logotype: large, weight 900, `--accent`; "Fantasy" subtitle in small uppercase `--text-faint`
- Headline: "Draft corps. Score points. Win the season." — 17–20px, weight 800
- Sub-copy: brief description of the app
- Feature bullet list: 3 bullets (snake draft / real DCI scores / private leagues), each with a small purple dot

**Right panel** (Auth0 Lock):
- `bg: #0f1117`
- Auth0 Lock widget rendered into a container div
- Lock configured with `theme: { primaryColor: '#c084fc' }` and dark colour scheme
- "Secured by Auth0" footer always present (Lock requirement)
- Lock handles email/password + Google social login

**Auth0 Lock implementation note:** install `auth0-lock` package and render the Lock instance into a `#lock-container` div inside the right panel. On successful authentication, Auth0 redirects back to the app and the normal `useAuth0` session takes over. Do not use `loginWithRedirect` from `@auth0/auth0-react` on this page — Lock handles the flow independently.

**Mobile:** Left panel collapses/hides, Lock panel fills the screen.

---

### 2. Leagues — `/leagues`

**Layout:** single column, max-width page content area.

**Featured league card** (top): shown when the user has an active or live-draft league.
- Background: `linear-gradient(135deg, #1e1230, --surface)`, purple border
- League name (large, weight 800), status badge (e.g. `LIVE DRAFT`)
- Three stat tiles: Rank / Points / Members
- Prominent "Draft Room →" button when status is `InProgress`

**Other leagues list** (below featured):
- Section label: "Other Leagues"
- Each league: `bg: --surface`, single row — name left, member count + rank right, chevron `›`

**Create button:** `+ New` button top-right of the page, `bg: --accent`

**Empty state:** if the user has no leagues, show a centred prompt to create or join one.

---

### 3. League Detail — `/leagues/:id`

**Layout:** league header bar + tab row + tab content below.

**Header bar** (`bg: --surface`, border-bottom):
- League name (weight 800), season year + member count below
- Status badge right-aligned
- "Draft Room →" button (`bg: --accent`) when draft is scheduled or live
- "Join League" button for non-members

**Tabs (in order):** `Home · Scores · Members · Picks · Info`

#### Home tab
Current content: ranked standings list. Future: may add recent show results, upcoming events, announcements.

Standings list:
- Each row: rank number (purple for 1st), display name, score right-aligned
- Your own row: `bg: --accent-bg`, `border: --accent-border`
- Rank 1 number in `--accent`, others in `--text-muted`

#### Scores tab
Faithful reproduction of the spreadsheet layout, dark-styled.

- Horizontally scrollable table
- **Player column**: sticky left, `min-width: 80px`; your row `bg: #130d1f`, others `bg: --surface`
- **Caption group headers**: span 3 columns (Corps / Score / Avg), uppercase label
- **Sub-headers**: Corps, Score, Avg — repeated per caption
- **Data rows**: 3 rows per player (one per drafted corps in that caption)
- **Avg column**: right-aligned per caption group; `color: --accent` for your row, `--text-muted` for others
- **Total column**: not sticky (scrolls with table); 12px, weight 900; `--accent` for your row, `--text-h` for others
- **Player gap rows**: 6px dark gap between each player group (`bg: --bg`)
- Caption group separated by `border-right: 1px solid --border-subtle`

#### Members tab
Simple list of member display names. Commissioner indicated with a small label.

#### Picks tab
Shows every member's full drafted roster. Same structure as the Draft Room side panel Picks tab (see §Draft Room): player switcher at top, sections grouped by caption with count badges, filled picks as cards, empty slots as dashed placeholders. On the League Detail page all slots will typically be filled (post-draft), so dashed placeholders only appear if the draft is still in progress.

#### Info tab
- Invite code (if private): displayed in monospace, `color: --accent`, with a copy button
- Draft settings: captions, corps-per-caption, draft time

---

### 4. Draft Room — `/leagues/:id/draft`

Single page with three distinct states driven by MQTT `draftState.status`. No page navigation between states — the UI transitions in place.

#### Scheduled / Lobby state

**Top bar** (replaces "On the Clock" banner):
- `background: linear-gradient(90deg, #0f1a0f, #101810)`, `border-bottom: 2px solid --green-border`
- Label: "Draft Begins In" in `--green`, uppercase
- Live countdown: `HH : MM : SS` — large (26px), weight 900, `--text-h`; colons in `--green`
- Right side: scheduled date/time string, league name
- Commissioner only: "Start Early" ghost button (`border: 1px solid --green-border`, `color: --green`)

**Pick grid** (left panel):
- Rendered at full opacity but non-interactive (`pointer-events: none`, opacity ~0.45)
- All cells shown as empty/locked (`bg: --surface`, `border: --border`) — no green/taken state yet
- Submit bar: faded and `cursor: not-allowed`
- Label above grid: "Pick board locks until the draft begins"

**Side panel** (right panel):
- Draft Order tab active by default
- Shows full pick order for round 1, then round 2 labelled "(snake reversal)" at reduced opacity
- No completed picks section yet

#### In-progress state

**Top bar** ("On the Clock"):
- `background: linear-gradient(90deg, #2e1065, #1a1535)`, `border-bottom: 2px solid --accent`
- "On the Clock" label in `--accent`, uppercase
- Current drafter name: 15px, weight 800, `--text-h`
- Right: "Round N · Pick N" and league name
- When it is NOT your turn: bar still shows but label changes to "Now Picking" and the drafter name is shown without highlighting

**Pick grid** (left panel, scrollable):
- Corps rows × caption columns
- **Cell states:**
  - Available: `bg: --green-bg`, `border: 1px solid --green-border`, green dot `●` centred
  - Taken: `bg: #12141a`, `border: 1px solid --border-subtle`, `—` in `--border` colour, `cursor: not-allowed`
  - Selected (click to choose): `bg: --accent-bg`, `border: 2px solid --accent`, star `★` centred, `box-shadow: 0 0 10px --accent-bg`
- **Cell size:** 44×44px squares as the default. In leagues with 3 or fewer captions the cells widen to maintain readability but cap at 2:1 width:height (88×44px max). Grid scrolls vertically for long corps lists; scrolls horizontally for wide caption sets.
- **Column headers:** caption names, 8–9px uppercase, `--text-muted`
- **Row labels:** corps names, right-aligned, 10px weight 600, `--text-h`
- Grid non-interactive when it is not your turn (cells still visible, no pointer events)

**Submit bar** (below grid):
- `bg: --surface`, `border: 1px solid --accent-border`, `border-radius: 6px`
- Left: "Selected" label + "Corps · Caption" value
- Right: "SUBMIT PICK" primary button — disabled (grey) until a cell is selected

**Side panel** (right, tabbed):

*Draft Order tab:*
- Chronological top-to-bottom: completed picks at top (faded, show pick number + drafter + selection), then current pick (purple highlight), then upcoming picks below ("Up Next" section label)
- Pick number shown as a small numbered circle

*Picks tab:*
- Player switcher: pill buttons at top showing first names; active player is `bg: --accent`
- Below: sections grouped by caption
- Caption heading: caption name uppercase + count badge (`1 / 2` format); badge is purple-tinted when at least one pick exists, grey at 0
- Filled picks: card with pick-number circle (`#N` in `--accent`) + corps name + "Pick #N overall" subtitle
- Empty slots: dashed border card, "Empty" italic in `--text-faint`

#### Completed state

Top bar: "Draft Complete" label in `--text-muted`, no countdown or drafter info.

Pick grid: all cells show as taken (no green cells). Non-interactive.

Side panel Picks tab: all slots filled, no empty dashes.

---

### 5. Create League — `/leagues/create`

**Layout:** single-page form, max-width ~480px, left-aligned under the nav.

Back link: `← Back to Leagues` top-right of nav area.

**Fields (in order):**

1. **League Name** — text input
2. **Visibility** — two-button toggle: `Public` / `Private` (selected state: `bg: --accent-bg`, `border: --accent`, weight 700)
3. **Captions** — toggle chip grid; chips are the available caption names (GE1, GE2, Brass, Guard, Perc, Visual etc.); click to select/deselect; selected = purple chip, unselected = grey
4. **Corps per Caption** — `−` / value / `+` stepper; below shows "= N total picks" computed from `corpsPerCaption × selectedCaptions.length`
5. **Draft Start** — datetime input, labelled as optional; placeholder "Pick a date and time…"

**Submit:** full-width "CREATE LEAGUE" primary button at the bottom.

**Post-creation:** redirect to the new league's detail page. If private, the invite code is shown in the Info tab.

---

### 6. Admin — `/admin`

Admin badge (small grey chip reading "ADMIN") appears next to the logo in the nav when on any admin page.

**Layout:** tabbed page, same tab pattern as League Detail.

**Tabs:** `Seasons · Corps`

#### Seasons tab

Seasons table:
- Columns: Year / Dates / Status / action
- Status badges: Active (green), Published (small green text badge), Completed (grey), Upcoming (grey)
- "Manage →" link in `--accent` navigates to Season Detail

Add Season form (below table, inside a `--surface` card):
- Three inline inputs: Year / Start date / End date
- "Add Season" primary button

#### Corps tab

Corps list:
- Simple list of corps names, one per row in `--surface` cards

Add Corps form (below list, inside a `--surface` card):
- Single text input for corps name
- "Add Corps" primary button

---

### 7. Season Detail — `/admin/seasons/:id`

**Layout:** season header bar + two-panel body.

**Header bar:**
- Season year (weight 800), date range + status below
- Status badges: Published (green), Upcoming/Active (grey/green)
- Publish button: shown only when not yet published and at least one corps is selected; primary button "Publish"

**Left panel — Corps checklist:**
- Section label: "Corps this season"
- Each corps: toggle row with a checkbox-style indicator (green checked = included, empty = excluded)
- Locks to read-only once season is published — all checkboxes disabled, "Save Corps" replaced with "Locked (published)" disabled button
- "Save Corps" primary button when editable

**Right panel — Shows:**
- Shows list: each show as a card — show name (weight 600) + date + "Scores at HH:MM" subtitle
- Add Show form (inside a `--surface` card below the list):
  - Inputs: Show name, DCI recap URL, Date, Scores announced time (datetime-local)
  - Participating corps: toggle chips from the season's corps list (same chip pattern as Create League captions)
  - "Add Show" primary button

---

## State Transitions (Draft Room)

The MQTT topic `dcf/leagues/{id}/draft` pushes a `DraftState` object. The UI reacts:

| `draftState.status` | Top bar | Grid | Submit bar |
|---|---|---|---|
| `NotStarted` / `Scheduled` | Green countdown | Locked, faded | Disabled |
| `Open` | Green countdown | Locked, faded | Disabled |
| `InProgress` (your turn) | Purple "On the Clock" + your name | Interactive | Enabled when cell selected |
| `InProgress` (other's turn) | Purple "Now Picking" + their name | Visible, non-interactive | Disabled |
| `Completed` | Neutral "Draft Complete" | All taken, non-interactive | Hidden |

No page reload is required between states — MQTT messages trigger React state updates that re-render the top bar and unlock/lock the grid in place.

---

## Out of Scope

- Mobile-specific layouts (responsive behaviour is a follow-up)
- Dark/light mode toggle
- Profile page design (deferred)
- Onboarding page design (deferred)
- Animation / transition specs
