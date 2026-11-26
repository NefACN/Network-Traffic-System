import { apiCallAuth } from '../services/api.js';
import { el, escapeHtml as esc, toast } from '../utils/ui.js';
import { renderPaginador, enlazarPaginador, conPagina } from '../components/paginacion.js';

const estados = { recibido: 'Recibido', en_analisis: 'En análisis', resuelto: 'Resuelto' };
const prioridades = { alta: 'Alta', media: 'Media', baja: 'Baja' };
const canales = { correo: 'Correo', sms: 'SMS', whatsapp: 'WhatsApp', llamada: 'Llamada', otro: 'Otro' };
const interacciones = { ninguna: 'Sin interacción', clic: 'Hizo clic', adjunto: 'Abrió un adjunto', datos: 'Entregó datos' };
const clasificaciones = { malicioso: 'Malicioso confirmado', spam: 'Correo no deseado', legitimo: 'Legítimo', simulacion: 'Simulación' };
const prioClass = { alta: 'nist-alta', media: 'nist-media', baja: 'nist-baja' };
const date = value => value ? new Date(value).toLocaleString('es-BO', { timeZone: 'America/La_Paz' }) : '—';
const hours = value => value == null ? 'Sin datos' : value < 48 ? `${Number(value).toFixed(1)} h` : `${(value / 24).toFixed(1)} días`;
let page = 1, current = null, version = 0;

function error(message) {
    el('incidentes-error').textContent = message;
    el('incidentes-error').classList.remove('hidden');
}

async function load() {
    const request = ++version;
    const q = new URLSearchParams();
    if (el('filtro-estado').value) q.set('estado', el('filtro-estado').value);
    if (el('filtro-prioridad').value) q.set('prioridad', el('filtro-prioridad').value);
    try {
        const data = await apiCallAuth(conPagina(`/incidentes?${q}`, page));
        if (request !== version) return;

        const i = data.indicadores;
        el('incidentes-indicadores').innerHTML = [
            ['Recibidos sin atender', i.porEstado.recibido], ['En análisis', i.porEstado.en_analisis], ['Resueltos', i.porEstado.resuelto],
            ['Maliciosos confirmados', i.porClasificacion.malicioso],
            ['Mediana hasta la atención', hours(i.medianaHorasHastaAtencion)],
            ['Mediana hasta la resolución', hours(i.medianaHorasHastaResolucion)],
        ].map(([label, value]) => `<article class="results-card"><span>${esc(label)}</span><strong>${esc(value)}</strong></article>`).join('');
        el('incidentes-body').innerHTML = data.items.length ? data.items.map(x => `<tr><td>${esc(x.codigo)}</td><td class="inc-wrap">${esc(x.asunto)}<small class="t-meta"> ${esc(x.sucursal || '')}${x.area ? ` / ${esc(x.area)}` : ''}</small></td>
            <td>${esc(canales[x.canal] || x.canal)} · ${esc(interacciones[x.interaccion] || x.interaccion)}</td><td><span class="chip ${prioClass[x.prioridad] || ''}">${esc(prioridades[x.prioridad] || x.prioridad)}</span></td>
            <td>${esc(estados[x.estado] || x.estado)}${x.clasificacion ? `<small class="t-meta"> · ${esc(clasificaciones[x.clasificacion])}</small>` : ''}</td><td>${esc(date(x.fechaCreacion))}<small class="t-meta"> ${esc(x.reportadoPor)}</small></td>
            <td><button class="text-action" type="button" data-open="${Number(x.id)}">Atender</button></td></tr>`).join('')
            : '<tr><td colspan="7" class="table-state">No hay incidentes para estos filtros.</td></tr>';
        el('incidentes-paginas').innerHTML = renderPaginador(data, 'incidentes');
    } catch (e) { if (request === version) error(e.message); }
}

function toggleResolution() {
    const resolving = el('inc-estado').value === 'resuelto' && current?.estado !== 'resuelto';
    el('inc-clasificacion-campo').hidden = !resolving;
    el('inc-respuesta-campo').hidden = !resolving;
}

async function open(id) {
    el('inc-error').textContent = '';
    el('incidente-detalle').textContent = 'Consultando…';
    el('dialog-incidente').showModal();
    try {
        current = await apiCallAuth(`/incidentes/${id}`);
        el('incidente-titulo').textContent = `${current.codigo} · ${estados[current.estado]}`;
        el('incidente-detalle').innerHTML = `<p><strong>${esc(current.asunto)}</strong></p>
            <p>${esc(canales[current.canal])}${current.remitente ? ` · remitente: ${esc(current.remitente)}` : ''} · ${esc(interacciones[current.interaccion])}</p>
            <p class="material-copy">${esc(current.descripcion)}</p>
            <p class="panel-caption">Reportado por ${esc(current.reportadoPor)} (${esc(current.correo || '')}) · ${esc(current.sucursal || 'Sin sucursal')} / ${esc(current.area || 'Sin área')} · ${esc(date(current.fechaCreacion))}${current.analista ? ` · analista: ${esc(current.analista)}` : ''}</p>
            ${current.respuesta ? `<p><strong>Respuesta enviada:</strong> ${esc(current.respuesta)}</p>` : ''}
            <h3>Historial</h3><ol class="inc-timeline">${current.seguimiento.map(s => `<li><strong>${esc(date(s.fecha))}</strong> · ${s.estadoAnterior ? `${esc(estados[s.estadoAnterior])} → ` : ''}${esc(estados[s.estadoNuevo])}${s.autor ? ` · ${esc(s.autor)}` : ''} <span class="chip ${s.publica ? 'nist-baja' : 'is-muted'}">${s.publica ? 'Visible para quien reportó' : 'Interna'}</span>${s.nota ? `<p>${esc(s.nota)}</p>` : ''}</li>`).join('')}</ol>`;
        el('form-incidente').reset();
        el('inc-estado').value = current.estado;
        el('inc-prioridad').value = current.prioridad;
        toggleResolution();
    } catch (e) { el('incidente-detalle').textContent = e.message; }
}

el('inc-estado').addEventListener('change', toggleResolution);
el('form-incidente').addEventListener('submit', async event => {
    event.preventDefault();
    if (!current) return;

    const button = event.submitter;
    button.disabled = true;
    el('inc-error').textContent = '';
    const resolving = el('inc-estado').value === 'resuelto' && current.estado !== 'resuelto';
    try {
        await apiCallAuth(`/incidentes/${current.id}`, { method: 'PATCH', body: {
            estado: el('inc-estado').value, nota: el('inc-nota').value.trim() || null, notaPublica: el('inc-publica').checked,
            prioridad: el('inc-prioridad').value !== current.prioridad ? el('inc-prioridad').value : null,
            clasificacion: resolving ? el('inc-clasificacion').value || null : null,
            respuesta: resolving ? el('inc-respuesta').value.trim() || null : null,
        } });
        toast('Avance registrado.');
        await open(current.id);
        await load();
    } catch (e) { el('inc-error').textContent = e.message; } finally { button.disabled = false; }
});

el('incidentes-body').addEventListener('click', event => {
    const b = event.target.closest('[data-open]');
    if (b) open(Number(b.dataset.open));
});

enlazarPaginador(el('incidentes-paginas'), 'incidentes', pagina => {
    page = pagina;
    load();
});

el('incidentes-filtros').addEventListener('change', () => {
    page = 1;
    load();
});

await load();
