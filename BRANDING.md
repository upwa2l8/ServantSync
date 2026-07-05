# ServantSync — branding

This file is the single source of truth for the visual identity of the
ServantSync app. The actual asset files live in
[`wwwroot/img/`](wwwroot/img/) (and a small README next to them that
points back here). If something on screen doesn't match what's
documented below, **the assets on disk are right** and this doc has
fallen behind — update it. If the assets on disk don't match what's
documented below, **update the assets**.

---

## TL;DR

Two stylized figures (cool indigo→violet on the left, warm amber→rose
on the right) with raised arms form a soft heart-shape void in the
negative space. A small warm-amber glint floats above the heart apex
in place of any religious symbol, and two opposing sync arrows sit at
the bottom. Four decorative connector dots punctuate the outer edges
of the figures as the "people / sync endpoints" motif. The brand reads
as universal "care-of-others" — works equally well for a Methodist
church coordinator, a Christian youth sports league, and a secular
neighborhood-watch co-op.

The mark is intentionally **non-overtly-Christian**: no cross, no fish,
no dove, no specific denomination. The brand is for organizations that
coordinate volunteers; the volunteer pool happens to skew Christian,
but the brand should still feel inviting to anyone who happens to land
on the page.

---

## Why no cross

The original session prompt for the brand was: *"cool logo for
ServantSync, primarily for Christian organizations but perhaps not
entirely."* That "perhaps not entirely" was the load-bearing constraint
that drove every design choice below.

A literal cross would have been the obvious answer, but it would have:
- Alienated the secular slice of the user base the prompt allowed for.
- Read as denominational (Catholic vs. Reformed vs. Orthodox vs.
  non-denominational) no matter how neutrally the cross was drawn.
- Made the brand harder to extend into non-religious use cases
  (corporate volunteer programs, college clubs, neighbourhood
  doesn't-care-what-you-believe mutual-aid groups).

The new mark replaces the cross with a small warm-amber glint dot
(`#fbbf24`, the existing `--ss-glint` token). The glint preserves the
vertical hierarchy a cross would have given the layout without
committing to any single tradition. The two-figures + heart shape
still carries the "care-of-others" energy through the negative space
alone, and the cool/warm color tension still reads as "two parties
meeting across difference" without being preachy about it.

A round-AS-style user submitted a reference logo with a literal
cross on top of the heart, and the cross was the load-bearing
non-starter that drove this round-AU redraw. The rest of the
reference (the two figures, the heart-via-negative-space, the
sync-arrow motif) is exactly the brand language we want — only the
cross was rejected.

---

## Mark geometry

```
        ┌─────────────────┐
        │                 │
        │       ✦         │     ✦ = bridge glint (replaces cross)
        │                 │
        │   ╭─♥─╮  ←── figures raising arms to form heart
        │   ◀     ▶        │
        │   │  ▼  │        │     ▼ = heart-shape void in negative space
        │   ╰──●──╯        │
        │  ●       ●       │     ● = decorative connector dots
        │                 │
        │   ↶       ↷     │     ↶/↷ = opposing sync arrows
        │                 │
        └─────────────────┘
```

| Layer | Geometry (round-AU viewBox 512×512) | Notes |
|-------|------------------------------------|-------|
| Left figure | Cubic Bezier closed path; inner side `M 230 100 → 195 135 → 256 340`; outer top peaks at y=100, base at lower-left `(180,390)` → `(80,350)` | Cool gradient fill — `#3730a3 → #a855f7` (45°). The closed path traces head + body + reaching arm as a single teardrop silhouette. |
| Right figure | Mirrored cubic Bezier closed path; inner side `M 282 100 → 317 135 → 256 340`; outer top peaks at y=100, base at lower-right `(332,390)` → `(432,350)` | Warm gradient fill — `#f59e0b → #e11d48` (vertical). Same teardrop structure as the left, mirrored across `x=256`. |
| Bridge glint | `circle r=14 cx=256 cy=90` | Replaces the cross with a secular warm-amber spark (`#fbbf24`). Preserves vertical hierarchy without committing to any single tradition. |
| 4 connector dots | `circle r=12` upper + `circle r=10` lower on the outer edge of each figure, at `(50,200)` / `(40,320)` (cool) and `(462,200)` / `(472,320)` (warm) | All sit clearly outside the figure body (which extends to x≈60 / x≈452). Echo the prior mark's "people at sync endpoints" motif. |
| Top sync arc | `path d="M 220 420 A 40 40 0 0 0 292 420"` (sweep=0, curves UP) + `polygon` arrowhead at `(292, 420)` pointing down | Cool stroke — top half of a clockwise rotation, arriving at 3-o'clock. |
| Bottom sync arc | `path d="M 292 460 A 40 40 0 0 0 220 460"` (sweep=0, curves DOWN) + `polygon` arrowhead at `(220, 460)` pointing up | Warm stroke — bottom half of the same clockwise rotation, arriving at 9-o'clock. |

The two figures' inner edges form the heart-shape void in the negative
space: **52 px cleft** at `y=100` (from `x=230` to `x=282`),
**122 px lobes** at `y=135` (only 35 px below the cleft, keeping
the heart shape compact), narrowing to a **point** at `y=340` on
the centerline. Outer top peaks are aligned with the cleft at
`y=100` (no "towers" above the heart). The two sync arrows are
**deliberately** not a closed ring — leaving the left + right gap
emphasizes that the brand is "two parties meeting across the
centerline", not "one closed circle".

---

## Color tokens

The same color values appear both in the SVG files and in any code
that simulates a brand accent (e.g., the `<meta name="theme-color">`
in `Components/App.razor`, the `theme-color` CSS variable in
`Components/Layout/NavMenu.razor.css`). The tokens below are the
source of truth.

| Token | Hex | RGB (approx) | Used by |
|-------|-----|--------------|---------|
| `--ss-cool-start` | `#3730a3` | 55, 48, 163 | Brand indigo. Cool side fade-out. |
| `--ss-cool-mid` | `#7c3aed` | 124, 58, 237 | Cool side mid-stop. |
| `--ss-cool-end` | `#a855f7` | 168, 85, 247 | Cool side fade-in. |
| `--ss-warm-start` | `#f59e0b` | 245, 158, 11 | Warm side fade-out (amber). |
| `--ss-warm-mid` | `#f97316` | 249, 115, 22 | Warm side mid-stop (orange). |
| `--ss-warm-end` | `#e11d48` | 225, 29, 72 | Warm side fade-in (rose). |
| `--ss-glint` | `#fbbf24` | 251, 191, 36 | Center bridge dot warm glint. |
| `--ss-text-start` | `#3730a3` | (mirrors cool-start) | Wordmark gradient start. |
| `--ss-text-end` | `#7c3aed` | (mirrors cool-mid) | Wordmark gradient end. |
| `--ss-theme-color` | `#3730a3` | (mirrors cool-start) | `<meta name="theme-color">` value for browser chrome (tab strip, status bar). |

If you ever wire these into CSS as variables, the same hex values
should appear in `:root { --ss-cool-start: #3730a3; ... }` and the SVG
gradient stops should drop those `var()` references rather than hard-
coding the hex (so a global theme-swap via `[data-theme="x"]` can
re-tint without touching the SVG source).

---

## Dark-mode swaps (recommended palette, not yet shipped)

The brand assets ship light-mode-only today. A dark-mode swap would
soften the gradients so they don't vibrate against a `#0b0b14`-style
background:

| Token | Light-mode (current) | Dark-mode (recommended) | Why |
|-------|---------------------|-------------------------|-----|
| Cool side `start` | `#3730a3` | `#1e1b4b` | Lifts less against a dark surface. |
| Cool side `end` | `#a855f7` | `#7c3aed` | Mid violet rather than pastel purple — matches the dark-surface contrast budget. |
| Warm side `start` | `#f59e0b` | `#fbbf24` | Amber→lemon keeps the warmth without over-saturating. |
| Warm side `end` | `#e11d48` | `#be185d` | Rose pinks out at the same luminance as `#e11d48` would over-vibrate. |
| Bridge glint | `#fbbf24` | `#fde68a` | Soft lemon rather than saturated amber — smaller bloom against dark surface. |
| Wordmark gradient | `#3730a3 → #7c3aed` | `#a5b4fc → #c4b5fd` (lavender-mauve) | Pastel gradient stays readable when the page is dark; deep indigo on dark = invisible. |

Implementation path (when the dark-mode work lands):
- Add `[data-theme="dark"]` selector overrides in `app.css`.
- Re-render dark-themed submissions of `servantsync-mark.svg` and
  `servantsync-logo.svg` to `wwwroot/img/`, OR serve them via a
  CSS-background-color trick that's harder to maintain.
- The blazor-side `@Assets["..."]` references don't change — both
  files reference the same paths. The browser picks the right one
  based on `<html data-theme="dark">` which `Program.cs` and `App.razor`
  set.

---

## Theme-color meta

`Components/App.razor` emits `<meta name="theme-color" content="#3730a3">`
matching `--ss-theme-color`. Two reasons:

1. **Mobile browser chrome.** Android Chrome colors the URL bar
   `#3730a3` when the user adds a home-screen shortcut. iOS Safari
   uses it for the splash screen background tint.
2. **Tab strip on supported browsers.** Some browsers tint the tab
   background when the favicon and theme-color match; `#3730a3` is
   the brand-indigo baseline.

If you change the brand indigo, change the meta at the same time —
they should never drift.

---

## Accessibility (a11y)

The brand assets are designed to be readable across most vision
ranges:

- The mark's outer ring + end-dots are **always** rendered at fixed
  pixel sizes (`36×36` in the navbar; `16×16 → 32×32` for favicons
  via the `apple-touch-icon` link in `App.razor`). The viewBox +
  scale preserve crispness on Retina displays.
- The SVG `<title>` and `aria-label` are set on both files so screen
  readers announce "ServantSync logo" / "ServantSync logo mark" when
  the image is the page's primary brand element.
- The navbar `<img>` has `alt="ServantSync"` so a screen reader still
  announces the brand if the SVG is replaced by a fallback. The
  parent `<a aria-label="ServantSync home">` adds the navigation
  intent on top of the visual reading.
- The wordmark `<span class="navbar-brand-wordmark">ServantSync</span>`
  is repeatably announced by a screen reader, so an audit of the
  nav reads "ServantSync home" rather than a bare image-with-aria-label.

If a future contributor adds a dark-mode variant, **keep the title
and aria-label identical** so the brand's accessibility contract
holds across themes.

---

## What this asset is NOT

- **Not a religious symbol.** No cross, no fish, no dove, no church
  silhouette. Catching a future reviewer thinking "should we add a
  chapel outline / stained-glass window / baptism font?" — no. The
  brand is for organizations, religious or otherwise, that coordinate
  volunteers. The mark should not pre-commit to one tradition. The
  current round-AU mark uses a secular warm-amber glint dot at the
  top of the heart in place of a cross for exactly this reason — see
  "Why no cross" above for the full rationale.
- **Not a corporate logo.** No monogram, no logotype glyph, no flat
  cool-tone-only treatment. The mark has personality; that's
  intentional. Future contributors tempted to "tidy up" the
  end-dots by removing them — don't. The dots ARE the brand contract.
- **Not a Bootstrap component.** It uses inline `<svg>` rather than
  Bloora's icon classes so future contributors can copy the SVG to a
  README, an email signature, or a printed banner without re-rendering
  it through Bootstrap's SVG pipeline.

---

## Update procedure

If you change the brand:

1. Edit the SVG file in `wwwroot/img/` first. This is ground truth —
   what renders on screen MUST match what's stored here.
2. Update the color-token table above. The hex values listed in the
   table are documentation; they must match the SVG gradient stops.
3. If you change `theme-color`, also update the `<meta name="theme-color">`
   line in `Components/App.razor`, AND any future CSS variable in
   `app.css` (when dark mode lands).
4. Update the small README in `wwwroot/img/README.md` so a contributor
   scanning that folder alone can reverse-engineer the design rationale.
5. **Visual sanity check.** Open the SVG in a headless Chrome session
   (or any browser), screenshot it at the navbar size (36×36) and the
   favicon size (16×16, 32×32), and confirm the brand still reads.
   The `<system>` reminder earlier was specifically because a stray
   `\u200B` zero-width space inside an SVG path can break renders
   silently on some browsers — render the file, don't just lint it.
6. Update STATUS.md → Where we are → what's new with a one-paragraph
   entry under the latest round.

---

## License & attribution

Both SVG files were authored in-session and are part of the
ServantSync codebase — no third-party assets or licensing is
involved. They fall under the same license as the rest of the
project (per `LICENSE.md`, if present). No attribution is owed.

If a future contributor brings in a third-party icon set (Font
Awesome, Lucide, Tabler, etc.), document the license inline in this
file under a new `Third-party assets` section, and pin the version in
`ServantSync.csproj` or `package.json` so a transitive upgrade can't
quietly change the brand kit.
