// pagina senuelo: nunca se lee ni transmite lo escrito, solo se informa el envio
import { apiCall } from '../services/api.js';

const token = new URLSearchParams(location.search).get('token') || '';
const valido = /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(token);
const educacion = `phishing-educacion.html?token=${encodeURIComponent(token)}`;
const form = document.getElementById('form-senuelo');
document.getElementById('senuelo-token').value = token;
// quitar el token de la URL evita que quede en el historial
history.replaceState(null, '', location.pathname);

if (!valido) location.replace(educacion);

let enviado = false;
form.addEventListener('submit', async event => {
    event.preventDefault();
    if (enviado) return;

    enviado = true;
    for (const input of form.querySelectorAll('input:not([type="hidden"])')) input.value = '';
    document.getElementById('senuelo-enviar').disabled = true;
    try {
        await apiCall(`/campanias-phishing/senuelo/${encodeURIComponent(token)}`, { timeoutMs: 8000 });
    } catch {
        // si no se pudo registrar, la capacitacion sigue disponible
    }

    location.replace(educacion);
});
