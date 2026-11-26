import { apiCallAuth } from '../services/api.js';
import { el, escapeHtml as esc } from '../utils/ui.js';
import { etiqueta as nistLabel } from '../utils/phish-scale.js';
import { renderPaginador, enlazarPaginador } from '../components/paginacion.js';

const compare = document.body.dataset.page === 'comparacion-evaluaciones';
const base = '/resultados-campanias';
let catalog = null, version = 0, detailVersion = 0, peoplePage = 1, eventsPage = 1, eventType = '';
const date = value => value ? new Date(value).toLocaleString('es-BO', { timeZone: 'America/La_Paz' }) : 'Sin fecha registrada';
const percent = value => value == null ? 'Sin datos' : `${Number(value).toFixed(2)} %`;
const yes = value => value ? 'Sí' : 'No';
const typeLabel = value => ({ inicial: 'Inicial · pretest', posterior: 'Posterior · postest', seguimiento: 'Seguimiento' })[value]
    || 'Sin clasificar';
const actionLabel = value => ({
    apertura: 'Apertura',
    clic: 'Clic',
    credenciales: 'Credenciales en página señuelo',
    reporte: 'Reporte',
    capacitacion: 'Capacitación completada'
})[value] || value;
const minutes = value => value == null
    ? 'Sin datos'
    : Number(value) < 60
    ? `${Number(value).toFixed(1)} min`
    : `${(Number(value) / 60).toFixed(1)} h`;
const deliveryLabel = value => ({
    accepted: 'Aceptado por el proveedor',
    failed: 'Fallido',
    pending: 'Pendiente',
    canceled: 'Cancelado',
    legacy: 'Histórico sin confirmación'
})[value] || value;
const panel = (title, content) => `<section class="panel"><div class="panel-heading"><h2>${esc(title)}</h2></div>${content}</section>`;
const table = (headers, rows, message = 'No hay registros para estos filtros.') => `<div class="results-scroll"><table class="results-table"><thead><tr>${headers.map(h => `<th>${esc(h)}</th>`).join('')}</tr></thead><tbody>${rows || `<tr><td colspan="${headers.length}">${esc(message)}</td></tr>`}</tbody></table></div>`;
const card = (label, value, caption) => `<article class="results-card"><span>${esc(label)}</span><strong>${esc(value)}</strong><small>${esc(caption)}</small></article>`;

el('resultados-app').innerHTML = `<section class="page-heading"><div><p class="eyebrow">EVALUACIÓN PREVENTIVA</p><h1>${compare ? 'Comparar evaluaciones' : 'Resultados de simulaciones'}</h1><p>${compare ? 'Pretest y postest con participantes comunes y condiciones verificables.' : 'Consulta las interacciones y los resultados personales de cada campaña.'}</p></div></section>
<div id="results-error" class="page-notice hidden" role="alert"></div>
<section class="panel"><form id="results-filters" class="results-filters">
${compare ? '<label>Evaluación inicial<select id="results-inicial" required></select></label><label>Evaluación posterior<select id="results-posterior" required></select></label>' : '<label>Campaña<select id="results-campaign" required></select></label>'}
<label>Sucursal al enviar<select id="results-branch"><option value="">Todas</option></select></label><label>Área al enviar<select id="results-area"><option value="">Todas</option></select></label>
<label>Envíos desde<input id="results-from" type="date"></label><label>Envíos hasta<input id="results-to" type="date"></label>
<button class="btn btn-primary" id="results-submit" type="submit">${compare ? 'Comparar evaluaciones' : 'Consultar resultados'}</button></form>
<p class="results-note">Fechas en hora de Bolivia. Los filtros corresponden al contexto y la fecha del envío, conservados aunque el colaborador cambie de sucursal.</p><div id="results-selection"></div></section>
<p id="results-status" class="results-note" role="status">Consultando campañas…</p><div id="results-data"></div>`;

function query() {
    const p = new URLSearchParams();
    if (el('results-branch').value) p.set('idSucursal', el('results-branch').value);
    if (el('results-area').value) p.set('idArea', el('results-area').value);
    if (el('results-from').value) p.set('desde', el('results-from').value + 'T00:00:00-04:00');
    if (el('results-to').value) p.set('hasta', el('results-to').value + 'T23:59:59.9999999-04:00');
    return p;
}

function error(err) {
    el('results-error').textContent = err.message;
    el('results-error').classList.remove('hidden');
}

function selection() {
    const ids = compare ? [el('results-inicial').value, el('results-posterior').value] : [el('results-campaign').value];
    el('results-selection').innerHTML = ids.map(id => catalog?.campanias.find(c => String(c.idCampania) === id)).filter(Boolean).map(c =>
        `<p class="results-note"><strong>${esc(c.titulo)}</strong> · ${esc(typeLabel(c.tipoEvaluacion))} · ${esc(c.estado)} · ${Number(c.participantes) || 0} participantes registrados<br>
        Inicio: ${esc(date(c.fechaInicioProgramada))} · Fin programado: ${esc(date(c.fechaFinProgramada))} · Cierre: ${esc(date(c.fechaFinalizacion))}<br>
        Tema: ${esc(c.tema || 'Sin tema capturado')} · Dificultad: ${c.dificultad == null ? 'Sin registro' : Number(c.dificultad)}</p>`).join('');
}

function peopleRows(data) {
    return table(
        ['Participante', 'Contexto al enviar', 'Dificultad NIST', 'Estado', 'Clic / credenciales / reporte', 'Detalle'],
        data.items.map(p => `<tr><td>${esc(p.nombre)}<small>${esc(p.correo || '')}</small></td><td>${esc(p.sucursal || 'Sin registro')} / ${esc(p.area || 'Sin registro')}</td><td>${esc(nistLabel(p.dificultadNist))}</td><td>${esc(deliveryLabel(p.estadoEntrega))}</td><td>${yes(p.hizoClick)} / ${yes(p.ingresoCredenciales)} / ${p.fechaReporte ? 'Sí' : 'No'}</td><td><button type="button" class="text-action" data-individual="${Number(p.idEnvio)}">Ver resultado</button></td></tr>`).join(''))
        + renderPaginador(data, 'people');
}

function eventRows(data) {
    return table(
        ['Fecha', 'Interacción', 'Participante', 'Detalle'],
        data.items.map(e => `<tr><td>${esc(date(e.fecha))}</td><td>${esc(actionLabel(e.tipo))}</td><td>${esc(e.nombre)}<small>${esc(e.correo || '')}</small></td><td><button class="text-action" type="button" data-individual="${Number(e.idEnvio)}">Ver resultado</button></td></tr>`).join(''),
        'No existen interacciones con fecha registrada para estos filtros.')
        + renderPaginador(data, 'events');
}

function renderResults(s, people, events) {
    const r = s.indicadores, t = s.totales;
    const state = s.parcial
        ? 'Resultados parciales: la campaña sigue en ejecución.'
        : s.pendiente
        ? 'Campaña pendiente de ejecución.'
        : 'Resultados conservados de una campaña cerrada.';
    el('results-data').innerHTML = `<p class="results-warning">${state} Consulta: ${esc(date(s.generadoEn))}.</p>
        ${s.sinDatos ? '<p class="results-warning">No hay envíos para los filtros seleccionados.</p>' : s.sinInteracciones ? '<p class="results-warning">Todavía no existen interacciones registradas en los envíos seleccionados.</p>' : ''}
        <div class="results-cards">${card('Participantes', t?.participantes ?? 0, `${t?.envios ?? 0} envíos registrados`)}${card('Clics evaluables', r.clics, percent(r.tasaClic))}${card('Credenciales enviadas', s.paginaSenuelo === 'credenciales' ? r.credenciales : 'No aplica', s.paginaSenuelo === 'credenciales' ? percent(r.tasaCredenciales) : 'Sin página señuelo')}${card('Mensajes reportados', r.reportes, percent(r.tasaReporte))}${card('Aperturas evaluables', r.aperturas, percent(r.tasaApertura))}</div>
        ${panel('Resumen y calidad de datos', `<p>${r.evaluables} envíos evaluables · ${t?.fallidos ?? 0} fallidos · ${t?.pendientes ?? 0} pendientes · ${t?.cancelados ?? 0} cancelados · ${t?.historicos ?? 0} históricos sin confirmación.</p><p>${r.reportesSinClickPrevio} reportes sin clic previo · ${r.capacitaciones} capacitaciones completadas en los envíos evaluables.</p><p>${t?.sinContexto ?? 0} envíos sin contexto completo y ${t?.interaccionesSinFecha ?? 0} envíos con interacciones sin fecha. Los registros incompletos se conservan, pero no se les atribuyen fechas ni ubicaciones supuestas.</p><p class="results-note">${esc(s.nota)}</p>`)}
        ${panel('Tiempos de respuesta y dificultad NIST', `<p>Mediana desde el envío hasta el clic: <strong>${esc(minutes(s.tiempos?.medianaMinutosHastaClic))}</strong> · hasta enviar credenciales: <strong>${esc(minutes(s.tiempos?.medianaMinutosHastaCredenciales))}</strong> · hasta reportar: <strong>${esc(minutes(s.tiempos?.medianaMinutosHastaReporte))}</strong>.</p>
            <p>Reportes por cada clic: <strong>${r.reportesPorClic == null ? 'Sin clics' : Number(r.reportesPorClic).toFixed(2)}</strong>. Un valor mayor que 1 indica que se reporta más de lo que se cae.</p>
            ${table(['Dificultad NIST Phish Scale', 'Envíos evaluables', 'Clic', 'Credenciales', 'Reporte'], (s.porDificultad || []).map(d => `<tr><td>${esc(d.etiqueta)}</td><td>${Number(d.envios)}</td><td>${Number(d.clics)} · ${percent(d.tasaClic)}</td><td>${Number(d.credenciales)} · ${percent(d.tasaCredenciales)}</td><td>${Number(d.reportes)} · ${percent(d.tasaReporte)}</td></tr>`).join(''), 'Sin envíos evaluables.')}
            <p class="results-note">La dificultad se calcula con NIST Phish Scale (señales de alerta × alineación con el área) y se captura al iniciar la campaña. Una tasa de clic alta en un escenario muy difícil no equivale a la misma tasa en uno poco difícil.</p>`)}
        ${panel('Resultados por participante', `<div id="results-people">${peopleRows(people)}</div>`)}
        ${panel('Interacciones registradas', `<label>Tipo de interacción <select id="results-event-type"><option value="">Todas</option>${['apertura', 'clic', 'credenciales', 'reporte', 'capacitacion'].map(t => `<option value="${t}" ${eventType === t ? 'selected' : ''}>${esc(actionLabel(t))}</option>`).join('')}</select></label><div id="results-events">${eventRows(events)}</div>`)}
        <a class="text-action" href="comparacion-evaluaciones.html">Comparar evaluación inicial y posterior</a>`;
}

async function loadRows(kind) {
    const request = ++version, params = query(), id = Number(el('results-campaign').value);
    params.set('pagina', kind === 'people' ? peoplePage : eventsPage);
    if (kind === 'events' && eventType) params.set('tipo', eventType);
    const containerId = kind === 'people' ? 'results-people' : 'results-events';
    el(containerId).textContent = 'Consultando registros…';
    try {
        const data = await apiCallAuth(`${base}/${id}/${kind === 'people' ? 'participantes' : 'interacciones'}?${params}`);
        if (request !== version) return;

        el(containerId).innerHTML = kind === 'people' ? peopleRows(data) : eventRows(data);
    } catch (e) {
        if (request === version) {
            error(e);
            el(containerId).textContent = 'No se pudieron cargar los registros. Pulsa Consultar resultados para volver a intentar.';
        }
    }
}

function exclusions(title, list) {
    return `<details><summary>${esc(title)} (${list.length})</summary>${table(['Participante', 'Motivo'], list.map(e => `<tr><td>${esc(e.nombre)}<small>${esc(e.correo || '')}</small></td><td>${esc(e.motivo)}</td></tr>`).join(''), 'No hay exclusiones de este tipo.')}</details>`;
}

function renderComparison(c) {
    const notices = [...c.limitaciones, ...c.advertencias].map(w => `<li>${esc(w)}</li>`).join('');
    let content = `<p class="results-warning">${c.comparable ? 'Comparación descriptiva de participantes comunes.' : 'No se puede presentar una comparación equivalente con estos datos.'}</p>${notices ? `<ul class="results-warning">${notices}</ul>` : ''}<p>${c.participantesComunes} participantes con registros válidos en ambas evaluaciones.</p>`;
    if (c.comparable) {
        const a = c.indicadoresIniciales, b = c.indicadoresPosteriores, v = c.variaciones;
        const metrics = [
            ['Clics', a.tasaClic, b.tasaClic, v.clicsPuntosPorcentuales],
            ['Credenciales', a.tasaCredenciales, b.tasaCredenciales, v.credencialesPuntosPorcentuales],
            ['Reportes', a.tasaReporte, b.tasaReporte, v.reportesPuntosPorcentuales],
            ['Aperturas', a.tasaApertura, b.tasaApertura, v.aperturasPuntosPorcentuales]
        ];
        content += panel(
            'Indicadores del grupo pareado',
            table(
                ['Indicador', 'Inicial', 'Posterior', 'Variación posterior − inicial'],
                metrics.map(m => `<tr><td>${m[0]}</td><td>${percent(m[1])}</td><td>${percent(m[2])}</td><td>${m[3] == null ? 'Sin datos' : `${Number(m[3]).toFixed(2)} puntos porcentuales`}</td></tr>`).join(''))
                + `<p class="results-note">Ambas tasas utilizan las mismas ${a.participantes} personas. Se excluyen personas evaluadas en un solo grupo y envíos sin trazabilidad suficiente.</p>`);
        if (c.pruebas?.length)
            content += panel('Prueba estadística (McNemar exacto)', table(['Indicador', 'En ambas', 'Solo inicial', 'Solo posterior', 'En ninguna', 'p', 'Interpretación'],
                c.pruebas.map(t => `<tr><td>${esc(t.indicador)}</td><td>${Number(t.ambos)}</td><td>${Number(t.soloInicial)}</td><td>${Number(t.soloPosterior)}</td><td>${Number(t.ninguno)}</td><td>${Number(t.p).toFixed(4)}</td><td>${t.significativo ? '<strong>' : ''}${esc(t.interpretacion)}${t.significativo ? '</strong>' : ''}</td></tr>`).join(''))
                + `<p class="results-note">${esc(c.metodoPrueba)}. Solo cuentan las personas que cambiaron de resultado («solo inicial» frente a «solo posterior»). Con pocos cambios la prueba no puede detectar diferencias aunque existan: un resultado no significativo no demuestra que la capacitación no funcione.</p>`);
        content += panel(
            'Variación de las tasas',
            `<div class="results-chart" role="img" aria-label="Comparación de tasas iniciales y posteriores; valores exactos disponibles en la tabla anterior">${metrics.map(m => `<div class="results-chart-row"><strong>${m[0]}</strong><div><small>Inicial: ${percent(m[1])}</small><div class="results-bar" style="width:${Math.max(0, Math.min(100, Number(m[1]) || 0))}%"></div><small>Posterior: ${percent(m[2])}</small><div class="results-bar posterior" style="width:${Math.max(0, Math.min(100, Number(m[2]) || 0))}%"></div></div></div>`).join('')}</div>`);
        content += panel(
            'Participantes comparados',
            table(
                ['Participante', 'Clic inicial / posterior', 'Reporte inicial / posterior'],
                c.participantes.map(p => `<tr><td>${esc(p.nombre)}<small>${esc(p.correo || '')}</small></td><td>${yes(p.inicial.hizoClick)} / ${yes(p.posterior.hizoClick)}</td><td>${p.inicial.fechaReporte ? 'Sí' : 'No'} / ${p.posterior.fechaReporte ? 'Sí' : 'No'}</td></tr>`).join('')));
    }

    content += panel(
        'Exclusiones del análisis pareado',
        exclusions('Solo evaluación inicial válida', c.excluidosInicial)
            + exclusions('Solo evaluación posterior válida', c.excluidosPosterior)
            + exclusions('Registros incompletos o no aceptados', c.registrosExcluidos));
    el('results-data').innerHTML = content + `<p class="results-note">${esc(c.nota)}</p>`;
}

async function load() {
    const request = ++version;
    el('results-error').classList.add('hidden');
    el('results-data').replaceChildren();
    el('results-submit').disabled = true;
    el('results-status').textContent = 'Consultando resultados…';
    try {
        if (compare) {
            const p = query();
            p.set('inicial', el('results-inicial').value);
            p.set('posterior', el('results-posterior').value);
            const result = await apiCallAuth(`${base}/comparacion?${p}`);
            if (request !== version) return;

            renderComparison(result);
        } else {
            peoplePage = eventsPage = 1;
            const id = Number(el('results-campaign').value), p = query(), eventParams = query();
            if (eventType) eventParams.set('tipo', eventType);
            const [s, people, events] = await Promise.all([
                apiCallAuth(`${base}/${id}/resumen?${p}`),
                apiCallAuth(`${base}/${id}/participantes?${p}`),
                apiCallAuth(`${base}/${id}/interacciones?${eventParams}`)
            ]);
            if (request !== version) return;

            renderResults(s, people, events);
        }

        el('results-status').textContent = 'Consulta terminada. Los resultados originales no se modificaron.';
    } catch (e) {
        if (request === version) {
            error(e);
            el('results-status').textContent = 'No se pudo completar la consulta.';
        }
    }
    finally { if (request === version) el('results-submit').disabled = false; }
}

el('results-filters').addEventListener('submit', e => {
    e.preventDefault();
    load();
});

enlazarPaginador(el('results-data'), 'people', pagina => {
    peoplePage = pagina;
    loadRows('people');
});

enlazarPaginador(el('results-data'), 'events', pagina => {
    eventsPage = pagina;
    loadRows('events');
});

el('results-filters').addEventListener('change', () => {
    version++;
    el('results-submit').disabled = false;
    el('results-data').replaceChildren();
    el('results-status').textContent = 'Filtros modificados. Confirma la consulta para actualizar los resultados.';
    selection();
});

document.addEventListener('change', event => {
    if (event.target.id === 'results-event-type') {
        eventType = event.target.value;
        eventsPage = 1;
        loadRows('events');
    }
});

document.addEventListener('click', async event => {
    const target = event.target.closest('[data-individual]');
    if (!target) return;

    const request = ++detailVersion, dialog = el('resultado-individual');
    el('individual-contenido').textContent = 'Consultando participante…';
    dialog.showModal();
    try {
        const p = await apiCallAuth(`${base}/${Number(el('results-campaign').value)}/participantes/${Number(target.dataset.individual)}`);
        if (request !== detailVersion || !dialog.open) return;

        el('individual-contenido').innerHTML = `<h3>${esc(p.nombre)}</h3><p>${esc(p.correo || '')}</p><p>${esc(p.sucursal || 'Sin contexto registrado')} / ${esc(p.area || 'Sin contexto registrado')}</p><p>${esc(deliveryLabel(p.estadoEntrega))} · ${esc(date(p.fechaEnvio))}</p><p>Dificultad NIST capturada: ${esc(nistLabel(p.dificultadNist))}${p.alineacion ? ` · alineación ${esc(p.alineacion)}` : ''}</p>${table(['Hecho observado', 'Resultado', 'Fecha registrada'], [
            ['Apertura', yes(p.fueAbierto), p.fechaApertura], ['Clic', yes(p.hizoClick), p.fechaVulneracion], ['Credenciales en página señuelo', yes(p.ingresoCredenciales), p.fechaIngresoCredenciales], ['Reporte', p.fechaReporte ? `Sí${p.reporteSinClickPrevio ? ' · sin clic previo' : ' · después de un clic'}` : 'No', p.fechaReporte], ['Capacitación completada', yes(p.capacitacionCompleta), p.fechaCapacitacion]
        ].map(r => `<tr><td>${r[0]}</td><td>${esc(r[1])}</td><td>${esc(date(r[2]))}</td></tr>`).join(''))}<p class="results-note">Los hechos corresponden a esta campaña. Las fechas faltantes no se reconstruyen; los resultados se conservan en modo de solo lectura.</p>`;
    } catch (e) { if (request === detailVersion) el('individual-contenido').textContent = e.message; }
});

el('resultado-individual')?.addEventListener('close', () => { detailVersion++; });
try {
    catalog = await apiCallAuth(base + '/catalogos');
    const options = list => '<option value="">Selecciona una campaña</option>'
        + list.map(c => `<option value="${Number(c.idCampania)}">${esc(c.titulo)} · ${esc(typeLabel(c.tipoEvaluacion))} · ${esc(c.estado)}</option>`).join('');
    if (compare) {
        el('results-inicial').innerHTML = options(catalog.campanias.filter(c => c.tipoEvaluacion === 'inicial'
            || c.tipoEvaluacion === 'sin_clasificar'));
        el('results-posterior').innerHTML = options(catalog.campanias.filter(c => c.tipoEvaluacion === 'posterior'
            || c.tipoEvaluacion === 'sin_clasificar'));
        el('results-status').textContent = 'Selecciona las dos evaluaciones y revisa sus periodos antes de comparar.';
    } else {
        el('results-campaign').innerHTML = options(catalog.campanias);
        const chosen = new URLSearchParams(location.search).get('campania');
        el('results-campaign').value = chosen || catalog.campanias[0]?.idCampania || '';
        el('results-status').textContent = catalog.campanias.length
            ? 'Selecciona una campaña para consultar.'
            : 'No hay campañas registradas.';
    }

    for (const [id, rows] of [['results-branch', catalog.sucursales], ['results-area', catalog.areas]])
        el(id).innerHTML = '<option value="">Todas</option>'
            + rows.map(r => `<option value="${Number(r.id)}">${esc(r.nombre)}${r.activo === false ? ' (inactiva)' : ''}</option>`).join('');
    selection();
    if (!compare && el('results-campaign').value) await load();
} catch (e) {
    error(e);
    el('results-status').textContent = 'No se pudieron cargar las campañas. Recarga para volver a intentar.';
}
