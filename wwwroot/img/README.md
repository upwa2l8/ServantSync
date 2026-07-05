# wwwroot/img — brand assets

This directory holds the ServantSync brand assets that ship as static
web assets (favicon, apple-touch-icon, etc.). The two primary public
files are:

| File                          | Use case                                  | Format |
|-------------------------------|-------------------------------------------|--------|
| `servantsync-mark.svg`        | Favicon + navbar brand image              | SVG    |
| `servantsync-logo.svg`        | Full lockup with wordmark + tagline       | SVG    |
| `apple-touch-icon.png`        | iOS home-screen pinned-tab icon (180×180) | PNG    |

There's also a third public-facing asset: the same brand mark is
embedded as a `cid:`-attached inline image in **every branded email**
ServantSync sends (confirmation, password reset link, password reset
code, 2FA code). The bytes are the PNG version of the mark, served
under the same path resolution as the apple-touch-icon (see
`Services/EmailBrandAssets.cs` for the loader + the
`ServantSync` brand color tokens in `BRANDING.md` for the source
palette).

## Apple-touch-icon: why PNG only

iOS Safari ignores SVG `apple-touch-icon` link tags. The iOS pinned-tab
surface (and the Android equivalent on older WebViews) requires a
literal rasterized PNG/JPEG. So we ship two flavors:

1. SVG mark for the modern favicon (Chrome / Firefox / Edge / Safari 14+).
2. PNG rasterization at **exactly 180×180** for the apple-touch-icon.

The PNG itself is rendered from the SVG via headless Chrome with
`--window-size=180,180 --default-background-color=00000000` so the
alpha channel survives — iOS adds its own chrome around the icon, so
baking white into the PNG would create an undesirable white ring.

## Regenerating the apple-touch-icon PNG

After editing `servantsync-mark.svg`, re-rasterize the PNG:

```powershell
# From repo root
$CHROME = "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
$WRAPPER = "wwwroot\img\_render-wrapper.html"

# Tiny wrapper that pins the SVG to exactly 180×180 on a transparent
# background — see _render-wrapper.html for the full source.
$html = '<!doctype html><meta charset=utf-8><style>html,body{margin:0;padding:0;background:transparent}img{display:block}</style><img src="servantsync-mark.svg" width=180 height=180>'
Set-Content -Path $WRAPPER -Value $html

& $CHROME --headless --disable-gpu --hide-scrollbars `
  --no-default-browser-check --no-first-run `
  --window-size=180,180 `
  --default-background-color=00000000 `
  --screenshot="wwwroot\img\apple-touch-icon.png" `
  "file:///$(($WRAPPER | Resolve-Path).Path)"

# Clean up the wrapper after the screenshot — it is NOT a runtime
# asset.
Remove-Item $WRAPPER
```

If you'd rather use a dedicated SVG rasterizer (Inkscape,
ImageMagick + librsvg, `cairosvg`, etc.), the equivalent options are:
- Inkscape: `inkscape servantsync-mark.svg --export-type=png --export-filename=apple-touch-icon.png -w 180 -h 180`
- ImageMagick: `magick -background none -density 384 servantsync-mark.svg -resize 180x180 apple-touch-icon.png`
- librsvg: `rsvg-convert -w 180 -h 180 servantsync-mark.svg -o apple-touch-icon.png`
- cairosvg: `python -c "import cairosvg; cairosvg.svg2png(url='servantsync-mark.svg', write_to='apple-touch-icon.png', output_width=180, output_height=180)"`

After re-export, the new file does NOT need to ship to the
`img/_render-wrapper.html` workflow in CI — direct rasterizers
preserve transparency correctly on the first try.

## When to use which file

- **App chrome** (`Components/App.razor` + `Components/Layout/NavMenu.razor`):
  `servantsync-mark.svg` at 36×36 for navbar; `apple-touch-icon.png` at
  180×180 for the apple-touch-icon link.
- **README + docs**: `servantsync-logo.svg` (the full lockup with the
  wordmark). README bitmap previews are taken from the SVG via
  headless-Chrome `--screenshot` at the same 720×280 viewBox.
- **Emails**: `servantsync-mark.png` (or any other rasterized brand
  asset) is attached as a `cid:` inline image under
  `cid:servantsync-mark` by `ServantSync.Services.MailKitEmailSender`.
  See `BRANDING.md` for the design tokens; see
  `Services/EmailBrandAssets.cs` for the configuration / override
  hook.

## Do not

- Don't replace the C-arcs with a literal cross or any other overtly
  religious symbol. The mark is intentionally readable across both
  Christian-organization and secular contexts.
- Don't re-export the SVG with anti-aliasing turned off — the gradient
  arcs need the browser default AA to read as smooth on retina.
- Don't ship a JPEG version of the apple-touch-icon — JPEG has no
  alpha channel and iOS forces a white background, which would create
  the visible ring described above.
