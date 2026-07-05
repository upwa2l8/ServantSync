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
