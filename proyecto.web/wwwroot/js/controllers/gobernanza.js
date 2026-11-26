import { apiCallAuth } from '../services/api.js';
import { el, escapeHtml as esc, toast } from '../utils/ui.js';
import { renderPaginador, enlazarPaginador, conPagina } from '../components/paginacion.js';

const motivos = {
    licencia: 'Licencia',
    vacaciones: 'Vacaciones',
    incidente_reciente: 'Incidente real reciente',
    solicitud: 'Solicitud',
    otro: 'Otro'
};

const day = value => value ? new Date(value).toLocaleDateString('es-BO', { timeZone: 'America/La_Paz' }) : '';
// fecha local (Bolivia) a ISO UTC al inicio del dia
const isoDate = value => value ? new Date(`${value}T00:00:00-04:00`).toISOString() : null;

function error(message) {
    el('gobernanza-error').textContent = message;
    el('gobernanza-error').classList.remove('hidden');
}

async function loadPolicy() {
    const p = await apiCallAuth('/gobernanza/politica');
    el('gobernanza-revision').textContent = `Revisión ${p.revision}`;
    el('gov-aprobacion').checked = p.requiereAprobacion;
    el('gov-ventana').checked = p.ventanaActiva;
    el('gov-habiles').checked = p.soloDiasHabiles;
    el('gov-inicio').value = p.horaInicio;
    el('gov-fin').value = p.horaFin;
    el('gov-maximo').value = p.maxSimulacionesPorPersona;
    el('gov-umbral').value = p.umbralAgregacion;
    el('gov-pretextos').value = p.pretextosProhibidos.join('\n');
    el('gov-ventana-estado').textContent = p.ventanaActiva
        ? (p.ventanaAbiertaAhora ? 'La ventana de envío está abierta en este momento.' : 'La ventana de envío está cerrada en este momento: las campañas no podrán iniciarse hasta que abra.')
        : 'Sin restricción horaria.';
}

// paginador de exclusiones debajo de la tabla
const exclusionesPaginador = document.createElement('div');
el('exclusiones-body').closest('.table-scroll').after(exclusionesPaginador);
let paginaExclusiones = 1;

async function loadExclusions() {
    const data = await apiCallAuth(conPagina(`/gobernanza/exclusiones?vigentes=${!el('gov-historial').checked}`, paginaExclusiones));
    // si la pagina quedo vacia tras revocar, se vuelve a la anterior
    if (!data.items.length && paginaExclusiones > 1) {
        paginaExclusiones--;
        return loadExclusions();
    }

    const rows = data.items;
    exclusionesPaginador.innerHTML = renderPaginador(data, 'exclusiones');
    el('exclusiones-body').innerHTML = rows.length ? rows.map(x => `<tr><td>${esc(x.nombre)}<small class="t-meta"> ${esc(x.correo || '')}</small></td><td>${esc(motivos[x.motivo] || x.motivo)}${x.nota ? `<small class="t-meta"> · ${esc(x.nota)}</small>` : ''}</td>
        <td>${esc(day(x.desde))} – ${x.hasta ? esc(day(x.hasta)) : 'sin fecha de fin'}</td><td>${x.vigente ? 'Vigente' : x.activa ? 'Programada o vencida' : 'Revocada'}</td>
        <td>${x.activa ? `<button class="text-action" type="button" data-revoke="${Number(x.id)}">Revocar</button>` : ''}</td></tr>`).join('')
        : '<tr><td colspan="5" class="table-state">No hay exclusiones para mostrar.</td></tr>';
}

async function loadUsers() {
    const users = await apiCallAuth('/usuarios/catalogo');
    el('exc-usuario').innerHTML = '<option value="">Selecciona una persona…</option>' + users
        .sort((a, b) => `${a.nombre} ${a.apellido}`.localeCompare(`${b.nombre} ${b.apellido}`, 'es'))
        .map(u => `<option value="${Number(u.idUsuario)}">${esc(u.nombre)} ${esc(u.apellido)} · ${esc(u.email)}</option>`).join('');
}

el('form-gobernanza').addEventListener('submit', async event => {
    event.preventDefault();
    const button = event.submitter;
    button.disabled = true;
    try {
        await apiCallAuth('/gobernanza/politica', { method: 'PUT', body: {
            requiereAprobacion: el('gov-aprobacion').checked,
            ventanaActiva: el('gov-ventana').checked,
            soloDiasHabiles: el('gov-habiles').checked,
            horaInicio: Number(el('gov-inicio').value),
            horaFin: Number(el('gov-fin').value),
            maxSimulacionesPorPersona: Number(el('gov-maximo').value),
            umbralAgregacion: Number(el('gov-umbral').value),
            pretextosProhibidos: el('gov-pretextos').value.split('\n').map(t => t.trim()).filter(Boolean),
        } });
        toast('Política guardada.');
        await loadPolicy();
    } catch (e) { error(e.message); } finally { button.disabled = false; }
});

el('form-exclusion').addEventListener('submit', async event => {
    event.preventDefault();
    const button = event.submitter;
    button.disabled = true;
    try {
        await apiCallAuth('/gobernanza/exclusiones', {
            method: 'POST',
            body: {
                idUsuario: Number(el('exc-usuario').value),
                motivo: el('exc-motivo').value,
                nota: el('exc-nota').value.trim() || null, desde: isoDate(el('exc-desde').value), hasta: isoDate(el('exc-hasta').value)
            } });
        event.target.reset();
        toast('Persona excluida de las simulaciones.');
        paginaExclusiones = 1;
        await loadExclusions();
    } catch (e) { error(e.message); } finally { button.disabled = false; }
});

el('exclusiones-body').addEventListener('click', async event => {
    const revoke = event.target.closest('[data-revoke]');
    if (!revoke) return;
    if (!confirm('¿Revocar esta exclusión? La persona volverá a ser elegible para las próximas campañas.')) return;

    revoke.disabled = true;
    try {
        await apiCallAuth(`/gobernanza/exclusiones/${Number(revoke.dataset.revoke)}`, { method: 'DELETE' });
        await loadExclusions();
    }
    catch (e) {
        error(e.message);
        revoke.disabled = false;
    }
});

el('gov-historial').addEventListener('change', () => {
    paginaExclusiones = 1;
    loadExclusions().catch(e => error(e.message));
});

enlazarPaginador(exclusionesPaginador, 'exclusiones', pagina => {
    paginaExclusiones = pagina;
    loadExclusions().catch(e => error(e.message));
});

try { await Promise.all([loadPolicy(), loadExclusions(), loadUsers()]); } catch (e) { error(e.message); }
