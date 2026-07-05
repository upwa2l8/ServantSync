# ServantSync — status & next steps

> **Read this first on every new session.** It captures the current
> build state, the seed data, the known quirks, and the pending work,
> so you (or the next AI) can pick up without re-deriving the
> architecture.

The original product spec is in [`PLAN.md`](PLAN.md). The end-user
documentation is in [`README.md`](README.md). This file is the
working-state companion to those two.

## Where we are

- **Latest — Round-AV: per-org timezone routing on `/Open` + bullet-character regression fix + 309-test suite upgrade (8 steps, multi-pass).** User reported two distinct symptoms on `/Open`: (a) the date stamps rendered the literal six-character string `u2022` instead of the actual bullet character `•` between the date and the time; (b) every stamp was rendered in UTC regardless of the volunteer's actual timezone. User's framing: *"when browsing open slots can we show dates and times in local times rather than UTC? if it's too difficult to determine by user we can have a timezone setting for the organization level."* Round-AV ships the end-to-end fix as eight named steps, each individually scoped + code-reviewed. **(1) Bullet-character fix.** `Components/Pages/Open.razor` — wrapped the literal `\u2022` inside `Format="@(\"ddd, MMM d \u2022 h:mm tt\")"` so Razor parses it as a C# string literal whose escape resolves to the actual U+2022 character; before the wrap, Razor treated the attribute as a plain HTML value and forwarded the six raw characters `\u2022` to `DateTime.ToString`. **(2) Persistent org-TZ column.** `Models/Organization.cs` + new EF migration `Data/Migrations/<autogen>_AddOrganizationTimeZone.*` (added in this round) — added a nullable `TimeZoneId TEXT(64)` column. Legacy orgs backfill as `NULL` so the round-N "use browser zone only" behavior is preserved verbatim; new orgs default to `NULL` and the admin-set form forces a non-null id only if the picker is left not at the empty option. **(3) LocalTime 3-tier resolution chain.** `Components/Shared/LocalTime.razor` — added a `[Parameter] string? FallbackTimeZoneId` and a new private `ResolveZone()` method. Precedence: tier 1 is the browser-detected `TzProvider.TimeZoneId` (strict — surfaces a real JS-layer bug rather than silent mislocalization), tier 2 is the new `FallbackTimeZoneId` (defensive — a hand-typed or migrated IANA typo in the DB must not break a page render, so wraps `TimeZoneInfo.FindSystemTimeZoneById` in a broad `catch (Exception)` per the round-AV doc-string intent; narrower `TimeZoneNotFoundException` / `InvalidTimeZoneException` was the alternative and was DELIBERATELY rejected on this layer — see the WHY-block in `LocalTime.razor`), tier 3 is `TimeZoneInfo.Local` for the prerender window before either of the above is known. **(4) Admin picker on `/Organizations/{id}/Edit`.** New `Services/TimeZoneOptions.cs` (curated `IReadOnlyList<TimeZoneOption>` of ~32 IANA ids partitioned by region — Americas, Europe, Africa & Middle East, Asia & Pacific, UTC — constrains writes to known-good ids so admins can't surface a Windows-style id through the UI). New `Components/Shared/IanaTimeZoneAttribute.cs` (custom `ValidationAttribute` — null/empty/whitespace passes (= "use browser default"); any other value is round-tripped through `TimeZoneInfo.FindSystemTimeZoneById` with two SPECIFIC catches: `TimeZoneNotFoundException` for ids the OS tz table doesn't know, and `InvalidTimeZoneException` for structurally malformed ids; broad `catch (Exception)` was deliberately rejected at the form-layer per the system's "no unnecessary try/catch" rule). `Components/Pages/Organizations/Edit.razor` grew a Bootstrap `<InputSelect class="form-select">` with `<optgroup>` partition + a `<option value="">` header mapped to `null` (the picker "default" position); the save path coerces empty/whitespace back to `null` via `Trim` on the write so the user-intent survives the round-trip. **Hidden signature bump:** `IOrganizationService.CreateOrgAsync` gained a `string? timeZoneId` trailing parameter so the new-org picker choice survives the `/New` round-trip (without this bump, the picker display would survive but the DB write would silently drop the column). 15 existing test call-sites (13 in `tests/ServantSync.Tests/OrganizationServiceTests.cs` + 2 in `tests/ServantSync.Tests/PageAccessTests.cs`) were updated via `sed` to pass `timeZoneId: null` at the test boundary; no test semantics changed. **(5) Project org-TZ onto the open-shifts read path.** `Services/IAssignmentService.cs` extended `OpenSlotOccurrenceView` with a new trailing field `OrganizationTimeZoneId` (string?), with a 4-line doc-comment spelling out the round-AV contract: null = "no override" → `LocalTime`'s chain steps down; invalid id present → handled defensively by `LocalTime`'s tier-2 catch. `Services/AssignmentService.ListOpenSlotOccurrencesAsync` extended the LINQ projection with `join org in db.Organizations on m.OrganizationId equals org.Id` + `select OrganizationTimeZoneId = org.TimeZoneId` + a named arg in the final `OpenSlotOccurrenceView` constructor call; the join is INNER because `Ministries.OrganizationId` is a non-nullable FK. **(6) Render-path wire-up on `/Open`.** `Components/Pages/Open.razor` — the two `<LocalTime>` invocations (one start-time stamp with `Format="@(\"ddd, MMM d \u2022 h:mm tt\")"`, one end-time stamp with `Format="h:mm tt"`) gained `FallbackTimeZoneId="@s.OrganizationTimeZoneId"`. **(7) Tests.** `tests/ServantSync.Tests/LocalTimeTests.cs` (NEW) — 6 bUnit + xUnit tests: `ProviderTimeZone_TakesPrecedence_OverFallback` (LA wins over NY when both are set, asserting 07:00 PDT not 10:00 EDT), `FallbackTimeZoneId_Used_WhenProviderMissing` (NY from tier 2 when provider is null), `FallbackTimeZoneId_Used_WhenProviderEmpty` (NY from tier 2 when provider is ""), `InvalidFallbackId_FallsThrough_SilentlyWithoutThrowing` (rendered output MUST match `Assert.Matches(@"^\d{2}:\d{2}$", ...)`; locks in "tier 3 took over" not just "tier 3 didn't crash"), `ProviderTimeZone_IsStrict_ThrowsOnInvalidId` (explicit `try { RenderComponent; Assert.Fail } catch (TimeZoneNotFoundException) { /* expected */ } catch (Exception ex) { Assert.Fail(...) }` so the asymmetry between tier 1 (throws) and tier 2 (catch) is regression-tested and so the test can't silently pass via bUnit exception-routing buffering), and `OpenRazor_BulletFormat_IsWrappedInAtParen_SoRazorResolvesEscape` (file-content regression guard — walks up from `AppContext.BaseDirectory` to locate `Components/Pages/Open.razor`, then asserts the wrapped fixed-form `Format=\"@(\"ddd, MMM d \u2022 h:mm tt\")\"` is present AND the buggy literal-string form `Format=\"ddd, MMM d \u2022 h:mm tt\"` is absent; future contributors who drop the `@(...)` wrap will trip this test). `tests/ServantSync.Tests/OrganizationServiceTests.cs` (+4 tests): `CreateOrg_WithValidIanaTimeZoneId_PersistsColumn`, `CreateOrg_WithNullTimeZoneId_StoresNullColumn`, `CreateOrg_WithWhitespaceTimeZoneId_StoresNullColumn`, `CreateOrg_WithTimeZoneId_TrimsTrailingWhitespace`. **(8) Final build + STATUS.md + final commit.** This entry. **Files touched.** Step 1: `Components/Pages/Open.razor` (Format wrap); step 2: `Models/Organization.cs`, `Data/Migrations/<autogen>_AddOrganizationTimeZone.{cs,Designer.cs,snapshot}` (MODEL-LEVEL; see Status of the migration file below); step 3: `Components/Shared/LocalTime.razor`; step 4: `Services/TimeZoneOptions.cs` (NEW), `Components/Shared/IanaTimeZoneAttribute.cs` (NEW), `Components/Pages/Organizations/Edit.razor`, `Services/IOrganizationService.cs`, `Services/OrganizationService.cs`, `tests/ServantSync.Tests/OrganizationServiceTests.cs`, `tests/ServantSync.Tests/PageAccessTests.cs`; step 5: `Services/IAssignmentService.cs`, `Services/AssignmentService.cs`; step 6: `Components/Pages/Open.razor`; step 7: `tests/ServantSync.Tests/LocalTimeTests.cs` (NEW), `tests/ServantSync.Tests/OrganizationServiceTests.cs`; step 8: `STATUS.md` (this entry). **Untouched on this round (Tier 2 followup, per the per-site discipline).** The 13 other `<LocalTime>` usages across the codebase were deliberately NOT wired to `FallbackTimeZoneId` — only `/Open` shipped in round-AV because that's the page the user named. The remaining sites split into two categories: (a) no parent org — `Games/Detail`, `Teams/Detail`, `Leagues/Detail`, `GameDayCalendar`, `Training/Index` (these don't have an Organization ancestor at all, so a per-org TZ doesn't apply); (b) parent org exists but the loaded model doesn't currently carry it — `Dashboard.razor`, `MySchedule.razor`, `People/Detail.razor`, `AssignmentCalendar.razor`, `ServiceSlots/Detail.razor`, `ServiceSlots/Schedule.razor`, `ServiceSlots/ScheduleSeries.razor`. Threading `FallbackTimeZoneId` into (b) requires a `Ministry → Organization → TimeZoneId` lookup in each loader, which is beyond the user's literal ask (which named `/Open`); the per-org TZ would still apply if the admin's intent is "all shifts in this org stamp in our zone", but the user's mind-model was "when browsing open slots" — i.e., volunteer-facing only. Logged for tracking if it surfaces. **`LocalTimeTests.cs` scope.** The bullet-character regression test guards `Components/Pages/Open.razor` only. If a future page (Dashboard, Schedule, et al.) copies the bullet pattern incorrectly (drops the `@(...)` wrap), this test will not catch it — a more thorough guard would sweep across all `Components/**/*.razor` for `Format="..."` not wrapped in `@(...)`. Out of scope for round-AV but flagged below. **Migration status.** The `Data/Migrations/<autogen>_AddOrganizationTimeZone` migration file generated by `dotnet ef migrations add` (the auto-generated migration name format used by this repo) was added in step 2; tests use `EnsureCreated()` and don't depend on it. Production deploys should run `dotnet ef database update` on the next release cut. **Verification.** Step 8 final: build clean (0 errors / 1 pre-existing unrelated `xUnit2013` in `AssignmentServiceTests.cs:791`); **309/309** tests pass (was 299 baseline pre-round-AV + 10 new this round: 6 LocalTimeTests + 4 OrganizationServiceTests TZ round-trip). No regressions across the existing `AssignmentServiceTests`, `OrganizationServiceTests.cs` pre-existing tests, `PageAccessTests.cs`, or any bUnit / xUnit surface. **Code-reviewer verdict (multiple dispatches across all 8 sub-steps).** Caught and resolved: (a) Step 2 reviewer flagged broad `catch (Exception)` on the tier-2 LocalTime fallback; the round-AV doc-string explicitly DELIBERATELY KEEPS the broad catch on this layer (only the form-layer `IanaTimeZoneAttribute` was narrowed) because tier 2 is the only consumer of admin-typed DB content and the invariant is "garbage string can't break a render" — accepted after clarification; (b) Step 3 reviewer flagged a broken `<see cref="ResolveZone"/>` because the method is private; rewrote to plain `<c>...</c>` references; (c) Step 4 reviewer flagged `IanaTimeZoneAttribute.cs`'s broad `catch (Exception)`; narrowed to two specific catches (`TimeZoneNotFoundException`, `InvalidTimeZoneException`) per the system's "no unnecessary try/catch" rule; (d) Step 4 also caught a real bug — `CreateOrgAsync` was called from `/New` WITHOUT a TZ arg, which would silently drop the picker choice on creation; fixed by extending the service signature with a `string? timeZoneId` trailing arg rather than a `SetTimeZoneAsync` follow-up round-trip; (e) Step 4 caught a writer-side bug — a `public static sealed class TimeZoneOptions` would CS0441 because `static` is implicitly sealed in C#; reverted; (f) Step 4 also caught a `/New`-propagate-bug — the production code at the time called `CreateOrgAsync` without the new arg, fixed via the signature extension above; (g) Step 6 reviewer held the scope discipline; only `/Open` got the `FallbackTimeZoneId` wiring. **Empirical next-step.** User should hard-reload `/Open` and verify three outcomes: (1) date stamps render with the actual bullet character `•` (NOT the literal word "u2022"); (2) dates display in the browser's local zone when the JS interop completes; (3) visiting as an in-org Admin and setting `Organization.TimeZoneId` via `/Organizations/{id}/Edit` to e.g. `America/New_York` makes the shifts stamp in NY time when the browser zone is unset (set via the new picker — drop the cursor on the empty "Use browser default" option to clear an existing setting). The dev server's StaticFiles middleware serves the new Razor markup + new Razor component edits on the next assembly reload — a quick `Stop-Process -Name dotnet` + `dotnet run` may help surface fresh assets if the cached circuit has any old component binds.

- **Prior — Round-AU: redraw the new ServantSync brand logo in SVG (no cross, preserves the ecumenical brand book) (1 round, 5 iterations + 1 doc-fix pass).** User submitted a new reference logo (`C:\Users\robsa\Downloads\ServantSyncLogo.png`, 1254×1254, RGB no-alpha) with two stylized figures forming a heart void + a literal Christian cross above + a sync arrow below + the "ServantSync" wordmark + a "VOLUNTEER MANAGEMENT & TRAINING SYSTEM" tagline. Asked: *"I really like this logo, can we use it instead, or modify it with the same design concept?"* After surfacing the conflict — `BRANDING.md` explicitly bans religious symbols (*"no religious cross, no replacing the C-arcs with literal 'S' ligatures, no flipping cool→red because it reads liturgical"*) because the brand needs to work equally well for a Methodist church, a Christian youth-sports league, AND a secular neighborhood-watch co-op — the user picked *"Redraw as SVG, drop the cross, use my judgment on the navbar."* **Five iterations to land the path math.** (1) Iteration 1 — initial redraw produced two teardrops meeting at the centerline with no visible heart void (a 12 px slit at the top, filled below). (2) Iteration 2 — pulled the inner sides outward to form lobes, but the void read as a spade/shield because the inner walls were straight vertical. (3) Iteration 3 — reworked the path math to widen the cleft to 52 px and pull the lobes adjacent to the cleft (35 px below), and a code-review pass caught three more bugs: sync arcs with `sweep=1` curved the top arc DOWN and the bottom arc UP so they overlapped as a lens instead of forming a circle; the arrowhead polygons didn't match the corrected tangents; one connector dot sat INSIDE the figure body and was effectively invisible. (4) Iteration 4 — fixed all three bugs and reworked the path math again, but a visual audit caught the "towers" issue: the figures' outer top peaks were at y=75 while the cleft was at y=100, so the figures looked like they had heads sticking up above the heart. (5) Iteration 5 — dropped the outer top peak to y=100 (aligned with the cleft) so the heart is the TOPMOST feature. **Final design (matches reference's shapes; honors brand book's colors).** Two stylized figures whose raised arms form an unambiguous heart-shaped void in the negative space. Left figure: cool gradient `#3730a3 → #7c3aed → #a855f7`; right figure: warm gradient `#f59e0b → #f97316 → #e11d48`. Bridge glint: small warm-amber dot (`#fbbf24`, the existing `--ss-glint` token) above the heart cleft — replaces the banned cross with a secular spark. Two opposing sync arrows at the bottom (top arc cool + bottom arc warm) form a clockwise rotation, with a left+right gap emphasizing "two parties meeting" rather than a closed ring. Four connector dots punctuate the outer edges. Void dimensions: 52 px cleft at y=100, 122 px lobes at y=135, 0 px point at y=340 on the centerline. **Seven files touched.** (1) **`wwwroot/img/servantsync-mark.svg`** (square 512×512, 3 KB) — the new mark, transparent background, `role="img"` + `aria-label="ServantSync mark"` + `<title>ServantSync logo mark</title>`. (2) **`wwwroot/img/servantsync-logo.svg`** (landscape 720×280, lockup) — mark on the left at 0.45× scale, "ServantSync" wordmark on the right using a cool→violet gradient + the spaced-caps tagline "VOLUNTEER MANAGEMENT & TRAINING SYSTEM" beneath. (3) **`wwwroot/img/apple-touch-icon.png`** (180×180, RGBA) — re-rasterized from the new mark via headless Chrome `--window-size=180,180 --default-background-color=00000000`. (4) **`wwwroot/img/servantsync-mark.png`** (400×400, RGBA) — re-rasterized for email embedding. (5) **`wwwroot/img/servantsync-logo.png`** (720×280, RGBA) — re-rasterized for README/docs preview. (6) **`BRANDING.md`** — the "TL;DR" / "Why no cross" / "Mark geometry" / "Color tokens" sections all updated to the round-AU design language; the geometry table corrected to the actual SVG coordinates (the prior table still described the old dual-C-arcs design with `tip at (244,96)` / `bridge glint at cy=58` / `heart apex y=96` etc., all wrong for the new mark). (7) **`wwwroot/img/README.md`** — file inventory + regeneration commands were already current for the new asset list. **No C# / Razor / CSS changes** — the navbar wiring (`Components/App.razor` SVG favicon link, `Components/Layout/NavMenu.razor` 36×36 mark, `Components/Layout/NavMenu.razor.css` halo) all reference paths that don't change with the asset swap. The dark navy navbar halo (`rgba(255,255,255,0.06)`) and the 36×36 sizing work cleanly with the new transparent-alpha mark — no navbar tweak was needed (a one-line user decision up front saved a half-round of CSS iteration). **Verification.** Build clean (0 errors / 0 warnings); full suite **299/299** PASS unchanged (no new tests this round; round-AU is brand-asset-only, same test surface as round-AT). **Browser verification.** The SVG mark renders correctly in the navbar against the dark navy backdrop; both figures' colors are distinguishable, the heart void is visible, the glint dot is visible. The standalone PNGs at `/img/servantsync-mark.png` etc. returned 404 from the long-running dev server (the dev server's `StaticFiles` middleware serves from a baked-in snapshot taken at build time) — the PNGs are correct on disk and serve correctly on a fresh `dotnet run` or in production. **Code-reviewer verdict (6 dispatches).** Iterations 1-3 each caught a real geometric bug (no heart / spade / lens + wrong tangents + invisible dot). Iteration 4 caught the "towers" issue. Iteration 5 + the doc-fix pass both approved: "Ship." Minor cosmetic notes deferred to **Tier 2 future work**: (a) the figures' top plateau from x=180 to x=230 at y=100 is flat — a subtle dome (e.g. `C 195 92, 215 92, 230 100`) would feel less like a "tabletop"; (b) the 4 connector dots may become invisible mush at 16-32 px favicon sizes — consider a `<g id="dots-detail">` wrapper the small-favicon rasterizer can drop; (c) the "122 px lobes at y=135" doc claim is precise for that slice, but the void is actually slightly wider (~130 px) between y=180 and y=200 before narrowing to the point. **Untouched on this round (Tier 2 future audit, per the per-site discipline).** Dark-mode variant — `BRANDING.md` documents the recommended dark-mode palette but the SVG ships light-mode only. Future dark-mode work can apply the recommended palette via the `[data-theme="dark"]` selector pattern already documented. **Empirical followup (round-AU-e).** User should hard-reload the dashboard (`/`) to confirm the new mark renders against the dark navbar in their actual browser session; the dev server's static-asset cache held onto the prior build, so a `Stop-Process` + `dotnet run` cycle will surface the fresh assets.

- **Prior — Round-AT: cosmetic cleanup pass — hoist B-a WHY-comment to single block above click chain + idiomatic `+= 1` + lexical-chain precision + grep-coherence renames (1 round, multi-pass).** The round-AR initial ship + round-AS root-cause fix both logged Tier-2 cosmetic nits that a future contributor would either re-litigate or re-anchor on wrongly: (a) `"the round-AR B-a WHY-comment is only above the Next branch but the same await-then-mutate semantics apply to Prev/Jump"` (round-AR's own STATUS verbatim) — a reader scanning the file sees only the Next-branch comment and has to infer the rule for Prev/Jump; (b) `currentPage = currentPage + 1` instead of the idiomatic `+= 1` (round-AR code-reviewer's stylistic feedback); (c) the paintErr scope comment said `"via module scope"` which skips the lexical-chain step (`catch → click listener closure → mountPdf function scope → paintErr declaration` — round-AS removed the prior IIFE so mountPdf is module-top, but the closure walks through mountPdf's local function scope, NOT module scope); (d) the round-AT-affected WHY-comment headers initially used the prefix `"Round-AR-nits:"` which broke the codebase's alphabetical-letter scheme (`Round-AG` / `Round-AL` / `Round-AS` / `Round-AR` / `Round-AO`) — a future `grep -nE 'Round-(A[A-Z]):'` wouldn't line up cleanly. **Round-AT fix (one file, three comment-tweaks).** (1) **`wwwroot/js/trainingview.js`** — hoisted the await-then-mutate WHY-block from the Next branch (asymmetric position) to a SINGLE block ABOVE the entire if/else chain. The new block explicitly names *"ALL three branches (next / prev / jump) follow `await renderPage(target); currentPage = target;`"* so a reader doesn't have to infer the rule. Cross-references round-AR's asymmetric-pre-state and the `+= 1` stylistic feedback. (2) **Idiomatic operators** — swapped `currentPage = currentPage + 1` → `currentPage += 1` on Next, same for Prev's `-= 1`. Jump branch's `currentPage = p` stays (single-step, no double-step needed). (3) **`paintErr` scope comment precision** — rewrote the helper's preamble to enumerate the full lexical chain (`outer-catch / click-listener callback → mountPdf function body → paintErr declaration`) instead of the imprecise "via module scope" wording. WHY-block also notes the audit-chain scheme choice so future round-prefix decisions follow the alphabet. **Audit chain renamed to `Round-AT:`** (replacing the round-AT-pass-1 `Round-AR-nits:` prefix) so both comment headers now grep-cohere against the codebase's alphabetical-letter scheme used in STATUS.md's round headings. **Round-AT text-only — no behavioral diff.** Pure WHY-comment rewording + operator swap. The IIFE-removal (round-AS), the `TryHealPdfPageCountAsync` (round-AI), the catch-scope (round-AR), the error-paint helper (round-AR), and the conditional wrapper-class fix (round-AO) all stay untouched. **Files touched.** `wwwroot/js/trainingview.js` only (2 WHY-comment blocks tightened + 2 operator swaps). No C# changes. **Verification.** Build clean (0 errors / 1 pre-existing unrelated `xUnit2013` in `AssignmentServiceTests.cs:791`); full suite **299/299** PASS unchanged (no new tests this round — round-AT is comment-rewording + operator-idiom, same test surface as round-AS). **Code-reviewer verdict (2 dispatches).** First pass flagged the scope-imprecision + the `-nits`-prefix outlier; second pass confirmed both were addressed and called "Ship." **Untouched on this round.** No file line counts changed in user-facing paint flow; the file's preamble is genuinely dense at this point but each block carries one load-bearing round's context and consolidation would lose audit trail. Future round-AU+ contributors can fold the WHY-blocks if they have round-AU+ work to add to the file; round-AT deferred that consolidation per the per-site-scope discipline. **Empirical followup.** Release-mode smoke test (Round-AT-e) is being run alongside the commit step — `dotnet publish -c Release -o bin\ReleasePub`, run the published binary on `https://localhost:5070`, browser-verify `/Training/3` paints + the volunteer can complete the training under release build, then commit the cosmetic change against this verified-green baseline. See below.

- **Prior — Round-AS: confirm root cause + fix the missing ES-module export (1 round, multi-pass).** User pasted the verbatim JS error text from the round-AR-empowered gray box: **`Could not find 'ServantSyncTraining.mountPdf' ('ServantSyncTraining' was undefined) at https://localhost:7012/_framework/blazor.web.js:1:384`**. That diagnostic localizes the actual root cause — six prior rounds (AO/AP/AQ/AR) fixed layout, CSS visibility, error-paint scope, and catch-block scope, but the fundamental issue was always at the JS module-load layer: the JS file was wrapped in `(function () { ... })();` (IIFE) that hung the namespace off `window.ServantSyncTraining = { ... }` only — no top-level ES `export`. The C# side does `var module = await JS.InvokeAsync<IJSObjectReference>("import", "/js/trainingview.js"); ... module.InvokeVoidAsync("ServantSyncTraining.mountPdf", ...)`. `import()` returns the ES module's exports namespace (which was empty in this file), and Blazor's dotted-identifier lookup found `module.ServantSyncTraining === undefined`. **The IIFE was the structural mismatch.** ES modules already scope variables to the module, so the IIFE was faking closure semantics the module-level scope gives us for free — and worse, it forced the `export const` declaration INSIDE a function body, which is a SyntaxError in module scope. **Round-AS fix (one file, two surgical moves).** (1) **`wwwroot/js/trainingview.js`** — removed the IIFE wrapper entirely. `let pdfjsLib = null;` and `let ytPromise = null;` are now module-private (same effective scope as the prior IIFE-internal state, safely still closed-over by `ensurePdfJs` / `ensureYouTube`). All function bodies de-indented. (2) **`export const ServantSyncTraining = { mountPdf, mountVideo, mountYouTube, mountBestEffortDwell, EngagementSink };`** as the LAST statement at module top level. The legacy `window.ServantSyncTraining = ...` line is OMITTED — grep of the entire codebase (excluding `bin/` / `obj/` / `wwwroot/lib/`) returned **zero** matches for `window.ServantSyncTraining`; the only `ServantSyncTraining` references in app code are Take.razor's 4 `module.InvokeVoidAsync("ServantSyncTraining.xxx", ...)` calls (through `IJSObjectReference`) and the export itself. **Round-AL provenance retained.** A new ~14-line WHY-comment block between the round-AG signature contract and the round-AS IIFE-removal block: round-AK.0 introduced per-tick `?v=${Date.now()}` URL tokens; round-AL reverted them with `Mechanism unconfirmed`. Round-AS supplies the actually-confirmed root cause (missing ES export); round-AL's `?v=` revert stays as a Tier-2 future-work item only. **Files touched.** `wwwroot/js/trainingview.js` only (IIFE removal + top-level export + WHY-comment restoration + 17→11 line WHY-comment trim + hedge-strip in round-AL provenance). No C# changes. **Verification.** Build clean (0 errors / 1 pre-existing unrelated `xUnit2013` in `AssignmentServiceTests.cs:791`); full suite **299/299** PASS unchanged (no new tests this round; round-AS is JS-only, same test surface as round-AO/AP/AQ/AR). **Code-reviewer verdict (4 dispatches).** "Ship" with the diagnostics confirmed: first-pass flagged the SyntaxError-trap (`export const` inside IIFE); second-pass confirmed the IIFE-removal is safe; third-pass called out the round-AL provenance drop and the WHY-comment length; fourth-pass confirmed the hedge-strip is honest and the audit chain is preserved. **Empirical next-step.** After rebuild, the user should hard-reload `/Training/3` and the PDF should finally paint (canvas appears, badge moves `0/5` → `1/5` as page-1 renders, engagement unlocks when N/5 is met). Six rounds closed; if this still fails, the diagnostic will be in front of us, not buried.

- **Prior — Round-AR: extend JS error-paint scope + page-2+ retry correctness + surface `_lastError` in structural panel (1 round + sub-fix).** User confirmed round-AQ's structural fallback panel rendered ("Engagement tracking unavailable in this browser — using native rendering. Mark-completed gating is still enforced.") BUT reported the gray canvas-host was still empty — no red `alert-danger` text visible. That localizes the failure: round-AQ's JS `try/catch` only wrapped `await renderPage(1)` and the click-listener wiring. The earlier steps in `mountPdf` (`new EngagementSink(...)`, `await ensurePdfJs()`, `await pdfjs.getDocument({ url }).promise`, `sink.setTotal(doc.numPages)`) sat OUTSIDE the catch. When the dynamic `import('/lib/pdfjs/pdf.mjs')` rejected (worker / MIME / network) or `pdfjs.getDocument(...)` rejected (corrupt file / encryption), the throw propagated straight through to the C# MountViewerAsync catch without ever entering the JS catch block — so the gray box stayed empty and the volunteer only saw the structural fallback panel (which itself didn't show `_lastError` content). **Three changes + one sub-fix.** (1) **`wwwroot/js/trainingview.js` mountPdf** — extracted a top-level `paintErr(err)` helper declared BEFORE the try block (so both the outer catch AND the inner click-handler catch can call it). Moved the `try { ... } catch (err) { paintErr(err); throw err; }` SCOPE to wrap the ENTIRE mountPdf body (`new EngagementSink`, `await ensurePdfJs()`, `await pdfjs.getDocument({ url }).promise`, `sink.setTotal(...)`, `await renderPage(1)`, AND the click-listener wiring). Any error in the mountPdf chain now paints inline. WHY-block ~12 lines explains the round-AQ scope gap. (2) **Same file, click handler** — converted `navRoot.addEventListener('click', ...)` from fire-and-forget `renderPage(currentPage)` to `async` + `await renderPage(...)` + inner `try/catch { paintErr(err); }` so a page-2+ failure also paints inline instead of unhandled-rejecting silently. The handler-level WHY-comment cross-references the round-AR mountPdf block. (3) **`Components/Pages/Training/Take.razor` structural-fallback panel** — appended a `@if (!string.IsNullOrEmpty(_lastError)) { <div class="mt-1 mb-0 small text-danger font-monospace">@_lastError</div> }` block INSIDE the existing `alert-warning` so the C# `ex.Message` ("Viewer tracking unavailable: {specific}") sits right next to the unavailable-here message instead of being buried near the Mark Completed button. Razor `@_lastError` auto-escapes for HTML, so the actual JS error text (potentially containing `<` / `>` / `&`) is rendered safely. WHY-comment ~10 lines explains the completeness rationale. **Round-AR sub-fix (B-a followup in-place).** Code-reviewer flagged that the round-AR initial click-handler followed the `currentPage++ ; await renderPage(currentPage);` mutation-before-await pattern. If page-2 failed, currentPage would advance to 2 even though `markViewed(2)` never fired (the badge read would still be 0 for page-2). A retry Next would then jump the volunteer to page 3 — skipping past the still-failed page-2. Sub-fix swapped all three branches (next / prev / jump) to `await renderPage(target); currentPage = target;` — mutate only on successful paint. ~7-line WHY-comment explains. **Files touched.** `wwwroot/js/trainingview.js` (mountPdf try-scope extension + paintErr helper + click-handler wrap + B-a swap); `Components/Pages/Training/Take.razor` (structural-panel `_lastError` display). **Untouched on this round (Tier 2 future audit).** Cosmetic pass: the round-AR B-a WHY-comment is only above the Next branch but the same await-then-mutate semantics apply to Prev/Jump (a future contributor scanning the file will correctly infer the rule but might miss the cross-reference). Could be hoisted to a single block above the entire if/else chain. Logged for stylistic followup. **Verification.** Round-AR final (after sub-fix): build clean (0 errors / 1 pre-existing `xUnit2013` in `AssignmentServiceTests.cs:791`); full suite **299/299** PASS unchanged (no new tests this round; round-AR is markup + JS catch-scope refactor, same test surface as round-AO/AP/AQ). **Code-reviewer verdict.** "Ship." Two minor nits (asymmetric WHY-comment; `currentPage = currentPage + 1` could be `currentPage += 1`) — both logged as future-pass cosmetics. **Empirical next-step.** After rebuild, the user should hard-reload `/Training/3` and observe three branched outcomes:
  - **Happy path (the actual root cause was a stale asset cache, not real mounting failure):** canvas paints page-1, badge moves `0/5` → `1/5`, engagement unlocks when N/5 is met. ← wrap-up.
  - **Genuine JS exception now painted inline** (most likely the intended outcome of round-AQ round-2): the gray box contains a Bootstrap red alert reading `PDF rendering failed: <actual JS error text>` AND the structural fallback panel below it shows `_lastError` content too. **The exact text inside the alert is the diagnostic.** If it reads "Failed to fetch dynamically imported module: /lib/pdfjs/pdf.mjs" — worker URL or MIME issue. If it reads "Setting up fake worker failed: ..." — the `workerSrc` URL isn't returning JS. If it reads "Failed to load PDF" — the path or storage layer. Paste the exact message back and round-AS targets the actual root cause.
  - **Partial scope fallthrough** (very rare): JS-side catch paints AND clicks re-throw. Retry Next → catch fires inside the click handler → paintErr paints the page-2 error message → user can retry. B-a followup keeps currentPage pinned to the still-failed page so they don't skip past it.

- **Prior — Round-AQ: catch-block rerender + inline JS error reporting (1 round).** User reported that even after round-AO (layout) + round-AP (canvas CSS + visible empty-state), the PDF still did not load — the gray box rendered correctly, the buttons stacked correctly, the "Open PDF directly" link was in place, but the canvas inside the gray box was still empty, and the badge stayed at `0 / 5 pages`. Round-AQ diagnoses the **silent-failure-after-exception** half: when any step in the JS mount chain (`ensurePdfJs`, `getDocument`, `doc.getPage`, `page.render`) rejects, the `await` propagates up to the MountViewerAsync try/catch in `Take.razor`, which sets `_trackingFailed = true` — but the catch did NOT call `InvokeAsync(StateHasChanged)`, so the structural fallback warning panel + native iframe (`@if (_trackingFailed ...)`) never re-rendered. The user was seeing the *initial* render's empty gray box forever, with no signal that the JS pipeline had just failed. **Two complementary fixes.** (1) **`Components/Pages/Training/Take.razor` MountViewerAsync catch block** — added `await InvokeAsync(StateHasChanged);` after the `_trackingFailed = true` and `_lastError = ...` assignments. WHY-comment (~13 lines) names the Blazor Server silent-flush-after-catch failure mode, cross-references the JS-side half of round-AQ, and notes the round-AP context. (2) **`wwwroot/js/trainingview.js` mountPdf body wrap** — wrapped `await renderPage(1); const navRoot = ...; navRoot.addEventListener(...);` in a try/catch. On throw, the catch paints a Bootstrap `.alert.alert-danger.m-2.small.mb-0` div directly INTO `#pdf-canvas-host` containing the actual JS error text (`err.message || err.reason.message || String(err)`, with `< > &` HTML-escaped), THEN re-throws so the Blazor catch also fires and the structural fallback (with the new StateHasChanged) renders in parallel. WHY-comment (~10 lines) explains the dual-write strategy — error visible inline (no DevTools required) + state-rerender triggers the structural fallback for guaranteed-content access. **Files touched.** `Components/Pages/Training/Take.razor` (MountViewerAsync catch); `wwwroot/js/trainingview.js` (mountPdf try/catch wrap). **Untouched on this round (Tier 2 future audit, per the per-site discipline).** The click handler inside `navRoot.addEventListener` still calls `renderPage(currentPage)` without an inner try/catch — a page-2+ failure would unhandled-reject silently. Future round-AR followup if it surfaces. **Verification.** Build clean (0 errors / 1 pre-existing `xUnit2013` in `AssignmentServiceTests.cs:791`); full suite **299/299** PASS unchanged (no new tests this round; round-AQ is markup + JS catch-wrap, same test surface as round-AO/AP/AJ/AK). **Code-reviewer verdict.** "Ship." One minor nit (not blocking): `err.reason.message` is dead code unless the catch fires on a raw `PromiseRejectionEvent` (which only fires on `window.addEventListener('unhandledrejection')`, not awaited rejections); could trim to `err.message || String(err)`. Logged for future round-AR. **Empirical next-step.** After rebuild, the user should hard-reload `/Training/3`. Expected outcomes (branched):
  - **Happy path (Hypothesis B was wrong, A was structurally fine at deeper layer):** the canvas paints page-1, the badge resets from `0 / 5` to `1 / 5` on first paint (round-AG mechanism, untouched), engagement unlocks when N/5 is met. ← wrap-up.
  - **Genuine render failure (most likely):** the gray box now contains a Bootstrap red alert reading e.g. `PDF rendering failed: Setting up fake worker failed: ...` and the structural fallback warning + native iframe is also visible below it. **The error text is the diagnostic.** With it in hands, the user pastes the error back, and a future round-AR fixes the actual root cause (worker URL configuration, MIME-type rejection, etc.) instead of guessing.
  - **Partial-failure:** addEventListener fires (page-1 paints), then a later line throws — listener registered, fallback also renders. Click Next → renderPage(2) → may fail silently (round-AR followup noted).

- **Prior — Round-AP: make Training/3 PDF canvas actually visible + defensive Open-directly fallback (1 round).** Round-AO correctly diagnosed and fixed the **layout** half of the Training/3 PDF bug — the buttons no longer cover the canvas area (Bootstrap `.ratio > * { position: absolute; inset: 0 }` collision disarmed by the conditional wrapper class). The user re-tested and reported: "the entire PDF canvas is missing." The buttons were now properly stacked, but inside `#pdf-canvas-host` nothing was visible. Round-AP diagnoses the **paint/visibility** half: round-AJ's bigger revert (STATUS.md round-AJ) bundled three round-AH changes as a single revert, but the **canvas `display: block` CSS rule** that round-AH added was independent of the HiDPI-ratio experiment that actually broke things. With round-AO's wrapper-class conditionalization, Bootstrap's `.ratio > *` rule no longer applies to `#pdf-canvas-host` for the local-PDF branch, so the canvas CSS is once again safe to add back without triggering the round-AH regression. **Three changes.** (1) **`wwwroot/app.css`** — re-added `#pdf-canvas-host canvas { display: block; max-width: 100%; height: auto; margin: 0 auto; }`. Without `display: block`, the `<canvas>` element defaults to `display: inline` and inside a scrollable block parent the canvas collapses to near-zero height for unusual intrinsic-size / overflow-auto combinations — making the rendered PDF look "missing" even though the canvas DOM node has the correct `width` / `height` HTML attributes. WHY-comment (~12 lines) explains the round-AJ bundling history and the post-round-AO disarmed-collision. (2) **`Components/Pages/Training/Take.razor`** `<div id="pdf-canvas-host">` inline-style update: replaced `class="w-100" style="overflow:auto; max-height:70vh;"` with `class="pdf-canvas-host border rounded p-1" style="overflow:auto; min-height: 60vh; background: #f8f9fa; width: 100%;"`. Subtler: `min-height: 60vh` so the host always has enough space for the canvas; subtle gray bg + thin border so the canvas extent is visible even when the rendered PDF page's top region is whitespace (white-on-white-invisible becomes gray-on-white-visible); `border + p-1` for soft outline so the canvas-host reads as a discrete UI surface, not a continuous margin. WHY-comment (~9 lines) explains the round-AP motivation. (3) **Same file** — added a defensive `<div class="small text-muted mt-1">` block right below the Prev/Next `btn-group` containing a direct-download `<a href="@_content.FilePathOrUrl" target="_blank" rel="noopener">Open PDF directly</a>` link + "(always available, even if PDF.js can't render)" hint. The link is INSIDE the local-PDF branch (the parent branch boolean already filtered out `https://`-prefixed paths), so it only ever points at `/uploads/training/<safe-filename>` server-side-validated paths. The `target="_blank" rel="noopener"` keeps the new-tab navigation safe. WHY: even if PDF.js engine ever fails (browser without ES modules, encryption mismatch, corrupt file mid-fetch), the volunteer can always read the file directly without admin re-upload. **No service-level changes.** Round-AP is markup + CSS-only (same test surface as round-AO/AJ/AK). **Files touched.** `wwwroot/app.css` (canvas CSS); `Components/Pages/Training/Take.razor` (canvas-host inline-style + direct-download link). **Verification.** Build clean (0 errors / 1 pre-existing `xUnit2013` in `AssignmentServiceTests.cs:791`); full suite **299/299** PASS unchanged. **Browser verification.** User should hard-reload `/Training/3` (the dev server's StaticFiles middleware serves the new CSS on the next request; the new Razor markup is picked up on the next assembly reload, which means they may need to restart `dotnet run` to see the link / inline-style change). **Code-reviewer assessment.** Confirmed safe (round-AO's conditional wrapper disarm confirmed by re-reading the markup). **Supersession note.** Round-AJ's "ratio keeps rendering visible" reasoning was actually about a DIFFERENT issue (the round-AH HiDPI/cssViewport/renderViewport split + inline dims were the regression — but the canvas `display: block` rule was in the same round-AH commit and got bundling-collateral-damage into round-AJ's revert). Round-AP disentangles the two: re-enable only the part that helps (canvas `display: block`), keep the remainder of round-AO's structural layout fix intact. **User followup needed.** Once the user verifies in-browser that the canvas paints, the engagement badge should reset from `0/5` to `1/5` automatically on first paint (since `markViewed(1)` runs in the JS bridge after `await page.render().promise` resolves, then `sendNow` invokes `SyncProgress → SyncActivityAsync → CheckEligibilityAsync`). If it stays at `0/5`, that's the next round's symptom — JS bridge may not be reaching the C# callback. Logged for tracking if it surfaces.

- **Prior — Round-AO: fix Training/3 PDF viewer — canvas was hiding behind buttons (Bootstrap `.ratio > *` collision) + canvas-replace + scroll-to-top UX (2 sub-rounds).** User reported the local-PDF branch on `/Training/3` showed two large side-by-side rectangles (with the Prev/Next buttons stretched to fill each), no visible PDF canvas, and the badge stuck at `0 / 5 pages`. Round-AJ's previous revert had kept the `ratio ratio-16x9` wrapper UNCONDITIONAL because round-AH's HiDPI-ratio experiment regressed canvas visibility — but that revert was half-right. The real culprit had a second half: Bootstrap 5's `.ratio > * { position: absolute; inset: 0 }` rule applied to **both** `#pdf-canvas-host` AND `.btn-group` (sibling direct children of the ratio div). Combined with `.btn-group`'s `display: inline-flex`, the Prev/Next buttons split horizontally across the entire 16:9 area — covering the canvas completely. The user clicked Next, and the click handler correctly fired, but visually nothing changed because the buttons overlaid everything. **Round-AO fix (two-file).** (1) **`Components/Pages/Training/Take.razor`** — change the `#training-host` wrapper class from a constant `"ratio ratio-16x9 mb-3"` to an inline C# ternary `@(_content.Format == TrainingFormat.Pdf && !_content.FilePathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? "mb-3" : "ratio ratio-16x9 mb-3")`. Drops the 16:9 wrapper for local PDFs (so the canvas-host and btn-group render in normal flow with no `position: absolute` collision); keeps the 16:9 wrapper for iframe-based branches (external PDF, video, YouTube, slideshow) where the 16:9 padding has worked fine. WHY-comment trimmed to 9 lines per codebase rhythm. (2) **`wwwroot/js/trainingview.js`** — in `mountPdf`'s `renderPage(p)`, move `canvasRoot.innerHTML = ''` from the once-on-mount position INTO `renderPage` (just before `appendChild(canvas)`), and change `canvasRoot.scrollTop = canvasRoot.scrollHeight` to `canvasRoot.scrollTop = 0`. The round-AG shape appended new canvases on each page without removing the old ones, and the `scrollTop = scrollHeight` always pinned the user to the BOTTOM of the most recently rendered page (which on letter-sized pages = whitespace at the bottom). New UX: one canvas per page, scrolled to TOP of each new page on arrival — matches a normal PDF reader. Existing WHY-comment above `mountPdf` (round-AJ's HiDPI TODO) collapsed from 12 lines to 7 lines (status-pointer + intentional accept-trade note), so the file no longer carries stale-history drift. **Round-AO sub-rounds.** (1) Initial fix — build failed on file-lock because dev process was running (kill + rebuild succeeded); 299/299 tests pass unchanged; first-pass code-reviewer flagged the new WHY-comment at 18 lines (out of style) AND the stale round-AJ block still lived above `mountPdf` (drift). (2) Comment trim — Take.razor block to 9 lines, trainingview.js block to 7 lines (both inside the codebase's 6-12 line rhythm). **Files touched.** `Components/Pages/Training/Take.razor` (conditional ternary class + trimmed WHY-comment); `wwwroot/js/trainingview.js` (canvas-replace + scrollTop=0 + trimmed historical comment). **Untouched on this round (Tier 2 future audit, per the per-site discipline).** External-PDF / Video / YouTube / Slideshow branches — all keep the `ratio ratio-16x9` wrapper, which works correctly for iframe mounts. The fallback `@if (_trackingFailed && …)` alert + iframe block already lives OUTSIDE `#training-host`, so it doesn't interact with the new layout either. **Verification.** Build clean (0 errors / 1 pre-existing unrelated `xUnit2013` in `AssignmentServiceTests.cs:791`); full suite **299/299** PASS unchanged (no new tests this round — round-AO is markup + JS-only refactor, not service-level logic; the PDF render path still isn't reachable from the xUnit / bUnit surface, same as round-AI/AJ/AK). **Code-reviewer verdict.** "Ship." Cosmetic nit (not blocking): a small computed helper like `private bool IsLocalPdf => _content!.Format == TrainingFormat.Pdf && !_content.FilePathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase);` would tighten the call-site to `class="@(IsLocalPdf ? "mb-3" : "ratio ratio-16x9 mb-3")"`. Worth considering on a follow-up pass. **Supersession note (precise).** Round-AJ's revert was directionally correct (returning to round-AG's known-good baseline), but the canvas-visibility regression it "fixed" came from round-AH's HiDPI-ratio experiment, NOT from the unconditionally-on `ratio ratio-16x9` itself. Round-AJ's "ratio keeps rendering visible" framing was therefore an over-generalization. Round-AO pins the truth: 16:9 is fine for iframe branches; it's wrong for the PDF.js canvas+button-pair because Bootstrap's `.ratio > *` collision. Future contributors who touch this block should follow round-AO's conditional-class pattern, not round-AJ's "always-on" pattern.

- **Prior — Round-AN: structural fix — move Login + Register off Blazor-Interactive onto static-SSR + minimal-API endpoints (3 sub-rounds; round-AM's FormName-strip is SUPERSEDED).** The user reported the round-AM fix didn't unblock them: post-rebuild they hit `System.InvalidOperationException: Headers are read-only, response has already started.` at `Components/Account/Pages/Login.razor:line 63` (now line 63 because the round-AM WHY-comment shifted it from 56). The verifier re-ran and the round-AM's build was clean + 299/299 — so why did the live site still crash? **Confirmed root cause (round-AN final).** The round-AM diagnosis (`FormName` enables enhanced-form handling; the HTTP POST side begins streaming the response; the SignalR-initiated cookie write races against the now-stuck streaming response) was **directionally correct but insufficient**. The deeper issue: the cookie-write path runs against the SignalR-circuit HttpContext, which is the FIRST page-render request's context — that response is already streaming the initial HTML, regardless of `FormName`. The "headers are read-only" exception is intrinsic to any Blazor InteractiveServer handler that writes cookies; the FormName-strip just removes one of the racing dispatches, it doesn't give SignInManager a fresh HttpContext to write into. Strip FormName, run the handler anyway; the handler STILL writes against a circuit HttpContext that has streamed the initial page. **Round-AN fix (the structural move).** Two-piece fix: (a) **`Components/Account/Pages/Login.razor` + `Register.razor`** rewritten as **static-SSR** pages (no `@rendermode InteractiveServer` directive) with plain HTML `<form method="post" action="…">` blocks pointing at two new minimal-API endpoints in `Program.cs`. Login now carries `@inject IAntiforgery Antiforgery` + `[CascadingParameter] private HttpContext` so the antiforgery token can be embedded into a hidden `__RequestVerificationToken` field via `Antiforgery.GetAndStoreTokens(HttpContext).RequestToken`. Register keeps its `OnInitializedAsync` resolution of `_autoJoinOrg` / `_codeWasProvided` / `_orgsForDropdown` (these still run at static-SSR init time before the form is rendered). (b) **`Program.cs` minimal-API endpoints** `MapPost("/Account/PerformLogin", ...)` + `MapPost("/Account/PerformRegister", ...)`, both validating antiforgery via `IAntiforgery.ValidateRequestAsync(ctx)`. PerformLogin calls `SignInManager.PasswordSignInAsync` against the FRESH form-POST HttpContext (Response hasn't streamed the cookie write), redirects to `UrlSafety.IsLocalUrl(returnUrl) ? returnUrl : "/"` on success, redirects back to `/Account/Login?returnUrl=...&error={locked|invalid}` on failure. PerformRegister creates `IdentityUser` (via `UserManager.CreateAsync`) + `Person` + optional `OrganizationMembership(Role = Volunteer)` + signs in (via `SignInManager.SignInAsync`), all against the same fresh HttpContext+, with the registration-token-wins-over-dropdown resolution mirrored exactly from the round-AM HandleRegister logic. Open-redirect safety preserved through `UrlSafety.IsLocalUrl` on every redirect target — the same helper the previous Razor pages routed through client-side. Added `using Microsoft.AspNetCore.Antiforgery;` to both .razor pages so `@inject IAntiforgery Antiforgery` resolves (CS0246 fix during the apply step). **Round-AN sub-rounds.** (1) Initial endpoint add + Razor rewrite → build failed with CS0246 on `IAntiforgery` (the test pass that reported "299/299" was against the OLD binary via `--no-build`, misleading green). (2) `@using` directive added to both .razor pages + remember-checkbox fix (`form["remember"].Count > 0` for browsers sending the default `"on"`, which `bool.TryParse` would reject) + safeTarget hoist + merge the two `await using var db` scopes into one in PerformRegister + trim the 30-line WHY-comment to ~14 lines per the code-reviewer's first-pass feedback. **Files touched.** `Components/Account/Pages/Login.razor` (static-SSR rewrite + `@using`); `Components/Account/Pages/Register.razor` (same); `Program.cs` (two `MapPost` endpoints + `using` import + 14-line Round-AN WHY-comment). **Untouched on this round (Tier 2 future audit, per the per-site discipline).** `ForgotPassword.razor` / `ResetPassword.razor` / `ChangePassword.razor` / `Manage.razor` — none of these handlers write cookies, so they don't trip the round-AN root cause. They keep their round-AF `<EditForm FormName>` + `@rendermode InteractiveServer` shape (the "round-AF circuit-drop" failure mode is still possible but orthogonal to round-AN's "cookie write in Blazor-Interactive" failure mode). **Verification.** Round-AN final: build clean (0 errors / 1 pre-existing `xUnit2013` in `AssignmentServiceTests.cs:791` from an unrelated round); full suite **299/299** PASS unchanged (no new tests this round; round-AN ships zero new tests because `tests/ServantSync.Tests/SqliteTestBase.cs` is service-level only — exercising the new endpoints would require a `Microsoft.AspNetCore.Mvc.Testing` + `WebApplicationFactory<Program>` addition; flagged as Tier-1 test-infra followup below). **Code-reviewer verdict.** "Ship it." Test-gap re-flagged. **Supersession note.** The round-AM entry below is amended: round-AM's FormName-strip is preserved in the git history but the live Razor pages no longer carry `<EditForm FormName=…>` — they carry plain HTML forms. Future contributors who land on the round-AM-supersession claim should follow the round-AN structural pattern for any Account page whose handler writes cookies. **Round-AM fix.** Two-file `FormName` strip on the Account pages whose `OnValidSubmit` handler **writes cookies server-side**. `Components/Account/Pages/Login.razor` — `<EditForm Model="_model" OnValidSubmit="HandleLogin" FormName="login">` → `<EditForm Model="_model" OnValidSubmit="HandleLogin">`. `Components/Account/Pages/Register.razor` — `<EditForm Model="_model" OnValidSubmit="HandleRegister" FormName="register">` → `<EditForm Model="_model" OnValidSubmit="HandleRegister">`. Both stripped EditForms now ride the pure SignalR interactive path, so `SignInManager.PasswordSignInAsync` / `SignInAsync` cookies write cleanly without racing a started HTTP response. The `[SupplyParameterFromForm] private T _model { get; set; } = new();` attribute stays on both pages per the round-AF recipe; without it the OnValidSubmit handler would receive empty `_model.Email` and `_model.Password` and `_model.OrganizationId`. **WHY-comment additions.** Both files gained a 6-line Round-AM rationale Razor-comment ABOVE the EditForm so future contributors don't re-attach `FormName`. Each comment names the verbatim error string (`"Headers are read-only, response has already started."`) for diagnostic anchoring. **Round-AM scope DISPOSITION — narrow, per-round-AF per-site discipline.** Only Login + Register got the strip this round, because both write cookies in OnValidSubmit. ForgotPassword / ResetPassword / ChangePassword / Manage kept `FormName`. Reasoning: (a) `Manage.razor` has no EditForm at all (info display); (b) ForgotPassword / ResetPassword / ChangePassword handlers call `UserManager.FindByEmailAsync` / `GeneratePasswordResetTokenAsync` / `ResetPasswordAsync` / `ChangePasswordAsync` — no `HttpContext.Response.Cookies.Append` surface, so they don't trigger this exact race; (c) they MAY surface a different Blazor failure mode (the circuit-drop variant under round-AE / AF, or an enhanced-form-POST redirect) — that audit is logged as a Tier-2 followup, not rolled into round-AM. Round-AF's "Do NOT blanket-strip `FormName`" policy is honored. **Files touched.** `Components/Account/Pages/Login.razor` (FormName strip + 6-line WHY-comment); `Components/Account/Pages/Register.razor` (same). **Verification.** Build clean (single pre-existing `xUnit2013` in `AssignmentServiceTests.cs:791` from an unrelated round); full suite **299/299** PASS unchanged (no new tests this round; the fix is markup-only and `tests/ServantSync.Tests/PageAccessTests.cs` walks service-level gates, not DOM-level FormName assertions). **Code-reviewer verdict.** "minimal, surgical, honest, diagnostic-anchored. Ship." **Verification of the fix.** After rebuild, the user can sign in via the seeded credentials (e.g., `admin@demo.local` / `Passw0rd!`); subsequent registration via `/Account/Register` stays interactive without the cookie-write race.

- **Prior — Round-AL: REVERT round-AK.0 cache-bust URLs that crashed the host (1 round).** The user reported a server-side process crash on `/Training/3`: `Microsoft.JSInterop.JSException` followed by `The program '[20512] ServantSync.exe' has exited with code 4294967295 (0xffffffff).` Round-AK.0 had two cache-bust changes — `Components/Pages/Training/Take.razor` `MountViewerAsync` got `?v={DateTime.UtcNow.Ticks}` on the dynamic-import URL `"/js/trainingview.js?v=..."` + `wwwroot/js/trainingview.js` IIFE-top `PdfWorkerSrc` + `PdfMainSrc` constants got `?v=${Date.now()}` — and they were the only JS-side delta between round-AG (known-good — user observed "PDF looks fine to me" before round-AH) and the crash. **Mechanism unconfirmed.** The cache-bust created a fresh module instance on every `MountViewerAsync` invocation; a subsequent re-mount could orphan an in-flight `page.render()` promise against a now-detached canvas, surfacing as `JSException`, dropping the SignalR circuit, crashing the host. The thinker's deeper analysis flagged a parallel plausible root cause: per-tick `?v=$` regeneration may have caused `setInterval` timers (`YouTube` / `Best-EffortDwell` paths) to leak, storming `dotnetRef.invokeMethodAsync('SyncProgress', ...)` past SignalR limits. Either way the cache-bust is removed. **Round-AL change.** Both cache-bust URLs reverted to round-AG's bare URLs (`"/js/trainingview.js"`, `/lib/pdfjs/pdf.worker.mjs`, `/lib/pdfjs/pdf.mjs`). WHY-comments trimmed to 8 and 6 lines respectively, framing the issue as `Mechanism unconfirmed; round-AK's per-tick URL was the only JS-side delta; reverting returns to known-good; see STATUS.md round-AL` and pointing to STATUS.md for the full triage — explicit about the unconfirmed root cause so future contributors don't anchor on a hypothesis. **Files touched.** `Components/Pages/Training/Take.razor` (URL revert + comment trim); `wwwroot/js/trainingview.js` (URL revert + comment trim). **Round-AL scope DISPOSITION.** Round-AI's `PdfPageCountHealer` (verified working — populated `TotalPageCount=5`) + round-AJ's HiDPI/ratio/CSS revert + round-AK.1 / AK.2 test-helper fixes all stay. **Verification.** Build clean (single pre-existing `xUnit2013` in `AssignmentServiceTests.cs:791` from a fully unrelated round); full suite **299/299** PASS unchanged (no new tests this round; revert is a no-shape-change in tests). **Code-reviewer verdict.** "minimal, correct, honest. Ship." **Thinker verdict.** "Revert is the correct call. Session-stable URL token is a smarter middle-ground if cache-bust becomes a real need again — worth recording as a Tier-2 future-work item but NOT worth shipping now." **Documented future work (Tier 2).** If the dev-runtime 304 stale-asset race surfaces again during smoke-testing (per the existing `Stop-Process` + `dotnet publish -c Release` workaround in STATUS.md Known-quirks), the smarter middle-ground design is: assign a sessionStorage-keyed GUID on first JS module import, reuse on subsequent mounts within the same browser session. Same URL per session = browser module-cache hit = single module instance = no orphan-canvas race; different URL across sessions = revalidation on cold start. Logged for tracking but deferred until the 304 race is actually a user-visible regression in front of us, so we design against ground truth, not speculation.
- **Prior — Round-AK: defensive cache-bust + 2 test-helper bug fixes (3 sub-rounds; SUPERSEDED by round-AL — the round-AK.0 cache-bust is the regression that crashed the host).** Original round-AK entry preserved for audit trail. **(1) Round-AK.0 — root fix.** Two-file cache-bust for the dev-runtime 304 stale-asset race documented in the Known-quirks list. **`Components/Pages/Training/Take.razor` `MountViewerAsync` got `?v={DateTime.UtcNow.Ticks}` on the dynamic-import URL `"/js/trainingview.js?v=..."` so every page mount revalidates against the server. `wwwroot/js/trainingview.js` IIFE-top `PdfWorkerSrc` + `PdfMainSrc` constants got `?v=${Date.now()}` to bust the `pdf.worker.mjs` + `pdf.mjs` worker caches.** StaticFiles middleware doesn't strip query strings (server-side file lookup is path-only; client-side cache key per HTTP spec is full URL), so the URL change forces a fresh fetch — round-AJ's prior "viewer back but blank / nav dead / 0/5 pages" symptom was the strongest remaining hypothesis, and this round-AK.0 fix removes the cache as a confounder without any logic change. **(2) Round-AK.1 — `NewTempDir` test helper bug.** Initial verifiers of round-AI's heal tests reported 295/299 — 4 of the 7 `PdfPageCountHealerTests` failed with `DirectoryNotFoundException` on `C:\Users\robsa\AppData\Local\Temp\PdfHeal_<guid>\guide.pdf`. Root cause: `NewTempDir()` returned a path but did NOT create the directory on disk; tests calling `WritePdf` or `File.WriteAllBytes` BEFORE constructing the `StubUploadPathProvider` (whose ctor calls `Directory.CreateDirectory` defensively) crashed. Fix: `NewTempDir()` now calls `Directory.CreateDirectory(dir)` before returning. **(3) Round-AK.2 — `TryHeal_AlreadyHasTotalPageCount_IsNoOp` stale-snapshot bug.** Verifier post-AK.1 reported 298/299 — the same test family dropped to one failure: `Assert.Equal(content.FilePathOrUrl, reloaded.FilePathOrUrl)` at line 80, expecting `"/uploads/training/test.pdf"` (the value `TestData.PdfContent` seeded into the in-memory `content` snapshot), actual `"/uploads/training/guide.pdf"` (the post-override DB value). The intent was "no-op heal didn't rewrite the path" — the honest assertion is against the literal the test itself wrote, which is what the fix does, plus a multi-line WHY-comment. **Round-AK.0 is the regression.** The cache-bust was the user's reported runtime crash — `Microsoft.JSInterop.JSException` followed by `ServantSync.exe exit code 4294967295 (0xFFFFFFFF)`. Round-AK.1 + AK.2 are NOT regressions and remain in place.
- **Prior — Round-AJ: revert round-AH's HiDPI/ratio/CSS regression (1 round).** Round-AH shipped three changes to address a "blurry PDF on /Training/3" user report: (a) `wwwroot/js/trainingview.js` `mountPdf` HiDPI handling (dpr cap at 2, cssViewport/renderViewport split, `ctx.scale(dpr,dpr)`, inline CSS dims with `Math.floor`); (b) `Components/Pages/Training/Take.razor` conditional `ratio ratio-16x9 mb-3` removal for the local-PDF branch; (c) `wwwroot/app.css` appended `#pdf-canvas-host canvas { display: block; max-width: 100%; height: auto; margin-left: auto; margin-right: auto; }`. After round-AH shipped, the user reported the PDF.js canvas was entirely missing (NOT blurry, MISSING) plus the prev/next nav went dead plus the page badge still showed 0/5. The user's earlier "looks fine to me" round-AG baseline was the reference. **Round-AJ revert.** All three round-AH changes reverted to round-AG's known-good baseline: `mountPdf` back to the simple `scale:1.4` rendering loop; `#training-host` wrapper class set to the constant `"ratio ratio-16x9 mb-3"` always (no conditional); `#pdf-canvas-host canvas` CSS rule removed. **KEPT.** Round-AH-2's `target.parentElement` click-delegation fix (so prev/next buttons actually fire) survives the revert because it was scope-isolated to a single JavaScript line. **Files touched.** `wwwroot/js/trainingview.js`, `Components/Pages/Training/Take.razor`, `wwwroot/app.css`. **Known-quirks entry.** The new round-AK.0 entry above supersedes any prior cache-bust fragility commentary in STATUS.md.
- **Prior — Round-AI: PDF page-count self-heal (1 round).** "PDF page count unknown — admin re-upload required" appeared for a valid openable PDF that already existed in `/uploads/training/`. Diagnosed: `TrainingContent.TotalPageCount` was null because the original upload predated the `PdfPageCounter` wiring OR the upload-time extension/ContentType check silently missed the `.pdf` case. The volunteer cannot fix the data — pure UX/profile mismatch. **New service.** `Services/PdfPageCountHealer.cs` exposing `IPdfPageCountHealer.TryHealAsync(int trainingContentId)`. Behavior: AsNoTracking read of the row → resolve the on-disk path via `IUploadPathProvider.GetTrainingUploadPath` (two-layer `Path.GetFileName` traversal defense) → call `PdfPageCounter.Count(path)` (PdfPig) → idempotently mutate ONLY `TotalPageCount`, never bump `Version` (TrainingActivity rows are version-keyed). Swallows PdfPig exceptions and returns `null` on missing / encrypted / malformed files; the existing "page count unknown" gate stays intact for those. **`Program.cs` DI.** `AddScoped<IPdfPageCountHealer, PdfPageCountHealer>()`. **`Components/Pages/Training/Take.razor`.** Added `@inject IPdfPageCountHealer PdfHealer`, new private `TryHealPdfPageCountAsync` method that calls the healer and re-reads `_content` so the next `CheckEligibilityAsync` picks up the corrected count. Gated inside the existing `if (_inOwningOrg)` block so only members of the content's organization can trigger it. Public API surface unchanged. **New tests.** `tests/ServantSync.Tests/PdfPageCountHealerTests.cs` — 7 tests covering happy-path populates / already-populated no-op / missing file no-op / Version invariant under heal / malformed-or-encrypted bytes trigger the catch branch / `https://` URL triggers the http-skip branch / non-PDf format triggers the format gate. Includes a `StubUploadPathProvider` shim (mirrors the real one's path-traversal defense) and a `WritePdf` helper using PdfPig's `PdfDocumentBuilder`. **Test execution.** Round-AI initially shipped 295/299 (see round-AK.1 below for the `NewTempDir` fix). **Empirical verification on the user's actual file.** The user reported "0/5 pages" badge — round-AI's heal worked correctly: `TotalPageCount = 5` populated; the page renders.
- **Prior — Round-AF: Training/Edit FormName restoration + auth-gate restructure (1 round).** REPLACES the round-AE diagnosis (`FormName drop`) — that hypothesis was wrong per .NET 9 docs. Microsoft docs say: (a) conditional EditForm mount on an InteractiveServer page does NOT drop the SignalR circuit, and (b) FormName is RECOMMENDED on EditForms in InteractiveServer mode (\"Always provide a FormName to EditForm when using InteractiveServer mode. The FormName helps Blazor maintain state consistency during re-renders.\"). Round-AE broke the page by removing FormName while leaving the conditional EditForm mount in place — the user-reported \"Upload file\" pill button on /Training/3/Edit remained inert AFTER round-AE exactly because the form-aware wiring was destabilised by form-name removal. **Four-part fix.** (1) **Restore FormName.** EditForm now carries `FormName="training-content-edit"`. The round-AE-1 inline Razor comment inside the EditForm is replaced with a round-AF comment block that cross-references this STATUS entry. (2) **Always-render EditForm + internal fieldset gate.** The auth-gate moved OUT of `@if (_canSubmit) { <EditForm>… </EditForm> }` into an internal `<fieldset disabled="@(!_authChecked || !_canSubmit)">` wrapping all the form contents. The EditForm is part of the initial render once auth-check completes (no mid-circuit mount/dismount). Per HTML spec, `<fieldset disabled>` gates contained form controls (input, select, textarea, button type=submit, including the `<button type="submit">Save`) but does NOT disable plain `<button type="button">` — so the pill-nav `@onclick` handlers (`_source = "upload"` / `_source = "url"`) still fire while the form fields stay disabled until auth+canSubmit clear. (3) **`_authChecked` sentinel.** New `private bool _authChecked;` field set true at EVERY OnInitializedAsync terminal — 5 early-return paths (no userId, no admin orgs, content not found, not admin of owning org, NEW-mode auto-fill returning early) + the successful EDIT-mode end after `_model = new ContentModel {...}`. Initial value false; the top-level `@if (!_authChecked) { <p class="text-muted">Loading permissions…</p> }` sentinel above the form renders ONLY until auth-check completes; the fieldset flips from disabled to (enabled | stays-disabled) in a single re-render. (4) **Cancel href pre-existing fix.** `<a class="btn btn-outline-secondary ms-2" href="Training">Cancel</a>` was a relative href that would have resolved to `/Training/3/Training` from `/Training/3/Edit`; corrected to `<a href="/Training/Manage">Cancel</a>` to match Save()'s `Nav.NavigateTo("Training/Manage")` redirect target. **Files touched.** `Components/Pages/Training/Edit.razor` only. **Known-quirks entry** (the canonical `EditForm FormName mounted inside an @if ... drops the circuit` paragraph) is now wrong per official docs and flagged as needing review in Tier 2 below; do NOT rely on it for new interactive EditForms until it's corrected. **Round-AE demoted.** The "Latest — Round-AE" entry below is now labeled "(SUPERSEDED by round-AF)". **Reviewer-flagged followups (NOT blocking, recorded for Tier 2).** (a) When `_canSubmit = false && _authChecked = true`, the user sees a deny `<StatusMessage>` on top + a greyed-out-but-visible EditForm below. Defensible but a future polish pass could collapse the gate structure to `@if (!_authChecked) { Loading } else if (!_canSubmit) { deny } else { EditForm }` to fully suppress the form. (b) `_authChecked` testing via bUnit is gated on a `(string, IOrgAuthService)` DI seam + per-component InteractiveServer form rendering that isn't currently exercised by the bUnit test fixture (none of `DateRangeChips.razor` / `AssignmentCalendar.razor` / etc. require InteractiveServer form lifecycle). Worth it once a follow-on round adds the seam. **Verification.** Build clean (single pre-existing xUnit2013 in `AssignmentServiceTests.cs:791` from an unrelated round, no new warnings); PdfPageCounterTests 7/7 regression still green; MailKit 4/4 + EmailBrand 4/4 regression still green; full suite **292 / 292** pass unchanged (no new tests this round; the broken-button bug is a SignalR / form-aware-wiring failure on the user's actual `/Training/3/Edit` URL and isn't reachable from the bUnit / xUnit surface).
- **Prior — Round-AE: Training/Edit FormName drop (1 round; SUPERSEDED by round-AF).** Original round-AE entry preserved for audit trail.
  reported "file upload no longer works on training page" after
  round-AC + round-AD. Diagnosis matches the canonical
  `EditForm + FormName + conditional-mount + InteractiveServer =
  SignalR-drop` shape documented in STATUS.md's Known-quirks
  list. `Components/Pages/Training/Edit.razor` had its
  `<EditForm Model="_model" OnValidSubmit="Save" FormName="train">`
  mounted inside `@if (_canSubmit) { ... }`. When the org-admin
  gate flips `_canSubmit` true after `OnInitializedAsync`
  resolves the membership lookup, Blazor treats the conditional
  EditForm mount as a streaming-SSR-capable form arriving
  mid-circuit and drops the SignalR channel — every subsequent
  `OnChange` and `@onclick` becomes inert. The browser-native
  file picker opens (no circuit needed for the OS dialog),
  but `HandleUpload` never fires once a file is selected so
  "Uploading…" never progresses. **Fix (round-AE-1).** Drop
  `FormName="train"` per the documented recipe; keep `Model` +
  `OnValidSubmit` + the defensive `[SupplyParameterFromForm]
  private ContentModel _model { get; set; } = new();` (no-op in
  pure interactive mode, harmless). Inline Razor comment on
  the `<EditForm>` line cross-references the Known-quirks entry
  so a future contributor doesn't re-add `FormName` believing
  the form is otherwise broken. **Round-AE-2 audit.** `Training/Edit.razor`
  joins the verified-clean list in the Known-quirks
  render-mode-mount entry alongside `Components/Pages/Organizations/Detail.razor`
  Members + Arenas (round 7), `Components/Pages/Games/Detail.razor`
  score form, `Components/Pages/Organizations/OrgTrainingEditor.razor`
  AddRequirement, `Components/Pages/ServiceSlots/SlotTrainingEditor.razor`
  Add, `Components/Pages/ServiceSlots/ScheduleSeries.razor`
  Generate, `Components/Pages/Teams/Players.razor` Save. The
  `Components/Account/Pages/{Login,Register,ForgotPassword,ResetPassword,Manage,ChangePassword}.razor`
  page set does NOT enter this list because it uses
  `@layout AccountLayout` (the default Identity scaffold
  layout, NOT a `LayoutComponentBase`-derived custom layout) so
  the conditional-mount-vs-layout-vs-circuit-drop shape doesn't
  apply. **Build / tests.** Verifier reported clean build
  (the pre-existing `xUnit2013` warning in
  `AssignmentServiceTests.cs` line 791 from an unrelated round;
  no new warnings) + **292 / 292** pass unchanged (no new tests
  this round, the bug is signalR-render-mode + form-mount timing
  and isn't reachable from the bUnit / xUnit surface).
- **Prior — Round-AD: cascade-refactor to fix RenderFragment-cannot-cross-render-mode-boundary (1 round).** User hit:

  ```
  System.InvalidOperationException: Cannot pass the parameter 'Body'
  to component 'MainLayout' with rendermode 'InteractiveServerRenderMode'.
  This is because the parameter is of the delegate type
  'Microsoft.AspNetCore.Components.RenderFragment', which is arbitrary
  code and cannot be serialized.
  ```

  **Root cause.** Round-AB-3's fix (collapse `@@rendermode InteractiveServer` → `@rendermode InteractiveServer`) promoted the directive from per-page to layout-level on `Components/Layout/MainLayout.razor` line 1. That promotion made every page interactive via cascade but introduced this runtime exception because the framework tries to serialize the layout's `@Body` parameter for streaming SSR, and a `RenderFragment` (delegate capturing the parent render context) is not serializable. The same constraint applies to any layout deriving from `LayoutComponentBase` (also `AccountLayout` — but that one resolves to the default Identity scaffold layout class which has no `@rendermode`, so it's safe). **Fix.** The directive cannot live on a layout OR on `<AuthorizeRouteView>` (whose templated `<NotAuthorized>` child is also a `RenderFragment<T>` that can't cross a render-mode boundary). The only safe placement is the page itself — every interactive page MUST carry its own `@rendermode InteractiveServer` directive at the top of the .razor file. **Files touched.**
  - `Components/Layout/MainLayout.razor` — REMOVED `@rendermode InteractiveServer` from line 1, replaced with a multi-paragraph Razor comment block that quotes the verbatim .NET 9 exception, narrates the per-page-only rule, and backreferences `Routes.razor`. `@inherits LayoutComponentBase` is intact.
  - `Components/Pages/Home.razor`, `Components/Pages/Leagues/Standings.razor` — ADDED `@rendermode InteractiveServer` (after `@attribute [Authorize]`).
  - `Components/Account/Pages/{Login, Register, ForgotPassword, ResetPassword, Manage, ChangePassword}.razor` — ADDED `@rendermode InteractiveServer` (6 files; insertion after `@layout AccountLayout` or `@attribute [Authorize]`).
  - `Components/Routes.razor` — REWROTE the long `<AuthorizeRouteView>` comment block. The prior comment claimed "the directive therefore lives on `<Components/Layout/MainLayout.razor>`" — the new comment quotes BOTH exceptions (the templated-child exception AND the layout-Body exception), establishes the per-page-only rule, names the `Weather.razor`/`Error.razor` exceptions (those are intentionally static), and notes the Account-self-service pages flow through `<AuthorizeRouteView>` so they too must opt in explicitly.

  **Build / tests.** Round-AC-1 verifier reported clean build (single pre-existing `xUnit2013` warning in `AssignmentServiceTests.cs` line 791 from an unrelated round, not introduced here) + **292 / 292** pass (was 285 baseline + 7 new PdfPageCounterTests; this round is UI-only and adds no new tests).

  **Maintenance note.** Adding `@rendermode InteractiveServer` to every routed page is a maintenance burden if a future contributor adds a page without the directive — they'd discover the failure on first navigation, not at compile time. **Suggested followup:** a Roslyn analyzer that emits a warning on any new `@page` .razor file under `Components/Pages/` or `Components/Account/Pages/` that lacks either `@rendermode InteractiveServer` or an explicit "static SSR is fine here" marker (a doc comment `// @rendermode: static` or similar). Cross-link from the analyzer's message to the new Routes.razor commentary block so the policy is self-documenting. Logged as **Tier 2** below.

- **Prior — PDF page-count bug fix (1 round).** User reported
  that `C:\Users\robsa\OneDrive\Documents\test.pdf` (382 KB,
  `%PDF-1.7`) "did not upload or parse correctly into pages".
  Root cause: `Services/PdfPageCounter.cs` was reading the
  first 2 MB of the uploaded file and counting occurrences of
  the ASCII token `/Type /Page` via regex. Modern PDFs
  (PDF 1.5+) routinely store pages inside compressed object
  streams where the literal text is never present in raw
  bytes → regex returns 0 → `Math.Max(1, 0) = 1` silently
  broke the Take-page eligibility rule into a frozen
  "Viewed N of 1 pages" gate that the volunteer could not
  escape. Fix: replace the regex scanner with a real PDF
  parser. **New dependency.** Added
  `<PackageReference Include="UglyToad.PdfPig"
  Version="1.7.0-custom-5" />` to both `ServantSync.csproj`
  and `tests/ServantSync.Tests/ServantSync.Tests.csproj`
  (NuGet `--prerelease` corporate feed; restore clean).
  Reflection probe confirmed the forked assembly merged its
  Writer sub-namespace into `UglyToad.PdfPig` and placed
  `PageSize` under `UglyToad.PdfPig.Content` (i.e.
  `using UglyToad.PdfPig.Content;` exposes `PageSize.A4`;
  standard PdfPig source on GitHub uses
  `UglyToad.PdfPig.PageSize`). **`PdfPageCounter.cs`
  rewrite** (the public signature is unchanged). New body:
  ```csharp
  public static int Count(string filePath)
  {
      if (string.IsNullOrWhiteSpace(filePath)) return 0;
      if (!File.Exists(filePath)) return 0;
      using var doc = PdfDocument.Open(filePath);
      return doc.NumberOfPages;
  }
  ```
  XML doc rewritten to narrate the prior bug class and the
  new behaviour. Contract reaffirmed: throws on
  invalid/encrypted PDFs (`PdfDocumentEncryptedException`
  etc.); the caller in
  `Components/Pages/Training/Edit.razor → HandleUpload`
  already wraps Count in try/catch and treats the
  exception as `_detectedPageCount = null` → existing
  "PDF page count unknown — admin re-upload required"
  eligibility gate. **New tests
  file** `tests/ServantSync.Tests/PdfPageCounterTests.cs`
  — 7 tests: empty-path / missing-file / [Theory 1/2/7]
  page-counts via `PdfDocumentBuilder` / malformed-bytes
  throw contract / defence-in-depth "matches our own
  `NumberOfPages`" regression. Imports explicit
  (`using ServantSync.Services;`, `using Xunit;`,
  `using UglyToad.PdfPig.Content;`, `using UglyToad.PdfPig
  .Writer;`) — the fork's namespace layout prevents the
  top-level `using UglyToad.PdfPig;` shortcut. Build
  clean (single pre-existing `xUnit2013` warning in
  `AssignmentServiceTests.cs` line 791 from an unrelated
  round — not introduced here); **292 / 292** tests pass
  (was 285 baseline + 7 new PdfPageCounterTests).
  **Empirical verification on the user's actual file:**
  one-off console harness at
  `C:\Users\robsa\AppData\Local\Temp\pdfcheck`
  (PdfPig 1.7.0-custom-5 + net9.0 + `PdfDocument.Open`)
  printed `NUMBER_OF_PAGES: 5`, `PDF_VERSION: 1.7`, page 1
  sample text `"Presentation subtitle"`. The volunteer will
  now see "Viewed N of 5 pages" instead of "Viewed N of 1
  pages" and can complete the training once all 5 pages are
  rendered. **Supply-chain audit note.** The
  `1.7.0-custom-5` prerelease is sourced from the
  configured NuGet feed only (no nuget.org canonical stable
  for `UglyToad.PdfPig` resolves in this environment);
  flag the version for a supply-chain pass before this
  ships to a production deployment that wants
  reproducibility.
- **Prior — Volunteer UX fixes (1 round; round-AB-2 patch).** Two
  interrelated UX issues reported during a hands-on session, with
  a NavigationException follow-up fix that became its own sub-round.
  **(a) `Register.razor` no longer offers org-selection when a code
  is present.** Added a `_codeWasProvided` boolean set when EITHER
  `?token=…` or `?orgId=…` appears in the URL (regardless of whether
  it resolved). The form rendering now branches three ways: code
  resolved → "You'll be joining <Org>" banner + form; code provided
  but unresolved → "This invitation code didn't match any
  organization" warning banner + form (no dropdown); no code at all
  → form + the existing dropdown fallback for manual self-placement.
  Backend `HandleRegister` still prioritizes `_autoJoinOrg` over
  `_model.OrganizationId` so a hostile POST body can't sneak into a
  different org via the picker. **(b) `Home.razor` redirects
  Volunteers to `/MySchedule`** with the canonical
  `OnAfterRender(firstRender: true)` pattern (NOT a direct
  `OnInitializedAsync` call — see the NavigationException note
  below). The same `IsAnyOrgManagerAsync` gate that `NavMenu.razor`
  already uses for its manage-only links drives both pages, so a
  Volunteer consistently has: hidden Organizations nav (existing)
  + `/Organizations/{id}` URL still gets them there under the
  membership-scope rules (existing) + Home redirects to MySchedule
  (new). The org-wide KPI dashboard + "My ministries" panel remains
  intact for Admin / Coordinator. **(c) round-AB-2 NavigationException
  fix.** The first cut of (b) called `Nav.NavigateTo("/MySchedule",
  forceLoad: false)` directly inside `OnInitializedAsync`, which
  threw `Microsoft.AspNetCore.Components.NavigationException` at
  runtime. The framework uses this exception internally to abort a
  render cycle for a soft nav, but in the prerender→interactive
  blend used here it surfaced as an unhandled exception to the
  user. Fix: introduce a `_shouldRedirectToSchedule` boolean set in
  `OnInitializedAsync` + an override of `OnAfterRender(bool firstRender)`
  that performs `Nav.NavigateTo("/MySchedule")` on the first
  render. `OnAfterRender` runs AFTER the initial render cycle, so
  the navigation is processed cleanly without throwing. The flag
  is cleared on the first-render nav so a subsequent re-render
  doesn't re-fire the navigation loop. **(d) round-AB-3 escape-typo
  fix.** Line 1 of `Components/Layout/MainLayout.razor` was
  `@@rendermode InteractiveServer` — doubled `@@` is Razor's escape
  for a literal `@`, so the framework was rendering the text
  "@rendermode InteractiveServer" at the top of every page routed
  through the default layout. Fixed by collapsing to single `@`. The
  layout reverts to its intended `InteractiveServer` cascade; per-
  page `@rendermode` directives (33 pages have them) are now
  redundant but harmless — preserved as defense-in-depth in case
  the MainLayout cascade is ever removed. Build clean, **285/285
  pass** (no new tests; round is template/UX-only).
- **Prior — Email branding + apple-touch-icon PNG (1 round).** Apple-touch-icon + every outbound email now carries the ServantSync
  brand mark. (a) **Apple-touch-icon PNG.** New
  `wwwroot/img/apple-touch-icon.png` at exactly 180×180 RGBA with
  transparent alpha, rasterized from `servantsync-mark.svg` via
  headless Chrome `--window-size=180,180 --default-background-color=00000000`.
  Verified downstream: PNG signature `89504e470d0a1a0a`, bit-depth=8,
  color-type=6 (RGBA), dimensions 180×180. iOS Safari ignores
  SVG apple-touch-icons (only PNG/JPEG), so the rasterization matters
  — without it, iOS home-screen pinning falls back to the default
  Safari icon. Re-rasterization commands for Chrome / Inkscape /
  ImageMagick+librsvg / cairosvg are listed in `wwwroot/img/README.md`
  for future editorial passes; the `_render-wrapper.html` helper
  used during the Chrome rasterization is NOT a runtime asset and
  has been deleted. (b) **App.razor chrome wiring.** `<link
  rel="apple-touch-icon" sizes="180x180" href="@Assets["img/apple-touch-icon.png"]" />`
  re-added now that the PNG exists; SVG `<link rel="icon">` +
  `#1f1f2e` theme-color stay. (c) **Email brand pipeline.** New
  `Services/EmailBrandAssets.cs` — `IEmailBrandAssets` interface +
  `EmailBrandAssets` impl that reads brand bytes via
  `IFileProvider` at construction (resolver-agnostic: filesystem,
  embedded, single-file publish), caches the bytes for the
  singleton's lifetime, and degrades to `LogoBytes = null` if the
  file is missing (logged warning, no throw). The production
  `MailKitEmailSender` was rewritten: extracts a pure
  `BuildMessage(opts, brand, to, subject, innerHtml)` **`public static`**
  helper (no `InternalsVisibleTo` plumbing needed), wraps inner HTML
  in an Outlook-friendly nested-`<table role="presentation">` layout
  (Outlook renders via Word, not a browser, so div-based layouts
  break), attaches the brand image as a
  `BodyBuilder.LinkedResources` entry with `ContentDisposition =
  "inline"` (string ctor per RFC 2183, sidesteps a MimeKit enum-
  namespace-resolution edge case discovered during dev) and a
  Content-Id header set raw via `image.Headers["Content-Id"] =
  brand.ContentId;` (RFC 2392 cid format). Plain-text alt body
  produced via a sibling `WrapTextBody` helper. Graceful degradation:
  when `LogoBytes == null`, the LinkedResources.Add is skipped AND
  the html brandLine switches from `<img src="cid:...">` to a
  textual wordmark, so the recipient always sees *something* that
  tags the message. All 4 IEmailSender methods (confirmation,
  password reset link, password reset code, 2FA) sit on the new
  branded path. (d) **`Program.cs`.** DI registers
  `IEmailBrandAssets` as a singleton via factory delegate
  `(IOptions<EmailOptions>, IFileProvider, ILogger<EmailBrandAssets>)`.
  (e) **`EmailOptions.cs`.** Added `BrandImagePath` (default
  `img/servantsync-mark.png`) + `BrandImageContentId` (default
  `servantsync-mark`). (f) **Tests.** New
  `tests/ServantSync.Tests/EmailBrandAssetsTests.cs` — 4 tests via
  the test-seam ctor (defaults, custom cid override, empty bytes
  tolerance). New
  `tests/ServantSync.Tests/MailKitEmailSenderTests.cs` — 8 tests
  for the MIME builder, all asserting on MimeKit's stable top-level
  `MimeMessage` surface (`Subject`, `From`, `To`, `HtmlBody`,
  `TextBody`, recurrence over `Attachments` is replaced by a
  body-tree `FindMimePartWithMediaType` walker that works across
  MimeKit 3.x/4.x). Subject HTML-encoding trust boundary is
  explicitly pinned. Build clean, **285/285 pass** (273 baseline +
  12 new). Several MimeKit-version-specific API mismatches surfaced
  during round-AA development (ContentDisposition namespace,
  LinkedResources.Add positional arg, InternetAddress.Address
  visibility, ContentType.Subtype) and were resolved per-version
  with documented workarounds — see the inline comments for each.
- **Prior — Brand wiring (2 rounds: SVG generation + chrome\n  integration).** Two interlocking deliverables ship together. (a)\n  **Logo generation.** Two SVG files in `wwwroot/img/`: the\n  square 512×512 mark (`servantsync-mark.svg`, dual C-arcs in\n  cool indigo→violet + warm amber→rose with a soft heart-shaped\n  void in the negative space, four end-dots and a central bridge\n  dot — reads as "two parties in sync" without any overtly\n  religious symbol, so the mark works equally well for a Methodist\n  church coordinator, a Christian youth-sports league, or a\n  secular neighborhood-watch roster), plus the 720×280 wordmark\n  variant (`servantsync-logo.svg`) pairing a 0.5× shrink of the\n  mark with a system-ui "ServantSync" wordmark (weight 800,\n  negative letter-spacing) and a spaced-caps "VOLUNTEERS · IN\n  SYNC" tagline. Both files verified rendering correctly via\n  headless Chrome at native + 32×32 favicon scale before\n  shipping. (b) **Chrome integration.** `Components/App.razor`\n  wires the mark as the SVG favicon + sets a `theme-color` meta\n  to `#1f1f2e` (the navbar-as-rendered tint, NOT the brand\n  indigo, so Android Chrome's address bar doesn't seam against\n  the dark navbar on first paint); the dead `<link rel="alternate\n  icon" type="image/png">` (file didn't exist) + the\n  iOS-unsupported `<link rel="apple-touch-icon">SVG` are both\n  removed with in-line comments explaining the drops (180×180\n  PNG export is tracked as a follow-up in **Brand-related\n  followups** below). `Components/Layout/NavMenu.razor`\n  replaced the plain `<a class="navbar-brand">` text with a\n  `d-flex align-items-center gap-2` anchor carrying the SVG mark\n  at 36×36 + a `<span class="navbar-brand-wordmark">ServantSync</span>`\n  for screen readers + no-CSS fallback. `NavMenu.razor.css`\n  gained `.navbar-brand-mark` (flex-none halo with subtle\n  `rgba(255,255,255,0.06)` background, plus a hover lift to\n  `0.12`) and `.navbar-brand-wordmark` (white, weight 700,\n  `-0.4px` letter-spacing, `1.05rem`), with a matching\n  `.navbar-brand:hover .navbar-brand-wordmark` color shift to\n  `#e6e6f0` so hovering EITHER half of the brand link lights\n  BOTH halves. (c) **Docs.** `BRANDING.md` is the repo-root\n  brand-book: palette table (primary indigo `#3730a3`,\n  warm-edge secondary `#d97706`, amber→rose `#fb7185`,\n  white-95% `#f5f5fa`), gradient & clear-space tokens, typography\n  scale, dark-mode swap notes (cool-side arcs flip from indigo→\n  slate so a dark chrome doesn't fight the dark mode), don't-do-\n  this list (no religious cross, no replacing the C-arcs with\n  literal "S" ligatures, no flipping cool→red because it\n  reads liturgical). `wwwroot/img/README.md` documents the\n  asset variants + when to use which + the regeneration\n  command. No test count change (chrome wiring is markup/CSS,\n  not domain logic) — build 0 errors / 273/273 pass.\n- **Prior — Coordinator dashboard (1 round).** Org-scoped
  aggregation surface at `/Organizations/{Id:int}/Coordinators`
  showing every slot (across all ministries) with its coordinator
  triple (FK + display name + email + phone), unassigned slots
  sorted to the top by default. Discoverable via a new "Manage
  coordinators" button next to "Edit" on the org header. Filter
  chips at the top: All / Unassigned (default-on: "what still needs
  attention?") / Assigned / Inactive (only renders when there are
  inactive slots). Per-row inline Edit widget: dropdown of org
  members that auto-seeds the phone field from `Person.Phone` (the
  email field stays admin-typed because Person schema stores
  Phone but not Email; coordinators routinely prefer a
  personal/work alias separate from their auth email anyway), plus
  email/phone textboxes honoured with `??=` preservation semantics
  so admin overrides survive a dropdown re-selection. Unassign
  button clears all three fields in one SaveChanges via a service
  shortcut delegate to AssignAsync. Service: new
  `ICoordinatorAssignmentsService` with `ListAsync(orgId)`,
  `AssignAsync(slotId, userId?, email?, phone?, callerUserId)`,
  `UnassignAsync(slotId, callerUserId)`. `ListAsync` eager-joins
  `Ministry → Slot → CoordinatorPerson` via EF LINQ, sorts with
  `ORDER BY (CASE WHEN CoordinatorPersonUserId IS NULL THEN 0
  ELSE 1 END), Ministry.Name, Slot.Name` so unassigned rows float
  to the top. `AssignAsync` gates on `OrgAuth.CanManageOrgAsync`
  (Admin OR Coordinator) AND a secondary `OrganizationMemberships`
  check that the assigned Person lives in the slot's parent org
  (otherwise PermissionDenied — surprising for admins to see a
  foreign-org volunteer shown as "coordinator" of an unrelated
  org's slot). Null `coordinatorUserId` is always allowed so
  admins can clear the FK without picking a replacement.
  Result-enum `CoordinatorMutationResult { Updated, PermissionDenied,
  NotFound }` so the Razor page branches without exceptions.
  16 new `CoordinatorAssignmentsServiceTests` (ListAsync:
  all-slots-in-org / excludes-other-orgs / unassigned-first-sort /
  display-name-populated / empty-org; AssignAsync: admin /
  org-coord-allowed / volunteer-refused / stranger-refused /
  cross-org-admin-refused / coordinator-must-be-org-member-refused
  / null-userid-clears-fk / empty-caller-refused / unknown-slot-
  not-found; UnassignAsync: admin-clears-all-three / volunteer-
  refused). Pre-existing test count 254 → **270/270 passing**;
  build 0 errors / 1 pre-existing xUnit2013 in
  `AssignmentServiceTests` unrelated to this round.
- **Prior — Per-ServiceSlot coordinator (2 rounds: Ministry-CTA +
  schema/UI).** (a) **Round 1:** Ministries/Detail now surfaces an
  admin-only `+ Add team` button next to `+ Add slot`, plus a
  one-line explainer ("A ministry becomes a League once it has a
  team — games, standings, and the Leagues nav become available.")
  that disappears after the first team lands; landing page is a new
  `Components/Pages/Leagues/Teams/New.razor` so the CTA goes
  somewhere real (admin-only via `OrgAuth.CanManageMinistryAsync`,
  coaches picker, age bracket/gender/description, save through
  `ITeamService.CreateTeamAsync`, redirect to `/Leagues/{id}`).
  Verifies: build clean, full suite 245/245 pass. (b) **Round V:**
  schema+UI uplift so each volunteer opportunity can have its own
  coordinator distinct from the ministry coordinator. New
  `ServiceSlot.CoordinatorPersonUserId` (FK People, cascade
  `SetNull` — mirrors Ministry / Team), `CoordinatorEmail`,
  `CoordinatorPhone`, navigation `CoordinatorPerson`. New
  `IOrgAuthService.CanManageSlotAsync(string userId, int slotId)`
  resolution chain: short-circuit on slot coord, otherwise defer to
  existing `CanManageMinistryAsync` (which inherits org Admin /
  Coordinator + parent-ministry transitive). `ISlotDocumentService
  .CanManageSlotAsync(int, string)` now delegates to the new
  OrgAuth method (no duplicate logic). UI: `ServiceSlots/Edit.razor`
  EditForm gained a "Coordinator" section mirroring the Ministry
  Edit coordinator triple (member dropdown + email + phone), with
  full save/load wiring; `ServiceSlots/Detail.razor` got a
  dedicated coordinator card showing name (linked) + email + phone
  with an "Assign one" link when none; `ServiceSlots/Schedule.razor`
  `_canSchedule` swapped from `CanManageOrgAsync(userId, OrgId)` to
  `CanManageSlotAsync(userId, Id)` so a slot coordinator can
  schedule their own slot without an org-wide role. Migration
  `20260705030352_AddServiceSlotCoordinator` generated by
  `dotnet ef migrations add AddServiceSlotCoordinator` — adds 3
  columns, an index, and the SetNull FK. Tests: 9 new
  `OrgAuthServiceTests.CanManageSlotAsync_*` cases pinning each
  branch of the chain (slot coord true, org Admin true, org
  Coordinator true, ministry coord true, unrelated volunteer
  false, not-in-org false, wrong-org membership false, empty
  userId false, nonexistent slot false). Pre-existing test count
  245 → **254/254 passing**; build 0 errors / 1 unrelated
  pre-existing xUnit2013 (in `AssignmentServiceTests`,
  unrelated to this round).
- **Prior — Engagement-verifying training viewer (1 round).**
  Volunteers can no longer click "Mark as completed" without
  actually engaging. PDF: page-rendered-painted check via PDF.js
  (`wwwroot/lib/pdfjs/` + `wwwroot/js/trainingview.js`), every page
  must fire `render {canvasContext, viewport}.promise` before
  counting toward eligibility. Uploaded video: HTML5
  `timeupdate` + `ended` events drive `HighestWatchedSec`. YouTube
  / Vimeo embed: IFrame Player API poll every 5 s for
  `getCurrentTime`. Slideshow / external URL: best-effort 1 Hz
  dwell timer. New `Models/TrainingActivity.cs` (one row per
  `(Person, Content, Version)` — version bump resets progress per
  the contract), new `TrainingContent.TotalPageCount` nullable
  int (server-extracted from the file via lightweight
  `Services/PdfPageCounter.cs` regex scan, used as the "every page"
  denominator), `TrainingService.SyncActivityAsync` coalesces
  monotonically (`Math.Max` on seconds so a hostile client can't
  burn down; set-union on `ViewedPagesCsv` so page-counts can't
  lose pages under concurrent ticks; SQLite-safe today per the
  `DisjointConcurrentPageSets_UnionSurvives` test — note the
  test pins the SQLite-serializability assumption and would
  regress on Postgres without a row-lock). `CheckEligibilityAsync`
  read-only per-format rule: PDF = `viewed.Count >= totalPages`,
  Video = `highestSec >= actualDuration * 0.95`, Slideshow =
  `dwellSec >= 80% of admin-entered EstimatedDuration`. Anti-cheat
  floors: `MinActualDurationSec = 10` (refuse 1-second fabrications),
  `MinAbsoluteDwellSec = 30` (refuse 1-second burn-ins). Anti-abuse:
  `SessionResetWindow = 30 min` — if `LastUpdatedUtc` is more
  than 30 min old, `FirstOpenedUtc` re-anchors to "now" so a
  volunteer who opens the training once and returns days later
  doesn't instantly qualify for a 60-s Slideshow. New
  `TrainingCompletionResult.InsufficientEngagement` enum value
  surfaces the gate at `RecordCompletionAsync` (the trust boundary)
  and renders in the Take page as `_lastError = "you haven't
  engaged enough yet" + the rule-specific reason`. `Take.razor`
  rewritten with `[JSInvokable] SyncProgress(int, TrainingActivitySync)`
  + `DotNetObjectReference` + per-format ElementRefs (`_pdfHostEl`
  / `_iframeEl` / `_videoEl`) + a `_trackingFailed` flag that
  renders an iframe fallback when PDF.js fails to mount so a
  no-PDF.js browser still sees the file (engagement just isn't
  tracked). YouTube URL → `embed/{id}` rewrite via
  `ToYouTubeEmbed` (hand-rolled query parse to avoid leaning on
  `System.Web`). `Edit.razor` calls `PdfPageCounter.Count(path)`
  after upload and persists `TotalPageCount` on both NEW and
  EDIT (version bump below also auto-resets any in-flight
  activity for the previous version). Migration regenerated by
  `dotnet ef migrations add AddTrainingActivity` so the snapshot
  matches the live model exactly — the warning that round-M's
  hand-written `Designer.cs` triggered (`PendingModelChangesWarning`)
  in `DatabaseMigrationTests` is gone. 13 new
  `TrainingActivityTests` (+ 2 concurrency / anti-abuse regressions
  + 4 legacy `RecordCompletion_*` test re-seeds because the
  engagement gate correctly refuses the old "no activity"
  pattern); pre-existing test count 228 → 245/245 passing; build
  0 errors / 1 unrelated pre-existing xUnit2013. New "Known
  quirks" entries below for `TrainingActivity`, the
  soft-anti-cheat posture, and the dwell-reset heuristic.

- **Prior — Per-org TrainingContent scoping (1 round).** Every
  `TrainingContent` row is now owned by exactly one
  `Organization`; cross-org bleed-through (the bug: an admin of
  Org A could attach a `TrainingRequirement` pointing at
  training belonging to Org B, and a volunteer in Org B would
  hit `Take` only to get rejected because their `OrganizationMembership`
  didn't match the `TrainingContent`'s new owner) is closed at
  the model layer. Schema: `TrainingContent.OrganizationId` is a
  required int (not nullable), cascades on `Organization` delete
  to match `Ministry`'s policy; the existing global
  `(Title, Version)` UNIQUE index is replaced with the per-org
  `(OrganizationId, Title, Version)` index (so two orgs can both
  publish a "Welcome Training" v1 without colliding), plus a new
  `(OrganizationId, Title)` index for catalog listing. Migration
  `20260705020246_AddTrainingContentOrganization` is hand-edited
  from EF's auto-generation in a 3-phase backfill: (A) `ADD COLUMN
  OrganizationId INTEGER NULL`, (B) CTE-driven UPDATE that pulls
  the org from each content's `TrainingRequirement.OrganizationId`
  or its slot's `Ministry.OrganizationId`, falls back to
  `MIN(OrganizationId) FROM Organizations`, (C) `ALTER COLUMN`
  to NOT NULL + indexes + FK with `ON DELETE CASCADE`. Service
  contract changed: `RecordCompletionAsync` now returns a
  `TrainingCompletionResult` enum (`Ok` / `ContentNotFound` /
  `NotOrgMember`) so callers can branch without exceptions; new
  `ListOrgTrainingAsync(int orgId)`, `ListSlotOrgTrainingAsync(
  int slotId)` (resolves slot → ministry → org first), and
  `ListManageableTrainingAsync(string userId)` (union of all orgs
  the user admin's, eager-loaded org name). UI pages (a)
  `Training/Take.razor` — gates completion on OrgMembership
  presence; renders a friendly "You're not a member of this
  organization yet" panel when the gate returns `NotOrgMember`,
  (b) `Training/Manage.razor` — now uses `ListManageableTrainingAsync`
  with the subtitle "Managing for: <org-list>", (c)
  `Training/Edit.razor` — shows an org dropdown for NEW content
  with auto-pick when the admin only admin's one org; shows a
  locked "Owned by {OrgName}" badge for EDIT so the page can
  never be quietly used to reassign content to a different org,
  (d) `Organizations/OrgTrainingEditor.razor` + `ServiceSlots/
  SlotTrainingEditor.razor` — the "Required" dropdowns are now
  scoped to in-org content (via the new `ListOrgTrainingAsync` /
  `ListSlotOrgTrainingAsync`), and both editors do a per-org
  guard at submit time (`db.TrainingContents.Where(...).Select(
  OrganizationId)` for org editor; `db.ServiceSlots.{Ministry.
  OrganizationId}` + content check for slot editor) so a stale
  or hostile POST body can never insert a foreign-org requirement.
  If the guard fires, a dismissable inline `alert-warning` explains
  why ("That training isn't part of this organization anymore.
  Please pick from the refreshed list."); the alert auto-clears
  on the next dropdown change via `@bind:after="ClearError"` and
  on a successful save. Seeder + tests updated: `DatabaseSeeder.cs`
  now assigns `OrganizationId` on its seeded `TrainingContent`
  rows, `TestData.TrainingContent` requires the `orgId` arg, and
  `AssignmentServiceTests.cs` was updated for the 5 stale
  `TestData.TrainingContent` calls. New `TrainingServiceTests.cs`
  coverage +16 tests (3 result-enum cases, 8 list-org-method
  cases, 3 privacy-pin cases, 2 behavior assertions). Total
  tests: **226/226 passing** (was 209).

- **Prior — Ministry interest ("Join this ministry") + Open-page
  filter (1 round).** New junction table `MinistryInterest`
  (composite-unique on `PersonUserId × MinistryId`, cascade FKs
  to People + Ministries, indexed per-person for
  `ListJoinedAsync`); new EF migration `AddMinistryInterest`.
  New `IMinistryInterestService` with strict per-org permission
  gate (caller must be a member of the ministry's org, regardless
  of role — reuses the existing `OrganizationMembership`
  sandbox). `IAssignmentService.ListOpenSlotOccurrencesAsync`
  gained an optional `ministryIdsFilter` parameter (null/empty
  falls back to "all my orgs"; outer "only from my orgs"
  constraint still applies so cross-org ministry ids in the
  filter can't leak shifts). Three surfaces changed:
  (a) `Ministries/Detail` gained a Join/Leave button on each
  ministry page (only visible to in-org users; out-of-org
  visitors are bounced to the Not Found panel). Coordinator
  email card stays always-rendered per the existing visibility
  rule. (b) `Home.razor` gained a "My ministries" panel below
  the four display cards with eager-loaded Ministry +
  Organization, inline Leave buttons, and three distinct
  empty-state branches (0-org-membership / 0-joined /
  shows-rows). (c) `Open.razor` pill-segment with default-on
  "My ministries (@count)" and "All my orgs"; combined
  empty-state CTA directs first-time users to Home (with an
  inline "Switch to All my orgs" escape hatch); in-flight
  Reload race handled with a monotonic `_reloadVersion`
  monotonic-counter (each call captures `version` at entry,
  reads every result into a LOCAL variable, commits to
  Blazor-bound fields only after the final
  `if (!iAmLatest()) return;` guard — no CancellationToken
  plumbing to avoid `OperationCanceledException` surfaces).
  New test coverage: `tests/ServantSync.Tests/MinistryInterestServiceTests.cs`
  (16 tests: Join happy/duplicate/cross-org/empty-caller/
  admin/coord/nonexistent; Leave after-join/not-joined/
  cross-org/nonexistent; ListJoinedAsync eager-load/sort/
  per-user filter/cross-org invariant/empty-userId) plus 2
  pinning tests pinning the "gate is org-membership-only,
  caller-decides, target-irrelevant" invariant on both
  Admin and Coordinator tiers. `AssignmentServiceTests.cs`
  gained 6 `ministryIdsFilter` tests
  (no-filter / single / multi / mismatched-empty /
  empty-collection-as-no-filter / cross-org-sandbox-still-holds).
  Total tests: **194/194 passing** (was 168).
- **Prior — Per-org encoded registration link + role-aware visibility (1
  round).** Two interrelated changes ship together. (a) **Encoded
  register link.** New `Organization.RegistrationToken` column
  (`TEXT(32)` nullable, indexed unique). New EF migration
  `AddRegistrationToken` adds the column + index and backfills
  existing rows with `lower(hex(randomblob(16)))` (SQLite-only
  syntax; the project targets SQLite only). `OrganizationService.CreateOrgAsync`
  seeds a fresh token on new orgs; new
  `IOrganizationService.GenerateRegistrationTokenAsync` (admin-only) is
  the public rotation entry point used by the Organizations/Detail token
  card. `DatabaseSeeder` also backfills the seeded Demo Church. New
  `Register.razor` accepts `?token=<guid>` (preferred), `?orgId=<id>`,
  and a dropdown fallback that all auto-insert the user as Volunteer on
  success. Signed-in users landing on `/Account/Register` bounce to
  `/`. The Organizations/Detail admin section displays the canonical
  registration URL (built from `Nav.BaseUri` so HTTPS reverse-proxied
  deployments get the right host), a Copy button (`navigator.clipboard`
  JS interop with graceful fallback to a selectable readonly input),
  and a two-stage Rotate button with inline confirmation. (b) **Role-
  aware nav + per-page affordance visibility.** New
  `IOrgAuthService.IsAnyOrgManagerAsync` (Admin OR Coordinator of any
  org) drives the NavMenu block — managers see Organizations, People,
  Leagues, Dashboard; everyone (authenticated) sees Training, My
  schedule, Browse open slots, Account. NavMenu subscribes to
  `AuthenticationStateChanged` and unsubscribes in `Dispose` so the
  manager flag refreshes when the user's role set mutates mid-session.
  `Organizations/Index` listing is filtered to the user's memberships
  with per-row Admin-only Edit buttons and a global `+New organization`
  button gated on `IsAnyOrgAdminAsync`. `People/Index` is now
  Admin-only (non-admins get a "no access" panel). `People/Detail` has
  a server-side route gate (self OR Admin of a shared org; Person
  fetch short-circuited on deny). `Ministries/Detail` hides the
  `Edit ministry` + `+Add slot` affordances for non-admins (Coordinator
  email stays visible per the user requirement). `ServiceSlots/Detail`
  hides the `Edit slot` / `Schedule one` / `Schedule series` cluster for
  non-managers. The Organizations/Detail admin token card sits ABOVE
  the tab strip so it's visible across every tab (rather than buried
  inside one). Test surface added: 9 new `IsAnyOrgManagerAsync` /
  `IsAnyOrgAdminAsync` tests in a new dedicated
  `tests/ServantSync.Tests/OrgAuthServiceTests.cs`, plus 6 new
  token-rotation tests in `OrganizationServiceTests` (token-on-create
  shape, distinct tokens across orgs, rotation happy/double/deny/empty/
  unknown).
- **Prior — Recurring-series inline on `/Roles/{Id}/Schedule` (1
  round).** Coordinator Schedule page now has a "Schedule a recurring
  series" section inline, with a pill-tab mode toggle between
  *Pre-assigned* (pinned volunteer every week, reuses existing
  `ScheduleSeriesAsync`) and *Open shift* (volunteers sign up each
  week, new `ScheduleOpenShiftSeriesAsync` on
  `IAssignmentService`). User-picked scope: **weekly only, both
  modes**. Both modes share day-of-week + time-of-day + duration +
  first/last date + time-zone fields. New `OpenShiftSeriesResult`
  record (created/skipped lists, cap-reached flag) and one new
  service method mirroring `ScheduleSeriesAsync`'s weekly walker.
  Per-occurrence skip reasons are surfaced (existing open shift at
  that time, etc.) and the result panel drills down to actual rows
  instead of just counts — matching the existing `/Series` page.
  Page-level edits follow STATUS.md Known Quirks: the new section's
  `EditForm` deliberately omits `FormName="…"` and `Context="…"`
  (consistent with the open-shift form's avoidance of the
  EditForm-in-conditional SignalR-drop quirk). Stale-field cleanup
  on the pill-tab toggle, page-level capacity-override
  pre-validation, and the results-panel drill-down together apply
  the reviewer's three round-1 fixes end-to-end. The existing
  `/Roles/{Id}/Series` page is left intact (still useful for power
  users; can be deleted in a follow-up if the inline form proves
  sufficient). Verified end-to-end via a browser session against the
  dev server on `https://localhost:7012`: pill-tab toggle clears
  stale fields correctly, "Created 2, skipped 0" alert appears on a
  14-day Sunday-only open-shift series, per-occurrence drill-down
  rows render correctly, and the new occurrences round-trip into the
  existing "Open shifts queued for this slot" table at the top of
  the page. The user's URL `/Organizations/2/Ministries/7/Roles/11/
  Schedule` is from a non-seeded org; the test slot used was
  `/Organizations/1/Ministries/1/Roles/1/Schedule` (Sunday Sound
  Tech). The dev server's static-asset CSS-drop race (see Known
  quirks) was avoided by reusing an already-running dev session.
- **Prior — Global InteractiveServer render-mode cascade +
  Release-mode smoke-test (1 round).** The per-page
  `@rendermode InteractiveServer` directive on
  `Components/Pages/Organizations/Detail.razor` (Round 1) has been
  promoted to a single directive at the top of
  `Components/Layout/MainLayout.razor` so every routed page through
  the DefaultLayout is now interactive by default — covering
  `People/Detail`, `MySchedule`, `Training/Edit`, the
  `ServiceSlots/{Detail,Schedule}` / OrgTraining /
  SlotTraining editor surfaces, and any future `@page` route
  without per-page patches. The directive can't live on
  `<AuthorizeRouteView>` in `Components/Routes.razor` because that
  element has a templated `<NotAuthorized>` child that can't
  cross a `RenderMode` boundary — see the verbatim exception
  quoted in the comment at the top of that file.  Verified end-to-end by `dotnet publish -c Release -o
  bin\ReleasePub` + a browser session against the published
  binary on port 5070 across the originally-reported surface
  (`/Organizations/{id}`) plus every previously-latent surface:
  `/People/{UserId}`, `/MySchedule`, `/ServiceSlots/{Detail}`,
  `/ServiceSlots/{Schedule}`, `/Training/New` (source-toggle:
  Use URL / Upload file), `/Training/{id}` Take,
  `/Training/{id}/Edit` (source-toggle), the embedded
  `SlotTrainingEditor` on a ServiceSlot Detail page, and the
  explicit `?ReturnUrl=` auth round-trip. All interactive,
  no 500s, no console errors on any surface. The dev-mode 500
  on `/People/{id}` (`StaticAssetDevelopmentRuntimeHandler`
  race) was confirmed environmental (now a known quirk).
  Baking these surfaces into a CI smoke-test job that runs
  against each Release build is the natural next step.

- **Prior — RBAC tier-1 closure + snake easter egg (5 rounds).**
  Admin-only gates now end-to-end on the Organization surface.
  `MemberManagementService.AddAsync` / `UpdateRoleAsync` /
  `RemoveAsync` cover the Members tab; `ArenaService.CreateAsync`
  covers arena adds; `OrganizationMinistryService.UpsertAsync`
  covers Ministry create/edit (with cross-org scope check on
  edit). `OrganizationService.CreateOrgAsync` bootstrap-promotes
  the creator to Admin in a single `BeginTransactionAsync` so
  newly-created orgs never land managerless. New
  `MemberRemoveResult { Removed, NotFound, PermissionDenied,
  LastAdminRefused }` plus a shared
  `WouldLeaveOrgWithoutAdminAsync(db, organizationId,
  exceptPersonUserId)` helper guard the invariant "every org
  has ≥1 Admin". Coordinators explicitly lose the ability to add
  ministries / arenas / promote to Admin (intentional
  tightening — was Admin+Coordinator, now Admin-only); the
  Volunteers tab hides the form affordance for non-Admins so
  they don't 403 on submit. Side touch-ups the reviewer
  flagged and we applied: rename `_joinMessage` → `_lastMessage`
  (the alert is shared by add/update/remove/add-arena now),
  drop dead `_canManage` flag, drop redundant `orgExists`
  precheck in `ArenaService`, drop a dead-test stub in
  `MemberManagementServiceTests`, document the deliberate
  self-demote-vs-self-remove asymmetry in code comments.
  Bonus: popup snake game easter egg — type `snake` anywhere on
  the app or open `?snake=1` to play. Pure client-side
  (`wwwroot/js/snake.js` + `wwwroot/css/snake.css`, mounted in
  `Components/App.razor`). Arrow keys, SPACE restart, ESC
  close, retro CRT styling.
- **Prior — UX audit sweep + form-binding defect sweep (1 sitting).**
  (a) **Form-binding defect:** every `<EditForm Model="_x"
  OnValidSubmit="…" FormName="…">` in the app was failing silently
  on submit because `_x` was a *field* (not a property), so the
  HTTP POST round-trip lost the typed values and validators refused
  the input ("Name is required" even when filled). Fixed at 13 sites
  across 12 files: every Edit page (`Organizations/Edit`,
  `Ministries/Edit`, `ServiceSlots/Edit`, `Teams/Players`),
  `Training/Edit`, `Games/Edit`, `Games/Detail` (score form),
  `Organizations/Detail` (Join + AddArena), `OrgTrainingEditor`,
  `SlotTrainingEditor`, `ServiceSlots/ScheduleSeries`,
  `ServiceSlots/Schedule`. Pattern is now uniformly
  `[SupplyParameterFromForm] private T _x { get; set; } = new();`.
  (b) **UX sweep (the top 5 wins from the audit):** new
  `Components/Shared/LocalTime.razor` converts UTC → browser-local
  on every assignment-time display; new
  `Components/Shared/SkeletonLoader.razor` provides Bootstrap
  `placeholder-glow` shimmer for the Home cards (no more flashing
  zeros); `Components/Shared/ConfirmDialog.razor` gains Esc / backdrop
  cancel / Cancel autofocus / `aria-labelledby` for a11y parity with
  Bootstrap's modal; `LoginModel` gets `[Required]` + `inputmode="email"`;
  new `Services/SlotUploadLimits.cs` centralizes the 10 MB cap
  (used by both server validation and `InputFile maxAllowedSize`).
  (c) **Timezone bucketing:** `UserTimeZoneProvider.ToLocal(utc)` is
  the single canonical conversion. `AssignmentCalendar`,
  `Dashboard`, and `GameDayCalendar` were routing user-perceived
  day-bucketing through raw UTC `.Date` predicates — restructured
  to bucket by `TzProvider.ToLocal(utc).Date` and to
  `@implements IDisposable` + subscribe to `TzProvider.TimeZoneChanged`
  so the bucketing re-runs when the browser zone arrives after
  prerender. `Dashboard` caches the raw rows in `_rawRows` and adds
  a `RebuildBuckets()` method so TZ change re-buckets in memory
  without hitting the DB. `Games/Edit.razor` edit-path `Date` and
  `StartLocal` seeds use `TzProvider.ToLocal(_game.StartUtc)` so a
  late-evening UTC game reading on an EST device shows the correct
  local calendar day (not the UTC date). The
  `AssignmentCalendarTests.ClickingNext_AdvancesTheAnchor` regression
  that the audit surfaced is fixed.
- **Last session:** added per-slot shared documents ("upload an area
  under each volunteering slot where coordinators can share documents
  for volunteers and organize them"). New `Models/SlotDocument.cs`
  (FK chain `UploadedByUserId` → `People.UserId` → `AspNetUsers.Id`
  so the UI can show `Person.DisplayName`); new
  `Services/SlotDocumentService.cs` with upload (10 MB cap,
  extension allow-list, collision-resistant file names),
  grouped-by-category listing, delete with proper permission
  gating; `UploadPathProvider` extended with slot-scoped root/
  relative/absolute path helpers; new minimal-API download endpoint
  at `/slots/{slotId:int}/documents/{docId:int}/download`, auth-
  and slot-membership-gated; SignalR `MaximumReceiveMessageSize`
  bumped to 20 MB to give the 10 MB upload cap real headroom; the
  "Replace" feature (re-upload a new version of an existing doc,
  preserving the row id so the download URL is stable). The
  "Shared documents" section in
  `Components/Pages/ServiceSlots/Detail.razor` has a coordinator-
  only upload card, file-type badge per row, delete-confirm
  dialog, and separate `_uploadError` / `_deleteError` /
  `_replaceError` states. 2 sample documents seeded on the Sound
  Tech slot.
- **Earlier:** built the sports-league MVP. `Arena` / `Team` /
  `Player` / `Game` tables, `TeamAgeBracket` + `GameStatus` enums,
  `Ministry.ParentMinistryId` self-FK for sub-ministry coordinators,
  `StandingsCalculator` (pure, 8 tests) + `TeamService` +
  `GameService` (with arena-conflict detection), `OrgAuthService`
  extensions for ministry / team / parent access, 8 new pages
  (`/Leagues/*`, `/Teams/{id}`, `/Players/{id}/Edit`,
  `/Games/{id}`), `GameDayCalendar.razor` overlay component
  (games + game-day volunteers on one grid with arena filter),
  `Organizations/Detail` gains an Arenas tab, and a full seeder
  extension that seeds 13 users, 5 ministries (3 sub-ministries of
  the league), 3 arenas, 4 teams, 16 players, 6 games (1 played
  for standings, 1 intentional arena double-book), 1 Concussion
  training content, and 3 volunteer-shift assignments. The
  `AddSportsLeague` + `AddSlotDocuments` migrations are generated.
- **Build state:** `dotnet build` → 0 errors / 0 warnings.
- **Test state:** `dotnet test` → 159 / 159 pass. Coverage: `OrgAuthService`
  (new direct-test file: 9 tests pinning `IsAnyOrgManagerAsync` Admin
  / Coordinator / Volunteer / empty / null / no-memberships / mixed
  edges plus `IsAnyOrgAdminAsync`-vs-Coordinator), `CalendarEvent.Tooltip`,
  `AssignmentStatusUi.ColorFor`, `DateRangeCalculator.Resolve`,
  `IcsCalendarGenerator` (long lines / escaping / empty list /
  multiple events),`StandingsCalculator` (7 win/draw/loss scenarios
  + sort tiebreakers), `AssignmentService` (conflict matrix + training
  matrix + the new ScheduleOpenShiftSeriesAsync happy / skip-on-dup /
  inactive-slot + the existing capacity matrix, real SQLite in
  `SqliteTestBase`), `GameService` (arena-conflict matrix, same-team,
  cross-org arena, self-exclusion on update), `MemberManagementService`
  (Add + Update + Remove paths, real SQLite), `ArenaService` +
  `OrganizationMinistryService` (Admin-only gates, real SQLite),
  `OrganizationService` (now includes the token-rotation matrix: token
  on create shape, distinct tokens, rotation happy/double/Coordinator-
  denied/Volunteer-denied/empty-caller/unknown-org),
  plus a `PageAccessTests` matrix walking the page-level Deny
  semantics from Coordinator / Volunteer callers. bUnit component
  tests for `DateRangeChips` and `AssignmentCalendar`.

  `CalendarEvent.Tooltip`, `AssignmentStatusUi.ColorFor`,
  `DateRangeCalculator.Resolve`, `IcsCalendarGenerator` (long
  lines / escaping / empty list / multiple events),`StandingsCalculator` (7 win/draw/loss scenarios + sort
  tiebreakers), `AssignmentService` (conflict matrix + training
  matrix, real SQLite in `SqliteTestBase`), `GameService`
  (arena-conflict matrix, same-team, cross-org arena,
  self-exclusion on update), `MemberManagementService` (Add
  + Update + Remove paths, real SQLite), `ArenaService`
  + `OrganizationMinistryService` (Admin-only gates, real
  SQLite), plus a `PageAccessTests` matrix walking the
  page-level Deny semantics from Coordinator / Volunteer
  callers. bUnit component tests for `DateRangeChips` and
  `AssignmentCalendar`.

## Quick commands

```bash
# Restore + build (from the repo root)
dotnet restore
dotnet build

# Run the app (seeds the DB on first launch)
dotnet run

# Run unit + bUnit tests
dotnet test

# Force a re-seed
rm servantsync.db && dotnet run

# Apply the latest migration to an EXISTING database
dotnet ef database update

# CI runs on every push to main and every PR. The workflow lives at
# .github/workflows/ci.yml; trigger it locally with:
dotnet build --configuration Release
dotnet test  --configuration Release
```

## Seed data (sign in with any of these, password is `Passw0rd!`)

| Email                       | Name              | Role in Demo Church | Notes                                              |
|-----------------------------|-------------------|---------------------|----------------------------------------------------|
| `admin@demo.local`          | Alex Admin        | **Admin**           | Can manage the org, its ministries, and its users. |
| `coordinator@demo.local`    | Chris Coordinator | **Coordinator**     | Can manage the org's scheduling surface only.      |
| `volunteer@demo.local`      | Vee Volunteer     | **Volunteer**       | Safe-Spaces compliant (completed 1 month ago).     |
| `volunteer2@demo.local`     | Val Walker        | **Volunteer**       | Safe-Spaces compliant (completed 2 months ago).    |

The seed creates 1 org, 2 ministries, 3 service slots, 1 training
content + 2 requirements, 2 training completions, and 6 assignments
across this Sunday + next Sunday (with one intentional overlap on
Vee's next-Sunday Vocals + Sound Tech). See
[`README.md → Seeded domain data`](README.md#seeded-domain-data)
for the full table and the 13-user / 4-team / 16-player / 6-game
sports-league seed overlay.

## Known quirks (read before changing anything)

- **The `/MySchedule/ics` Subscribe button can't actually be
  subscribed to from an external calendar client today.** The
  minimal-API endpoint uses `.RequireAuthorization()` (cookie-only).
  Google / Apple / Outlook calendars cannot present your domain's
  cookie, so the in-browser button works when you're signed in,
  but the endpoint can't be *subscribed* to. This is a UX lie
  the tooltip doesn't acknowledge. See **Pending work → Tier 1**
  for the fix.

- **`AssignmentCalendar`'s `AnchorDate` parameter is a *local-frame*
  month marker**, not a UTC instant. Callers derive it from
  `TzProvider.ToLocal(DateTime.UtcNow).Year/.Month`; never pass
  `DateTime.UtcNow.Year/.Month` directly. The previous behavior
  (convert UTC midnight to local then read Year/Month) shifted
  the grid one calendar month back for negative-offset users at
  the UTC-day boundary — the fix to drop the conversion is what
  unblocked `AssignmentCalendarTests.ClickingNext_AdvancesTheAnchor`.

- **`UserTimeZoneProvider.ToLocal(DateTime utc)` is the single
  canonical UTC → browser-local conversion.** Any new page that
  renders time-of-day or day-buckets MUST go through this method
  instead of inlining the conversion, and (if it shows a calendar
  view on top of bucketed data) MUST `@implements IDisposable` +
  subscribe to `TzProvider.TimeZoneChanged` so a TZ arrival after
  prerender re-buckets without a page reload. Existing call sites
  that follow this convention: `AssignmentCalendar.razor`,
  `Dashboard.razor`, `GameDayCalendar.razor`, `MySchedule.razor`
  (uses `<LocalTime>` for display), all per-slot / per-team /
  per-league / per-game detail pages.

- **Arena-conflict enforcement is application-level, not DB-level.**
  The arena double-book check lives in `GameService.ScheduleGameAsync`,
  not in the database. The seeder writes two games at the same
  `Field 1` / same time on purpose (games 5 + 6) to exercise the
  conflict path — EF accepts both inserts because the
  `(ArenaId, StartUtc)` index is non-unique. If you want
  DB-enforced uniqueness, add a filtered unique index on
  `(ArenaId, StartUtc)` where `Status != Cancelled`. Deferred to
  **Pending work** as Tier 2.

- **`CanManageMinistryAsync` only walks one parent level.** A
  sub-sub-ministry's grandparent coordinator won't inherit
  authority. Three-level nesting isn't needed for the MVP; flag in
  the known-quirks.

- **Player.PrimaryContactPhone/Email are denormalized on the
  `Player` record** so coaches can call a parent without a join.
  They are NOT auto-synced from the `Person` row — if a parent's
  phone changes, the player's contact info is stale until edited
  by the coach.

- **Leagues/Index.razor lists ministries with at least one team.**
  Ministries without teams (Worship Team, Children's Ministry) are
  reachable via the Organizations page; the Leagues nav is for the
  sports surface specifically.

- **Sports league uses soccer's 3-1-0-0 points scheme.** Standings
  are parameterized (3-1-0 default) but the UI hard-codes the
  header copy as "3 per win, 1 per draw, 0 per loss". For
  basketball/volleyball, the page text needs a tweak; the math
  itself is parameterized.

- **Test project is nested under the main project.**
  `tests/ServantSync.Tests/` is inside the main project's `**/*.cs`
  glob. The csproj has
  `<DefaultItemExcludes>$(DefaultItemExcludes);tests\**</DefaultItemExcludes>`
  to keep the main project from compiling the test sources.
  **Remove that line** if you restructure into
  `src/ServantSync/` + `tests/ServantSync.Tests/`.
- **The per-org TrainingContent migration (`AddTrainingContentOrganization`)
  had a SQLite-navigation-resolution bug that's now fixed.** The
  Phase-2 backfill SQL referenced `s.Ministry.OrganizationId` (EF
  Core notation for `ServiceSlot → Ministry → Organization`) but
  never joined the `Ministries` table explicitly. SQLite parses
  identifiers as flat column lookups against the FROM list, so
  the migration crashed with `SQLite Error 1: no such column:
  s.Ministry.OrganizationId`, rolled Phase 1's `ADD COLUMN` back,
  and the user saw the symptom as `'no such column: OrganizationId'`
  at seed time. Round-1 fix: insert an explicit
  `LEFT JOIN Ministries m ON m.Id = s.MinistryId` so `m.OrganizationId`
  resolves. Plus a perf-comment noting the per-row correlated
  subquery is N round-trips (acceptable for the seeded dev DB and
  one-time migrations; revisit with a temp-table approach if
  production-scale data already in the thousands trips the wall).
  The migration's in-file comment block documents the gotcha for
  the next contributor.
- **If `dotnet ef database update` ever fails mid-migration, wipe
  `servantsync.db` (+ `.db-shm`, `.db-wal`) and re-run from scratch.**
  SQLite + EF Core's transactional handling around DDL is
  inconsistent across versions; a malformed Phase-2 SQL that fails
  mid-up can leave the database in a partial state where
  `__EFMigrationsHistory` does not record the failed migration but
  some adjacent rows were partially altered. Recovery is
  `rm servantsync.db servantsync.db-shm servantsync.db-wal`
  + `dotnet run` again — there's no in-place migration "reset"
  command in EF Core.

- **Person Overlaps KPI counts rows, not unique people.** The
  symmetric overlap-check loop in `Dashboard.razor` sets
  `ConflictsWithSelf` on every row in an overlapping pair, so two
  overlapping assignments → KPI reads 2. The README test plan
  reflects this. If you want unique-people counting, change the
  loop or the KPI computation.

- **`DateRangeChips` is purely presentational.** The range math is
  in `Components/Shared/DateRangeCalculator.cs` so it's testable.
  Don't inline the math back into the component.

- **StatusColor is centralized in
  `Components/Shared/AssignmentStatusUi.cs`.** Dashboard and
  MySchedule use `AssignmentStatusUi.ColorFor(...)`. Don't
  reintroduce local copies (one had to be fixed mid-way when the
  helper was extracted).

- **The intentional overlap is on Vee, the primary demo user.**
  When you sign in as Vee, the dashboard / MySchedule shows a
  2-row overlap warning. This is intentional, not a seed bug.

- **`SignIn.RequireConfirmedAccount = false`** in `Program.cs`.
  Don't flip this to `true` without first adding an
  email-confirmation flow (the dev email sender logs the
  confirmation link, but the user experience is incomplete).

- **MailKit has an open moderate advisory**
  (`GHSA-9j88-vvj5-vhgr`) suppressed in the csproj via
  `NoWarn=NU1902`. Bump the package when a patched release ships.

- **Password rules are relaxed** (`RequiredLength = 8`, no
  digit / uppercase / non-alphanumeric required) so the seed
  password `Passw0rd!` passes. Tighten for production.

- **The seeder is idempotent on the Organizations table only.** If
  you add a new piece of seed content, either add it inside the
  existing skip-if-empty block, or add an existence check for that
  specific piece.

- **The seeder uses `DateTime.UtcNow` for assignment times.** Re-seed
  when the date context matters (e.g., after a long gap the
  "this Sunday" anchor advances).

- **`ApplicationDbContext` is registered as a factory (for
  Blazor Server) AND as a scoped service (required by Identity).**
  The scoped registration resolves the context from the factory
  (`sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>()
  .CreateDbContext()`), so the configuration is shared. Don't call
  `AddDbContext<T>` separately with its own options-builder — that
  would register a second, scoped `IDbContextOptionsConfiguration<T>`
  that the factory can't resolve from the root provider. Identity's
  stores will break if the scoped registration is removed.

- **RBAC uses a custom `OrganizationRole` enum, not Identity roles.**
  `AuthorizeView Roles="..."` is a no-op for this app. Use
  `OrgAuthService.CanManageOrgAsync` instead. NavMenu has a
  comment to this effect.

- **The dashboard's KPIs and lists read from
  `ApplicationDbContext.Assignments` via `IDbContextFactory`.** Don't
  switch to the scoped `DbContext` inside the dashboard
  handler — long-lived Blazor circuits can outlive the request
  scope.

- **Every `<EditForm Model="…" FormName="…">` model property
  MUST have `[SupplyParameterFromForm]`.** A plain field or an
  auto-property without the attribute silently drops the typed
  value on submit and validators will refuse the input. Pattern:
  `[SupplyParameterFromForm] private T _x { get; set; } = new();`.
  The original defect was diagnosed at 13 sites; if you add a new
  `<EditForm>`, the model property MUST follow this pattern from
  day one.

- **SlotDocument upload cap is 10 MB** (`SlotUploadLimits.MaxFileSizeBytes`,
  used by both server validation and `InputFile maxAllowedSize`).
  Bump it if a coordinator needs to upload high-res PDFs, slide
  decks, or audio. The SignalR `MaximumReceiveMessageSize` is
  currently 20 MB — if you raise the per-file cap, raise this too.

- **SlotDocument extensions are allow-listed, not sniffed.**
  The list in `SlotDocumentService.AllowedExtensions` covers
  the common office + image formats but does NOT scan content.
  Coordinators are trusted not to upload executables. Don't
  expose the upload form to untrusted users.

- **SlotDocument paths block sibling-directory traversal** via
  `UploadPathProvider.GetSlotFileAbsolutePath` (compares the
  resolved directory against the slot's uploads root), but the
  check is not symlink-aware. Coordinators with shell access
  to the server could in theory plant a symlink in the slot's
  uploads dir. Not a realistic threat for a Blazor Server app.

- **The slot-document download endpoint is a minimal API**,
  not a controller. It works the same way and uses the same
  auth-gated pattern as `/MySchedule/ics`; if you add more
  slot-document actions (rename, version, etc.), promote it
  to a controller for consistency.

- **Open-redirect fix lives in `Services/UrlSafety.IsLocalUrl`.**
  Both `Login.razor` and `Register.razor` validate `?returnUrl=`
  through this helper before navigating. If you add any other
  page that takes a `?returnUrl=`, route it through the same
  helper.

- **SlotDocument categories are free-form strings**, not a
  fixed list. Coordinators can type anything ("Music", "Setup",
  "Announcements", or even typos like "musci"). A normalized
  category table is the obvious enhancement — see
  **Pending work → Tier 2**.

- **The slot-document delete confirmation is a reusable
  `Components/Shared/ConfirmDialog.razor` Bootstrap modal** with
  Esc / backdrop / Cancel autofocus / `aria-labelledby` a11y
  parity. Other destructive actions (remove player, drop
  membership) should use the same component for consistency.
- **The popup snake easter egg listens for the `snake` keystroke
  buffer globally** (capture phase). Single-character keys are
  appended to a 5-char rolling buffer; if the buffer equals
  "snake" (case-insensitive), the modal opens. Modifier-combination
  keystrokes (Ctrl/Alt/Meta) and non-character keys (Tab, Enter,
  Arrow keys when the modal is closed) are intentionally NOT
  buffered or intercepted. The same listener also accepts a
  `?snake=1` URL param as a mobile / no-keyboard fallback.
  Modal life-cycle: ESC closes from any state, arrow keys steer
  while alive, SPACE restarts on death. The Modal DOM lives
  outside of Blazor's render tree (it's a plain `appendChild`
  into `<body>`) so it can't be unmounted by Blazor's circuit
  teardown without an explicit `closeSnake()` call.
- **Snake game win condition: board completely filled.** Snake
  length cap is `400 - 1 = 399` (one cell holds the seed food
  that's about to be eaten when the snake is length 397). When
  the random-food picker can't find a free cell, the snake
  settles on the head; game-over overlay shows. Future
  enhancement: a "Board cleared!" celebration path distinct
  from "Game over".
- **Dev-runtime static-asset race on `dotnet run`.** When the
  app is started via `dotnet run` in Development, the
  `StaticAssetDevelopmentRuntimeHandler` patches scoped CSS
  files. The patcher occasionally throws
  `FileNotFoundException: Could not find file
  '...\wwwroot\ServantSync.styles.css'` even when the
  build-emitted scoped CSS bundle exists at
  `obj\Debug\net9.0\scopedcss\bundle\ServantSync.styles.css`.
  The error is environmental — it has reproduced across
  multiple rounds with no source-code change required and
  tends to crop up after one or two protected-route
  navigations in a hot dev session. Don't waste time trying
  to re-run `dotnet build --no-incremental` mid-session; the
  race is in the patcher's source-path mapping, not the
  build output. Workaround for browser smoke-testing: stop
  the dev session, then
  `dotnet publish -c Release -o bin\ReleasePub` and
  `dotnet bin\ReleasePub\ServantSync.dll --urls=http://localhost:5070`.
  The published binary resolves scoped CSS via the static-
  web-assets manifest's HASHED paths and bypasses the dev-
  runtime patcher entirely.
- **`.NET 9` `@rendermode` has no `Static` value.** The
  directive only accepts `InteractiveServer` /
  `InteractiveWebAssembly` / `InteractiveAuto`. Static rendering
  is the implicit default that requires an absence of any
  interactive ancestor. So a page currently nested under
  `MainLayout` cannot opt back out to Static SSR via
  `@rendermode` alone. Path forward when an opt-out is genuinely
  needed (none today): route through a sibling non-interactive
  layout, or move the cascade target to a layer above
  `MainLayout`. Tracked in **Pending work → Tier 2** so it
  stays on the radar.
- **`EditForm FormName` mounted inside an `@if` (or ternary /
  conditional) on a page with `@rendermode InteractiveServer`
  (directly or cascade-inherited from `MainLayout`) drops the
  SignalR circuit the moment the conditional branch is
  revealed, leaving every other `@onclick` on the same page
  inert (Members and Arenas tabs of `/Organizations/{id}` were
  the originally-failing surfaces).** Captured console error:
  `The connection to the server was lost.` Root cause: Blazor's
  enhanced-form handling treats any `<EditForm>` with a
  `FormName` parameter as static-form-SSR-capable; mounting
  such a form mid-render into an already-interactive circuit
  triggers a render-mode mismatch and the circuit drops. Fix:
  drop `FormName="…"` and `Context="…"` (if a renamesd
  EditContext variable is in use) from EditForms that are
  mounted conditionally on InteractiveServer pages; keep
  `Model` and `OnValidSubmit`. The `[SupplyParameterFromForm]`
  private-property attribute is a no-op in pure interactive
  mode but harmless — leave it as a defensive fallback for
  the day a future page route promotes the form to static.
  Verified-clean sites so far:
  `Components/Pages/Organizations/Detail.razor` Members + Arenas
  (and the schedule proposal form at `/Roles/{Id}/Schedule`,
  which has `FormName` on a NOT-conditionally-mounted EditForm
  and stayed interactive in the same Release-mode session).
  Razor comments at the Organizations/Detail EditForm sites
  document the deviation for future readers. Sites that share
  the same code shape but weren't smoke-tested this round are
  listed at **Pending work → Tier 2** for the audit. Do NOT
  blanket-strip `FormName` across the codebase — the
  form-binding-defect sweep invested 13 sites in the
  `EditForm + FormName + [SupplyParameterFromForm]` pattern
  specifically so pages can swap between static-SSR and
  interactive without a source rewrite. The fix is per-site
  (only when the form is mounted conditionally inside an
  interactive page).
- **There is a SECOND, distinct failure mode of `EditForm FormName`
  on `@rendermode InteractiveServer` pages that does NOT show as a
  circuit drop.** Captured exception:
  `System.InvalidOperationException: Headers are read-only,
  response has already started.` Stack frame points into the
  OnValidSubmit handler's call to a cookie-writing service
  (CookieAuthenticationHandler.AppendResponseCookie →
  HttpContext.Response.Cookies.Append → IHeaderDictionary
  set_SetCookie → Kestrel headers-read-only exception).
  Triggered from the SignInManager.PasswordSignInAsync /
  SignInManager.SignInAsync chain when those run inside the
  OnValidSubmit handler. Mechanism: `FormName` enables
  Blazor's enhanced-form handling, which on submit ALSO tries
  to start a static-form-SSR POST roundtrip — that POST
  begins streaming the response in the same RequestScope. The
  SignalR-initiated OnValidSubmit handler then runs and tries
  to write the auth cookie; by then the response has begun
  streaming and `IHeaderDictionary` is read-only. The circuit
  does NOT drop — the SignalR circuit stays alive while the
  C# handler's cookie write throws. **Affected surfaces**
  (round-AM, 2026-07-05): `Components/Account/Pages/Login.razor`
  (`FormName="login"` stripped) +
  `Components/Account/Pages/Register.razor`
  (`FormName="register"` stripped preventively since
  `HandleRegister` calls `SignInAsync` after
  `UserManager.CreateAsync`). Both WHY-comments in source
  cross-link to this entry. **Account pages that DON'T write
  cookies server-side** stay out of this list: Manage.razor
  has no EditForm; ForgotPassword / ResetPassword /
  ChangePassword handlers call `UserManager.{FindByEmail,
  GeneratePasswordResetToken, ResetPassword, ChangePassword}`
  which DON'T touch `HttpContext.Response.Cookies`. They MAY
  surface a third (yet-unseen) failure mode under enhanced-form
  handling — audit in Tier 2 if it surfaces under testing.
  **The AccountLayout-not-derived claim from round-AD/round-AF
  ("Account pages do NOT enter this list because they use
  `@layout AccountLayout`") is INCORRECT for these two
  pages.** Account pages that don't write cookies still pass
  through that exemption; Account pages that DO write cookies
  (Login, Register) join this list. Per-site discipline
  honored: do NOT blanket-strip `FormName` from the
  ForgotPassword / ResetPassword / ChangePassword set.

## Pending work (reprioritized)

The list below is ordered by my read of **value-to-the-user ×
risk-to-the-codebase**, not by chronology. Tier 1 is what the user
is most likely to trip over while testing; Tier 4 is the long
tail.

### Tier 1 — Ship before the next round of user testing

- [ ] **Add endpoint integration tests for
      `/Account/PerformLogin` + `/Account/PerformRegister`.** Round-AN
      shipped the two minimal-API endpoints but added zero new tests
      because `tests/ServantSync.Tests/SqliteTestBase.cs` is
      service-level only — exercising the endpoints would require a
      `Microsoft.AspNetCore.Mvc.Testing` + `WebApplicationFactory<Program>`
      test fixture. These are the most security-sensitive surfaces in
      the app (auth + user-create + cookie write + open-redirect
      validator) so the missing regression net is real. Concrete
      coverage targets: (a) POST `/Account/PerformLogin` with seeded
      credentials → 302 + Set-Cookie contains the Identity auth
      cookie; (b) POST with bad credentials → 302 to
      `/Account/Login?error=invalid`, no auth cookie; (c) POST with
      locked-out user → `?error=locked`; (d) POST
      `/Account/PerformLogin` with `returnUrl=https://evil.com` → 302
      to `/` (UrlSafety.IsLocalUrl rejection); (e) POST
      `/Account/PerformRegister` with a registration token → Person +
      OrganizationMembership(Role = Volunteer) created + auth cookie
      set; (f) POST `/Account/PerformRegister` with a token that's
      pre-resolved to orgId + dropdown pointing at a different org
      → token wins, dropdown is overridden; (g) POST
      `/Account/PerformRegister` with a duplicate email → 302 to
      `/Account/Register?error=…` (Identity error surfaces). One-time
      test-infra addition; re-uses the seeded SqliteTestBase DB +
      DatabaseSeeder. Tier 1 because the endpoints are live.
- [ ] **ICS subscribe endpoint accepts a per-user token for external
      calendar clients.** Right now
      `Program.cs → app.MapGet("/MySchedule/ics", ...).RequireAuthorization()`
      only accepts the user's cookie auth, which external calendar
      apps cannot present. The MySchedule page's "Subscribe" button
      tooltip *promises* something the endpoint can't deliver. Fix:
      add a `User.IcsFeedToken` (unguessable GUID, generated on user
      create, regeneratable from Account manage), and accept the URL
      form `/MySchedule/ics?token={guid}&scope=&days=...`. The
      endpoint accepts either cookie auth OR a valid token; the
      token path never expires but can be regen'd by the user.
- [x] **ICS line folding per RFC 5545 §3.1.** Done:
      `IcsCalendarGenerator.Generate` now folds content lines at 75
      octets with `\r\n ` continuation via the new `AppendFolded`
      helper. Fold is octet-based (not char-based) and respects
      multibyte UTF-8 boundaries — no char is ever split across a
      fold. Constants: `MaxLineOctets = 75`. 3 new tests:
      `Generate_FoldsContentLinesAt75Octets_Rfc5545` (ASCII long-line
      forces fold, no line > 75 octets after split),
      `Generate_FoldPreservesPayload_AfterReassembly` (RFC rejoins to
      original property value, byte-for-byte exact),
      `Generate_FoldsByOctetCount_NotCharCount` (20 grinning emojis
      — 80 chars but 80 octets UTF-8 + 8-char prefix > 75 octets →
      fold triggers even though char count is far above 75). 270 →
      273/273 passing.
- [x] **"All" date-range chip on `MySchedule` should not silently
      translate to a 90-day URL for the ICS feed.** Done: the
      Subscribe button now has a `SubscribeTooltip` that explicitly
      names the 90-day substitution, plus a visible "Feed: last 90
      days" `text-bg-warning` badge next to the button when the
      on-page range is `> 360 days`. The on-page chip still shows
      "All" because that's what the user picked for the view window
      — the substitution is now announced rather than silent.

### Tier 2 — Tech debt with ROI on the next round of features

- [ ] **More bUnit coverage.** `DateRangeChips` and
      `AssignmentCalendar` are covered. Still missing:
      `GameDayCalendar.razor` (game + volunteer overlay, arena
      filter, day bucketing in user-local TZ), `MySchedule.razor`
      (scope toggle, range chip integration), the slot-document
      flows on `ServiceSlots/Detail.razor`, and any future
      `ConfirmDialog`-using destructive action (player remove,
      drop membership).
- [ ] **Service tests for `SlotDocumentService`.** The service has
      the most security-critical code (path-traversal guard, file-
      extension allow-list, size cap, collision-resistant file
      names). Cover with an in-memory `IUploadPathProvider` shim
      that writes into a per-test temp directory.
- [ ] **DB-enforced arena-conflict unique index.** Add a filtered
      unique index on `(ArenaId, StartUtc)` where
      `Status != Cancelled`. The seeder currently writes two games
      at Field 1 / next-Sat 9:00 UTC on purpose; with the index
      in place, you'd flip the seeder to write them as different
      statuses (or drop one of them and let the test exercise the
      path through `GameService` end-to-end).
- [ ] **SlotDocument: normalized categories.** Free-form categories
      are a UX trap ("Music" vs "music" vs "Musci"). Promote
      `Category` to a `SlotDocumentCategory` table with a per-slot
      list and an autocomplete.
- [ ] **`LocalTimeZoneAwareComponentBase` shared base class.** The
      `@implements IDisposable` + `OnInitialized` subscribe +
      `Dispose` unsubscribe + handler that calls `InvokeAsync(
      StateHasChanged)` boilerplate is now duplicated across
      `AssignmentCalendar`, `Dashboard`, `GameDayCalendar`, and
      `LocalTime`. Extract a base class with a virtual
      `OnTimeZoneChanged()` for re-bucketing. Pure DRY win that
      keeps the alignment from drifting again.
- [ ] **`SkeletonLoader` for more loading states.** Currently it
      covers the Home dashboard cards. Apply to: dashboard list
      re-load after date-range chip click, game-detail score save,
      Schedule / ScheduleSeries "Generating…" button.
- [ ] **Per-page opt-out layout for `.NET 9` no-Static value.**
      The `@rendermode` directive only accepts `InteractiveServer`
      / `InteractiveWebAssembly` / `InteractiveAuto`. A page
      nested under `MainLayout` cannot opt back out to Static
      SSR via `@rendermode` alone. If / when a future page needs
      to escape the cascade (e.g., a pre-hydration form POST on
      a Login surface), introduce a sibling non-interactive
      layout (`Components/Layout/StaticLayout.razor`) so the
      opt-out page can route through it without forking the
      MainLayout cascade. Today no page needs this; tracked so
      it remains discoverable when the need arises.
- [x] **Audit the remaining `<EditForm FormName>` sites that
      mount conditionally on an InteractiveServer page.** Done: 5/5
      sites audited. 3 sites confirmed conditional and **fixed**:
      `Games/Detail.razor` score form (inside `@if(_canManage)`),
      `Organizations/OrgTrainingEditor.razor` AddRequirement (inside
      `@if(_content.Any())`), `ServiceSlots/SlotTrainingEditor.razor`
      Add (inside `@if(_content.Any())`) — `FormName=` and `Context=`
      dropped from each, with per-site comments cross-referencing the
      Known-quirks entry. 2 sites verified clean: `ScheduleSeries.razor`
      (form mounts unconditionally inside outer `@else` of
      `@if(_slot is null)`) and `Teams/Players.razor` (same). Plus the
      original `Organizations/Detail.razor` Members + Arenas forms
      already had the fix from round 7. The
      conditional-EditForm-mount-breaks-the-circuit regression is now
      bounded to the audited 5 paths. Future contributors: drop
      `FormName=` and `Context=` from any new `<EditForm>` that mounts
      inside a conditional inside an `@rendermode InteractiveServer`
      page; mirror the existing comments. Round 7 caught the bug on
      `Components/Pages/Organizations/Detail.razor`'s Members +
      Arenas branches but didn't smoke-test the remaining five
      sites that share the same
      `EditForm + FormName + conditional mount + InteractiveServer`
      recipe:
      `Components/Pages/Games/Detail.razor` (line ≈ 76, the
      score-form card),
      `Components/Pages/Organizations/OrgTrainingEditor.razor`
      (line ≈ 16, AddRequirement),
      `Components/Pages/ServiceSlots/SlotTrainingEditor.razor`
      (line ≈ 12, Add),
      `Components/Pages/ServiceSlots/ScheduleSeries.razor`
      (line ≈ 22, Generate),
      `Components/Pages/Teams/Players.razor` (line ≈ 20, Save).
      Drive each in Release mode and click the `Add / Record /
      Generate` button. If the same ConsoleError disconnect
      appears, drop `FormName="…" Context="…"` from the EditForm
      and add a Razor comment cross-referencing the Known
      quirks entry. Aggregate verified sites back into the Known
      quirks "Sites verified clean after the fix" list once
      the audit completes.

### Tier 3 — Feature investments for the next release cycle

- [ ] **Per-volunteer swap UI.** Coordinators can reschedule from
      the schedule page, but no "find a substitute" flow. The
      assignment is currently treated as immutable once Scheduled;
      a swap is "cancel + re-add with new person", which throws
      away the audit history. A real swap needs an
      `AssignmentSwap` audit row (who-was, who-now, when, why).
- [ ] **Per-game roster picker.** A `GameRosterEntry` table that
      lets a coach pick starters / bench / position for a specific
      game. Currently the team is the roster (any active player on
      the team is "on" the team for every game).
- [ ] **`ConfirmDialog` focus-trap.** Tab inside the modal can
      currently escape to the underlying page. Add the focus trap
      before reuse for any destructive confirm.
- [ ] **Login / Register polish.** Add a password-strength meter
      + confirm-password field + show-password toggle on
      `Register.razor`; tighten the Identity complexity ruleset
      once the meter is in place. (LoginModel already has
      `[Required]` from the audit sweep; Register still needs the
      password-meter treatment.)
- [ ] **Slot-document upload progress.** A coordinator uploading
      several large files sees no feedback other than
      eventually-saved / -failed. Wire a simple "Saving 12 MB of
      bulletin.pdf…" indicator next to the picker.
- [ ] **Slot-document per-document access control.** "Board only"
      vs "All volunteers" so a slot doc with private pastoral
      material isn't exposed to everyone. Currently anyone with
      the slot's org membership can download.

### Tier 4 — Long tail

- [ ] **Dockerfile + production deploy hardening.** HSTS, response
      compression, structured logging sinks, env-var-driven config
      (move SMTP creds out of appsettings), `Migrate()`-on-startup
      (already a no-op here; the migration is applied via
      `InitialCreate`).
- [ ] **Restructure into `src/ServantSync/` + `tests/ServantSync.Tests/`.**
      Removes the `DefaultItemExcludes` workaround and matches the
      standard .NET repo layout.
- [ ] **Push / email reminders** for upcoming assignments.
- [ ] **Reports / analytics** (by-org volunteer participation,
      training compliance %, monthly-hours-per-volunteer, per-team
      win rates).
- [ ] **Drag-and-drop schedule editor.**
- [ ] **Volunteer dashboard** (the current Dashboard is
      coordinator-only — the volunteer view is MySchedule).

## Architectural notes

- **EF Core 9 / SQLite.** `services.AddDbContextFactory` and
  `AddDbContext` are both registered. The factory is the primary
  pattern inside Blazor Server handlers; the scoped registration
  is required by Identity's stores.
- **Identity password-reset / change-password / forgot-password**
  all go through `IEmailSender<IdentityUser>`. In dev, that's
  `LoggingEmailSender`; in prod, `MailKitEmailSender`. The dev
  sender prints the link to the log so you can copy-paste into
  the browser.
- **Browser-timezone story** is cookie-backed IANA, set by
  `wwwroot/js/timezone.js` on first load. The
  `UserTimeZoneProvider` reads the cookie, exposes
  `TimeZoneId` + `Initialized` + `TimeZoneChanged` event + the
  canonical `ToLocal(utc)` conversion, and is injected into any
  page that renders time-of-day strings or day-buckets.
- **`Components/Shared/`** holds app-wide reusable components
  (`AssignmentCalendar`, `DateRangeChips`, `GameDayCalendar`,
  `LocalTime`, `SkeletonLoader`, `ConfirmDialog`) and pure
  helpers (`DateRangeCalculator`, `AssignmentStatusUi`). Pages
  live in `Components/Pages/`. Identity-scaffold pages live in
  `Components/Account/Pages/`.
- **`Services/`** holds the domain services
  (`IAssignmentService`, `ITrainingService`, `IOrgAuthService`,
  `IUploadPathProvider`, `UserTimeZoneProvider`, `SlotUploadLimits`
  constants, `UrlSafety` helper) and the email senders.
- **Date-range math is testable because it's pure.** The
  `DateRangeChips` component is purely presentational; all the
  math lives in `DateRangeCalculator.Resolve`. Don't move the
  math back into the component.
- **TZ math is testable because it's pure.** The display /
  bucketing logic is split between a stateful Shaper
  (`UserTimeZoneProvider` with the JS-roundtripped `TimeZoneId`)
  and a pure conversion (`ToLocal(utc)`). The pure part is what
  new components consume; the stateful part is what
  `TimeZoneInitializer.razor` populates.

## File map (what to know about)

- `Program.cs` — DI wiring (Identity, EF, Razor, email, domain
  services), cookie config, `/MySchedule/ics` minimal-API, slot-
  document download endpoint, and the seeder call. Read this first
  when wiring new dependencies.
- `Data/ApplicationDbContext.cs` — EF model, indexes, FK
  relationships, check constraints. Read this when adding new
  entities.
- `Data/DatabaseSeeder.cs` — sample data, idempotent. Read this
  when extending the demo or understanding what data exists out
  of the box.
- `Components/Shared/AssignmentCalendar.razor` + `.razor.css` —
  month + week calendar, user-local TZ bucketing,
  `IDisposable` + `TimeZoneChanged` subscription. Read this when
  adding view modes or re-bucketing logic.
- `Components/Shared/GameDayCalendar.razor` + `.razor.css` —
  same TZ-aware surface for the sports-league schedule overlay.
- `Components/Shared/DateRangeChips.razor` — chip strip.
- `Components/Shared/DateRangeCalculator.cs` — pure range math,
  fully tested. Tests in
  `tests/ServantSync.Tests/DateRangeCalculatorTests.cs`.
- `Components/Shared/LocalTime.razor` — UTC → browser-local
  display component, `IDisposable` + `TimeZoneChanged`
  subscription.
- `Components/Shared/SkeletonLoader.razor` — Bootstrap
  `placeholder-glow` shimmer for loading states.
- `Components/Shared/ConfirmDialog.razor` — Bootstrap confirm
  modal with full a11y parity. Read this when adding any other
  destructive action.
- `Components/Shared/AssignmentStatusUi.cs` — status → Bootstrap
  class mapping, fully tested. Tests in
  `tests/ServantSync.Tests/AssignmentStatusUiTests.cs`.
- `Components/Pages/Dashboard.razor` — coordinator / admin:
  14-day ops view, KPIs, calendar + list, local-TZ day bucketing
  via `_rawRows` + `RebuildBuckets()`. Wires chips + calendar.
- `Components/Pages/MySchedule.razor` — volunteer: own + org-wide
  assignments. Wires chips + calendar + scope toggle + ICS
  Subscribe button.
- `Services/AssignmentService.cs` — scheduling rules (conflict
  detection, training enforcement, recurring series).
- `Services/GameService.cs` — game scheduling / scoring / arena-
  conflict detection.
- `Services/SlotDocumentService.cs` — per-slot upload / download /
  replace / delete with security-conscious validation.
- `Services/SlotUploadLimits.cs` — central constants for the
  10 MB cap. Don't re-declare the size anywhere else.
- `Services/UserTimeZoneProvider.cs` — `TimeZoneId` +
  `Initialized` + `TimeZoneChanged` event + canonical
  `ToLocal(utc)` conversion.
- `Services/OrgAuthService.cs` — custom RBAC. Replaces Identity
  roles for org-level access control.
- `ServantSync.csproj` — net9.0, `Microsoft.NET.Sdk.Web`. Has the
  `DefaultItemExcludes` workaround for the nested test project.
- `tests/ServantSync.Tests/` — xUnit 2.9.2 + bUnit 1.31.3, 77 tests.
- `.github/workflows/ci.yml` — GitHub Actions CI: `restore` +
  `build` + `test` on every push to `main` and every PR. Uses
  `actions/setup-dotnet@v4` with the built-in NuGet cache.
- `README.md` — end-user / new-contributor documentation. Should
  stay focused on the user-facing story; for working state and
  quirks, see this file.
- `PLAN.md` — the original product spec plus the running list of
  "Subsequent additions" (every component / service added during
  development is logged here).
- `STATUS.md` — this file. Working state, quirks, pending work.
