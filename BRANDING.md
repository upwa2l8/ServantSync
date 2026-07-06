# ServantSync brand book

The single source of truth for ServantSync's visual identity. Anyone touching the navbar, favicon, splash page, README hero, or email-signature lockup should read this first.

## Two assets, two roles

ServantSync has TWO brand assets in `wwwroot/img/`, each with a different purpose. They share a palette but serve different surfaces:

| File                                       | Surface                                       | Role                                                                                       |
|--------------------------------------------|-----------------------------------------------|--------------------------------------------------------------------------------------------|
| `wwwroot/img/servantsync-mark.svg`         | Navbar, favicon, apple-touch-icon             | The **product** brand. Two-figure heart with sync arcs. Visible at 16px favicon scale.   |
| `wwwroot/img/servantsync-marketing.svg`    | README, splash, in-app empty states, email    | The **marketing** wordmark. Visible at 120–400 px. NOT used as the navbar / favicon.       |

The product mark is the only thing that appears in the navbar, browser tab, iOS home-screen pin, and inline help-doc icons. The marketing wordmark is for surfaces where the user sees the brand at a glance and the page reads as a brand page, not a product page.

## Color tokens

| Token              | Hex        | Used for                                                                  |
|--------------------|------------|---------------------------------------------------------------------------|
| `coolSide / 0%`    | `#3730a3`  | Indigo base of the product-mark gradient, navbar wordmark, marketing lockup, email wordmark text |
| `coolSide / 60%`   | `#7c3aed`  | Mid-violet gradient stop                                                  |
| `coolSide / 100%`  | `#a855f7`  | Light-purple end of the gradient                                          |
| `warmSide / 0%`    | `#f59e0b`  | Amber base of the product-mark gradient                                   |
| `warmSide / 55%`   | `#f97316`  | Orange mid-stop                                                           |
| `warmSide / 100%`  | `#e11d48`  | Rose end of the gradient                                                  |
| Brand glint        | `#fbbf24`  | Small dot above the heart cleft on the product mark                      |
| Navbar backdrop    | `#1f1f2e`  | Browser address-bar tint matching the dark navbar                         |

## Brand-book rules

### Why no religious symbols

ServantSync is built for churches first, but also sports leagues, on-call rotas, and conference staffing — secular by design. The product mark's "bridge glint" is a secular warm-amber dot, not a cross. The marketing wordmark likewise avoids crosses, liturgical ornamentation, or any symbol committed to a single religious tradition. This keeps the brand working on secular surfaces like a youth-soccer league or a hospital on-call rota, not just church contexts.

The fact that the brand pair expresses a **cool↔warm duality** (two figures, two gradient sides, two complementary pulls) carries the "two kinds of people in sync" abstraction without committing to any single tradition.

### Why two assets instead of one

A single logo file serving every surface would either (a) be too busy at 16 px favicon scale or (b) be too plain at 400 px splash scale. The product mark's two-figure heart + sync arcs is intentionally geometric so it survives 16 px without letterform compression; the marketing wordmark is typographic so it carries the brand name where there's room. The cost is keeping both files in sync on color tokens — see the table above.

### Email brand

Outbound email uses the marketing wordmark as a typographic text lockup in the header band (28 px, brand purple `#3730a3`, tight letter-spacing, with the breadth tagline "Volunteer coordination, in sync." below). The text approach is canonical because the marketing wordmark is itself typographic — a solid-color rendering of the wordmark text is the brand, not an approximation of it. The cid-attached image path is preserved as a backward-compatible override for deployments that want a visual mark in the email header (set `Email:BrandImagePath` to a file under `wwwroot/`).

Email clients differ widely in CSS support. Outlook (desktop) uses Microsoft Word for rendering, not a browser — it refuses every non-table layout, ignores CSS `background-clip: text` (the gradient trick used in the SVG), and strips `<style>` blocks from `<head>`. Gmail strips remote resources and most `data:` URIs. A solid-color text wordmark survives all of these; a rasterized PNG survives most but adds a 1-file asset and a tool-dependency to generate it. The text approach is the most reliable and the most aligned with the wordmark's typographic nature.

The 4 email types (confirmation link, password reset link, password reset code, 2FA code) all flow through `Services/MailKitEmailSender.cs` -> `EmailBranding.WrapHtmlBody` -> the new typographic wordmark. The text alt body (`EmailBranding.WrapTextBody`) strips the HTML markup and renders a clean banner + body — no `cid:` references, no inline styles.

## Where the assets are referenced in code

| File                                              | References                                                                           |
|---------------------------------------------------|--------------------------------------------------------------------------------------|
| `Components/App.razor`                            | `<link rel="icon" type="image/svg+xml" href="@Assets["img/servantsync-mark.svg"]" />` |
| `Components/Layout/NavMenu.razor`                 | `<img src="@Assets["img/servantsync-mark.svg"]" alt="ServantSync" width="36" height="36" class="navbar-brand-mark" />` |
| `README.md`                                       | `![ServantSync marketing lockup](wwwroot/img/servantsync-marketing.svg)`              |
| `Components/Shared/WordmarkSplash.razor`          | **Single source of truth for the marketing wordmark on the web.** 7 call sites consume it: Login (`Width=180` + breadth caption + `mt-4 mb-4` rhythm), Register (`Width=140`), and 2× Home / 3× Open / 2× MySchedule empty states (`Width=120` + `Loading="lazy"` for below-the-fold). The brand asset path is hard-coded in this component so a future rebrand is a 1-file edit, not 7. |
| `Services/MailKitEmailSender.cs`                  | `EmailBranding.WrapHtmlBody` renders the typographic text wordmark in the email header |

The WordmarkSplash call sites are NOT enumerated row-by-row because the
single source of truth IS the component — every call site is just a
`<WordmarkSplash Width="..." Loading="..." />` invocation. A
`grep -rn "WordmarkSplash" Components/` returns the full set in
one command.

If you add a new surface that needs the brand, add a row to that table
only if it's a NEW file or NEW mechanism (e.g. the future
`Components/Pages/ServiceSlots/CalendarPdf.razor` per PLAN.md
Round-FR-1). New `<WordmarkSplash>` invocations on existing pages
do NOT warrant a new row.

## Fork / rebrand checklist

If you ever need to fork ServantSync and rebrand it wholesale, you'll need to:

1. Replace `wwwroot/img/servantsync-mark.svg` with your new product mark on the same 512×512 transparent canvas (square viewBox for favicon density).
2. Replace `wwwroot/img/servantsync-marketing.svg` with your marketing lockup (typographic works best at this scale).
3. Update the color tokens table above.
4. Update all references in the "Where the assets are referenced in code" table.

The asset-header defs blocks (`<linearGradient id="brandPurple">` in the marketing SVG, `<linearGradient id="coolSide">` / `<linearGradient id="warmSide">` in the product mark) are intentionally **per-file** rather than shared via cross-file `<link href>` — the lockups must render standalone (email signature, README on a different host, etc.) without depending on a sibling file being loaded first.
