import { apiCallAuth } from '../services/api.js';
import { el, escapeHtml as esc, toast, conCarga } from '../utils/ui.js';
import { renderPaginador, enlazarPaginador, conPagina } from '../components/paginacion.js';

const api = '/programa';
const meses = [
    'Enero',
    'Febrero',
    'Marzo',
    'Abril',
    'Mayo',
    'Junio',
    'Julio',
    'Agosto',
    'Septiembre',
    'Octubre',
    'Noviembre',
    'Diciembre'];
const tipos = {
    campania: 'Campaña',
    capacitacion: 'Capacitación',
    encuesta: 'Encuesta',
    comunicacion: 'Comunicación',
    temporada: 'Temporada de riesgo'
};
const estadosRuta = { al_dia: 'Al día', pendiente: 'Pendiente', por_vencer: 'Por vencer', vencido: 'Vencido' };
const pct = v => v == null ? 'Sin datos' : `${Number(v).toFixed(1)} %`;
const num = v => v == null ? 'Sin datos' : String(v);
const day = v => v ? new Date(v).toLocaleDateString('es-BO', { timeZone: 'America/La_Paz' }) : '—';
const card = (label, value, caption = '') =>
    `<article class="results-card"><span>${esc(label)}</span><strong>${esc(value)}</strong>${caption ? `<small>${esc(caption)}</small>` : ''}</article>`;
const isoDia = (value, fin = false) => value
    ? new Date(`${value}T${fin ? '23:59:59' : '00:00:00'}-04:00`).toISOString()
    : null;
let catalog = { areas: [], temas: [], sucursales: [], actividades: [] }, rutas = [], plan = null, encuestas = [];

function error(message) {
    el('programa-error').textContent = message;
    el('programa-error').classList.remove('hidden');
}

const loaded = new Set();
const paginas = { rutas: 1, encuestas: 1, atrasados: 1 };

// pestanas
document.querySelector('.prog-tabs').addEventListener('click', async event => {
    const tab = event.target.closest('[data-tab]');
    if (!tab) return;

    document.querySelectorAll('.prog-tab').forEach(t => {
        const on = t === tab;
        t.classList.toggle('is-active', on);
        t.setAttribute('aria-selected', on);
    });
    document.querySelectorAll('.prog-panel').forEach(p => {
        const on = p.id === `tab-${tab.dataset.tab}`;
        p.hidden = !on;
        p.classList.toggle('is-active', on);
    });
    await abrir(tab.dataset.tab);
});

async function abrir(nombre) {
    try {
        if (nombre === 'panel') await cargarPanel();
        if (nombre === 'rutas') await Promise.all([cargarCumplimiento(), cargarRutas(), cargarEstadoRecordatorios()]);
        if (nombre === 'plan') await cargarPlan();
        if (nombre === 'encuestas') await cargarEncuestas();
        if (nombre === 'matriz') await cargarMatriz();
        loaded.add(nombre);
    } catch (e) { error(e.message); }
}

// panel Kirkpatrick
async function cargarPanel() {
    const q = new URLSearchParams();
    if (el('panel-desde').value) q.set('desde', isoDia(el('panel-desde').value));
    if (el('panel-hasta').value) q.set('hasta', isoDia(el('panel-hasta').value, true));

    const p = await apiCallAuth(`${api}/panel?${q}`);
    const n1 = p.nivel1, n2 = p.nivel2, n3 = p.nivel3, n4 = p.nivel4;
    el('panel-niveles').innerHTML = `
        <article class="prog-level"><p class="eyebrow">NIVEL 1 · REACCIÓN</p>${n1 ? `${card('Encuesta de cultura', n1.oculto ? 'Oculto' : `${num(n1.promedio)} / 5`, `${n1.titulo} · ${n1.respuestas} respuestas`)}${card('Respuestas favorables', n1.oculto ? 'Oculto' : pct(n1.favorable), 'Valores 4 y 5')}` : '<p class="empty-state">Aún no se aplicó una encuesta de cultura.</p>'}</article>
        <article class="prog-level"><p class="eyebrow">NIVEL 2 · APRENDIZAJE</p>${card('Actividades aprobadas', pct(n2.tasaAprobacion), `${n2.aprobadas} de ${n2.actividadesFinalizadas} finalizadas`)}${card('Cumplimiento de rutas', pct(n2.cumplimientoRutas), `${n2.requisitosVencidos} requisitos vencidos`)}${card('Constancias emitidas', n2.constancias)}</article>
        <article class="prog-level"><p class="eyebrow">NIVEL 3 · COMPORTAMIENTO</p>${card('Tasa de clic', pct(n3.tasaClic), `${n3.envios} envíos evaluables`)}${card('Tasa de credenciales', pct(n3.tasaCredenciales))}${card('Tasa de reporte', pct(n3.tasaReporte), n3.reportesPorClic == null ? '' : `${Number(n3.reportesPorClic).toFixed(2)} reportes por clic`)}</article>
        <article class="prog-level"><p class="eyebrow">NIVEL 4 · RESULTADOS</p>${card('Incidentes reales reportados', n4.incidentesReportados)}${card('Maliciosos confirmados', n4.maliciososConfirmados)}${card('Mediana hasta la resolución', n4.medianaHorasResolucion == null ? 'Sin datos' : `${n4.medianaHorasResolucion} h`)}${card('Anomalías HTTP confirmadas', n4.anomaliasHttpConfirmadas)}</article>`;

    const t = n3.tendencia;
    el('panel-tendencia').innerHTML = t.length
        ? `<div class="table-scroll"><table class="event-table"><thead><tr><th>Mes</th><th>Envíos</th><th>Clic</th><th>Credenciales</th><th>Reporte</th></tr></thead><tbody>${t.map(m => `<tr><td>${esc(new Date(m.mes).toLocaleDateString('es-BO', { month: 'long', year: 'numeric', timeZone: 'UTC' }))}</td><td>${m.envios}</td>
        <td><span class="prog-bar" style="--v:${Number(m.tasaClic) || 0}%"></span>${pct(m.tasaClic)}</td><td>${pct(m.tasaCredenciales)}</td><td><span class="prog-bar is-good" style="--v:${Number(m.tasaReporte) || 0}%"></span>${pct(m.tasaReporte)}</td></tr>`).join('')}</tbody></table></div>`
        : '<p class="empty-state">No hay simulaciones en el periodo.</p>';
    el('panel-nota').textContent = p.nota;
}

el('panel-filtros').addEventListener('submit', event => {
    event.preventDefault();
    cargarPanel().catch(e => error(e.message));
});

// rutas y cumplimiento
async function catalogos() {
    if (catalog.actividades.length) return;

    const [areas, temas, sucursales, actividades] = await Promise.all([
        apiCallAuth('/areas-operativas'),
        apiCallAuth('/campanias-phishing/categorias-educativas'),
        apiCallAuth('/sucursales?activo=true'),
        apiCallAuth('/concientizacion-admin/catalogo')]);
    catalog = { areas, temas, sucursales, actividades: actividades.filter(a => a.habilitada) };
}

async function cargarCumplimiento() {
    const c = await apiCallAuth(conPagina(`${api}/cumplimiento`, paginas.atrasados)), atrasados = c.atrasados;
    const abierto = el('cumplimiento-detalle').querySelector('details')?.open;
    el('cumplimiento-resumen').innerHTML = card('Cumplimiento', pct(c.cumplimiento), `${c.personas} personas`)
        + card('Al día', c.alDia)
        + card('Pendientes', c.pendientes)
        + card('Por vencer', c.porVencer)
        + card('Vencidos', c.vencidos)
        + card('Requisitos asignados', c.total);
    el('cumplimiento-detalle').innerHTML = `<div class="prog-columns"><div><h3>Por ruta</h3>${c.porRuta.length ? `<ul>${c.porRuta.map(r => `<li>${esc(r.ruta)}: ${pct(r.cumplimiento)} · ${r.vencidos} vencidos</li>`).join('')}</ul>` : '<p class="empty-state">Sin rutas activas.</p>'}</div>
        <div><h3>Por área</h3>${c.porArea.length ? `<ul>${c.porArea.map(a => `<li>${esc(a.area)}: ${pct(a.cumplimiento)} · ${a.vencidos} vencidos</li>`).join('')}</ul>` : '<p class="empty-state">Sin datos.</p>'}</div></div>
        ${atrasados.total ? `<details ${abierto ? 'open' : ''}><summary>Personas con requisitos vencidos o por vencer (${atrasados.total})</summary><div class="table-scroll"><table class="event-table"><thead><tr><th>Persona</th><th>Ruta / actividad</th><th>Estado</th><th>Vence</th></tr></thead><tbody>${atrasados.items.map(a => `<tr><td>${esc(a.nombre || '')}<small class="t-meta"> ${esc(a.sucursal || '')} / ${esc(a.area || '')}</small></td><td>${esc(a.ruta)} · ${esc(a.actividad)}</td><td>${esc(estadosRuta[a.estado])}</td><td>${esc(day(a.vence))}</td></tr>`).join('')}</tbody></table></div>${renderPaginador(atrasados, 'atrasados')}</details>` : ''}`;
}

enlazarPaginador(el('cumplimiento-detalle'), 'atrasados', p => {
    paginas.atrasados = p;
    cargarCumplimiento().catch(e => error(e.message));
});

async function cargarEstadoRecordatorios() {
    try {
        const r = await apiCallAuth(`${api}/recordatorios/estado`);
        let ultimo = '';
        if (r.ultimaEjecucion) {
            let detalle = null;
            try {
                detalle = JSON.parse(r.ultimoResultado);
            } catch { /* sin detalle */ }
            ultimo = ` Último envío automático: ${new Date(r.ultimaEjecucion).toLocaleString('es-BO', { timeZone: 'America/La_Paz' })}${detalle ? ` (${detalle.enviados} enviado(s))` : ''}.`;
        }

        el('recordatorios-estado').textContent = r.activo
            ? `Envío automático activo: ${r.soloDiasHabiles ? 'de lunes a viernes' : 'todos los días'} desde las ${String(r.horaLocal).padStart(2, '0')}:00 (hora de Bolivia), una vez al día.${ultimo || ' Aún no se ejecutó.'}`
            : `Envío automático desactivado en la configuración del servidor (Programa:Recordatorios).${ultimo}`;
    } catch { el('recordatorios-estado').textContent = ''; }
}

async function cargarRutas() {
    const data = await apiCallAuth(conPagina(`${api}/rutas`, paginas.rutas));
    rutas = data.items;
    el('rutas-lista').innerHTML = rutas.length
        ? rutas.map(r => `<article class="history-item"><div><strong>${esc(r.nombre)}</strong><small>${esc(r.area || 'Todas las áreas')} · ${r.activa ? 'Activa' : 'Inactiva'}</small>
        <ul>${r.requisitos.map(q => `<li>${esc(q.actividad)} · ${q.esInduccion ? 'inducción, ' : ''}plazo ${q.plazoDias} días · ${q.frecuenciaDias ? `refresco cada ${q.frecuenciaDias} días` : 'una sola vez'}${q.habilitada ? '' : ' · <em>actividad deshabilitada</em>'}</li>`).join('')}</ul></div>
        <button class="text-action" type="button" data-editar-ruta="${r.id}">Editar</button></article>`).join('') + renderPaginador(data, 'rutas')
        : '<p class="empty-state">Aún no hay rutas. Crea una para exigir inducción y refrescos por área.</p>';
}

enlazarPaginador(el('rutas-lista'), 'rutas', p => {
    paginas.rutas = p;
    cargarRutas().catch(e => error(e.message));
});

function filaRequisito(q = {}) {
    const row = document.createElement('div');
    row.className = 'prog-requirement';
    row.innerHTML = `<select class="js-req-actividad" aria-label="Actividad">${catalog.actividades.map(a => `<option value="${a.id}">${esc(a.titulo)} · ${esc(a.tema)}</option>`).join('')}</select>
        <label class="quiz-option"><input type="checkbox" class="js-req-induccion">Inducción</label>
        <label>Plazo (días)<input type="number" class="js-req-plazo" min="1" max="365" value="${q.plazoDias ?? 30}" required></label>
        <label>Refresco<select class="js-req-frecuencia"><option value="0">Una sola vez</option><option value="90">Cada 3 meses</option><option value="180">Cada 6 meses</option><option value="365">Anual</option></select></label>
        <button type="button" class="btn-icon is-danger js-req-quitar" aria-label="Quitar actividad">×</button>`;
    if (q.idActividad) row.querySelector('.js-req-actividad').value = q.idActividad;
    row.querySelector('.js-req-induccion').checked = !!q.esInduccion;

    const f = row.querySelector('.js-req-frecuencia');
    if (q.frecuenciaDias && ![...f.options].some(o => Number(o.value) === q.frecuenciaDias))
        f.add(new Option(`Cada ${q.frecuenciaDias} días`, q.frecuenciaDias));
    f.value = String(q.frecuenciaDias ?? 0);

    return row;
}

async function abrirRuta(r) {
    await catalogos();
    if (!catalog.actividades.length) {
        error('Habilita al menos una actividad en Concientización y gamificación antes de crear una ruta.');
        return;
    }

    el('ruta-error').textContent = '';
    el('ruta-id').value = r?.id ?? '';
    el('ruta-nombre').value = r?.nombre ?? '';
    el('ruta-descripcion').value = r?.descripcion ?? '';
    el('ruta-activa').checked = r?.activa ?? true;
    el('ruta-area').innerHTML = '<option value="">Todas las áreas</option>'
        + catalog.areas.map(a => `<option value="${a.idArea}">${esc(a.nombre)}</option>`).join('');
    el('ruta-area').value = r?.idArea ?? '';
    el('ruta-requisitos').replaceChildren(...(r?.requisitos?.length ? r.requisitos : [{}]).map(filaRequisito));
    el('dialog-ruta').showModal();
}

el('btn-nueva-ruta').addEventListener('click', () => abrirRuta(null));
el('rutas-lista').addEventListener('click', e => {
    const b = e.target.closest('[data-editar-ruta]');
    if (b) abrirRuta(rutas.find(r => r.id === Number(b.dataset.editarRuta)));
});
el('ruta-agregar').addEventListener('click', () => el('ruta-requisitos').append(filaRequisito()));
el('ruta-requisitos').addEventListener('click', e => {
    const b = e.target.closest('.js-req-quitar');
    if (b) b.closest('.prog-requirement').remove();
});

el('form-ruta').addEventListener('submit', async event => {
    event.preventDefault();
    const button = event.submitter;
    button.disabled = true;
    el('ruta-error').textContent = '';

    const id = el('ruta-id').value;
    const body = {
        nombre: el('ruta-nombre').value.trim(),
        descripcion: el('ruta-descripcion').value.trim() || null,
        idArea: el('ruta-area').value ? Number(el('ruta-area').value) : null,
        activa: el('ruta-activa').checked,
        requisitos: [...el('ruta-requisitos').children].map(r => ({
            idActividad: Number(r.querySelector('.js-req-actividad').value),
            esInduccion: r.querySelector('.js-req-induccion').checked,
            plazoDias: Number(r.querySelector('.js-req-plazo').value),
            frecuenciaDias: Number(r.querySelector('.js-req-frecuencia').value)
        }))
    };

    try {
        await apiCallAuth(`${api}/rutas${id ? `/${id}` : ''}`, { method: id ? 'PUT' : 'POST', body });
        el('dialog-ruta').close();
        toast('Ruta guardada.');
        await Promise.all([cargarRutas(), cargarCumplimiento()]);
    } catch (e) {
        el('ruta-error').textContent = e.message;
    } finally {
        button.disabled = false;
    }
});

el('btn-recordatorios').addEventListener('click', async event => {
    if (!confirm('¿Enviar recordatorios por correo a quienes tienen requisitos vencidos o por vencer?')) return;

    event.target.disabled = true;
    try {
        const r = await apiCallAuth(`${api}/recordatorios`, { method: 'POST' });
        toast(`${r.enviados} recordatorio(s) enviados${r.omitidosPorRecientes ? `; ${r.omitidosPorRecientes} omitido(s) por haber recibido uno en los últimos 7 días` : ''}.`);
    } catch (e) {
        error(e.message);
    } finally {
        event.target.disabled = false;
    }
});

// planificador
el('plan-anio').value = new Date().getFullYear();

async function cargarPlan() {
    await catalogos();
    plan = await apiCallAuth(`${api}/plan/${Number(el('plan-anio').value)}`);
    const c = plan.cobertura;
    el('plan-cobertura').innerHTML = `${c.planificados} planificados · ${c.realizados} realizados.
        ${c.mesesSinActividad.length ? `Meses sin actividades: <strong>${c.mesesSinActividad.map(m => esc(meses[m - 1])).join(', ')}</strong>.` : 'Todos los meses tienen al menos una actividad.'}
        ${c.temasSinCobertura.length ? ` Temas sin cobertura: ${c.temasSinCobertura.map(esc).join(', ')}.` : ''}
        ${plan.temporadasDisponibles.length ? ` Hay ${plan.temporadasDisponibles.length} temporadas sugeridas sin cargar.` : ''}`;
    el('btn-temporadas').disabled = !plan.temporadasDisponibles.length;

    el('plan-calendario').innerHTML = meses.map((nombre, i) => {
        const items = plan.eventos.filter(e => e.mes === i + 1);
        return `<article class="prog-month ${c.mesesSinActividad.includes(i + 1) ? 'is-empty' : ''}"><h3>${nombre}</h3>${items.map(e => `<button type="button" class="prog-event is-${e.tipo} is-${e.estado}" data-evento="${e.id}">
            <strong>${esc(e.titulo)}</strong><small>${esc(tipos[e.tipo])}${e.tema ? ` · ${esc(e.tema)}` : ''}${e.sucursal ? ` · ${esc(e.sucursal)}` : ''}${e.estado !== 'planificado' ? ` · ${esc(e.estado)}` : ''}</small></button>`).join('') || '<p class="t-meta">Sin actividades</p>'}</article>`;
    }).join('');
}

el('plan-anio').addEventListener('change', () => cargarPlan().catch(e => error(e.message)));

const cargarTemporadas = conCarga(el('btn-temporadas'), async () => {
    try {
        const r = await apiCallAuth(`${api}/plan/${Number(el('plan-anio').value)}/temporadas`, { method: 'POST' });
        toast(`${r.cargadas} temporadas incorporadas al calendario.`);
        await cargarPlan();
    } catch (e) { error(e.message); }
});

el('btn-temporadas').addEventListener('click', async () => {
    await cargarTemporadas();
    // conCarga reactiva el boton, solo sigue activo si quedan temporadas por cargar
    el('btn-temporadas').disabled = !plan?.temporadasDisponibles.length;
});

function abrirEvento(e) {
    el('evento-error').textContent = '';
    el('evento-id').value = e?.id ?? '';
    el('evento-eliminar').hidden = !e;
    el('evento-mes').innerHTML = meses.map((m, i) => `<option value="${i + 1}">${m}</option>`).join('');
    el('evento-mes').value = e?.mes ?? new Date().getMonth() + 1;
    el('evento-tema').innerHTML = '<option value="">Sin tema</option>'
        + catalog.temas.map(t => `<option value="${t.idCategoria}">${esc(t.nombre)}</option>`).join('');
    el('evento-sucursal').innerHTML = '<option value="">Todas las sucursales</option>'
        + catalog.sucursales.map(s => `<option value="${s.idSucursal}">${esc(s.nombre)}</option>`).join('');
    el('evento-tipo').value = e?.tipo ?? 'campania';
    el('evento-titulo-input').value = e?.titulo ?? '';
    el('evento-tema').value = e?.idCategoria ?? '';
    el('evento-sucursal').value = e?.idSucursal ?? '';
    el('evento-estado').value = e?.estado ?? 'planificado';
    el('evento-descripcion').value = e?.descripcion ?? '';
    el('dialog-evento').showModal();
}

el('btn-nuevo-evento').addEventListener('click', () => abrirEvento(null));
el('plan-calendario').addEventListener('click', e => {
    const b = e.target.closest('[data-evento]');
    if (b) abrirEvento(plan.eventos.find(x => x.id === Number(b.dataset.evento)));
});

el('form-evento').addEventListener('submit', async event => {
    event.preventDefault();
    const button = event.submitter;
    button.disabled = true;
    el('evento-error').textContent = '';

    const id = el('evento-id').value, original = plan.eventos.find(x => x.id === Number(id));
    const body = {
        anio: Number(el('plan-anio').value),
        mes: Number(el('evento-mes').value),
        titulo: el('evento-titulo-input').value.trim(),
        tipo: el('evento-tipo').value,
        idCategoria: el('evento-tema').value ? Number(el('evento-tema').value) : null,
        idSucursal: el('evento-sucursal').value ? Number(el('evento-sucursal').value) : null,
        descripcion: el('evento-descripcion').value.trim() || null,
        estado: el('evento-estado').value,
        idCampania: original?.idCampania ?? null
    };

    try {
        await apiCallAuth(`${api}/plan${id ? `/${id}` : ''}`, { method: id ? 'PUT' : 'POST', body });
        el('dialog-evento').close();
        await cargarPlan();
    } catch (e) {
        el('evento-error').textContent = e.message;
    } finally {
        button.disabled = false;
    }
});

el('evento-eliminar').addEventListener('click', async () => {
    if (!confirm('¿Eliminar este evento del plan?')) return;

    await conCarga(el('evento-eliminar'), async () => {
        try {
            await apiCallAuth(`${api}/plan/${Number(el('evento-id').value)}`, { method: 'DELETE' });
            el('dialog-evento').close();
            await cargarPlan();
        } catch (e) { el('evento-error').textContent = e.message; }
    })();
});

// encuestas
const estadosEncuesta = { borrador: 'Borrador', abierta: 'Abierta', cerrada: 'Cerrada' };

async function cargarEncuestas() {
    const data = await apiCallAuth(conPagina(`${api}/encuestas`, paginas.encuestas));
    encuestas = data.items;
    el('encuestas-lista').innerHTML = encuestas.length
        ? `<div class="table-scroll"><table class="event-table"><thead><tr><th>Encuesta</th><th>Periodo</th><th>Vigencia</th><th>Estado</th><th>Respuestas</th><th></th></tr></thead><tbody>${encuestas.map(e => `<tr><td>${esc(e.titulo)}</td><td>${esc(e.periodo)}</td><td>${esc(day(e.fechaInicio))} – ${esc(day(e.fechaFin))}</td><td>${esc(estadosEncuesta[e.estado])}</td><td>${e.respuestas}</td>
        <td class="row-actions">${e.estado === 'borrador' ? `<button class="btn" type="button" data-encuesta-estado="${e.id}" data-estado="abierta">Abrir</button>` : ''}${e.estado === 'abierta' ? `<button class="btn" type="button" data-encuesta-estado="${e.id}" data-estado="cerrada">Cerrar</button>` : ''}${e.estado !== 'borrador' ? `<button class="text-action" type="button" data-resultados="${e.id}">Resultados</button>` : ''}</td></tr>`).join('')}</tbody></table></div>${renderPaginador(data, 'encuestas')}`
        : '<p class="empty-state">Aún no hay encuestas. Crea la primera para medir la cultura de seguridad.</p>';
}

enlazarPaginador(el('encuestas-lista'), 'encuestas', p => {
    paginas.encuestas = p;
    cargarEncuestas().catch(e => error(e.message));
});

function dimensiones(m) {
    return m.porDimension.map(d => `<li><strong>${esc(d.nombre)}</strong>: ${num(d.promedio)} / 5 · ${pct(d.favorable)} favorable <span class="prog-bar is-good" style="--v:${(Number(d.promedio) || 0) * 20}%"></span></li>`).join('');
}

async function resultados(id) {
    const r = await apiCallAuth(`${api}/encuestas/${id}/resultados`);
    el('encuesta-resultados').innerHTML = `<h3>${esc(r.titulo)} · ${esc(r.periodo)}</h3><p>${r.general.respuestas} respuestas de ${r.poblacion} colaboradores (${pct(r.participacion)} de participación).</p>
        ${r.general.oculto ? `<p class="results-warning">Hay menos de ${r.umbral} respuestas: los resultados se mostrarán al alcanzar ese mínimo.</p>` : `<ul class="prog-dimensions">${dimensiones(r.general)}</ul>`}
        ${r.comparacion ? `<h4>Cambio respecto de ${esc(r.comparacion.anterior.titulo)} (${esc(r.comparacion.anterior.periodo)})</h4><ul>${r.comparacion.diferencias.map(d => `<li>${esc(d.nombre)}: ${d.diferencia == null ? 'Sin datos' : `${d.diferencia > 0 ? '+' : ''}${Number(d.diferencia).toFixed(2)}`}</li>`).join('')}</ul>` : ''}
        ${r.porPregunta ? `<details><summary>Detalle por pregunta</summary><ol>${r.porPregunta.map(p => `<li>${esc(p.texto)} · promedio ${num(p.promedio)} · distribución 1–5: ${p.distribucion.join(' / ')}</li>`).join('')}</ol></details>` : ''}
        ${r.porSucursal.length ? `<details><summary>Por sucursal</summary>${r.porSucursal.map(s => `<h4>${esc(s.sucursal)} (${s.medicion.respuestas})</h4><ul>${dimensiones(s.medicion)}</ul>`).join('')}</details>` : ''}
        ${r.porArea.length ? `<details><summary>Por área</summary>${r.porArea.map(a => `<h4>${esc(a.area)} (${a.medicion.respuestas})</h4><ul>${dimensiones(a.medicion)}</ul>`).join('')}</details>` : ''}
        <p class="panel-caption">${esc(r.nota)}${r.gruposOcultos ? ` ${r.gruposOcultos} grupo(s) ocultos por tamaño.` : ''}</p>`;
}

el('encuestas-lista').addEventListener('click', async e => {
    const estado = e.target.closest('[data-encuesta-estado]'), res = e.target.closest('[data-resultados]');
    try {
        if (estado) {
            if (!confirm(estado.dataset.estado === 'abierta'
                ? '¿Abrir la encuesta? El personal la verá en su portal.'
                : '¿Cerrar la encuesta? No se admitirán más respuestas.')) return;

            await conCarga(estado, async () => {
                await apiCallAuth(
                    `${api}/encuestas/${estado.dataset.encuestaEstado}/estado`,
                    { method: 'POST', body: { estado: estado.dataset.estado } });
                await cargarEncuestas();
            })();
        }

        if (res) await resultados(Number(res.dataset.resultados));
    } catch (err) { error(err.message); }
});

el('btn-nueva-encuesta').addEventListener('click', async () => {
    try {
        const base = await apiCallAuth(`${api}/encuestas/preguntas-base`);
        el('enc-preguntas').innerHTML = base.preguntas
            .map(p => `<li>${esc(p.texto)} <small class="t-meta">(${esc(base.dimensiones[p.dimension])})</small></li>`)
            .join('');

        const hoy = new Date();
        el('enc-desde').value = hoy.toISOString().slice(0, 10);
        el('enc-hasta').value = new Date(hoy.getTime() + 14 * 864e5).toISOString().slice(0, 10);
        el('enc-periodo').value = `${hoy.getFullYear()}-S${hoy.getMonth() < 6 ? 1 : 2}`;
        el('enc-error').textContent = '';
        el('dialog-encuesta').showModal();
    } catch (e) { error(e.message); }
});

el('form-encuesta').addEventListener('submit', async event => {
    event.preventDefault();
    const button = event.submitter;
    button.disabled = true;

    try {
        await apiCallAuth(`${api}/encuestas`, {
            method: 'POST',
            body: {
                titulo: el('enc-titulo').value.trim(),
                periodo: el('enc-periodo').value.trim(),
                fechaInicio: isoDia(el('enc-desde').value),
                fechaFin: isoDia(el('enc-hasta').value, true)
            }
        });
        el('dialog-encuesta').close();
        paginas.encuestas = 1;
        await cargarEncuestas();
    } catch (e) {
        el('enc-error').textContent = e.message;
    } finally {
        button.disabled = false;
    }
});

// matriz y evidencias
async function cargarMatriz() {
    const m = await apiCallAuth(`${api}/matriz`);
    el('matriz').innerHTML = `<div class="table-scroll"><table class="event-table prog-matrix"><thead><tr><th>Norma</th><th>Control</th><th>Requisito</th><th>Funcionalidad en DISMATEC</th><th>Evidencia</th></tr></thead><tbody>${m.map(r => `<tr>
        <td>${esc(r.norma)}</td><td>${esc(r.control)}</td><td>${esc(r.requisito)}</td><td>${esc(r.funcionalidad)} <a class="text-action" href="${esc(r.pantalla)}">Abrir</a></td>
        <td><span class="chip ${r.conEvidencia ? 'nist-baja' : 'is-muted'}">${r.evidencias} ${esc(r.descripcionEvidencia)}</span></td></tr>`).join('')}</tbody></table></div>`;
}

el('btn-evidencias').addEventListener('click', async event => {
    event.target.disabled = true;
    try {
        const paquete = await apiCallAuth(`${api}/evidencias`);
        const blob = new Blob([JSON.stringify(paquete, null, 2)], { type: 'application/json' });
        const link = Object.assign(document.createElement('a'), {
            href: URL.createObjectURL(blob),
            download: `evidencias-programa-${new Date().toISOString().slice(0, 10)}.json`
        });
        document.body.append(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(link.href), 1000);
        toast('Paquete de evidencias descargado.');
    } catch (e) {
        error(e.message);
    } finally {
        event.target.disabled = false;
    }
});

await abrir('panel');
