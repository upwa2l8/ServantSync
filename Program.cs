using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using ServantSync.Components;
using ServantSync.Data;
using ServantSync.Models;
using ServantSync.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Database ----
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=servantsync.db";

// Use the factory as the single source of truth for the DbContext configuration.
// Don't ALSO call AddDbContext<T> with its own options-builder — that registers a
// second set of IDbContextOptionsConfiguration<T> callbacks (scoped) on top of the
// factory's ones. The factory ends up asking the root provider for
// IEnumerable<IDbContextOptionsConfiguration<T>> and trips over the scoped one,
// throwing "Cannot resolve scoped service from root provider" at startup.
//
// Round-ACA-1.8 (this): Microsoft.Data.Sqlite does NOT accept "busy_timeout"
// as a connection-string keyword (its SqliteConnectionStringBuilder throws
// ArgumentException on unrecognised keywords -- observed at startup on
// revision servantsync--0000006 with "Connection string keyword 'busy_timeout'
// is not supported"). Register SqliteBusyTimeoutInterceptor as a singleton
// and attach it to the factory via AddInterceptors so every opened
// connection runs "PRAGMA busy_timeout=30000" right after the parser. The
// interceptor (not the connection string) is the canonical EF Core way to
// apply PRAGMAs that aren't recognised keywords on SqliteConnectionStringBuilder.
builder.Services.AddSingleton<SqliteBusyTimeoutInterceptor>();
builder.Services.AddDbContextFactory<ApplicationDbContext>((sp, opts) =>
{
    opts.UseSqlite(connectionString);
    opts.AddInterceptors(sp.GetRequiredService<SqliteBusyTimeoutInterceptor>());
});
// Identity's stores (and any other consumer) still need a scoped
// ApplicationDbContext. Resolve it from the factory so the configuration is
// shared — the factory owns the options, the DI scope owns the context lifetime.
builder.Services.AddScoped<ApplicationDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

// ---- Identity ----
builder.Services.AddIdentity<IdentityUser, IdentityRole>(opts =>
{
    opts.SignIn.RequireConfirmedAccount = false;
    opts.Password.RequireDigit = false;
    opts.Password.RequireNonAlphanumeric = false;
    opts.Password.RequireUppercase = false;
    opts.Password.RequiredLength = 8;
    // Default password-reset / email-confirmation token lifetime is one day
    // (DataProtectionTokenProviderOptions.Default). Service can be customized via
    // opts.Tokens.ProviderMap["ResetPassword"] = new ConsoleResetProvider() etc.
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Email sender — LoggingEmailSender in Development so you can copy the link from
// the test log; MailKitEmailSender in any other environment reads SMTP details
// from the "Email" section of appsettings.json (see EmailOptions).
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
// EmailBrandAssets is registered as a SINGLETON (not scoped) — the bytes
// are loaded once at construction time and cached for the lifetime of the
// process. Every MailKitEmailSender instance shares the same byte array;
// re-reading the file per email would burn IOPS for no benefit. Resolving
// via the host's PhysicalFileProvider so the file lookup matches how
// wwwroot/img is actually served.
builder.Services.AddSingleton<IEmailBrandAssets>(sp => new EmailBrandAssets(
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailOptions>>(),
    sp.GetRequiredService<IFileProvider>(),
    sp.GetRequiredService<ILogger<EmailBrandAssets>>()));
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<IEmailSender<IdentityUser>, LoggingEmailSender>();
}
else
{
    builder.Services.AddScoped<IEmailSender<IdentityUser>, MailKitEmailSender>();
}

builder.Services.ConfigureApplicationCookie(opts =>
{
    opts.LoginPath = "/Account/Login";
    opts.LogoutPath = "/Account/Logout";
    opts.AccessDeniedPath = "/Account/AccessDenied";
});

// ---- Razor / Blazor ----
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// DI-registered cascading authentication state. Routes.razor also wraps the
// Router in <CascadingAuthenticationState> manually, but the markup-only
// wrapper has a known .NET 9 quirk: during the prerender → interactive
// rehydration of pages with `@rendermode InteractiveServer`, a fresh
// <AuthorizeView> instantiation can miss the cascade and throw
//   "Authorization requires a cascading parameter of type
//    Task<AuthenticationState>. Consider using CascadingAuthenticationState
//    to supply this."
// The DI service injects the provider at a lower level than the markup
// wrapper, so it survives the render-mode boundary transition and is the
// canonical .NET 9 fix. Keeping the markup wrapper too — two layers of
// cascade is harmless (closest-wins), and the markup one is the safety net
// if a future contributor removes the DI call without realizing why.
builder.Services.AddCascadingAuthenticationState();

// Raise the SignalR message-size limit so the Blazor InputFile component
// can stream files larger than the 32 KB default. 20 MB to give the
// SlotDocumentService 10 MB cap real headroom — InputFile adds per-chunk
// metadata that eats into the message size budget.
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(o =>
    o.MaximumReceiveMessageSize = 20 * 1024 * 1024);

// HTTP context access for any code that needs the true scheme/host behind proxies.
builder.Services.AddHttpContextAccessor();

// ---- Domain services ----
builder.Services.AddScoped<IAssignmentService, AssignmentService>();
builder.Services.AddScoped<ITrainingService, TrainingService>();
// Round-FR-2.2: in-person scheduled training sessions with manual-completion
// audit. Sibling to TrainingService --- same DI lifetime as the rest of the
// domain services because every Razor page resolves it via a scoped factory.
builder.Services.AddScoped<ITrainingSessionService, TrainingSessionService>();
builder.Services.AddScoped<IUploadPathProvider, UploadPathProvider>();
builder.Services.AddScoped<IOrgAuthService, OrgAuthService>();
builder.Services.AddScoped<ITeamService, TeamService>();
builder.Services.AddScoped<IGameService, GameService>();
builder.Services.AddScoped<IStandingsService, StandingsService>();
builder.Services.AddScoped<ISlotDocumentService, SlotDocumentService>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<IMemberManagementService, MemberManagementService>();
// Round-FR-3.2: stub-Person + claim-token service. Sibling to
// MemberManagementService --- same DI lifetime as the rest of the
// domain services. CreateStubAsync / RotateClaimTokenAsync /
// ListStubsAsync are admin-gated; ClaimStubAsync is public (the raw
// token IS the authentication) and called from
// /Account/PerformRegister. See Services/IPersonService.cs for the
// full security posture + Services/PersonService.cs for the
// re-parent strategy.
builder.Services.AddScoped<IPersonService, PersonService>();
builder.Services.AddScoped<IArenaService, ArenaService>();
builder.Services.AddScoped<IOrganizationMinistryService, OrganizationMinistryService>();
// Round-BA: SlotManagementService hosts the gate-split logic for the
// ServiceSlots/Edit.razor page (create vs edit tier). Keeps the page
// markup thin and lets PageAccessTests pin the gate the same way it
// already does for ministries/arenas/members.
builder.Services.AddScoped<ISlotManagementService, SlotManagementService>();
// Round-BC: SystemAdminManagementService hosts the grant/revoke flow
// behind a SystemAdmin gate. Sole application pathway for mutating
// the SystemAdmin Identity role — every grant/revoke lands an audit
// row in SystemAdminGrantAudits.
builder.Services.AddScoped<ISystemAdminManagementService, SystemAdminManagementService>();
builder.Services.AddScoped<IMinistryInterestService, MinistryInterestService>();
builder.Services.AddScoped<ICoordinatorAssignmentsService, CoordinatorAssignmentsService>();
// Round-AI: self-heal handler. The Take page calls this on every
// volunteer visit when the content is a local PDF whose TotalPageCount
// is null — for legacy uploads predating PdfPageCounter's wiring, or
// for files whose upload-time extension/MIME detection silently
// missed the .pdf case. See Services/PdfPageCountHealer.cs.
builder.Services.AddScoped<IPdfPageCountHealer, PdfPageCountHealer>();
builder.Services.AddScoped<UserTimeZoneProvider>();
builder.Services.AddScoped<DatabaseSeeder>();

// ---- Background / hosted services ----
// SqliteBackupService: periodic VACUUM INTO snapshot, gated on
// Backup__Enabled=true. Production enables via appsettings.Production.json
// or env var (DEV defaults to disabled). Defaults land under
// <contentRoot>/backups so the working dir is NEVER exposed as a public
// static-file path. Service-level WHY-comment in Services/SqliteBackupService.cs.
builder.Services.Configure<BackupOptions>(builder.Configuration.GetSection("Backup"));
builder.Services.AddHostedService<SqliteBackupService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

// Serve uploaded training files from wwwroot/uploads/training.
// Round-ACA-1.4 symptom fix: the Dockerfile bakes a symlink
//   /app/wwwroot/uploads/training -> /data/uploads
// into the image at build time so the existing hardcoded
//   Path.Combine(WebRootPath, "uploads", "training")
// path in Program.cs stays unchanged when we mount a persistent Azure
// Files share at /data. The collision: at container boot the /data
// volume is mounted fresh and may not yet contain /data/uploads
// (first deploy / wiped share). The symlink then resolves to nothing
// and .NET's Directory.CreateDirectory throws
//   "The file '/app/wwwroot/uploads/training' already exists."
// (Linux: lstat on a dangling symlink returns a regular-file-like
// node, so mkdir(EEXIST) fires pre-target-check). The fix is to
// read DirectoryInfo.LinkTarget first; if uploadsRoot is a symlink,
// materialize the target directory before the original
// CreateDirectory call. Once /data/uploads exists on the volume,
// the symlink resolves and the second CreateDirectory is a no-op.
// Idempotent on the next start (target already exists -> DirectoryInfo
// resolves the link fine, LinkTarget still reports the target, and
// CreateDirectory(target) is idempotent too).
var uploadsRoot = Path.Combine(app.Environment.WebRootPath, "uploads", "training");
if (new DirectoryInfo(uploadsRoot).LinkTarget is string uploadsSymlinkTarget)
{
    Directory.CreateDirectory(uploadsSymlinkTarget);
}
Directory.CreateDirectory(uploadsRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsRoot),
    RequestPath = "/uploads/training",
});

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Identity endpoints. Logout is a real form POST; Login + Register are
// also real form POSTs now (round-AN) — see the round-AN WHY-comment
// block immediately below the Logout endpoint for the full rationale.
app.MapPost("/Account/Logout", async (SignInManager<IdentityUser> signIn) =>
{
    await signIn.SignOutAsync();
    return Results.Redirect("/");
});

// ---- Round-AN: Login + Register endpoints (static SSR form targets) ----
// THE PROBLEM. The previous Login.razor + Register.razor were
// `@rendermode InteractiveServer` pages whose OnValidSubmit handlers
// called SignInManager.PasswordSignInAsync / SignInAsync inside the
// SignalR-circuit HttpContext. That context's Response had already
// streamed the initial page render, so cookie writes threw
//   "Headers are read-only, response has already started."
// (verbatim, captured at Login.razor:63). Round-AM's per-site
// FormName-strip was directionally correct but insufficient: the
// cookie write path can't succeed inside ANY Blazor-Interactive
// handler. THE FIX. Login.razor + Register.razor are now static-SSR
// pages posting plain HTML forms to these endpoints; each POST runs
// against a FRESH HttpContext whose Response hasn't streamed. Open-
// redirect safety: UrlSafety.IsLocalUrl (same helper the previous
// Razor pages used). Per-site discipline preserved: do NOT blanket-
// rewrite the non-cookie-writing Account pages
// (ForgotPassword / ResetPassword / ChangePassword / Manage). See
// STATUS.md round-AN for the full triage.
app.MapPost("/Account/PerformLogin", async (
    HttpContext ctx,
    SignInManager<IdentityUser> signIn,
    IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(ctx);
    var form = await ctx.Request.ReadFormAsync();
    var email = form["email"].ToString();
    var password = form["password"].ToString();
    // Checkbox presence: browsers send the literal string "on" for
    // a checked box without a value attribute, which bool.TryParse
    // rejects. Presence-based check works regardless of value text.
    var remember = form["remember"].Count > 0;
    var returnUrl = form["returnUrl"].ToString();
    var safeTarget = UrlSafety.IsLocalUrl(returnUrl) ? returnUrl : "/";

    var result = await signIn.PasswordSignInAsync(
        email, password, remember, lockoutOnFailure: false);
    if (result.Succeeded)
        return Results.Redirect(safeTarget);

    // `error` = {locked, invalid} — both branches render in the
    // Login.razor switch-block.
    var errorToken = result.IsLockedOut ? "locked" : "invalid";
    return Results.Redirect($"/Account/Login?returnUrl={Uri.EscapeDataString(safeTarget)}&error={errorToken}");
});

app.MapPost("/Account/PerformRegister", async (
    HttpContext ctx,
    UserManager<IdentityUser> users,
    SignInManager<IdentityUser> signIn,
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IPersonService personSvc,             // Round-FR-3.3 (this): claim-token re-parent
    IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(ctx);
    var form = await ctx.Request.ReadFormAsync();
    var firstName = form["firstName"].ToString().Trim();
    var lastName = form["lastName"].ToString().Trim();
    var email = form["email"].ToString().Trim();
    var password = form["password"].ToString();
    var token = form["token"].ToString().Trim();
    // Round-FR-3.3 (this): the claiming stub's raw claim token, posted
    // from the Register.razor hidden form field. Empty string → no claim
    // (normal register path). Non-empty → after SignInAsync, call
    // PersonService.ClaimStubAsync to re-parent the stub's existing
    // memberships / assignments / training to the new IdentityUser.
    var claim = form["claim"].ToString().Trim();
    var orgIdRaw = form["organizationId"].ToString();
    var returnUrl = form["returnUrl"].ToString();

    // Token resolution mirrors the round-AM HandleRegister logic: a
    // registration token (admin-issued) wins over the dropdown's
    // organization choice. If the token didn't match, fall through
    // to the dropdown. orgId stays null if neither resolves — the
    // user can register without joining an org.
    int? orgId = null;
    if (!string.IsNullOrEmpty(orgIdRaw) && int.TryParse(orgIdRaw, out var parsedOrgId))
        orgId = parsedOrgId;

    await using var db = await dbFactory.CreateDbContextAsync();
    if (!string.IsNullOrEmpty(token))
    {
        var matched = await db.Organizations
            .FirstOrDefaultAsync(o => o.RegistrationToken == token);
        if (matched is not null)
            orgId = matched.Id;
    }

    var user = new IdentityUser
    {
        UserName = email,
        Email = email,
        // SignIn.RequireConfirmedAccount = false in Program.cs means
        // sign-in succeeds without confirmation; a real deployment
        // should tighten this together with the LoggingEmailSender's
        // link confirmation flow.
        EmailConfirmed = true,
    };
    var create = await users.CreateAsync(user, password);
    if (!create.Succeeded)
    {
        var errorMsg = string.Join(" ", create.Errors.Select(e => e.Description));
        var safeTarget = UrlSafety.IsLocalUrl(returnUrl) ? returnUrl : "/";
        return Results.Redirect($"/Account/Register?returnUrl={Uri.EscapeDataString(safeTarget)}&error={Uri.EscapeDataString(errorMsg)}");
    }

    db.People.Add(new Person { UserId = user.Id, FirstName = firstName, LastName = lastName });
    if (orgId.HasValue)
    {
        db.OrganizationMemberships.Add(new OrganizationMembership
        {
            PersonUserId = user.Id,
            OrganizationId = orgId.Value,
            Role = OrganizationRole.Volunteer,
        });
    }
    await db.SaveChangesAsync();

    await signIn.SignInAsync(user, isPersistent: false);

    // ─── Round-FR-3.3: claim-token re-parent (token-primary path) ───
    // If the volunteer registered with ?claim=... (FR-3.3), re-parent
    // the stub's memberships / assignments / training to this new
    // IdentityUser via PersonService.ClaimStubAsync. Service is the
    // source of truth (validates hash, expiry, consume state, then
    // does the FK re-parent; see the full re-parent sub-round audit
    // in STATUS.md round-FR-3.2 for the SQLite defer-foreign-keys
    // + FormattableString + PersonClaimToken.PersonUserId-flipped
    // sub-rounds that landed it green).
    //
    // On Succeeded → redirect to safeTarget (re-parented Person is
    // now live under the new IdentityUser; returning to / settles the
    // navigation redirect that would otherwise re-fire circle-back to
    // /Account/Register). On any non-Succeeded result → SIGN OUT the
    // just-signed-in cookie + redirect back to /Account/Register with
    // a descriptive error code so the user sees what happened. Sign-out
    // is critical: without it the user would be "authenticated as a
    // phantom account" whose Person row lived but whose stub merge
    // failed — the dashboard would render without their history and
    // there'd be no user-visible signal.
    if (!string.IsNullOrEmpty(claim))
    {
        var outcome = await personSvc.ClaimStubAsync(
            rawClaimToken: claim,
            newIdentityUserId: user.Id,
            newEmail: email);

        if (outcome.Result != StubClaimResult.Succeeded)
        {
            // Roll back the just-issued auth cookie so we're not
            // signed in as a phantom identity. The IdentityUser row
            // + Person row still exist (intentional: they're harmless
            // orphans and admin can clean them up; the alternative —
            // deleting the IdentityUser on failure — is more brittle
            // because the volunteer may have ALREADY been in your org
            // as a MailConfirmed-only user from a prior POST).
            await signIn.SignOutAsync();
            var reason = outcome.Result switch
            {
                StubClaimResult.InvalidToken    => "claim_invalid",
                StubClaimResult.Expired          => "claim_expired",
                StubClaimResult.AlreadyClaimed  => "claim_used",
                StubClaimResult.AlreadyLinked    => "claim_already_linked",
                StubClaimResult.ValidationFailed => "claim_invalid",
                _                                => "claim_failed",
            };
            var safeTarget = UrlSafety.IsLocalUrl(returnUrl) ? returnUrl : "/";
            return Results.Redirect(
                $"/Account/Register?returnUrl={Uri.EscapeDataString(safeTarget)}&error={reason}");
        }
    }

    return Results.Redirect(UrlSafety.IsLocalUrl(returnUrl) ? returnUrl : "/");
});

// ICS subscribe feed for /MySchedule. Auth-gated. Returns text/calendar with
// the user's assignments in a configurable window. Volunteers subscribe to
// this in Google / Apple / Outlook to see their schedule alongside everything
// else on their personal calendar.
app.MapGet("/MySchedule/ics", async (
    HttpContext ctx,
    IDbContextFactory<ApplicationDbContext> dbFactory) =>
{
    var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    var scope = ctx.Request.Query["scope"].ToString();
    var days = int.TryParse(ctx.Request.Query["days"], out var d) ? Math.Clamp(d, 1, 365) : 90;

    var fromUtc = DateTime.UtcNow;
    var toUtc = fromUtc.AddDays(days);

    await using var db = await dbFactory.CreateDbContextAsync();

    IQueryable<Assignment> query = db.Assignments
        .Include(a => a.ServiceSlot).ThenInclude(s => s.Ministry)
        .Where(a => a.StartUtc >= fromUtc
            && a.StartUtc < toUtc
            && a.Status != AssignmentStatus.Cancelled
            && a.Status != AssignmentStatus.NoShow);

    if (string.Equals(scope, "org", StringComparison.OrdinalIgnoreCase))
    {
        var orgIds = await db.OrganizationMemberships
            .Where(m => m.PersonUserId == userId)
            .Select(m => m.OrganizationId)
            .ToListAsync();
        query = orgIds.Count == 0
            ? query.Where(a => false)
            : query.Where(a => orgIds.Contains(a.ServiceSlot.Ministry.OrganizationId));
    }
    else
    {
        query = query.Where(a => a.PersonUserId == userId);
    }

    var assignments = await query
        .OrderBy(a => a.StartUtc)
        .AsNoTracking()
        .ToListAsync();

    var events = assignments.Select(a => new IcsEvent
    {
        Uid = $"assignment-{a.Id}@servantsync.local",
        StartUtc = a.StartUtc,
        EndUtc = a.EndUtc,
        Summary = $"{a.ServiceSlot.Ministry.Name} \u00b7 {a.ServiceSlot.Name}",
        Description = string.IsNullOrEmpty(a.Notes) ? null : a.Notes,
        Location = a.ServiceSlot.Location,
    });

    var ics = IcsCalendarGenerator.Generate("ServantSync", events, DateTime.UtcNow);
    return Results.File(
        System.Text.Encoding.UTF8.GetBytes(ics),
        contentType: "text/calendar; charset=utf-8",
        fileDownloadName: "servantsync.ics");
}).RequireAuthorization();

// Slot-document download endpoint. Auth-gated, membership-gated. The doc
// must belong to the slot in the URL (defense in depth — a stale link
// from a different slot is rejected).
app.MapGet("/slots/{slotId:int}/documents/{docId:int}/download", async (
    int slotId, int docId,
    HttpContext ctx,
    ISlotDocumentService docs,
    IUploadPathProvider paths,
    IOrgAuthService orgAuth) =>
{
    var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    // GetAsync eager-loads ServiceSlot → Ministry so the membership check
    // and the slot-mismatch check both use the same row.
    var doc = await docs.GetAsync(docId);
    if (doc is null || doc.ServiceSlotId != slotId) return Results.NotFound();

    var orgId = doc.ServiceSlot.Ministry.OrganizationId;
    if (await orgAuth.GetRoleAsync(userId, orgId) is null) return Results.Forbid();

    string filePath;
    try { filePath = paths.GetSlotFileAbsolutePath(slotId, doc.FilePath); }
    catch { return Results.NotFound(); }
    if (!File.Exists(filePath)) return Results.NotFound();

    // Stream the file rather than buffering with File.ReadAllBytes so a
    // future 100 MB+ doc doesn't OOM the server.
    return Results.File(
        System.IO.File.OpenRead(filePath),
        contentType: doc.ContentType ?? "application/octet-stream",
        fileDownloadName: doc.OriginalFileName);
}).RequireAuthorization();

// Round-ACA-1.9 (this): pre-EF Core cleanup of stale SQLite transaction
// files. Azure Files SMB preserves .db-journal / .db-wal / .db-shm from a
// previous container killed mid-write. SMB oplocks on those files persist
// and block the next migration's BEGIN IMMEDIATE (EF Core's
// AcquireDatabaseLockAsync via SqliteHistoryRepository -- observed as
// "SQLite Error 5: 'database is locked'" after a 30s busy_timeout window,
// confirmed empirically on revision servantsync--0000004 with full stack
// trace). Delete any stale files at startup to atomically remove the SMB
// oplock holder. Idempotent: if the files don't exist (warm-reuse case)
// it's a no-op, falling through to migration as normal. Best-effort: if a
// file is held by a still-active connection we can't delete it; the
// migration will fail with the same BUSY signal downstream, surfacing a
// still-deeper issue rather than masquerading a fix.
// `??` only catches null, not empty. If the env var is `Data Source=`
// (empty value after the equals), Substring returns `""`, not null, so
// fall through to the documented Azure Files path explicitly.
var candidate = connectionString
    .Split(';')
    .FirstOrDefault(p => p.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
    ?.Substring("Data Source=".Length);
var dbPath = string.IsNullOrEmpty(candidate) ? "/data/servantsync.db" : candidate;
foreach (var suffix in new[] { "-journal", "-wal", "-shm" })
{
    var stalePath = dbPath + suffix;
    try
    {
        if (File.Exists(stalePath))
        {
            File.Delete(stalePath);
        }
    }
    catch (Exception ex)
    {
        // Diagnostic -- surfaces in container log adjacent to the migration's BUSY signal.
        Console.Error.WriteLine($"[Round-ACA-1.9] SQLite pre-cleanup failed to delete {stalePath}: {ex.Message}");
    }
}

// Apply pending migrations, then seed sample data if the database is empty.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync();
}

app.Run();
