import { AdminApi, PreventionApi } from '../services/dashboard-api.js?v=20261119-control';
import { MlApi } from '../services/ml-api.js';
import { el, escapeHtml as esc, toast } from '../utils/ui.js';
import { subscribeLive } from '../services/live.js?v=20261120';
import { icon } from '../components/icons.js';
import { COLORS, histogram, lineChart, meterList, segmentedBar, sparkline } from '../components/charts.js?v=20261120';

// centro de control: primero la deteccion HDBSCAN, luego factor humano y decisiones
const stateLabels = {
    queued: 'En cola',
    running: 'En curso',
    completed: 'Completado',
    failed: 'Con error',
    stopped: 'Detenido'
};
const kindLabels = { historical: 'Histórico', replay: 'Reproducción', live: 'Tiempo real' };
const LEVEL_COLORS = { Bajo: COLORS.benign, Medio: COLORS.normal, Alto: COLORS.threshold, Crítico: COLORS.incident };
const number = value => Number(value ?? 0).toLocaleString('es-BO');
const percent = (part, whole) => whole
    ? `${(part / whole * 100).toLocaleString('es-BO', { maximumFractionDigits: 1 })} %`
    : '0 %';
const dateTime = value => value
    ? new Date(value).toLocaleString('es-BO', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })
    : '—';
const empty = text => `<p class="empty-state">${esc(text)}</p>`;
const value = result => result.status === 'fulfilled' ? result.value : null;
const state = { analyses: [], model: null, loading: false, queued: false, version: 0, live: null };
document.querySelectorAll('[data-icon]').forEach(node => { node.innerHTML = icon(node.dataset.icon); });

// quiet: actualizacion del canal en vivo sin vaciar los paneles
async function load({ quiet = false } = {}) {
    if (state.loading) {
        state.queued = true;
        return;
    }

    state.loading = true;
    el('dashboard-error').classList.add('hidden');
    document.getElementById('contenido').classList.toggle('is-updating', quiet);

    const settled = await Promise.allSettled([
        MlApi.runs(),
        MlApi.models(),
        PreventionApi.areas(),
        PreventionApi.pendingSuggestions(),
        AdminApi.campaigns()]);
    const [runs, models, areas, suggestions, campaigns] = settled.map(value);
    const failures = settled.filter(r => r.status === 'rejected').map(r => r.reason?.message);
    state.analyses = (runs ?? []).filter(run => run.kind !== 'validation');
    state.model = models?.active ?? null;

    renderModel();
    renderRuns(runs ? state.analyses : null);
    renderHuman(areas);
    renderDecisions(suggestions);
    renderCampaign(campaigns);

    const select = el('run-select'), previous = select.value;
    // si se veia el analisis mas reciente, se sigue al nuevo cuando aparece
    const followLatest = !previous || select.selectedIndex <= 0;
    const usable = state.analyses.filter(run => ['completed', 'running', 'stopped'].includes(run.state));
    select.innerHTML = usable.length
        ? usable.slice(0, 20)
            .map(run => `<option value="${esc(run.id)}">${esc(run.datasetName ?? 'Análisis')} · ${esc(dateTime(run.createdAt))}</option>`)
            .join('')
        : '<option value="">Sin análisis</option>';
    select.disabled = !usable.length;
    if (!followLatest && usable.some(run => run.id === previous)) select.value = previous;

    failures.push(...await loadAnalysis(select.value));
    showFailures(failures);
    el('snapshot-time').textContent = `Actualizado ${new Date().toLocaleTimeString('es-BO', { hour: '2-digit', minute: '2-digit' })}`;
    document.getElementById('contenido').classList.remove('is-updating');
    state.loading = false;
    if (state.queued) {
        state.queued = false;
        load({ quiet: true });
    }
}

function showFailures(failures) {
    const first = failures.find(Boolean);
    if (!first) return;

    el('dashboard-error').textContent = `Algunos datos no se pudieron cargar. ${first}`;
    el('dashboard-error').classList.remove('hidden');
}

// indicadores y graficos de un analisis, por defecto el mas reciente
async function loadAnalysis(id) {
    const version = ++state.version;
    const run = state.analyses.find(item => item.id === id);
    if (!run) {
        renderNoAnalysis();
        return [];
    }

    const settled = await Promise.allSettled([
        MlApi.summary(id, 60),
        MlApi.results(id, { candidate: true, review: 'pending', page: 1, size: 5 }),
        MlApi.profile(id),
        MlApi.correlations(id),
    ]);
    if (version !== state.version) return [];

    const [summary, pending, profile, correlations] = settled.map(value);
    reviewLinks(id);
    renderSummary(run, summary);
    renderTriage(pending, id);
    renderReview(profile?.review);
    renderProfile(profile);
    renderSubjects(correlations);

    return settled.filter(r => r.status === 'rejected').map(r => r.reason?.message);
}

function renderModel() {
    const active = state.model;
    el('model-light').classList.toggle('is-off', !active);
    el('model-status').textContent = active ? `Activo · ${active.version}` : 'Servicio del modelo no disponible';
    // los parametros van con los diagnosticos, no en la vista principal
    const decimal = v => Number(v).toLocaleString('es-BO', { maximumFractionDigits: 2 });
    el('model-params').textContent = active
        ? `Parámetros: HDBSCAN ${active.version} · tamaño mínimo de grupo ${active.min_cluster_size} · umbral de alerta ${decimal(active.umbral_alerta)}.`
        : 'Parámetros del modelo no disponibles.';

    const live = state.analyses.filter(run => run.kind === 'live' && run.state === 'running');
    el('live-status').textContent = live.length
        ? `${live.filter(run => run.agentConnected).length} de ${live.length} conectadas`
        : 'Ninguna activa';
}

function renderNoAnalysis() {
    ['kpi-total', 'kpi-noise', 'kpi-candidates', 'kpi-pending', 'kpi-incidents', 'kpi-clusters']
        .forEach(id => { el(id).textContent = '—'; });
    const message = empty('Aún no hay análisis. Incorpora un conjunto en «Conjuntos de registros» y ejecuta un análisis.');
    ['chart-timeline', 'chart-review', 'chart-clusters', 'chart-scores', 'chart-status', 'chart-routes', 'chart-subjects', 'triage-list']
        .forEach(id => { el(id).innerHTML = message; });
    el('period-status').textContent = 'Sin análisis';
}

function renderSummary(run, summary) {
    if (!summary) {
        el('chart-timeline').innerHTML = empty('No se pudo consultar el resumen del análisis.');
        return;
    }

    const windows = summary.windows.slice(-72);
    el('period-status').textContent = summary.periodFrom
        ? `${dateTime(summary.periodFrom)} → ${dateTime(summary.periodTo)} · ${stateLabels[run.state] ?? run.state}`
        : 'Sin resultados';
    el('kpi-total').textContent = number(summary.total);
    el('kpi-total-caption').textContent = `${number(run.valid)} válidos de ${number(run.read)} leídos`;
    el('kpi-noise').textContent = number(summary.noise);
    el('kpi-noise-caption').textContent = `${percent(summary.noise, summary.total)} del total`;
    el('kpi-candidates').textContent = number(summary.candidates);
    el('kpi-candidates-caption').textContent = `${percent(summary.candidates, summary.noise)} de los atípicos`;
    el('kpi-total-spark').innerHTML = sparkline(windows.map(w => w.count), COLORS.normal);
    el('kpi-noise-spark').innerHTML = sparkline(windows.map(w => w.noise), COLORS.atypical);
    el('kpi-candidates-spark').innerHTML = sparkline(windows.map(w => w.candidates), COLORS.threshold);

    const clusters = summary.groups.filter(g => g.cluster !== -1);
    el('kpi-clusters').textContent = number(clusters.length);
    el('kpi-clusters-caption').textContent = state.model
        ? `Tamaño mínimo de grupo: ${state.model.min_cluster_size}`
        : 'Patrones habituales identificados';

    if (windows.length)
        lineChart(el('chart-timeline'), {
            labels: windows.map(w => dateTime(w.from)),
            series: [
                { name: 'Atípicos', color: COLORS.atypical, values: windows.map(w => w.noise) },
                { name: 'Sobre el umbral', color: COLORS.threshold, values: windows.map(w => w.candidates) }],
            ariaLabel: `Atípicos por ventana de 60 minutos: máximo ${Math.max(...windows.map(w => w.noise))} en una ventana.`,
        });
    else
        el('chart-timeline').innerHTML = empty('El análisis aún no tiene resultados.');

    const noise = summary.groups.find(g => g.cluster === -1);
    const ranked = [...clusters].sort((a, b) => b.count - a.count);
    const top = ranked.slice(0, 7), rest = ranked.slice(7).reduce((sum, g) => sum + g.count, 0);
    const pct = count => count / Math.max(1, summary.total) * 100;
    const items = top.map(g => ({
        label: `Grupo ${g.cluster}`,
        value: pct(g.count),
        color: COLORS.normal,
        detail: `${number(g.count)} registros · ${number(g.uniqueRoutes)} rutas`,
        tooltip: `Grupo ${g.cluster}: ${number(g.count)} registros, ${number(g.errorResponses)} respuestas con error, pertenencia media ${Number(g.meanStrength).toFixed(2)}`
    }));
    if (rest)
        items.push({
            label: `Otros ${ranked.length - 7} grupos`,
            value: pct(rest),
            color: COLORS.normal,
            detail: `${number(rest)} registros`
        });
    if (noise)
        items.push({
            label: 'Ruido (−1) · atípicos',
            value: pct(noise.count),
            color: COLORS.atypical,
            detail: `${number(noise.count)} registros · puntaje medio ${Number(noise.meanScore).toFixed(2)}`
        });

    el('chart-clusters').innerHTML = items.length
        ? meterList(items, { max: 100, format: v => v.toLocaleString('es-BO', { maximumFractionDigits: 1 }), suffix: ' %' })
        : empty('Sin agrupamientos en este análisis.');
}

// estado de revision de las candidatas sobre el umbral del analisis
function renderReview(review) {
    el('kpi-pending').textContent = review ? number(review.pending) : '—';
    el('kpi-incidents').textContent = review ? number(review.incidents) : '—';
    if (!review) {
        el('chart-review').innerHTML = empty('No se pudo consultar el estado de revisión.');
        return;
    }

    const reviewed = review.benign + review.incidents;
    el('kpi-incidents-caption').textContent = reviewed
        ? `${percent(review.incidents, reviewed)} de lo revisado`
        : 'Aún no hay resultados revisados';
    el('chart-review').innerHTML = segmentedBar([
        { label: 'Pendientes', value: review.pending, color: COLORS.threshold },
        { label: 'En revisión', value: review.inReview, color: COLORS.normal },
        { label: 'Benignos', value: review.benign, color: COLORS.benign },
        { label: 'Incidentes confirmados', value: review.incidents, color: COLORS.incident },
    ]);
    el('review-caption').textContent = reviewed
        ? `De ${number(review.total)} resultados sobre el umbral, ${number(reviewed)} ya fueron revisados. Tasa de confirmación: ${percent(review.incidents, reviewed)}.`
        : `${number(review.total)} resultados sobre el umbral esperan revisión para medir cuántos son incidentes reales.`;
}

// perfil exacto de todos los atipicos, calculado en el servidor sin muestreo
function renderProfile(profile) {
    el('score-sample').textContent = profile ? `${number(profile.noise)} atípicos · exacto` : '—';
    if (!profile?.noise) {
        const message = empty(profile
            ? 'No hay comportamientos atípicos en este análisis.'
            : 'No se pudo consultar el perfil de los atípicos.');
        ['chart-scores', 'chart-status', 'chart-routes'].forEach(id => { el(id).innerHTML = message; });
        return;
    }

    const { scores } = profile;
    histogram(el('chart-scores'), {
        ...scores,
        ariaLabel: `Distribución del puntaje de anomalía de ${profile.noise} atípicos entre ${scores.min} y ${scores.max}; umbral ${scores.threshold}.`
    });

    const names = { 1: 'informativa', 2: 'éxito', 3: 'redirección', 4: 'error cliente', 5: 'error servidor' };
    el('chart-status').innerHTML = meterList(profile.status.map(row => ({
        label: `${row.statusClass}xx · ${names[row.statusClass] ?? 'otra'}`,
        value: row.count,
        color: row.statusClass >= 4 ? COLORS.atypical : COLORS.normal,
        tooltip: `${row.statusClass}xx: ${number(row.count)} de ${number(profile.noise)} atípicos (${percent(row.count, profile.noise)})`
    })), { max: profile.noise });
    el('chart-routes').innerHTML = meterList(profile.routes.map(row => ({
        label: row.route,
        value: row.count,
        color: row.candidates ? COLORS.threshold : COLORS.atypical,
        detail: `${number(row.candidates)} sobre el umbral`,
        tooltip: `${row.route}: ${number(row.count)} atípicos, ${number(row.candidates)} sobre el umbral`
    })), { max: profile.noise });
}

function renderSubjects(correlations) {
    if (!correlations) {
        el('chart-subjects').innerHTML = empty('No se pudo consultar la correlación.');
        return;
    }

    const rows = correlations.slice(0, 6).map(row => ({
        label: row.subject,
        value: row.events,
        color: row.candidates ? COLORS.threshold : COLORS.atypical,
        detail: `${number(row.candidates)} sobre el umbral · ${dateTime(row.from)} → ${dateTime(row.to)}`
    }));
    el('chart-subjects').innerHTML = rows.length
        ? `${meterList(rows)}<p class="panel-caption">Orígenes seudonimizados con más de un atípico en el análisis.</p>`
        : empty('Ningún origen repite comportamientos atípicos.');
}

// todo acceso a triage usa el mismo filtro: este analisis, sobre el umbral y pendiente
const pendingHref = runId =>
    `http-eventos.html?${new URLSearchParams({ ...(runId ? { run: runId } : {}), candidate: 'true', review: 'pending' })}`;

function reviewLinks(runId) {
    el('link-pending').href = pendingHref(runId);
    el('link-candidates').href = `http-eventos.html?${new URLSearchParams({ ...(runId ? { run: runId } : {}), candidate: 'true' })}`;
}

function renderTriage(page, runId) {
    const list = el('triage-list'), button = el('btn-review');
    button.href = pendingHref(runId);
    if (!page) {
        list.innerHTML = empty('No se pudo consultar la cola de revisión.');
        button.className = 'btn btn-ghost cc-review-btn';
        button.innerHTML = `Ver comportamientos atípicos ${icon('arrow')}`;
        return;
    }

    el('triage-count').textContent = `${number(page.total)} pendientes`;
    button.className = `btn ${page.total ? 'btn-primary' : 'btn-ghost'} cc-review-btn`;
    button.innerHTML = `${page.total ? `Revisar ${number(page.total)} ${page.total === 1 ? 'pendiente' : 'pendientes'}` : 'Ver comportamientos atípicos'} ${icon('arrow')}`;
    list.innerHTML = page.items.length
        ? page.items.map(item => `<a class="triage-item" href="${esc(pendingHref(runId))}">
        <span class="score" title="Puntaje de anomalía (umbral ${esc(item.threshold)})">${Number(item.anomalyScore).toFixed(2)}</span>
        <span class="triage-main"><strong class="mono">${esc(item.method)} ${esc(item.route)}</strong><small>${esc(item.subject)} · HTTP ${esc(item.status)} · ${esc(dateTime(item.occurredAt))}</small></span>
        ${icon('chevron')}</a>`).join('')
        : empty('No hay comportamientos atípicos pendientes de revisión.');
}

function renderHuman(areas) {
    if (!areas) {
        ['chart-levels', 'area-risk'].forEach(id => { el(id).innerHTML = empty('El riesgo por área no está disponible.'); });
        ['kpi-areas', 'kpi-score', 'kpi-people'].forEach(id => { el(id).textContent = '—'; });
        return;
    }

    const visible = areas.filter(area => !area.oculto);
    const evaluated = visible.reduce((sum, a) => sum + a.evaluados, 0);
    const sum = key => visible.reduce((total, a) => total + (a[key] ?? 0), 0);
    // misma regla que el seguimiento preventivo: nivel Alto/Critico o >= 30 % de personas en esos niveles
    const high = visible.filter(a => a.evaluados > 0
        && (['Alto', 'Crítico'].includes(a.nivel) || (a.porcentajeAltoCritico ?? 0) >= 30));
    el('kpi-areas').textContent = number(high.length);
    el('kpi-areas-caption').textContent = `De ${number(visible.filter(a => a.evaluados > 0).length)} áreas con personas evaluadas`;

    const weighted = visible.filter(a => a.promedio != null && a.evaluados > 0);
    const average = weighted.reduce((total, a) => total + a.promedio * a.evaluados, 0)
        / Math.max(1, weighted.reduce((t, a) => t + a.evaluados, 0));
    el('kpi-score').textContent = weighted.length ? average.toLocaleString('es-BO', { maximumFractionDigits: 0 }) : '—';
    el('kpi-score-caption').textContent = `${number(evaluated)} colaboradores evaluados`;

    const highPeople = sum('alto') + sum('critico');
    el('kpi-people').textContent = number(highPeople);
    el('kpi-people-caption').textContent = `${percent(highPeople, evaluated)} del total evaluado`;
    el('chart-levels').innerHTML = segmentedBar([
        { label: 'Bajo', value: sum('bajo'), color: LEVEL_COLORS.Bajo },
        { label: 'Medio', value: sum('medio'), color: LEVEL_COLORS.Medio },
        { label: 'Alto', value: sum('alto'), color: LEVEL_COLORS.Alto },
        { label: 'Crítico', value: sum('critico'), color: LEVEL_COLORS.Crítico },
    ]);

    const top = visible
        .filter(a => a.evaluados > 0)
        .sort((a, b) => (b.porcentajeAltoCritico ?? 0) - (a.porcentajeAltoCritico ?? 0))
        .slice(0, 5);
    el('area-risk').innerHTML = top.length
        ? meterList(top.map(a => ({
            label: a.area,
            value: Number(a.porcentajeAltoCritico ?? 0),
            detail: `Nivel ${a.nivel} · ${number(a.evaluados)} evaluados`,
            color: high.includes(a) ? COLORS.threshold : COLORS.normal
        })), { max: 100, format: v => v.toLocaleString('es-BO', { maximumFractionDigits: 0 }), suffix: ' %' })
        : empty('Todavía no hay áreas con colaboradores evaluados.');
}

function renderDecisions(suggestions) {
    const list = el('decision-list');
    if (!suggestions) {
        list.innerHTML = empty('Las sugerencias no están disponibles.');
        el('kpi-decisions').textContent = '—';
        return;
    }

    el('kpi-decisions').textContent = number(suggestions.length);
    list.innerHTML = suggestions.length
        ? suggestions.slice(0, 4).map(s => `<div class="priority"><span class="priority-symbol">${icon(s.origen === 'http' ? 'radar' : 'shield')}</span>
        <div><strong>${esc(s.tema ? `Reforzar «${s.tema}»` : 'Refuerzo de concientización')}${s.area ? ` · ${esc(s.area)}` : ''}</strong>
        <p>${esc(s.origen === 'http' ? 'Comportamiento atípico confirmado' : 'Incidente reportado confirmado')} · ${number(s.cantidadEvidencias)} evidencia(s)${s.alcance != null ? ` · ${number(s.alcance)} persona(s)` : ''}</p></div></div>`).join('')
        : empty('No hay decisiones pendientes. Se generan al confirmar comportamientos atípicos o incidentes.');
}

// lado phishing del ciclo preventivo: campanias en curso
function renderCampaign(campaigns) {
    if (!campaigns) {
        el('kpi-campaign').textContent = '—';
        el('kpi-campaign-caption').textContent = 'Las campañas no están disponibles.';
        return;
    }

    const running = campaigns.filter(c => (c.idEstado ? c.idEstado === 2 : c.estado === 'En Ejecución'));
    const waiting = campaigns.filter(c => (c.idEstado ? c.idEstado === 1 : c.estado === 'Pendiente'));
    el('kpi-campaign').textContent = number(running.length);

    const first = running[0];
    el('kpi-campaign-caption').textContent = first
        ? `En ejecución: «${first.titulo}» · ${number(first.enviosAceptados)} enviados · ${number(first.enviosPendientes)} por enviar`
        : waiting.length
            ? `Ninguna en ejecución · ${number(waiting.length)} por aprobar o iniciar`
            : 'Ninguna en ejecución';
}

function renderRuns(runs) {
    const body = el('runs-body');
    if (!runs) {
        body.innerHTML = '<tr><td colspan="8" class="table-state">El historial de análisis no está disponible.</td></tr>';
        return;
    }

    body.innerHTML = runs.length
        ? runs.slice(0, 6).map(run => `<tr><td>${esc(run.datasetName ?? '—')}</td><td>${esc(run.sucursal ?? 'Sin sucursal')}</td>
        <td>${esc(kindLabels[run.kind] ?? run.kind)}</td><td><span class="result-badge ${run.state === 'failed' ? 'is-failed' : ''}">${esc(stateLabels[run.state] ?? run.state)}</span></td>
        <td class="num">${number(run.valid)}</td><td class="num">${number(run.noise)}</td><td class="num">${percent(run.noise, run.valid)}</td><td>${esc(dateTime(run.createdAt))}</td></tr>`).join('')
        : '<tr><td colspan="8" class="table-state">Aún no hay análisis. Empieza en «Conjuntos de registros».</td></tr>';
}

el('run-select').addEventListener('change', async event => { showFailures(await loadAnalysis(event.target.value)); });

// tiempo real: el servidor avisa cuando cambian resultados, revisiones o sugerencias
const liveLabels = {
    connecting: 'Conectando…',
    live: 'En vivo',
    reconnecting: 'Reconectando…',
    offline: 'Sin conexión en vivo'
};
let refreshTimer = null, fallbackTimer = null;

function setLiveStatus(status) {
    const pill = el('live-pill');
    pill.dataset.state = status;
    pill.querySelector('span').textContent = liveLabels[status];
    // respaldo sin canal en vivo: se consulta cada 60 s
    clearInterval(fallbackTimer);
    if (status === 'offline') fallbackTimer = setInterval(() => load({ quiet: true }), 60000);
}

function onSnapshot(snapshot) {
    const previous = state.live;
    state.live = snapshot;
    if (!previous || previous.version === snapshot.version) return;

    const added = snapshot.recentCandidates - previous.recentCandidates;
    if (added > 0) {
        toast(`${number(added)} ${added === 1 ? 'nuevo comportamiento atípico supera' : 'nuevos comportamientos atípicos superan'} el umbral de alerta.`, 'warning');
        // el aviso se desvanece pero la cola mantiene un marcador hasta que el admin la abra
        const chip = el('triage-new'), total = Number(chip.dataset.count || 0) + added;
        chip.dataset.count = total;
        chip.hidden = false;
        chip.textContent = `${number(total)} ${total === 1 ? 'nuevo' : 'nuevos'} · Ver`;
        chip.href = el('btn-review').href;
    }

    clearTimeout(refreshTimer);
    refreshTimer = setTimeout(() => load({ quiet: true }), 800);
}

el('live-pill').addEventListener('click', () => load({ quiet: true }));
el('triage-new').addEventListener('click', () => {
    el('triage-new').hidden = true;
    el('triage-new').dataset.count = 0;
});

// los diagnosticos quedan plegados salvo que el admin los abriera la ultima vez
try {
    el('cc-diagnostics').open = localStorage.getItem('cc.diagnostics') === 'open';
} catch { }
el('cc-diagnostics').addEventListener('toggle', event => {
    try {
        localStorage.setItem('cc.diagnostics', event.target.open ? 'open' : 'closed');
    } catch { }
});

await load();
subscribeLive('/ml/live', { onSnapshot, onStatus: setLiveStatus });
