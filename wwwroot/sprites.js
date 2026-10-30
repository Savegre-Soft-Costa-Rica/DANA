// Personajes "pixel art" dibujados por código (sin imágenes). Compartido por la oficina y las tarjetas de agentes.
const HAIR = ['#3b2314', '#f2c14e', '#1f1f1f', '#b5651d', '#7a3e9d', '#d94f4f', '#e8e8e8', '#2f6f73'];
const SKIN = ['#f1c9a5', '#d9a577', '#8d5524', '#ffdbac', '#c68642', '#f1c9a5', '#e0ac69', '#8d5524'];

/** Dibuja un personaje con el pie en (x, y). size = alto en px (32-48). frame = fase de animación (0..1). */
export function drawCharacter(ctx, x, y, { color = '#0073ea', avatar = 0, dir = 'down', moving = false, frame = 0, size = 40, isAgent = false, alpha = 1, typing = false, hold = null } = {}) {
    const u = size / 16;                     // unidad de "pixel"
    const a = avatar % 8;
    const bob = moving ? Math.round(Math.sin(frame * Math.PI * 2) * u * 0.6) : 0;
    const legSwing = moving ? Math.sin(frame * Math.PI * 2) : 0;
    ctx.save();
    ctx.globalAlpha = alpha;
    ctx.translate(Math.round(x), Math.round(y));
    // sombra
    ctx.fillStyle = 'rgba(0,0,0,.18)';
    ctx.beginPath(); ctx.ellipse(0, 0, 5 * u, 2 * u, 0, 0, Math.PI * 2); ctx.fill();
    const px = (cx, cy, w, h, c) => { ctx.fillStyle = c; ctx.fillRect(Math.round(cx * u), Math.round(cy * u), Math.round(w * u), Math.round(h * u)); };
    // piernas
    px(-3, -4 + legSwing * 1.2, 2.5, 4, '#2c3e50');
    px(0.5, -4 - legSwing * 1.2, 2.5, 4, '#2c3e50');
    px(-3.5, -1 + legSwing * 1.2, 3, 1.2, '#1b1b1b');
    px(0.5, -1 - legSwing * 1.2, 3, 1.2, '#1b1b1b');
    ctx.translate(0, bob);
    // cuerpo
    px(-4, -10, 8, 6.5, color);
    px(-4, -10, 8, 1.2, shade(color, -25));
    // brazos (tecleando: se extienden hacia delante y alternan arriba/abajo)
    if (typing) {
        const t1 = Math.sin(frame * Math.PI * 2 * 3) * 0.8, t2 = Math.sin(frame * Math.PI * 2 * 3 + Math.PI) * 0.8;
        px(-5.5, -9.5, 1.6, 3, color); px(3.9, -9.5, 1.6, 3, color);
        px(-5.5, -6.8 + t1, 3.2, 1.6, color); px(2.3, -6.8 + t2, 3.2, 1.6, color);
        px(-2.6, -6.8 + t1, 1.4, 1.6, SKIN[a]); px(1.2, -6.8 + t2, 1.4, 1.6, SKIN[a]);
    } else {
        px(-5.5, -9.5, 1.6, 5, color);
        px(3.9, -9.5, 1.6, 5, color);
        px(-5.5, -4.8, 1.6, 1.4, SKIN[a]);
        px(3.9, -4.8, 1.6, 1.4, SKIN[a]);
    }
    if (hold === 'coffee') { px(4.2, -7.5, 2.4, 2.6, '#fff'); px(4.6, -7.1, 1.6, 1.2, '#6b4a2b'); }
    // cabeza
    px(-3.5, -16, 7, 6.5, SKIN[a]);
    // pelo según estilo
    const hair = HAIR[a];
    if (a === 1 || a === 5) { px(-4, -17, 8, 3, hair); px(-4, -17, 1.2, 7, hair); px(2.8, -17, 1.2, 7, hair); }
    else if (a === 3) { px(-4, -17, 8, 2.5, hair); px(2, -17, 2, 5, hair); }
    else if (a === 6) { px(-4, -17, 8, 2.2, hair); }
    else if (a === 4) { px(-4, -17.5, 8, 3.5, hair); px(-4, -17.5, 1.5, 6, hair); }
    else { px(-4, -17, 8, 2.5, hair); px(-4, -17, 1, 4, hair); px(3, -17, 1, 4, hair); }
    // cara
    if (dir !== 'up') {
        const ox = dir === 'left' ? -1 : dir === 'right' ? 1 : 0;
        px(-2 + ox, -13, 1.2, 1.4, '#1b1b1b');
        px(1 + ox, -13, 1.2, 1.4, '#1b1b1b');
        if (dir === 'down') px(-1, -11, 2, 0.7, '#b0624a');
    }
    if (isAgent) { // insignia de IA
        px(2.6, -11.2, 3, 2.2, '#fff');
        px(2.6, -11.2, 3, 2.2, 'rgba(0,0,0,0)');
        ctx.fillStyle = shade(color, -40); ctx.font = `bold ${Math.round(2.1 * u)}px sans-serif`; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
        ctx.fillText('AI', 4.1 * u, -10.1 * u);
    }
    ctx.restore();
}

export function shade(hex, amt) {
    const n = parseInt(hex.replace('#', ''), 16);
    if (isNaN(n)) return hex;
    const c = v => Math.max(0, Math.min(255, v + amt));
    return `rgb(${c(n >> 16)},${c((n >> 8) & 255)},${c(n & 255)})`;
}

/** Dibuja todos los <canvas class="agent-sprite" data-avatar data-color> de la página. */
export function renderAll() {
    document.querySelectorAll('canvas.agent-sprite').forEach(cv => {
        const ctx = cv.getContext('2d');
        ctx.clearRect(0, 0, cv.width, cv.height);
        drawCharacter(ctx, cv.width / 2, cv.height - 6, { color: cv.dataset.color || '#0073ea', avatar: +(cv.dataset.avatar || 0), size: cv.height * 0.8, isAgent: true });
    });
}

window.danaSprites = { renderAll, drawCharacter };
