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
// content id, NOT a prebuilt sink. The prior version of this file
// expected call sites to pre-build the sink; the C# side never did,
// so mountPdf crashed on the first setTotal() call and the page
// silently fell through to the engagement-tracking-unavailable
// fallback iframe. That's the bug round-AG fixes.

// Round-AL (CURRENT): revert round-AK's per-tick cache-bust on the
// PDF.js module + worker URLs. Round-AK was the only JS-side delta
// between round-AG (known-good) and the user-reported runtime crash
// (JSException + ServantSync.exe exit-4294967295). Reverting
// returns to known-good. See STATUS.md round-AL for the full
// triage.
const PdfWorkerSrc = `/lib/pdfjs/pdf.worker.mjs`;
const PdfMainSrc = `/lib/pdfjs/pdf.mjs`;

(function () {
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
    // fires onPageViewed(p) after the canvas paint completes. We chunk
    // "next page" navigation onto the same engine so volunteers can't
    // legitimately skip a page.
    //
    // Round-AJ (this): round-AH's HiDPI handling (split cssViewport /
    // renderViewport, dpr cap at 2, ctx.scale, inline CSS dims) was
    // speculatively added after the user reported "blurry PDF" with
    // round-AG's simple scale:1.4. After round-AI's heal went live and
    // the user's "0/5 pages" badge confirmed the bridge succeeds, the
    // PDF.js canvas area is reported as "missing" — round-AH's changes
    // are the regression. Reverting to round-AG's simple rendering loop
    // (which was at least visible) and shipping a documented HiDPI
    // TODO for a future round-AK. We intentionally accept the round-
    // AG-quality "slightly blurry on retina" until we have an actual
    // reproduction in front of us.
    async function mountPdf(target, url, dotnetRef, contentId) {
        const sink = new EngagementSink(dotnetRef, contentId);
        const pdfjs = await ensurePdfJs();
        const doc = await pdfjs.getDocument({ url }).promise;
        sink.setTotal(doc.numPages);

        const canvasRoot = target;     // <div id="pdf-canvas-host">
        canvasRoot.innerHTML = '';     // clear once on mount

        let currentPage = 1;
        async function renderPage(p) {
            const page = await doc.getPage(p);
            const viewport = page.getViewport({ scale: 1.4 });
            const canvas = document.createElement('canvas');
            canvas.width = viewport.width;
            canvas.height = viewport.height;
            canvasRoot.appendChild(canvas);
            // Newest on top so the rendered page is the visible one.
            canvasRoot.scrollTop = canvasRoot.scrollHeight;
            const ctx = canvas.getContext('2d');
            await page.render({ canvasContext: ctx, viewport }).promise;
            // Page only counts as "viewed" once painted — a tab-switch
            // mid-render doesn't qualify.
            sink.markViewed(p);
            sink.sendNow();
        }
        await renderPage(1);

        // Wire next/prev buttons that the Razor page renders as SIBLINGS
        // of the host (`<div id="pdf-canvas-host">` and the
        // `<div class="btn-group">` share `#training-host` as their
        // immediate parent). The clicks bubble up to that wrapper, NOT
        // through the host div itself, so we attach the listener to
        // `target.parentElement` (`#training-host`) — earlier rounds
        // attached to `target` directly which silently dropped every
        // button click. See round-AH finding A.
        const navRoot = target.parentElement ?? target;
        navRoot.addEventListener('click', (ev) => {
            const t = ev.target.closest('button[data-pdf-action]');
            if (!t) return;
            const action = t.getAttribute('data-pdf-action');
            if (action === 'next' && currentPage < doc.numPages) {
                currentPage++;
                renderPage(currentPage);
            } else if (action === 'prev' && currentPage > 1) {
                currentPage--;
                renderPage(currentPage);
            } else if (action === 'jump' && t.hasAttribute('data-page')) {
                const p = parseInt(t.getAttribute('data-page'), 10);
                if (p >= 1 && p <= doc.numPages) {
                    currentPage = p;
                    renderPage(currentPage);
                }
            }
        });
    }

    function mountVideo(videoEl, dotnetRef, contentId) {
        const sink = new EngagementSink(dotnetRef, contentId);
        function onLoaded() {
            if (!isNaN(videoEl.duration) && isFinite(videoEl.duration)) {
                sink.setDuration(videoEl.duration);
            }
        }
        function onTimeUpdate() {
            if (document.hidden) return;
            sink.setHighest(videoEl.currentTime);
        }
        function onPause() { sink.sendNow(); }
        function onEnded() {
            sink.setHighest(videoEl.duration);
            sink.sendNow();
        }
        videoEl.addEventListener('loadedmetadata', onLoaded);
        videoEl.addEventListener('durationchange', onLoaded);
        videoEl.addEventListener('timeupdate', onTimeUpdate);
        videoEl.addEventListener('pause', onPause);
        videoEl.addEventListener('ended', onEnded);
        // Final flush on page unload via beforeunload (best-effort).
        window.addEventListener('beforeunload', () => sink.sendNow());
        videoEl.addEventListener('seeking', () => sink.sendNow());
    }

    // YouTube IFrame Player API: load on demand and poll getCurrentTime
    // every 5s. The API exposes its readiness as window.YT.Player but
    // only after the user-visible "enable js" iframe-onload. If the
    // iframe isn't ready, we tick every 5s so the dwell timer still
    // progresses (best-effort fallback).
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
        const sink = new EngagementSink(dotnetRef, contentId);
        await ensureYouTube();
        const player = new window.YT.Player(iframeEl, {
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

    window.ServantSyncTraining = {
        mountPdf,
        mountVideo,
        mountYouTube,
        mountBestEffortDwell,
        EngagementSink,
    };
})();
