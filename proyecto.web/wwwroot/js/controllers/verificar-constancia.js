// verificacion publica: confirma autenticidad sin mostrar la calificacion
import { apiCall } from '../services/api.js';
import { escapeHtml as esc } from '../utils/ui.js';

const form = document.getElementById('form-verificar'),
    input = document.getElementById('codigo'),
    out = document.getElementById('resultado');
const fecha = v => new Date(v).toLocaleDateString('es-BO', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'America/La_Paz' });

async function verificar(codigo) {
    out.className = '';
    out.textContent = 'Verificando…';
    try {
        const r = await apiCall(`/constancias/verificar/${encodeURIComponent(codigo.trim())}`, { method: 'GET' });
        if (r.valida) {
            out.className = 'is-valid';
            out.innerHTML = `<strong>Constancia auténtica</strong><p>${esc(r.nombreCompleto)} aprobó «${esc(r.actividad)}» (${esc(r.tema)}) el ${esc(fecha(r.fechaAprobacion))}.</p><small>Código ${esc(r.codigo)}</small>`;
        } else {
            out.className = 'is-invalid';
            out.innerHTML = '<strong>No se encontró una constancia con ese código.</strong><p>Revisa que esté escrito tal como figura en el documento.</p>';
        }
    } catch (e) {
        out.className = 'is-invalid';
        out.textContent = e.message;
    }
}

form.addEventListener('submit', event => {
    event.preventDefault();
    verificar(input.value);
});

const inicial = new URLSearchParams(location.search).get('codigo');
if (inicial) {
    input.value = inicial;
    verificar(inicial);
}
