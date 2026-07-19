// ─────────────────────────────────────────────────────────────────────
// ServantSync — responsive drawer toggle + resize watcher.
//
// The drawer is a plain <aside> controlled by the .ss-drawer-open
// CSS class on <body>.  No Blazor interop needed — works in static
// SSR before the SignalR circuit warms up.
//
// • ssToggleDrawer() — called by the hamburger <button onclick>.
//   Toggles .ss-drawer-open on <body>.
// • ssCloseDrawer() — called by MainLayout.OnLocationChanged to
//   close the drawer on mobile after navigation.
// • ssWatchDrawer() — (legacy, kept for compat) no-op now since
//   resize watching is handled by the pure-JS approach below.
// • Resize watcher auto-adds/removes .ss-drawer-open when the
//   viewport crosses the 600px breakpoint.
// ─────────────────────────────────────────────────────────────────────
(function () {
    'use strict';

    var BREAKPOINT_PX = 600;

    function isDesktop() {
        return window.innerWidth >= BREAKPOINT_PX;
    }

    function syncDrawerState() {
        if (isDesktop()) {
            document.body.classList.add('ss-drawer-open');
        } else {
            document.body.classList.remove('ss-drawer-open');
        }
    }

    // Initial sync — runs immediately when the script loads (before
    // DOMContentLoaded is over, so the <body> element exists).
    syncDrawerState();

    // ── Hamburger toggle ────────────────────────────────────────────
    window.ssToggleDrawer = function () {
        document.body.classList.toggle('ss-drawer-open');
    };

    // ── Close drawer (called from MainLayout after navigation) ──────
    window.ssCloseDrawer = function () {
        if (!isDesktop()) {
            document.body.classList.remove('ss-drawer-open');
        }
    };

    // ── Mobile overlay click-to-close ──────────────────────────────
    // The CSS ::after pseudo-element on body.ss-drawer-open acts as
    // the overlay.  We listen on document and close if the click
    // landed on the overlay (i.e. the body itself, not the drawer).
    document.addEventListener('click', function (e) {
        if (!document.body.classList.contains('ss-drawer-open')) return;
        // Only handle mobile; desktop doesn't have the overlay.
        if (isDesktop()) return;
        // If the click is inside the drawer or on the hamburger, bail.
        var drawer = document.querySelector('.ss-drawer');
        if (drawer && drawer.contains(e.target)) return;
        if (e.target.closest('.mud-icon-button')) return;
        // Click landed on the overlay or main content — close.
        document.body.classList.remove('ss-drawer-open');
    });

    // ── Resize watcher ──────────────────────────────────────────────
    // Debounce via requestAnimationFrame so rapid resize bursts
    // (device rotation, window snap) coalesce into one state sync.
    var pending = false;
    window.addEventListener('resize', function () {
        if (pending) return;
        pending = true;
        requestAnimationFrame(function () {
            pending = false;
            syncDrawerState();
        });
    });

    // ── Legacy stubs (called from OnAfterRenderAsync, now no-ops) ───
    window.ssWatchDrawer = function () { };
    window.ssUnwatchDrawer = function () { };
})();
