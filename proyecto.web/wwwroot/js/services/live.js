import { TokenStore } from '../core/session.js';
import { intentarRenovarSesion } from './api.js';

// canal en vivo SSE sobre fetch para enviar el token en la cabecera, con reconexion exponencial
export function subscribeLive(path, { onSnapshot, onStatus = () => {} }) {
    const base = globalThis.APP_CONFIG?.apiUrl?.replace(/\/$/, '');
    let stopped = false, controller = null, attempt = 0;
    const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

    function dispatch(block) {
        let event = 'message', data = '';
        for (const line of block.split('\n')) {
            if (line.startsWith('event:')) event = line.slice(6).trim();
            else if (line.startsWith('data:')) data += line.slice(5).trim();
        }

        if (event === 'snapshot' && data) {
            try {
                onSnapshot(JSON.parse(data));
            } catch { /* evento incompleto, se ignora */ }
        }
    }

    async function run() {
        while (!stopped) {
            onStatus(attempt ? 'reconnecting' : 'connecting');
            try {
                if (!TokenStore.isValid() && !await intentarRenovarSesion()) {
                    onStatus('offline');
                    document.dispatchEvent(new CustomEvent('session:expired'));
                    return;
                }

                controller = new AbortController();
                const response = await fetch(`${base}${path}`, { cache: 'no-store', signal: controller.signal,
                    headers: { Accept: 'text/event-stream', Authorization: `Bearer ${TokenStore.getAccessToken()}` }
                });

                if (response.status === 401) {
                    await intentarRenovarSesion();
                    throw new Error('unauthorized');
                }

                if (!response.ok || !response.body) throw new Error(`HTTP ${response.status}`);

                attempt = 0;
                onStatus('live');
                const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
                let buffer = '';
                for (;;) {
                    const { value, done } = await reader.read();
                    if (done) break;

                    buffer += value.replace(/\r\n/g, '\n');
                    let index;
                    while ((index = buffer.indexOf('\n\n')) >= 0) {
                        dispatch(buffer.slice(0, index));
                        buffer = buffer.slice(index + 2);
                    }
                }

                // el servidor cierra al vencer el token, la pausa evita un bucle de reconexion
                await wait(1000);
            } catch {
                if (stopped) return;

                attempt++;
                onStatus(attempt > 3 ? 'offline' : 'reconnecting');
                await wait(Math.min(30000, 1000 * 2 ** Math.min(attempt, 5)));
            }
        }
    }

    run();
    const stop = () => {
        stopped = true;
        controller?.abort();
    };

    window.addEventListener('pagehide', stop, { once: true });
    return stop;
}
