// Grabación de notas de voz para el chat interno (MediaRecorder → base64).
let rec, chunks, stream;

export async function start() {
    stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    chunks = [];
    rec = new MediaRecorder(stream);
    rec.ondataavailable = e => { if (e.data && e.data.size > 0) chunks.push(e.data); };
    rec.start();
    return true;
}

export function stop() {
    return new Promise(resolve => {
        if (!rec) { resolve(null); return; }
        rec.onstop = async () => {
            try {
                const type = rec.mimeType || 'audio/webm';
                const blob = new Blob(chunks, { type });
                const bytes = new Uint8Array(await blob.arrayBuffer());
                let bin = '';
                const chunk = 0x8000;
                for (let i = 0; i < bytes.length; i += chunk) {
                    bin += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
                }
                resolve({ base64: btoa(bin), type, size: bytes.length });
            } catch (e) { resolve(null); }
            finally { if (stream) stream.getTracks().forEach(t => t.stop()); }
        };
        rec.stop();
    });
}

export function cancel() {
    try { if (rec && rec.state !== 'inactive') rec.stop(); } catch (e) {}
    try { if (stream) stream.getTracks().forEach(t => t.stop()); } catch (e) {}
}
