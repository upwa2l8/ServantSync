// ─────────────────────────────────────────────────────────────────────
// ServantSync — responsive drawer resize watcher.
// Reports viewport breakpoint crossings (>= 600px desktop, <600px
// mobile) to the .NET callback so the Razor layout's _drawerOpen
// state stays in sync if the user rotates the device or resizes the
// window. Uses Debounce via requestAnimationFrame to coalesce resize
// bursts into a single callback per frame, and only fires when the
// breakpoint actually changes (not on every pixel of pixel-width
// change).
//
// Wired by Components/Layout/MainLayout.razor's OnAfterRenderAsync,
// which captures a DotNetObjectReference and passes it here. The
// matching [JSInvokable] method OnBreakpointChanged(bool isDesktop)
// receives the state.
// ─────────────────────────────────────────────────────────────────────
(function () {
    'use strict';

    const BREAKPOINT_PX = 600; // matches DrawerVariant.Responsive + Breakpoint.Sm in MainLayout.razor
    // Map of DotNetObjectReference -> bound resize handler. Used both
    // for idempotency (ssWatchDrawer refuses to subscribe twice for
    // the same ref) and for cleanup (ssUnwatchDrawer reads from this
    // single source of truth instead of stashing a property on the
    // DotNetObjectReference object itself).
    const handlers = new Map();

    function makeHandler(dotnetRef) {
        let lastIsDesktop = window.innerWidth >= BREAKPOINT_PX;
        let pending = false;
        return function () {
            if (pending) return;
            pending = true;
            requestAnimationFrame(function () {
                pending = false;
                const isDesktop = window.innerWidth >= BREAKPOINT_PX;
                if (isDesktop === lastIsDesktop) return;
                lastIsDesktop = isDesktop;
                dotnetRef.invokeMethodAsync('OnBreakpointChanged', isDesktop)
                    .catch(function () { /* circuit dropped; safe to ignore */ });
            });
        };
    }

    window.ssWatchDrawer = function (dotnetRef) {
        if (!dotnetRef || handlers.has(dotnetRef)) return;
        const handler = makeHandler(dotnetRef);
        handlers.set(dotnetRef, handler);
        window.addEventListener('resize', handler);
    };

    window.ssUnwatchDrawer = function (dotnetRef) {
        if (!dotnetRef) return;
        const handler = handlers.get(dotnetRef);
        if (handler) {
            window.removeEventListener('resize', handler);
            handlers.delete(dotnetRef);
        }
    };
})();
