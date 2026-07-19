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
     a `Role` (Volunteer / MinistryDirector / Admin / SlotCoordinator)
     — Round-FR-5 split the old `Coordinator` into two distinct tiers
     (Ministry Director at the ministry level + Slot Coordinator at
     the slot level) so ministry directors and per-slot coordinators
     have exactly the access they need, scoped to the
     `CoordinatorPersonUserId` field on the entity they're delegated to.
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
- **RBAC (admin / ministry director / slot coordinator)** — `Services/OrgAuthService.cs`
  on top of the custom `OrganizationRole` enum. Replaces Identity
  roles for org-level access control. Round-FR-5 split the legacy
  `Coordinator` tier into `MinistryDirector` (org- or ministry-scoped
  management via `Ministry.CoordinatorPersonUserId`) and
  `SlotCoordinator` (slot-scoped management via
  `ServiceSlot.CoordinatorPersonUserId`), with five new
  IOrgAuthService methods (`IsMinistryDirectorAsync`,
  `IsSlotCoordinatorAsync`, `IsAnyMinistryDirectorAsync`,
  `IsAnySlotCoordinatorAsync`, `IsAnyTrainingManagerAsync`) and
  tightened `CanManageOrgAsync` (Admin-only). ✅
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

**Decisions (open questions resolved during spec authoring session).**
1. **QuestPDF license posture:** **MIT (QuestPDF Community).** ServantSync today ships for small non-profit use (churches, schools, leagues) which is squarely under QuestPDF's MIT Community license. If a future deployment wants commercial-friendly licensing, the swap is mechanical — replace `QuestPdfCalendarPdfBuilder` with a `PdfSharpCalendarPdfBuilder` because the spec routes everything through the library-agnostic `ICalendarPdfBuilder` interface. Config-time concern, not a code change.
2. **Volunteer-name visibility in the PDF:** **default OFF, coordinator-side toggle ON per PDF.** A publicly posted sign-up sheet (think a church vestibule kiosk) leaks name through "filled — Bob Smith", so the day-view timeline shows just "filled" by default. The PDF-generation form on `ServiceSlots/Detail.razor` gains a "Show filled shifts with volunteer names" checkbox (default unchecked) so coordinators can opt-in for internal-style handouts.
3. **Multiple slots per PDF:** **out of scope for this round.** Round-1 is per-slot. "Print the whole ministry" / "print the whole org" is a likely round-2 follow-up once the per-slot flow is battle-tested. Keeping the spec narrow now lets us ship the lib + QR + layout decisions; multi-scope is a UI multiplication, not a fundamental design change.
4. **Cover-page customization (org logo upload):** **out of scope for this round.** The text wordmark IS the brand. Allowing a per-org logo re-introduces the cid-image attachment pipeline that the round-EMAIL-BRAND refactor just deprecated in favor of text. Round-2 followup if needed for orgs that need a custom identifier.

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
  the PDF surface to the same brand tokens + text-wordmark rationale.- NEW tests: `tests/ServantSync.Tests/CalendarPdfBuilderTests.cs` (PDF byte-stream non-empty + page count per scope + brand-text present), `tests/ServantSync.Tests/QrCodeBuilderTests.cs` (encodes expected URL + reasonable byte size), and a `PageAccessTests` entry for the new endpoint's auth gate.

### Round-FR-2: in-person scheduled training sessions with manual-completion audit ✅ SHIPPED → 53d5ec2 (2026-07-07)

**User ask (verbatim intent).** Coordinators want to schedule in-person
training events (date/time/location), let volunteers sign up for those
sessions, and after the session have the coordinator or admin mark
volunteers complete. The system must keep an audit trail showing the
completion was MANUAL (not user-clicked from online training).

**Why now.** The current training pipeline is fully self-paced digital
(PDF / video / slideshow). In-person training is a real-world pattern (a
coach running a clinic, a trainer walking through safety procedures) that
needs to be supported. The "mark complete in person" workflow is
distinct from the engagement-verified online workflow in
`TrainingService.RecordCompletionAsync` and must be auditable.

**Tightly coupled to Round-FR-3.** The user explicitly tied FR-3 to
FR-2 in the same message: *"coordinators can assign [manually-added
volunteers] to scheduled duties if their trainings have been marked as
complete."* That "marked as complete" is FR-2's manual-mark flow. The
two specs should be implemented in order — FR-2 first (the manual-mark
flow), then FR-3 (the stub-Person + claim flow) — because FR-3's stub
duty-assignment feature is gated on FR-2's manual completion.

**RBAC.**
- Coordinator (or Admin) of the org: create / edit / cancel a session;
  mark attendees complete.
- Volunteer (or any org member): sign up for / cancel their own sign-up.
- Coordinator of a different org: no access (server-gated deny).

**Model additions.**

1. **`TrainingSession` (new model).**
   - `Id` (int, PK)
   - `OrganizationId` (FK, required, cascade-delete with org)
   - `TrainingContentId` (FK, optional — the material covered; null if
     the session is "general orientation" with no specific digital content)
   - `Title` (string, 200)
   - `Description` (string?, 2000)
   - `Location` (string, 200) — free-form: "Fellowship Hall", "Field 3", etc.
   - `StartUtc` (DateTime)
   - `EndUtc` (DateTime)
   - `MaxAttendees` (int?, optional capacity)
   - `Status` (enum: `Scheduled`, `Completed`, `Cancelled`)
   - `CreatedByUserId` (string, FK IdentityUser)
   - `CreatedUtc` (DateTime, default UtcNow)

2. **`TrainingSessionAttendee` (new model).**
   - `Id` (int, PK)
   - `TrainingSessionId` (FK, required, cascade-delete with session)
   - `PersonUserId` (FK, required) — links to Person (which may be a
     stub for manually-added volunteers; see Round-FR-3)
   - `SignedUpUtc` (DateTime, default UtcNow)
   - `Attended` (bool?) — null until marker records it; `true` = attended,
     `false` = no-show
   - Unique index on `(TrainingSessionId, PersonUserId)` to enforce the
     one-signup-per-volunteer invariant.

3. **Extend `TrainingCompletion` with 3 new columns.**
   - `CompletionSource` (enum, default `UserOnline`) — values
     `UserOnline` / `CoordinatorManual` / `CoordinatorManualSingle`. The
     existing `RecordCompletionAsync` path writes `UserOnline`; the new
     `MarkAttendeesCompleteAsync` writes `CoordinatorManual`; the new
     `MarkSingleCompleteAsync` writes `CoordinatorManualSingle`.
   - `MarkedCompleteByUserId` (string?, FK IdentityUser) — null for
     `UserOnline`; set to the marker (coordinator or admin) for the
     manual paths.
   - `ManualCompletionNotes` (string?, 1000) — free-form
     *"attended the May 5 session"*, *"showed competence via demo on
     June 3"*, etc.

4. **Extend `Models/Enums.cs` with a new enum.**
   ```csharp
   public enum TrainingCompletionSource
   {
       UserOnline = 0,
       CoordinatorManual = 1,
       CoordinatorManualSingle = 2,
   }
   ```

**Library / service decisions.** Pure C# / EF. No new NuGet dependencies.

**Route surface.**
- `/Organizations/{OrgId:int}/Training/Sessions` — list upcoming + past
  sessions for the org (coordinator view: all; volunteer view: their
  sign-ups only).
- `/Organizations/{OrgId:int}/Training/Sessions/New` — create a new
  session (coordinator/admin only).
- `/Organizations/{OrgId:int}/Training/Sessions/{Id:int}` — session
  detail with sign-up roster + marker UI.
- `/Organizations/{OrgId:int}/Training/Sessions/{Id:int}/Edit` — edit a
  session (coordinator/admin).
- API: `POST /Organizations/{OrgId:int}/Training/Sessions/{Id:int}/MarkComplete`
  — coordinator/admin only; body = list of `{personUserId, attended, notes}`;
  the service applies the completion rows.

**Calendar integration.** In-person sessions appear on the existing
`Components/Shared/AssignmentCalendar.razor` (org-wide view) as a
distinct event class with a "training" badge so they don't visually
compete with regular assignments. A future round could add a dedicated
"training calendar" view; out of scope for round 1.

**Service additions: `ITrainingSessionService`.**
- `ListUpcomingAsync(orgId)` — scheduled sessions in the next 60 days,
  eager-loaded `TrainingContent` + signed-up `Attendees`.
- `ListPastAsync(orgId, sinceUtc)` — completed / cancelled sessions.
- `CreateAsync(organizationId, title, description, location, startUtc, endUtc, maxAttendees?, trainingContentId?, callerUserId)`
  — coordinator/admin gate; returns `Created` / `PermissionDenied` /
  `ValidationFailed` enum.
- `SignUpAsync(sessionId, personUserId)` — volunteer self-service;
  refuses when the session is full or the volunteer is already signed up.
- `CancelSignUpAsync(sessionId, personUserId)` — volunteer self-service.
- `MarkAttendeesCompleteAsync(sessionId, markerUserId, attendeeResults, notes)`
  — coordinator/admin; for each `attended=true` entry, inserts a
  `TrainingCompletion` with `CompletionSource=CoordinatorManual` and
  `MarkedCompleteByUserId=markerUserId` (only when the session has a
  `TrainingContentId`); updates `TrainingSessionAttendee.Attended` for
  all entries regardless.
- Extend `Services/TrainingService.cs` with `MarkSingleCompleteAsync(
  contentId, personUserId, markerUserId, notes)` — coordinator/admin
  ad-hoc *"this volunteer knows the material without a session"*; inserts
  one `TrainingCompletion` with `CompletionSource=CoordinatorManualSingle`.

**UI changes.**
- NEW: `Components/Pages/Training/Sessions/{Index,Detail,New,Edit}.razor`.
- Extend `Components/Pages/Training/Manage.razor` with a "In-person
  sessions" tab.
- Extend `Components/Shared/AssignmentCalendar.razor` to render
  `TrainingSession` events as a separate class with a "training" badge.
- Extend `Components/Pages/Training/Take.razor` with a "Mark as completed
  by a coordinator" admin button (for `CoordinatorManualSingle`) on the
  coordinator view.

**Audit / reporting.**
- New "Audit log" panel on `Components/Pages/Organizations/Detail.razor`
  (or a dedicated `/Organizations/{Id:int}/Audit` page) that shows every
  `TrainingCompletion` with `CompletionSource != UserOnline`, listing
  the volunteer, content, when marked, by whom, and notes. Visible to
  org Admin only.

**Edge cases + behavior.**
- *No attendees:* session can be created + completed with 0 attendees.
- *Capacity full:* refuse sign-ups; no wait list in round 1.
- *Session time in past at creation:* warn via UI confirmation; do not block.
- *Mark-complete partial:* marker can mark some `attended=true`, some
  `attended=false` (no-shows); only `attended=true` rows get the
  `TrainingCompletion`.
- *Re-mark after manual completion:* the existing `TrainingCompletion`
  row is updated (marker + timestamp overwritten). The audit captures the
  latest mark. Round 2 could introduce a history table if multi-mark
  history is needed.
- *Session has no `TrainingContentId`:* the marker still records
  attendance (sets `TrainingSessionAttendee.Attended`) but no
  `TrainingCompletion` row is created — there's no content to complete.
- *Volunteer cancels after being marked attended:* the cancellation is
  refused; markers can't un-mark without going through an admin.

**Decisions (open questions resolved during spec authoring session).**
1. **Session capacity:** **ENFORCE.** Sign-ups past `MaxAttendees` are refused with a "this session is at capacity" message. Consistent with the existing `ServiceSlot.MaxVolunteers`-style limits already in the schema.
2. **Wait list:** **out of scope for round 1.** A wait list is a 5-state machine (pending / offered / accepted / declined / expired) — too heavy for round 1. Logged follow-up if a coordinator runs into the frequent-full-session pattern.
3. **Reminder notifications:** **out of scope for round 1.** MailKit is wired and a 24h-before-session `SendAsync` would be ~30 lines, but the user has explicitly deferred notification features in other rounds. Stay consistent here.
4. **Recurring sessions:** **out of scope for round 1.** `AssignmentService.ScheduleSeriesAsync` already exists for the assignment side; reusing it for training-session recurrence is a round-2 opportunity (sign-up join scope is the complexity).
5. **Manual mark notes:** **REQUIRED.** The audit-trail distinction only works if every manual mark has a non-empty reason. The marker form's notes textarea is `required` + non-empty validation; submit-with-empty-notes raises an inline error pointing at the policy.
6. **Engagement-gate bypass on manual mark:** **YES (bypass).** The whole point of FR-2 is that the marker asserts "yes this volunteer knows the material" out-of-band. The training-eligibility gate (PDF every page, video 95%, slideshow 80% dwell) is bypassed for manual marks; the marker accepts responsibility for that judgment. This is what makes the manual flow different from `RecordCompletionAsync`.
7. **Re-mark semantics:** **latest-wins.** Round 1's `MarkSingleCompleteAsync` upserts a single `TrainingCompletion` row. The audit trail comes from `MarkedCompleteByUserId` + `ManualCompletionNotes` + `TriagedUtc` columns. If multi-mark history becomes a real need, add a `TrainingCompletionAudit` table without breaking round 1's column shape.

**Files this round would touch (when implementation starts).**
- NEW models: `Models/TrainingSession.cs`, `Models/TrainingSessionAttendee.cs`;
  extend `Models/TrainingCompletion.cs` with 3 new columns.
- NEW enum value: extend `Models/Enums.cs` with `TrainingCompletionSource`.
- NEW service: `Services/TrainingSessionService.cs` +
  `ITrainingSessionService.cs`.
- Extend `Services/TrainingService.cs` with `MarkSingleCompleteAsync`.
- NEW pages: `Components/Pages/Training/Sessions/{Index,Detail,New,Edit}.razor`.
- Extend `Components/Pages/Training/Manage.razor` +
  `Components/Shared/AssignmentCalendar.razor` +
  `Components/Pages/Training/Take.razor`.
- Extend `Components/Pages/Organizations/Detail.razor` with the
  audit-log panel.
- NEW migration: `AddTrainingSessions`.
- NEW tests: `tests/ServantSync.Tests/TrainingSessionServiceTests.cs`;
  extend `tests/ServantSync.Tests/TrainingServiceTests.cs` for
  `MarkSingleCompleteAsync`.

### Round-FR-3: manually-added volunteers with account linking ✅ SHIPPED → 11e0235 (2026-07-06)

**User ask (verbatim intent).** Org admins want to add volunteers to
the system before those volunteers have an email address or
self-registered. The admin enters the volunteer's name (and optional
email/phone) and assigns them to ministries; coordinators can assign
them to scheduled duties if their training has been marked complete
(per Round-FR-2's manual-completion flow). When the volunteer eventually
self-registers at `/Account/Register`, they can "link" their new account
(with email + password) to the existing manual record, and their
historical record (memberships, assignments, training completions) follows
them.

**Why now.** Many volunteer coordinators know their volunteers by name
long before the volunteers have email accounts (children, elderly,
no-email relatives, members without stable email access). A manual-add
flow closes the gap between *"Sara is the head of Greeters"* and
*"Sara has an account she can log in with"* — without the coordinator
re-entering everything when Sara finally arrives at the kiosk.

**Tightly coupled to Round-FR-2.** The user explicitly said
*"coordinators can assign them to scheduled duties if their trainings
have been marked as complete."* That "marked as complete" is FR-2's
manual-mark flow. The two specs are inseparable in practice and should
be implemented in order: FR-2 first (the manual-mark flow), then FR-3
(the stub-Person + claim flow). FR-3 depends on FR-2's
`MarkSingleCompleteAsync` being available for stub Persons; the
existing `AssignmentService.ValidateAsync` training-gate already works
on `PersonUserId` as a string FK, so no changes to the gate are needed
for FR-3.

**RBAC.**
- Org Admin: create / edit / cancel stub Persons in their org; generate
  a `PersonClaimToken`.
- Coordinator of the org's ministry: can assign stub Persons to scheduled
  duties IF the stub has a `TrainingCompletion` row for the slot's
  required content (per FR-2's manual-mark path).
- Volunteer: can register at `/Account/Register` and consume a
  `PersonClaimToken` to claim a stub.
- Coordinator / Admin of a different org: no access (server-gated deny).

**Model additions.**

1. **Extend `Person` with two columns.**
   - `IsStub` (bool, default `false`) — true for manually-added records
     with no linked `IdentityUser`. The existing `UserId` (PK + FK to
     `IdentityUser.Id`) is still set — the stub points to a placeholder
     `IdentityUser` created at stub creation (random unusable password,
     `LockoutEnabled=true`, `LockoutEnd=9999-12-31`). The `IsStub` flag
     is the gate for "can this person log in?" in the auth pipeline
     (always refuse login for stubs).
   - `Email` (string?, EmailAddress, indexed) — captured at stub
     creation. The `IdentityUser.Email` is the source of truth once a
     stub is claimed, but the stub's email lives on `Person` for the
     email-match secondary claim flow.

   **Why `IsStub` over nullable `UserId`:** nullable FK is a breaking
   schema change (the PK is the FK; relaxing it ripples to every join +
   every FK chain). The `IsStub` boolean is a one-time additive
   migration with no FK changes. Round 1 ships `IsStub`; a future round
   can refactor to nullable `UserId` if a code-path-cleaner signal is
   needed.

2. **New `PersonClaimToken` (new model).**
   - `Id` (int, PK)
   - `PersonUserId` (string, FK Person.UserId, required) — the stub
     Person this token claims
   - `TokenHash` (string, indexed unique) — SHA-256 of a 32-byte
     cryptographically-random token; raw token is never stored (only
     returned to the admin ONCE at creation / rotation)
   - `CreatedUtc` (DateTime, default UtcNow)
   - `ExpiresUtc` (DateTime, default UtcNow + 30 days) — admin can
     rotate to extend
   - `ClaimedUtc` (DateTime?, nullable) — set when the token is
     consumed; repurposed in round 1 to also mean "rotated" (any
     terminal state). Round 2 could split this into a separate
     `IsRevoked` column for clarity.
   - `CreatedByUserId` (string, FK IdentityUser) — the admin who
     created the stub
   - Unique index on `TokenHash`.

3. **No change to `OrganizationMembership`, `Assignment`,
   `TrainingCompletion`, `TrainingActivity`.** The `PersonUserId` FK is
   a string and works whether the Person has a real IdentityUser or is
   a stub. The existing `AssignmentService.ValidateAsync` training-gate
   works for stubs unchanged because it reads `PersonUserId` as a string
   without caring about the IdentityUser state.

**Library / service decisions.** Pure C# / EF / ASP.NET Core Identity
(for the `UserManager.CreateAsync` + `SignInManager` calls). No new
NuGet dependencies.

**Route surface.**
- `/Organizations/{OrgId:int}/Members/AddStub` — admin-only; form to
  create a stub Person + display the claim token ONCE.
- `/Organizations/{OrgId:int}/Members/Stubs` — admin-only; list of
  stub Persons with claim status (active token / claimed / no token).
- `/Account/Register?claim={token}` — Register page accepts an optional
  `?claim=` param; if present, after successful registration the stub
  is claimed and the volunteer is signed in.
- `/Account/Claim/Confirm?token={token}` — confirmation page for the
  email-match secondary flow (admin can pre-approve or decline).

**Service additions: `IPersonService`.**
- `CreateStubAsync(organizationId, firstName, lastName, email, phone,
  callerUserId)` — admin-only; creates a placeholder `IdentityUser`
  (random unusable password, lockout-future) + a stub `Person`
  (`IsStub=true`) + an `OrganizationMembership(Role=Volunteer)` in
  the calling org; returns the new `Person` + the raw claim token
  (shown ONCE to the admin for the in-person handoff).
- `RotateClaimTokenAsync(personId, callerUserId)` — admin-only;
  invalidates the previous token (sets its `ClaimedUtc` = rotation
  timestamp), creates a new one. Old token cannot be claimed after
  rotation.
- `ListStubsAsync(organizationId)` — admin-only; returns every stub
  Person in the org with their current claim status.
- `ClaimStubAsync(claimToken, newIdentityUserId, newEmail)` — public;
  called by the Register page when the volunteer pastes the token
  during registration. Validates the token (not expired, not claimed);
  re-parents the stub's `Person.UserId` to the new IdentityUser;
  flips `IsStub=false`; sets `ClaimedUtc=now`; returns the merged
  Person.
- `LinkByEmailPromptAsync(identityUserId)` — alternative flow: if the
  new IdentityUser's email matches a stub's `Email` column, prompt
  the volunteer to confirm linking on first login. Admin can
  pre-approve or decline.

**Claim-time merge strategy (the key implementation decision).**
Re-parent, not copy. Round 1's recommendation: when the volunteer
registers with a claim token, the existing `PersonClaimToken` row's
stub `Person` is updated in one query: `UPDATE People SET UserId =
@newIdentityUserId, IsStub = 0, Email = @newEmail WHERE UserId =
@oldStubUserId`. Every FK chain (OrganizationMembership,
Assignment, TrainingCompletion, TrainingActivity) references
`Person.UserId` as a string — re-parenting in one UPDATE preserves
all FKs without copy logic. The placeholder `IdentityUser` is
soft-deleted (`LockoutEnabled=true`, `LockoutEnd=9999-12-31`) so no
one can log in with it.

**UI changes.**
- Extend `Components/Pages/Organizations/Detail.razor` Members tab with
  a "Add manually" button (admin-only) and a "Stub members" subsection.
- Extend `Components/Account/Pages/Register.razor` with a `?claim=`
  query param handling.
- NEW: `Components/Pages/Organizations/Members/{AddStub,Stubs}.razor`.

**Audit.**
- Every stub creation writes a `OrganizationMembership.Notes` entry:
  *"Stub created by {admin} on {date}"*.
- Every stub claim writes a `OrganizationMembership.Notes` entry:
  *"Claimed by {newUserId} on {date}"*.
- The audit-log panel (FR-2) also surfaces stub-claim events for the
  org admin.

**Edge cases.**
- *Email match collision:* if two stubs share the same email, the
  Register page warns and refuses to auto-link (admin resolves
  manually).
- *Stub already claimed:* the token check returns `AlreadyClaimed`;
  the Register page falls back to creating a new account (the stub's
  history is left orphaned — admin can manually merge).
- *Stub Person has a different email at registration:* the merge
  happens by token (primary) or by email-match (secondary); the
  stub's stored `Email` is updated to the new email on claim.
- *Coordinator assigns stub to duty before training:* the existing
  `AssignmentService.ValidateAsync` enforces the training gate; the
  stub's `PersonUserId` is a regular string FK — the existing
  training-gate query works without modification.
- *Volunteer with stub account declines to claim:* the stub remains
  orphaned; admin can delete the stub (cascade-deletes memberships /
  assignments / completions) or leave it for future claim.
- *Stub gets claimed by an account that ends up in a different org:*
  the Person's OrganizationMemberships are preserved; the new
  IdentityUser just gets read-access to those orgs as the Person. No
  cross-org leakage.
- *Token is rotated while a volunteer's invitation is in flight:* the
  old token's `ClaimedUtc` is set to the rotation timestamp
  (terminal-state repurposed); the new token is the only valid path.
- *A stub's underlying `IdentityUser` placeholder is somehow deleted:*
  the stub becomes orphaned; the admin can re-create a placeholder
  via a "Re-key stub" button that generates a new `IdentityUser` and
  re-points the stub's `UserId`.

**Decisions (open questions resolved during spec authoring session).**
1. **Claim mechanism:** **BOTH token-primary + email-secondary.** Token via printed-paper handoff is more secure, works for child/elderly/no-email volunteers, and is admin-controlled (30-day expiry, rotation). Email-match-on-first-registration is the secondary prompt — if the new `IdentityUser`'s email matches a stub's stored `Email` column, the volunteer sees a "looks like this might match Sara Smith, confirm linking?" prompt on first login (with explicit user confirmation, never silent auto-merge). Admin chooses which one to hand out.
2. **Stub data on claim:** **MERGE via re-parent.** One UPDATE on `People SET UserId = @newIdentityUserId, IsStub = 0, Email = @newEmail` re-parents the stub's historical record (memberships, assignments, training completions) to the new `IdentityUser` because every FK chain references `Person.UserId` as a string. The placeholder `IdentityUser` is soft-deleted (`LockoutEnabled=true`, `LockoutEnd=9999-12-31`) so it can't be logged into.
3. **Stub assigned to duties without ANY training:** **NO.** The flow is `admin adds stub → coordinator marks training (via FR-2's `MarkSingleCompleteAsync`) → coordinator assigns duty`. The existing `AssignmentService.ValidateAsync` training-gate works for stubs unchanged because it reads `PersonUserId` as a string FK. FR-2 unlocks this flow; FR-3 doesn't bypass the training gate.
4. **Coordinator (not admin) creating stubs:** **NOT ALLOWED.** User explicitly said "organization admin". Matches the RBAC tier that `MemberManagementService.AddAsync` already uses for adding real members (round-7 admin-only closure per STATUS.md).
5. **`Person.UserId` nullable vs. `IsStub` boolean:** **KEEP `IsStub` boolean.** The PK=FK pattern (`Person.UserId` is both) is load-bearing throughout the codebase; relaxing it to nullable cascades through every EF relationship. The boolean flag is a one-time additive migration; nullable is a future round-2 refactor if a code-cleaner signal is genuinely needed.
6. **Stub TTL:** **none in round 1.** Admin manually deletes orphaned stubs (via the same round-7 `MemberManagementService.RemoveAsync` path that handles real-member deletions). A "stale stub cleanup" job is a round-2 followup.
7. **Re-parent vs. copy on claim:** **RE-PARENT** (consistent with Q2's decision). One UPDATE on `Person.UserId` re-parents in-place; the copy alternative would create data-merge headaches if a future claim path forgets to copy a related table. The single-UPDATE approach keeps the false-negative surface tiny.

**Files this round would touch (when implementation starts).**
- NEW models: `Models/PersonClaimToken.cs`; extend `Models/Person.cs`
  with `IsStub` + `Email` columns.
- NEW service: `Services/PersonService.cs` + `IPersonService.cs`.
- NEW pages: `Components/Pages/Organizations/Members/{AddStub,Stubs}.razor`.
- Extend `Components/Pages/Organizations/Detail.razor` Members tab.
- Extend `Components/Account/Pages/Register.razor` with `?claim=`
  handling.
- Extend `Program.cs` `/Account/PerformRegister` endpoint to call
  `ClaimStubAsync` after `SignInAsync` if a `?claim=` token is present.
- Extend `Components/Pages/Account/Pages/Login.razor` with the
  email-match secondary prompt.
- NEW migration: `AddPersonClaimTokens` (+ `IsStub` + `Email` columns
  on `Person`).
- NEW tests: `tests/ServantSync.Tests/PersonServiceTests.cs`; extend
  `tests/ServantSync.Tests/PageAccessTests.cs` for the new
  admin-gated routes.

### Round-FR-4: public feature request form (anyone can submit)

**User ask (verbatim intent).** Anyone — a visitor, a volunteer, a
coordinator, a curious passerby — should be able to fill out a
feature request form on the public site and have it land in a
triage queue for the product team to review.

**Why now.** The user has been queuing feature requests verbally
in this session (Round-FR-1, FR-2, FR-3). A public form gives end
users a low-friction way to add their own feature requests,
creating a feedback channel from real users to the product team
and closing the loop between *"I have an idea"* and *"the
coordinators know about it"*. The triage queue also creates a
back-channel for the queued PLAN.md specs: admins can mark an
inbound request as "tracked in Round-FR-2" so the submitter
sees their idea has been heard even before any code ships.

**RBAC.**
- **Anyone (unauthenticated or authenticated):** submit a feature
  request via `/feedback` (the public form).
- **SystemAdmin:** view the full triage queue at
  `/SystemAdmin/FeatureRequests`, change status, add triage notes,
  link to an existing PLAN.md spec.
- **OrgAdmin:** read-only view filtered to requests that mention
  their org (a "my org" filter chip on the SystemAdmin view; the
  matching is fuzzy on the title + description text).
- **Volunteer / Coordinator:** no access to the triage view; the
  public submit form is the only surface they see.

**Model additions.**

1. **`FeatureRequest` (new model).**
   - `Id` (int, PK)
   - `Title` (string, 200) — short summary, shown in the triage list
   - `Description` (string, 4000) — free-form detailed request
   - `SubmitterEmail` (string?, 120, EmailAddress) — optional, for
     admin follow-up
   - `SubmitterName` (string?, 80) — optional, displayed in the
     admin view
   - `SubmitterUserId` (string?, FK IdentityUser, nullable) — set
     when the submitter was logged in; null for anonymous
   - `SubmitterIpAddress` (string, 64) — for spam rate-limiting
   - `Status` (enum `FeatureRequestStatus`, default `New`)
   - `CreatedUtc` (DateTime, default UtcNow)
   - `TriagedByUserId` (string?, FK IdentityUser) — the SystemAdmin
     who triaged
   - `TriagedUtc` (DateTime?) — set when the status changes from
     `New` to anything else
   - `TriageNotes` (string?, 2000) — admin's reasoning (e.g. *"this
     overlaps with Round-FR-2"*, *"declined because X"*)
   - `LinkedSpecAnchor` (string?, 200) — optional link to a PLAN.md
     spec (e.g. *"Round-FR-2"* or *"Round-FR-1"*); when set, the
     admin view shows a *"See PLAN.md#round-fr-2-…"* link
   - `VoteCount` (int, default 0) — reserved for round 2 public
     voting; round 1 leaves the column at 0

2. **Extend `Models/Enums.cs` with a new enum.**
   ```csharp
   public enum FeatureRequestStatus
   {
       New = 0,
       UnderReview = 1,
       Planned = 2,
       Completed = 3,
       Declined = 4,
       Duplicate = 5,
   }
   ```

**Library / service decisions.** Pure C# / EF / ASP.NET Core
Identity. No new NuGet dependencies. Rate-limiting uses the
built-in .NET 8+ `Microsoft.AspNetCore.RateLimiting` middleware
(`AddRateLimiter` + a fixed-window policy keyed on remote IP).

**Route surface.**
- `/feedback` (public) — the submission form. No auth required.
  Renders a hero header with the brand wordmark
  (`Components/Shared/WordmarkSplash.razor` at 140 px) + the form.
- `/feedback/thanks` (public) — thank-you page after submit;
  renders the brand wordmark + a *"Your request has been received"*
  message + a link back to `/`.
- `/SystemAdmin/FeatureRequests` (SystemAdmin only) — triage queue
  with status filter chips (All / New / UnderReview / Planned /
  Completed / Declined / Duplicate) and a sort toggle
  (newest-first / oldest-first).
- `/SystemAdmin/FeatureRequests/{Id:int}` (SystemAdmin only) —
  detail view with the submitter info, the full description, the
  triage form (status dropdown + notes textarea + linked-spec
  input), and a *"Mark as Duplicate of {spec}"* quick action that
  pre-fills the linked-spec anchor.
- API: `POST /api/feedback/submit` (public, anti-forgery + rate-limited)
  — the submission endpoint. Body: `title`, `description`,
  `submitterEmail?`, `submitterName?`, `__RequestVerificationToken`,
  `website` (honeypot, must be empty).

**Anti-spam strategy (3 layers).**
1. **Honeypot field** — a hidden `<input name="website" />` that's
   invisible to humans (CSS `display:none` + `tabindex=-1` +
   `autocomplete=off`); bots fill every field, humans don't see it.
   If the field is non-empty on submit, the endpoint silently
   returns `200 OK` (so the bot thinks it succeeded) but does NOT
   persist the row. The honeypot value is the strongest single
   filter against naive bots; combined with rate limiting it covers
   the realistic threat model for a low-traffic volunteer platform.
2. **IP-based rate limit** — a fixed-window policy via
   `Microsoft.AspNetCore.RateLimiting`: max 3 submissions per IP
   per hour. Exceeded → `429 Too Many Requests` with a friendly
   *"Please try again in {minutes} minutes"* message. The limiter
   keyed on `HttpContext.Connection.RemoteIpAddress` so a single
   user behind a NAT doesn't burn all three slots in a school
   computer lab.
3. **Email confirmation (optional, round 2).** When a submitter
   provides an email, send a confirmation link; the row stays in
   `Status = New` until the link is clicked. Round 1 ships the
   confirmation flow stub (the email template is wired) but
   defaults the feature OFF so a casual visitor doesn't have to
   confirm-by-email just to make a feature request.

No captcha in round 1. The honeypot + rate limit combination is
sufficient for the threat model; if spam becomes a problem, add
reCAPTCHA v3 (invisible) in round 2.

**Email notifications.**
- On new submission, notify all SystemAdmin users via the existing
  `MailKitEmailSender` pipeline (the round-EMAIL-BRAND branded
  wordmark template is reused). Email body: title, description
  preview (first 280 chars), submitter name + email, link to
  `/SystemAdmin/FeatureRequests/{Id}`.
- No submitter notification on triage (round 1). Round 2 could
  email the submitter when their request moves to `Completed` or
  `Declined` if the submitter provided an email.

**Service additions: `IFeatureRequestService`.**
- `SubmitAsync(title, description, submitterEmail, submitterName,
  submitterUserId, ipAddress, honeypotValue)` — public; returns
  `Submitted` / `RateLimited` / `ValidationFailed` / `HoneypotTriggered`
  enum. Validates: title (5-200 chars), description (20-4000 chars),
  email format if provided. Persists the row on success. The
  honeypot branch returns `Submitted` to the caller (so the bot
  sees a success response) but does not persist.
- `ListAsync(statusFilter?, sortBy, page, pageSize)` — SystemAdmin;
  returns paged list of `FeatureRequest` rows with eager-loaded
  `TriagedByPerson` (so the admin can see the triager's display
  name without an N+1).
- `GetAsync(id)` — SystemAdmin; returns a single request.
- `TriageAsync(id, newStatus, notes, linkedSpecAnchor, triagedByUserId)`
  — SystemAdmin; updates status + audit fields. Returns
  `Updated` / `NotFound` / `PermissionDenied` enum. The audit
  fields (`TriagedByUserId`, `TriagedUtc`, `TriageNotes`,
  `LinkedSpecAnchor`) are updated on every transition; a future
  round could introduce a separate `FeatureRequestAudit` table if
  multi-triage history is needed (round 1: latest-wins).
- `ListForOrgAsync(orgId, page, pageSize)` — OrgAdmin; filters
  requests whose title or description fuzzy-matches the org's
  name. Useful for "what are people saying about my org?".

**UI changes.**
- NEW: `Components/Pages/Feedback.razor` — the public form. Renders
  the brand wordmark + the form fields (title, description,
  submitter email + name, honeypot). Posts to
  `/api/feedback/submit` via a plain HTML form (round-AN pattern:
  no `@rendermode InteractiveServer` needed; static-SSR + minimal
  API endpoint avoids the round-AN cookie-write race).
- NEW: `Components/Pages/FeedbackThanks.razor` — thank-you page.
- NEW: `Components/Pages/SystemAdmin/FeatureRequests/{Index,Detail}.razor`
  — triage queue + detail.
- Extend `Components/Pages/SystemAdmin/Index.razor` with a
  *"Feature requests (N new)"* link, where `N` is the count of
  `Status = New` rows.

**Coupling to existing PLAN.md specs.** When triaging, the admin
can set `LinkedSpecAnchor` to a spec name (e.g. *"Round-FR-2"*) and
the detail view shows a *"See PLAN.md#round-fr-2-…"* link. This
creates a feedback loop: the queued specs in PLAN.md become
findable from the triage UI, and new submissions can be marked
as *"Duplicate of Round-FR-2"* so the submitter sees their idea
has been heard. The reverse direction is also useful: when a
spec in PLAN.md is implemented, the admin can run a search
*"FeatureRequests where LinkedSpecAnchor = 'Round-FR-2' AND Status
in (New, UnderReview, Planned)"* and bulk-transition them to
`Completed`, emailing the submitters (round 2).

**Edge cases.**
- *Empty title or description:* validation fails before persist.
- *Title too short / too long:* validation fails.
- *Rate limit exceeded:* 429 response with friendly message.
- *Honeypot triggered:* silent 200 + `HoneypotTriggered` enum;
  no row persisted.
- *Submitter is logged in:* `SubmitterUserId` is captured for
  cross-reference with the existing user table.
- *Admin triages their own request:* allowed (admins can submit
  too).
- *Email format invalid:* validation fails before persist.
- *Status transitions:* round 1 uses a permissive model — any
  status can move to any other status (admin override). Round 2
  could introduce a state machine if the workflow gets more
  complex. The audit fields track the latest transition; if
  multi-triage history is needed, a `FeatureRequestAudit` table
  can be added without breaking the existing column shape.
- *Submission during server maintenance:* the static-SSR form
  renders without a Blazor circuit; if the server is down, the
  user sees the standard 503 page (the same UX any other public
  page has).

**Decisions (open questions resolved during spec authoring session).**
1. **Public visibility:** **round 1 admin-only; round 2 public-with-voting.** Round 1 keeps the trust boundary tight (admin-only triage queue; submitter doesn't see "declined" reasons from internal discussion; no upvote-bombing surface). Round 2 ships the public-visibility + voting decision after round-2's submitter-email-confirmation flow lands.
2. **Captcha:** **NO in round 1.** The honeypot + IP rate limit (3/hour) is sufficient for low-traffic internal volunteer platforms. Add reCAPTCHA v3 only if real spam is measured post-launch — at that point the add is mechanical (one middleware + a config key).
3. **Email validation:** **OPTIONAL in round 1.** Casual visitors can submit without email (the field is nullable). Round 2 adds a confirmation email-link flow once the admin flips the org's setting. This matches the standard "low-friction public form" UX pattern.
4. **Auto-link to existing PLAN.md spec:** **MANUAL in round 1; round 2 fuzzy-match auto-suggest.** Admin types the anchor (`"Round-FR-2"` etc.) manually — a 5-second admin action vs a real-time fuzzy-index engine. Round 2 ships auto-suggest if the manual path proves friction-heavy.
5. **Voting:** **NO in round 1; `VoteCount` column reserves the schema spot.** Round 2 ships the public-vote surface after round-2's public-visibility decision is locked. Vote-bombing detection + "I voted" cookie management both come with round 2.
6. **Status transition rules:** **PERMISSIVE with audit trail.** Round 1 allows any → any transition (admin override). The audit fields (`TriagedByUserId`, `TriagedUtc`, `TriageNotes`, `LinkedSpecAnchor`) capture every transition; round 2 can layer a state machine on top without losing round-1 history.
7. **Notification on triage:** **NO in round 1.** Until the round-2 submitter-email-confirmation flow lands, the submitter email is unverified — we'd be emailing unverified recipients. Round 2 with confirmed email is the right place.

**Files this round would touch (when implementation starts).**
- NEW model: `Models/FeatureRequest.cs`.
- NEW enum: `FeatureRequestStatus` in `Models/Enums.cs`.
- NEW service: `Services/FeatureRequestService.cs` +
  `IFeatureRequestService.cs`.
- NEW pages: `Components/Pages/Feedback.razor`,
  `Components/Pages/FeedbackThanks.razor`,
  `Components/Pages/SystemAdmin/FeatureRequests/{Index,Detail}.razor`.
- Extend `Components/Pages/SystemAdmin/Index.razor` with the
  *"Feature requests (N new)"* link.
- NEW API endpoint: `MapPost("/api/feedback/submit", ...)` in
  `Program.cs`, registered with the rate-limiter middleware.
- NEW: `Services/RateLimiter.cs` (or use
  `Microsoft.AspNetCore.RateLimiting` directly via
  `AddRateLimiter` in `Program.cs`).
- Email notification: extend `Services/MailKitEmailSender.cs`
  with a `NotifySystemAdminsOfNewFeatureRequestAsync(...)` helper
  (or new `Services/FeatureRequestNotifier.cs`).
- NEW migration: `AddFeatureRequests`.
- NEW tests: `tests/ServantSync.Tests/FeatureRequestServiceTests.cs`
  (submit, validation, rate-limit simulation via
  `MemoryCache`-backed counter, triage transitions, honeypot
  silent-reject). Extend `tests/ServantSync.Tests/PageAccessTests.cs`
  for the new SystemAdmin-gated routes.

### Round-FR-5: role reorganization — Ministry Director + Slot Coordinator + role-based dashboards ✅ SHIPPED → a1bee3e (2026-07-08)

**User ask (verbatim intent).** Reorganize the role hierarchy so the
separation of concerns is more distinct:
- **App Admin (SystemAdmin)** — no changes.
- **Organization Admin** — no changes.
- **Ministry Director** (renamed from the current Coordinator role) —
  oversees one or more specific ministries they've been assigned to
  (e.g. "director of AWANA", "director of the soccer league ministry").
  Can manage their assigned ministries and sub-ministries transitively,
  and can add to the training catalog for their organization.
- **Slot Coordinator** (new role) — manages individual service slots
  under a ministry. Coordinates volunteers and times for those slots.
  Sits under the Ministry Director in the hierarchy.
- **Volunteer** — no changes.

The user also wants role-specific dashboard views:
- Organization Admin dashboard (already exists)
- Ministry Director dashboard (new — shows assignments across assigned ministries)
- Slot Coordinator dashboard (new — shows assignments across assigned slots)

**Why now.** The current "Coordinator" role is too broad — it grants
org-wide management access alongside Admin. Real-world churches have
ministry-specific directors (AWANA, worship, children's) who should only
see and manage their own areas, and per-slot coordinators (greeter lead,
welcome desk lead) who should see even less. Splitting the role into
Ministry Director and Slot Coordinator gives each tier exactly the access
they need without over-provisioning.

**Role hierarchy (descending authority).**

| Role | Scope | Dashboard | Nav links |
|------|-------|-----------|-----------|
| SystemAdmin | All orgs (visibility only; no write bypass) | Sees everything | System admin link |
| Admin | Full org management | All org assignments | Organizations, Volunteers, Leagues, Dashboard, In-person training |
| MinistryDirector | Assigned ministries + sub-ministries (transitive) | Assignments under their ministries | Dashboard, In-person training |
| SlotCoordinator | Assigned slots only | Assignments for their slots | Dashboard only |
| Volunteer | No management | N/A | Training, My schedule, Browse open slots |

**Key design principle.** The `OrganizationRole` enum value controls UI
visibility (which nav links, which dashboard type). Actual management
authority for specific entities comes from the `CoordinatorPersonUserId`
field on `Ministry` / `ServiceSlot`. A Ministry Director gets their
management scope from BOTH their membership role AND their
`CoordinatorPersonUserId` assignment on specific ministries. A Slot
Coordinator gets their scope from their `CoordinatorPersonUserId`
assignment on specific slots.

**Enum changes (`Models/Enums.cs`).**
```csharp
public enum OrganizationRole
{
    Volunteer = 0,
    MinistryDirector = 1,  // renamed from Coordinator (same value — zero DB migration impact)
    Admin = 2,
    SlotCoordinator = 3,   // NEW
}
```

The rename from `Coordinator` to `MinistryDirector` keeps the same
underlying value (1), so existing database rows are unaffected. The new
`SlotCoordinator = 3` is purely additive.

**Authorization changes (`Services/OrgAuthService.cs`).**

| Method | Before | After | Reason |
|--------|--------|-------|--------|
| `CanManageOrgAsync` | Admin \|\| Coordinator | **Admin only** | Ministry Directors manage ministries, not the whole org |
| `IsOrgAdminAsync` | Admin only | No change | — |
| `IsAnyOrgAdminAsync` | Admin in any org | No change | — |
| `IsAnyOrgManagerAsync` | Admin \|\| Coordinator | Admin \|\| MinistryDirector \|\| SlotCoordinator | Drives dashboard + nav visibility |
| `CanManageMinistryAsync` | Admin\|\|Coordinator \|\| ministry CoordPersonUserId \|\| parent Coord | Same logic, but now checks Admin\|\|**MinistryDirector** instead of Admin\|\|Coordinator | Ministry scope is already entity-assignment-based — no change to the entity-level checks |
| `CanManageSlotAsync` | Slot CoordPersonUserId \|\| delegates to CanManageMinistry | No change needed | Already scoped by entity assignment |

**New methods to add:**
- `IsMinistryDirectorAsync(userId, orgId)` — single-org check
- `IsSlotCoordinatorAsync(userId, orgId)` — single-org check
- `IsAnyMinistryDirectorAsync(userId)` — any-org check (for nav visibility)
- `IsAnySlotCoordinatorAsync(userId)` — any-org check (for nav visibility)
- `IsAnyTrainingManagerAsync(userId)` — Admin \|\| MinistryDirector (for training-catalog nav links)

**Service authorization updates.**

| File | Line/Method | Before | After |
|------|------------|--------|-------|
| `TrainingSessionService.cs` | `IsCallerOrgManagerAsync` (line 749) | Admin \|\| Coordinator | Admin \|\| MinistryDirector |
| `TrainingService.cs` | `MarkSingleCompleteAsync` (line 495) | Admin \|\| Coordinator | Admin \|\| MinistryDirector |
| `SlotDocumentService.cs` | Lines 151, 271 | Admin \|\| Coordinator | Admin \|\| MinistryDirector |

**Dashboard changes (`Components/Pages/Dashboard.razor`).**

Single `/Dashboard` page with three role-based query paths:

1. **Admin view (existing):** All assignments for selected org.
2. **Ministry Director view:** Only assignments where
   `Ministry.CoordinatorPersonUserId == userId` (their assigned
   ministries). Org picker shows only orgs where the user holds
   `MinistryDirector` role.
3. **Slot Coordinator view:** Only assignments where
   `ServiceSlot.CoordinatorPersonUserId == userId` (their assigned
   slots). Org picker shows only orgs where the user holds
   `SlotCoordinator` role.

KPI cards (Assignments, Unique volunteers, Person overlaps,
Outstanding trainings) are scoped to the role-filtered assignment set.
Title/description changes per role:
- Admin: "Organization dashboard"
- MinistryDirector: "Ministry director dashboard"
- SlotCoordinator: "Slot coordinator dashboard"

SystemAdmin gets the all-orgs view (existing behavior).

**NavMenu changes (`Components/Layout/NavMenu.razor`).**

Replace single `_isManager` flag with three role-aware flags:

| Flag | Who | Shows |
|------|-----|-------|
| `_isAdmin` | Admin in any org | Organizations, Volunteers, Leagues |
| `_isTrainingManager` | Admin \|\| MinistryDirector | Dashboard, In-person training |
| `_isSlotCoordinator` | SlotCoordinator in any org | Dashboard only |
| `_isSystemAdmin` | SystemAdmin identity role | System admin link (no change) |

Expected nav structure:
```
Home                        → all authenticated
Organizations               → _isAdmin only
Volunteers                  → _isAdmin only
Leagues                     → _isAdmin only
Dashboard                   → _isTrainingManager || _isSlotCoordinator
In-person training          → _isTrainingManager only
Training                    → all authenticated
My schedule                 → all authenticated
Browse open slots           → all authenticated
Account                     → all authenticated
System admin                → _isSystemAdmin only
```

**Other page authorization updates.**

| File | Current Check | Change |
|------|--------------|--------|
| `Organizations/Index.razor` | Admin \|\| Coordinator | Admin only |
| `People/Index.razor` | Admin \|\| Coordinator | Admin only |
| `Leagues/Teams/New.razor` | Admin \|\| Coordinator \|\| ministry Coord | Admin \|\| MinistryDirector \|\| ministry Coord |
| `Organizations/Coordinators.razor` | Admin \|\| Coordinator | Admin \|\| MinistryDirector |
| Various detail pages | CanManageMinistry / CanManageSlot | No change (delegates to updated OrgAuthService) |

**DatabaseSeeder changes (`Data/DatabaseSeeder.cs`).**
Lines 131, 135, 136: Change `OrganizationRole.Coordinator` →
`OrganizationRole.MinistryDirector`. These seed demo users (a coordinator
and coaches) who should become MinistryDirector since they manage
ministries.

**Test file changes.**
All test files (~16 files, ~40+ occurrences) need find-replace:
`OrganizationRole.Coordinator` → `OrganizationRole.MinistryDirector`.
Test method names like `UpdateRole_PromotesToCoordinator` should be
renamed to `…PromotesToMinistryDirector` for clarity.

New OrgAuthService tests needed for:
- `IsMinistryDirectorAsync` (positive + negative)
- `IsSlotCoordinatorAsync` (positive + negative)
- `IsAnyMinistryDirectorAsync` / `IsAnySlotCoordinatorAsync`
- `IsAnyTrainingManagerAsync`
- `CanManageOrgAsync` now denies MinistryDirector

**Migration.**
`dotnet ef migrations add AddSlotCoordinatorRole` — purely additive
(new enum value `SlotCoordinator = 3` in the model snapshot). No data
migration needed (the `Coordinator → MinistryDirector` rename keeps value 1).

**Documentation updates.**
- `PLAN.md` core data model item #1: update role description to list
  Volunteer / MinistryDirector / Admin / SlotCoordinator.
- `README.md`: update any references to "Coordinator" role.

**Decisions (resolved during spec planning session).**
1. **Rename vs. new value for Coordinator:** **Rename to MinistryDirector,
   keep value=1.** Existing database rows get the new label automatically
   (EF Core stores enums as ints). No data migration needed.
2. **Dashboard structure:** **Single `/Dashboard` page with role-based
   views.** The user selected this over separate pages.
3. **Nav visibility:** **Restricted.** Ministry Directors see dashboard +
   training catalog. Slot Coordinators see dashboard only. Main nav links
   (Orgs, Volunteers, Leagues) are Admin-only.
4. **Ministry Director authority:** **Transitive.** A Ministry Director
   of a parent ministry automatically manages sub-ministries and their
   slots. This matches the existing `CanManageMinistryAsync` behavior
   which already handles parent-ministry transitivity.
5. **Ministry Director scope:** **Assigned ministries only.** A Ministry
   Director sees/manages only ministries where they are the
   `CoordinatorPersonUserId` — NOT all ministries in the org. The
   `OrganizationRole.MinistryDirector` membership label controls UI
   visibility (nav links, dashboard type); the `CoordinatorPersonUserId`
   on specific `Ministry` rows controls actual data scoping.
6. **Slot Coordinator scope:** **Assigned slots only.** Same pattern as
   Ministry Director — the membership label enables dashboard access;
   the `CoordinatorPersonUserId` on specific `ServiceSlot` rows controls
   which slots they see.

**Files this round would touch (when implementation starts).**
- `Models/Enums.cs`: rename Coordinator → MinistryDirector, add SlotCoordinator.
- `Services/OrgAuthService.cs`: interface + implementation — 5 new methods,
  tightened `CanManageOrgAsync`, updated `IsAnyOrgManagerAsync`.
- `Services/TrainingSessionService.cs`: `IsCallerOrgManagerAsync` gate.
- `Services/TrainingService.cs`: `MarkSingleCompleteAsync` gate.
- `Services/SlotDocumentService.cs`: two `isOrgAdminOrCoordinator` checks.
- `Components/Layout/NavMenu.razor`: three role-aware flags + restructured
  nav link visibility.
- `Components/Pages/Dashboard.razor`: role-based query paths + KPI scoping +
  title/per-role copy.
- `Components/Pages/Organizations/Index.razor`: Admin-only gate.
- `Components/Pages/People/Index.razor`: Admin-only gate.
- `Components/Pages/Leagues/Teams/New.razor`: permission message update.
- `Components/Pages/Organizations/Coordinators.razor`: permission message
  update.
- `Data/DatabaseSeeder.cs`: three membership rows.
- `tests/ServantSync.Tests/*.cs` (~16 files): find-replace
  `OrganizationRole.Coordinator` → `OrganizationRole.MinistryDirector`.
- `tests/ServantSync.Tests/OrgAuthServiceTests.cs`: new test cases for
  MinistryDirector + SlotCoordinator auth methods.
- NEW migration: `AddSlotCoordinatorRole`.
- `PLAN.md` core data model item #1: update role list.
- `README.md`: update Coordinator references.

### Round-FR-6: per-org "training due soon" grid — overdue + upcoming within 30 days ✅ SHIPPED → 6721e66 (2026-07-08)

**User ask (verbatim intent).** "Show by organization anyone who currently needs training or will need it within 30 days in a grid." Coordinators want a single org-scoped grid listing every volunteer in their organization who is **at risk on training** — either already overdue (their last `TrainingCompletion.ExpiresUtc` is in the past, or they have no completion record at all for a `TrainingRequirement` they should have) or upcoming within the next 30 days (their `ExpiresUtc` is `today ≤ ExpiresUtc ≤ today + 30 days`). Replaces the current "ask around + drill into each person" workflow with a glance-size table that prioritizes who's at risk this month, so the coordinator can schedule the next in-person session (Round-FR-2) or follow up with individual volunteers proactively.

**Why now.** Training is yearly + every-N-months + one-time (`TrainingCadence` enum), and `TrainingCompletion.ExpiresUtc` is already precomputed at recording time per the existing schema — but neither `/Training/Take` nor `/Dashboard` surfaces an "at-risk" view. Coordinators have to walk the membership roster one person at a time to find lapsed volunteers. A dedicated "training due soon" view is the input to a meaningful Round-FR-2 session scheduling decision: "who needs what, by when?" becomes the agenda for the next training event. Tightly coupled to Round-FR-2 — the FR-2 marker form writes the completion that flips a row from Overdue → Compliant on this grid; conversely, this grid tells a coordinator WHICH volunteers should get manual marks if they attended out-of-band.

**RBAC.** Mirrors Round-FR-5's restricted visibility rules:
- **Organization Admin:** full visibility into every member's training status.
- **Ministry Director:** visibility + actionable (link to `/Training/Take/{contentId}` or the org-modal "Mark complete") for their assigned ministries' members' training status.
- **Slot Coordinator:** NO ACCESS this round — slot scope is too narrow to meaningfully surface "what's due across the slot's volunteers"; Admin + Ministry Director cover the meaningful triage use cases.
- **Volunteer:** NO ACCESS — sees only their own status on `/MySchedule` (out of scope for this round to expand `/MySchedule`).
- **SystemAdmin:** NO ACCESS this round — out of scope for round 1, deferred per decision Q8.

**Model additions.**
NONE. All required data already exists:
- `TrainingRequirement` — per-org (`OrganizationId` set) OR per-slot (`ServiceSlotId` set); `Cadence` ∈ {`OneTime`, `Yearly`, `EveryMonths`} where `EveryMonths` carries `CadenceMonths` (default 12).
- `TrainingCompletion` — `ExpiresUtc` is precomputed at recording time per the existing model doc-comment (`Computed at recording time based on the requirement's cadence`), so the "due-soon" calc is a simple read.
- `TrainingCompletionSource` — already distinguishes `UserOnline` / `CoordinatorManual` / `CoordinatorManualSingle` (Round-FR-2). The grid shows this as a badge so a coordinator knows which completion path flipped a row Compliant.
- `TrainingCompletion.IsValid(asOfUtc)` — already-provided helper (`return ExpiresUtc is null || ExpiresUtc > asOfUtc;`). Drives the Overdue vs Compliant math.
- `Person.IsStub` (Round-FR-3) — grid includes stub People so a coordinator sees pre-account-link at-risk stubs alongside real volunteers.

No migrations this round.

**Library / service decisions.** Pure C# / EF + existing `Components/Shared/LocalTime.razor` (for the "Last completed on" stamp) + `DateRangeChips.razor`-style filter-chip pattern (reused for the grid's filter chips). The grid is the same Bootstrap responsive table shape (`table table-hover table-sm`) used by `/Organizations/{Id}/Coordinators` and `/Organizations/{Id}/Members/Stubs` — no new NuGet dep. The math lives in a new `Services/TrainingDueSoonService.cs` (matches the `ITrainingSessionService` / `ITrainingService` per-page-service pattern; keeps Razor markup thin and testable).

**Route surface.**
- NEW: `/Organizations/{OrgId:int}/Training/DueSoon` — single-org-scoped page. Route mirrors the existing `/Organizations/{OrgId:int}/Training/Sessions` shape so a coordinator navigating the Org-training tab lands naturally on both. Org is pre-bound by the URL segment (no global org picker on this page — coordinators use `/Organizations/{Id}` to switch orgs).
- DEFERRED (decision Q8): `/SystemAdmin/TrainingDueSoon` global overview listing all orgs with their at-risk counts, same shape as the Round-FR-4 `SystemAdmin/Index` "Feature requests (N new)" link. Round 2.

**Page contents — the grid.**
A single Bootstrap responsive table sorted by default to "by urgency" (most-overdue first, then upcoming-soonest-first, then alphabetical fallback). Columns (left → right):

| Column | Where it comes from | Renders as |
|---|---|---|
| Person | `Person.DisplayName` → `/People/{Id}` deep link | bold text link, stub Person shows `⚠ stub` badge suffix |
| Email | `Person.Email ?? IdentityUser.Email` (Person preferred, see decision Q7) | muted small text link |
| Ministry / Slot scope | `TrainingRequirement.Scope` ("Organization" or "ServiceSlot · {slot.Name}") | small text label + slot deep link when scoped |
| Requirement (content) | `TrainingContent.Title` → `/Training/Take/{Id}` deep link | bold text link |
| Status | computed (see math below) | Bootstrap badge: `Overdue` `bg-danger`, `Due in N days` `bg-warning text-dark`, `No record` `bg-secondary` |
| Days | `Math.Abs((ExpiresUtc ?? baseline) - today).Days` (negative `–` prefix for overdue) | small text, monospace digit width |
| Last completed | `CompletionUtc` (`LocalTime`-rendered) | LocalTime stamp `Format="MMM d, yyyy"` |
| Mark source | `CompletionSource` | small badge `Online` / `CoordinatorManual` / `CoordinatorManualSingle` |
| Actions (Admin / MinistryDirector only) | n/a | link "Mark complete" → calls `ITrainingService.MarkSingleCompleteAsync(contentId, personUserId, callerUserId, notes` with a Bootstrap modal for the REQUIRED notes (Round-FR-2 decision Q5 dissallowed empty marker notes) |

**Filter chips (above the grid).**
Reuses the project's `DateRangeChips.razor`-style shape (Bootstrap chip group + `aria-pressed` for screen-reader parity):

| Chip | What it shows |
|---|---|
| **All at-risk** (default) | Overdue (any past-overdue days) + Due-in-30-days |
| **Overdue only** | Just Overdue (past-`ExpiresUtc` or no completion) |
| **Due in 30 days** | Just Due-in-30-days rows, no overdue |
| **Completed in last 30 days** | Inverse validation — does recent completion traffic exist? (rows where the most recent completion was within the last 30 days; these are excluded from "at-risk" by default but exposed for an admin audit) |

**Sort toggle (Bootstrap btn-group above the grid).** Three options: `By urgency` (default) | `Alphabetical by person` | `Alphabetical by content`.

**Math (the heart of the feature).**
Drives off the existing `ExpiresUtc` field, no new cadence extension needed:

```
due_row = requirements_in_org
       | where person has OrganizationMembership in {OrgId} person's ministries
       | left_join most_recent_completion_per_(person, content)
       | project [
           person,
           requirement,
           most_recent_completion,    // null if never completed
           expires_utc = completion?.ExpiresUtc ?? compute_for_oneime(never_completed) ?? null,
         ] into row
status_for(row, now):
  if row.most_recent_completion is null:
    if row.requirement.Cadence == OneTime:
      return NotRequired   // OneTime requirement, nobody has to take it (only tracked-on-completion)
    return Overdue         // Yearly / EveryMonths-with-no-completion = "should have by now"
  if row.expires_utc is null:
    return CompliantSkip   // OneTime completed (forever valid)
  if row.expires_utc < now:
    return Overdue(days = (now - expires_utc).Days)
  if row.expires_utc <= now + 30days:
    return DueSoon(days = (expires_utc - now).Days)
  else:
    return Compliant   // grid filters this out by default
filter_by_chip(status, chip) is straightforward.
```

Edge cases specifically tested in the service-layer tests:
- Person has 3 different overdue requirements → 3 rows at the same person+content row index. Round 2 could collapse to per-person N-overdue + M-upcoming summary; out of scope.
- Person has multiple completions for the SAME requirement (content-version bumps over time) → most-recent wins per `OrderByDescending(CompletionUtc).FirstOrDefault()`.
- Person has NO completions for a OneTime requirement → "NotRequired" status (filtered out of "All at-risk" by default; surfaced under an "Untracked OneTimes" chip if added in round 2).
- Person is a stub (`Person.IsStub == true`) → row IS included so a coordinator sees stubs alongside real volunteers (round-FR-3 stubs are linkable + assigned to ministries). Email column falls back to `IdentityUser.Email` (placeholders are lockout-future so this is empty; grid shows "—" gracefully).

**Service additions: `ITrainingDueSoonService`.**
- `ListAtRiskAsync(organizationId, filter, sort, callerUserId, nowUtc)` — returns paged (round 1: no paging, all results) `TrainingDueSoonRow` records. Eager-loads `Person` (with `.ThenInclude(p => p.User)` for the IdentityUser email lookup) + `TrainingContent` + `TrainingRequirement.ServiceSlot` (for the ministry/slot column). Caller gate: Admin OR MinistryDirector in the calling org (`OrgAuthService.IsOrgAdminAsync` + `IsMinistryDirectorAsync`); throws `PermissionDeniedResult` otherwise (defense-in-depth UI mirror of this gate is in the page itself).
- `ListAtRiskCountsAsync(organizationId, callerUserId)` — single tuple `(OverdueCount, DueSoonCount, TotalAtRiskCount)` for the page header badge. Same gate.
- `TrainingDueSoonRow` (record): `{ int PersonId; string PersonDisplayName; bool IsStub; string? EmailAtMoment; int RequirementId; string RequirementTitle; string RequirementScope; int? SlotId; string? SlotName; DateTime? LastCompletionUtc; DateTime? ExpiresUtc; TrainingCompletionSource? CompletionSource; TrainingDueSoonStatus Status; int? DaysDelta; }`.
- `TrainingDueSoonStatus` enum: `{ NotRequired, Overdue, DueSoon, Compliant, NoRecord }` (mirrors decisions Q5 + Q6 + the OneTime-never-tracked carve-out).

**UI changes.**
- NEW: `Components/Pages/Organizations/Training/DueSoon.razor`.
- MODIFIED: `Components/Pages/Organizations/Detail.razor` — Org-training tab gains a third link: *"'Training due soon' (N at-risk) →"* where N is `TrainingDueSoonService.ListAtRiskCountsAsync` returned (gated on `_isOrgManager` so non-managers don't trigger a wasted query). The "N at-risk" count is a small inline badge so the dashboard shows the org's liar-at-red count at-a-glance.
- (Optional, out of scope for round 1) `Components/Layout/NavMenu.razor` gains a "Training due" link visible to Admin+MinistryDirector. Round 1 keeps it inside the Org-training tab so the navigation discovery path matches the existing "In-person training sessions" link from Round-FR-2.3.

**Tests.**
- NEW: `tests/ServantSync.Tests/TrainingDueSoonServiceTests.cs` (~25 tests) covering: gate (Admin + MinistryDirector in-org; others denied), overdue math (3 cadences × 3 completion states), 30-day-upcoming window boundary (29/30/31-day tested), OneTime-never-tracked carve-out, stub inclusion, multi-content single-person multi-row, sort orders (urgency / alphabetical-by-person / alphabetical-by-content), filter chips (All / Overdue only / Due-in-30 / Completed-recently).
- MODIFIED: `tests/ServantSync.Tests/PageAccessTests.cs` (+2 entries for the new admin-gated + ministry-gated routes; matches the round-FR-3.3 `NewPersonService()` helper discipline for real-OrgAuth gates).
- NO bUnit coverage added this round (consistent with the codebase's per-site discipline; the service-layer cohorts above cover the security-critical paths).

**Edge cases.**
- *Org has no training requirements configured:* page renders a Bootstrap `alert-info` panel with a deep-link to `/Organizations/{Id}/Training/Manage` ("This organization has no training requirements yet."). No grid.
- *Org has requirements but ZERO at-risk members:* page renders an empty grid + an `alert-success` panel ("Everyone is up-to-date on training — great work!"). Future round 2 could add a "recently completed" feed here as a +1 signal.
- *Stub assigned to a slot under a requirement (FR-3 + this flow):* stub IS in the grid. Coordinator can either (a) hand off the claim token + offer the stub training opportunity, (b) wait for the volunteer to register themselves and complete online, or (c) mark complete manually via the "Mark complete" modal (FR-2 path).
- *Timezone:* date math is UTC-only (`ExpiresUtc` and `CompletionUtc` are stored as UTC; clock skew between ACA instances is irrelevant). The "Last completed on" stamp uses `LocalTime` for human display (round-AV); the urgency math is unaffected by display TZ.
- *Multi-org person:* a coordinator of Org A sees only Org A's memberships in the grid. Multi-org volunteers are surfaced under each org independently — no cross-org rollup in round 1.
- *Grid truncation:* round 1 returns ALL rows (no paging). A 50-requirement × 100-person org could produce ~5000 rows; round 2 introduces paging if this becomes a perf concern. Round-1 scale assumption: most churches have <50 requirements and <500 members → max row count ~25,000 for "All at-risk" (rarely all at-risk) → still renders fine on a single page.
- *Person membership changed (e.g., a volunteer just added to the org):* the grid re-queries on Reload; the new person's existing completion is detected via the at-risk view the moment they become a member.

**Decisions (resolved during spec authoring session).**
1. **Slot Coordinator access:** **NO** this round. Slot scope is too narrow; Ministry Director + Admin cover the meaningful triage use cases (per RBAC + per the spec's "show me 'what's due' across ALL of my slots at once" use case).
2. **Overdue + Upcoming are BOTH shown.** "All at-risk" chip is the default; filter chips let a coordinator isolate Overdue-only or Due-in-30-only. Surfacing both keeps the page useful for "what's lurking this month" planning, not just "who's already late" firefighting.
3. **Person membership scope:** org-scoped (all `OrganizationMembership` rows in `{OrgId}` with any role).
4. **Stub inclusion:** YES. Stubs are real volunteers-from-another-time; they need training. Round-3 already wired stubs into the slot-assignment + duty-assignment pipelines, so FR-6's training-at-risk view must include them or coordinators will miss them.
5. **OneTime never-tracked carve-out:** `NotRequired` status, filtered out by default. Rationale: a OneTime requirement with no completions may mean "nobody's ever needed this", not "everyone is overdue." The carve-out prevents the grid from screaming about a perfectly-fine real-world scenario.
6. **Sort order:** `By urgency` default. Overdue rows first (sorted by days-overdue DESC), then Due-in-30 rows (by days-until ASC), then alphabetical fallback if tied.
7. **Email column source:** `Person.Email ?? IdentityUser.Email`. Person is preferred per round-FR-3's stub email column; IdentityUser falls back for users without a stub legacy.
8. **Global `/SystemAdmin/TrainingDueSoon`:** OUT OF SCOPE for round 1; round 2 if SystemAdmin wants the same view across orgs.
9. **Mark-complete modal in this grid (vs navigate to `/Training/Take`):** IN THIS GRID. Coordinator has higher completion throughput if the mark-complete dialog opens in-place on the grid instead of routing away. The modal reuses the same `notes` REQUIRED pattern as Round-FR-2.3's per-row single mark card; no new view-model needed.
10. **Performance:** direct EF LINQ (no pre-computed table, no nightly job). Round-2 caching is a Tier-2 followup if a large org measurably slows the page.

**Files this round would touch (when implementation starts).**
- NEW service: `Services/ITrainingDueSoonService.cs` + `Services/TrainingDueSoonService.cs` (5-method service + 1 row record + 1 status enum).
- NEW page: `Components/Pages/Organizations/Training/DueSoon.razor`.
- NEW modals: Bootstrap modal markup inlined in `DueSoon.razor` for the "Mark complete" / "Notes required" / "Stub handoff" flows (matches Round-FR-2.3's in-page modal pattern).
- MODIFIED: `Components/Pages/Organizations/Detail.razor` — gain the "Training due soon (N at-risk) →" link in the Org-training tab.
- NEW tests: `tests/ServantSync.Tests/TrainingDueSoonServiceTests.cs` (~25 cases covering gate + math + sort + filter per decision Q1-Q10).
- MODIFIED: `tests/ServantSync.Tests/PageAccessTests.cs` (+2 entries for gate).
- NO migrations (all schema already exists).
- (Optional Tier-2 followup) `Services/TrainingDueSoonCache.cs` if the LINQ is too slow on a 1000-row grid.

### Round-FR-7: per-slot volunteer interest — let volunteers "subscribe" to slots separately from joining a ministry, and filter `/Open` accordingly ✅ SHIPPED

**User ask (verbatim intent).** "Currently users can 'join' a ministry. I'd like for them to also be able to 'volunteer' for a slot. Then on open slots we show only slots which are volunteered for and not all slots within a ministry. Someone might want to see referee slots for a game but have no interest in coaching."

**Why now.** Today the `/Open` filter (per round-FR-patterns) sits at the **ministry** granularity: a volunteer who joined an AWANA ministry sees every AWANA slot's open shifts, regardless of whether they actually want to coach, lead songs, do refreshments, or work the welcome desk. Concretely, the Springfield Youth Soccer League ministry with `Coach`, `AssistantCoach`, `GameDayReferee`, `Concessions`, and `DevotionLeader` slots, a parent who only wants to referee games still sees all five slot families' open shifts, and gets exit-button fatigue on /Open. Splitting the existing two-tier interest model — Org membership + Ministry interest — into a clean three-tier model — Org membership + Ministry interest + **Slot interest** (NEW) — gives the volunteer a single subscription knob per slot, and gives the `/Open` page a single new filter that says "I only want to see shifts for slots I've signed up for".

**Modeling verdict (per the thinker's design pass).**

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| (A) NEW `SlotInterest` junction table | Clean mental model; /Open filter is straight JOIN; parallels `MinistryInterest` shape verbatim; Source-audit friendly | Yet another junction table | **CHOSEN** |
| (B) Reuse `MinistryInterest` + SlotIndex side-table | Less schema | Ambiguous semantics ("subscribed to a ministry" doesn't answer "which slots in it"); /Open filter still needs a separate JOIN | REJECTED |
| (C) Reuse `AssignmentStatus.Tentative` | Zero new schema | Assignments are time-windowed (Slots with `StartUtc`/`EndUtc`); interest is ongoing; Assignment FK shape doesn't fit "always want to referee" | REJECTED |

**RBAC matrix.**

| Action | Caller gate |
|---|---|
| Self subscribe to slot | `OrgMember(slot.Ministry.OrganizationId)` (Volunteer / MinistryDirector / Admin / SlotCoordinator in that org) — matches `MinistryInterestService.JoinAsync`'s exact same gate |
| Self unsubscribe | Same |
| Subscribe another person (coordinator delegates on volunteer's behalf) | `IOrgAuthService.CanManageSlotAsync(slotId)` — Admin of `slot.Ministry.OrganizationId`, MinistryDirector of the parent ministry, or `slot.CoordinatorPersonUserId == callerId` (Round-FR-5 coordinator chain) |
| Unsubscribe another person | Same — TIGHTER than `MinistryInterest` (where any in-org member could remove) — slot interest is more sensitive than ministry interest because it drives the `/Open` default filter, so coordinators have more exclusive authority |
| View `ListForSlotAsync(slotId)` (slot-coord interest roster) | `CanManageSlotAsync(slotId)` |

**Model additions.**

1. **`SlotInterest` (new model).**
   - `Id` (int, PK)
   - `PersonUserId` (string, FK Person, cascade-delete with Person)
   - `ServiceSlotId` (int, FK ServiceSlot, cascade-delete with slot)
   - `SubscribedUtc` (DateTime, default UtcNow)
   - `Source` (`SlotInterestSource` enum, default `Explicit`)
   - Composite-unique index on `(PersonUserId, ServiceSlotId)`.

2. **Extend `Models/Enums.cs` with a new enum.**
   ```csharp
   public enum SlotInterestSource
   {
       Explicit = 0,            // volunteer clicked the Subscribe button
       AutoFromAssignment = 1,  // auto-created by /Open's Sign-Up follow-up
   }
   ```

3. **No change** to `OrganizationMembership`, `Assignment`, `TrainingCompletion`, `TrainingActivity`, `MinistryInterest`. The `PersonUserId` FK is a string and works whether the Person has a real IdentityUser or is a stub (Round-FR-3 parity). The existing `AssignmentService.ValidateAsync` training-gate works for stubs unchanged.

**Library / service decisions.** Pure C# / EF. No new NuGet dependencies. The service shape mirrors `IMinistryInterestService` verbatim so a round-2 contributor can cross-walk the two with no mental translation.

**Route surface.** All changes live on existing pages — no new routes this round.

**Service additions: `ISlotInterestService`.**

| Method | RBAC | Result enum |
|---|---|---|
| `SubscribeAsync(callerUserId, personUserId, slotId, ct)` | caller is `OrgMember(slot.Ministry.OrganizationId)` AND (caller == `personUserId` OR caller passes `CanManageSlotAsync(slotId)`) | `SlotInterestJoinResult { Subscribed, AlreadySubscribed, PermissionDenied, SlotNotFound }` |
| `UnsubscribeAsync(callerUserId, personUserId, slotId, ct)` | Same | `SlotInterestLeaveResult { Unsubscribed, NotSubscribed, PermissionDenied, SlotNotFound }` |
| `ListSubscribedAsync(personUserId, ct)` | Self (read-only) for the volunteer; populates `_subscribedSlots` cache + /Open filter | eager-loaded `ServiceSlot.Ministry` for the home-page panel + /Open filter |
| `ListForSlotAsync(slotId, callerUserId, ct)` | caller has `CanManageSlotAsync(slotId)` | eager-loaded `Person` for the slot-coord interest roster |

**Auto-subscribe on Sign-Up on `/Open` (per Q1 decision).** When a volunteer uses `/Open`'s "Sign up" action, `Open.razor.SignUp()` handler calls `AssignmentService.SignUpAsync` first, then on success independently fires `ISlotInterestService.SubscribeAsync(callerId, personUserId, slotId)` with `Source=AutoFromAssignment`. The Subscribe result is reported to the volunteer (green "Signed up" + small footer chip "Subscribed for future shifts") and any failure is rolled forward (see Q-C). **Coordinator-driven assignments via `AssignmentService.AssignAsync` (called from `ServiceSlots/Schedule.razor` etc.) DO NOT auto-subscribe** — only self-sign-ups through /Open do, so coordinators can't pollute volunteer preferences.

**Auto-subscribe on TrainingSession Sign-Up (per Q-D decision).** **NO.** Training sessions don't have a `ServiceSlotId` FK (they're content-scoped via `TrainingContentId`), so there's no physical slot to subscribe to when a volunteer signs up for a training session. Coordinator-driven training-session sign-ups don't auto-subscribe either.

**UI changes.**

- **MODIFY**: `Components/Pages/ServiceSlots/Detail.razor` — add a prominent **"Subscribe to this slot"** / **"Subscribed (since {date})"** toggle in the top-right header area next to the existing manager edit buttons. Mirror the **"Interested"** / **"Join ministry"** badge placement pattern from `Ministries/Detail.razor`. Add a **"Subscribers (N)"** panel below the existing slot info card for managers (gate `CanManageSlotAsync(slotId)`) rendering `ListForSlotAsync(slotId)` — just `Person.DisplayName` + `SubscribedUtc` (LocalTime-stamped). NO source badge (per Q-B2 decision).
- **MODIFY**: `Components/Pages/Ministries/Detail.razor` — add a compact **Subscribe ✓** / **Subscribed ✓** pill on each slot row next to the existing **Schedule** button, for quick multi-slot opt-in without navigating into the slot. Sourced from `ISlotInterestService.ListSubscribedAsync(personUserId)` once per page load and looked-up per row.
- **MODIFY**: `Components/Pages/Open.razor`:
  - Upgrade the existing 2-way filter (currently `All ministries` / `My ministries` toggle) to a **3-way pill segment** (per Q3 decision, replacing the current toggle):
    - **All slots** — no interest filter (current default behavior for users with 0 subscriptions).
    - **My ministries (M)** — current behavior, broad ministry-level filter.
    - **My slots (N)** — narrow slot-level filter, NEW.
  - **Auto-default** in `OnInitializedAsync`: seed `_filterMode = SlotInterest.rows.Any() ? MySlots : (MinistryInterest.rows.Any() ? MyMinistries : AllSlots)`. Persisted in `_filterMode` for the session; not URL-bound this round.
  - Fire `SubscribeAsync(..., Source=AutoFromAssignment)` immediately after a successful `SignUpAsync`. The subscribe result is rendered as a small footer chip ("Subscribed for future shifts") on success and silently logged on failure (Q-C).
- **MODIFY**: `Components/Pages/Home.razor` — add a new panel **"Slots I'm subscribed to (N)"** below the existing **"Ministries I'm interested in (N)"** panel. Same skeleton (card list with per-item Leave button). Volunteers can clear their whole slot-subscription list from this panel in one place.

**Failure tolerance (per Q-C decision).** If `SignUpAsync` succeeds but the follow-up `SubscribeAsync` returns `PermissionDenied` or `SlotNotFound` (rare race: slot was deleted between assignment commit and subscribe call, or a coordinator-side revoke just happened), the volunteer still sees the standard green "Signed up" success — the subscribe side-effect is silently rolled forward and the failure is logged via `ILogger<Open>` for diagnosability. The volunteer knows they got the shift; a soft-preference failure should not surface as a red error that confuses them into thinking their assignment failed.

**Audit / reporting.** `ListForSlotAsync(slotId)` is the slot coordinator's per-slot interest roster (names + since-date). The `Source` column is captured but NOT surfaced as a UI badge (Q-B2 decision) — coordinators primarily care *that* someone is reachable, not their origin journey. Round 2 could expose Source via a dedicated "My Preferences" self-audit page for the volunteer.

**Edge cases + behavior.**
- *Slot `IsActive=false`:* SlotInterest row persists; volunteer remains subscribed; `/Open` naturally hides the slot's shifts via the existing active-slot filter.
- *Slot soft-deleted (still in DB, hidden via UI):* SlotInterest row persists; re-activation restores the volunteer's preference without data loss.
- *Slot hard-deleted:* cascade-deletes SlotInterest.ServiceSlotId FK (slot deletion already cascades to TrainingRequirements).
- *Person deleted:* cascade-deletes SlotInterest.PersonUserId FK.
- *Cross-org subscribe attempt:* caller not in `slot.Ministry.OrganizationId` → `PermissionDenied`.
- *Foreign-org coordinator trying to subscribe someone in their target slot:* `CanManageSlotAsync` denial → `PermissionDenied`.
- *Subscriber is a stub Person (Round-FR-3):* row is created on stub's Person.UserId (string FK); on stub claim via `PersonClaimToken`, the `SlotInterest.PersonUserId` re-parents with `Person.UserId` automatically (same re-parent shape as `OrganizationMembership`/`Assignment`/`TrainingCompletion` — zero migration impact). Coordinator's `ListForSlotAsync` includes stub entries with `Person.IsStub=true` so the admin sees who's still pending registration.
- *Sign-Up race: slot becomes full between page load and click:* `SignUpAsync` returns `SlotFull` BEFORE `SubscribeAsync` fires; no SlotInterest row created. Clean.
- *User clicks Sign-Up twice quickly:* `SignUpAsync` creates one Assignment (existing idempotency); `SubscribeAsync`'s second call returns `AlreadySubscribed`, swallow naturally. Clean.
- *Volunteer un-subscribes after signing up for a single shift:* the Assignment row stays; only the future `/Open` "My slots" filter disappears. Single backward-compatible round-trip.
- *Sibling slots sharing a ministry:* subscribing to one doesn't subscribe to siblings — explicit per the new junction-table model. A "subscribe to all slot under this ministry" affordance is a round-2 follow-up.

**Decisions (resolved during spec authoring session).**

1. **Schema shape:** NEW `SlotInterest` table. Mirrors `MinistryInterest` shape verbatim (`PersonUserId`, `ServiceSlotId`, `SubscribedUtc`, `Source`). Composite-unique on `(PersonUserId, ServiceSlotId)`. Cascade FK on both Person + Slot.
2. **UI placement:** Primary toggle on `ServiceSlots/Detail.razor` (one slot = one button); per-slot-row pill on `Ministries/Detail.razor` (multi-slot opt-in); per-shift-card footer pill on `Open.razor` (last-chance opt-in); new Home panel showing all subscribed slots.
3. **`/Open` filter UX:** **3-way pill** (All slots / My ministries / My slots) replacing the current 2-way toggle. Auto-default based on user's existing subscribe/interest counts (slot-subscribe if any, else ministry-interest if any, else all-slots).
4. **Auto-subscribe on Sign-Up:** **YES.** `Open.razor.SignUp()` calls both `AssignmentService.SignUpAsync` AND `ISlotInterestService.SubscribeAsync(Source=AutoFromAssignment)` sequentially. **`AssignmentService` itself stays clean** (no new DI dependency); the cross-service fan-out lives in the page handler. Coordinator-driven `AssignAsync` calls (manager side) DO NOT trigger auto-subscribe.
5. **Audit field `SlotInterestSource`:** **FOR.** Enum in `Models/Enums.cs` (`Explicit=0`, `AutoFromAssignment=1`). NOT surfaced as a UI badge round 1 (might create clutter on coordinator rosters); captured for round-2 self-audit / data-quality debugging.
6. **Failure tolerance:** **Roll forward silently.** Sign-Up success followed by Subscribe failure → volunteer sees green "Signed up" success message. Subscribe failure logged via `ILogger<Open>` for diagnosability. No red error UX for a soft-preference side-effect.
7. **Cross-person subscribe gate:** **Tighten vs MinistryInterest.** Only `CanManageSlotAsync(slotId)` callers (Admin / MinistryDirector of `slot.Ministry` / `slot.CoordinatorPersonUserId == callerId`) can subscribe / unsubscribe on behalf of someone else. Stricter than `MinistryInterest` — slot interest is more sensitive than ministry interest because it drives the `/Open` filter default.
8. **Stub seam:** **No special logic needed.** String FK to Person handles every stub case identically to real users. `ListForSlotAsync` rows render stub Persons with `Person.IsStub=true` so the coordinator sees who's still pending registration.
9. **Email notification on subscribe:** **NO.** Silent subscription; coordinator sees the new row on `ServiceSlots/Detail.razor` next page load. Confirmed via user's Q2 decision.
10. **Auto-subscribe from `TrainingSessionService.SignUpAsync`:** **NO.** Training sessions have no `ServiceSlotId` FK (they're content-scoped via `TrainingContentId`); no physical slot to subscribe to when a volunteer signs up for a training session. Coordinator-driven training-session sign-ups don't auto-subscribe either.

**Files this round would touch (when implementation starts).**

- NEW: `Models/SlotInterest.cs`.
- MODIFY: `Models/Enums.cs` (add `SlotInterestSource` enum).
- MODIFY: `Data/ApplicationDbContext.cs` (add `DbSet<SlotInterest> SlotInterests` + composite-unique index + cascade-FK configuration).
- NEW: `Services/ISlotInterestService.cs`.
- NEW: `Services/SlotInterestService.cs` (constructor: `IDbContextFactory<ApplicationDbContext>, ILogger<SlotInterestService>`; mirrors `MinistryInterestService`'s idempotency + permission-denied error swallowing verbatim).
- MODIFY: `Program.cs` (DI registration: `builder.Services.AddScoped<ISlotInterestService, SlotInterestService>();`).
- MODIFY: `Components/Pages/ServiceSlots/Detail.razor` (subscribe toggle in header + subscribed-since date text + Subscribers(N) panel for managers).
- MODIFY: `Components/Pages/Ministries/Detail.razor` (per-slot-row subscribe pill sourced from `_subscribedSlots` set).
- MODIFY: `Components/Pages/Open.razor` (3-way filter pill segment + auto-default logic + `_filterMode` state field + auto-subscribe-after-sign-up branch + small footer chip on success).
- MODIFY: `Components/Pages/Home.razor` ("Slots I'm subscribed to (N)" panel below the existing ministries panel).
- NEW migration: `AddSlotInterests` (pure additive; creates the table + composite-unique index; no schema mutations to existing tables; no data migration).
- NEW tests: `tests/ServantSync.Tests/SlotInterestServiceTests.cs` (~14 cases mirroring `MinistryInterestServiceTests`' positive / negative / `AlreadySubscribed` / `PermissionDenied` matrix + 2 new edge cases: `AutoFromAssignment` source sets correctly when called from `Open.razor.SignUp()` flow; cross-org stub Person with re-parented slot interest after stub claim).

**Performance notes.** Sample church has 4 members × 5 slots → SlotInterest is a ≤20-row junction. /Open filter JOIN is well within existing responsive budget (no paging needed round 1). The 3-way filter adds ~1 ms in-memory filter work to the Open.razor Reload query.


**User ask (verbatim).** "Currently users can 'join' a ministry. I'd like for them to also be able to 'volunteer' for a slot. Then on open slots we show only slots which are volunteered for and not all slots within a ministry. Someone might want to see referee slots for a game but have no interest in coaching."

**Why now.** Today the `/Open` filter (per round-FR-patterns) sits at the **ministry** granularity: a volunteer who joined an AWANA ministry sees every AWANA slot's open shifts, regardless of whether they actually want to coach, lead songs, do refreshments, or work the welcome desk. Concretely, in a Springfield Youth Soccer League ministry with `Coach`, `AssistantCoach`, `GameDayReferee`, `Concessions`, and `DevotionLeader` slots, a parent who only wants to referee games still sees all five slot families' open shifts, and gets sexit-button fatigue on /Open. Splitting the existing two-tier interest model — Org membership + Ministry interest — into a clean three-tier model — Org membership + Ministry interest + **Slot interest** (NEW) — gives the volunteer a single subscription knob per slot, and gives the `/Open` page a single new filter that says "I only want to see shifts for slots I've signed up for".

**Design-verdict provenance.** This spec is grounded on the thinker's verdict on the modeling question. Three options were weighed (new `SlotInterest` table vs reusing `AssignmentStatus.Tentative` vs piggy-backing on the existing `MinistryInterest` pattern) and the verdict picked the new table per the analysis below.

**Modeling verdict (per the thinker's design pass).**

Option (A) — new `SlotInterest` table — wins:
- `Assignment` (`Models/Assignment.cs:17`) models a CONCRETE TIME-BOUND COMMITMENT (StartUtc + EndUtc + AssignmentStatus.Scheduled/Tentative/...). Repurposing `AssignmentStatus.Tentative` for "volunteer interest" conflates intent with scheduling and requires faking `StartUtc=EndUtc` (no real time yet) — wrong shape.
- `MinistryInterest` is a per-(user, ministry) row in a separate service (`IMinistryInterestService`) that already exists. Slot interest is structurally identical (per-(user, slot)) so following that exact pattern is the obvious-shared-shape win.
- Slot interest is logically distinct from BOTH membership and assignment. Two users can volunteer for the same slot. A user can volunteer for a slot they're already scheduled into (idempotent — re-declared interest is a no-op). A slot can have many interested volunteers and few scheduled ones (recruiting mode).
- Composite-unique `(PersonUserId, ServiceSlotId)` enforces one row per (volunteer, slot) — toggling interest is a material no-op if the row exists.

Decision NOT made (see decisions Q9): keep interest at the SLOT level (per-role, not per-occurrence). A future round could split this into slot-interest + occurrence-interest if a volunteer wants Saturday 9am coaching but not Tuesday 6pm coaching; outside round 1.

**RBAC (per design-verdict decision Q5).**

| Who | Can toggle interest for |
|---|---|
| The user themself | Their own interest in any Active slot in any ministry in any org they have a real (non-stub) `OrganizationMembership` in. |
| Slot Coordinator (per Round-FR-5's `ServiceSlot.CoordinatorPersonUserId`) | Any volunteer's interest in their slot. The "I told my coord I'd referee but I'm not good with computers" case — common at small churches + youth leagues. |
| Ministry Director (per FR-5) | Any volunteer's interest in any slot in their assigned ministries. |
| Organization Admin | Any volunteer's interest in any slot in their org. |
| System Admin | No direct toggle (out of scope per existing admin-tier discipline); can be added if a SystemAdmin surface surfaces it later. |
| Stub Person (Round-FR-3) | Cannot toggle their own (stubs have no usable auth); a coordinator toggling on their behalf stamps `Source=SlotCoordinatorOnBehalf` (or higher tier) and rides through the FR-3 stub-claim re-parent. |

The `Source` enum encodes which tier toggled the row — critical for the audit trail and for "who can undo this" UX (a self-declared interest can be un-toggled by the user; a coordinator-Stamped interest can be un-toggled by the same coordinator or higher).

**Model additions (per the new-table verdict).**

1. **`SlotInterest` (NEW).**
   - `Id` (int, PK)
   - `PersonUserId` (FK IdentityUser, required, cascade-delete matching OrganizationMembership + Assignment on Person delete)
   - `ServiceSlotId` (FK, required, cascade-delete on slot delete — slot interests orphan with the slot)
   - `CreatedUtc` (DateTime, default UtcNow) — when the row was created
   - `Source` (`SlotInterestSource` enum, default `SelfDeclared`) — which tier toggled it
   - `CreatedByPersonUserId` (FK IdentityUser, nullable) — when Source ≠ SelfDeclared, the actor who toggled on the user's behalf
   - `[StringLength(500)] Notes` (nullable) — coordinator-provided reason for a delegated toggle (e.g. *"volunteered via phone call at the 5/14 league meeting"*)
   - Composite-unique `(PersonUserId, ServiceSlotId)` enforces the no-duplicate-rows invariant
   - Index on `(PersonUserId)` to support the `/Open` "My slots" filter query in O(matching-row-count)
   - Index on `(ServiceSlotId)` to support the per-slot roster view on a coordinator page

2. **`SlotInterestSource` (NEW enum in `Models/Enums.cs`).**
   ```csharp
   public enum SlotInterestSource { SelfDeclared = 0, SlotCoordinatorOnBehalf = 1, MinistryDirectorOnBehalf = 2, AdminOnBehalf = 3 }
   ```
   Doesn't change existing enum values; purely additive. No data migration for existing rows.

**Library / service decisions.** Pure C# / EF. No new NuGet deps. Round-FR-7 specs a NEW `ISlotInterestService` mirroring `IMinistryInterestService` (same `PersonUserId → hashset<TId>` shape, same `JoinAsync` / `LeaveAsync` / `ListJoinedAsync` verb pattern per round-FR-patterns). Round-1 keeps `IAssignmentService` untouched; the filter-router change is in `Open.razor` on the client side (already supports a `filter` arg per round-pre-FR-7).

**Route surface.**
- NEW minimal-API endpoint `POST /ServiceSlots/{slotId:int}/Interest` — toggle interest ON. Body: empty `{ }`. Response: 200 with `{ Succeeded: bool, Reason?: "already-on" | "permission-denied" | "slot-not-found" | "slot-inactive" | "stub-cannot-self-declare" }`.
- NEW minimal-API endpoint `DELETE /ServiceSlots/{slotId:int}/Interest` — toggle interest OFF. Same response shape; Reason values: `"already-off" | "permission-denied" | "slot-not-found"`.
- The `/Open` page gains a query-param `?filter=slots|ministries|all` (default=`slots` per decision Q2 — the user explicitly wants the volunteered-for filter to be default-on).
- No new top-level Razor page in round 1; the Slot Detail page (`/ServiceSlots/{Id}`) gains a single inline toggle button + the Ministry Detail page (`/Organizations/{orgId}/Ministries/{mId}`) gains an inline toggle per slot row.

**`/Open` page change (per design-verdict decision Q2; mirrors Open.razor's existing `FilterState` pattern).**

Replace the current two-segment pill (`My ministries` / `All ministries`) with a three-segment pill:
- **`My slots` (default-on).** Open shifts from slots the user has volunteered for AND ministries the user has joined. Volunteer on a slot they haven't joined the ministry for? Yields "results from joined ministries" only — auto-restrict, no auto-join (per existing "Join and sign up" pattern in Open.razor's card footer).
- **`My ministries`.** Open shifts from any slot in any joined ministry (current My-ministries behaviour).
- **`All ministries`.** Open shifts across every ministry in the selected org (current All-ministries behaviour).

The org picker stays. The 90-day forward window stays. The training-compliance check stays. The `Join and sign up` CTA on the card footer (the existing per-shift join flow when the user hasn't joined the ministry yet) stays.

**`/ServiceSlots/Detail` page change.**

Add a small Bootstrap toggle button to the slot detail header:
- Current user has interest → green `btn-outline-success`, text "I'm volunteering for this slot — click to un-volunteer".
- Current user does NOT have interest → blue `btn-outline-primary`, text "Volunteer for this slot".
- Stub current user → button disabled with tooltip "claim this stub first to volunteer".
- Coordinator-Affiliated current user → button shows a small spinner-mode "Declaring on behalf of …" (the Source enum drives the audit trail).

**`/Organizations/{orgId}/Ministries/{mId}` page change.**

Each slot row in the ministry's slot list gains a small inline toggle (same shape as the slot-detail button), so a volunteer browsing from the ministry landing can declare interest without drilling into the slot detail. State persisted to URL `?slot=42&vol=1` so the toggle is shareable.

**`/MySchedule` page change: ZERO (per design-verdict decision Q3).**

`/MySchedule` keeps its strict itinerary of actual time-bound commitments. A "volunteer interest" is NOT a calendar event — bleeding interest into the calendar would confuse volunteers about whether they're actually scheduled. The interest filter surfaces on `/Open` (where the user IS choosing shifts) — not on `/MySchedule`. Decision Q3 in the verdict.

**Service additions: `ISlotInterestService`.**
- `ToggleInterestAsync(slotId, personUserId, callerUserId)` — server-gated per the RBAC matrix above. Returns `SlotInterestResult { ToggledOn | ToggledOff | AlreadyOn | AlreadyOff | PermissionDenied | SlotNotFound | SlotInactive | StubCannotSelfDeclare }`. Idempotent: re-clicking flips; double-clicks at the same state no-op with `AlreadyOn`/`AlreadyOff` (so the UI can prompt-without-side-effect during optimistic-update rollback).
- `ListInterestsAsync(personUserId)` — HashSet of slot IDs, mirrors `IMinistryInterestService.ListJoinedAsync`.
- `ListInterestedPeopleAsync(slotId, callerUserId)` — for the Slot Coordinator's "who has volunteered?" roster (gated: only the slot's coordinator / ministry director / org admin can call).
- Stub-claim cross-effect (per FR-3 coupling): `PersonService.ClaimStubAsync` now ALSO migrates `SlotInterest` rows from `oldStubId → newIdentityUserId` alongside the existing migration list. The multi-statement SQL `PRAGMA defer_foreign_keys = 1` recipe (already proven in FR-3.2) gains an additional `UPDATE SlotInterests SET PersonUserId = {newIdentityUserId} WHERE PersonUserId = {oldStubId}` line; the canonical `every Person.UserId FK` enumeration in the FR-3.2 inline comment grows by one bullet.

**UI additions / overrides.**
- `Components/Pages/ServiceSlots/Detail.razor`: gain `IHttpClientFactory` or direct `HttpClient` injection; the in-page button is `@onclick` to a POST/DELETE against the API. Toggle refreshes on success.
- `Components/Pages/Organizations/Detail.razor` (Ministries tab): each ministry row surfaces a small count of slots within that ministry + a "Manage slot interests" link (org-admin/coord-gated) that opens the slot list. Round-1 keeps the existing ministry-detail drill-down; the inline toggle is on `/Ministries/{mId}` not on the org-detail `Ministries` tab (avoids clutter).
- `Components/Pages/Ministries/Detail.razor`: gain inline toggle per slot row, same shape as slot-detail.

**Stub-claim migration AMENDMENT.** `Services/PersonService.ClaimStubAsync`'s multi-statement SQL grows one UPDATE. The inline comment enumerating `every Person.UserId FK` includes `SlotInterests` (NEW). +1 audit case in `tests/ServantSync.Tests/PersonServiceTests.cs` covering the stub-claim-with-volunteer-interest scenario.

**Tests.**
- NEW `tests/ServantSync.Tests/SlotInterestServiceTests.cs` (~12 cases) covering:
  - Toggle idempotency (`AlreadyOn` / `AlreadyOff` on repeat clicks)
  - RBAC matrix: self, slot-coord, ministry director, org admin, foreign-actor, stub-self-declined
  - Composite-unique invariant (no duplicate rows on race)
  - Cascade-delete on Person delete (mirror `MinistryInterest` test)
  - Cascade-delete on Slot delete
  - Stub-claim migration: `$ SlotInterest row migrates from oldStubId to newIdentityUserId`
- EXTENDED `tests/ServantSync.Tests/PageAccessTests.cs` (+3 entries): the new minimal-API endpoints gate tests.
- EXTENDED `tests/ServantSync.Tests/PersonServiceTests.cs` (+1 entry): stub-claim retains `SlotInterest` rows.
- No bUnit coverage added (consistent with codebase pattern; service-layer is the security-critical path).
- `tests/ServantSync.Tests/OpenRazorTests.cs` (NEW) — ~5 cases proving the three-state filter routing. The tests stub the `IAssignmentService` to record which `filter` arg was passed and assert the right value per pill segment.

**Migration.**
NEW `dotnet ef migrations add AddSlotInterests` (auto-name). Purely additive: 1 `CreateTable` block for `SlotInterests` + 1 row-add for `SlotInterestSource` enum registration in the snapshot. No `DropColumn`, no data migration. Existing `MinistryInterest` rows untouched.

**Edge cases + behavior.**
- *Volunteer toggles interest on an inactive slot:* returns `SlotInactive`, page surfaces an inline alert. Coordinator can flip `ServiceSlot.IsActive=true` and the volunteer retries.
- *Volunteer joins a ministry after declaring slot interest for a slot in that ministry (orphaned link):* the multi-statement `SlotInterest` row IS valid regardless of `MinistryInterest` state; the `/Open` My-slots filter is the cross-product of `SlotInterest ∩ joined-organization-slots`. A user with slot interest but no ministry interest won't see any shifts (the cross-product is empty); the /Open empty-state CTA changes accordingly (round 1: existing empty-state CTA, future round 2: a "you've volunteered for N slots in ministries you haven't joined — join them to see shifts" CTA).
- *Slot is deleted while a volunteer has interest in it:* cascade-delete removes the `SlotInterest` row silently per EF Core's no-action-on-cascade-update configuration.
- *Slot's ministry is re-named:* `SlotInterest.ServiceSlotId` doesn't change; the UI re-resolves the ministry name on each Reload. No additional migration.
- *Volunteer toggles interest on a `SlotCoordinatorOnBehalf` source that they themselves previously toggled OFF:* the row exists with Source=SlotCoordinatorOnBehalf. Toggle-off by the volunteer themselves? Allowed per decision Q5 (a self-stamped toggle-off beats a still-valid `ToggledBy` audit). The audit trail (`CreatedBy`, `Source`) is preserved per round-1 policy; round-2 could grow a history table if multi-audit conflicts arise.
- *Volunteer's profile says they have a training-completion with `IsValid(now)=false` for the slot's `TrainingRequirements`:* interest toggle is STILL allowed (different from assignment-sign-up which is gated). The intent is independent of the scheduling gate; round-2 could surface a warning badge on the toggle button ("FYI: training is overdue — signing up will fail until training is current"). Round-1 ships the warning badge as disabled-when-incomplete (the simpler pattern).
- *Slot has `Capacity=10`, 10 already-scheduled, 0 with interest:* a fresh volunteer's toggle-on succeeds; their shift SIGNUP later fails per the existing capacity gate. Interest is decoupled.
- *Volunteer toggles interest twice fast (race):* the `SlotInterestService` uses a SERIALIZABLE transaction or an upsert-pattern that handles the unique-violation gracefully. Round-1 spec: catch `DbUpdateException` wrapping a unique-constraint violation, return `AlreadyOn`. Round-2 could add ETag-style optimistic concurrency.
- *Email-notify on slot-occurrence publish to all interested volunteers:* OUT OF SCOPE for round 1 (decision Q9). Round-2 candidate.
- *Per-occurrence interest ("only Sept 14 9am"):* OUT OF SCOPE for round 1 (decision Q9). Round-2 candidate.

**Decisions (resolved during spec authoring session).**
1. **Modeling:** **NEW table `SlotInterest`.** Thinking-verdict rejected `Assignment.Tentative` reuse (wrong shape) and `MinistryInterest` piggyback (`MinistryInterest` is ministry-scoped service; slot is one level deeper — slot interest has its own cardinality).
2. **`/Open` filter UX:** **Three-segment pill, "My slots" default-on.** This is the user's explicit ask: "show only slots which are volunteered for". Default-on is the right starting point; a volunteer can flip back to "My ministries" or "All ministries" for discovery mode.
3. **`/MySchedule` change:** **NONE.** Keeping interest out of the calendar prevents "am I scheduled Saturday?" confusion. Calendar stays strict to actual assignments.
4. **`/ServiceSlots/Detail` + `/Ministries/{mId}` UX:** **Both.** Inline toggle on the slot detail page; inline toggle on each slot row in the ministry's slot list. State stored in `SlotInterest` (server is canonical).
5. **RBAC matrix:** **Self + Hierarchy Managers.** A coordinator pushing on behalf of a volunteer is a real-world pattern (the "I'm not great with computers" volunteer). Audit-trail via `Source` enum + `CreatedByPersonUserId`.
6. **Auto-assignment on interest:** **NEVER.** Interest is intent; assignment is commitment. The two user actions remain distinct.
7. **Migration impact:** **Zero-data migration.** Purely additive table. Existing MinistryInterest rows untouched.
8. **Stub-claim interaction:** **`SlotInterest` rows migrate with Person.UserId.** Same multi-statement SQL pattern as FR-3. The canonical `every Person.UserId FK` enumeration grows one row. Plus-1 test case enforces.
9. **Out of scope for round 1 (Tier-2 follow-up list):** email notifications on slot-occurrence publish to interested volunteers; per-occurrence interest vs per-slot interest; training-pre-req warning badge on toggle button; pin-the-interest-by-ministry-context (cross-volunteer / cross-org discovery).
10. **Idempotency under rapid clicks:** **Server-side unique-constraint-violation catch returns `AlreadyOn`/`AlreadyOff` to the caller.** Page UI optimistically toggles, server tells the truth.

**Files this round would touch (when implementation starts).**
- NEW model: `Models/SlotInterest.cs`.
- NEW enum: add `SlotInterestSource` to `Models/Enums.cs`.
- MODIFIED `Models/Enums.cs`: append the new enum.
- MODIFIED `Data/ApplicationDbContext.cs`: add `DbSet<SlotInterest>`, the composite-unique config, the indexes.
- MODIFIED `Services/PersonService.cs`: extend `ClaimStubAsync`'s multi-statement SQL with one UPDATE on `SlotInterests`; grow the inline `every Person.UserId FK` enumeration by one bullet.
- NEW service: `Services/ISlotInterestService.cs` + `Services/SlotInterestService.cs`.
- MODIFIED `Program.cs`: register the new service in DI; add the two minimal-API endpoints (`/ServiceSlots/{slotId:int}/Interest` POST + DELETE); add antiforgery gates (round-AN pattern, mirrors the existing endpoints in this file).
- MODIFIED `Components/Pages/Open.razor`: three-segment pill (replace the existing two-segment) + `filter=slots|ministries|all` query-param default `slots` + a `?_vol=1` success-toast when a toggle-on from /Open fired the API.
- MODIFIED `Components/Pages/ServiceSlots/Detail.razor`: toggle button + audit-trail display ("you've been volunteered for this slot on 2026-08-12 by Sara Smith").
- MODIFIED `Components/Pages/Ministries/Detail.razor`: inline toggle per slot row.
- NEW migration: `Data/Migrations/<autogen>_AddSlotInterests.cs` + `.Designer.cs`.
- NEW tests: `tests/ServantSync.Tests/SlotInterestServiceTests.cs` (~12 cases) + `tests/ServantSync.Tests/OpenRazorTests.cs` (~5 cases).
- EXTENDED tests: `tests/ServantSync.Tests/PageAccessTests.cs` (+3) + `tests/ServantSync.Tests/PersonServiceTests.cs` (+1).
- STATUS.md + PLAN.md ledger updates at round-ship time.


## Cleanup tasks (UI polish, copy edits, minor UX fixes)

Low-risk, incremental improvements that don't change functionality.
Each item is a small self-contained commit.

### C-1: Training catalog page — trim verbose intro paragraph ✅

**Page:** `Components/Pages/Training/Manage.razor`

**Current text (visible to the user).**
> Training content you administer. Since round N this is the
> org-admin edit surface [and more internal implementation notes]
> ...this is the org-admin edit surface.

**Target text.**
> Training content you administer. Volunteers only see *their*
> outstanding requirements and history on the /Training page.

**Rationale.** The current paragraph exposes internal implementation
notes (round numbers, architecture details) to end users. The
reduced version tells a coordinator what the page is and where
volunteers see their view — nothing more.

**Files touched.**
- `Components/Pages/Training/Index.razor` — replace the intro
  paragraph text. No code changes, no logic changes.

**Estimated effort:** 5 minutes. One-line text replacement.