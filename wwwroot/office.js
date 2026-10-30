// Oficina virtual estilo gather.town: mapa de casillas, personajes, colisiones, ruta por clic y presencia.
import { drawCharacter, shade } from './sprites.js';

const T = 32, W = 28, H = 16;           // tamaño de casilla y dimensiones del mapa
const SPEED = 4.2;                      // casillas por segundo
let canvas, ctx, dotnet, me, agents = [], people = new Map(), blocked, decor = [];
let keys = {}, path = [], raf = 0, last = 0, lastSent = 0, nearId = null, bubbles = new Map(), busy = new Set();
let dragging = null, scale = 1, offX = 0, offY = 0, destroyed = false, dpr = 1, ro = null, heartbeat = 0;

export function init(canvasId, ref, opts) {
    destroyed = false;
    canvas = document.getElementById(canvasId); ctx = canvas.getContext('2d'); dotnet = ref;
    me = { id: opts.id, name: opts.name, color: opts.color, avatar: opts.avatar, x: 14, y: 13, dir: 'down', moving: false, frame: 0, canEdit: opts.canEdit, focusAgent: opts.focusAgent };
    buildMap();
    resize();
    window.addEventListener('resize', resize);
    ro = new ResizeObserver(() => resize()); ro.observe(canvas.parentElement);
    heartbeat = setInterval(() => { if (!destroyed) dotnet.invokeMethodAsync('Move', +me.x.toFixed(2), +me.y.toFixed(2), me.dir, me.moving); }, 8000);
    canvas.addEventListener('keydown', onKey); canvas.addEventListener('keyup', onKeyUp);
    canvas.addEventListener('mousedown', onDown); canvas.addEventListener('mousemove', onMove); window.addEventListener('mouseup', onUp);
    canvas.addEventListener('touchstart', e => { const t = e.touches[0]; onDown({ clientX: t.clientX, clientY: t.clientY, button: 0, preventDefault() { } }); e.preventDefault(); }, { passive: false });
    canvas.addEventListener('blur', () => keys = {});
    canvas.focus();
    last = performance.now();
    raf = requestAnimationFrame(loop);
}

export function destroy() {
    destroyed = true; cancelAnimationFrame(raf); ro?.disconnect(); clearInterval(heartbeat);
    window.removeEventListener('resize', resize); window.removeEventListener('mouseup', onUp);
}
export function focus() { canvas?.focus(); }
export function setAgents(list) {
    const prev = new Map(agents.map(a => [a.id, a]));
    agents = list.map(a => { const o = prev.get(a.id); return { ...a, frame: Math.random(), px: o?.px ?? a.x, py: o?.py ?? a.y, state: o?.state ?? 'desk', timer: o?.timer ?? 8 + Math.random() * 30, path: o?.path ?? [], dir: o?.dir ?? 'down', moving: false, activity: o?.activity ?? null, hidden: o?.hidden ?? false }; });
    busy = new Set(list.filter(a => a.busy).map(a => a.id));
    buildMap();
    if (me.focusAgent) { const a = agents.find(z => z.id === me.focusAgent); if (a) { me.x = a.x; me.y = a.y + 2; } me.focusAgent = null; }
}
export function setPeople(list) {
    const seen = new Set();
    for (const p of list) {
        seen.add(p.id);
        const cur = people.get(p.id);
        if (cur) { cur.tx = p.x; cur.ty = p.y; cur.dir = p.dir; cur.moving = p.moving; cur.name = p.name; cur.color = p.color; }
        else people.set(p.id, { ...p, tx: p.x, ty: p.y, frame: 0 });
    }
    for (const id of [...people.keys()]) if (!seen.has(id)) people.delete(id);
}
export function setBusy(id, b) { if (b) busy.add(id); else busy.delete(id); const a = agents.find(z => z.id === id); if (b && a && a.state !== 'desk') goHome(a, true); }
export function setMood(id, mood, emoji) { const a = agents.find(z => z.id === id); if (a) { a.mood = mood; a.moodEmoji = emoji; } }
export function say(id, text) { bubbles.set(id, { text, until: performance.now() + 7000 }); }

// ---------- mapa ----------
// Zonas: cocina (arriba derecha), baño (puerta WC), sala de reuniones acristalada (abajo derecha), lounge (izquierda) y open space con puestos.
const ZONES = { kitchen: { x: 21, y: 1, w: 6, h: 3 }, meeting: { x: 19, y: 9, w: 8, h: 6 }, lounge: { x: 1, y: 6, w: 4, h: 6 } };
export const DESK_SLOTS = [{ x: 6, y: 3 }, { x: 10, y: 3 }, { x: 14, y: 3 }, { x: 18, y: 3 }, { x: 6, y: 7 }, { x: 10, y: 7 }, { x: 14, y: 7 }, { x: 6, y: 11 }, { x: 10, y: 11 }, { x: 14, y: 11 }];
function buildMap() {
    blocked = Array.from({ length: H }, () => Array(W).fill(false));
    decor = [];
    for (let x = 0; x < W; x++) { blocked[0][x] = blocked[H - 1][x] = true; }
    for (let y = 0; y < H; y++) { blocked[y][0] = blocked[y][W - 1] = true; }
    blocked[H - 1][13] = blocked[H - 1][14] = false;     // puerta principal
    const add = (type, x, y, w = 1, h = 1, extra = {}) => { decor.push({ type, x, y, w, h, ...extra }); for (let j = y; j < y + h; j++) for (let i = x; i < x + w; i++) if (blocked[j] && i >= 0 && i < W) blocked[j][i] = true; };
    const deco = (type, x, y, w = 1, h = 1, extra = {}) => decor.push({ type, x, y, w, h, noblock: true, ...extra });
    // puestos de trabajo
    for (const a of agents) { add('desk', a.x, a.y + 1, 2, 1, { color: a.color, agent: a }); deco('chair', a.x, a.y); }
    // cocina
    add('counter', 21, 1, 2, 1); add('coffee', 23, 1); add('fridge', 24, 1); add('microwave', 25, 1); add('stool', 21, 3); add('stool', 23, 3);
    deco('zone', 21, 1, 6, 3, { label: 'Cocina' });
    deco('wc', 26, 0);
    // sala de reuniones (paredes de cristal con puerta en (19,11))
    for (let x = 19; x < W - 1; x++) if (!blocked[9][x]) add('glass', x, 9, 1, 1, { dir: 'h' });
    for (let y = 10; y < H - 1; y++) if (y !== 11 && y !== 12) add('glass', 19, y, 1, 1, { dir: 'v' });
    add('table', 21, 11, 4, 2); deco('tv', 23, 9); deco('zone', 20, 10, 7, 5, { label: 'Sala de reuniones' });
    // lounge
    add('sofa', 1, 7, 1, 3); add('sidetable', 2, 10); add('bookshelf', 1, 11); add('watercooler', 1, 5); deco('rug', 1, 6, 4, 6); deco('zone', 1, 6, 4, 6, { label: 'Lounge' });
    // plantas y detalles
    add('plant', 1, 1); add('plant', 19, 1); add('plant', W - 2, 7); add('plant', 1, H - 2); add('plant', W - 2, H - 2); add('plant', 17, 13);
    deco('board', 11, 0, 3, 1); deco('clock', 7, 0); deco('logo', 2, 0, 2, 1); deco('reception', 11, 13, 2, 1);
    add('reception', 11, 13, 2, 1);
    decor.sort((a, b) => (a.y + a.h) - (b.y + b.h) || (a.type === 'zone' || a.type === 'rug' ? -1 : 0));
}
const isBlocked = (x, y) => x < 0 || y < 0 || x >= W || y >= H || blocked[y][x];
const SPOTS = {
    coffee: [{ x: 23, y: 2 }, { x: 22, y: 2 }, { x: 24, y: 2 }],
    wc: [{ x: 26, y: 1 }],
    meeting: [{ x: 21, y: 10 }, { x: 22, y: 10 }, { x: 23, y: 10 }, { x: 24, y: 10 }, { x: 21, y: 13 }, { x: 23, y: 13 }],
    sofa: [{ x: 2, y: 7 }, { x: 2, y: 9 }, { x: 3, y: 8 }],
    rug: [{ x: 9, y: 5 }, { x: 13, y: 9 }, { x: 17, y: 7 }],
};
const ACT = { coffee: { label: '☕ café', min: 8, max: 16, hold: 'coffee' }, wc: { label: '🚻', min: 8, max: 14, hide: true }, meeting: { label: '📋 reunión', min: 15, max: 35 }, sofa: { label: '😌 descanso', min: 10, max: 25 }, rug: { label: '💬 charla', min: 10, max: 22 }, visit: { label: '💬 charla', min: 8, max: 18 } };
function freeSpot(kind) { const taken = new Set(agents.filter(a => a.target).map(a => a.target.x + ',' + a.target.y)); return SPOTS[kind].find(sp => !taken.has(sp.x + ',' + sp.y) && !isBlocked(sp.x, sp.y)); }
function goHome(a, urgent) { a.state = 'return'; a.activity = null; a.hidden = false; a.target = null; a.path = bfs({ x: Math.round(a.px), y: Math.round(a.py) }, { x: a.x, y: a.y }); if (urgent) a.speedMul = 1.6; }
function pickActivity(a) {
    const mood = a.mood ?? 70;
    const r = Math.random();
    // ánimo bajo → más pausas de café/sofá; alto → más reuniones y charlas
    let kind = r < 0.3 ? 'coffee' : r < 0.42 ? 'wc' : r < 0.6 ? 'meeting' : r < 0.75 ? 'rug' : r < 0.88 ? 'visit' : 'sofa';
    if (mood < 45 && r < 0.5) kind = r < 0.3 ? 'coffee' : 'sofa';
    let target = null, visitId = null;
    if (kind === 'visit') {
        const others = agents.filter(o => o.id !== a.id && o.state === 'desk' && !busy.has(o.id));
        if (!others.length) return false;
        const o = others[Math.floor(Math.random() * others.length)];
        const cands = [{ x: o.x - 1, y: o.y }, { x: o.x + 2, y: o.y }, { x: o.x, y: o.y + 2 }, { x: o.x + 1, y: o.y + 2 }].filter(c => !isBlocked(c.x, c.y) && !(c.x === o.x && c.y === o.y));
        if (!cands.length) return false;
        target = cands[Math.floor(Math.random() * cands.length)]; visitId = o.id;
    } else { target = freeSpot(kind); if (!target) return false; }
    const path = bfs({ x: Math.round(a.px), y: Math.round(a.py) }, target);
    if (!path.length) return false;
    a.state = 'go'; a.activity = kind; a.target = target; a.visitId = visitId; a.path = path; a.speedMul = 1;
    return true;
}
function updateAgent(a, dt) {
    const busyNow = busy.has(a.id);
    if (a.state === 'desk') {
        a.px += (a.x - a.px) * Math.min(1, dt * 10); a.py += (a.y - a.py) * Math.min(1, dt * 10);
        a.moving = false; a.dir = 'down';
        if (busyNow) { a.timer = 10 + Math.random() * 20; return; }
        a.timer -= dt;
        if (a.timer <= 0) { if (!pickActivity(a)) a.timer = 10 + Math.random() * 20; }
        return;
    }
    if (a.state === 'go' || a.state === 'return') {
        if (a.path.length) {
            const t = a.path[0], vx = t.x - a.px, vy = t.y - a.py, d = Math.hypot(vx, vy), sp = 2.4 * (a.speedMul || 1) * dt;
            if (d <= sp) { a.px = t.x; a.py = t.y; a.path.shift(); }
            else { a.px += vx / d * sp; a.py += vy / d * sp; a.dir = Math.abs(vx) > Math.abs(vy) ? (vx < 0 ? 'left' : 'right') : (vy < 0 ? 'up' : 'down'); }
            a.moving = true;
        } else {
            a.moving = false;
            if (a.state === 'return') { a.state = 'desk'; a.timer = 20 + Math.random() * 40 + (a.mood > 80 ? 15 : 0); a.speedMul = 1; }
            else { const act = ACT[a.activity]; a.state = 'stay'; a.timer = act.min + Math.random() * (act.max - act.min); a.hidden = !!act.hide; a.dir = a.activity === 'coffee' || a.activity === 'wc' ? 'up' : 'down';
                if (a.activity === 'visit') { const o = agents.find(z => z.id === a.visitId); if (o && o.state === 'desk') { o.chatting = a.timer; } } }
        }
        return;
    }
    if (a.state === 'stay') {
        a.timer -= dt; a.moving = false;
        if (a.timer <= 0 || busyNow) goHome(a, busyNow);
    }
}

function bfs(from, to) {
    if (isBlocked(to.x, to.y)) return [];
    const key = (x, y) => y * W + x, prev = new Map(), q = [from]; prev.set(key(from.x, from.y), null);
    while (q.length) {
        const c = q.shift();
        if (c.x === to.x && c.y === to.y) { const out = []; let k = key(c.x, c.y); while (k !== null) { out.push({ x: k % W, y: Math.floor(k / W) }); k = prev.get(k); } return out.reverse().slice(1); }
        for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) { const nx = c.x + dx, ny = c.y + dy, nk = key(nx, ny); if (!isBlocked(nx, ny) && !prev.has(nk)) { prev.set(nk, key(c.x, c.y)); q.push({ x: nx, y: ny }); } }
    }
    return [];
}

// ---------- entrada ----------
function onKey(e) {
    const k = e.key.toLowerCase();
    if (['arrowup', 'arrowdown', 'arrowleft', 'arrowright', 'w', 'a', 's', 'd', ' '].includes(k)) e.preventDefault();
    if (k === 'e' || k === 'enter' || k === ' ') { if (nearId) dotnet.invokeMethodAsync('OpenAgent', nearId); return; }
    keys[k] = true; path = [];
}
function onKeyUp(e) { keys[e.key.toLowerCase()] = false; }
function toTile(ev) { const r = canvas.getBoundingClientRect(); return { x: Math.floor((ev.clientX - r.left - offX) / (T * scale)), y: Math.floor((ev.clientY - r.top - offY) / (T * scale)) }; }
function onDown(ev) {
    canvas.focus(); ev.preventDefault?.();
    const t = toTile(ev);
    const a = agents.find(z => !z.hidden && Math.abs(Math.round(z.px) - t.x) <= 0 && (Math.round(z.py) === t.y || Math.round(z.py) === t.y + 1)) || agents.find(z => (z.x === t.x || z.x + 1 === t.x) && (z.y === t.y || z.y + 1 === t.y) && z.state === 'desk');
    if (a) {
        if (me.canEdit && ev.shiftKey) { dragging = a; return; }
        if (Math.max(Math.abs(me.x - a.px), Math.abs(me.y - a.py)) <= 2.2) { dotnet.invokeMethodAsync('OpenAgent', a.id); return; }
        // acercarse al agente (donde esté) y abrir al llegar
        const ax = Math.round(a.px), ay = Math.round(a.py);
        const goal = [{ x: ax, y: ay + 2 }, { x: ax + 1, y: ay + 2 }, { x: ax - 1, y: ay }, { x: ax + 2, y: ay }, { x: ax, y: ay - 1 }, { x: ax - 1, y: ay + 1 }].find(g => !isBlocked(g.x, g.y));
        if (goal) { path = bfs({ x: Math.round(me.x), y: Math.round(me.y) }, goal); path.openAgent = a.id; }
        return;
    }
    path = bfs({ x: Math.round(me.x), y: Math.round(me.y) }, t);
}
function onMove(ev) { if (!dragging) return; const t = toTile(ev); dragging.x = Math.max(1, Math.min(W - 3, t.x)); dragging.y = Math.max(1, Math.min(H - 3, t.y)); buildMap(); }
function onUp() { if (dragging) { dotnet.invokeMethodAsync('MoveAgent', dragging.id, dragging.x, dragging.y); dragging = null; } }

// ---------- bucle ----------
function resize() {
    if (!canvas) return;
    const box = canvas.parentElement.getBoundingClientRect();
    dpr = window.devicePixelRatio || 1;
    canvas.width = Math.floor(box.width * dpr); canvas.height = Math.floor(box.height * dpr);
    canvas.style.width = box.width + 'px'; canvas.style.height = box.height + 'px';
    scale = Math.min(box.width / (W * T), box.height / (H * T), 1.6);
    offX = Math.floor((box.width - W * T * scale) / 2); offY = Math.floor((box.height - H * T * scale) / 2);
}

function loop(now) {
    if (destroyed) return;
    const dt = Math.min(0.05, (now - last) / 1000); last = now;
    update(dt, now);
    draw(now);
    raf = requestAnimationFrame(loop);
}

function update(dt, now) {
    let dx = 0, dy = 0;
    if (keys['arrowup'] || keys['w']) dy = -1; else if (keys['arrowdown'] || keys['s']) dy = 1;
    else if (keys['arrowleft'] || keys['a']) dx = -1; else if (keys['arrowright'] || keys['d']) dx = 1;
    if (dx || dy) {
        me.dir = dy < 0 ? 'up' : dy > 0 ? 'down' : dx < 0 ? 'left' : 'right';
        const nx = me.x + dx * SPEED * dt, ny = me.y + dy * SPEED * dt;
        const cx = Math.round(nx + dx * 0.35), cy = Math.round(ny + dy * 0.35);
        if (!isBlocked(cx, cy)) { me.x = nx; me.y = ny; me.moving = true; }
        else { me.x = Math.round(me.x); me.y = Math.round(me.y); me.moving = false; }
        // alinear el eje perpendicular suavemente
        if (dx) me.y += (Math.round(me.y) - me.y) * Math.min(1, dt * 10); else me.x += (Math.round(me.x) - me.x) * Math.min(1, dt * 10);
    } else if (path.length) {
        const t = path[0], vx = t.x - me.x, vy = t.y - me.y, d = Math.hypot(vx, vy);
        if (d < 0.05) { me.x = t.x; me.y = t.y; path.shift(); if (!path.length && path.openAgent) { const id = path.openAgent; path = []; dotnet.invokeMethodAsync('OpenAgent', id); } }
        else { const s = Math.min(d, SPEED * dt); me.x += vx / d * s; me.y += vy / d * s; me.dir = Math.abs(vx) > Math.abs(vy) ? (vx < 0 ? 'left' : 'right') : (vy < 0 ? 'up' : 'down'); me.moving = true; }
    } else me.moving = false;
    me.frame = me.moving ? (me.frame + dt * 2.2) % 1 : 0;
    if (now - lastSent > 120 && (me.moving || me._wasMoving)) { lastSent = now; me._wasMoving = me.moving; dotnet.invokeMethodAsync('Move', +me.x.toFixed(2), +me.y.toFixed(2), me.dir, me.moving); }
    for (const p of people.values()) { p.x += (p.tx - p.x) * Math.min(1, dt * 8); p.y += (p.ty - p.y) * Math.min(1, dt * 8); p.frame = p.moving ? (p.frame + dt * 2.2) % 1 : 0; }
    for (const a of agents) { updateAgent(a, dt); a.frame = (a.frame + dt * (a.moving ? 2.2 : a.state === 'desk' ? 0.6 : 0.25)) % 1; if (a.chatting > 0) a.chatting -= dt; }
    // proximidad
    let best = null, bd = 9;
    for (const a of agents) { if (a.hidden) continue; const d = Math.max(Math.abs(me.x - a.px), Math.abs(me.y - a.py)); if (d <= 2.2 && d < bd) { bd = d; best = a.id; } }
    if (best !== nearId) { nearId = best; dotnet.invokeMethodAsync('NearAgent', nearId); }
    for (const [id, b] of bubbles) if (b.until < now) bubbles.delete(id);
}

// ---------- dibujo ----------
function draw(now) {
    const dark = document.documentElement.dataset.theme === 'dark';
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.fillStyle = dark ? '#0f1116' : '#d6dde6';
    ctx.fillRect(0, 0, canvas.width / dpr, canvas.height / dpr);
    ctx.translate(offX, offY); ctx.scale(scale, scale);
    ctx.imageSmoothingEnabled = false;
    drawFloor(dark);
    drawWalls(dark, now);
    const items = [...decor.map(d => ({ z: d.type === 'zone' || d.type === 'rug' ? -1 : (d.y + d.h) * T, draw: () => drawDecor(d, dark, now) })),
        ...agents.filter(a => !a.hidden).map(a => ({ z: (a.py + 0.5) * T + 1, draw: () => drawAgent(a, now) })),
        ...[...people.values()].map(p => ({ z: (p.y + 0.5) * T, draw: () => drawPerson(p, false) })),
        { z: (me.y + 0.5) * T, draw: () => drawPerson(me, true) }];
    items.sort((a, b) => a.z - b.z).forEach(i => i.draw());
    for (const a of agents) drawBubble(a, now);
    // viñeta suave
    const g = ctx.createRadialGradient(W * T / 2, H * T / 2, H * T * 0.5, W * T / 2, H * T / 2, W * T * 0.75);
    g.addColorStop(0, 'rgba(0,0,0,0)'); g.addColorStop(1, dark ? 'rgba(0,0,0,.45)' : 'rgba(40,50,70,.18)');
    ctx.fillStyle = g; ctx.fillRect(0, 0, W * T, H * T);
    ctx.setTransform(1, 0, 0, 1, 0, 0);
}

function inZone(z, x, y) { return x >= z.x && x < z.x + z.w && y >= z.y && y < z.y + z.h; }

function drawFloor(dark) {
    for (let y = 1; y < H - 1; y++) for (let x = 1; x < W - 1; x++) {
        const X = x * T, Y = y * T;
        if (inZone(ZONES.kitchen, x, y)) { // baldosas
            ctx.fillStyle = (x + y) % 2 ? (dark ? '#2a3038' : '#e9edf1') : (dark ? '#262b33' : '#dfe4ea'); ctx.fillRect(X, Y, T, T);
            ctx.strokeStyle = dark ? '#1f242b' : '#cfd6de'; ctx.lineWidth = 1; ctx.strokeRect(X + .5, Y + .5, T - 1, T - 1);
        } else if (inZone(ZONES.meeting, x, y)) { // moqueta
            ctx.fillStyle = dark ? '#1f2a3a' : '#d9e4f2'; ctx.fillRect(X, Y, T, T);
            ctx.fillStyle = dark ? 'rgba(255,255,255,.03)' : 'rgba(0,0,0,.035)';
            for (let i = 0; i < 4; i++) ctx.fillRect(X + (i * 8 + (y % 2) * 4) % T, Y + ((i * 5) % T), 2, 2);
        } else { // tarima de madera
            ctx.fillStyle = (x % 2 === 0) ? (dark ? '#2b241c' : '#eadcc3') : (dark ? '#2f281f' : '#e4d4b8'); ctx.fillRect(X, Y, T, T);
            ctx.fillStyle = dark ? 'rgba(0,0,0,.25)' : 'rgba(120,80,40,.12)';
            ctx.fillRect(X, Y + ((x * 7 + y * 13) % 3) * 10, T, 1);
            ctx.fillRect(X, Y, 1, T);
        }
    }
}

function drawWalls(dark, now) {
    // pared superior con zócalo y ventanas
    ctx.fillStyle = dark ? '#3a3f4b' : '#c3cad4'; ctx.fillRect(0, 0, W * T, T);
    ctx.fillStyle = dark ? '#2b2f38' : '#9aa5b3'; ctx.fillRect(0, T - 6, W * T, 6);
    for (const wx of [3, 8, 15, 23]) {
        ctx.fillStyle = dark ? '#5b6472' : '#fff'; ctx.fillRect(wx * T + 2, 2, T * 2 - 4, T - 10);
        const sky = ctx.createLinearGradient(0, 2, 0, T - 8); sky.addColorStop(0, dark ? '#17345e' : '#a9dcff'); sky.addColorStop(1, dark ? '#26466f' : '#e3f4ff');
        ctx.fillStyle = sky; ctx.fillRect(wx * T + 4, 4, T * 2 - 8, T - 14);
        ctx.fillStyle = dark ? '#5b6472' : '#fff'; ctx.fillRect(wx * T + T - 1, 4, 2, T - 14);
        if (!dark) { ctx.fillStyle = 'rgba(255,255,255,.6)'; ctx.fillRect(wx * T + 6, 6, 8, 3); }
    }
    // paredes laterales e inferior
    ctx.fillStyle = dark ? '#2b2f38' : '#9aa5b3';
    ctx.fillRect(0, T, T, (H - 1) * T); ctx.fillRect((W - 1) * T, T, T, (H - 1) * T);
    ctx.fillRect(0, (H - 1) * T, 13 * T, T); ctx.fillRect(15 * T, (H - 1) * T, (W - 15) * T, T);
    ctx.fillStyle = dark ? 'rgba(255,255,255,.04)' : 'rgba(255,255,255,.25)';
    for (let y = 1; y < H; y += 1) { ctx.fillRect(0, y * T, T, 1); ctx.fillRect((W - 1) * T, y * T, T, 1); }
    // puerta principal (alfombrilla + luz)
    ctx.fillStyle = dark ? '#3b3024' : '#c9b8a0'; ctx.fillRect(13 * T, (H - 1) * T, 2 * T, T);
    ctx.fillStyle = dark ? '#5c4a35' : '#a88c6b'; ctx.fillRect(13 * T + 4, (H - 1) * T + 4, 2 * T - 8, T - 8);
    ctx.fillStyle = dark ? '#8fa3bf' : '#1e293b'; ctx.font = 'bold 8px sans-serif'; ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.fillText('ENTRADA', 14 * T, (H - 1) * T + T / 2);
}

function drawDecor(d, dark, now) {
    const x = d.x * T, y = d.y * T, w = d.w * T, h = d.h * T;
    const ink = dark ? '#c8d0dc' : '#1e293b';
    switch (d.type) {
        case 'zone': ctx.font = '600 9px Figtree, system-ui, sans-serif'; ctx.textAlign = 'left'; ctx.textBaseline = 'top'; ctx.fillStyle = dark ? 'rgba(255,255,255,.28)' : 'rgba(30,41,59,.35)'; ctx.fillText(d.label.toUpperCase(), x + 4, y + 3); break;
        case 'rug': ctx.fillStyle = dark ? 'rgba(120,80,200,.22)' : 'rgba(162,93,220,.16)'; roundRect(x + 2, y + 2, w - 4, h - 4, 6); ctx.fill(); ctx.strokeStyle = dark ? 'rgba(180,140,255,.35)' : 'rgba(120,60,180,.3)'; ctx.lineWidth = 2; roundRect(x + 7, y + 7, w - 14, h - 14, 4); ctx.stroke(); break;
        case 'desk': {
            const a = d.agent; const typing = a && a.state === 'desk' && !(a.chatting > 0);
            ctx.fillStyle = 'rgba(0,0,0,.12)'; ctx.fillRect(x + 4, y + h - 4, w - 8, 4);
            ctx.fillStyle = dark ? '#6b4a2b' : '#c89b6a'; ctx.fillRect(x + 2, y + 6, w - 4, h - 8);
            ctx.fillStyle = dark ? '#8a6238' : '#e2bb8c'; ctx.fillRect(x + 2, y + 6, w - 4, 5);
            // monitor
            ctx.fillStyle = '#2b2f38'; ctx.fillRect(x + 8, y - 1, 22, 13); ctx.fillRect(x + 17, y + 12, 4, 2); ctx.fillRect(x + 13, y + 14, 12, 2);
            const on = typing || busy.has(a?.id);
            ctx.fillStyle = on ? (busy.has(a?.id) ? '#ffcb00' : shade(d.color || '#0073ea', 50)) : '#1a1d24'; ctx.fillRect(x + 10, y + 1, 18, 9);
            if (on) { ctx.fillStyle = 'rgba(255,255,255,.7)'; const k = Math.floor(now / 400); for (let i = 0; i < 3; i++) ctx.fillRect(x + 12, y + 3 + i * 2.5, 4 + ((i + k) % 3) * 4, 1); }
            // teclado, ratón, taza, papeles
            ctx.fillStyle = '#d8dde6'; ctx.fillRect(x + 9, y + 18, 16, 5); ctx.fillStyle = '#9aa5b3'; for (let i = 0; i < 5; i++) ctx.fillRect(x + 10 + i * 3, y + 19, 2, 1);
            ctx.fillStyle = '#e8ecf2'; ctx.fillRect(x + 28, y + 19, 4, 5);
            ctx.fillStyle = '#fff'; ctx.fillRect(x + w - 22, y + 12, 13, 9); ctx.fillStyle = '#c8d0dc'; ctx.fillRect(x + w - 20, y + 14, 9, 1); ctx.fillRect(x + w - 20, y + 17, 7, 1);
            ctx.fillStyle = d.color || '#0073ea'; ctx.fillRect(x + w - 9, y + 8, 5, 5); ctx.fillStyle = '#fff'; ctx.fillRect(x + w - 4, y + 9, 2, 3);
            break;
        }
        case 'chair': ctx.fillStyle = dark ? '#3b3f4a' : '#4f5b6b'; roundRect(x + 7, y + 2, 18, 16, 4); ctx.fill(); ctx.fillStyle = dark ? '#2f333c' : '#3b4654'; ctx.fillRect(x + 9, y + 18, 14, 4); break;
        case 'counter': ctx.fillStyle = dark ? '#4b5563' : '#cfd6de'; ctx.fillRect(x + 1, y + 4, w - 2, h - 4); ctx.fillStyle = dark ? '#6b7280' : '#eef1f5'; ctx.fillRect(x + 1, y + 4, w - 2, 6); ctx.fillStyle = '#9aa5b3'; ctx.fillRect(x + 6, y + 16, 10, 6); ctx.fillRect(x + 36, y + 16, 10, 6); ctx.fillStyle = '#e2445c'; ctx.fillRect(x + 22, y + 5, 6, 4); ctx.fillStyle = '#9cd326'; ctx.fillRect(x + 29, y + 5, 5, 4); break;
        case 'coffee': ctx.fillStyle = dark ? '#4b5563' : '#cfd6de'; ctx.fillRect(x + 1, y + 4, w - 2, h - 4); ctx.fillStyle = '#333'; ctx.fillRect(x + 6, y - 6, 20, 18); ctx.fillStyle = '#555'; ctx.fillRect(x + 9, y - 3, 14, 5); ctx.fillStyle = (Math.floor(now / 600) % 2) ? '#e2445c' : '#7a1f2c'; ctx.fillRect(x + 11, y + 4, 3, 3); ctx.fillStyle = '#fff'; ctx.fillRect(x + 18, y + 5, 6, 5); ctx.fillStyle = '#6b4a2b'; ctx.fillRect(x + 19, y + 6, 4, 2); break;
        case 'fridge': ctx.fillStyle = '#e8ecf2'; ctx.fillRect(x + 4, y - 10, w - 8, h + 10); ctx.fillStyle = '#c8d0dc'; ctx.fillRect(x + 4, y + 2, w - 8, 1); ctx.fillStyle = '#9aa5b3'; ctx.fillRect(x + w - 10, y - 6, 2, 6); ctx.fillRect(x + w - 10, y + 6, 2, 10); break;
        case 'microwave': ctx.fillStyle = dark ? '#4b5563' : '#cfd6de'; ctx.fillRect(x + 1, y + 4, w - 2, h - 4); ctx.fillStyle = '#2b2f38'; ctx.fillRect(x + 4, y - 4, w - 8, 12); ctx.fillStyle = '#6b7280'; ctx.fillRect(x + 6, y - 2, 14, 8); ctx.fillStyle = '#9cd326'; ctx.fillRect(x + 22, y - 1, 3, 2); break;
        case 'stool': ctx.fillStyle = dark ? '#3b3f4a' : '#6b7280'; ctx.beginPath(); ctx.ellipse(x + T / 2, y + T / 2, 8, 5, 0, 0, Math.PI * 2); ctx.fill(); ctx.fillRect(x + T / 2 - 1, y + T / 2 + 4, 2, 8); break;
        case 'glass': ctx.fillStyle = dark ? 'rgba(160,200,255,.18)' : 'rgba(120,170,230,.28)'; if (d.dir === 'h') ctx.fillRect(x, y + T / 2 - 3, T, 6); else ctx.fillRect(x + T / 2 - 3, y, 6, T); ctx.fillStyle = dark ? 'rgba(200,225,255,.5)' : 'rgba(255,255,255,.8)'; if (d.dir === 'h') ctx.fillRect(x, y + T / 2 - 3, T, 1); else ctx.fillRect(x + T / 2 - 3, y, 1, T); ctx.fillStyle = dark ? '#5b6472' : '#8c96a5'; if (d.dir === 'h') { ctx.fillRect(x, y + T / 2 - 4, 2, 8); } else { ctx.fillRect(x + T / 2 - 4, y, 8, 2); } break;
        case 'table': ctx.fillStyle = 'rgba(0,0,0,.12)'; ctx.fillRect(x + 6, y + h - 6, w - 12, 6); ctx.fillStyle = dark ? '#4a3a2b' : '#a37a52'; ctx.fillRect(x + 4, y + 8, w - 8, h - 12); ctx.fillStyle = dark ? '#5d4a36' : '#c69a6e'; ctx.fillRect(x + 4, y + 8, w - 8, 7);
            for (let i = 0; i < d.w; i++) { ctx.fillStyle = dark ? '#3b3f4a' : '#4f5b6b'; roundRect(x + i * T + 9, y - 6, 14, 10, 3); ctx.fill(); roundRect(x + i * T + 9, y + h - 2, 14, 10, 3); ctx.fill(); }
            ctx.fillStyle = '#c8d0dc'; ctx.fillRect(x + 14, y + 18, 12, 8); ctx.fillRect(x + 78, y + 18, 12, 8); ctx.fillStyle = '#2b2f38'; ctx.fillRect(x + 14, y + 16, 12, 2); ctx.fillRect(x + 78, y + 16, 12, 2);
            ctx.fillStyle = '#2e8b57'; ctx.beginPath(); ctx.arc(x + w / 2, y + h / 2 + 2, 5, 0, Math.PI * 2); ctx.fill(); break;
        case 'tv': ctx.fillStyle = '#1e293b'; ctx.fillRect(x + 2, y + 8, T - 4, 18); ctx.fillStyle = '#0073ea'; ctx.fillRect(x + 4, y + 10, T - 8, 14); ctx.fillStyle = '#fff'; ctx.fillRect(x + 7, y + 20, 6, 2); ctx.fillRect(x + 15, y + 16, 5, 6); ctx.fillRect(x + 22, y + 13, 4, 9); break;
        case 'plant': ctx.fillStyle = 'rgba(0,0,0,.12)'; ctx.beginPath(); ctx.ellipse(x + 16, y + 28, 9, 3, 0, 0, Math.PI * 2); ctx.fill(); ctx.fillStyle = '#9b5b3a'; ctx.fillRect(x + 10, y + 16, 12, 12); ctx.fillStyle = '#7a4630'; ctx.fillRect(x + 10, y + 16, 12, 3); ctx.fillStyle = '#2e8b57'; ctx.beginPath(); ctx.arc(x + 16, y + 10, 10, 0, Math.PI * 2); ctx.fill(); ctx.fillStyle = '#3cb371'; ctx.beginPath(); ctx.arc(x + 12, y + 8, 6, 0, Math.PI * 2); ctx.fill(); ctx.fillStyle = '#58c27d'; ctx.beginPath(); ctx.arc(x + 20, y + 6, 4, 0, Math.PI * 2); ctx.fill(); break;
        case 'sofa': ctx.fillStyle = 'rgba(0,0,0,.12)'; ctx.fillRect(x + 4, y + h - 4, w - 6, 4); ctx.fillStyle = '#6c8cff'; roundRect(x + 4, y + 4, w - 8, h - 8, 5); ctx.fill(); ctx.fillStyle = '#4f6fe0'; ctx.fillRect(x + 4, y + 4, 7, h - 8); ctx.fillStyle = '#86a1ff'; for (let i = 0; i < d.h; i++) ctx.fillRect(x + 13, y + 8 + i * T, w - 20, T - 10); break;
        case 'sidetable': ctx.fillStyle = dark ? '#6b4a2b' : '#c89b6a'; ctx.beginPath(); ctx.ellipse(x + 16, y + 16, 11, 7, 0, 0, Math.PI * 2); ctx.fill(); ctx.fillStyle = '#e8ecf2'; ctx.fillRect(x + 11, y + 11, 8, 6); ctx.fillStyle = '#e2445c'; ctx.fillRect(x + 20, y + 12, 4, 4); break;
        case 'bookshelf': ctx.fillStyle = dark ? '#4a3a2b' : '#a37a52'; ctx.fillRect(x + 4, y - 4, w - 8, h + 4); const cols = ['#e2445c', '#0073ea', '#fdab3d', '#00c875', '#a25ddc', '#579bfc']; for (let r = 0; r < 3; r++) for (let i = 0; i < 5; i++) { ctx.fillStyle = cols[(r + i) % cols.length]; ctx.fillRect(x + 6 + i * 4, y - 1 + r * 10, 3, 8); } break;
        case 'watercooler': ctx.fillStyle = '#e8ecf2'; ctx.fillRect(x + 11, y + 10, 10, 18); ctx.fillStyle = '#9ad7ff'; ctx.fillRect(x + 10, y - 2, 12, 12); ctx.fillStyle = '#cdefff'; ctx.fillRect(x + 12, y, 3, 8); ctx.fillStyle = '#0073ea'; ctx.fillRect(x + 13, y + 14, 2, 3); break;
        case 'reception': ctx.fillStyle = 'rgba(0,0,0,.12)'; ctx.fillRect(x + 4, y + h - 4, w - 8, 4); ctx.fillStyle = dark ? '#3b3f4a' : '#e8ecf2'; roundRect(x + 2, y + 4, w - 4, h - 8, 4); ctx.fill(); ctx.fillStyle = d.color || '#0073ea'; ctx.fillRect(x + 2, y + 4, w - 4, 4); ctx.fillStyle = '#2b2f38'; ctx.fillRect(x + 10, y + 10, 10, 6); ctx.fillStyle = '#e2445c'; ctx.beginPath(); ctx.arc(x + w - 12, y + 12, 3, 0, Math.PI * 2); ctx.fill(); ctx.fillStyle = ink; ctx.font = 'bold 7px sans-serif'; ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.fillText('RECEPCIÓN', x + w / 2, y + 22); break;
        case 'wc': ctx.fillStyle = '#5b6b7c'; ctx.fillRect(x + 4, 2, T - 8, T - 6); ctx.fillStyle = '#e8eef5'; ctx.fillRect(x + 6, 4, T - 12, T - 10); ctx.fillStyle = '#1e293b'; ctx.font = 'bold 9px sans-serif'; ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.fillText('WC', x + T / 2, 14); ctx.fillStyle = '#c0392b'; ctx.fillRect(x + T - 11, 16, 3, 3); break;
        case 'board': ctx.fillStyle = '#fff'; ctx.fillRect(x + 4, 4, w - 8, T - 10); ctx.strokeStyle = '#8c96a5'; ctx.lineWidth = 2; ctx.strokeRect(x + 4, 4, w - 8, T - 10);
            ctx.strokeStyle = '#0073ea'; ctx.lineWidth = 2; ctx.beginPath(); ctx.moveTo(x + 10, 20); ctx.lineTo(x + 24, 12); ctx.lineTo(x + 40, 18); ctx.lineTo(x + 56, 8); ctx.lineTo(x + 80, 14); ctx.stroke(); ctx.fillStyle = '#e2445c'; ctx.fillRect(x + 62, 8, 14, 3); ctx.fillStyle = '#00c875'; ctx.fillRect(x + 62, 13, 9, 3); break;
        case 'clock': { ctx.fillStyle = '#fff'; ctx.beginPath(); ctx.arc(x + T / 2, 13, 9, 0, Math.PI * 2); ctx.fill(); ctx.strokeStyle = '#1e293b'; ctx.lineWidth = 1.5; ctx.stroke(); const t = new Date(); const hA = ((t.getHours() % 12) / 12 + t.getMinutes() / 720) * Math.PI * 2 - Math.PI / 2, mA = (t.getMinutes() / 60) * Math.PI * 2 - Math.PI / 2; ctx.beginPath(); ctx.moveTo(x + T / 2, 13); ctx.lineTo(x + T / 2 + Math.cos(hA) * 4.5, 13 + Math.sin(hA) * 4.5); ctx.stroke(); ctx.beginPath(); ctx.moveTo(x + T / 2, 13); ctx.lineTo(x + T / 2 + Math.cos(mA) * 7, 13 + Math.sin(mA) * 7); ctx.stroke(); break; }
        case 'logo': ctx.fillStyle = '#0073ea'; roundRect(x + 6, 5, 16, 16, 4); ctx.fill(); ctx.fillStyle = '#fff'; ctx.font = 'bold 11px Figtree, sans-serif'; ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.fillText('D', x + 14, 13.5); ctx.fillStyle = ink; ctx.font = '700 10px Figtree, sans-serif'; ctx.textAlign = 'left'; ctx.fillText('Dana', x + 26, 13.5); break;
    }
}

function label(text, x, y, color) {
    ctx.font = '600 11px Figtree, system-ui, sans-serif'; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    const w = ctx.measureText(text).width + 12;
    ctx.fillStyle = 'rgba(255,255,255,.92)'; roundRect(x - w / 2, y - 8, w, 16, 6); ctx.fill();
    ctx.fillStyle = color || '#1e293b'; ctx.fillText(text, x, y);
}
function roundRect(x, y, w, h, r) { ctx.beginPath(); ctx.moveTo(x + r, y); ctx.arcTo(x + w, y, x + w, y + h, r); ctx.arcTo(x + w, y + h, x, y + h, r); ctx.arcTo(x, y + h, x, y, r); ctx.arcTo(x, y, x + w, y, r); ctx.closePath(); }

function drawPerson(p, isMe) {
    const px = (p.x + 0.5) * T, py = (p.y + 1) * T - 4;
    drawCharacter(ctx, px, py, { color: p.color, avatar: p.avatar, dir: p.dir, moving: p.moving, frame: p.frame, size: 40 });
    label(isMe ? p.name + ' (tú)' : p.name, px, py - 52, isMe ? '#0073ea' : '#1e293b');
}
function drawAgent(a, now) {
    const px = (a.px + 0.5) * T, py = (a.py + 1) * T - 4;
    const near = nearId === a.id;
    if (near) { ctx.strokeStyle = a.color; ctx.lineWidth = 2; ctx.setLineDash([4, 3]); ctx.beginPath(); ctx.ellipse(px, py, 16, 7, 0, 0, Math.PI * 2); ctx.stroke(); ctx.setLineDash([]); }
    const atDesk = a.state === 'desk';
    const typing = atDesk && !(a.chatting > 0);
    drawCharacter(ctx, px, py + (a.moving ? 0 : Math.round(Math.sin(a.frame * Math.PI * 2) * 1.2)), { color: a.color, avatar: a.avatar, dir: a.dir || 'down', moving: a.moving, frame: a.frame, size: 40, isAgent: true, typing, hold: a.state === 'stay' && a.activity === 'coffee' ? 'coffee' : null });
    label((a.boss ? '👑 ' : '') + a.name + (a.moodEmoji ? ' ' + a.moodEmoji : ''), px, py - 64, a.color);
    if (a.state === 'stay' || a.state === 'go') { const act = ACT[a.activity]; if (act) { ctx.font = '500 10px Figtree, system-ui, sans-serif'; ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; const tw = ctx.measureText(act.label).width + 10; ctx.fillStyle = 'rgba(255,255,255,.9)'; roundRect(px + 14, py - 50, tw, 14, 6); ctx.fill(); ctx.fillStyle = '#1e293b'; ctx.fillText(act.label, px + 14 + tw / 2, py - 43); } }
    else if (a.chatting > 0) { ctx.font = '500 10px Figtree, system-ui, sans-serif'; ctx.textAlign = 'center'; ctx.fillStyle = 'rgba(255,255,255,.9)'; roundRect(px + 14, py - 50, 22, 14, 6); ctx.fill(); ctx.fillStyle = '#1e293b'; ctx.fillText('💬', px + 25, py - 43); }
    ctx.font = '500 9px Figtree, system-ui, sans-serif'; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    const rw = ctx.measureText(a.role).width + 8; ctx.fillStyle = 'rgba(255,255,255,.85)'; roundRect(px - rw / 2, py - 56, rw, 12, 5); ctx.fill(); ctx.fillStyle = 'rgba(30,41,59,.8)'; ctx.fillText(a.role, px, py - 50);
}
function drawBubble(a, now) {
    if (a.hidden) return;
    const px = (a.px + 0.5) * T, py = (a.py + 1) * T - 4;
    const b = bubbles.get(a.id);
    if (busy.has(a.id)) {
        const n = Math.floor(now / 350) % 4;
        ctx.fillStyle = '#fff'; roundRect(px + 10, py - 90, 34, 18, 8); ctx.fill();
        ctx.fillStyle = '#1e293b'; for (let i = 0; i < 3; i++) { ctx.globalAlpha = i < n ? 1 : .25; ctx.beginPath(); ctx.arc(px + 19 + i * 8, py - 81, 2.2, 0, Math.PI * 2); ctx.fill(); } ctx.globalAlpha = 1;
    } else if (b) {
        ctx.font = '500 11px Figtree, system-ui, sans-serif'; ctx.textAlign = 'left'; ctx.textBaseline = 'top';
        const lines = wrap(b.text, 170), w = Math.min(190, Math.max(...lines.map(l => ctx.measureText(l).width)) + 16), h = lines.length * 14 + 10;
        let bx = px - w / 2, by = py - 76 - h; bx = Math.max(4, Math.min(W * T - w - 4, bx));
        ctx.fillStyle = '#fff'; ctx.strokeStyle = a.color; ctx.lineWidth = 1.5; roundRect(bx, by, w, h, 8); ctx.fill(); ctx.stroke();
        ctx.fillStyle = '#fff'; ctx.beginPath(); ctx.moveTo(px - 5, by + h); ctx.lineTo(px + 5, by + h); ctx.lineTo(px, by + h + 6); ctx.fill();
        ctx.fillStyle = '#1e293b'; lines.forEach((l, i) => ctx.fillText(l, bx + 8, by + 6 + i * 14));
    }
}
function wrap(text, max) {
    const words = text.split(/\s+/), lines = []; let cur = '';
    for (const w of words) { const t = cur ? cur + ' ' + w : w; if (ctx.measureText(t).width > max && cur) { lines.push(cur); cur = w; } else cur = t; if (lines.length === 3) break; }
    if (cur && lines.length < 4) lines.push(cur);
    return lines;
}
