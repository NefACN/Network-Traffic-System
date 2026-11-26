// CyberScore de colaboradores: lista individual para admin y distribucion para gerente
import { apiCallAuth } from '../services/api.js';
import { el, escapeHtml as esc, toast, openDialog } from '../utils/ui.js';
import { TokenStore } from '../core/session.js';
import { cyberScoreCard } from '../features/learning/journey.js';
import { LEVELS, cls, color, areasPanel } from '../features/cyberscore/areas.js';
import { renderPaginador, enlazarPaginador, conPagina } from '../components/paginacion.js';

const root = el('cyberscore-app'), admin = TokenStore.esAdmin();
const filters = { idSucursal: '', idArea: '', nivel: '', busqueda: '' };
let catalogs = [[], []], data = null, pagina = 1;

function distribution(d) {
    const total = d.total || 1;
    return `<div class="cs-kpis">${d.niveles.map(n => `<article class="cs-kpi ${cls(n.nivel)}"><small>${esc(n.nivel === 'Sin evaluación suficiente' ? 'Sin evaluación' : `Riesgo ${n.nivel}`)}</small><strong>${n.cantidad}</strong><small>${Math.round(100 * n.cantidad / total)}% · ${esc(LEVELS.find(l => l[0] === n.nivel)?.[2] ?? '')}</small></article>`).join('')}</div>
        <div class="cs-distribution" role="img" aria-label="Distribución de colaboradores por nivel de riesgo">${d.niveles.filter(n => n.cantidad).map(n => `<span style="width:${100 * n.cantidad / total}%;background:${color(n.nivel)}" title="${esc(n.nivel)}: ${n.cantidad}"></span>`).join('')}</div>
        <p class="panel-caption">${d.total} colaboradores${d.promedio == null ? '' : ` · CyberScore promedio de los evaluados: <strong>${Number(d.promedio).toFixed(0)}</strong> / 100`}.</p>`;
}

const options = (rows, id, name, selected, empty) => `<option value="">${empty}</option>`
    + rows.map(r => `<option value="${Number(r[id])}" ${String(r[id]) === String(selected) ? 'selected' : ''}>${esc(r[name])}</option>`).join('');

function adminView() {
    const [sucursales, areas] = catalogs;
    const rows = data.items.map(c => `<tr><td><strong>${esc(c.nombre)}</strong><small>${esc(c.correo ?? '')}</small></td><td>${esc(c.sucursal ?? '—')}<small>${esc(c.area ?? 'Sin área')}</small></td>
        <td>${c.evaluable ? `<span class="cs-score">${Number(c.cyberScore).toFixed(0)}</span><span class="cs-bar"><span style="width:${Number(c.cyberScore)}%;background:${color(c.nivel)}"></span></span>` : '—'}</td>
        <td><span class="cs-level ${cls(c.nivel)}">${esc(c.evaluable ? c.nivel : 'Sin evaluación')}</span>${c.planPendiente ? `<small>${c.planPendiente} refuerzos pendientes</small>` : ''}</td>
        <td>${c.simulaciones}<small>${c.clics} clics · ${c.credenciales} credenciales · ${c.reportes} reportes</small></td>
        <td><button class="text-action" type="button" data-detail="${c.id}">Ver detalle</button></td></tr>`).join('');
    return `<section class="panel"><div class="panel-heading"><h2>Distribución por nivel de riesgo</h2></div>${distribution(data.distribucion)}
        <p class="panel-caption">Escala: Bajo ${data.umbrales.bajo}–100 · Medio ${data.umbrales.medio}–${data.umbrales.bajo - 1} · Alto ${data.umbrales.alto}–${data.umbrales.medio - 1} · Crítico 0–${data.umbrales.alto - 1}. Los umbrales se configuran en <a class="text-action" href="concientizacion-admin.html">Concientización y gamificación</a>.</p></section>
        ${areasPanel(data.areas, true)}
        <section class="panel" id="cs-personas"><div class="panel-heading"><h2>Colaboradores</h2><span class="subtle-tag">${data.total} resultados</span></div>
        <form id="cs-filters" class="cs-filters"><label>Sucursal<select name="idSucursal">${options(sucursales, 'idSucursal', 'nombre', filters.idSucursal, 'Todas')}</select></label>
            <label>Área<select name="idArea">${options(areas, 'idArea', 'nombre', filters.idArea, 'Todas')}</select></label>
            <label>Nivel<select name="nivel"><option value="">Todos</option>${LEVELS.map(([n]) => `<option ${n === filters.nivel ? 'selected' : ''} value="${esc(n)}">${esc(n)}</option>`).join('')}</select></label>
            <label>Buscar<input name="busqueda" value="${esc(filters.busqueda)}" placeholder="Nombre o correo"></label><button class="btn btn-primary">Filtrar</button><button class="btn" type="button" data-clear>Limpiar</button></form>
        <div class="table-scroll"><table class="event-table"><thead><tr><th>Colaborador</th><th>Sucursal / área</th><th>CyberScore</th><th>Nivel de riesgo</th><th>Simulaciones</th><th></th></tr></thead>
        <tbody>${rows || '<tr><td colspan="6" class="table-state">No hay colaboradores para estos filtros.</td></tr>'}</tbody></table></div>${renderPaginador(data, 'colaboradores')}
        <p class="panel-caption">Ordenados de menor a mayor CyberScore. Consultar el detalle de una persona queda registrado en la auditoría.</p></section>`;
}

function managerView() {
    return `<section class="panel"><div class="panel-heading"><h2>Distribución de tu sucursal</h2></div>${data.oculto ? `<p class="empty-state">${esc(data.nota)}</p>` : distribution(data.distribucion)}
        <p class="panel-caption">${esc(data.oculto ? '' : data.nota)}</p></section>${areasPanel(data.areas, false)}<section class="panel"><p class="panel-caption">Para reforzar a los niveles Alto y Crítico, coordina actividades en <a class="text-action" href="seguimiento-preventivo.html">Seguimiento preventivo</a>.</p></section>`;
}

async function load() {
    try {
        const params = new URLSearchParams(Object.entries(filters).filter(([, v]) => v));
        // el gerente solo recibe agregados, la paginacion aplica a la lista del administrador
        data = await apiCallAuth(admin ? conPagina(`/cyberscore/colaboradores?${params}`, pagina) : `/cyberscore/colaboradores?${params}`);
        root.innerHTML = `<section class="page-heading"><div><p class="eyebrow">CONCIENTIZACIÓN · NIST SP 800-50 · ISO/IEC 27002 6.3</p><h1>${admin ? 'CyberScore de colaboradores' : 'CyberScore de la sucursal'}</h1>
            <p>El CyberScore (0–100) mide qué tan preparado está cada colaborador frente al phishing: baja al caer en simulaciones y se recupera al reportar y completar refuerzos.</p></div></section>` + (admin ? adminView() : managerView());
    } catch (error) { root.innerHTML = `<p class="page-notice" role="alert">${esc(error.message)}</p>`; }
}

async function detail(id) {
    el('cs-dialog-body').innerHTML = '<p class="empty-state">Cargando…</p>';
    openDialog('cs-dialog');
    try {
        const d = await apiCallAuth(`/cyberscore/colaboradores/${id}`), p = d.persona;
        el('cs-dialog-title').textContent = p.nombre;
        el('cs-dialog-body').innerHTML = `<p class="panel-caption">${esc(p.email ?? '')} · ${esc(p.sucursal ?? 'Sin sucursal')} / ${esc(p.area ?? 'Sin área')}</p>${cyberScoreCard(d.cyberScore)}
            <h3>Plan de refuerzo</h3>${d.planRefuerzo.length ? `<ul>${d.planRefuerzo.map(a => `<li>${esc(a.titulo)} · asignada ${esc(new Date(a.fechaAsignacion).toLocaleDateString('es-BO'))} · ${a.fechaCompletado ? 'completada' : '<strong>pendiente</strong>'}</li>`).join('')}</ul>` : '<p class="panel-caption">Sin plan de refuerzo asignado.</p>'}
            ${d.cyberScore.nivel === 'Crítico' ? `<button class="btn btn-primary" type="button" data-plan="${Number(id)}">Asignar plan de refuerzo</button><p class="panel-caption">Asigna microlecciones de los temas en los que cayó (si aún no las tiene pendientes).</p>` : ''}`;
    } catch (error) { el('cs-dialog-body').innerHTML = `<p class="page-notice">${esc(error.message)}</p>`; }
}

root.addEventListener('submit', async event => {
    if (event.target.id !== 'cs-filters') return;

    event.preventDefault();
    Object.assign(filters, Object.fromEntries(new FormData(event.target)));
    pagina = 1;
    await load();
});

root.addEventListener('click', async event => {
    if (event.target.closest('[data-clear]')) {
        Object.keys(filters).forEach(k => filters[k] = '');
        pagina = 1;
        await load();
    }

    const button = event.target.closest('[data-detail]');
    if (button) await detail(button.dataset.detail);
    const area = event.target.closest('[data-area]');
    if (area) {
        filters.idArea = area.dataset.area;
        pagina = 1;
        await load();
        el('cs-personas')?.scrollIntoView({ behavior: 'smooth' });
    }
});

el('cs-dialog').addEventListener('click', async event => {
    const button = event.target.closest('[data-plan]');
    if (!button) return;

    button.disabled = true;
    try {
        const r = await apiCallAuth(`/cyberscore/colaboradores/${button.dataset.plan}/plan-refuerzo`, { method: 'POST' });
        toast(r.asignadas ? `Se asignaron ${r.asignadas} microlecciones.` : 'No hay microlecciones nuevas para asignar (ya tiene pendientes o no hay disponibles).');
        await detail(button.dataset.plan);
        await load();
    } catch (error) {
        toast(error.message, 'error');
        button.disabled = false;
    }
});

enlazarPaginador(root, 'colaboradores', p => {
    pagina = p;
    load().then(() => el('cs-personas')?.scrollIntoView({ block: 'start' }));
});

if (admin) {
    try {
        catalogs = await Promise.all([apiCallAuth('/sucursales?activo=true'), apiCallAuth('/areas-operativas?activo=true')]);
    } catch {
        catalogs = [[], []];
    }
}

await load();
