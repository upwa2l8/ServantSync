# ServantSync

![ServantSync marketing lockup](wwwroot/img/servantsync-marketing.svg)

A multi-organization volunteer-scheduling platform built on Blazor Server +
ASP.NET Core Identity + EF Core / SQLite. Designed to serve churches first
and to be extensible to other scheduling use cases (sports leagues, on-call
rotas, conference staffing) without a schema rewrite.

The two brand assets in this repo are intentionally complementary:

  * `wwwroot/img/servantsync-marketing.svg` — the typographic wordmark above.
    Marketing surface (README, splash page, in-app empty states, email header).
    Transparent canvas, brand-purple gradient, composes on dark or light
    backgrounds. NOT used as the navbar/favicon.
  * `wwwroot/img/servantsync-mark.svg` — the existing heart-shaped two-figure
    mark with sync arcs. The product brand: navbar, favicon, apple-touch-icon.
    Same brand-purple + amber-rose gradient (cool ↔ warm duality).

Both are centralized through `Components/Shared/WordmarkSplash.razor` on the
web (the single source of truth for splash sites on Login, Register, Home,
Open, and MySchedule) and as a typographic text lockup in
`Services/MailKitEmailSender.cs` -> `EmailBranding.WrapHtmlBody` for outbound
email. See `BRANDING.md` for the asset inventory, color tokens, and the
why-two-assets rationale.

No religious symbols in either asset by design (no crosses, no liturgical
ornamentation) so the wordmark composes well on secular surfaces like
on-call rotas and conference staffing, not just church contexts.

## Documentation map

| File         | Audience                  | Purpose                                                    |
|--------------|---------------------------|------------------------------------------------------------|
| `README.md`  | End users, new contributors | How to run, what's seeded, end-to-end test plan, feature inventory, configuration. |
| `PLAN.md`    | Anyone asking "what was the original spec?" | The original product plan, with current build state per item. |
| `STATUS.md`  | Future-AI sessions, returning contributors   | Working state, known quirks, pending work, architectural notes, file map. Read this first on every new session. |

---

## Quick start

```bash
# 1. Restore + build
dotnet restore
dotnet build

# 2. Run the app (seeds the SQLite database on first launch)
dotnet run

# 3. Open the URL the launch profile prints (default https://localhost:7012
#    or http://localhost:5050) and sign in as one of the seeded users below.
```

To **force a re-seed**, delete `servantsync.db` at the repo root and run
again. The seeder is idempotent — it skips when the `Organizations` table
has any rows.

To **run the unit tests**:

```bash
dotnet test
```

To **run an EF migration** (only after schema changes):

```bash
dotnet ef migrations add <Name>
dotnet ef database update
```

---

## Seeded users

All passwords are `Passw0rd!`.

| Email                       | Display name    | Role in Demo Church | Notes                                              |
|-----------------------------|-----------------|---------------------|----------------------------------------------------|
| `admin@demo.local`          | Alex Admin      | **Admin**           | Can manage the org, its ministries, and its users. |
| `coordinator@demo.local`    | Chris Coordinator | **Coordinator**   | Can manage the org's scheduling surface only.      |
| `volunteer@demo.local`      | Vee Volunteer   | **Volunteer**       | Safe-Spaces compliant (completed 1 month ago).     |
| `volunteer2@demo.local`     | Val Walker      | **Volunteer**       | Safe-Spaces compliant (completed 2 months ago).    |
| `league-admin@demo.local`   | Logan League-Admin | **Admin**        | League admin for Springfield Youth Soccer.        |
| `coach1@demo.local`         | Casey Coach     | **Coordinator**     | Coach of both U10 Boys teams.                       |
| `coach2@demo.local`         | Morgan Coach    | **Coordinator**     | Coach of both U12 Girls teams.                      |
| `ref1@demo.local`           | Riley Referee   | **Volunteer**       | Concussion-compliant (completed 2 months ago).     |
| `ref2@demo.local`           | Quinn Referee   | **Volunteer**       | Concussion-compliant (completed 1 month ago).      |
| `devotion@demo.local`       | Drew Devotion   | **Volunteer**       | Pre-game devotion leader.                          |
| `concession@demo.local`     | Sam Concessions | **Volunteer**       | Concession stand on game days.                     |
| `parent1@demo.local`        | Pat Parent      | **Volunteer**       | Primary contact for 8 of the 16 seeded players.    |
| `parent2@demo.local`        | Jordan Parent   | **Volunteer**       | Primary contact for the other 8.                   |

`SignIn.RequireConfirmedAccount` is `false` in `Program.cs`, so registering
a new account via **Register** does not require email confirmation. Use
this to create non-compliant test accounts (see "End-to-end test plan"
below).

---

## Seeded domain data

**Organization** — `Demo Church` (Springfield, `office@demo.local`).

**Ministries**

| Name                  | Description                       | Coordinator email      |
|-----------------------|-----------------------------------|------------------------|
| Worship Team          | Sets the tone for Sunday services. | `worship@demo.local`   |
| Children's Ministry   | Sunday school and nursery care.   | `kids@demo.local`      |

**Service slots**

| Name                   | Ministry            | Location     | Default duration |
|------------------------|---------------------|--------------|------------------|
| Sunday Sound Tech      | Worship Team        | Sound booth  | 120 min          |
| Sunday Vocals          | Worship Team        | Stage        | 90 min           |
| Sunday School Helper   | Children's Ministry | Room 204     | 75 min           |

**Training**

- Content: `Safe Spaces & Child Protection` (2026 Edition, ~45 min video).
- Two requirements both pointing at this content:
  - **Org-wide**, Yearly cadence — every assigned volunteer in Demo Church
    must complete it.
  - **Slot-specific** on `Sunday School Helper`, Yearly cadence.

**Assignments** (six rows across two Sundays, all `Scheduled`)

| When (UTC)            | Slot                   | Person                | Notes                                       |
|-----------------------|------------------------|-----------------------|---------------------------------------------|
| This Sunday 14:00     | Sunday Sound Tech      | Vee Volunteer         |                                             |
| This Sunday 14:00     | Sunday Vocals          | Val Walker            |                                             |
| This Sunday 14:00     | Sunday School Helper   | Vee Volunteer         | Slot has its own training requirement too.  |
| Next Sunday 14:00     | Sunday Sound Tech      | Val Walker            |                                             |
| Next Sunday 14:00     | Sunday Vocals          | Vee Volunteer         |                                             |
| Next Sunday 14:30     | Sunday Sound Tech      | Vee Volunteer         | **Intentional overlap** with the next row.  |

The 14:00/14:30 overlap on next Sunday between Vee's two assignments
populates the **Person Overlaps** KPI on the dashboard on first load and
is also visible as a red list-view row + a flagged calendar pill.

Times are stored in UTC. The seed writes 14:00 UTC, which is the typical
late-morning Sunday-service slot (10:00 EDT / 07:00 PDT). The
`UserTimeZoneProvider` + `wwwroot/js/timezone.js` detect the browser's
IANA zone on first load and route the read-side through
`Components/Shared/LocalTime.razor` so every display on the page
(MySchedule list + calendar pills, Dashboard daily feed, slot-detail
upcoming assignments, league / team / game pages) renders in the
viewer's local time. Calendar day-bucketing and the dashboard's day
grouping also bucket by the user's local date, not UTC, so a Sunday-
evening service that is stored as "2025-01-12 03:00Z" appears under
Jan 11 for an EST viewer throughout the app.

### Springfield Youth Soccer League (sub-ministries, arenas, teams, games)

The same Demo Church org also owns a `Springfield Youth Soccer League`
ministry with three sub-ministries (each with its own coordinator via
`Ministry.CoordinatorPersonUserId`):

| Sub-ministry | Coordinator      | Slots under it                                                  |
|--------------|------------------|-----------------------------------------------------------------|
| `Referees`   | Riley Referee    | `Game-Day Referee` (Concussion training required)               |
| `Concessions`| Sam Concessions  | `Concession Stand Worker`                                       |
| `Devotion`   | Drew Devotion    | `Devotion Leader` (pre-game devotion)                           |

Plus four coaching slots under the league ministry itself: `U10 Boys
Head Coach`, `U10 Boys Asst Coach`, `U12 Girls Head Coach`, `U12 Girls
Asst Coach`. All seven slots are **regular ServiceSlots** that reuse the
existing `Assignment` + training-gate + conflict-detection pipeline.

**Playing arenas** (org-scoped, shared by every league in the org;
managed on the org's "Arenas" tab):

| Arena   | Surface   | Capacity | Notes                          |
|---------|-----------|----------|--------------------------------|
| Field 1 | Grass     | —        | Used by both divisions         |
| Field 2 | Grass     | —        |                                |
| Gym A   | Hardwood  | 300      |                                |

**Teams** (all in the soccer ministry):

| Team   | Age bracket | Gender | Coach         | Players |
|--------|-------------|--------|---------------|---------|
| Eagles | U10         | Boys   | Casey Coach   | 4       |
| Hawks  | U10         | Boys   | Casey Coach   | 4       |
| Comets | U12         | Girls  | Morgan Coach  | 4       |
| Stars  | U12         | Girls  | Morgan Coach  | 4       |

**Players** (16 total, 4 per team) carry a `JerseyNumber`, optional
`Position` (free-form, sport-agnostic), and a `PrimaryContactPersonUserId`
linking to one of the two seeded parent accounts. The phone/email on the
player record is denormalized from the parent so coaches can call a
parent without a join.

**Games** (6 across last Saturday, this Saturday, next Saturday):

| When (UTC)             | Home   | Away   | Arena  | Status    | Score | Notes                                          |
|------------------------|--------|--------|--------|-----------|-------|------------------------------------------------|
| Last Sat 9:00–10:00    | Eagles | Hawks  | Field 1 | Played   | 3–1   | Counts for standings.                          |
| This Sat 9:00–10:00   | Comets | Stars  | Field 2 | Scheduled | —    |                                                |
| This Sat 10:00–11:00  | Eagles | Comets | Field 1 | Scheduled | —    |                                                |
| This Sat 11:00–12:00  | Hawks  | Stars  | Gym A   | Scheduled | —    |                                                |
| Next Sat 9:00–10:00   | Eagles | Hawks  | Field 1 | Scheduled | —    | **Intentional arena double-book**              |
| Next Sat 9:00–10:00   | Comets | Stars  | Field 1 | Scheduled | —    | with the row above — tests `GameService`.      |

Game times are 9:00 UTC. The intentional double-book (last two rows)
is inserted directly by the seeder; the `GameService.ScheduleGameAsync`
path rejects any attempt to add a *new* conflicting game through the UI.

**Concussion training** (additional training surface for the league):

- Content: `Concussion & Head Injury Awareness` (2026 Edition, ~20 min
  video).
- Requirement: **Game-Day Referee** (slot-scoped, Yearly cadence).
- Completions: `Riley Referee` and `Quinn Referee` are both compliant
  (completed 1–2 months ago, valid for ~10+ months).

**Volunteer-shift assignments** (the existing `Assignment` pipeline):

| When (UTC)             | Person        | Slot             | Status    |
|------------------------|---------------|------------------|-----------|
| Last Sat 9:00–10:00    | Riley Referee | Game-Day Referee | Completed |
| This Sat 9:00–12:00   | Quinn Referee | Game-Day Referee | Scheduled |
| Next Sat 9:00–11:00   | Riley Referee | Game-Day Referee | Scheduled |

---

## End-to-end test plan

The seeded data is engineered so the recently-built features can be
exercised in a single sitting without further setup. Suggested order:

### 1. Sign in as the admin and explore the dashboard
- Go to **Dashboard** in the nav.
- KPIs should read roughly: 6 assignments, 2 unique volunteers, **2
  person overlaps**, 0 outstanding trainings. (The "overlaps" KPI
  counts conflicting rows, not unique people — Vee's two next-Sunday
  assignments overlap each other, so **both** rows are flagged. This is
  intentional so the red row is immediately visible in the list view.)
- Default range is **14 days**. Try the other chip presets:
  - **Today** — empty (Sunday hasn't started yet in the user's wall clock).
  - **7 days** — drops one of the Sundays.
  - **30 days** — same 6, but the window is wider.
  - **90 days** — same.
  - **All** — same.
  - **Custom** — pick a window that includes both Sundays.
- Toggle **Calendar** view. The two Sundays show up as days with multiple
  pills each. Click a pill to drill into the slot's schedule page.
- Toggle **List** view. Find the two red overlap rows on next Sunday
  (Vee's Vocals and Vee's Sound Tech both flag each other).

### 2. Drill into a slot's schedule
- From the dashboard, click **Open** on the Vee / Vocals row, or navigate
  **Organizations → Demo Church → Worship Team → Sunday Vocals →
  Schedule**.
- The schedule page shows the full slot timeline plus the assignment.

### 3. Trigger the conflict-detection path
- From the same schedule, attempt to assign **Val Walker** to a slot that
  overlaps Val's existing next-Sunday Sound Tech assignment (14:00-16:00
  UTC).
- The schedule should reject the new assignment with a clear
  "Conflicts with …" message that names the existing assignment.

### 4. Trigger the training-enforcement path
- Register a new account, e.g. `tester@demo.local`. Sign in as the new
  user.
- Navigate to **Organizations → Demo Church** and **Join** as a
  Volunteer.
- As **admin@demo.local**, open a slot's schedule and try to assign
  `tester@demo.local` to **any** slot.
- The schedule should reject the new assignment with "Training required
  (and currently expired): Safe Spaces & Child Protection".
- Sign in as `tester@demo.local` and visit **Training** to complete the
  Safe Spaces training. Then retry the assignment.

### 5. View the calendar as a volunteer
- Sign in as `volunteer@demo.local` (Vee).
- Go to **My schedule**.
- Try the same chip presets and the **List / Calendar** toggle.
- The list view shows Vee's four assignments across both Sundays,
  **with two red overlap rows on next Sunday** (the same overlap
  surfaced on the dashboard in step 1).
- Flip the scope to **All in my orgs**. The calendar / list now adds
  Val's two assignments (Vocals on this Sunday, Sound Tech on next
  Sunday) so you can see what other volunteers in your org are doing
  without leaving the page.
- Use **prev / next / today** nav on the calendar to walk between
  months.

### 6. Test the password-reset / change-password flow
- Sign out, then on the **Login** page click **Forgot your password?**.
- Enter a seeded email, e.g. `volunteer@demo.local`. The response is
  always generic ("If an account exists…") regardless of whether the
  address is registered.
- The reset link is written to the dev log (look for "To:
  volunteer@demo.local" with a `https://localhost:.../Account/ResetPassword?code=...`
  line). Copy-paste it into the browser.
- After resetting, sign in and visit **Account → Change password** to
  rotate it again.

### 7. Explore the sports league as the league admin
- Sign out, then sign in as `league-admin@demo.local` / `Passw0rd!`.
- Go to **Leagues** in the nav. You should see exactly one row
  (Springfield Youth Soccer League) — the "Leagues" page only
  surfaces ministries that have at least one team.
- Click into the league. The home page shows: the four teams (with
  their coach + age bracket), the top of standings (Eagles first on
  +2 goal difference, Hawks second on −2, Comets and Stars on 0
  played), the upcoming games list, and (because the league has
  sub-ministries) the sub-ministry table with each sub-coordinator.
- Click **Full standings** to see the full table. The top row is
  highlighted green. The bottom two teams (Comets, Stars) are at
  0 played / 0 pts because the seed only includes one played game
  (Eagles 3–1 Hawks).

### 8. Drill into a team
- From the league detail, click the **Eagles** team.
- The team page shows the roster (4 players with jersey numbers,
  positions, and the parent's contact phone/email — coaches and
  league admins see contact info; parents see only their own child's
  row plus the team-level data).
- The right column shows the upcoming games (Eagles vs Comets this
  Saturday, plus the double-booked next-Saturday game against
  Hawks) and the recent results (the Eagles 3–1 Hawks win from
  last Saturday with a "W" badge).
- The link in the header goes back to the parent ministry
  (`/Leagues/{leagueId}`).

### 9. View the schedule overlay
- Back on the league detail, click **Schedule**.
- The page is a day-grouped list (the `GameDayCalendar` component)
  with two event types: games (with arena) and game-day volunteer
  shifts (referee assignments, with the ref's name).
- Each event shows: time window (HH:mm–HH:mm), title, subtitle
  (arena for games, person for shifts), location, and a status
  badge.
- The "Filter by arena" dropdown narrows the view to a single
  arena. Try `Field 1` to see only the games + shifts at that
  field.
- On this Saturday you should see three games plus Quinn
  Referee's shift covering all three; on next Saturday you should
  see the two double-booked games plus Riley Referee's shift.

### 10. Trigger the arena-conflict path
- Go back to the league detail. Click **+ Schedule a game** (only
  visible because you're the league admin).
- Fill in: Home = Eagles, Away = Hawks, Arena = Field 1, Date =
  next Saturday, Start = 09:00, Duration = 60 min.
- Submit. The page should reject the request with: "Arena is
  already booked from {next-Sat 9:00 UTC} to {10:00 UTC} (Eagles
  vs Hawks)." — this is the intentional double-book in the seed
  exercising the `GameService` arena-conflict check.
- Change the start to 10:00 and try again. The new game is
  non-overlapping (10:00–11:00) so it saves successfully, and the
  conflict goes away because the original game ends at 10:00.

### 11. Score a game
- Navigate to the game you just created (Eagles vs Hawks next Sat
  10:00 at Field 1).
- As the league admin, the **Record final score** card on the
  right is visible. Enter a home score and away score, click
  **Save final score**.
- The page reloads with the new score and a green `Played` badge
  in the header. The standings page will now reflect the result on
  the next refresh.
- Sign out and sign in as `coach1@demo.local` (Coach Casey, who
  coaches both U10 Boys teams). Navigate to the **Eagles** team
  page. The recent-results list should now show this game with a
  W or L depending on the score you entered.

### 12. Upload and download a slot document
- Sign in as `coordinator@demo.local` (Chris Coordinator).
- Navigate to **Organizations → Demo Church → Worship Team →
  Sunday Sound Tech**. Scroll to the **Shared documents**
  section at the bottom of the page.
- The seeder pre-populates two `.txt` files. Each shows a
  file-type badge (`TEXT`), the file name, the size, the
  uploader's name, and the upload date.
- Click **Upload a document**. Pick any small file (a
  screenshot, a PDF, etc.). The title field pre-fills with
  the file name. Fill in a category (e.g. "Setup") and click
  **Upload**. The new row appears in the appropriate group.
- Click **Download** to verify the file downloads with the
  original filename.
- Click **Delete** and confirm. The row disappears.
- Sign out and sign in as a regular volunteer
  (`volunteer@demo.local`). The same slot page shows the
  shared documents section but the **Upload** card and the
  **Delete** buttons are hidden — read-only for non-
  coordinators.
- As a coordinator, try uploading a file larger than 10 MB.
  The upload fails with "File exceeds 10 MB limit."

---

## Feature inventory

| Area                       | Status        | Where                                                    |
|----------------------------|---------------|----------------------------------------------------------|
| Multi-org / multi-ministry | ✅ built      | `Models/Organization.cs`, `Models/Ministry.cs`           |
| Multi-volunteer sign-up    | ✅ built      | `Components/Account/Pages/Register.razor`                |
| Coordinator + admin RBAC   | ✅ built      | `Services/OrgAuthService.cs` (custom enum, not Identity) |
| Service-slot CRUD          | ✅ built      | `Components/Pages/ServiceSlots/`                         |
| Recurring schedule         | ✅ built      | `AssignmentService.ScheduleSeriesAsync`                  |
| Person-conflict detection  | ✅ built      | `AssignmentService.ValidateAsync`                        |
| Training enforcement       | ✅ built      | `AssignmentService.ValidateAsync` + `TrainingService`    |
| Video / PDF / slideshow    | ✅ built      | `TrainingContent` + `Components/Pages/Training/`        |
| Browser-timezone story     | ✅ built      | `UserTimeZoneProvider`, `wwwroot/js/timezone.js`         |
| Dashboard w/ KPIs          | ✅ built      | `Components/Pages/Dashboard.razor`                       |
| Date-range chips           | ✅ built      | `Components/Shared/DateRangeChips.razor`                 |
| Month-grid calendar        | ✅ built      | `Components/Shared/AssignmentCalendar.razor`             |
| Volunteer "My schedule"    | ✅ built      | `Components/Pages/MySchedule.razor`                      |
| Org-wide calendar scope    | ✅ built      | toggle in `MySchedule.razor`                             |
| Password reset / change    | ✅ built      | `Components/Account/Pages/{Forgot,Reset,Change}Password.razor` |
| Production email (MailKit) | ✅ built      | `Services/MailKitEmailSender.cs` (env-gated)             |
| Sample seeder              | ✅ built      | `Data/DatabaseSeeder.cs`                                 |
| EF migration               | ✅ built      | `Data/Migrations/InitialCreate.cs`                       |
| Unit tests (xUnit + bUnit) | ✅ built (77) | `tests/ServantSync.Tests/` — pure helpers (`CalendarEvent`, `AssignmentStatusUi`, `DateRangeCalculator`, `IcsCalendarGenerator`, `StandingsCalculator`), service integration (`AssignmentService`, `GameService`), and bUnit components (`DateRangeChips`, `AssignmentCalendar`) |
| Calendar week view         | ✅ built      | `Components/Shared/AssignmentCalendar.razor` (Month \| Week toggle) |
| Per-user-local timezone display | ✅ built | `Components/Shared/LocalTime.razor` (UTC → browser-local, semantic `<time>` tag) |
| Timezone-aware day bucketing | ✅ built   | `UserTimeZoneProvider.ToLocal` + `IDisposable` subscriptions on `AssignmentCalendar` / `Dashboard` / `GameDayCalendar` |
| SkeletonLoader for loading states | ✅ built | `Components/Shared/SkeletonLoader.razor` (Bootstrap placeholder-glow) |
| ConfirmDialog Bootstrap modal | ✅ built   | `Components/Shared/ConfirmDialog.razor` (a11y parity: Esc / backdrop / focus trap / `aria-labelledby`) |
| `[SupplyParameterFromForm]` form-binding pattern | ✅ built | All `<EditForm FormName="…">` sites use this to prevent the silent field-vs-property POST binding defect |
| Centralized upload limits   | ✅ built      | `Services/SlotUploadLimits.cs` (single source of truth for the 10 MB cap) |
| Login password validation  | ✅ built      | `Components/Account/Pages/Login.razor` has `[Required]` on `LoginModel` + `inputmode="email"` |
| ICS subscribe feed         | ✅ built      | `/MySchedule/ics` minimal-API endpoint + `IcsCalendarGenerator` |
| Sports-league surface      | ✅ built      | `Arena`, `Team`, `Player`, `Game` + `StandingsCalculator` + 8 new pages |
| Multiple configurable arenas | ✅ built   | org-scoped, shared by all leagues in the org            |
| Sub-ministry coordinators | ✅ built      | `Ministry.ParentMinistryId` self-FK                      |
| Standings calculation     | ✅ built      | `StandingsCalculator` (3-1-0 default, GD/GF/name tiebreakers) |
| Sports-league training gate | ✅ built    | `Game-Day Referee` requires Concussion training         |
| Per-slot shared documents | ✅ built      | `Models/SlotDocument.cs`, `Services/SlotDocumentService.cs`, "Shared documents" section in `Components/Pages/ServiceSlots/Detail.razor`, `/slots/{slotId:int}/documents/{docId:int}/download` minimal API |
| Volunteer dashboard        | ❌ not built  | n/a                                                      |
| Per-slot coordinator        | ✅ built      | `ServiceSlot.CoordinatorPersonUserId` + `CoordinatorEmail` + `CoordinatorPhone`; Admin/Coordinator of the slot's parent org assigns via `/Organizations/{id}/Ministries/{minId}/Roles/{slotId}/Edit` (mirrors the Ministry-level coordinator). Per-org gate enforced via `OrgAuthService.CanManageSlotAsync`. Migration `AddServiceSlotCoordinator` adds the 3 columns + SetNull FK. |
| Coordinator dashboard       | ✅ built      | `/Organizations/{id}/Coordinators` aggregates every slot across all of the org's ministries; filter chips (All / Unassigned [default] / Assigned / Inactive); per-row inline Assign/Edit + Unassign; defaults the "Unassigned" lens because that's what needs attention. Service: `ICoordinatorAssignmentsService.ListAsync` (org-scoped, unassigned-first sort) + `AssignAsync` (Admin OR Coordinator, with cross-org membership guard on the assigned Person) + `UnassignAsync` (single SaveChanges clearing all three fields). Discoverable via "Manage coordinators" button on the org header. |
| Per-volunteer swap UI      | ❌ not built  | n/a                                                      |
| Per-game roster picker     | ❌ not built  | team is currently the roster; no per-game lineup UI     |
| Push / email reminders     | ❌ not built  | n/a                                                      |
| CI workflow                | ✅ built      | `.github/workflows/ci.yml` (Ubuntu, .NET 9, push + PR)  |
| Dockerfile                 | ❌ not built  | n/a                                                      |

---

## Architecture

```
ServantSync/
├── Components/                          # Razor + Blazor
│   ├── Account/                         # ASP.NET Core Identity pages
│   │   ├── Pages/
│   │   │   ├── Login.razor               # has a "Forgot password?" link
│   │   │   ├── ForgotPassword.razor      # generic reply, logs reset URL
│   │   │   ├── ResetPassword.razor       # consumes ?code=&email=
│   │   │   ├── ChangePassword.razor      # [Authorize]
│   │   │   └── Manage.razor              # has a "Change password" link
│   │   └── Shared/                       # IdentityStatusMessage, etc.
│   ├── Layout/                          # MainLayout + NavMenu
│   ├── Pages/                           # Application pages
│   │   ├── Dashboard.razor               # Coordinator/admin: 14-day ops view
│   │   ├── MySchedule.razor              # Volunteer: own + org-wide assignments
│   │   ├── Organizations/                # List, Detail, Edit, Join (+ Arenas tab)
│   │   ├── Ministries/                   # List, Edit
│   │   ├── ServiceSlots/                 # List, Edit, Schedule
│   │   ├── People/                       # Person directory
│   │   ├── Training/                     # Edit, Take
│   │   ├── Leagues/                      # Index, Detail, Standings, Schedule (sports)
│   │   ├── Teams/                        # Detail, Players (sports)
│   │   └── Games/                        # Detail, Edit (sports)
│   ├── Shared/                          # App-wide reusable components
│   │   ├── AssignmentCalendar.razor      # Month + week calendar (Sun-first), user-local TZ bucketing
│   │   ├── AssignmentCalendar.razor.css  # Scoped styles
│   │   ├── GameDayCalendar.razor         # Overlay: games + game-day volunteers, user-local TZ bucketing
│   │   ├── GameDayCalendar.razor.css     # Scoped styles
│   │   ├── CalendarEvent.cs              # POCO (+ ArenaId)
│   │   ├── DateRangeChips.razor          # Preset + custom range strip
│   │   ├── AssignmentStatusUi.cs         # AssignmentStatus + GameStatus → Bootstrap bg-*
│   │   ├── DateRangeCalculator.cs        # Pure range math
│   │   ├── LocalTime.razor               # UTC → browser-local <time datetime="...">; subscribe to TimeZoneChanged
│   │   ├── SkeletonLoader.razor          # Bootstrap placeholder-glow shimmer (Size + Columns params)
│   │   └── ConfirmDialog.razor           # Generic Bootstrap confirm modal; Esc / backdrop / focus trap / aria-labelledby
│   ├── _Imports.razor                   # Global @using directives
│   ├── App.razor / Routes.razor          # Router + layout
│   └── *.razor.css                       # Layout-scoped CSS
├── Data/
│   ├── ApplicationDbContext.cs          # EF Core context
│   ├── ApplicationDbContextFactory.cs    # IDbContextFactory<T>
│   ├── DatabaseSeeder.cs                 # Idempotent sample data
│   └── Migrations/                       # EF Core migrations
├── Models/                              # Domain entities
│   ├── Assignment.cs
│   ├── Enums.cs                          # OrganizationRole, TrainingFormat, TeamAgeBracket, GameStatus, ...
│   ├── Ministry.cs                       # + ParentMinistryId (sub-ministries)
│   ├── Organization.cs                   # + Arenas nav
│   ├── OrganizationMembership.cs
│   ├── Person.cs
│   ├── ServiceSlot.cs
│   ├── TrainingCompletion.cs
│   ├── TrainingContent.cs
│   ├── TrainingRequirement.cs
│   ├── Arena.cs                          # NEW — org-scoped playing surface
│   ├── Team.cs                           # NEW — sport-agnostic (soccer/bball/etc.)
│   ├── Player.cs                         # NEW — roster + parent contact
│   ├── Game.cs                           # NEW — schedule + score + arena FK
│   └── SlotDocument.cs                   # NEW — per-slot shared files
├── Services/
│   ├── AssignmentService.cs              # Scheduling + validation
│   ├── IAssignmentService.cs
│   ├── ITrainingService.cs
│   ├── TrainingService.cs
│   ├── OrgAuthService.cs                 # Custom RBAC on OrganizationMembership (+ CanManageMinistry/Team)
│   ├── UserTimeZoneProvider.cs           # Per-user browser timezone; canonical `ToLocal(utc)` + `TimeZoneChanged`
│   ├── UploadPathProvider.cs             # Training-file storage
│   ├── EmailOptions.cs                   # SMTP config binding
│   ├── EmailSender.cs                    # LoggingEmailSender (dev fallback)
│   ├── MailKitEmailSender.cs             # Production SMTP
│   ├── IcsCalendarGenerator.cs           # Minimal RFC 5545 writer
│   ├── StandingsCalculator.cs            # NEW — pure: 3-1-0 default, GD/GF/name tiebreakers
│   ├── IStandingsService.cs              # NEW
│   ├── TeamService.cs                    # NEW — roster CRUD, soft-delete
│   ├── ITeamService.cs                   # NEW
│   ├── GameService.cs                    # NEW — schedule/score + arena-conflict check
│   ├── IGameService.cs                   # NEW
│   ├── SlotDocumentService.cs            # NEW — per-slot file upload + download
│   ├── ISlotDocumentService.cs           # NEW
│   ├── SlotUploadLimits.cs               # NEW — single source of truth for the 10 MB cap (used by both server validation and InputFile maxAllowedSize)
│   └── UrlSafety.cs                      # NEW — shared open-redirect check
├── Components/Shared/                    # Reusable Blazor components
│   ├── ...
│   └── ConfirmDialog.razor               # NEW — generic Bootstrap confirm modal
├── wwwroot/
│   ├── app.css
│   ├── lib/bootstrap/                    # Bootstrap 5
│   └── js/timezone.js                    # IANA detection + cookie
├── tests/
│   └── ServantSync.Tests/                # xUnit 2.9.2, 37 tests
├── .github/
│   └── workflows/
│       └── ci.yml                        # GitHub Actions: restore + build + test on push/PR
├── appsettings.json
├── appsettings.Development.json
├── Program.cs
└── ServantSync.csproj                    # Microsoft.NET.Sdk.Web, net9.0
```

**Architectural conventions**

- Both `IDbContextFactory<ApplicationDbContext>` and the standard scoped
  `DbContext` are registered. The factory is used inside Blazor Server
  handlers for short-lived per-operation contexts (safer across SignalR
  circuits); the scoped registration is required by Identity's stores.
- RBAC uses a custom `OrganizationRole` enum on
  `OrganizationMembership`, not ASP.NET Identity roles. Pages that
  require an org-specific role call `OrgAuthService.CanManageOrgAsync`.
- All times are stored in UTC and converted to the browser's IANA zone
  for display via `UserTimeZoneProvider` (cookie-backed, set by
  `wwwroot/js/timezone.js`). The single canonical conversion lives on
  `UserTimeZoneProvider.ToLocal(utc)`; every page that renders a time
  or buckets by date goes through this method so the conversion stays
  consistent. Widely-shared pages (`AssignmentCalendar`,
  `Dashboard`, `GameDayCalendar`, `MySchedule`, per-slot pages) all
  `@implements IDisposable` and subscribe to `TzProvider.TimeZoneChanged`
  so a TZ arrival after the prerender round-trip triggers in-place
  re-bucketing without a full page reload.
- `DateRangeChips` is purely presentational; the date math is in
  `DateRangeCalculator`, which is unit-tested. `AssignmentStatusUi` is
  the single source of truth for status → Bootstrap class mappings.

---

## Configuration reference

`appsettings.json` keys (override per-environment in
`appsettings.Development.json` or via env vars):

| Key                              | Default                | Notes                                                   |
|----------------------------------|------------------------|---------------------------------------------------------|
| `ConnectionStrings:DefaultConnection` | `Data Source=servantsync.db` | SQLite file path. Relative to the app's content root. |
| `Email:Smtp:Host`                | `""`                   | Empty = drop-to-log. Required for production sends.     |
| `Email:Smtp:Port`                | `587`                  | Standard STARTTLS submission port.                      |
| `Email:Smtp:User` / `Password`   | `""`                   | Leave User empty to skip `AUTH` on the SMTP session.    |
| `Email:Smtp:TlsMode`             | `StartTlsWhenAvailable`| `None` / `StartTls` / `StartTlsWhenAvailable` / `SslOnConnect`. |
| `Email:FromAddress` / `FromName` | `noreply@servantsync.local` / `ServantSync` | Header sender.        |

**Environment-driven behavior**

- `ASPNETCORE_ENVIRONMENT=Development` (default for `dotnet run`) →
  `LoggingEmailSender` is wired in. All four Identity email methods
  (confirmation link, reset link, reset code, 2FA code) are written to
  the console with the actual link so you can copy-paste into the
  browser.
- `Production` / `Staging` / anything else → `MailKitEmailSender` is
  wired in and reads SMTP details from the `Email` config section.

---

## Test commands

```bash
# Run all unit tests
dotnet test

# Run only the calendar tests
dotnet test --filter "FullyQualifiedName~CalendarEvent"

# Run with code coverage (coverlet output in TestResults/)
dotnet test --collect:"XPlat Code Coverage"
```

The test project is at `tests/ServantSync.Tests/`. It targets `net9.0`,
uses xUnit 2.9.2 + Microsoft.NET.Test.Sdk 17.11.1, and references the
main project. Thirty-seven tests cover `CalendarEvent.Tooltip`,
`AssignmentStatusUi.ColorFor`, `DateRangeCalculator.Resolve`,
`IcsCalendarGenerator.FormatUtc`, and `StandingsCalculator.Calculate`.

---

## Known limitations / follow-ups

- **The `/MySchedule/ics` Subscribe button can't actually be
  subscribed to in external calendar apps today.** The minimal-API
  endpoint is `[Authorize]`-gated (cookie-only). Google / Apple /
  Outlook can't present your domain's auth cookie, so the URL works
  in-browser (when you're signed in) but the tooltip *promises*
  something the endpoint can't deliver for an actual subscribed
  feed. The fix is a per-user unguessable token in the URL
  (e.g. `/MySchedule/ics?token={GUID}`) so external clients can
  pull on a schedule without impersonating the user. Tracked as
  the top item in `STATUS.md → Pending work`.
- **~~ICS long lines are not folded per RFC 5545 §3.1.~~** Fixed —
  `IcsCalendarGenerator` now folds content lines at 75 octets with
  `\r\n ` continuation, per RFC 5545 §3.1. Tests updated to assert
  on the folded form.
- **The "All" chip on `DateRangeChips` silently renames itself to
  90 days** when generating the ICS URL. A user who clicks "All"
  expects their whole schedule; the feed they get is the next 90
  days. The Subscribe button's `title=` attribute and a visible
  "Feed: last 90 days" badge now declare the substitution before
  the user clicks. The chip itself still shows "All" because
  that's what the user picked for the on-page view.
- **No per-volunteer swap UI.** Coordinators can reschedule from the
  schedule page, but there's no drag-and-drop or "find a substitute"
  flow.
- **No per-game roster picker.** A team is currently the roster — any
  active player on the team is "on" the team for every game. A
  `GameRosterEntry` table + UI for picking starters/bench/position per
  game is the obvious next step.
- **Arena-conflict is service-enforced, not DB-enforced.** The check
  lives in `GameService.ScheduleGameAsync`, not at the DB level. The
  seeder writes two games at the same `Field 1` / same time on
  purpose to exercise the conflict path. For a real production system,
  add a filtered unique index on `(ArenaId, StartUtc)` where
  `Status != Cancelled`.
- **Sports-league points scheme is hard-coded in the UI** as "3 per
  win, 1 per draw, 0 per loss". The `StandingsCalculator` is
  parameterized (3-1-0 default but the call site can override) — the
  header copy just needs to match the scheme you pick.
- **CI runs `dotnet build` + `dotnet test` on every push and PR.** Workflow
  at `.github/workflows/ci.yml` uses `ubuntu-latest`, `actions/setup-dotnet@v4`
  with built-in NuGet cache, and `Release` configuration. Branch-protection
  rules are not enabled by default (no `.github/CODEOWNERS`, no required
  checks) — opt in on the GitHub side when you're ready.
- **The test project is nested under the main project.** Because the
  main `ServantSync.csproj` lives at the repo root, the
  `tests/ServantSync.Tests/` directory is inside its `**/*.cs` glob.
  The csproj adds `<DefaultItemExcludes>$(DefaultItemExcludes);tests\**</DefaultItemExcludes>`
  to keep the main project from compiling the test sources. If you
  restructure into the standard `src/ServantSync/` + `tests/ServantSync.Tests/`
  layout, remove that line.
- **No Dockerfile / production deploy hardening.** HSTS, response
  compression, structured logging sinks, and `Migrate()`-on-startup are
  all possible follow-ups.
- **MailKit has an open moderate advisory** (`GHSA-9j88-vvj5-vhgr`) that
  is suppressed in the csproj via `NoWarn=NU1902`. Bump the package when
  a patched release ships.
- **Default Identity password rules are relaxed** to support the demo
  (`Passw0rd!` is the seed password). Tighten `Password.RequiredLength`
  / complexity in `Program.cs` for production.
- **Global InteractiveServer render-mode at the layout level.** Pages
  become interactive (and `@onclick` handlers fire) only through the
  top-of-file `@rendermode InteractiveServer` directive on
  `Components/Layout/MainLayout.razor`. This is .NET 9's recommended
  pattern for promoting a few `@onclick` handlers site-wide. The
  directive deliberately does NOT live on `<AuthorizeRouteView>` in
  `Components/Routes.razor`: that element has a templated
  `<NotAuthorized>` child that can't cross a `RenderMode` boundary
  — see the verbatim exception quoted at the top of that file.
  Future contributors, don't try to move the directive onto
  `<AuthorizeRouteView>`; it'll re-explode the RenderFragment
  serialization exception.
- **`.NET 9` has no Static opt-out for `@rendermode`.** A page that
  wants to escape the cascade and stay Static SSR cannot do so via
  `@rendermode="@(RenderMode.Static)"`; the directive only accepts
  `InteractiveServer` / `InteractiveWebAssembly` / `InteractiveAuto`.
  When an opt-out is genuinely needed (none today), route through a
  sibling non-interactive layout instead. Tracked as Tier 2 in
  [`STATUS.md`](STATUS.md).
- **Dev-mode (`dotnet run`) is flaky for static assets.** The
  `StaticAssetDevelopmentRuntimeHandler` occasionally throws
  `FileNotFoundException: wwwroot\ServantSync.styles.css` on
  protected-route navigations, with no codebase change required.
  Workaround for browser smoke-testing: stop the dev session,
  `dotnet publish -c Release -o bin\ReleasePub`, then
  `dotnet bin\ReleasePub\ServantSync.dll --urls=http://localhost:5070`.
  The published binary uses the static-web-assets manifest's HASHED
  paths and bypasses the dev-runtime patcher. See
  [`STATUS.md → Known quirks`](STATUS.md) for the diagnostic trace.
