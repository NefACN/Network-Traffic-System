import { apiCallAuth } from '../services/api.js';
import { el, escapeHtml as esc } from '../utils/ui.js';
import { renderPaginador, enlazarPaginador } from '../components/paginacion.js';

const audit = document.body.dataset.page === 'auditoria';
let data = null, report = null, version = 0, page = 1, detailVersion = 0;
const date = value => value ? new Date(value).toLocaleString('es-BO', { timeZone: 'America/La_Paz' }) : 'Sin fecha';
const table = (headers, rows) => `<div class="results-scroll"><table class="results-table"><thead><tr>${headers.map(h => `<th>${esc(h)}</th>`).join('')}</tr></thead><tbody>${rows || `<tr><td colspan="${headers.length}">No hay registros para estos criterios.</td></tr>`}</tbody></table></div>`;

function failure(e) {
    el('reporting-error').textContent = e.message;
    el('reporting-error').classList.remove('hidden');
}

function iso(id, end = false) {
    return el(id).value ? el(id).value + (end ? 'T23:59:59.9999999-04:00' : 'T00:00:00-04:00') : null;
}

const filters = audit
    ? '<label>Usuario<select id="audit-user"><option value="">Todos</option></select></label><label>Operación<select id="audit-action"><option value="">Todas</option></select></label>'
    : '<label>Tipo de reporte<select id="report-type"><option value="preventivo">Campañas y concientización</option><option value="http">Análisis HTTP</option></select></label><label id="report-branch-label">Sucursal<select id="report-branch"><option value="">Selecciona una sucursal</option></select></label><label id="report-area-label">Área<select id="report-area"><option value="">Todas</option></select></label><label id="report-run-label" hidden>Ejecución HTTP<select id="report-run"><option value="">Selecciona una ejecución</option></select></label><label id="report-candidate-label" hidden><span>Selección HTTP</span><select id="report-candidate"><option value="all">Todos los resultados</option><option value="candidate">Solo candidatas</option></select></label>';
el('reporting-app').innerHTML = `<section class="page-heading"><div><p class="eyebrow">CONSULTA ADMINISTRATIVA</p><h1>${audit ? 'Auditoría' : 'Reportes y exportación'}</h1><p>${audit ? 'Operaciones registradas, disponibles para consulta de solo lectura.' : 'Revisa los indicadores del periodo y conserva una copia exportada.'}</p></div></section><div id="reporting-error" class="page-notice hidden" role="alert"></div><section class="panel"><form class="results-filters" id="reporting-form">${filters}<label>Desde<input type="date" id="reporting-from" ${audit ? '' : 'required'}></label><label>Hasta<input type="date" id="reporting-to" ${audit ? '' : 'required'}></label><button class="btn btn-primary" id="reporting-submit" type="submit">${audit ? 'Consultar operaciones' : 'Generar reporte'}</button></form><p class="results-note">Fechas en hora de Bolivia.${audit ? '' : ' En reportes preventivos se selecciona una sucursal. Para HTTP se elige una de las últimas 200 ejecuciones disponibles.'}</p></section><p id="reporting-status" class="results-note" role="status">Consultando opciones…</p><div id="reporting-content"></div>`;

function invalidate() {
    version++;
    report = null;
    page = 1;
    el('reporting-content').replaceChildren();
    el('reporting-submit').disabled = false;
    el('reporting-status').textContent = 'Criterios modificados. Confirma la consulta para ver los resultados.';
}

function reportType() {
    const http = el('report-type').value === 'http';
    for (const id of ['report-branch-label', 'report-area-label']) el(id).hidden = http;
    for (const id of ['report-run-label', 'report-candidate-label']) el(id).hidden = !http;
}

function showReport(r) {
    el('reporting-content').innerHTML = `<section class="panel"><div class="panel-heading"><h2>${r.tipo === 'http' ? 'Reporte de análisis HTTP' : 'Reporte preventivo'}</h2><button class="btn btn-primary" type="button" id="report-export">Exportar reporte</button></div><p>${r.parcial ? 'Resultados parciales' : 'Copia consolidada'} · Generado: ${esc(date(r.generadoEn))}</p><p>Periodo: ${esc(date(r.desde))} — ${esc(date(r.hasta))}</p>${table(['Criterio', 'Selección'], r.criterios.map(c => `<tr><td>${esc(c.nombre)}</td><td>${esc(c.valor)}</td></tr>`).join(''))}${table(['Indicador', 'Resultado'], r.indicadores.map(c => `<tr><td>${esc(c.nombre)}</td><td>${esc(c.valor)}</td></tr>`).join(''))}</section>${r.tablas.map(t => `<section class="panel"><h2>${esc(t.titulo)}</h2>${table(t.columnas, t.filas.map(row => `<tr>${row.map(v => `<td>${esc(v)}</td>`).join('')}</tr>`).join(''))}</section>`).join('')}<ul class="results-note">${r.notas.map(n => `<li>${esc(n)}</li>`).join('')}</ul>`;
}

function auditParams() {
    const p = new URLSearchParams({ pagina: page, tamanio: 25 });
    if (el('audit-user').value) p.set('idUsuario', el('audit-user').value);
    if (el('audit-action').value) p.set('accion', el('audit-action').value);
    if (iso('reporting-from')) p.set('desde', iso('reporting-from'));
    if (iso('reporting-to', true)) p.set('hasta', iso('reporting-to', true));
    return p;
}

async function load() {
    const request = ++version;
    el('reporting-submit').disabled = true;
    el('reporting-error').classList.add('hidden');
    el('reporting-content').replaceChildren();
    report = null;
    el('reporting-status').textContent = 'Preparando información…';
    try {
        if (audit) {
            const r = await apiCallAuth('/auditoria/consulta?' + auditParams());
            if (request !== version) return;

            const username = a => a.nombreUsuario || data.usuarios.find(u => u.id === a.idUsuario)?.nombre || 'Sin usuario identificado';
            el('reporting-content').innerHTML = `<section class="panel">${table(['Fecha', 'Usuario', 'Operación', 'Elemento afectado', 'Resultado', 'Detalle'], r.items.map(a => `<tr><td>${esc(date(a.fechaHora))}</td><td>${esc(username(a))}</td><td>${esc(a.accion)}</td><td>${a.entidad ? `${esc(a.entidad)}${a.idEntidadAfectada ? ` #${esc(a.idEntidadAfectada)}` : ''}` : '—'}</td><td>${a.exitoso ? 'Exitosa' : 'No exitosa'}</td><td><button class="text-action" type="button" data-audit-id="${Number(a.id)}">Ver detalle</button></td></tr>`).join(''))}${renderPaginador(r, 'auditoria')}</section>`;
        } else {
            const r = await apiCallAuth('/reportes', {
                method: 'POST',
                body: {
                    tipo: el('report-type').value,
                    desde: iso('reporting-from'),
                    hasta: iso('reporting-to', true),
                    idSucursal: el('report-branch').value ? Number(el('report-branch').value) : null,
                    idArea: el('report-area').value ? Number(el('report-area').value) : null,
                    idEjecucion: el('report-run').value || null,
                    soloCandidatas: el('report-candidate').value === 'candidate'
                } });
            if (request !== version) return;

            report = r;
            showReport(r);
        }

        el('reporting-status').textContent = audit
            ? 'Consulta completada. Los registros se mantienen sin modificaciones.'
            : 'Reporte listo para revisar. La copia para descarga se conserva durante 15 minutos.';
    } catch (e) {
        if (request === version) {
            failure(e);
            el('reporting-status').textContent = 'No se pudo preparar la información.';
        }
    }
    finally { if (request === version) el('reporting-submit').disabled = false; }
}

el('reporting-form').addEventListener('submit', event => {
    event.preventDefault();
    page = 1;
    load();
});

enlazarPaginador(el('reporting-content'), 'auditoria', pagina => {
    page = pagina;
    load();
});

el('reporting-form').addEventListener('change', () => {
    invalidate();
    if (!audit) reportType();
});

document.addEventListener('click', async event => {
    if (event.target.closest('#report-export') && report) {
        el('export-error').textContent = '';
        el('export-dialog').showModal();
    }

    const operation = event.target.closest('[data-audit-id]');
    if (!operation) return;

    const request = ++detailVersion;
    el('audit-detail').textContent = 'Consultando operación…';
    el('audit-dialog').showModal();
    try {
        const r = await apiCallAuth('/auditoria/' + Number(operation.dataset.auditId));
        if (request !== detailVersion || !el('audit-dialog').open) return;

        el('audit-detail').innerHTML = table(['Campo', 'Valor'], [
            ['Usuario', r.nombreUsuario || data.usuarios.find(u => u.id === r.idUsuario)?.nombre || 'Sin usuario identificado'],
            ['Fecha', date(r.fechaHora)],
            ['Acción', r.accion],
            ['Resultado', r.exitoso ? 'Exitoso' : 'No exitoso'],
            ['Entidad', r.entidad || 'Sin entidad'],
            ['Identificador afectado', r.idEntidadAfectada || 'Sin identificador'],
            ['Origen', r.ipOrigen || 'Sin registro']
        ].map(row => `<tr><td>${esc(row[0])}</td><td>${esc(row[1])}</td></tr>`).join(''))
            + '<h3>Detalles registrados</h3><pre id="audit-json" style="white-space:pre-wrap;overflow-wrap:anywhere"></pre>';
        let details = r.detalles || 'Sin detalles adicionales';
        try {
            details = JSON.stringify(JSON.parse(details), null, 2);
        } catch { /* detalle heredado en texto plano */ }
        el('audit-json').textContent = details;
    } catch (e) { if (request === detailVersion) el('audit-detail').textContent = e.message; }
});

el('audit-dialog')?.addEventListener('close', () => { detailVersion++; });
el('export-confirm')?.addEventListener('click', async () => {
    if (!report) return;

    const snapshot = report, button = el('export-confirm');
    button.disabled = true;
    try {
        const format = el('export-format').value;
        const blob = await apiCallAuth(`/reportes/${snapshot.id}/exportar?formato=${format}`, { responseType: 'blob' });
        if (!el('export-dialog').open || report?.id !== snapshot.id) return;

        const url = URL.createObjectURL(blob), a = document.createElement('a');
        a.href = url;
        a.download = `reporte-${snapshot.tipo}-${snapshot.id.slice(0, 8)}.${format}`;
        document.body.append(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        el('export-dialog').close();
    } catch (e) { el('export-error').textContent = e.message; }
    finally { button.disabled = false; }
});

try {
    data = await apiCallAuth(audit ? '/auditoria/catalogos' : '/reportes/catalogos');
    if (audit) {
        el('audit-user').innerHTML += data.usuarios.map(u => `<option value="${Number(u.id)}">${esc(u.nombre)} · ${esc(u.usuario || '')}</option>`).join('');
        el('audit-action').innerHTML += data.acciones.map(a => `<option value="${esc(a)}">${esc(a)}</option>`).join('');
        await load();
    } else {
        for (const [id, rows] of [['report-branch', data.sucursales], ['report-area', data.areas]])
            el(id).innerHTML += rows.map(r => `<option value="${Number(r.id)}">${esc(r.nombre)}${r.activo === false ? ' (inactiva)' : ''}</option>`).join('');
        el('report-run').innerHTML += data.ejecuciones.map(r => `<option value="${esc(r.id)}">${esc(r.conjunto)} · ${esc(r.modelVersion || 'Sin versión')} · ${esc(r.state)}</option>`).join('');
        reportType();
        el('reporting-status').textContent = 'Selecciona el tipo, periodo y criterios del reporte.';
    }
} catch (e) {
    failure(e);
    el('reporting-status').textContent = 'No se pudieron cargar las opciones. Recarga para volver a intentar.';
}
