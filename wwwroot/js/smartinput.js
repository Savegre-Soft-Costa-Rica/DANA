// Entrada de texto sin "lag" en Blazor Server: el DOM manda, avisamos al servidor por 'input'
// (opcionalmente con debounce) y el servidor solo reescribe el valor cuando cambia por código
// (insertar mención, limpiar). Así nunca se reenvía un value viejo que borre letras.
const reg = new WeakMap();

export function init(el, dotnet, debounceMs) {
    let t;
    const handler = () => {
        if (debounceMs > 0) { clearTimeout(t); t = setTimeout(() => dotnet.invokeMethodAsync('OnInput', el.value), debounceMs); }
        else dotnet.invokeMethodAsync('OnInput', el.value);
    };
    el.addEventListener('input', handler);
    reg.set(el, { dotnet, handler });
}

// Reescribe el valor solo desde el servidor (programático). Mantiene el cursor al final si focus=true.
export function setValue(el, value, focus) {
    if (el.value !== value) el.value = value ?? '';
    if (focus) {
        try { el.focus(); const n = el.value.length; el.setSelectionRange(n, n); } catch (e) { }
    }
}

export function dispose(el) {
    const r = reg.get(el);
    if (r) { el.removeEventListener('input', r.handler); reg.delete(el); }
}
