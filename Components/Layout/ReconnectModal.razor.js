// Modal de reconexión de Blazor Server.
// Además del comportamiento estándar, cuando la conexión se pierde se sondea el servidor
// periódicamente y la página se recarga sola en cuanto vuelve a responder.
const reconnectModal = document.getElementById("components-reconnect-modal");
reconnectModal.addEventListener("components-reconnect-state-changed", handleReconnectStateChanged);

document.getElementById("components-reconnect-button").addEventListener("click", retry);
document.getElementById("components-resume-button").addEventListener("click", resume);
document.getElementById("components-reload-button").addEventListener("click", () => location.reload());

let pollTimer = null;

function handleReconnectStateChanged(event) {
    const state = event.detail.state;
    if (state === "show") {
        reconnectModal.showModal();
        startPolling();
    } else if (state === "hide") {
        stopPolling();
        reconnectModal.close();
    } else if (state === "failed") {
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
        startPolling();
    } else if (state === "rejected") {
        location.reload();
    }
}

// Sondea el servidor cada 2 s; si responde, recarga (el circuito anterior ya no existe tras un reinicio).
function startPolling() {
    if (pollTimer) return;
    pollTimer = setInterval(async () => {
        try {
            const res = await fetch(document.baseURI, { method: "HEAD", cache: "no-store", redirect: "manual" });
            if (res.ok || res.type === "opaqueredirect" || res.status === 0) {
                // El servidor responde: intentar reconectar el circuito; si no se puede, recargar.
                stopPolling();
                try {
                    const ok = await Blazor.reconnect();
                    if (ok) return;
                } catch { }
                location.reload();
            }
        } catch {
            // servidor aún caído
        }
    }, 2000);
}

function stopPolling() {
    if (pollTimer) { clearInterval(pollTimer); pollTimer = null; }
}

async function retry() {
    document.removeEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    try {
        const successful = await Blazor.reconnect();
        if (!successful) {
            const resumeSuccessful = await Blazor.resumeCircuit();
            if (!resumeSuccessful) location.reload();
            else reconnectModal.close();
        }
    } catch (err) {
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
        startPolling();
    }
}

async function resume() {
    try {
        const successful = await Blazor.resumeCircuit();
        if (!successful) location.reload();
    } catch {
        reconnectModal.classList.replace("components-reconnect-paused", "components-reconnect-resume-failed");
    }
}

async function retryWhenDocumentBecomesVisible() {
    if (document.visibilityState === "visible") await retry();
}
