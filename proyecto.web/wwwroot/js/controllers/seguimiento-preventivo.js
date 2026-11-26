import { apiCallAuth } from '../services/api.js';
import { cls } from '../features/cyberscore/areas.js';
import { TokenStore } from '../core/session.js';
import { el, escapeHtml as esc } from '../utils/ui.js';
// prioridad por nivel CyberScore del area, la tasa de clics queda como respaldo sin evaluacion suficiente
const PRIORIDAD_CS = { 'Crítico': 'Alta', Alto: 'Alta', Medio: 'Media', Bajo: 'Baja' };

function cyberCell(a) {
    const c = a.cyberScore;
    if (!c || c.oculto) return '<td>—</td>';

    if (c.promedio == null)
        return `<td><span class="cs-level is-none">Sin evaluación</span><small>${c.colaboradores} personas</small></td>`;

    return `<td><span class="cs-level ${cls(c.nivel)}">${esc(c.nivel)} · ${Number(c.promedio).toFixed(0)}</span><small>${Number(c.porcentajeAltoCritico ?? 0).toFixed(0)}% en Alto o Crítico${c.planesPendientes ? ` · ${c.planesPendientes} refuerzos pendientes` : ''}</small></td>`;
}

function prioridad(a) {
    const c = a.cyberScore, porCs = c && !c.oculto && c.promedio != null ? PRIORIDAD_CS[c.nivel] : null;
    // muchas personas en Alto/Critico elevan la prioridad aunque el promedio sea aceptable
    const final = porCs && porCs !== 'Alta' && (c.porcentajeAltoCritico ?? 0) >= 30 ? 'Alta' : porCs;
    return final
        ? `<strong>${esc(final)}</strong><small>Por CyberScore · tasa de clics: ${esc(a.evaluacion.prioridad)}</small>`
        : `<strong>${esc(a.evaluacion.prioridad)}</strong><small>Por tasa de clics</small>`;
}

let version = 0;
const routeParams = new URLSearchParams(location.search);
let initialArea = routeParams.get('idArea');
const percent = (n, d) => d ? `${(100 * n / d).toFixed(1)} %` : 'Sin datos';
const empty = (columns, message) => `<tr><td colspan="${columns}" class="table-state">${esc(message)}</td></tr>`;

async function load() {
    const request = ++version;
    el('preventivo-error').classList.add('hidden');
    el('preventivo-resultados').hidden = true;
    el('preventivo-detalle').hidden = true;
    el('preventivo-status').textContent = 'Consultando resultados…';
    el('preventivo-consultar').disabled = true;
    try {
        const from = el('preventivo-desde').value, to = el('preventivo-hasta').value;
        if (from && to && from > to) throw new Error('La fecha inicial debe ser anterior a la final.');

        const query = new URLSearchParams();
        if (from) query.set('desde', new Date(`${from}T00:00:00`).toISOString());
        if (to) query.set('hasta', new Date(`${to}T23:59:59.999`).toISOString());
        if (el('preventivo-area').value) query.set('idArea', el('preventivo-area').value);
        if (TokenStore.esAdmin() && el('preventivo-sucursal').value) query.set('idSucursal', el('preventivo-sucursal').value);
        const data = await apiCallAuth(`/seguimiento-preventivo?${query}`);
        if (request !== version) return;

        // confidencialidad: el servidor oculta al gerente los grupos menores al umbral
        const k = data.umbralAgregacion, oculto = 'Oculto';
        data.campanias = data.campanias.map(c => c.suprimido ? { ...c, participantes: `< ${k}`, envios: oculto, clics: oculto } : c);
        data.actividades = data.actividades.map(a => a.suprimido ? {
            ...a,
            participantes: `< ${k}`,
            intentos: oculto,
            aprobados: oculto,
            noAprobados: oculto,
            pendientes: oculto
        } : a);
        data.indicadoresArea = (data.indicadoresArea || []).map(a => a.suprimido ? {
            ...a,
            participantes: `< ${k}`,
            envios: oculto,
            clics: oculto,
            tasaClic: null,
            reincidentes: oculto,
            capacitacionesPendientes: oculto
        } : a);
        el('branch-name').textContent = `${data.sucursal.nombre} · Resultados consolidados de la sucursal`;
        const area = el('preventivo-area').value;
        el('preventivo-area').innerHTML = '<option value="">Todas las áreas de la sucursal</option>'
            + data.areas.map(a => `<option value="${a.idArea}">${esc(a.nombre)}</option>`).join('');
        el('preventivo-area').value = area;
        if (initialArea && data.areas.some(a => String(a.idArea) === initialArea)) {
            el('preventivo-area').value = initialArea;
            initialArea = null;
            await load();
            return;
        }

        initialArea = null;
        const r = data.resumen.suprimido ? {
            ...data.resumen,
            participantesCampanias: `< ${k}`,
            clics: oculto,
            envios: 0,
            reportes: 0,
            reportesSinClickPrevio: 0,
            participantesCursos: oculto, aprobados: oculto, intentos: oculto
        } : data.resumen;
        el('preventivo-participantes').textContent = r.participantesCampanias;
        el('preventivo-campanias').textContent = r.campanias;
        el('preventivo-clics').textContent = r.clics;
        el('preventivo-reportes').textContent = `${r.reportes ?? 0} mensajes reportados (${percent(r.reportes ?? 0, r.envios)} de los envíos aceptados); ${r.reportesSinClickPrevio ?? 0} reportados sin clic previo.`;
        el('preventivo-tasa').textContent = `${percent(r.clics, r.envios)} de ${r.envios} envíos aceptados por el proveedor`;
        el('preventivo-cursos').textContent = r.participantesCursos;
        if (data.resumen.suprimido) {
            const aviso = `Oculto para proteger la confidencialidad: el periodo tiene menos de ${k} participantes.`;
            el('preventivo-reportes').textContent = aviso;
            el('preventivo-tasa').textContent = aviso;
        }

        el('preventivo-aprobados').textContent = `${r.aprobados} intentos aprobados de ${r.intentos}`;
        el('preventivo-campanias-body').innerHTML = data.campanias.length
            ? data.campanias.map(c => `<tr><td>${esc(c.titulo)}</td><td>${c.fecha ? esc(new Date(c.fecha).toLocaleDateString('es-BO')) : 'Sin fecha'}</td><td>${c.participantes}</td><td>${c.envios}</td><td>${c.clics}</td><td><button class="text-action" type="button" data-campaign="${c.idCampania}">Ver indicadores</button></td></tr>`).join('')
            : empty(6, 'No existen campañas con resultados para esta sucursal y estos filtros.');
        el('preventivo-campanias-body').querySelectorAll('[data-campaign]').forEach(button => button.addEventListener('click', () => {
            const c = data.campanias.find(c => c.idCampania === Number(button.dataset.campaign));
            if (c.suprimido) {
                el('preventivo-detalle').innerHTML = `<h3>${esc(c.titulo)}</h3><p>Esta campaña tuvo menos de ${k} participantes en tu sucursal. Sus indicadores se ocultan para que no se puedan identificar personas.</p>`;
                el('preventivo-detalle').hidden = false;
                return;
            }

            el('preventivo-detalle').innerHTML = `<h3>${esc(c.titulo)}</h3><p>${c.participantes} participantes · ${c.aperturas} aperturas registradas · ${c.clics} clics (${percent(c.clics, c.envios)}) · ${c.reportes ?? 0} reportes (${percent(c.reportes ?? 0, c.envios)}), ${c.reportesSinClickPrevio ?? 0} sin clic previo · ${c.capacitacionesCompletadas} capacitaciones completadas.</p>`;
            el('preventivo-detalle').hidden = false;
        }));

        el('preventivo-actividades-body').innerHTML = data.actividades.length
            ? data.actividades.map(a => `<tr><td>${esc(a.tema)}</td><td>${a.participantes}</td><td>${a.intentos}</td><td>${a.aprobados}</td><td>${a.noAprobados}</td><td>${a.pendientes}</td></tr>`).join('')
            : empty(6, 'No existen actividades de concientización para los filtros seleccionados.');
        const reinforcement = [
            ...data.temasRefuerzo.map(t => `<li><strong>${esc(t.tema)}</strong>: ${t.noAprobados} intentos no aprobados de ${t.intentos} intentos.</li>`),
            ...data.refuerzoPhishing.map(t => `<li><strong>${esc(t.tema)}</strong>: ${t.clics} clics en simulaciones; ${t.pendientes} capacitaciones pendientes.</li>`),
        ];
        el('preventivo-refuerzo').innerHTML = reinforcement.length
            ? `<ul>${reinforcement.join('')}</ul>`
            : '<p class="empty-state">No hay dificultades registradas en los datos consultados.</p>';
        el('preventivo-areas-body').innerHTML = (data.indicadoresArea || [])
            .map(a => `<tr><td>${esc(a.nombre)}</td><td>${esc(a.criticidadNegocio)}</td><td>${a.participantes} / ${a.envios}</td><td>${a.clics}</td><td>${a.tasaClic == null ? 'Sin datos' : `${Number(a.tasaClic).toFixed(1)} %`}</td><td>${a.reincidentes}</td><td>${a.capacitacionesPendientes}</td><td>${esc(a.evaluacion.estadoDatos)}</td>${cyberCell(a)}<td>${prioridad(a)}</td></tr>`)
            .join('')
            || empty(10, 'No hay áreas vinculadas a esta sucursal.');
        el('preventivo-acciones').innerHTML = (data.indicadoresArea || [])
            .map(a => `<details><summary>${esc(a.nombre)} · Medidas y evolución</summary><ul>${a.evaluacion.recomendaciones.map(text => `<li>${esc(text)}</li>`).join('')}</ul>${(a.evolucion || []).map(t => `<p class="panel-caption">${esc(t.anterior)} → ${esc(t.actual)}: ${Number(t.tasaAnterior).toFixed(1)} % → ${Number(t.tasaActual).toFixed(1)} % (${Number(t.diferenciaPuntos).toFixed(1)} puntos porcentuales). ${t.muestraSuficiente ? 'Comparación descriptiva del mismo tema y dificultad.' : 'Muestra limitada; no permite concluir una mejora.'}</p>`).join('')}${TokenStore.esAdmin() ? `<a class="text-action" href="campanias-phishing.html?refuerzo=1&idSucursal=${data.sucursal.idSucursal}&idArea=${a.idArea}">Preparar simulación de seguimiento</a>` : ''}</details>`)
            .join('');
        const q = data.calidadDatos;
        el('preventivo-calidad').textContent = q
            ? `${q.enviosSinContexto} envíos y ${q.cursosSinContexto} cursos antiguos o incompletos asociados actualmente a esta sucursal no tienen contexto histórico y no se incluyen. ${q.enviosSinConfirmar} envíos del alcance consultado están pendientes o sin confirmación del proveedor.`
            : '';
        el('preventivo-politica').textContent = data.politica
            ? `${data.politica.descripcion} Política ${data.politica.version}; mínimo ${data.politica.minimoEnvios} envíos y ${data.politica.minimoParticipantes} personas distintas, con actividad en los últimos ${data.politica.vigenciaDias} días.`
            : '';
        el('preventivo-nota').textContent = data.nota;
        el('preventivo-status').textContent = !r.envios && !r.intentos
            ? 'No existen resultados disponibles para los filtros seleccionados.'
            : `Consulta actualizada a las ${new Date().toLocaleTimeString('es-BO')}.`;
        el('preventivo-resultados').hidden = false;
        const campaignButton = el('preventivo-campanias-body').querySelector(`[data-campaign="${Number(routeParams.get('campania'))}"]`);
        if (campaignButton) campaignButton.click();
    } catch (error) {
        if (request !== version) return;

        el('preventivo-error').textContent = error.message;
        el('preventivo-error').classList.remove('hidden');
        el('preventivo-status').textContent = 'No se pudieron consultar los resultados.';
    } finally { if (request === version) el('preventivo-consultar').disabled = false; }
}

el('preventivo-filtros').addEventListener('submit', event => {
    event.preventDefault();
    load();
});

el('preventivo-limpiar').addEventListener('click', () => {
    ['preventivo-desde', 'preventivo-hasta', 'preventivo-area'].forEach(id => { el(id).value = ''; });
    load();
});

el('preventivo-sucursal').addEventListener('change', () => {
    el('preventivo-area').value = '';
    load();
});

if (TokenStore.esAdmin()) {
    el('branch-filter').hidden = false;
    el('preventivo-nueva-campania').hidden = false;
    try {
        const branches = await apiCallAuth('/sucursales?activo=true');
        el('preventivo-sucursal').innerHTML = branches.map(b => `<option value="${b.idSucursal}">${esc(b.nombre)}</option>`).join('');
        if (branches.some(b => String(b.idSucursal) === routeParams.get('idSucursal')))
            el('preventivo-sucursal').value = routeParams.get('idSucursal');
    } catch (error) {
        el('preventivo-error').textContent = error.message;
        el('preventivo-error').classList.remove('hidden');
    }
}

await load();
