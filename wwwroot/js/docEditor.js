// Interop para el editor visual de documentos (Toast UI Editor en modo WYSIWYG).
// El contenido se mantiene en Markdown por debajo (getMarkdown/setMarkdown), para no
// romper la exportación a Word/PDF ni el relleno de marcadores.

let assetsPromise;

function loadAssets() {
    if (assetsPromise) return assetsPromise;
    assetsPromise = new Promise((resolve, reject) => {
        const addCss = (href) => {
            if (!document.querySelector(`link[data-toastui="${href}"]`)) {
                const l = document.createElement('link');
                l.rel = 'stylesheet'; l.href = href; l.setAttribute('data-toastui', href);
                document.head.appendChild(l);
            }
        };
        addCss('/lib/toastui/toastui-editor.min.css');
        addCss('/lib/toastui/toastui-editor-dark.min.css');

        if (window.toastui && window.toastui.Editor) { resolve(); return; }

        const main = document.createElement('script');
        main.src = '/lib/toastui/toastui-editor-all.min.js'; // bundle all-in-one (incluye ProseMirror)
        main.onload = () => {
            const i18n = document.createElement('script');
            i18n.src = '/lib/toastui/toastui-editor-i18n-es.min.js';
            i18n.onload = () => resolve();
            i18n.onerror = () => resolve(); // el idioma es opcional
            document.body.appendChild(i18n);
        };
        main.onerror = () => reject(new Error('No se pudo cargar el editor'));
        document.body.appendChild(main);
    });
    return assetsPromise;
}

function prefersDark() {
    try {
        const bg = getComputedStyle(document.body).backgroundColor || '';
        const m = bg.match(/\d+/g);
        if (m && m.length >= 3) {
            const [r, g, b] = m.map(Number);
            const lum = 0.2126 * r + 0.7152 * g + 0.0722 * b; // luminancia 0..255
            return lum < 128;
        }
    } catch { /* ignora */ }
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
}

const instances = {};
const dirtyTimers = {};

export async function init(id, markdown, dotnet) {
    await loadAssets();
    const el = document.getElementById(id);
    if (!el || !window.toastui || !window.toastui.Editor) return;
    if (instances[id]) { try { instances[id].destroy(); } catch {} delete instances[id]; }

    let editor;
    const opts = {
        el,
        height: '100%',
        minHeight: '400px',
        initialEditType: 'wysiwyg',
        previewStyle: 'tab',
        hideModeSwitch: true,          // sin modo "código" (Markdown): solo visual
        usageStatistics: false,
        theme: prefersDark() ? 'dark' : 'default',
        language: 'es-ES',
        initialValue: markdown || '',
        autofocus: false,
        toolbarItems: [
            ['heading', 'bold', 'italic', 'strike'],
            ['hr', 'quote'],
            ['ul', 'ol', 'indent', 'outdent'],
            ['table', 'link'],
            ['code', 'codeblock']
        ],
        events: {
            // Solo avisa "hay cambios" (con debounce). El Markdown se extrae al guardar
            // con getMarkdown, para no enviar todo el texto por SignalR en cada tecla.
            change: () => {
                if (!dotnet) return;
                clearTimeout(dirtyTimers[id]);
                dirtyTimers[id] = setTimeout(() => { try { dotnet.invokeMethodAsync('OnEditorDirty'); } catch {} }, 500);
            }
        }
    };
    try { editor = new toastui.Editor(opts); }
    catch { delete opts.language; editor = new toastui.Editor(opts); } // reintenta sin idioma si faltó el i18n
    instances[id] = editor;
}

export function setMarkdown(id, md) {
    const e = instances[id];
    if (e) e.setMarkdown(md || '', false);
}

export function getMarkdown(id) {
    const e = instances[id];
    return e ? e.getMarkdown() : null;
}

export function destroy(id) {
    const e = instances[id];
    if (e) { try { e.destroy(); } catch {} delete instances[id]; }
}
