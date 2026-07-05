/* Easter-egg snake game for ServantSync.
   ─────────────────────────────────────
   Trigger: type "snake" anywhere on the app (case-insensitive, 5 chars),
   or open any page with ?snake=1 in the URL.
   Controls: arrow keys to move, SPACE to restart on game over, ESC to close.
   Designed to coexist peacefully with normal page input — single-char
   keys are buffered but never preventDefault'd when the game is closed.
   Self-contained: no Bootstrap JS, no Blazor interop. */

(function () {
    'use strict';

    // ─── Trigger detection ────────────────────────────────────────────
    const TRIGGER = 'snake';
    const TRIGGER_LEN = TRIGGER.length;
    let recent = '';
    let open = false;

    // Capture-phase listener so we see keys before any focused control
    // (a Modal dialog, a focused input, etc.) stops them.
    window.addEventListener('keydown', function (e) {
        // ESC closes from anywhere.
        if (e.key === 'Escape') {
            if (open) closeSnake();
            return;
        }
        if (open) {
            // Routed to the game when the modal is up.
            handleGameKey(e);
            return;
        }
        // Don't intercept keys used by modifier combinations or by
        // non-character keys like Tab, Enter, arrows (arrows don't reach
        // here anyway because the page doesn't scroll on them in a
        // Blazor Server shell, but we still check for safety).
        if (e.ctrlKey || e.altKey || e.metaKey) return;
        if (e.key.length !== 1) return;
        recent = (recent + e.key).slice(-TRIGGER_LEN).toLowerCase();
        if (recent === TRIGGER) {
            e.preventDefault();
            openSnake();
        }
    }, true);

    // ─── Game state (closure-scoped) ──────────────────────────────────
    const GRID = 20;        // 20×20 cells
    const CELL = 20;        // 20px per cell → 400×400 canvas
    const TICK_MS = 110;    // classic snake cadence
    let snake, food, dir, nextDir, score, alive, tickHandle, canvas, ctx, maxLen;

    function start() {
        snake = [{ x: 10, y: 10 }, { x: 9, y: 10 }, { x: 8, y: 10 }];
        food = randomEmptyCell();
        dir = { x: 1, y: 0 };
        nextDir = dir;
        score = 0;
        alive = true;
        maxLen = 3;
        draw();
        if (tickHandle) clearInterval(tickHandle);
        tickHandle = setInterval(tick, TICK_MS);
        // tickHandle is now a real timer ID; the draw() game-over
        // guard relies on this being non-null AND being explicitly
        // nulled in die() / closeSnake() — see those functions.
    }

    function tick() {
        if (!alive) return;
        // Direction is buffered (nextDir) so a user can't reverse into
        // themselves mid-tick by hitting ArrowDown while moving Up.
        dir = nextDir;
        const head = snake[0];
        const nx = head.x + dir.x;
        const ny = head.y + dir.y;
        // Wall collision
        if (nx < 0 || ny < 0 || nx >= GRID || ny >= GRID) return die();
        // Self collision (skip the tail because it moves out of the way)
        for (let i = 0; i < snake.length - 1; i++) {
            if (snake[i].x === nx && snake[i].y === ny) return die();
        }
        snake.unshift({ x: nx, y: ny });
        if (nx === food.x && ny === food.y) {
            score++;
            maxLen = Math.max(maxLen, snake.length);
            placeFood();
        } else {
            snake.pop();
        }
        draw();
    }

    function die() {
        alive = false;
        // clearInterval cancels the timer but does NOT null the
        // variable — the timer id stays as a non-null number.
        // Explicitly nulling lets the draw() game-over guard work.
        if (tickHandle) clearInterval(tickHandle);
        tickHandle = null;
        draw();
    }

    function placeFood() {
        food = randomEmptyCell();
    }

    function randomEmptyCell() {
        // Build a list of free cells and pick one. Avoids the infinite
        // loop you'd get from rejection sampling once the snake fills
        // most of the grid; max possible snake length = 397 (minus 3
        // starting cells; we keep the food on one of the free cells).
        const occupied = new Set(snake.map(s => s.x + ',' + s.y));
        const free = [];
        for (let x = 0; x < GRID; x++) {
            for (let y = 0; y < GRID; y++) {
                if (!occupied.has(x + ',' + y)) free.push({ x: x, y: y });
            }
        }
        if (free.length === 0) {
            // Whole board filled — win state.
            die();
            return snake[0]; // any cell; nothing more will be drawn
        }
        return free[Math.floor(Math.random() * free.length)];
    }

    function draw() {
        ctx.fillStyle = '#111';
        ctx.fillRect(0, 0, canvas.width, canvas.height);

        // Faint grid
        ctx.strokeStyle = '#1d1d1d';
        ctx.lineWidth = 1;
        for (let i = 1; i < GRID; i++) {
            ctx.beginPath();
            ctx.moveTo(i * CELL + 0.5, 0);
            ctx.lineTo(i * CELL + 0.5, canvas.height);
            ctx.stroke();
            ctx.beginPath();
            ctx.moveTo(0, i * CELL + 0.5);
            ctx.lineTo(canvas.width, i * CELL + 0.5);
            ctx.stroke();
        }

        // Food
        ctx.fillStyle = '#f44';
        ctx.fillRect(food.x * CELL + 2, food.y * CELL + 2, CELL - 4, CELL - 4);

        // Snake (head brighter than body)
        for (let i = snake.length - 1; i >= 0; i--) {
            const seg = snake[i];
            ctx.fillStyle = i === 0 ? '#5f5' : '#0c0';
            ctx.fillRect(seg.x * CELL + 1, seg.y * CELL + 1, CELL - 2, CELL - 2);
        }

        // HUD
        const scoreEl = document.getElementById('snake-score');
        const maxEl = document.getElementById('snake-max');
        if (scoreEl) scoreEl.textContent = String(score);
        if (maxEl) maxEl.textContent = String(maxLen);

        // Game over overlay. tickHandle is explicitly set to null in
        // die() so this draw-time guard stays meaningful — clearInterval
        // alone wouldn't null the timer-id variable, leaving the dual
        // condition `!alive && tickHandle === null` permanently false.
        if (!alive && !tickHandle) {
            ctx.fillStyle = 'rgba(0,0,0,0.7)';
            ctx.fillRect(0, 0, canvas.width, canvas.height);
            ctx.fillStyle = '#0f0';
            ctx.font = '22px ui-monospace, monospace';
            ctx.textAlign = 'center';
            ctx.fillText('GAME OVER', canvas.width / 2, canvas.height / 2 - 10);
            ctx.font = '12px ui-monospace, monospace';
            ctx.fillText('Press SPACE to restart or ESC to close',
                canvas.width / 2, canvas.height / 2 + 18);
            ctx.textAlign = 'start';
        }
    }

    function handleGameKey(e) {
        // Direction keys: ignore 180° flips (yields self-collision next tick).
        if (e.key === 'ArrowUp' && dir.y === 0) { nextDir = { x: 0, y: -1 }; e.preventDefault(); return; }
        if (e.key === 'ArrowDown' && dir.y === 0) { nextDir = { x: 0, y: 1 }; e.preventDefault(); return; }
        if (e.key === 'ArrowLeft' && dir.x === 0) { nextDir = { x: -1, y: 0 }; e.preventDefault(); return; }
        if (e.key === 'ArrowRight' && dir.x === 0) { nextDir = { x: 1, y: 0 }; e.preventDefault(); return; }
        // SPACE restarts on death.
        if (e.key === ' ' && !alive) { e.preventDefault(); start(); return; }
        // All other keys: just consume so they don't bubble to the page
        // (e.g. space scrolling, single-letter keystrokes that would
        // refill the trigger buffer if we let them through).
        if (e.key === ' ' || e.key.startsWith('Arrow')) e.preventDefault();
    }

    // ─── Mount / unmount ──────────────────────────────────────────────
    function openSnake() {
        if (open) return;
        open = true;
        recent = '';  // reset the trigger buffer once it fires.

        const modal = document.createElement('div');
        modal.id = 'snake-modal';
        modal.setAttribute('role', 'dialog');
        modal.setAttribute('aria-label', 'Snake game easter egg');
        modal.innerHTML =
            '<div id="snake-card">' +
              '<canvas id="snake-canvas" width="400" height="400"></canvas>' +
              '<div id="snake-hud">Score <span id="snake-score">0</span> · Max <span id="snake-max">3</span></div>' +
              '<div id="snake-help">↑ ↓ ← → to move · SPACE restart · ESC close</div>' +
            '</div>';
        document.body.appendChild(modal);
        canvas = modal.querySelector('#snake-canvas');
        ctx = canvas.getContext('2d');
        start();
    }

    function closeSnake() {
        if (!open) return;
        open = false;
        if (tickHandle) {
            clearInterval(tickHandle);
        }
        // Idempotent reset of every per-game state so a future open
        // starts from a known-clean baseline.
        tickHandle = null;
        alive = false;
        snake = food = dir = nextDir = null;
        score = maxLen = 0;
        const m = document.getElementById('snake-modal');
        if (m) m.remove();
    }

    // ─── URL fallback (?snake=1) — useful for mobile / no-keyboard ────
    if (new URLSearchParams(window.location.search).has('snake')) {
        const startWhenReady = function () { openSnake(); };
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', startWhenReady, { once: true });
        } else {
            startWhenReady();
        }
    }
})();
