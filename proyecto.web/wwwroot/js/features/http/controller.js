// modulo Analisis HTTP (HU-19 a HU-26 y HU-28), una vista por pagina segun data-page
import { MlApi } from '../../services/ml-api.js?v=20261118';
import {
    groupBars,
    timelineChart,
    alertPanel,
    densityMap,
    pairedBars,
    downloadJson,
    downloadCsv,
    n,
    pct,
    groupName
} from './report.js?v=20261118';
import { escapeHtml as esc, toast } from '../../utils/ui.js';
import { icon } from '../../components/icons.js';
import { apiCallAuth } from '../../services/api.js';
import { subscribeLive } from '../../services/live.js?v=20261120';
import { renderPaginador, enlazarPaginador, paginarEnNavegador } from '../../components/paginacion.js';

const root = document.getElementById('http-content'), page = document.body.dataset.page;
// paginas actualizadas por el canal en vivo (SSE) en vez de consultar periodicamente
const LIVE_PAGES = ['http-monitoreo', 'http-eventos'];
const liveLabels = {
    connecting: 'Conectando…',
    live: 'En vivo',
    reconnecting: 'Reconectando…',
    offline: 'Sin conexión en vivo'
};
let liveStatusValue = 'connecting';
const headingAction = () => LIVE_PAGES.includes(page)
    ? `<button type="button" class="live-pill" id="live-pill" data-action="refresh" data-state="${liveStatusValue}" title="La página se actualiza sola cuando hay cambios"><i></i><span>${liveLabels[liveStatusValue]}</span></button>`
    : button('refresh', 'Actualizar');
const params = new URLSearchParams(location.search);

// textos
const PAGES = {
    'http-analisis': ['Panel de detección', 'Indicadores, grupos de comportamiento y evolución del análisis seleccionado.'],
    'http-fuentes': ['Registros HTTP', 'Incorpora archivos de acceso de NGINX. Primero se validan y después confirmas su incorporación.'],
    'http-ejecuciones': ['Análisis históricos', 'Analiza un conjunto de registros incorporado y consulta el historial de análisis realizados.'],
    'http-monitoreo': ['Monitoreo en tiempo real', 'Analiza los registros nuevos de un servidor a medida que se generan.'],
    'http-eventos': ['Comportamientos atípicos', 'Busca, filtra y revisa las peticiones que el modelo identificó como atípicas.'],
    'http-comparacion': ['Comparación de periodos', 'Compara la cantidad y proporción de comportamientos atípicos entre dos periodos.'],
    'http-modelo': ['Modelo de análisis', 'Modelo que define los patrones habituales. Ajusta, valida y activa nuevas versiones.'],
    'http-reportes': ['Reportes', 'Genera, previsualiza y exporta reportes de un análisis.'],
};
const STATE = {
    queued: 'En cola',
    running: 'En proceso',
    completed: 'Finalizado',
    failed: 'Con error',
    stopped: 'Detenido',
    validating: 'Validando',
    validated: 'Listo para confirmar',
    ready: 'Disponible',
    rejected: 'Sin registros válidos',
    canceled: 'Descartado',
};
const KIND = { historical: 'Histórico', replay: 'Histórico (progresivo)', live: 'Tiempo real' };
const REVIEW = { pending: 'Pendiente', in_review: 'En revisión', benign: 'Descartada', incident: 'Incidente confirmado' };
const REASONS = {
    static_resource: 'Recursos estáticos (imágenes, CSS, JS)',
    invalid_format: 'Formato no reconocido',
    out_of_order: 'Fecha fuera de orden',
    invalid_timestamp: 'Fecha inválida',
    line_too_long: 'Línea demasiado extensa',
    invalid_http_attributes: 'Datos HTTP inválidos',
    non_finite_feature: 'Valor numérico inválido'
};
const FEATURES = [
    ['codigo_status', 'Código de respuesta'],
    ['metodo', 'Método HTTP'],
    ['bytes_enviados', 'Bytes enviados', v => n(v)],
    ['tiempo_respuesta_ms', 'Tiempo de respuesta', v => v == null ? 'Sin dato' : `${n(v)} ms`],
    ['upstream_response_ms', 'Tiempo de la aplicación', v => v == null ? 'Sin dato' : `${n(v)} ms`],
    ['interval_request_ms', 'Tiempo desde la petición anterior del mismo origen', v => v == null ? 'Primera petición del origen' : `${n(v)} ms`],
    ['request_sessions', 'Peticiones en la sesión hasta este momento', v => n(v)],
    ['errors_ip', 'Proporción de errores del origen', v => pct(v, 1)],
    ['entropy_ruta', 'Variedad de caracteres de la ruta (entropía)', v => Number(v).toFixed(2)],
    ['hora', 'Hora local', v => `${String(v).padStart(2, '0')}:00`],
    ['dia_semana', 'Día', v => ['Lunes', 'Martes', 'Miércoles', 'Jueves', 'Viernes', 'Sábado', 'Domingo'][v] ?? v],
];

// utilidades de presentacion
const date = value => value
    ? new Date(value).toLocaleString('es-BO', { timeZone: 'America/La_Paz', dateStyle: 'short', timeStyle: 'short' })
    : '—';
const dateInput = value => {
    if (!value) return '';
    const d = new Date(value);
    return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
};
const toIso = value => value ? new Date(value).toISOString() : '';
const bytes = value => {
    let v = Number(value ?? 0), u = 0;
    while (v >= 1024 && u < 3) {
        v /= 1024;
        u++;
    }
    return `${v.toLocaleString('es-BO', { maximumFractionDigits: 1 })} ${['B', 'KB', 'MB', 'GB'][u]}`;
};
const ago = value => {
    if (!value) return 'sin datos';
    const s = Math.max(0, (Date.now() - new Date(value)) / 1000);
    return s < 60 ? 'hace menos de 1 min'
        : s < 3600 ? `hace ${Math.floor(s / 60)} min`
        : s < 86400 ? `hace ${Math.floor(s / 3600)} h`
        : `hace ${Math.floor(s / 86400)} d`;
};
const tone = value => ['failed', 'rejected', 'incident'].includes(value) ? 'danger'
    : ['queued', 'running', 'validating', 'validated', 'in_review'].includes(value) ? 'info'
    : ['completed', 'ready', 'benign'].includes(value) ? 'ok'
    : '';
const badge = (value, text = STATE[value] ?? REVIEW[value] ?? value) => `<span class="ml-badge ${tone(value)}">${esc(text)}</span>`;
const panel = (title, body, caption = '', extra = '') =>
    `<section class="panel"${extra}><h2>${title}</h2>${caption ? `<p class="ml-caption">${caption}</p>` : ''}${body}</section>`;
const table = (headers, rows, empty = 'No hay resultados para esta selección.') =>
    `<div class="table-scroll"><table class="event-table"><thead><tr>${headers.map(h => `<th>${h}</th>`).join('')}</tr></thead><tbody>${rows || `<tr><td colspan="${headers.length}" class="table-state">${empty}</td></tr>`}</tbody></table></div>`;
const metric = (title, value, caption = '', cls = '') =>
    `<article class="metric ${cls}"><div class="metric-top">${title}</div><strong>${value}</strong><small>${caption}</small></article>`;
const button = (action, text, attrs = '', cls = 'btn') =>
    `<button class="${cls}" type="button" data-action="${action}" ${attrs}>${text}</button>`;
const steps = items =>
    `<ol class="ml-steps">${items.map(([title, text]) => `<li><strong>${title}</strong><span>${text}</span></li>`).join('')}</ol>`;
const notice = text => `<p class="ml-notice">${icon('alert')}<span>${text}</span></p>`;
const runLabel = r => `${r.datasetName} · ${date(r.createdAt)}`;
const progress = run => run.fileSize > 0 && run.kind !== 'live' && ['running', 'queued'].includes(run.state)
    ? Math.min(100, 100 * run.offset / run.fileSize)
    : null;

// estado
let saved = {};
try {
    saved = JSON.parse(sessionStorage.getItem('dismac.ml.selection') ?? '{}');
} catch { /* preferencia opcional */ }
const state = {
    runId: params.get('run') ?? saved.runId ?? '',
    window: [5, 15, 60].includes(saved.window) ? saved.window : 15,
    period: {},
    datasets: [],
    runs: [],
    page: 1,
    rows: [],
    filter: initialFilter(),
    historySearch: '',
    // la API pagina, pero los selectores necesitan todo el catalogo, las tablas se paginan aqui
    pages: { fuentes: 1, historial: 1, monitoreo: 1 },
    compare: null,
    reportType: 'summary',
    reportPeriod: {},
    report: null,
};

function initialFilter() {
    if (params.get('candidate') === 'true')
        return { candidate: 'true', ...(params.get('review') === 'pending' ? { review: 'pending' } : {}) };
    if (/^-?\d+$/.test(params.get('cluster') ?? '')) return { cluster: params.get('cluster') };
    return { noise: 'true' };
}

function persist() {
    try {
        sessionStorage.setItem('dismac.ml.selection', JSON.stringify({ runId: state.runId, window: state.window }));
    } catch { }
}

let loading = false, busy = false, trainingBusy = false, trainingSources = [], trainingConfig = {};
const analyses = () => state.runs.filter(r => r.kind !== 'validation');

// pagina actual acotada de una tabla paginada en el navegador
function paginada(clave, lista) {
    const vista = paginarEnNavegador(lista, state.pages[clave]);
    state.pages[clave] = vista.pagina;
    return vista;
}

function runSelect(id, selected, filter = () => true, empty = 'Selecciona un análisis') {
    const runs = analyses().filter(filter), group = (label, items) => items.length
        ? `<optgroup label="${label}">${items.map(r => `<option value="${r.id}" ${r.id === selected ? 'selected' : ''}>${esc(runLabel(r))}${r.state !== 'completed' && r.kind !== 'live' ? ` (${esc(STATE[r.state])})` : ''}</option>`).join('')}</optgroup>`
        : '';
    return `<select id="${id}" name="${id}"><option value="">${empty}</option>${group('Tiempo real', runs.filter(r => r.kind === 'live'))}${group('Históricos', runs.filter(r => r.kind !== 'live'))}</select>`;
}

const periodFields = (prefix, period) =>
    `<label>Desde<input type="datetime-local" name="${prefix}from" value="${dateInput(period.from)}"></label><label>Hasta<input type="datetime-local" name="${prefix}to" value="${dateInput(period.to)}"></label>`;

function readPeriod(data, prefix = '') {
    const period = { from: toIso(data.get(`${prefix}from`)), to: toIso(data.get(`${prefix}to`)) };
    if (period.from && period.to && period.from > period.to)
        throw new Error('El inicio del periodo debe ser anterior al final.');

    return period;
}

// avisa cuando llegan nuevas alertas con la pagina abierta (HU-22, HU-24)
const observed = new Map();

function observeAlerts(id, total) {
    const before = observed.get(id);
    if (before !== undefined && total > before) toast(`${n(total - before)} nuevas alertas para revisar.`, 'info');
    observed.set(id, total);
}

let catalogs = null;

async function catalogos() {
    if (!catalogs) {
        try {
            catalogs = await Promise.all([
                apiCallAuth('/sucursales?activo=true'),
                apiCallAuth('/areas-operativas?activo=true'),
                apiCallAuth('/campanias-phishing/categorias-educativas')]);
        } catch {
            catalogs = [[], [], []];
        }
    }

    return catalogs;
}

const options = (rows, id, name, empty) => `<option value="">${esc(empty)}</option>`
    + rows.map(r => `<option value="${Number(r[id])}">${esc(r[name])}</option>`).join('');
const optionalNumber = value => value ? Number(value) : null;

const modal = document.createElement('dialog');
modal.className = 'modal ml-dialog';
modal.innerHTML = '<div class="modal-head"><h2></h2><button class="modal-close" aria-label="Cerrar" type="button">×</button></div><div class="modal-body"></div>';
document.body.append(modal);
modal.querySelector('.modal-close').addEventListener('click', () => modal.close());

function showDialog(title, html) {
    modal.querySelector('h2').textContent = title;
    modal.querySelector('.modal-body').innerHTML = html;
    modal.showModal();
}

// HU-19 registros HTTP
function datasetStatus(d) {
    const v = d.validation, job = state.runs.find(r => r.id === v?.id);
    if (d.state === 'validating')
        return `${badge('validating')}<small>${job?.state === 'failed' ? `<span class="ml-error-text">${esc(job.error ?? 'La validación no terminó.')}</span> Descarta el archivo y vuelve a subirlo.` : job?.state === 'queued' ? 'Esperando al agente de procesamiento.' : 'Leyendo el archivo…'}</small>`;
    if (d.state === 'validated')
        return `${badge('validated')}<small>Revisa el resumen y confirma para poder analizarlo.</small>`;
    if (d.state === 'rejected')
        return `${badge('rejected')}<small>El archivo no contiene peticiones con el formato de registro de NGINX esperado.</small>`;

    return badge(d.state);
}

function datasetCounts(v) {
    if (!v) return '—';

    const reasons = (() => {
        try {
            return Object.entries(JSON.parse(v.reasons ?? '{}'));
        } catch {
            return [];
        }
    })();
    return `<strong>${n(v.valid)}</strong> válidas<small>de ${n(v.read)} líneas leídas</small>${v.read > v.valid ? `<details><summary>Ver no incluidas (${n(v.read - v.valid)})</summary><ul class="ml-list">
        <li>Excluidas: ${n(v.excluded)}</li><li>Rechazadas: ${n(v.rejected)}</li><li>Vacías: ${n(v.empty)}</li>${reasons.map(([k, c]) => `<li>${esc(REASONS[k] ?? k)}: ${n(c)}</li>`).join('')}</ul></details>` : ''}`;
}

function sourcesView() {
    const files = state.datasets.filter(d => d.source !== 'nginx' && d.state !== 'canceled');
    return panel('Incorporar registros', steps([
        ['1. Validar', 'Selecciona el archivo; el sistema comprueba su formato.'],
        ['2. Confirmar', 'Revisa el resumen, asigna un nombre y confirma.'],
        ['3. Analizar', 'Inícialo desde «Análisis históricos».']]) +
        `<form id="ml-upload" class="http-context"><label>Archivo de registro NGINX (.log o .txt, máximo 2 GB)<input type="file" name="file" accept=".log,.txt" required></label><button class="btn btn-primary">Validar archivo</button></form>`) +
        panel('Conjuntos de registros', table(['Conjunto', 'Estado', 'Peticiones', 'Periodo', ''], paginada('fuentes', files).items.map(d => {
            const v = d.validation;
            return `<tr><td><strong>${esc(d.name)}</strong><small>${esc(d.origin || d.fileName)} · ${bytes(d.size)}${d.sucursal ? ` · ${esc(d.sucursal)}` : ''}${d.idCampaniaSimulacion ? ' · Tráfico de simulación' : ''}</small></td>
                <td>${datasetStatus(d)}</td><td>${datasetCounts(v)}</td><td>${date(v?.from)}<small>a ${date(v?.to)}</small></td>
                <td class="ml-row-actions">${d.state === 'validated' ? button('confirm', 'Confirmar', `data-id="${v.id}" data-name="${esc(d.name)}"`, 'btn btn-primary') : ''}
                ${d.state === 'ready' ? `<a class="btn btn-primary" href="http-ejecuciones.html?dataset=${encodeURIComponent(d.id)}">Analizar</a>` : ''}
                ${['validating', 'validated', 'rejected'].includes(d.state) ? button('cancel-validation', 'Descartar', `data-id="${v?.id}"`) : ''}</td></tr>`;
        }).join(''), 'Todavía no has incorporado registros.') + renderPaginador(paginada('fuentes', files), 'fuentes'),
        'Los recursos estáticos se excluyen; bots y peticiones repetidas se conservan, igual que en el entrenamiento del modelo.');
}

// HU-21 / HU-25 analisis historicos
function runState(r) {
    const p = progress(r);
    return `${badge(r.state)}${p !== null ? `<progress max="100" value="${p.toFixed(1)}" aria-label="Avance"></progress><small>${p.toFixed(1)}% leído</small>` : ''}
        ${r.state === 'queued' ? '<small>Esperando al agente de procesamiento.</small>' : ''}${r.error ? `<small class="ml-error-text">${esc(r.error)}</small>` : ''}`;
}

const runResults = r =>
    `<strong>${n(r.valid)}</strong> analizadas<small>${n(r.noise)} atípicas (${pct(r.noise, r.valid)}) · ${n(r.candidates)} alertas</small>`;

function runActions(r) {
    return `<a class="btn" href="http-analisis.html?run=${encodeURIComponent(r.id)}">Ver panel</a>
        ${r.candidates ? `<a class="text-action" href="http-eventos.html?run=${encodeURIComponent(r.id)}&candidate=true">Alertas</a>` : ''}
        ${['queued', 'running'].includes(r.state) ? button('stop', 'Detener', `data-id="${r.id}"`) : ['failed', 'stopped'].includes(r.state) ? button('resume', 'Reanudar', `data-id="${r.id}"`) : ''}`;
}

function executionsView() {
    const ready = state.datasets.filter(d => d.state === 'ready' && d.source === 'file'), preset = params.get('dataset');
    const search = state.historySearch.toLowerCase();
    const history = analyses().filter(r => r.kind !== 'live'
        && (!search || r.datasetName.toLowerCase().includes(search) || (STATE[r.state] ?? '').toLowerCase().includes(search)));
    const start = ready.length
        ? `<form id="ml-start"><div class="http-context"><label>Conjunto de registros<select name="datasetId" required><option value="">Selecciona un conjunto</option>${ready.map(d => `<option value="${d.id}" ${d.id === preset ? 'selected' : ''}>${esc(d.name)}</option>`).join('')}</select></label><button class="btn btn-primary">Iniciar análisis</button></div>
        <details class="ml-advanced"><summary>Opción de demostración</summary><label class="ml-check"><input type="checkbox" name="replay"> Simular la llegada progresiva de los registros</label><label>Velocidad de simulación (× tiempo real)<input name="speed" type="number" value="60" min="0.1" max="10000" step="0.1"></label><p class="ml-caption">Solo cambia la espera entre peticiones; los resultados son los mismos.</p></details></form>`
        : `<p class="ml-empty">No hay conjuntos disponibles. <a class="text-action" href="http-fuentes.html">Incorpora registros</a> primero.</p>`;
    return panel('Nuevo análisis', start, 'Cada análisis clasifica todas las peticiones del conjunto con el modelo activo y guarda los resultados para consultarlos después.') +
        panel('Historial de análisis', `<form id="ml-history" class="http-context ml-inline"><label>Buscar por conjunto o estado<input name="search" value="${esc(state.historySearch)}" placeholder="Nombre del conjunto"></label><button class="btn">Buscar</button>${state.historySearch ? button('clear-history', 'Limpiar') : ''}</form>` +
            table(['Análisis', 'Periodo de los registros', 'Estado', 'Resultados', ''], paginada('historial', history).items.map(r => `<tr><td><strong>${esc(r.datasetName)}</strong><small>${esc(KIND[r.kind])} · iniciado ${date(r.createdAt)}<br>Modelo ${esc(r.modelVersion)}</small></td>
                <td>${date(r.from)}<small>a ${date(r.to)}</small></td><td>${runState(r)}</td><td>${runResults(r)}</td><td class="ml-row-actions">${runActions(r)}</td></tr>`).join(''), 'Todavía no hay análisis.') + renderPaginador(paginada('historial', history), 'historial'));
}

// HU-22 monitoreo en tiempo real
function liveStatus(r) {
    if (r.state === 'queued') return badge('queued', 'Esperando al agente');
    if (r.state === 'running')
        return r.agentConnected
            ? (Date.now() - new Date(r.updatedAt) < 120000
                ? badge('running', 'Recibiendo registros')
                : badge('running', 'Conectado · sin registros nuevos'))
            : badge('queued', 'Agente desconectado');

    return badge(r.state, r.state === 'stopped' ? 'Detenido' : STATE[r.state]);
}

const agentCommand = id => `python3 worker.py --env /etc/dismac/agente.env --follow /var/log/nginx/sitio.access.log --execution ${id}`;
const agentHelp = id => `<p>Identificador de la fuente:</p><code class="ml-code">${esc(id)}</code>
    <p>En el servidor donde está el archivo de registro, ejecuta el agente (o configúralo como servicio <code>dismac-agente</code>):</p><code class="ml-code">${esc(agentCommand(id))}</code>
    <p class="ml-caption">La clave del agente se configura en el servidor y nunca se muestra en el navegador. Consulta <code>deploy/DESPLIEGUE.md</code>.</p>`;

async function monitoringView() {
    const [sucursales] = await catalogos();
    const live = analyses().filter(r => r.kind === 'live'), active = live.filter(r => ['queued', 'running'].includes(r.state));
    const total = live.reduce((s, r) => s + r.valid, 0),
        noise = live.reduce((s, r) => s + r.noise, 0),
        alerts = live.reduce((s, r) => s + r.candidates, 0);
    live.forEach(r => observeAlerts(r.id, r.candidates));

    return `<div class="metric-grid ml-kpis">${metric('Fuentes activas', n(active.length), `${n(live.length)} registradas`)}${metric('Peticiones analizadas', n(total))}${metric('Comportamientos atípicos', n(noise), pct(noise, total) + ' del total', 'is-atypical')}${metric('Alertas', n(alerts), 'Requieren revisión')}</div>` +
        panel('Fuentes monitoreadas', table(['Fuente', 'Estado', 'Último registro', 'Resultados', ''], paginada('monitoreo', live).items.map(r => `<tr><td><strong>${esc(r.datasetName)}</strong><small>${r.sucursal ? esc(r.sucursal) + ' · ' : ''}registrada ${date(r.createdAt)}</small></td>
            <td>${liveStatus(r)}${r.error ? `<small class="ml-error-text">${esc(r.error)}</small>` : ''}</td><td>${date(r.to)}<small>${r.to ? ago(r.updatedAt) : 'Aún sin registros'}</small></td><td>${runResults(r)}</td>
            <td class="ml-row-actions">${runActions(r)}${button('agent-help', 'Instrucciones', `data-id="${r.id}"`, 'text-action')}</td></tr>`).join(''), 'Todavía no hay fuentes. Registra una para empezar.') + renderPaginador(paginada('monitoreo', live), 'monitoreo'),
            'Se actualiza en vivo cuando llegan registros nuevos o cambia la conexión del agente. Detener conserva los resultados; puedes reanudar después.') +
        panel('Registrar una fuente', steps([
            ['1. Registrar', 'Indica el nombre del sitio que quieres monitorear.'],
            ['2. Iniciar el agente', 'En el servidor, el agente lee el archivo de registro y envía las peticiones nuevas.'],
            ['3. Revisar', 'Los resultados y alertas aparecen aquí y en el panel general.']]) +
            `<form id="ml-source"><div class="http-context"><label>Nombre de la fuente<input name="name" maxlength="120" required placeholder="Portal comercial"></label><label>Archivo de registro<input name="fileName" value="access.log" maxlength="200" required></label><label>Sucursal (opcional)<select name="idSucursal">${options(sucursales, 'idSucursal', 'nombre', 'Toda la organización')}</select></label><button class="btn btn-primary">Registrar fuente</button></div></form>`,
            'Cada fuente empieza con historial vacío y se analiza con el modelo activo en el momento de registrarla.');
}

// HU-24 panel general
const periodText = s => s.periodFrom ? `${date(s.periodFrom)} → ${date(s.periodTo)}` : 'sin resultados';

function selectionBar(withPeriod = true) {
    return `<form id="ml-selection" class="http-context ml-selection"><label>Análisis${runSelect('ml-run', state.runId)}</label>${withPeriod ? `${periodFields('', state.period)}<button class="btn">Aplicar periodo</button>${state.period.from || state.period.to ? button('clear-period', 'Todo el análisis') : ''}` : ''}</form>`;
}

function runHeader(run, s) {
    return `<div class="ml-run-header"><div><strong>${esc(run.datasetName)}</strong> ${badge('', KIND[run.kind])} ${run.kind === 'live' ? liveStatus(run) : badge(run.state)}</div>
        <small>Periodo: ${periodText(s)} · Modelo ${esc(run.modelVersion)}${state.period.from || state.period.to ? ' · <b>periodo filtrado</b>' : ''}</small></div>`;
}

function indicators(s, alertSummary) {
    const groups = s.groups.filter(g => g.cluster !== -1).length;
    return `<div class="metric-grid ml-kpis">${metric('Peticiones analizadas', n(s.total))}${metric('Comportamientos atípicos', n(s.noise), `${pct(s.noise, s.total)} del total`, 'is-atypical')}
        ${metric('Alertas pendientes', n(alertSummary.pending), `de ${n(alertSummary.total)} alertas`)}${metric('Grupos de comportamiento', n(groups), 'Patrones habituales')}</div>`;
}

const explainer = () => `<details class="ml-explainer"><summary>${icon('alert')} ¿Cómo interpretar estos resultados?</summary><ul>
    <li><b>Grupo:</b> conjunto de peticiones con un comportamiento parecido (patrón habitual).</li>
    <li><b>Atípico:</b> petición que no se parece a ningún grupo conocido.</li>
    <li><b>Alerta:</b> atípico con una puntuación de rareza superior al umbral; conviene revisarlo.</li>
    <li>Ninguna de estas categorías confirma un ataque: la confirmación es una decisión del analista.</li></ul></details>`;

function groupTable(s) {
    const groups = [...s.groups]
        .sort((a, b) => (a.cluster === -1 ? -1 : b.cluster === -1 ? 1 : b.count - a.count));
    return table(['Grupo', 'Peticiones', 'Proporción', 'Respuestas con error', 'Rutas distintas', 'Alertas', ''], groups.map(g => `<tr class="${g.cluster === -1 ? 'is-atypical-row' : ''}"><td>${groupName(g.cluster)}</td><td>${n(g.count)}</td><td>${pct(g.count, s.total)}</td>
        <td>${n(g.errorResponses)} <small>${pct(g.errorResponses, g.count)}</small></td><td>${n(g.uniqueRoutes)}</td><td>${n(g.candidates)}</td>
        <td><a class="text-action" href="http-eventos.html?run=${encodeURIComponent(state.runId)}&cluster=${Number(g.cluster)}">Ver peticiones</a></td></tr>`).join(''));
}

async function dashboardView() {
    const run = analyses().find(r => r.id === state.runId);
    const [report, recurring] = await Promise.all([
        MlApi.report(state.runId, state.window, state.period),
        MlApi.correlations(state.runId, state.period)]);
    const s = report.summary;
    observeAlerts(state.runId, report.alertSummary.total);

    return selectionBar() + runHeader(run, s) + indicators(s, report.alertSummary) + explainer() +
        `<div class="http-grid">${panel('Distribución por grupo', groupBars(s, state.runId), 'Selecciona un grupo para ver sus peticiones.')}${alertPanel(report, state.runId)}</div>` +
        panel('Evolución en el tiempo', `<label class="ml-inline-label">Agrupar cada <select id="http-window">${[5, 15, 60].map(w => `<option value="${w}" ${state.window === w ? 'selected' : ''}>${w === 60 ? '1 hora' : `${w} minutos`}</option>`).join('')}</select></label>${timelineChart(s)}`) +
        panel('Detalle por grupo', groupTable(s)) +
        panel('Orígenes con más comportamientos atípicos', table(['Origen anónimo', 'Peticiones atípicas', 'Alertas', 'Primera', 'Última'], recurring.slice(0, 15).map(r => `<tr><td><code>${esc(r.subject.slice(0, 12))}</code></td><td>${n(r.events)}</td><td>${n(r.candidates)}</td><td>${date(r.from)}</td><td>${date(r.to)}</td></tr>`).join(''), 'Ningún origen repite comportamientos atípicos.'),
            'Las direcciones IP se sustituyen por un código anónimo. Que un origen repita atípicos no demuestra un ataque.');
}

// HU-23 / HU-25 comportamientos atipicos
const VIEWS = {
    alerts: ['Alertas pendientes de revisión', { candidate: 'true', review: 'pending' }],
    atypical: ['Todos los atípicos', { noise: 'true' }],
    candidates: ['Todas las alertas', { candidate: 'true' }],
    grouped: ['Asociadas a un grupo', { noise: 'false' }],
    all: ['Todas las peticiones', {}]
};
const currentView = f => f.candidate === 'true' ? (f.review === 'pending' ? 'alerts' : 'candidates')
    : f.noise === 'true' ? 'atypical'
    : f.noise === 'false' ? 'grouped'
    : 'all';
const classification = r => r.noise
    ? `<span class="ml-badge atypical">Atípico</span>`
    : `<span class="ml-badge">${groupName(r.cluster)}</span>`;

function eventTable(items) {
    state.rows = items;
    return table(['Fecha', 'Petición', 'Respuesta', 'Clasificación', 'Revisión', ''], items.map(r => `<tr class="${r.candidate ? 'is-alert-row' : ''}"><td>${date(r.occurredAt)}<small>Origen ${esc(r.subject.slice(0, 8))}</small></td>
        <td class="ml-route"><b>${esc(r.method)}</b> ${esc(r.route)}</td><td>${r.status}</td><td>${classification(r)}${r.candidate ? ' <span class="ml-badge danger">Alerta</span>' : ''}</td>
        <td>${badge(r.review?.state ?? 'pending', REVIEW[r.review?.state ?? 'pending'])}</td><td>${button('review', 'Ver detalle', `data-id="${r.id}"`)}</td></tr>`).join(''));
}

async function eventsView() {
    const f = state.filter, view = currentView(f);
    const data = await MlApi.results(state.runId, { page: state.page, size: 25, ...f });
    const advanced = ['cluster', 'route', 'method', 'status', 'from', 'to'].some(k => f[k])
        || (f.review && view !== 'alerts');
    return selectionBar(false) + notice('Estas peticiones son <b>observaciones que requieren revisión</b>, no ataques confirmados. La puntuación de rareza no es una probabilidad de ataque.') +
        panel('Buscar peticiones', `<form id="ml-filter"><div class="http-context"><label>Mostrar<select name="view">${Object.entries(VIEWS).map(([k, [label]]) => `<option value="${k}" ${k === view ? 'selected' : ''}>${label}</option>`).join('')}</select></label>
            ${periodFields('', f)}</div>
            <details class="ml-advanced" ${advanced ? 'open' : ''}><summary>Más filtros</summary><div class="http-context"><label>Ruta contiene<input name="route" value="${esc(f.route ?? '')}" maxlength="2048" placeholder="/login"></label>
            <label>Método<select name="method"><option value="">Todos</option>${['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS'].map(m => `<option ${f.method === m ? 'selected' : ''}>${m}</option>`).join('')}</select></label>
            <label>Código de respuesta<input type="number" name="status" min="100" max="599" value="${esc(f.status ?? '')}" placeholder="404"></label>
            <label>Grupo (−1 = atípicos)<input type="number" name="cluster" min="-1" value="${esc(f.cluster ?? '')}"></label>
            <label>Revisión<select name="review"><option value="">Todas</option>${Object.entries(REVIEW).map(([k, v]) => `<option value="${k}" ${f.review === k ? 'selected' : ''}>${v}</option>`).join('')}</select></label></div></details>
            <div class="ml-actions"><button class="btn btn-primary">Buscar</button>${button('clear', 'Limpiar filtros')}</div></form>`) +
        panel(`Resultados · ${n(data.total)}`, eventTable(data.items) + `<div class="ml-pagination">${button('previous', 'Anterior', state.page === 1 ? 'disabled' : '')}<span>Página ${n(state.page)} de ${n(Math.max(1, Math.ceil(data.total / data.size)))}</span>${button('next', 'Siguiente', state.page * data.size >= data.total ? 'disabled' : '')}</div>`);
}

async function reviewDialog(id) {
    const r = state.rows.find(row => String(row.id) === id);
    if (!r) return;

    const [sucursales, areas, temas] = await catalogos();
    let features = {};
    try {
        features = JSON.parse(r.features);
    } catch { /* sin atributos */ }
    const current = r.review?.state ?? 'pending';
    showDialog('Detalle de la petición', `<div class="ml-obs-head">${classification(r)}${r.candidate ? ' <span class="ml-badge danger">Alerta</span>' : ''} ${badge(current, REVIEW[current])}</div>
        ${notice('Observación que requiere revisión. No es un ataque confirmado.')}
        <dl class="ml-details"><dt>Fecha</dt><dd>${date(r.occurredAt)}</dd><dt>Petición</dt><dd><b>${esc(r.method)}</b> ${esc(r.route)}</dd><dt>Respuesta</dt><dd>${r.status}</dd><dt>Origen anónimo</dt><dd><code>${esc(r.subject.slice(0, 16))}</code></dd></dl>
        <h3>Resultado del modelo</h3><dl class="ml-details"><dt>Clasificación</dt><dd>${r.noise ? 'No se parece a ningún grupo conocido (atípico).' : `Asociada al ${groupName(r.cluster).toLowerCase()} (comportamiento habitual).`}</dd>
        <dt>Puntuación de rareza</dt><dd>${Number(r.anomalyScore).toFixed(3)} <small>(umbral de alerta ${r.threshold})</small></dd><dt>Pertenencia al grupo</dt><dd>${r.noise ? 'No aplica: no pertenece a ningún grupo' : pct(r.strength, 1)}</dd></dl>
        <h3>Atributos de la petición</h3><dl class="ml-details">${FEATURES.filter(([k]) => k in features).map(([k, label, fmt]) => `<dt>${label}</dt><dd>${esc(fmt ? fmt(features[k]) : features[k])}</dd>`).join('')}</dl>
        <form id="ml-review" data-id="${esc(id)}"><h3>Revisión del analista</h3><label>Estado<select name="state">${Object.entries(REVIEW).map(([k, v]) => `<option value="${k}" ${k === current ? 'selected' : ''}>${v}</option>`).join('')}</select></label>
        <label>Observaciones<textarea name="notes" maxlength="2000" placeholder="Qué revisaste y por qué llegaste a esta conclusión">${esc(r.review?.notes ?? '')}</textarea></label>
        <div class="ml-incident" ${current === 'incident' ? '' : 'hidden'}><label class="ml-check"><input type="checkbox" name="confirmed"> Confirmo que revisé manualmente esta petición y es un incidente.</label>
        <fieldset class="ml-suggestion"><legend>Sugerir concientización (opcional)</legend><label>Sucursal<select name="idSucursal">${options(sucursales, 'idSucursal', 'nombre', 'La de la fuente (si tiene)')}</select></label><label>Área<select name="idArea">${options(areas, 'idArea', 'nombre', 'Todas las áreas')}</select></label><label>Tema<select name="idCategoria">${options(temas, 'idCategoria', 'nombre', 'Sin tema')}</select></label><p>Se creará una sugerencia en «Decisiones preventivas». No se envía nada sin tu decisión.</p></fieldset></div>
        <button class="btn btn-primary">Guardar revisión</button></form>`);
}

// HU-26 comparacion de periodos
const usableForCompare = r => r.state === 'completed' || r.kind === 'live';
const percent = digits => v => `${v.toLocaleString('es-BO', { maximumFractionDigits: digits })}%`;

async function comparisonView() {
    const usable = analyses().filter(usableForCompare);
    if (!usable.length)
        return panel('Comparación de periodos', '<p class="ml-empty">Necesitas al menos un análisis finalizado o una fuente de monitoreo con resultados.</p>');

    const c = state.compare ??= {
        left: usable[1]?.id ?? usable[0].id,
        right: usable[0].id,
        leftPeriod: {},
        rightPeriod: {}
    };
    const side = (key, title, cls) =>
        `<fieldset class="ml-period ${cls}"><legend>${title}</legend><label>Análisis${runSelect(key, c[key], usableForCompare)}</label>${periodFields(key, c[`${key}Period`])}</fieldset>`;
    let body = '';
    if (c.left && c.right) {
        const r = await MlApi.compare({
            left: c.left,
            right: c.right,
            leftFrom: c.leftPeriod.from,
            leftTo: c.leftPeriod.to,
            rightFrom: c.rightPeriod.from,
            rightTo: c.rightPeriod.to
        });
        const A = r.left, B = r.right, ratio = (x, s) => s.total ? 100 * x / s.total : 0;
        const delta = (a, b) => {
            const d = b - a;
            return `<span class="ml-delta ${d > 0 ? 'up' : d < 0 ? 'down' : ''}">${d > 0 ? '▲ +' : d < 0 ? '▼ ' : ''}${d.toLocaleString('es-BO', { maximumFractionDigits: 2 })} pp</span>`;
        };
        const groupsOf = s => s.groups.filter(g => g.cluster !== -1).length;
        const clusters = [...new Set([...A.groups, ...B.groups].map(g => g.cluster))].sort((a, b) => a - b);
        const share = (s, cl) => ratio(s.groups.find(g => g.cluster === cl)?.count ?? 0, s);
        body = (r.warnings.length ? `<div class="ml-error">${r.warnings.map(esc).join('<br>')}</div>` : '') +
            `<div class="ml-compare-heads"><div class="ml-period-a"><b>Periodo A</b> ${esc(A.datasetName)}<small>${periodText(A)}</small></div><div class="ml-period-b"><b>Periodo B</b> ${esc(B.datasetName)}<small>${periodText(B)}</small></div></div>` +
            table(['Indicador', 'Periodo A', 'Periodo B', 'Variación'], [
                ['Peticiones analizadas', n(A.total), n(B.total), ''],
                ['Comportamientos atípicos', n(A.noise), n(B.noise), ''],
                ['Proporción de atípicos', pct(A.noise, A.total), pct(B.noise, B.total), delta(ratio(A.noise, A), ratio(B.noise, B))],
                ['Alertas', n(A.candidates), n(B.candidates), ''],
                ['Proporción de alertas', pct(A.candidates, A.total), pct(B.candidates, B.total), delta(ratio(A.candidates, A), ratio(B.candidates, B))],
                ['Grupos de comportamiento', n(groupsOf(A)), n(groupsOf(B)), ''],
            ].map(([k, a, b, d]) => `<tr><td>${k}</td><td>${a}</td><td>${b}</td><td>${d}</td></tr>`).join('')) +
            (r.comparable ? `<h3>Proporciones</h3>${pairedBars([{ label: 'Atípicos', a: ratio(A.noise, A), b: ratio(B.noise, B), format: percent(2) }, { label: 'Alertas', a: ratio(A.candidates, A), b: ratio(B.candidates, B), format: percent(2) }])}
                <h3>Distribución por grupo (% de peticiones)</h3>${pairedBars(clusters.map(cl => ({ label: groupName(cl), a: share(A, cl), b: share(B, cl), format: percent(1) })))}` : '<p class="ml-caption">Se omiten las gráficas de variación porque los periodos no son comparables.</p>') +
            `<ul class="ml-list ml-caption">${r.notes.map(x => `<li>${esc(x)}</li>`).join('')}</ul>`;
    }

    return panel('Periodos a comparar', `<form id="ml-compare"><div class="http-grid">${side('left', 'Periodo A', 'ml-period-a')}${side('right', 'Periodo B', 'ml-period-b')}</div><div class="ml-actions"><button class="btn btn-primary">Comparar</button></div></form>`,
        'Puedes comparar dos análisis distintos, o dos rangos de fechas del mismo análisis (por ejemplo, esta semana frente a la anterior en una fuente de monitoreo).') +
        (body ? panel('Resultado de la comparación', body) : '');
}

// HU-20 modelo de analisis
const quality = v => v == null ? 'No disponible'
    : v >= .5 ? `Buena (${v.toFixed(2)})`
    : v >= .25 ? `Moderada (${v.toFixed(2)})`
    : `Baja (${v.toFixed(2)})`;
const distribution = groups => Object.entries(groups ?? {}).map(([k, v]) => `${groupName(Number(k))}: ${n(v)}`).join(' · ');

function versionState(j, v) {
    if (v?.activa) return badge('completed', 'En uso');

    return {
        queued: badge('queued', 'En cola'),
        running: badge('running', 'Ajustando…'),
        completed: badge('validated', 'Listo para validar'),
        validated: badge('ready', 'Validado'),
        failed: badge('failed', 'Falló')
    }[j?.estado] ?? badge('ready', 'Validado');
}

async function modelView() {
    const data = await MlApi.models(), m = data.active;
    let training;
    try {
        training = await trainingView(m);
    } catch (error) {
        trainingBusy = false;
        training = panel('Ajuste y versiones', `<p role="alert" class="ml-error">${esc(error.message)}</p><p>El modelo en uso sigue disponible para los análisis. Actualiza esta página después de revisar el servicio del modelo.</p>`);
    }

    return panel('Modelo en uso', `<div class="metric-grid ml-kpis">${metric('Versión', esc(m.version), '', 'is-text')}${metric('Peticiones de entrenamiento', n(m.n_train))}${metric('Umbral de alerta', esc(m.umbral_alerta), 'Puntuación mínima para generar alerta')}</div>
        <p>El modelo agrupa las peticiones con comportamiento parecido (HDBSCAN). Las que no encajan en ningún grupo se marcan como atípicas.</p>
        <details class="ml-advanced"><summary>Detalles técnicos</summary><dl class="ml-details"><dt>Tamaño mínimo de grupo</dt><dd>${m.min_cluster_size}</dd><dt>Mínimo de vecinos</dt><dd>${m.min_samples}</dd><dt>Preparación de datos</dt><dd>${esc(m.escalado)}</dd><dt>Atributos utilizados</dt><dd>${(m.columnas_entrada ?? []).map(esc).join(', ')}</dd></dl></details>`) + training;
}

function trainingSummary() {
    const source = trainingSources.find(s => s.id === root.querySelector('#ml-training [name="executionId"]')?.value);
    const summary = root.querySelector('#ml-training-source');
    if (summary)
        summary.textContent = source
            ? `${source.name}${source.kind === 'live' ? ' (tráfico real)' : ''}: ${n(source.records)} peticiones, del ${date(source.from)} al ${date(source.to)}.${source.incidents ? ` Se excluyen ${n(source.incidents)} confirmadas como incidente.` : ''}`
            : 'Selecciona los datos de referencia para ver su periodo y cantidad de peticiones.';
    // el periodo es util con trafico real, por ejemplo la ultima semana de monitoreo
    root.querySelectorAll('#ml-training [data-period]').forEach(b => { b.disabled = !source; });
}

async function trainingView(active) {
    const data = await MlApi.training();
    if (!Array.isArray(data?.sources) || !Array.isArray(data?.jobs) || !Array.isArray(data?.versions))
        throw new Error('El servicio de ajuste devolvió una respuesta inesperada.');

    trainingSources = data.sources;
    trainingBusy = data.jobs.some(j => ['queued', 'running'].includes(j.estado));
    const versionOf = j => data.versions.find(v => v.version === j.version);
    const base = data.versions.find(v => v.base);
    const rows = [...(base ? [`<tr><td><strong>${esc(base.version)}</strong><small>Modelo de referencia original</small></td><td>${base.activa ? badge('completed', 'En uso') : badge('ready', 'Validado')}</td><td>${base.activa ? `Entrenado con ${n(active.n_train)} peticiones` : 'Entrenado con el conjunto original'}</td><td>${!base.activa ? button('activate-model', 'Restaurar referencia', `data-id="${esc(base.version)}"`) : ''}</td></tr>`] : []),
        ...data.jobs.map(j => {
            const m = j.metricas, o = j.opciones, v = versionOf(j);
            return `<tr><td><strong>${esc(j.version ?? 'Sin versión')}</strong><small>Con «${esc(j.source_name)}» · ${date(j.creado_en)}</small></td><td>${versionState(j, v)}${j.error ? `<small class="ml-error-text">${esc(j.error)}</small>` : ''}</td>
                <td>${m ? `${n(m.clusters)} grupos · ${pct(m.ruido_validacion, 1)} atípicos en evaluación<small>Separación de grupos: ${quality(m.silhouette_validacion)}</small>
                    <details><summary>Detalles técnicos</summary><dl class="ml-details"><dt>Muestra</dt><dd>${n(j.registros)} de ${n(j.source_count)} peticiones</dd><dt>Entrenamiento / evaluación</dt><dd>${n(m.n_train)} / ${n(m.n_validacion)}</dd>
                    <dt>Grupos (entrenamiento)</dt><dd>${esc(distribution(m.grupos_train))}</dd><dt>Grupos (evaluación)</dt><dd>${esc(distribution(m.grupos_validacion))}</dd><dt>Atípicos entrenamiento</dt><dd>${pct(m.ruido_train, 1)}</dd>
                    <dt>DBCV aproximado</dt><dd>${m.dbcv_aproximado_train == null ? 'No disponible' : Number(m.dbcv_aproximado_train).toFixed(3)}</dd><dt>Parámetros</dt><dd>Grupo mínimo ${o.min_cluster_size} · vecinos ${o.min_samples} · semilla ${o.seed} · umbral ${o.umbral_alerta}</dd></dl></details>` : '<small>Sin resultados todavía</small>'}</td>
                <td>${j.estado === 'completed' ? button('validate-model', 'Validar versión', `data-id="${esc(j.id)}"`, 'btn btn-primary') : ''}${v?.validada && !v.activa ? button('activate-model', 'Activar versión', `data-id="${esc(v.version)}"`) : ''}</td></tr>`;
        })];

    return panel('Nuevo ajuste del modelo', steps([
        ['1. Ajustar', 'Aprende los patrones del tráfico real reciente o de un análisis histórico.'],
        ['2. Validar', 'Comprueba técnicamente la versión generada.'],
        ['3. Activar', 'Los nuevos análisis usarán esa versión.']]) +
        `<form id="ml-training"><div class="http-context"><label>Datos de referencia<select name="executionId" required><option value="">Selecciona los datos</option>${[['Tráfico real (monitoreo en tiempo real)', data.sources.filter(s => s.kind === 'live')], ['Análisis históricos', data.sources.filter(s => s.kind !== 'live')]].filter(([, list]) => list.length).map(([label, list]) => `<optgroup label="${label}">${list.map(s => `<option value="${esc(s.id)}">${esc(s.name)} · ${n(s.records)} peticiones</option>`).join('')}</optgroup>`).join('')}</select></label>
        <label>Desde (opcional)<input type="datetime-local" name="desde"></label><label>Hasta (opcional)<input type="datetime-local" name="hasta"></label></div>
        <div class="ml-actions ml-periods"><span class="ml-caption">Periodo rápido:</span>${[7, 30].map(d => `<button type="button" class="btn" data-period="${d}">Últimos ${d} días</button>`).join('')}<button type="button" class="text-action" data-period="0">Todo</button></div>
        <div class="http-context">
        <label>Tamaño mínimo de un grupo<input name="minClusterSize" type="number" min="5" max="2000" value="200" required><small>Peticiones parecidas necesarias para formar un patrón.</small></label>
        <label>Mínimo de vecinos<input name="minSamples" type="number" min="1" max="1000" value="20" required><small>Más alto = criterio más estricto y más atípicos.</small></label>
        <label>Umbral de alerta (0 a 1)<input name="threshold" type="number" min="0" max="1" step="0.01" value="0.80" required><small>Rareza mínima de un atípico para generar alerta.</small></label></div>
        <details class="ml-advanced"><summary>Opciones avanzadas</summary><div class="http-context"><label>Máximo de peticiones de la muestra<input name="maxRecords" type="number" min="100" max="30000" value="20000" required></label><label>Semilla de muestreo<input name="seed" type="number" min="0" max="2147483647" value="42" required></label></div></details>
        <p id="ml-training-source" class="ml-caption"></p><div class="ml-actions"><button class="btn btn-primary" ${!data.sources.length || trainingBusy ? 'disabled' : ''}>Ejecutar ajuste</button></div></form>
        ${!data.sources.length ? '<p class="ml-empty">No hay datos elegibles. Registra una fuente de monitoreo en tiempo real o termina un análisis histórico con al menos 100 peticiones.</p>' : ''}${trainingBusy ? '<p role="status" class="ml-caption">Hay un ajuste en curso; el estado se actualiza automáticamente.</p>' : ''}`,
        'Se toma una muestra de los datos: 70% para aprender y 30% para evaluar, en orden temporal. Con tráfico real, elige un periodo representativo (por ejemplo, la última semana); las peticiones confirmadas como incidente se excluyen. El ajuste crea una versión nueva; el modelo en uso no cambia hasta que la actives.') +
        panel('Versiones del modelo', table(['Versión', 'Estado', 'Resultado del ajuste', ''], rows.join('')),
            'Las métricas describen la calidad de los grupos sin etiquetas de ataques: no son una medida de precisión. Los análisis ya realizados conservan su versión.');
}

// HU-28 reportes
const REPORT_TYPES = {
    summary: 'Resumen del análisis',
    alerts: 'Alertas para revisión',
    full: 'Reporte completo (incluye detalle técnico)'
};

function reportDocument(report, run) {
    const s = report.summary, type = state.reportType, include = k => ({
        summary: ['kpi', 'groups', 'timeline'],
        alerts: ['kpi', 'alerts', 'routes'],
        full: ['kpi', 'groups', 'timeline', 'alerts', 'routes', 'tech']
    })[type].includes(k);
    const p = report.period ?? {};
    return `<article id="ml-report-doc" class="ml-report-doc"><header><p class="eyebrow">DISMATEC · ANÁLISIS HTTP</p><h2>${esc(REPORT_TYPES[type])}</h2>
        <dl class="ml-details"><dt>Análisis</dt><dd>${esc(run.datasetName)} (${esc(KIND[run.kind])})</dd><dt>Periodo</dt><dd>${p.from || p.to ? `Filtrado: ${date(p.from)} → ${date(p.to)}` : periodText(s)}</dd>
        <dt>Estado</dt><dd>${esc(STATE[run.state])}${run.state !== 'completed' ? ' (resultados provisionales)' : ''}</dd><dt>Modelo</dt><dd>${esc(run.modelVersion)}</dd><dt>Generado</dt><dd>${date(report.generatedAt)}</dd></dl></header>
        ${include('kpi') ? indicators(s, report.alertSummary) + `<p class="ml-caption">Se excluyeron ${n(report.execution.excluded)} recursos estáticos y ${n(report.execution.rejected)} líneas no válidas.</p>` : ''}
        ${include('groups') ? `<h3>Distribución por grupo</h3>${groupBars(s, run.id)}${groupTable(s)}` : ''}
        ${include('timeline') ? `<h3>Evolución en el tiempo</h3>${timelineChart(s)}` : ''}
        ${include('alerts') ? `<h3>Alertas con mayor puntuación (hasta ${report.alertLimit ?? 50})</h3><div class="ml-alert-counts"><span><strong>${n(report.alertSummary.pending)}</strong> pendientes</span><span><strong>${n(report.alertSummary.inReview)}</strong> en revisión</span><span><strong>${n(report.alertSummary.benign)}</strong> descartadas</span><span><strong>${n(report.alertSummary.incidents)}</strong> incidentes</span></div>
            ${table(['Fecha', 'Petición', 'Respuesta', 'Rareza', 'Revisión'], report.alerts.map(a => `<tr><td>${date(a.occurredAt)}</td><td class="ml-route"><b>${esc(a.method)}</b> ${esc(a.route)}</td><td>${a.status}</td><td>${Number(a.anomalyScore).toFixed(3)}</td><td>${esc(REVIEW[a.state] ?? a.state)}</td></tr>`).join(''), 'No hay alertas en este periodo.')}` : ''}
        ${include('routes') ? `<h3>Rutas con más alertas (hasta ${report.routeLimit ?? 20})</h3>${table(['Ruta', 'Peticiones', 'Alertas', 'Respuestas con error'], report.routes.map(r => `<tr><td class="ml-route">${esc(r.route)}</td><td>${n(r.count)}</td><td>${n(r.candidates)}</td><td>${n(r.errors)}</td></tr>`).join(''))}` : ''}
        ${include('tech') ? `<h3>Detalle técnico</h3>${densityMap(report)}<details><summary>Configuración del modelo</summary><pre class="ml-features">${esc(report.execution.modelMetadata ?? 'Sin metadatos')}</pre></details><ul class="ml-list ml-caption">${report.notes.map(x => `<li>${esc(x)}</li>`).join('')}</ul>` : ''}
        <footer class="ml-caption">Uso interno. Los orígenes aparecen anonimizados; el reporte no contiene direcciones IP. Las alertas son observaciones para revisión, no ataques confirmados.</footer></article>`;
}

async function reportsView() {
    const run = analyses().find(r => r.id === state.runId);
    state.report = await MlApi.report(state.runId, state.window, state.reportPeriod);
    return panel('Configurar reporte', `<form id="ml-report"><div class="http-context"><label>Tipo de reporte<select name="type">${Object.entries(REPORT_TYPES).map(([k, v]) => `<option value="${k}" ${k === state.reportType ? 'selected' : ''}>${v}</option>`).join('')}</select></label>
        <label>Análisis${runSelect('ml-run', state.runId)}</label>${periodFields('', state.reportPeriod)}</div><div class="ml-actions"><button class="btn btn-primary">Generar vista previa</button></div></form>`) +
        `<div class="ml-report-actions" id="report-export">${button('print-report', 'Imprimir / guardar PDF', '', 'btn btn-primary')}${button('export-csv', 'Descargar CSV')}${button('export-report', 'Descargar reporte JSON')}</div>` + reportDocument(state.report, run);
}

function exportCsv() {
    const r = state.report, s = r.summary;
    downloadCsv(`reporte-http-${r.execution.id}.csv`, [
        {
            title: 'Indicadores',
            headers: ['Indicador', 'Valor'],
            rows: [
                ['Peticiones analizadas', s.total],
                ['Atípicos', s.noise],
                ['Alertas', s.candidates],
                ['Alertas pendientes', r.alertSummary.pending],
                ['Incidentes confirmados', r.alertSummary.incidents],
                ['Desde', s.periodFrom ?? ''],
                ['Hasta', s.periodTo ?? '']]
        },
        {
            title: 'Grupos',
            headers: ['Grupo', 'Peticiones', 'Respuestas con error', 'Rutas distintas', 'Alertas'],
            rows: s.groups.map(g => [groupName(g.cluster), g.count, g.errorResponses, g.uniqueRoutes, g.candidates])
        },
        {
            title: 'Alertas',
            headers: ['Fecha UTC', 'Método', 'Ruta', 'Respuesta', 'Rareza', 'Revisión'],
            rows: r.alerts.map(a => [a.occurredAt, a.method, a.route, a.status, a.anomalyScore, REVIEW[a.state] ?? a.state])
        },
        {
            title: 'Rutas',
            headers: ['Ruta', 'Peticiones', 'Alertas', 'Errores'],
            rows: r.routes.map(x => [x.route, x.count, x.candidates, x.errors])
        },
    ]);
}

// carga y eventos
const NEEDS_RUN = ['http-analisis', 'http-eventos', 'http-reportes'];

async function content() {
    if (page === 'http-fuentes') return sourcesView();
    if (page === 'http-ejecuciones') return executionsView();
    if (page === 'http-monitoreo') return monitoringView();
    if (page === 'http-modelo') return modelView();
    if (page === 'http-comparacion') return comparisonView();
    if (!state.runId)
        return panel('Todavía no hay análisis', `<p class="ml-empty">Incorpora registros y ejecuta un análisis, o registra una fuente de monitoreo.</p><div class="ml-actions"><a class="btn btn-primary" href="http-fuentes.html">Incorporar registros</a><a class="btn" href="http-monitoreo.html">Monitoreo en tiempo real</a></div>`);
    if (page === 'http-eventos') return eventsView();
    if (page === 'http-reportes') return reportsView();

    return dashboardView();
}

async function load() {
    if (loading || busy || modal.open) return;

    loading = true;
    try {
        [state.datasets, state.runs] = await Promise.all([MlApi.datasets(), MlApi.runs()]);
        if (NEEDS_RUN.includes(page) && !analyses().some(r => r.id === state.runId))
            state.runId = analyses()[0]?.id ?? '';
        persist();

        const html = await content(), [title, description] = PAGES[page];
        root.innerHTML = `<section class="page-heading"><div><p class="eyebrow">ANÁLISIS HTTP</p><h1>${title}</h1><p>${description}</p></div>${headingAction()}</section>${html}`;
        if (page === 'http-modelo') {
            for (const [name, value] of Object.entries(trainingConfig)) {
                const field = root.querySelector(`#ml-training [name="${name}"]`);
                if (field) field.value = value;
            }
            trainingSummary();
        }
    } catch (error) {
        root.innerHTML = `<section class="page-heading"><div><p class="eyebrow">ANÁLISIS HTTP</p><h1>${PAGES[page]?.[0] ?? 'Análisis HTTP'}</h1></div></section><div role="alert" class="ml-error">${esc(error.message)}</div>${button('refresh', 'Reintentar')}`;
    } finally { loading = false; }
}

root.addEventListener('change', async event => {
    const t = event.target;
    if (t.closest('#ml-training')) {
        trainingConfig = Object.fromEntries(new FormData(root.querySelector('#ml-training')));
        trainingSummary();
    }
    if (t.id === 'http-window') {
        state.window = Number(t.value);
        persist();
        await load();
    }
    if (t.id === 'ml-run') {
        state.runId = t.value;
        state.page = 1;
        persist();
        await load();
    }
    if (t.matches('#ml-filter [name="view"]')) t.form.requestSubmit();
});

modal.addEventListener('change', event => {
    if (event.target.matches('#ml-review [name="state"]')) modal.querySelector('.ml-incident').hidden = event.target.value !== 'incident';
});

root.addEventListener('click', event => {
    const b = event.target.closest('#ml-training [data-period]');
    if (!b) return;

    const form = b.form ?? b.closest('form'), days = Number(b.dataset.period);
    const local = d => new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
    form.desde.value = days ? local(new Date(Date.now() - days * 864e5)) : '';
    form.hasta.value = days ? local(new Date()) : '';
    trainingConfig = Object.fromEntries(new FormData(form));
});

root.addEventListener('click', async event => {
    const target = event.target.closest('[data-action]');
    if (!target || busy) return;

    const action = target.dataset.action, id = target.dataset.id;
    if (action === 'activate-model') {
        showDialog('Confirmar cambio de modelo', `<form id="ml-activate-model" data-version="${esc(id)}"><p>Los nuevos análisis usarán la versión <strong>${esc(id)}</strong>.</p><p>Los análisis y fuentes en curso con otra versión se detendrán al detectar el cambio y deberán iniciarse de nuevo. Los resultados ya guardados no cambian.</p><div class="ml-actions"><button type="button" class="btn" data-dismiss-model>Cancelar</button><button class="btn btn-primary">Confirmar activación</button></div></form>`);
        modal.querySelector('[data-dismiss-model]').onclick = () => modal.close();
        return;
    }
    if (action === 'agent-help') {
        showDialog('Iniciar el agente', agentHelp(id));
        return;
    }
    if (target.closest('[data-period]')) return;
    if (action === 'export-report' && state.report) {
        downloadJson(`hdbscan-${state.report.execution.id}.json`, state.report);
        return;
    }
    if (action === 'export-csv' && state.report) {
        exportCsv();
        return;
    }
    if (action === 'print-report') {
        window.print();
        return;
    }
    if (action === 'review') {
        await reviewDialog(id);
        return;
    }
    if (action === 'confirm') {
        const [sucursales] = await catalogos();
        showDialog('Confirmar incorporación', `<form id="ml-confirm" data-id="${esc(id)}"><label>Nombre del conjunto<input name="name" value="${esc(target.dataset.name)}" maxlength="120" required></label><label>Origen o finalidad<input name="origin" maxlength="300" required placeholder="Servidor web del portal, semana del 1 al 7"></label><label>Sucursal (opcional)<select name="idSucursal">${options(sucursales, 'idSucursal', 'nombre', 'Toda la organización')}</select></label><p class="ml-caption">Solo se incorporan las peticiones válidas. Si se confirma un incidente en estos registros, la sugerencia de concientización se dirigirá a esta sucursal.</p><button class="btn btn-primary">Confirmar</button></form>`);
        return;
    }

    busy = true;
    target.disabled = true;
    try {
        if (action === 'cancel-validation') await MlApi.cancel(id);
        if (action === 'validate-model') {
            await MlApi.validateModel(id);
            toast('Versión validada. Ya puedes activarla.');
        }
        if (['stop', 'resume'].includes(action)) await MlApi.control(id, action);
        if (action === 'previous') state.page = Math.max(1, state.page - 1);
        if (action === 'next') state.page++;
        if (action === 'clear') {
            state.filter = {};
            state.page = 1;
        }
        if (action === 'clear-period') state.period = {};
        if (action === 'clear-history') {
            state.historySearch = '';
            state.pages.historial = 1;
        }
    } catch (error) {
        toast(error.message, 'error');
    } finally {
        busy = false;
        await load();
    }
});

async function submit(event) {
    const form = event.target;
    if (!form.matches('form[id^="ml-"]')) return;

    event.preventDefault();
    if (busy) return;

    const data = new FormData(form), submitter = form.querySelector('button[type="submit"], button:not([type])');
    busy = true;
    if (submitter) submitter.disabled = true;
    try {
        if (form.id === 'ml-upload') {
            await MlApi.upload(data.get('file'));
            toast('Archivo recibido. Se está validando.');
        }
        if (form.id === 'ml-confirm') {
            await MlApi.confirm({
                validationId: form.dataset.id,
                name: data.get('name'),
                origin: data.get('origin'),
                idSucursal: optionalNumber(data.get('idSucursal'))
            });
            modal.close();
            toast('Conjunto incorporado. Ya puedes analizarlo.');
        }
        if (form.id === 'ml-start') {
            const job = await MlApi.start(
                { datasetId: data.get('datasetId'), windowMinutes: 5, speed: Number(data.get('speed') || 60) },
                data.has('replay'));
            state.runId = job.id;
            persist();
            toast('Análisis iniciado. Puedes seguir su avance en el historial.');
        }
        if (form.id === 'ml-history') {
            state.historySearch = String(data.get('search') ?? '').trim();
            state.pages.historial = 1;
        }
        if (form.id === 'ml-source') {
            const job = await MlApi.source({
                name: data.get('name'),
                fileName: data.get('fileName'),
                idSucursal: optionalNumber(data.get('idSucursal'))
            });
            showDialog('Fuente registrada', agentHelp(job.id));
        }
        if (form.id === 'ml-selection') { state.period = readPeriod(data); }
        if (form.id === 'ml-filter') {
            // "Mostrar" define la base, los demas filtros se suman
            const next = { ...(VIEWS[data.get('view')]?.[1] ?? {}) };
            for (const [key, value] of data)
                if (value !== '' && !['view', 'from', 'to'].includes(key)) next[key] = value;
            Object.assign(next, readPeriod(data));
            for (const k of Object.keys(next)) if (next[k] === '') delete next[k];
            state.filter = next;
            state.page = 1;
        }
        if (form.id === 'ml-compare') {
            state.compare = {
                left: data.get('left'),
                right: data.get('right'),
                leftPeriod: readPeriod(data, 'left'),
                rightPeriod: readPeriod(data, 'right')
            };
            if (!state.compare.left || !state.compare.right) throw new Error('Selecciona un análisis para cada periodo.');
        }
        if (form.id === 'ml-report') {
            state.reportType = data.get('type');
            state.runId = data.get('ml-run') || state.runId;
            state.reportPeriod = readPeriod(data);
            persist();
        }
        if (form.id === 'ml-training') {
            const periodo = readPeriod(new Map([['from', data.get('desde')], ['to', data.get('hasta')]]));
            const body = {
                executionId: data.get('executionId'),
                desde: periodo.from || null,
                hasta: periodo.to || null,
                ...Object.fromEntries(['minClusterSize', 'minSamples', 'maxRecords', 'seed', 'threshold']
                    .map(k => [k, Number(data.get(k))]))
            };
            if (body.minSamples > body.minClusterSize)
                throw new Error('El mínimo de vecinos no puede superar el tamaño mínimo del grupo.');
            await MlApi.train(body);
            toast('Ajuste iniciado. Su estado aparece en «Versiones del modelo».');
        }
        if (form.id === 'ml-activate-model') {
            await MlApi.activateModel(form.dataset.version);
            modal.close();
            toast('Versión activa actualizada.');
        }
        if (form.id === 'ml-review') {
            await MlApi.review(form.dataset.id, {
                state: data.get('state'),
                notes: data.get('notes'),
                confirmIncident: data.has('confirmed'),
                idSucursal: optionalNumber(data.get('idSucursal')),
                idArea: optionalNumber(data.get('idArea')),
                idCategoria: optionalNumber(data.get('idCategoria'))
            });
            modal.close();
            toast(data.get('state') === 'incident'
                ? 'Incidente confirmado. Se registró una sugerencia en «Decisiones preventivas».'
                : 'Revisión guardada.');
        }
    } catch (error) {
        toast(error.message, 'error');
    } finally {
        busy = false;
        if (submitter) submitter.disabled = false;
        if (!modal.open) await load();
    }
}

root.addEventListener('submit', submit);
modal.addEventListener('submit', submit);
for (const clave of Object.keys(state.pages))
    enlazarPaginador(root, clave, pagina => {
        state.pages[clave] = pagina;
        load();
    });
await load();

// no se interrumpe a quien edita un formulario o revisa un resultado
const editing = () => root.contains(document.activeElement)
    && document.activeElement?.matches('input:not([type="file"]), textarea, select');
const canRefresh = () => !document.hidden && !busy && !modal.open && !editing();
// refresco periodico con trabajo en curso, en paginas en vivo solo es respaldo
const timer = setInterval(() => {
    const active = trainingBusy || state.runs.some(r => ['queued', 'running'].includes(r.state));
    if (LIVE_PAGES.includes(page) && liveStatusValue !== 'offline') return;
    if (canRefresh() && active && !['http-comparacion', 'http-reportes'].includes(page)) load();
}, ['http-fuentes', 'http-ejecuciones', 'http-monitoreo'].includes(page) ? 5000 : 15000);

if (LIVE_PAGES.includes(page)) {
    let lastSnapshot = null, pendingTimer = null;
    // aplica el cambio si se puede, si el usuario esta editando se reintenta en 2 s
    const applyChange = () => {
        clearTimeout(pendingTimer);
        if (canRefresh()) load();
        else pendingTimer = setTimeout(applyChange, 2000);
    };
    subscribeLive('/ml/live', {
        onStatus: status => {
            liveStatusValue = status;
            const pill = document.getElementById('live-pill');
            if (pill) {
                pill.dataset.state = status;
                pill.querySelector('span').textContent = liveLabels[status];
            }
        },
        onSnapshot: snapshot => {
            const previous = lastSnapshot;
            lastSnapshot = snapshot;
            if (!previous || previous.version === snapshot.version) return;

            // en monitoreo observeAlerts ya avisa por fuente, aqui se avisa en la lista de atipicos
            const added = snapshot.recentCandidates - previous.recentCandidates;
            if (page === 'http-eventos' && added > 0)
                toast(`${n(added)} ${added === 1 ? 'nuevo comportamiento atípico supera' : 'nuevos comportamientos atípicos superan'} el umbral de alerta.`, 'warning');
            // una rafaga de cambios se agrupa en una sola recarga
            clearTimeout(pendingTimer);
            pendingTimer = setTimeout(applyChange, 600);
        },
    });
}

window.addEventListener('pagehide', () => clearInterval(timer), { once: true });
