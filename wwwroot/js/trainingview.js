// Training viewer JS bridge for ServantSync.
// Owns three flows:
//   1. PDF: load via PDF.js, render pages, fire onPageViewed for each page
//      that's actually painted on screen.
//   2. <video>: native HTML5 events — timeupdate + ended + duration — feed
//      HighestWatchedSec monotonically up to DotNet.
//   3. YouTube/Vimeo iframe: poll getCurrentTime via the iframe Player API;
//      falls back to a dwell timer if the API isn't ready (cross-origin
//      delay).
//   4. Slideshow / external URL: pure duration-based best-effort timer; ships
//      HighestWatchedSec = dwell so the server rule unlocks.
// In every case we suppress ticks while document.hidden == true so a
// backgrounded tab doesn't burn dwell time.
//
// Round-AG signature contract (don't change without re-reading
// Take.razor + this file end-to-end):
//   mountPdf(target, url, dotnetRef, contentId)
//   mountVideo(videoEl, dotnetRef, contentId)
//   mountYouTube(iframeEl, dotnetRef, contentId)
//   mountBestEffortDwell(dotnetRef, contentId)
// Each call site constructs an EngagementSink(dotnetRef, contentId)
// internally — call sites pass the raw DotNetObjectReference and the
// content id, NOT a prebuilt sink.
//
// Round-AL provenance: round-AK.0 introduced per-tick cache-bust
// (`?v=${Date.now()}`) URL tokens here; round-AL reverted them. The
// mechanism behind the JSException + ServantSync.exe-exit-4294967295
// crash was UNCONFIRMED at round-AL time. Round-AS supplies the
// actually-confirmed root cause: the namespace was hung off `window`
// only, never exported, so `import()` returned an empty exports
// namespace and Blazor's interop fell through to the engagement-
// tracking-unavailable fallback. Round-AL's `?v=` cache-bust stays
// reverted as a Tier-2 future-work item only. See STATUS.md round-AL
// (legacy) + round-AS (root cause confirmed).

// Round-AS removes the IIFE wrapper that round-AO-and-prior had here.
// ES modules already scope variables to the module, so the IIFE was
//   faking closure semantics that the module-level scope gives us
//   for free. Removing it was mandatory: `export const` declarations
//   are only valid at the module top level — placing one inside an
//   IIFE function body throws a SyntaxError at parse time and the
//   module would have failed to load, replacing the undefined-
//   namespace runtime error with a parse-time error.
// `let pdfjsLib = null;` and `let ytPromise = null;` are now
// module-private (same effective scope as the prior IIFE-internal
// state); safely still closed-over by ensurePdfJs / mountYouTube.
// See STATUS.md round-AS for the full triage.

const PdfWorkerSrc = `/lib/pdfjs/pdf.worker.mjs`;
const PdfMainSrc = `/lib/pdfjs/pdf.mjs`;

let pdfjsLib = null;

async function ensurePdfJs() {
    if (pdfjsLib) return pdfjsLib;
    const mod = await import(PdfMainSrc);
    pdfjsLib = mod;
    pdfjsLib.GlobalWorkerOptions.workerSrc = PdfWorkerSrc;
    return pdfjsLib;
}

// Sends a sync payload to Blazor via .NET callback. We coalesce into a
// "highest so far" local model and only push new maxes — avoids the
// server from doing a write storm every 250ms.
class EngagementSink {
    constructor(dotnetRef, contentId) {
        this.dotnetRef = dotnetRef;
        this.contentId = contentId;
        this.highest = 0;
        this.viewedPages = new Set();
        this.pagesCount = 0;
        this.actualDuration = 0;
        this.lastSentMs = 0;
    }
    setTotal(pages) { this.pagesCount = pages; }
    markViewed(page) {
        if (page > 0) this.viewedPages.add(page);
    }
    setDuration(sec) {
        if (sec > this.actualDuration) this.actualDuration = Math.floor(sec);
    }
    setHighest(sec) {
        if (sec > this.highest) this.highest = Math.floor(sec);
        this.maybeSend();
    }
    maybeSend() {
        const now = Date.now();
        // Throttle: send at most every 2 seconds, but always send on
        // pause / ended / unload via the explicit call sites below.
        if (now - this.lastSentMs < 2000) return;
        this.lastSentMs = now;
        this.sendNow();
    }
    async sendNow() {
        if (!this.dotnetRef) return;
        // Page Visibility: never accumulate dwell while backgrounded.
        if (document.hidden) return;
        try {
            await this.dotnetRef.invokeMethodAsync('SyncProgress', this.contentId, {
                HighestWatchedSec: this.highest,
                ViewedPages: Array.from(this.viewedPages),
                ActualDurationSec: this.actualDuration,
            });
        } catch (e) {
            // dotnetRef can go away on navigation; swallow.
        }
    }
}

// PDF.js render loop. Renders one page at a time to a target div;
// fires onPageViewed(p) after the canvas paint completes.
async function mountPdf(target, url, dotnetRef, contentId) {
    // Round-AU: Accept either a DOM element (legacy Blazor ElementReference)
    // or a string element id (current, reliable across all hosting models).
    // On ACA/.NET 9 InteractiveServer, ElementReferences arrive as plain
    // serialized objects — not real DOM nodes — so canvasRoot.appendChild
    // throws "is not a function". Passing the id as a string and resolving
    // via document.getElementById is the portable fix. See STATUS.md round-AU.
    const canvasRoot = typeof target === 'string' ? document.getElementById(target) : target;

    // Shared error-paint helper, used by BOTH the outer catch
    // (initial mount failures) AND the click-handler catch
    // (page-2+ failures). Declared inside mountPdf so both catches
    // close over it via the function's local scope — round-AT
    // rounded the lexical-chain description:
    //   outer-catch / click-listener callback → mountPdf function body → paintErr declaration
    // all live within mountPdf; round-AS removed the IIFE wrapper so
    // mountPdf is module-top directly, but the inner closure pattern
    // is unchanged. The escape map keeps the error text safe to
    // inject as innerHTML — ServerTrust round (canvasRoot is the
    // volunteer's own training page, no cross-user injection vector,
    // defense in depth). Round-AT prefix chosen over the round-AR
    // round-AR-nits naming to keep the file's audit-chain grep-clean
    // against the codebase's alphabetical-letter scheme (AO/AP/AQ/
    // AR/AS/AT).
    function paintErr(err) {
        const msg = (err && (err.message || (err.reason && err.reason.message)))
            || String(err);
        canvasRoot.innerHTML =
            '<div class="alert alert-danger m-2 small mb-0">'
            + 'PDF rendering failed: '
            + msg.replace(/[<>&]/g, c => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;' }[c]))
            + '</div>';
    }

    let currentPage = 1;
    // Round-AR: SCOPE THE TRY TO THE ENTIRE mountPdf body, not just
    // `renderPage(1)`. Round-AQ's catch only wrapped renderPage(1) +
    // navRoot.addEventListener — the sink creation, ensurePdfJs(),
    // getDocument(), and setTotal() calls sat OUTSIDE the catch.
    // When `import('/lib/pdfjs/pdf.mjs')` rejects (worker / MIME /
    // network) or pdfjs.getDocument rejects (corrupt file /
    // encryption), the throw propagates straight through to the C#
    // MountViewerAsync catch without ever entering the JS catch
    // block — so the gray box stays empty and the volunteer only
    // sees the C#-side structural fallback panel. With the try
    // moved up, every error path in mountPdf paints the same
    // diagnostic text inline, so the volunteer can read what
    // actually failed without DevTools. See STATUS.md round-AR.
    try {
        const sink = new EngagementSink(dotnetRef, contentId);
        const pdfjs = await ensurePdfJs();
        const doc = await pdfjs.getDocument({ url }).promise;
        sink.setTotal(doc.numPages);

        // Round-AO: REPLACE the canvas on each page render (clear
        // before append). One canvas per page, scrolled to top of
        // each new page on arrival — normal PDF-reader UX.
        async function renderPage(p) {
            const page = await doc.getPage(p);
            const viewport = page.getViewport({ scale: 1.4 });
            const canvas = document.createElement('canvas');
            canvas.width = viewport.width;
            canvas.height = viewport.height;
            canvasRoot.innerHTML = '';     // clear prior page's canvas
            canvasRoot.appendChild(canvas);
            // Start at the TOP of the new page, not the bottom of the stack.
            canvasRoot.scrollTop = 0;
            const ctx = canvas.getContext('2d');
            await page.render({ canvasContext: ctx, viewport }).promise;
            // Page only counts as "viewed" once painted — a tab-switch
            // mid-render doesn't qualify.
            sink.markViewed(p);
            sink.sendNow();
        }
        await renderPage(1);
        // Wire next/prev buttons. Take.razor renders the buttons as
        // SIBLINGS of `<div id="pdf-canvas-host">`, both children of
        // `#training-host`. Clicks bubble up to that wrapper, NOT
        // through the host div itself, so we attach the listener to
        // `target.parentElement` (`#training-host`) — round-AH.
        //
        // Round-AR: wrap the per-click renderPage call in a try/catch
        // so a page-2+ failure (corrupt page, memory pressure)
        // paints the inline error instead of unhandled-rejecting
        // silently.
        const navRoot = canvasRoot.parentElement ?? canvasRoot;
        navRoot.addEventListener('click', async (ev) => {
            const t = ev.target.closest('button[data-pdf-action]');
            if (!t) return;
            const action = t.getAttribute('data-pdf-action');
            try {
                // Round-AT: hoist the await-then-mutate rule above the
                // entire chain. ALL three branches (next / prev / jump)
                // follow `await renderPage(target); currentPage = target;`
                // — mutate currentPage ONLY on successful paint. If a
                // page's renderPage throws (corrupt page, memory
                // pressure, etc.), currentPage stays pinned to the
                // still-failed page so a retry doesn't skip past it.
                // The prior round-AR shape only documented the rule on
                // the Next branch; consolidating here so a future
                // contributor doesn't have to infer the same rule for
                // Prev/Jump. (Round-AT also switched `currentPage =
                // currentPage + 1` to the idiomatic `+= 1` form per the
                // round-AR code-reviewer's stylistic feedback.)
                if (action === 'next' && currentPage < doc.numPages) {
                    await renderPage(currentPage + 1);
                    currentPage += 1;
                } else if (action === 'prev' && currentPage > 1) {
                    await renderPage(currentPage - 1);
                    currentPage -= 1;
                } else if (action === 'jump' && t.hasAttribute('data-page')) {
                    const p = parseInt(t.getAttribute('data-page'), 10);
                    if (p >= 1 && p <= doc.numPages) {
                        await renderPage(p);
                        currentPage = p;
                    }
                }
            } catch (err) {
                // Page-2+ failure: paint inline (no rethrow, click
                // handler doesn't expect a throw). Volunteer stays
                // on the page, error visible.
                paintErr(err);
            }
        });
    } catch (err) {
        // Initial mount failure (sink, ensurePdfJs import,
        // getDocument, etc.). Paint inline + rethrow so C#
        // MountViewerAsync catch fires and sets _trackingFailed
        // (structural fallback panel then renders via the
        // round-AQ InvokeAsync(StateHasChanged) fix).
        paintErr(err);
        throw err;
    }
}

function mountVideo(videoEl, dotnetRef, contentId) {
    // Round-AU: accept string id or DOM element (see mountPdf for rationale).
    const el = typeof videoEl === 'string' ? document.getElementById(videoEl) : videoEl;
    const sink = new EngagementSink(dotnetRef, contentId);
    function onLoaded() {
        if (!isNaN(el.duration) && isFinite(el.duration)) {
            sink.setDuration(el.duration);
        }
    }
    function onTimeUpdate() {
        if (document.hidden) return;
        sink.setHighest(el.currentTime);
    }
    function onPause() { sink.sendNow(); }
    function onEnded() {
        sink.setHighest(el.duration);
        sink.sendNow();
    }
    el.addEventListener('loadedmetadata', onLoaded);
    el.addEventListener('durationchange', onLoaded);
    el.addEventListener('timeupdate', onTimeUpdate);
    el.addEventListener('pause', onPause);
    el.addEventListener('ended', onEnded);
    // Final flush on page unload via beforeunload (best-effort).
    window.addEventListener('beforeunload', () => sink.sendNow());
    el.addEventListener('seeking', () => sink.sendNow());
}

// YouTube IFrame Player API: load on demand and poll getCurrentTime
// every 5s. The API exposes its readiness as window.YT.Player after
// the iframe-onload. If the iframe isn't ready, we tick every 5s so
// the dwell timer still progresses (best-effort fallback).
let ytPromise = null;
function ensureYouTube() {
    if (ytPromise) return ytPromise;
    ytPromise = new Promise((resolve) => {
        const tag = document.createElement('script');
        tag.src = 'https://www.youtube.com/iframe_api';
        tag.async = true;
        const prev = window.onYouTubeIframeAPIReady;
        window.onYouTubeIframeAPIReady = () => { if (prev) prev(); resolve(); };
        document.head.appendChild(tag);
    });
    return ytPromise;
}

async function mountYouTube(iframeEl, dotnetRef, contentId) {
    // Round-AU: accept string id or DOM element (see mountPdf for rationale).
    const el = typeof iframeEl === 'string' ? document.getElementById(iframeEl) : iframeEl;
    const sink = new EngagementSink(dotnetRef, contentId);
    await ensureYouTube();
    const player = new window.YT.Player(el, {
        events: {
            'onReady': (ev) => {
                sink.setDuration(ev.target.getDuration());
                const tick = () => {
                    if (document.hidden) return;
                    sink.setHighest(ev.target.getCurrentTime());
                };
                setInterval(tick, 5000);
            },
            'onStateChange': (ev) => {
                if (ev.data === window.YT.PlayerState.ENDED) {
                    sink.setHighest(ev.target.getDuration());
                    sink.sendNow();
                } else if (ev.data === window.YT.PlayerState.PAUSED) {
                    sink.sendNow();
                }
            },
        },
    });
}

function mountBestEffortDwell(dotnetRef, contentId) {
    // Pure timer — for Slideshow (often pptx → Office Web Viewer) and
    // arbitrary external URLs where we have no media hooks. 1Hz tick.
    const sink = new EngagementSink(dotnetRef, contentId);
    let dwell = 0;
    setInterval(() => {
        if (document.hidden) return;
        dwell += 1;
        sink.setHighest(dwell);
    }, 1000);
}

// Round-AS: top-level ES-module export. Registers
// `ServantSyncTraining` on the module's named exports so Blazor's
// `IJSObjectReference.InvokeVoidAsync("ServantSyncTraining.mountPdf",
// …)` (called from Take.razor via
// `JS.InvokeAsync<IJSObjectReference>("import", …)`) resolves the
// dotted identifier. Without this, the IIFE form only sets
// `window.ServantSyncTraining`, the exports namespace is empty, and
// Blazor logs "ServantSyncTraining was undefined" at
// blazor.web.js:1:384. IIFE removal was mandatory: `export const` is
// only valid at module top level. The legacy `window.…` assignment
// is omitted because grep confirmed no caller reads through it.
// STATUS.md round-AS.
export const ServantSyncTraining = {
    mountPdf,
    mountVideo,
    mountYouTube,
    mountBestEffortDwell,
    EngagementSink,
};
