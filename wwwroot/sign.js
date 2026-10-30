// Panel de firma manuscrita para /firmar/{token}
(function () {
    var c = document.getElementById('sigpad'); if (!c) return;
    var ctx = c.getContext('2d'), drawing = false, has = false, last = null;
    ctx.lineWidth = 2.2; ctx.lineCap = 'round'; ctx.strokeStyle = '#1e293b';
    function pos(e) { var r = c.getBoundingClientRect(), t = e.touches ? e.touches[0] : e; return { x: (t.clientX - r.left) * c.width / r.width, y: (t.clientY - r.top) * c.height / r.height }; }
    function start(e) { drawing = true; last = pos(e); e.preventDefault(); }
    function move(e) { if (!drawing) return; var p = pos(e); ctx.beginPath(); ctx.moveTo(last.x, last.y); ctx.lineTo(p.x, p.y); ctx.stroke(); last = p; has = true; e.preventDefault(); }
    function end() { drawing = false; }
    c.addEventListener('mousedown', start); c.addEventListener('mousemove', move); window.addEventListener('mouseup', end);
    c.addEventListener('touchstart', start, { passive: false }); c.addEventListener('touchmove', move, { passive: false }); c.addEventListener('touchend', end);
    document.getElementById('sigclear').onclick = function () { ctx.clearRect(0, 0, c.width, c.height); has = false; };
    document.getElementById('signForm').onsubmit = function () { document.getElementById('sigdata').value = has ? c.toDataURL('image/png') : ''; };
})();
