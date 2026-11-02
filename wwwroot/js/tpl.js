// Inserta texto en la posición del cursor de un <input>/<textarea> y notifica a Blazor (evento input).
export function insertAtCaret(id, text) {
    const el = document.getElementById(id);
    if (!el) return;
    el.focus();
    const start = el.selectionStart ?? el.value.length;
    const end = el.selectionEnd ?? el.value.length;
    el.value = el.value.slice(0, start) + text + el.value.slice(end);
    const pos = start + text.length;
    el.setSelectionRange(pos, pos);
    el.dispatchEvent(new Event('input', { bubbles: true }));
}
