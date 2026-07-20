/* ──────────────────────────────────────────────────────────────────────
   Fish Fling – Yeti Sports-inspired easter-egg game for ServantSync.
   ──────────────────────────────────────────────────────────────────────
   Trigger: type "fish" anywhere on the app (case-insensitive, 4 chars),
   or open any page with ?fish=1 in the URL.
   Controls: CLICK / TAP to launch, SPACE to restart, ESC to close.
   A polar bear flings fish at other polar bears who are hungry for dinner.
   ────────────────────────────────────────────────────────────────────── */

(function () {
    'use strict';

    // ─── Trigger detection ────────────────────────────────────────────
    const TRIGGER = 'fish';
    const TRIGGER_LEN = TRIGGER.length;
    let recent = '';
    let open = false;

    window.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') {
            if (open) closeFishFling();
            return;
        }
        if (open) {
            handleGameKey(e);
            return;
        }
        if (e.ctrlKey || e.altKey || e.metaKey) return;
        if (e.key.length !== 1) return;
        recent = (recent + e.key).slice(-TRIGGER_LEN).toLowerCase();
        if (recent === TRIGGER) {
            e.preventDefault();
            openFishFling();
        }
    }, true);

    // ─── Constants ────────────────────────────────────────────────────
    const W = 800, H = 460;
    const GROUND_Y = 380;       // ground level
    const GRAVITY = 0.18;
    const MAX_ROUNDS = 12;
    const MISS_LIMIT = 4;       // game over after 4 consecutive misses
    const MS_PER_FRAME = 1000 / 60; // reference frame time (60 fps)

    // Fish types: name, size radius, base points, color, launch power factor
    const FISH_TYPES = [
        { name: 'Sardine',  r: 8,  pts: 10,  color: '#7ec8e3', power: 1.15, emoji: '🐟' },
        { name: 'Salmon',   r: 12, pts: 25,  color: '#f08080', power: 1.0,  emoji: '🐠' },
        { name: 'Tuna',     r: 16, pts: 50,  color: '#4682b4', power: 0.82, emoji: '🐡' },
        { name: 'Golden',   r: 10, pts: 100, color: '#ffd700', power: 1.05, emoji: '✨' },
    ];

    // ─── Game state ───────────────────────────────────────────────────
    let canvas, ctx, animId;
    let phase, score, round, combo, consecutiveMisses, bestScore;
    let currentFish, fishX, fishY, fishVx, fishVy, fishAngle, fishSpin;
    let power, powerDir, aim, aimDir;
    let targets;         // array of { x, y, caught, expression }
    let particles;       // snow + splash + sparkle
    let messages;        // floating score messages
    let bgStars;         // background snowflakes
    let snowBumps;       // pre-computed snow bump positions
    let lastTime;        // previous rAF timestamp (ms)
    let launchX, launchY; // launcher position
    let pendingTimers = []; // track timeouts for cleanup on close

    // ─── Phases ───────────────────────────────────────────────────────
    const PH = {
        TITLE: 0, POWER: 1, AIM: 2, FLYING: 3,
        CAUGHT: 4, MISSED: 5, GAMEOVER: 6
    };

    // ─── Helpers ──────────────────────────────────────────────────────
    function rand(a, b) { return Math.random() * (b - a) + a; }
    function randInt(a, b) { return Math.floor(rand(a, b + 1)); }
    function clamp(v, lo, hi) { return Math.max(lo, Math.min(hi, v)); }

    function pickFish() {
        // Weighted random: common fish more likely, golden rare
        const weights = [40, 30, 20, 10];
        let total = weights.reduce((a, b) => a + b, 0);
        let r = Math.random() * total;
        for (let i = 0; i < weights.length; i++) {
            r -= weights[i];
            if (r <= 0) return { ...FISH_TYPES[i] };
        }
        return { ...FISH_TYPES[0] };
    }

    // ─── Targets (hungry polar bears) ─────────────────────────────────
    function createTargets() {
        const ts = [];
        const count = randInt(3, 5);
        const minDist = 200, maxDist = 700;
        const spacing = (maxDist - minDist) / count;
        for (let i = 0; i < count; i++) {
            const x = minDist + spacing * i + rand(-30, 30);
            const y = GROUND_Y;
            const size = rand(0.7, 1.1);  // scale variation
            const posture = randInt(0, 2); // 0=standing, 1=sitting, 2=lying
            ts.push({
                x: clamp(x, minDist, maxDist),
                y: y,
                size: size,
                posture: posture,
                caught: false,
                expression: 'hungry', // hungry, happy, sad, eating
                catchAnim: 0,
                mouthOpen: 0,
            });
        }
        // Sort by x so closer bears render first (painter's algorithm)
        ts.sort((a, b) => a.x - b.x);
        return ts;
    }

    // ─── Particles ────────────────────────────────────────────────────
    function initBgSnow() {
        bgStars = [];
        for (let i = 0; i < 60; i++) {
            bgStars.push({
                x: rand(0, W), y: rand(0, GROUND_Y - 20),
                r: rand(1, 3), speed: rand(0.2, 0.8),
                drift: rand(-0.3, 0.3), opacity: rand(0.3, 0.7)
            });
        }
    }

    function initSnowBumps() {
        snowBumps = [];
        for (let x = 20; x < W; x += 60) {
            snowBumps.push({ x: x, w: 15 + (x * 7 % 15), h: 4 + (x * 3 % 4) });
        }
    }

    function spawnSplash(x, y, color, count) {
        for (let i = 0; i < count; i++) {
            const angle = rand(-Math.PI, 0);
            const speed = rand(1, 4);
            particles.push({
                x: x, y: y,
                vx: Math.cos(angle) * speed,
                vy: Math.sin(angle) * speed - rand(0, 2),
                life: 1, decay: rand(0.015, 0.035),
                r: rand(2, 5), color: color,
                type: 'splash'
            });
        }
    }

    function spawnSparkles(x, y, count) {
        for (let i = 0; i < count; i++) {
            const angle = rand(0, Math.PI * 2);
            const speed = rand(1, 5);
            particles.push({
                x: x, y: y,
                vx: Math.cos(angle) * speed,
                vy: Math.sin(angle) * speed,
                life: 1, decay: rand(0.02, 0.04),
                r: rand(2, 4), color: '#ffd700',
                type: 'sparkle'
            });
        }
    }

    function spawnScoreMsg(x, y, text, color) {
        messages.push({
            x: x, y: y, text: text, color: color,
            life: 1, decay: 0.012, vy: -1.5
        });
    }

    // ─── Drawing helpers ──────────────────────────────────────────────

    function drawSky() {
        // Gradient sky
        const grad = ctx.createLinearGradient(0, 0, 0, GROUND_Y);
        grad.addColorStop(0, '#0a1628');
        grad.addColorStop(0.4, '#162544');
        grad.addColorStop(0.7, '#1e3a5f');
        grad.addColorStop(1, '#2a5a8f');
        ctx.fillStyle = grad;
        ctx.fillRect(0, 0, W, GROUND_Y);

        // Aurora borealis hint
        ctx.save();
        ctx.globalAlpha = 0.08;
        const aGrad = ctx.createLinearGradient(0, 40, 0, 180);
        aGrad.addColorStop(0, '#00ff88');
        aGrad.addColorStop(0.5, '#00ccff');
        aGrad.addColorStop(1, 'transparent');
        ctx.fillStyle = aGrad;
        ctx.fillRect(0, 40, W, 140);
        ctx.restore();
    }

    function drawMountains() {
        // Far mountains
        ctx.fillStyle = '#1a2d4a';
        ctx.beginPath();
        ctx.moveTo(0, GROUND_Y);
        const peaks = [0, 80, 160, 280, 400, 520, 620, 720, 800];
        const heights = [60, 100, 70, 120, 80, 110, 65, 90, 70];
        for (let i = 0; i < peaks.length; i++) {
            ctx.lineTo(peaks[i], GROUND_Y - heights[i]);
        }
        ctx.lineTo(W, GROUND_Y);
        ctx.closePath();
        ctx.fill();

        // Snow caps
        ctx.fillStyle = '#3a5a8a';
        ctx.beginPath();
        ctx.moveTo(0, GROUND_Y);
        for (let i = 0; i < peaks.length; i++) {
            ctx.lineTo(peaks[i], GROUND_Y - heights[i] * 0.6);
        }
        ctx.lineTo(W, GROUND_Y);
        ctx.closePath();
        ctx.fill();
    }

    function drawGround() {
        // Snow ground
        const gGrad = ctx.createLinearGradient(0, GROUND_Y, 0, H);
        gGrad.addColorStop(0, '#b8d4e8');
        gGrad.addColorStop(0.3, '#a0c4d8');
        gGrad.addColorStop(1, '#7898b0');
        ctx.fillStyle = gGrad;
        ctx.fillRect(0, GROUND_Y, W, H - GROUND_Y);

        // Snow surface highlight
        ctx.strokeStyle = 'rgba(255,255,255,0.4)';
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(0, GROUND_Y + 1);
        ctx.lineTo(W, GROUND_Y + 1);
        ctx.stroke();

        // Snow bumps (pre-computed, no per-frame randomness)
        ctx.fillStyle = 'rgba(255,255,255,0.15)';
        for (const b of snowBumps) {
            ctx.beginPath();
            ctx.ellipse(b.x, GROUND_Y + 8, b.w, b.h, 0, 0, Math.PI * 2);
            ctx.fill();
        }
    }

    function drawSnow() {
        for (const s of bgStars) {
            ctx.globalAlpha = s.opacity;
            ctx.fillStyle = '#fff';
            ctx.beginPath();
            ctx.arc(s.x, s.y, s.r, 0, Math.PI * 2);
            ctx.fill();
        }
        ctx.globalAlpha = 1;
    }

    function drawPolarBear(x, y, scale, posture, expression, mouthOpen) {
        ctx.save();
        ctx.translate(x, y);
        ctx.scale(scale, scale);

        const s = posture === 0 ? 1 : posture === 1 ? 0.85 : 0.65;
        ctx.scale(1, s);

        // Body
        ctx.fillStyle = '#f0f0f0';
        ctx.beginPath();
        ctx.ellipse(0, -30, 28, 30, 0, 0, Math.PI * 2);
        ctx.fill();
        ctx.strokeStyle = '#ddd';
        ctx.lineWidth = 1;
        ctx.stroke();

        // Head
        ctx.fillStyle = '#f5f5f5';
        ctx.beginPath();
        ctx.arc(0, -65, 18, 0, Math.PI * 2);
        ctx.fill();
        ctx.strokeStyle = '#ddd';
        ctx.stroke();

        // Ears
        ctx.fillStyle = '#e8e8e8';
        ctx.beginPath();
        ctx.arc(-14, -80, 6, 0, Math.PI * 2);
        ctx.fill();
        ctx.beginPath();
        ctx.arc(14, -80, 6, 0, Math.PI * 2);
        ctx.fill();
        // Inner ear
        ctx.fillStyle = '#f8c8d0';
        ctx.beginPath();
        ctx.arc(-14, -80, 3, 0, Math.PI * 2);
        ctx.fill();
        ctx.beginPath();
        ctx.arc(14, -80, 3, 0, Math.PI * 2);
        ctx.fill();

        // Eyes
        ctx.fillStyle = '#222';
        if (expression === 'happy' || expression === 'eating') {
            // Happy curved eyes
            ctx.lineWidth = 2;
            ctx.strokeStyle = '#222';
            ctx.beginPath();
            ctx.arc(-7, -68, 4, Math.PI, 0);
            ctx.stroke();
            ctx.beginPath();
            ctx.arc(7, -68, 4, Math.PI, 0);
            ctx.stroke();
        } else if (expression === 'sad') {
            // Sad eyes
            ctx.beginPath();
            ctx.arc(-7, -66, 3, 0, Math.PI * 2);
            ctx.fill();
            ctx.beginPath();
            ctx.arc(7, -66, 3, 0, Math.PI * 2);
            ctx.fill();
            // Tear
            ctx.fillStyle = '#88ccff';
            ctx.beginPath();
            ctx.ellipse(-5, -62, 1.5, 3, 0, 0, Math.PI * 2);
            ctx.fill();
        } else {
            // Hungry / normal eyes
            ctx.beginPath();
            ctx.arc(-7, -67, 3, 0, Math.PI * 2);
            ctx.fill();
            ctx.beginPath();
            ctx.arc(7, -67, 3, 0, Math.PI * 2);
            ctx.fill();
            // Pupils
            ctx.fillStyle = '#fff';
            ctx.beginPath();
            ctx.arc(-6, -68, 1, 0, Math.PI * 2);
            ctx.fill();
            ctx.beginPath();
            ctx.arc(8, -68, 1, 0, Math.PI * 2);
            ctx.fill();
        }

        // Nose
        ctx.fillStyle = '#333';
        ctx.beginPath();
        ctx.ellipse(0, -62, 4, 3, 0, 0, Math.PI * 2);
        ctx.fill();

        // Mouth
        if (mouthOpen > 0) {
            ctx.fillStyle = '#d44';
            ctx.beginPath();
            ctx.ellipse(0, -56, 6 * mouthOpen, 4 * mouthOpen, 0, 0, Math.PI * 2);
            ctx.fill();
            // Teeth
            if (mouthOpen > 0.5) {
                ctx.fillStyle = '#fff';
                ctx.fillRect(-4, -58, 2, 3);
                ctx.fillRect(2, -58, 2, 3);
            }
        } else if (expression === 'hungry') {
            // Open mouth anticipation
            ctx.fillStyle = '#d44';
            ctx.beginPath();
            ctx.ellipse(0, -56, 5, 3, 0, 0, Math.PI * 2);
            ctx.fill();
        } else if (expression === 'happy') {
            // Smile
            ctx.strokeStyle = '#555';
            ctx.lineWidth = 1.5;
            ctx.beginPath();
            ctx.arc(0, -60, 6, 0.2, Math.PI - 0.2);
            ctx.stroke();
        } else if (expression === 'sad') {
            // Frown
            ctx.strokeStyle = '#555';
            ctx.lineWidth = 1.5;
            ctx.beginPath();
            ctx.arc(0, -52, 5, Math.PI + 0.3, -0.3);
            ctx.stroke();
        }

        // Arms/paws
        ctx.fillStyle = '#e8e8e8';
        if (posture === 0) {
            // Standing - arms at sides
            ctx.beginPath();
            ctx.ellipse(-26, -35, 8, 12, 0.3, 0, Math.PI * 2);
            ctx.fill();
            ctx.beginPath();
            ctx.ellipse(26, -35, 8, 12, -0.3, 0, Math.PI * 2);
            ctx.fill();
        } else if (posture === 1) {
            // Sitting - arms forward (ready to catch!)
            ctx.beginPath();
            ctx.ellipse(-22, -30, 8, 10, 0.5, 0, Math.PI * 2);
            ctx.fill();
            ctx.beginPath();
            ctx.ellipse(22, -30, 8, 10, -0.5, 0, Math.PI * 2);
            ctx.fill();
        }

        // Feet
        ctx.fillStyle = '#e0e0e0';
        ctx.beginPath();
        ctx.ellipse(-12, -2, 10, 5, 0, 0, Math.PI * 2);
        ctx.fill();
        ctx.beginPath();
        ctx.ellipse(12, -2, 10, 5, 0, 0, Math.PI * 2);
        ctx.fill();

        ctx.restore();
    }

    function drawLauncherBear(x, y, holdingFish) {
        ctx.save();
        ctx.translate(x, y);

        // Body
        ctx.fillStyle = '#f0f0f0';
        ctx.beginPath();
        ctx.ellipse(0, -30, 32, 34, 0, 0, Math.PI * 2);
        ctx.fill();
        ctx.strokeStyle = '#ddd';
        ctx.lineWidth = 1;
        ctx.stroke();

        // Head (slightly bigger, determined expression)
        ctx.fillStyle = '#f5f5f5';
        ctx.beginPath();
        ctx.arc(0, -70, 22, 0, Math.PI * 2);
        ctx.fill();
        ctx.strokeStyle = '#ddd';
        ctx.stroke();

        // Ears
        ctx.fillStyle = '#e8e8e8';
        ctx.beginPath();
        ctx.arc(-16, -88, 7, 0, Math.PI * 2);
        ctx.fill();
        ctx.beginPath();
        ctx.arc(16, -88, 7, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = '#f8c8d0';
        ctx.beginPath();
        ctx.arc(-16, -88, 3.5, 0, Math.PI * 2);
        ctx.fill();
        ctx.beginPath();
        ctx.arc(16, -88, 3.5, 0, Math.PI * 2);
        ctx.fill();

        // Determined eyebrows
        ctx.strokeStyle = '#555';
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(-12, -78);
        ctx.lineTo(-5, -76);
        ctx.stroke();
        ctx.beginPath();
        ctx.moveTo(12, -78);
        ctx.lineTo(5, -76);
        ctx.stroke();

        // Eyes (focused)
        ctx.fillStyle = '#222';
        ctx.beginPath();
        ctx.arc(-8, -72, 3.5, 0, Math.PI * 2);
        ctx.fill();
        ctx.beginPath();
        ctx.arc(8, -72, 3.5, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = '#fff';
        ctx.beginPath();
        ctx.arc(-7, -73, 1.2, 0, Math.PI * 2);
        ctx.fill();
        ctx.beginPath();
        ctx.arc(9, -73, 1.2, 0, Math.PI * 2);
        ctx.fill();

        // Nose
        ctx.fillStyle = '#333';
        ctx.beginPath();
        ctx.ellipse(0, -66, 5, 3.5, 0, 0, Math.PI * 2);
        ctx.fill();

        // Mouth - determined grin
        ctx.strokeStyle = '#555';
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        ctx.arc(0, -62, 7, 0.1, Math.PI - 0.1);
        ctx.stroke();

        // Arms holding fish
        if (holdingFish) {
            // Arms extended forward holding fish
            ctx.fillStyle = '#e8e8e8';
            ctx.beginPath();
            ctx.ellipse(-24, -45, 9, 14, 0.6, 0, Math.PI * 2);
            ctx.fill();
            ctx.beginPath();
            ctx.ellipse(24, -45, 9, 14, -0.6, 0, Math.PI * 2);
            ctx.fill();

            // Fish in mouth (classic Yeti Sports style)
            drawFishSprite(32, -68, currentFish, 0.8);
        } else {
            // Arms at rest
            ctx.fillStyle = '#e8e8e8';
            ctx.beginPath();
            ctx.ellipse(-28, -38, 9, 14, 0.3, 0, Math.PI * 2);
            ctx.fill();
            ctx.beginPath();
            ctx.ellipse(28, -38, 9, 14, -0.3, 0, Math.PI * 2);
            ctx.fill();
        }

        // Feet
        ctx.fillStyle = '#e0e0e0';
        ctx.beginPath();
        ctx.ellipse(-14, -2, 12, 6, 0, 0, Math.PI * 2);
        ctx.fill();
        ctx.beginPath();
        ctx.ellipse(14, -2, 12, 6, 0, 0, Math.PI * 2);
        ctx.fill();

        ctx.restore();
    }

    function drawFishSprite(x, y, fish, scale) {
        ctx.save();
        ctx.translate(x, y);
        ctx.scale(scale, scale);
        ctx.rotate(fishAngle || 0);

        // Body
        ctx.fillStyle = fish.color;
        ctx.beginPath();
        ctx.ellipse(0, 0, fish.r * 1.5, fish.r, 0, 0, Math.PI * 2);
        ctx.fill();

        // Tail
        ctx.beginPath();
        ctx.moveTo(-fish.r * 1.3, 0);
        ctx.lineTo(-fish.r * 2.2, -fish.r * 0.8);
        ctx.lineTo(-fish.r * 2.2, fish.r * 0.8);
        ctx.closePath();
        ctx.fill();

        // Eye
        ctx.fillStyle = '#fff';
        ctx.beginPath();
        ctx.arc(fish.r * 0.6, -fish.r * 0.2, fish.r * 0.35, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = '#111';
        ctx.beginPath();
        ctx.arc(fish.r * 0.7, -fish.r * 0.2, fish.r * 0.18, 0, Math.PI * 2);
        ctx.fill();

        // Belly highlight
        ctx.fillStyle = 'rgba(255,255,255,0.3)';
        ctx.beginPath();
        ctx.ellipse(0, fish.r * 0.25, fish.r * 1.1, fish.r * 0.4, 0, 0, Math.PI * 2);
        ctx.fill();

        // Fin
        ctx.fillStyle = fish.color;
        ctx.globalAlpha = 0.7;
        ctx.beginPath();
        ctx.ellipse(0, -fish.r * 0.7, fish.r * 0.5, fish.r * 0.4, -0.3, 0, Math.PI * 2);
        ctx.fill();
        ctx.globalAlpha = 1;

        ctx.restore();
    }

    function drawFlyingFish() {
        if (phase !== PH.FLYING) return;

        // Trail effect
        ctx.save();
        ctx.globalAlpha = 0.3;
        for (let i = 0; i < 5; i++) {
            const tx = fishX - fishVx * i * 2;
            const ty = fishY - fishVy * i * 2;
            ctx.globalAlpha = 0.15 - i * 0.03;
            drawFishSprite(tx, ty, currentFish, 0.7 - i * 0.1);
        }
        ctx.restore();

        // Main fish
        drawFishSprite(fishX, fishY, currentFish, 1);
    }

    function drawPowerMeter() {
        if (phase !== PH.POWER) return;

        const mx = 50, my = H - 60;
        const mw = 150, mh = 16;

        // Background
        ctx.fillStyle = 'rgba(0,0,0,0.6)';
        ctx.fillRect(mx - 5, my - 25, mw + 10, mh + 40);

        // Label
        ctx.fillStyle = '#fff';
        ctx.font = 'bold 11px ui-monospace, monospace';
        ctx.textAlign = 'center';
        ctx.fillText('POWER', mx + mw / 2, my - 10);

        // Bar background
        ctx.fillStyle = '#333';
        ctx.fillRect(mx, my, mw, mh);

        // Bar fill (oscillating)
        const fillW = mw * power;
        const pGrad = ctx.createLinearGradient(mx, 0, mx + mw, 0);
        pGrad.addColorStop(0, '#44ff44');
        pGrad.addColorStop(0.5, '#ffff44');
        pGrad.addColorStop(1, '#ff4444');
        ctx.fillStyle = pGrad;
        ctx.fillRect(mx, my, fillW, mh);

        // Marker
        ctx.fillStyle = '#fff';
        ctx.fillRect(mx + fillW - 1, my - 3, 2, mh + 6);

        // Sweet spot indicator (60-85%)
        ctx.strokeStyle = 'rgba(255,255,255,0.5)';
        ctx.lineWidth = 1;
        ctx.setLineDash([3, 3]);
        ctx.strokeRect(mx + mw * 0.55, my - 2, mw * 0.3, mh + 4);
        ctx.setLineDash([]);

        ctx.textAlign = 'start';
    }

    function drawAimMeter() {
        if (phase !== PH.AIM) return;

        const cx = launchX, cy = launchY - 80;
        const radius = 60;

        // Arc background
        ctx.strokeStyle = 'rgba(255,255,255,0.2)';
        ctx.lineWidth = 8;
        ctx.beginPath();
        ctx.arc(cx, cy, radius, -Math.PI, 0);
        ctx.stroke();

        // Arc fill showing current angle
        const angleNorm = aim; // 0 to 1 maps to -PI to 0
        const aColor = aim > 0.3 && aim < 0.7 ? '#44ff44' : '#ffaa44';
        ctx.strokeStyle = aColor;
        ctx.lineWidth = 6;
        ctx.beginPath();
        ctx.arc(cx, cy, radius, -Math.PI, -Math.PI + Math.PI * angleNorm);
        ctx.stroke();

        // Direction arrow
        const arrowAngle = -Math.PI + Math.PI * aim;
        const arrowLen = 40;
        const ax = cx + Math.cos(arrowAngle) * (radius + 15);
        const ay = cy + Math.sin(arrowAngle) * (radius + 15);
        const tipX = cx + Math.cos(arrowAngle) * (radius + 15 + arrowLen);
        const tipY = cy + Math.sin(arrowAngle) * (radius + 15 + arrowLen);

        ctx.strokeStyle = '#fff';
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(ax, ay);
        ctx.lineTo(tipX, tipY);
        ctx.stroke();

        // Arrowhead
        const headLen = 8;
        ctx.fillStyle = '#fff';
        ctx.beginPath();
        ctx.moveTo(tipX, tipY);
        ctx.lineTo(tipX - headLen * Math.cos(arrowAngle - 0.4), tipY - headLen * Math.sin(arrowAngle - 0.4));
        ctx.lineTo(tipX - headLen * Math.cos(arrowAngle + 0.4), tipY - headLen * Math.sin(arrowAngle + 0.4));
        ctx.closePath();
        ctx.fill();

        // Label
        ctx.fillStyle = '#fff';
        ctx.font = 'bold 11px ui-monospace, monospace';
        ctx.textAlign = 'center';
        ctx.fillText('ANGLE', cx, cy - radius - 15);
        ctx.textAlign = 'start';
    }

    function drawHUD() {
        // Score panel
        ctx.fillStyle = 'rgba(0,0,0,0.6)';
        ctx.fillRect(W - 180, 8, 172, 70);
        ctx.strokeStyle = 'rgba(255,255,255,0.15)';
        ctx.lineWidth = 1;
        ctx.strokeRect(W - 180, 8, 172, 70);

        ctx.fillStyle = '#fff';
        ctx.font = 'bold 13px ui-monospace, monospace';
        ctx.textAlign = 'right';
        ctx.fillText('SCORE', W - 18, 28);
        ctx.font = 'bold 22px ui-monospace, monospace';
        ctx.fillStyle = '#ffd700';
        ctx.fillText(String(score), W - 18, 52);

        ctx.font = '11px ui-monospace, monospace';
        ctx.fillStyle = '#aaa';
        ctx.fillText('Round ' + round + '/' + MAX_ROUNDS, W - 18, 68);

        // Combo indicator
        if (combo > 1) {
            ctx.fillStyle = '#ff6644';
            ctx.font = 'bold 14px ui-monospace, monospace';
            ctx.fillText('COMBO x' + combo + '!', W - 18, 86);
        }

        // Fish type indicator (bottom left)
        ctx.fillStyle = 'rgba(0,0,0,0.5)';
        ctx.fillRect(8, H - 32, 140, 24);
        ctx.font = '11px ui-monospace, monospace';
        ctx.fillStyle = currentFish.color;
        ctx.textAlign = 'left';
        ctx.fillText(currentFish.emoji + ' ' + currentFish.name + ' (' + currentFish.pts + 'pts)', 14, H - 15);

        // Miss counter
        ctx.fillStyle = 'rgba(0,0,0,0.5)';
        ctx.fillRect(W / 2 - 60, 8, 120, 24);
        ctx.font = '11px ui-monospace, monospace';
        ctx.textAlign = 'center';
        const misses = '❤️'.repeat(MISS_LIMIT - consecutiveMisses) + '🖤'.repeat(consecutiveMisses);
        ctx.fillText(misses, W / 2, 24);

        ctx.textAlign = 'start';
    }

    function drawTitle() {
        ctx.fillStyle = 'rgba(0,0,0,0.7)';
        ctx.fillRect(0, 0, W, H);

        ctx.textAlign = 'center';

        // Title
        ctx.fillStyle = '#4fc3f7';
        ctx.font = 'bold 48px ui-monospace, monospace';
        ctx.fillText('🐟 FISH FLING 🐻‍❄️', W / 2, H / 2 - 60);

        ctx.fillStyle = '#b8d4e8';
        ctx.font = '16px ui-monospace, monospace';
        ctx.fillText('Feed the hungry polar bears!', W / 2, H / 2 - 20);

        ctx.fillStyle = '#ffd700';
        ctx.font = '14px ui-monospace, monospace';
        ctx.fillText('Click to aim → Click to launch!', W / 2, H / 2 + 20);

        ctx.fillStyle = '#888';
        ctx.font = '12px ui-monospace, monospace';
        ctx.fillText('Different fish = different points. Golden fish are rare!', W / 2, H / 2 + 50);

        if (bestScore > 0) {
            ctx.fillStyle = '#ffd700';
            ctx.font = 'bold 14px ui-monospace, monospace';
            ctx.fillText('Best: ' + bestScore, W / 2, H / 2 + 80);
        }

        ctx.fillStyle = '#666';
        ctx.font = '11px ui-monospace, monospace';
        ctx.fillText('Click anywhere to start  ·  ESC to close', W / 2, H / 2 + 110);

        ctx.textAlign = 'start';
    }

    function drawGameOver() {
        ctx.fillStyle = 'rgba(0,0,0,0.75)';
        ctx.fillRect(0, 0, W, H);

        ctx.textAlign = 'center';

        ctx.fillStyle = '#ff6644';
        ctx.font = 'bold 36px ui-monospace, monospace';
        ctx.fillText('GAME OVER', W / 2, H / 2 - 50);

        ctx.fillStyle = '#ffd700';
        ctx.font = 'bold 24px ui-monospace, monospace';
        ctx.fillText('Score: ' + score, W / 2, H / 2);

        if (score >= bestScore && score > 0) {
            ctx.fillStyle = '#44ff44';
            ctx.font = 'bold 16px ui-monospace, monospace';
            ctx.fillText('★ NEW BEST! ★', W / 2, H / 2 + 30);
        }

        // Stats
        ctx.fillStyle = '#aaa';
        ctx.font = '13px ui-monospace, monospace';
        const caught = targets.filter(t => t.caught).length;
        ctx.fillText('Bears fed: ' + caught + '/' + targets.length, W / 2, H / 2 + 60);

        ctx.fillStyle = '#888';
        ctx.font = '12px ui-monospace, monospace';
        ctx.fillText('Press SPACE to play again  ·  ESC to close', W / 2, H / 2 + 90);

        ctx.textAlign = 'start';
    }

    function drawMessages() {
        for (const m of messages) {
            ctx.globalAlpha = m.life;
            ctx.fillStyle = m.color;
            ctx.font = 'bold 16px ui-monospace, monospace';
            ctx.textAlign = 'center';
            ctx.fillText(m.text, m.x, m.y);
        }
        ctx.globalAlpha = 1;
        ctx.textAlign = 'start';
    }

    function drawParticles() {
        for (const p of particles) {
            ctx.globalAlpha = p.life;
            ctx.fillStyle = p.color;
            if (p.type === 'sparkle') {
                // Star shape
                ctx.save();
                ctx.translate(p.x, p.y);
                ctx.rotate(p.life * 3);
                ctx.fillRect(-p.r / 2, -p.r / 2, p.r, p.r);
                ctx.restore();
            } else {
                ctx.beginPath();
                ctx.arc(p.x, p.y, p.r * p.life, 0, Math.PI * 2);
                ctx.fill();
            }
        }
        ctx.globalAlpha = 1;
    }

    // ─── Game logic ───────────────────────────────────────────────────

    function initGame() {
        score = 0;
        round = 0;
        combo = 0;
        consecutiveMisses = 0;
        bestScore = bestScore || 0;
        particles = [];
        messages = [];
        launchX = 80;
        launchY = GROUND_Y;

        clearPendingTimers();
        nextRound();
    }

    function nextRound() {
        if (round >= MAX_ROUNDS || consecutiveMisses >= MISS_LIMIT) {
            endGame();
            return;
        }

        round++;
        currentFish = pickFish();
        fishX = launchX + 32;
        fishY = launchY - 68;
        fishAngle = 0;
        fishSpin = 0;
        power = 0;
        powerDir = 1;
        aim = 0.5;
        aimDir = 1;
        phase = PH.POWER;

        // Create new targets each round (bears move around)
        targets = createTargets();
    }

    function launchFish() {
        const powerVal = power * currentFish.power;
        const speed = 4 + powerVal * 10; // 4 to 14 base speed
        const angle = -Math.PI + Math.PI * aim; // -PI to 0 (left to right, upward arc)

        fishVx = Math.cos(angle) * speed;
        fishVy = Math.sin(angle) * speed;
        fishSpin = rand(-0.15, 0.15);
        phase = PH.FLYING;
    }

    function updateFlying(dtFactor) {
        fishX += fishVx * dtFactor;
        fishY += fishVy * dtFactor;
        fishVy += GRAVITY * dtFactor;
        fishAngle += fishSpin * dtFactor;

        // Trail particles
        if (Math.random() < 0.3) {
            spawnSplash(fishX, fishY, currentFish.color, 1);
        }

        // Check if fish hit the ground
        if (fishY >= GROUND_Y - 5) {
            fishY = GROUND_Y - 5;
            checkCatch();
        }

        // Check if off screen
        if (fishX > W + 50 || fishX < -50 || fishY > H + 50) {
            miss();
        }
    }

    function checkCatch() {
        // Check each target
        let caught = false;
        for (const t of targets) {
            if (t.caught) continue;
            const dx = fishX - t.x;
            const dy = fishY - (t.y - 30 * t.size);
            const dist = Math.sqrt(dx * dx + dy * dy);
            const catchRadius = 35 * t.size;

            if (dist < catchRadius) {
                // CAUGHT!
                t.caught = true;
                t.expression = 'eating';
                t.catchAnim = 1;
                caught = true;
                combo++;
                consecutiveMisses = 0;

                // Score: base points × distance multiplier × combo
                const distMultiplier = 1 + (t.x - launchX) / 300;
                const comboMultiplier = Math.min(combo, 5);
                const pts = Math.round(currentFish.pts * distMultiplier * comboMultiplier);
                score += pts;

                // Effects
                spawnSplash(t.x, t.y - 20, currentFish.color, 15);
                spawnSparkles(t.x, t.y - 40, 10);
                const msgColor = combo > 2 ? '#ff6644' : combo > 1 ? '#ffd700' : '#44ff44';
                const msgText = '+' + pts + (comboMultiplier > 1 ? ' (x' + comboMultiplier + ')' : '');
                spawnScoreMsg(t.x, t.y - 70, msgText, msgColor);

                // Update expression after eating animation
                const tid = setTimeout(() => {
                    if (t) t.expression = 'happy';
                }, 800);
                pendingTimers.push(tid);

                break;
            }
        }

        if (!caught) {
            miss();
        } else {
            // Brief landing phase then next round
            phase = PH.CAUGHT;
            const tid = setTimeout(() => {
                if (phase === PH.CAUGHT && open) nextRound();
            }, 1200);
            pendingTimers.push(tid);
        }
    }

    function miss() {
        combo = 0;
        consecutiveMisses++;
        phase = PH.MISSED;

        // Sad reaction from nearest bear
        let nearest = null, nearDist = Infinity;
        for (const t of targets) {
            const d = Math.abs(t.x - fishX);
            if (d < nearDist) { nearDist = d; nearest = t; }
        }
        if (nearest) {
            nearest.expression = 'sad';
            const tid = setTimeout(() => {
                if (nearest) nearest.expression = 'hungry';
            }, 1500);
            pendingTimers.push(tid);
        }

        spawnSplash(fishX, GROUND_Y, '#8888aa', 8);

        const tid = setTimeout(() => {
            if (phase === PH.MISSED && open) nextRound();
        }, 1000);
        pendingTimers.push(tid);
    }

    function endGame() {
        phase = PH.GAMEOVER;
        if (score > bestScore) bestScore = score;
    }

    // ─── Update loop (dt-normalised to 60 fps) ────────────────────────

    function update(dtFactor) {
        // Animate power meter
        if (phase === PH.POWER) {
            power += powerDir * 0.02 * dtFactor;
            if (power >= 1) { power = 1; powerDir = -1; }
            if (power <= 0) { power = 0; powerDir = 1; }
        }

        // Animate aim meter
        if (phase === PH.AIM) {
            aim += aimDir * 0.015 * dtFactor;
            if (aim >= 1) { aim = 1; aimDir = -1; }
            if (aim <= 0) { aim = 0; aimDir = 1; }
        }

        // Update flying fish
        if (phase === PH.FLYING) {
            updateFlying(dtFactor);
        }

        // Update background snow
        for (const s of bgStars) {
            s.y += s.speed * dtFactor;
            s.x += s.drift * dtFactor;
            if (s.y > GROUND_Y) {
                s.y = -5;
                s.x = rand(0, W);
            }
        }

        // Update particles
        particles = particles.filter(p => {
            p.x += p.vx * dtFactor;
            p.y += p.vy * dtFactor;
            p.vy += 0.05 * dtFactor; // gravity on particles
            p.life -= p.decay * dtFactor;
            return p.life > 0;
        });

        // Update messages
        messages = messages.filter(m => {
            m.y += m.vy * dtFactor;
            m.life -= m.decay * dtFactor;
            return m.life > 0;
        });

        // Animate caught bears
        for (const t of targets) {
            if (t.catchAnim > 0) {
                t.mouthOpen = t.catchAnim;
                t.catchAnim -= 0.02 * dtFactor;
                if (t.catchAnim < 0) {
                    t.catchAnim = 0;
                    t.mouthOpen = 0;
                }
            }
        }
    }

    // ─── Render ───────────────────────────────────────────────────────

    function render() {
        ctx.clearRect(0, 0, W, H);

        drawSky();
        drawMountains();
        drawGround();
        drawSnow();

        // Draw target bears
        for (const t of targets) {
            drawPolarBear(t.x, t.y, t.size, t.posture, t.expression, t.mouthOpen);
        }

        // Draw launcher bear (always at launch position)
        const showFishInMouth = phase === PH.POWER || phase === PH.AIM || phase === PH.TITLE;
        drawLauncherBear(launchX, launchY, showFishInMouth);

        // Draw flying fish
        drawFlyingFish();

        // UI overlays
        drawPowerMeter();
        drawAimMeter();
        drawHUD();
        drawParticles();
        drawMessages();

        if (phase === PH.TITLE) drawTitle();
        if (phase === PH.GAMEOVER) drawGameOver();
    }

    // ─── Game loop ────────────────────────────────────────────────────

    function loop(timestamp) {
        if (!open) return;

        // Delta-time normalisation (clamp to avoid huge jumps on tab-switch)
        const rawDt = lastTime ? timestamp - lastTime : MS_PER_FRAME;
        const dtFactor = Math.min(rawDt, MS_PER_FRAME * 3) / MS_PER_FRAME;
        lastTime = timestamp;

        update(dtFactor);
        render();
        animId = requestAnimationFrame(loop);
    }

    // ─── Input ────────────────────────────────────────────────────────

    function handleClick() {
        if (phase === PH.TITLE) {
            initGame();
            return;
        }
        if (phase === PH.POWER) {
            phase = PH.AIM;
            return;
        }
        if (phase === PH.AIM) {
            launchFish();
            return;
        }
        if (phase === PH.GAMEOVER) {
            initGame();
            return;
        }
    }

    function handleGameKey(e) {
        if (e.key === ' ' || e.key === 'Enter') {
            e.preventDefault();
            if (phase === PH.GAMEOVER || phase === PH.TITLE) {
                initGame();
            } else if (phase === PH.POWER) {
                phase = PH.AIM;
            } else if (phase === PH.AIM) {
                launchFish();
            }
            return;
        }
        if (e.key === 'Escape') {
            closeFishFling();
            return;
        }
        // Prevent single-char keys from triggering the "fish" buffer
        if (e.key.length === 1 && !e.ctrlKey && !e.altKey && !e.metaKey) {
            e.preventDefault();
        }
    }

    // ─── Mount / unmount ──────────────────────────────────────────────

    function openFishFling() {
        if (open) return;
        open = true;
        recent = '';

        bestScore = bestScore || 0;
        lastTime = 0;

        const modal = document.createElement('div');
        modal.id = 'fishfling-modal';
        modal.setAttribute('role', 'dialog');
        modal.setAttribute('aria-label', 'Fish Fling easter egg game');
        modal.innerHTML =
            '<div id="fishfling-card">' +
              '<canvas id="fishfling-canvas" width="' + W + '" height="' + H + '"></canvas>' +
              '<div id="fishfling-hud">' +
                '<span id="fishfling-controls">CLICK to aim · CLICK to launch · SPACE restart · ESC close</span>' +
              '</div>' +
            '</div>';

        document.body.appendChild(modal);
        canvas = modal.querySelector('#fishfling-canvas');
        ctx = canvas.getContext('2d');

        // Click/touch on the entire modal so mobile taps outside canvas still work
        modal.addEventListener('click', handleClick);
        modal.addEventListener('touchstart', function (e) {
            e.preventDefault();
            handleClick();
        }, { passive: false });

        phase = PH.TITLE;
        targets = [];
        particles = [];
        messages = [];
        score = 0;
        round = 0;
        combo = 0;
        consecutiveMisses = 0;
        currentFish = FISH_TYPES[0];
        fishAngle = 0;

        initBgSnow();
        initSnowBumps();
        animId = requestAnimationFrame(loop);
    }

    function clearPendingTimers() {
        for (const t of pendingTimers) clearTimeout(t);
        pendingTimers = [];
    }

    function closeFishFling() {
        if (!open) return;
        open = false;
        clearPendingTimers();
        if (animId) cancelAnimationFrame(animId);
        animId = null;
        phase = PH.TITLE;
        const m = document.getElementById('fishfling-modal');
        if (m) m.remove();
    }

    // ─── URL fallback (?fish=1) ───────────────────────────────────────
    if (new URLSearchParams(window.location.search).has('fish')) {
        const startWhenReady = function () { openFishFling(); };
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', startWhenReady, { once: true });
        } else {
            startWhenReady();
        }
    }
})();
