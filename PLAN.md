# ServantSync — original plan

The original product plan for the ServantSync volunteer-scheduling
platform. This is the **spec** (relatively static). The current build
state of each item is also noted inline so this single file answers
"was it built and where?". The working state, pending work, and
operational quirks live in [`STATUS.md`](STATUS.md). The
end-user-facing docs live in [`README.md`](README.md).

## Product goals

A multi-organization, multi-ministry volunteer-scheduling platform
designed for churches first and extensible (without a schema rewrite)
to other scheduling use cases — sports leagues, on-call rotas,
conference staffing.

## Functional requirements

These are the items from the original spec, each annotated with the
current build state. Status legend: ✅ built and exercised by the
seed / tests; 🟡 designed-for or partially built; ❌ not started.

### Core data model

1. **Multi-org / multi-ministry / multi-volunteer-opportunity hierarchy** — ✅
   - `Organization` contains many `Ministry`s; `Ministry` contains many
     `ServiceSlot`s.
   - Each `Person` has one or more `OrganizationMembership`s, each with
     a `Role` (Volunteer / Coordinator / Admin).
   - Files: `Models/Organization.cs`, `Models/Ministry.cs`,
     `Models/ServiceSlot.cs`, `Models/OrganizationMembership.cs`,
     `Models/Person.cs`, `Models/Enums.cs`.

2. **Coordinator + contact info on ministry** — ✅
   - `Ministry.CoordinatorPersonUserId`, `CoordinatorEmail`,
     `CoordinatorPhone` (plus display navigation through `Person`).
   - Editable via `Components/Pages/Ministries/Edit.razor`.

3. **Volunteer-opportunity CRUD** — ✅
   - `ServiceSlot.Name`, `Description`, `Location`,
     `DefaultDurationMinutes`, `IsActive`.
   - Editable via `Components/Pages/ServiceSlots/Edit.razor` and
     schedule page.

### Volunteer flows

4. **Volunteer self-signup** — ✅
   - Anyone can register an account via `/Account/Register`.
   - `SignIn.RequireConfirmedAccount` is `false` in `Program.cs`
     (dev convenience). See "Known quirks" in `STATUS.md` before
     flipping this in production.

5. **Choose ministry after signup** — ✅
   - A registered user can browse `/Organizations` and click **Join**
     on any ministry to become a `Volunteer` member of that org.
   - Implemented in `Components/Pages/Organizations/Detail.razor`'s
     `Join` flow.

### Training

6. **Yearly org-wide training requirement** — ✅
   - `TrainingRequirement` with `OrganizationId` set,
     `Cadence = TrainingCadence.Yearly`.
   - DB check constraint enforces exactly-one scope (org OR slot).

7. **Opportunity-specific training requirement** — ✅
   - `TrainingRequirement` with `ServiceSlotId` set,
     `Cadence = TrainingCadence.Yearly`.
   - Same model as #6 with the other scope populated.

8. **Video / slideshow / PDF media** — ✅
   - `TrainingFormat` enum: `Video`, `Slideshow`, `Pdf`.
   - `TrainingContent.FilePathOrUrl` stores a YouTube embed URL or a
     relative path under `wwwroot/uploads/training/`.
   - Files: `Models/Enums.cs`, `Models/TrainingContent.cs`,
     `Components/Pages/Training/{Edit,Take}.razor`,
     `Services/UploadPathProvider.cs`.

### Scheduling

9. **Schedule volunteers** — ✅
   - `Assignment` row links a `Person` to a `ServiceSlot` for a UTC
     window.
   - Recurring series via
     `AssignmentService.ScheduleSeriesAsync` (weekly recurrence,
     TimeZoneInfo-aware, 500-occurrence cap).
   - Files: `Models/Assignment.cs`,
     `Services/AssignmentService.cs`,
     `Services/IAssignmentService.cs`,
     `Components/Pages/ServiceSlots/.../Schedule.razor`.

10. **Avoid global per-person scheduling conflicts** — ✅
    - Per-person overlap check in
      `AssignmentService.ValidateAsync`:
      `existing.StartUtc < candidate.EndUtc AND existing.EndUtc > candidate.StartUtc`
      for the same `PersonUserId`, excluding `Cancelled` and `NoShow`
      statuses.
    - The dashboard surfaces these as red rows in the list view and
      as flagged pills in the calendar view.

### Design goal (forward-looking)

11. **Sports-league reuse / extensibility** — ✅ built (validated)
    - A new `Springfield Youth Soccer League` ministry lives inside
      Demo Church with three sub-ministries (Referees, Concessions,
      Devotion) and a fully working sports-league surface.
    - New tables: `Arena` (org-scoped, shared by all leagues), `Team`,
      `Player`, `Game`. New enums: `TeamAgeBracket`, `GameStatus`.
      Minimal additive schema change: `Ministry.ParentMinistryId`
      (self-FK) for sub-ministry coordinators.
    - Volunteer positions (coach, asst coach, game-day referee,
      concession, devotion leader) are **regular ServiceSlots** that
      reuse the existing `Assignment` + training-gate + conflict
      pipeline. The "designed for extensibility" promise is validated:
      a new use case plugged in with **no rewrite of the existing
      tables**.
    - Files: `Models/{Arena,Team,Player,Game}.cs`, `Models/Enums.cs`,
      `Models/Ministry.cs`, `Data/ApplicationDbContext.cs`,
      `Data/DatabaseSeeder.cs`, `Services/{Standings,Team,Game}*Service.cs`,
      `Components/Shared/GameDayCalendar.razor`,
      `Components/Pages/{Leagues,Teams,Games}/`, plus an
      `AddSportsLeague` migration.

## Out of scope (explicitly deferred from the original spec)

- Mobile app (the web UI is responsive; no native client).
- Real-time push notifications (Blazor Server circuit only — no
  SignalR hub for cross-user live updates beyond what Blazor
  already gives).
- Payment processing / pledge tracking (this is not a CRM).
- ~~Calendar publish / subscribe (no ICS feed yet — see
  `STATUS.md → Pending work`).~~ **SHIPPED** — `/MySchedule/ics`
  minimal-API endpoint + `IcsCalendarGenerator`. Token-based
  external auth and RFC-5545 line folding remain pending (see
  `STATUS.md`).
- Drag-and-drop schedule editor.

## Known UX-audit findings pending (not in original spec; surfaced
during testing after the form-binding + TZ fixes landed)

These are real defects the user / next-contributor is likely to
discover while exercising the app. They are tracked, with priority,
in `STATUS.md → Pending work`.

- **`/MySchedule/ics` Subscribe button can't actually be subscribed
  to from an external calendar client.** Endpoint is `[Authorize]`
  cookie-gated; Google/Apple/Outlook calendars cannot present the
  domain cookie. Fix: per-user unguessable token in the URL.
- **`IcsCalendarGenerator` doesn't fold long lines per RFC 5545
  §3.1.** Some clients reject lines over 75 octets.
- **"All" date-range chip silently translates to a 90-day URL for
  the ICS feed** with no user-visible indicator.
- **`ConfirmDialog` does not yet trap Tab focus inside the dialog**
  when used as a destructive-confirmation for very long async
  work; Tab can escape to the underlying page.
- **`SlotDocument` upload lacks upload-progress UI.** The 10 MB
  cap is fine but a coordinator uploading several large files
  sees no feedback other than eventually-saved / -failed.
- **No "in-flight" `SkeletonLoader` use outside the Home cards.**
  Saves and long DB queries elsewhere render an unconditional
  "Loading..." line with no shimmer.
- **Login / Register have no password strength meter or
  confirm-password field.** Register readily accepts
  `Passw0rd!`-level inputs that would not pass a real-world
  complexity check.
- **`SlotDocument` has no per-document access control** — anyone
  with the slot's org membership can download. The pastor-only
  use case isn't covered.

## Subsequent additions (built during development but not in the
original spec)

These were added as the implementation progressed, in rough
chronological order:

- **Browser-timezone story** — `UserTimeZoneProvider` +
  `wwwroot/js/timezone.js`. IANA zone detection on first load,
  cookie-backed, used by all time-of-day displays. ✅
- **RBAC (admin / coordinator)** — `Services/OrgAuthService.cs`
  on top of the custom `OrganizationRole` enum. Replaces Identity
  roles for org-level access control. ✅
- **Sample seeder** — `Data/DatabaseSeeder.cs`. Idempotent on the
  `Organizations` table; creates 4 users, 1 org, 2 ministries, 3
  service slots, 1 training content, 2 training completions, 6
  sample assignments. ✅
- **EF migration** — `Data/Migrations/InitialCreate.cs`. ✅
- **Password reset / change password** — `Components/Account/Pages/
  {ForgotPassword,ResetPassword,ChangePassword}.razor`. ✅
- **Production email (MailKit)** — `Services/MailKitEmailSender.cs`
  + `Services/EmailOptions.cs`. Conditional DI: `LoggingEmailSender`
  in Development, `MailKitEmailSender` elsewhere. ✅
- **Coordinator dashboard** — `Components/Pages/Dashboard.razor`.
  KPIs (Total / Unique / Overlaps / Outstanding trainings), list +
  calendar views, 14-day default window. ✅
- **Date-range chip strip** — `Components/Shared/DateRangeChips.razor`
  + `Components/Shared/DateRangeCalculator.cs` (pure, testable). ✅
- **Month-grid calendar** — `Components/Shared/AssignmentCalendar.razor`
  + `.razor.css` + `Components/Shared/CalendarEvent.cs`. ✅
- **Volunteer "My schedule"** — `Components/Pages/MySchedule.razor`.
  Own + org-wide scope toggle, list + calendar views. ✅
- **Unit tests** — `tests/ServantSync.Tests/`, 21 xUnit tests
  covering `CalendarEvent`, `AssignmentStatusUi`, `DateRangeCalculator`. ✅
- **Per-slot shared documents** — `Models/SlotDocument.cs` (FK
  chain `UploadedByUserId` → `People.UserId` → `AspNetUsers.Id`
  so the UI can show `Person.DisplayName`);
  `Services/SlotDocumentService.cs` with upload (10 MB cap,
  extension allow-list, collision-resistant file names),
  grouped-by-category listing, delete with proper permission
  gating; `UploadPathProvider` extended with slot-scoped
  root/relative/absolute path helpers; new minimal-API download
  endpoint (`/slots/{slotId:int}/documents/{docId:int}/download`),
  auth- and slot-membership-gated; SignalR message-size budget
  bumped to 20 MB; new "Shared documents" section in
  `Components/Pages/ServiceSlots/Detail.razor` with a
  coordinator-only upload card, file-type badge per row, delete
  confirmation, and separate error states for upload vs delete;
  2 sample documents seeded on the Sound Tech slot;
  `AddSlotDocuments` EF migration. Also: open-redirect fix in
  `Register.razor` (the `?returnUrl=` query was being navigated
  to without validation) and shared `Services/UrlSafety.IsLocalUrl`
  helper that both `Login.razor` and `Register.razor` now use. ✅
- **README, PLAN, STATUS docs** — this file + `STATUS.md` + `README.md`. ✅
- **`[SupplyParameterFromForm]` form-binding pattern.** An
  `<EditForm Model="_x" OnValidSubmit="..." FormName="...">`
  does an HTTP POST round-trip on submit. Without
  `[SupplyParameterFromForm]` *on a property* (not a field), the
  typed values get discarded and validators refuse the input
  ("Name is required" even when the user filled it in). Defect
  was in 13 sites across 12 files; every model-bound form in the
  app now uses the `[SupplyParameterFromForm] private T _x { get;
  set; } = new();` pattern. ✅
- **`LocalTime` shared component** — `Components/Shared/LocalTime.razor`.
  UTC → browser-local via `UserTimeZoneProvider.ToLocal(...)`,
  semantic `<time datetime="...">` wrapper, format + class
  passthrough. Migrated every assignment-time display site
  (MySchedule list + calendar pills, Dashboard daily feed, slot/
  team/league/game detail pages). ✅
- **Timezone-aware day bucketing.** Extracted
  `UserTimeZoneProvider.ToLocal(utc)` as the single canonical
  conversion. `AssignmentCalendar`, `Dashboard`, and
  `GameDayCalendar` all `@implements IDisposable` and subscribe to
  `TzProvider.TimeZoneChanged` so the day-bucketing predicates
  refresh when the browser zone arrives after prerender.
  `Dashboard` caches raw assignment rows in `_rawRows` and adds a
  `RebuildBuckets()` method so the TZ change re-buckets in
  memory without a DB re-query. ✅
- **`SkeletonLoader` shared component** — Bootstrap
  `placeholder-glow` shimmer with Size + Columns parameters,
  rendered `aria-hidden="true"`. Wired into the home dashboard
  cards so the KPI numbers don't flash `0` on first paint. ✅
- **`ConfirmDialog` a11y parity with Bootstrap's modal** —
  Escape key dismiss, backdrop click to cancel, Cancel button
  autofocused on show, `aria-labelledby` linking to the title,
  focus trap. Now reused across `ServiceSlots/Detail.razor` (slot
  document delete) and any other destructive action (player
  removal, drop-membership). ✅
- **`SlotUploadLimits` central constants** — single source of
  truth for the per-file upload size cap (`MaxFileSizeBytes`,
  `MaxFileSizeDisplay`). Both the server-side validator
  (`SlotDocumentService`) and the Blazor `InputFile` producer
  pass `maxAllowedSize: SlotUploadLimits.MaxFileSizeBytes`, so a
  cap change takes effect in one place. ✅- **Login password validation** — `LoginModel` has `[Required]` on
  email + password, and the email input uses
  `inputmode="email"` for a better mobile keyboard. The
  Register page still needs a strength meter / confirm field
  (deferred). ✅ partial
- **Admin-only RBAC tier-1 closure** — every Manage action on the
  Organization surface now routes through a service that gates on
  `OrgAuth.IsOrgAdminAsync` instead of the role-agnostic save path.
  `OrganizationMembership` writes (add / promote / demote /
  remove) move to `MemberManagementService.AddAsync`,
  `UpdateRoleAsync`, and `RemoveAsync` (Admin-only; remove
  refused if it would leave zero Admins — see the new
  `WouldLeaveOrgWithoutAdminAsync` helper). Arena writes move to
  `ArenaService.CreateAsync` (Admin-only). Ministry create/edit
  moves to `OrganizationMinistryService.UpsertAsync` (Admin-only,
  with cross-org scope-check on the edit path). The org create
  flow now auto-promotes the creator to Admin in a single
  transaction (`OrganizationService.CreateOrgAsync`) so newly-
  minted orgs never land in a managerless state. The Members-tab
  UI hides the add-form, role selector, and remove button for
  non-Admins; the Arenas tab and "Add ministry" link are gated
  the same way. Files: `Services/MemberManagementService.cs`,
  `Services/ArenaService.cs`,
  `Services/OrganizationMinistryService.cs`,
  `Services/OrganizationService.cs`, page gates in
  `Components/Pages/Organizations/{Detail,Edit}.razor` and
  `Components/Pages/Ministries/Edit.razor`. ✅
- **Popup snake easter egg** — small pop culture moment for the
  team during demos. Triggered by typing `snake` anywhere on the
  app (case-insensitive, 5-char rolling buffer) or by navigating
  to `?snake=1`. Arrow keys control direction, SPACE restarts
  on death, ESC closes. Pure client-side: new
  `wwwroot/js/snake.js` (game loop + canvas rendering, no
  Blazor interop) and `wwwroot/css/snake.css` (retro CRT-style
  modal). Mounted via `<script defer>` + `<link>` in
  `Components/App.razor`. Self-contained: doesn't depend on
  Bootstrap JS or any server state. Doesn't intercept
  modifier-key keystrokes or non-character keys. ✅
- **Per-slot coordinator** — `ServiceSlot.CoordinatorPersonUserId`
  (FK People, cascade `SetNull` — mirrors Ministry / Team), plus
  the coordinator's `CoordinatorEmail` + `CoordinatorPhone` and
  the navigation `CoordinatorPerson`. New
  `OrgAuthService.CanManageSlotAsync(userId, slotId)` resolution
  chain: short-circuits on slot-coord assignment, otherwise defers
  to existing `CanManageMinistryAsync` (which inherits org Admin /
  Coordinator + parent-ministry transitive). UI: EditForm on
  `ServiceSlot/Edit.razor` gained a Coordinator section (member
  dropdown + email + phone), full save/load wiring on both Create +
  Edit; `ServiceSlot/Detail.razor` shows a coordinator card with
  linked display name + email + phone (and an "Assign one" link
  when none); `ServiceSlot/Schedule.razor` `_canSchedule` swapped
  from `CanManageOrgAsync(userId, OrgId)` to `CanManageSlotAsync(
  userId, Id)` so a slot coordinator can schedule their own slot
  without org-wide role. Migration
  `AddServiceSlotCoordinator` (auto-generated). 9 new
  `OrgAuthServiceTests` cases pinning each chain branch. ✅
- **Coordinator dashboard** — new org-scoped aggregation surface
  at `/Organizations/{Id:int}/Coordinators`. Lists every slot
  across every ministry with its coordinator FK + display name +
  email + phone, or an "Unassigned" badge if none. Filter chips
  All / Unassigned [default-on so admins see "what still needs
  attention"] / Assigned / Inactive; per-row inline Assign/Edit
  (member dropdown auto-seeds the phone field from `Person.Phone`
  — the email field stays admin-typed because Person schema stores
  Phone but not Email; coordinators routinely prefer a personal
  alias separate from their auth email) + Unassign that clears
  all three fields in one SaveChanges via service
  `CoordinatorAssignmentsService.UnassignAsync`. New service:
  `ICoordinatorAssignmentsService` with `ListAsync(orgId)`
  (eager-joins Ministry → Slot → CoordinatorPerson via EF LINQ,
  sorts with `ORDER BY (CASE WHEN CoordinatorPersonUserId IS
  NULL THEN 0 ELSE 1 END), Ministry.Name, Slot.Name` so
  unassigned rows float to the top), `AssignAsync` (Admin OR
  Coordinator gate + secondary `OrganizationMemberships` check
  that the assigned Person lives in the slot's parent org),
  `UnassignAsync` (delegates to AssignAsync with all-null
  inputs). Result-enum `CoordinatorMutationResult { Updated,
  PermissionDenied, NotFound }` so the page branches without
  exceptions. 16 new
  `CoordinatorAssignmentsServiceTests` cases. Discovered via a
  "Manage coordinators" button on the org page header. ✅
- **"All" chip ICS substitution hint** — `MySchedule.razor`
  collapses the user-picked "All" range to 90 days for the ICS
  URL (external calendar clients can't ingest unbounded feeds
  anyway). Previously silent. The Subscribe button's `title=`
  attribute and a small inline badge now declare "Feed: last 90
  days" so the user knows the on-page view window and the feed
  window may differ. ✅
- **ICS line folding per RFC 5545 §3.1** —
  `IcsCalendarGenerator.Generate` now folds content lines at 75
  octets with `\r\n ` continuation per the RFC. The fold is
  deterministic (octet count, not char count; continuation
  indented with one space). Strictly-conformant Outlook builds
  that previously rejected the un-folded feed now accept it.
  `IcsCalendarGeneratorTests` updated to assert on the folded
  form. ✅
- **Audit + fix: conditional EditForm `FormName` mount
  regression** — the STATUS.md Known-quirk flags 5 sites that
  share the recipe `EditForm + FormName + conditional mount +
  InteractiveServer`, all candidates for the
  enhanced-form-mount-breaks-the-SignalR-circuit regression.
  Audit results: 3 sites confirmed conditional (`Games/Detail`
  score form inside `@if(_canManage)`, `OrgTrainingEditor` Add
  inside `@if(_content.Any())`, `SlotTrainingEditor` Add inside
  `@if(_content.Any())`) — `FormName=` + `Context=` dropped from
  each; 2 sites verified clean (`ScheduleSeries.razor` +
  `Teams/Players.razor`). Per-site comments cross-reference the
  Known-quirks entry. ✅

## Feature requests (queued for future rounds; not yet built)

These are user-requested capabilities that have been scoped, library-decided,
and acceptance-criteria'd, but not yet implemented. Each entry is a
self-contained spec the next contributor (or a future round) can pick up
without re-deriving the design.

### Round-FR-1: per-slot printable calendar PDF with QR codes

**User ask (verbatim intent).** Coordinators want to print a one-page (or
few-page) calendar for a `ServiceSlot` that:
1. Lists the slot's upcoming occurrences in a clear month / week / day layout.
2. Embeds an **org-join QR** so a passerby who scans the printed sheet is
   taken to `/Account/Register?token=<orgToken>` and auto-joins the org as a
   volunteer.
3. Embeds a **per-open-occurrence QR** so a volunteer who scans a specific
   time block is taken to that specific occurrence's sign-up flow.
4. Is selectable at three scopes: **month**, **week**, or **single day**.

**Why now.** Coordinators in the seeded org (a small Methodist church) are
already taping handwritten sign-up sheets to the volunteer kiosk; a
printable PDF with QR codes is a one-step upgrade that closes the
"phone-pic-to-sign-up" loop without requiring the volunteer to retype a URL.

**RBAC.** Reuse the existing `IOrgAuthService.CanManageSlotAsync` gate
(slot coord, ministry coord, or org Admin) — same gate that
`ServiceSlots/Schedule.razor` already enforces. The PDF endpoint MUST
re-check server-side; the Razor page MUST hide the "Print calendar" button
for non-managers (same pattern as the Edit/Schedule/Series buttons on
`ServiceSlots/Detail.razor`).

**Library decisions.**
- **PDF: QuestPDF (MIT for non-commercial / community; commercial license
  required for paid deployments).** Fluent layout DSL, fluent text +
  image + table primitives, pure-managed, no native deps. Alternatives
  considered: PdfSharpCore (works but is layout-imperative — much more
  code for tables + grids), iText7 (AGPL — viral license, wrong for a
  volunteer platform that might be forked). If the project ever wants
  commercial-friendly licensing, switch to PdfSharpCore; the
  `ICalendarPdfBuilder` interface below is library-agnostic so the swap
  is a single class.
- **QR: QRCoder (MIT, pure C#).** Emits PNG or SVG; PNG embeds cleanly in
  QuestPDF's `Image` element. Alternatives: Net.Codecrete.QrCodeGenerator
  (Apache-2.0, fine — fallback if QRCoder maintenance lapses).

**Route surface.** New minimal-API endpoint
`GET /Organizations/{OrgId:int}/Ministries/{MinId:int}/Roles/{Id:int}/Calendar.pdf?scope=month&start=2026-07-01&tz=America/Chicago`
→ `Content-Type: application/pdf` stream. The 5 segment matches the
existing `ServiceSlots/Detail.razor` page route exactly so a `<a>` link
copy-pastes cleanly. Query params:
- `scope` ∈ {`month`, `week`, `day`}, default `month`.
- `start` ISO date (`yyyy-MM-dd`), default today in the requested zone.
- `tz` IANA id, default = `Organization.TimeZoneId` (round-AV column) →
  `UserTimeZoneProvider.TimeZoneId` (browser-detected) → `Local` (the
  same 3-tier chain `LocalTime.razor` already uses, just hoisted into
  a static helper `Components.Shared.TimeZoneResolver.ResolveForPdf`).

The page button on `ServiceSlots/Detail.razor` becomes a Bootstrap
`<div class="btn-group">` with three links (one per scope) + a small
zone-picker dropdown whose value becomes the `tz` query param. No JS
required — the dropdown is a plain `<select>` that submits via a
sibling `<form method="get">`.

**PDF layout (per scope).**
- **Cover band (every page):** typographic wordmark in `#3730a3`
  (reusing the round-EMAIL-BRAND text approach — text IS the brand,
  no PNG dependency), org name in 18pt semibold, slot name in 14pt
  regular, "as of <date>" + "Times shown in <tz>" in 9pt muted. A
  thin 1px `#3730a3` rule separates the band from the body.
- **Month view:** 1 page. 5×7 (or 6×7) grid with day-numbered cells;
  each cell lists up to 3 occurrences as `HH:MM–HH:MM` lines +
  `(N more)` overflow. A small filled-circle icon next to a filled
  occurrence distinguishes it from an open one. A "key" row at the
  bottom of the page explains the icons.
- **Week view:** 1 page. 7 columns (Mon–Sun) × N hourly rows
  (configurable 6am–10pm default; coordinator's preference).
  Each cell either empty, or contains one occurrence's
  `HH:MM–HH:MM` line.
- **Day view:** 1 page. Vertical 24-hour timeline with every
  occurrence plotted at its start time, labeled with slot name +
  assigned volunteer (if any) or "open".
- **QR placement:**
  - Org-join QR: bottom-right corner of the cover band, 1.5cm × 1.5cm,
    with 0.5cm caption "Scan to join <OrgName>".
  - Per-open-occurrence QR: rendered inside the day cell or timeline
    row next to the `HH:MM–HH:MM` line, 1.0cm × 1.0cm, with a
    0.3cm caption "Scan to sign up". Filled occurrences do NOT get a
    QR (the row shows "filled — <VolunteerName>" instead).
- **Footer (every page):** "Generated <UTC timestamp> · Verify
  availability before traveling · ServantSync calendar" in 8pt muted,
  centered.

**QR targets + format.**
- **Org-join QR** encodes `${Nav.BaseUri}Account/Register?token=${org.RegistrationToken}`. If
  `org.RegistrationToken` is null (admin hasn't generated one yet),
  the cover-band QR slot becomes a printed-text fallback: "Ask a
  coordinator for the invite link or sign up at
  <Nav.BaseUri>Account/Register". Existing
  `Components/Pages/Organizations/Detail.razor` already shows the
  rotate-confirm warning; same UX.
- **Per-occurrence QR** encodes a new deep link `${Nav.BaseUri}Open?occ=<SlotOccurrenceId>`. The `/Open` page
  already lists open slots; adding `?occ=` makes the new
  `OpenSlotOccurrenceViewModel` resolution auto-scroll to the row +
  pop a "You're signing up for <date/time>" confirmation modal that
  posts to the existing `AssignmentService.SignUpAsync` endpoint.
  No new routes required; the existing `/Open` Razor page gains one
  query param + one `@query` block.
- **Error correction level: M (15% recovery).** Good balance for
  print degradation + a phone camera at 1-2m.
- **Module size: 4px per module.** Quiet zone: 4 modules.

**Time zone default + fallback.** As above (3-tier chain hoisted into
`TimeZoneResolver.ResolveForPdf`). The PDF header always declares
"Times shown in <tz>" so a coordinator who printed the sheet in one
zone and is handing it out in another has the disambiguation in
plain sight.

**Offline-stale-URL handling.** Printed sheets can be stale by the time
the volunteer scans. Two cases:
- *Org-join token rotated between print and scan:* the existing
  `/Account/Register?token=...` page already shows "This invitation
  code didn't match any organization. Please ask the admin who
  shared the link to re-send it." — no new code required. The PDF
  footer adds a "As of <timestamp>" line so a volunteer who sees the
  failure can verify with the coordinator instead of assuming the QR
  is broken.
- *Per-occurrence slot filled between print and scan:* the new
  `?occ=` handler on `/Open` shows the existing open-shift
  confirmation modal but with the existing assignment displayed
  under it; the volunteer sees "this shift is already filled — pick
  another?" instead of being allowed to sign up for a closed slot.
  The PDF footer adds a "Verify before you go" line.

**Edge cases + behavior.**
- *No registration token yet:* cover-band QR slot becomes printed
  text + an instruction pointer to the public register URL.
- *No occurrences in the selected window:* body of the page shows
  "No shifts scheduled in this window. Check back after the
  coordinator adds the next round of assignments." — no QRs.
- *Many occurrences (e.g. 50 in a month):* month view shows up to
  3 per cell + `(N more)` overflow; week + day views have no
  practical cap.
- *Org has no ministry or slot under it (config error):* endpoint
  404s with a "slot not found" page; the same NotFound UX pattern
  `Components/Pages/Organizations/Detail.razor` uses.
- *QR encode-failure (extremely long URL overflows QR capacity):*
  QRCoder throws; the page falls back to the printed-text variant
  + a log warning. Capped at the practical URL-length limit
  (~2000 chars = version 40 QR).

**File naming convention.** Content-Disposition header:
`servantsync-calendar-{orgSlug}-{slotSlug}-{scope}-{startDate}.pdf`
where `{orgSlug}` and `{slotSlug}` are kebab-cased ASCII (non-ASCII
→ ASCII transliteration, falling back to `id-<n>`). Example:
`servantsync-calendar-demo-church-sound-tech-month-2026-07-01.pdf`.

**i18n / localization.** Spec is English-only to match the rest of
the app. The `ICalendarPdfBuilder` accepts a `string culture` param
defaulted to `CultureInfo.CurrentUICulture.Name` so a future
localization pass wires through cleanly without an interface bump.

**Open questions for the user before implementation starts.**
1. **QuestPDF license posture:** is this project's deployment
   non-commercial (churches, schools, non-profits) or commercial
   (paid SaaS)? QuestPDF Community is MIT-licensed; commercial
   deployment requires the paid license. If commercial, prefer
   PdfSharpCore upfront.
2. **Volunteer-name visibility in the PDF:** should the day-view
   timeline show the assigned volunteer's name next to a filled
   occurrence, or just "filled"? Names aid in-the-moment coverage
   ("oh Sara's already on") but leak PII on a publicly-postered
   sheet. Default off, with a coordinator-side toggle to enable.
3. **Multiple slots per PDF:** out of scope for this round
   (spec is per-slot). A "print the whole ministry" or "print the
   whole org" view is a likely follow-up once the per-slot flow
   is battle-tested.
4. **Cover-page customization (org logo upload):** out of scope
   for this round. The text wordmark is the brand.

**Files this round would touch (when implementation starts).**
- NEW: `Services/CalendarPdf/ICalendarPdfBuilder.cs` +
  `QuestPdfCalendarPdfBuilder.cs` (or `PdfSharpCalendarPdfBuilder.cs`
  per the license question).
- NEW: `Components/Shared/TimeZoneResolver.cs` (hoist the 3-tier
  chain from `LocalTime.razor` into a static helper).
- NEW: `Services/CalendarPdf/QrCodeBuilder.cs` (thin wrapper over
  QRCoder for test seam).
- `Program.cs`: register the builder + QR service in DI; add the
  minimal-API endpoint with the auth gate.
- `Components/Pages/ServiceSlots/Detail.razor`: add the
  Print-calendar btn-group with 3 scope links + the zone-picker
  dropdown.
- `Components/Pages/Open.razor`: gain `?occ=` query param handling
  + the "this shift is already filled" disambiguation.
- `Components/Shared/WordmarkSplash.razor`: extract the
  typographic text wordmark into a sibling
  `WordmarkText.razor` (or extend `WordmarkSplash` with a
  `RenderMode="Svg|Text"` param) so the PDF builder can reuse the
  same mark logic that the email + login + register + empty-state
  surfaces use.
- `BRANDING.md`: add a new "Printed material" section cross-linking
  the PDF surface to the same brand tokens + text-wordmark rationale.
- NEW tests: `tests/ServantSync.Tests/CalendarPdfBuilderTests.cs`
  (PDF byte-stream non-empty + page count per scope + brand-text
  present), `tests/ServantSync.Tests/QrCodeBuilderTests.cs` (encodes
  expected URL + reasonable byte size), and a `PageAccessTests` entry
  for the new endpoint's auth gate.

