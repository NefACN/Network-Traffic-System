import { apiCallAuth } from '../services/api.js';
import { el, escapeHtml as esc, toast, conCarga } from '../utils/ui.js';
import { renderPaginador, enlazarPaginador, conPagina } from '../components/paginacion.js';

const api = '/ciclo-http';
const date = value => value ? new Date(value).toLocaleString('es-BO', { timeZone: 'America/La_Paz' }) : '—';
const estados = { pendiente: 'Pendiente', campania: 'Campaña preparada', microleccion: 'Microlección asignada', descartada: 'Descartada' };
const origenes = { http: 'Anomalía HTTP confirmada', incidente: 'Incidente real confirmado' };
const tipos = { apertura: 'Apertura (píxel)', clic: 'Clic en el enlace', credenciales: 'Envío del formulario señuelo' };
const tecnicos = {
    validating: 'Validando',
    validated: 'Validado: falta confirmar',
    ready: 'Disponible',
    rejected: 'Sin datos válidos',
    canceled: 'Cancelado',
    queued: 'En cola',
    running: 'Procesando',
    completed: 'Finalizada',
    failed: 'Fallida',
    stopped: 'Detenida'
};

const tec = v => tecnicos[v] || v;
const pct = v => v == null ? 'Sin datos' : `${Number(v).toFixed(1)} %`;
let sugerencias = [], microlecciones = null, actual = null;
const paginas = { sugerencias: 1, campanias: 1, conjuntos: 1 };

// contenedor del paginador debajo de cada listado
function paginador(despues) {
    const box = document.createElement('div');
    despues.after(box);
    return box;
}

const paginadorSugerencias = paginador(el('sugerencias')),
    paginadorCampanias = paginador(el('ciclo-campanias').closest('.table-scroll')),
    paginadorConjuntos = paginador(el('ciclo-conjuntos'));

function error(message) {
    el('ciclo-error').textContent = message;
    el('ciclo-error').classList.remove('hidden');
}

async function cargarSugerencias() {
    const estado = el('sug-estado').value;
    const data = await apiCallAuth(conPagina(
        `${api}/sugerencias${estado ? `?estado=${encodeURIComponent(estado)}` : ''}`,
        paginas.sugerencias));
    if (!data.items.length && paginas.sugerencias > 1) {
        paginas.sugerencias--;
        return cargarSugerencias();
    }

    sugerencias = data.items;
    paginadorSugerencias.innerHTML = renderPaginador(data, 'sugerencias');
    el('sugerencias').innerHTML = sugerencias.length ? sugerencias.map(s => `<article class="history-item suggestion">
        <div><strong>${esc(s.sucursal || 'Toda la organización')}${s.area ? ` · ${esc(s.area)}` : ''}</strong>
        <small>${esc(origenes[s.origen] || s.origen)} · ${s.cantidadEvidencias} evidencia(s) · última ${esc(date(s.ultimaEvidencia))}${s.tema ? ` · tema: ${esc(s.tema)}` : ''} · ${esc(estados[s.estado])}</small>
        <p>${esc(s.motivo)}</p>
        <details><summary>Evidencias</summary><ul>${(s.evidencias || []).map(e => `<li>${esc(date(e.fecha))} · ${esc(e.descripcion)} <small class="t-meta">${esc(e.referencia)}</small></li>`).join('')}</ul></details>
        ${s.estado === 'pendiente' ? `<p class="panel-caption">Alcance actual: ${Number(s.alcance) || 0} colaborador(es) activos sin exclusión.</p>` : ''}
        ${s.estado === 'microleccion' ? `<p class="panel-caption">Microlección asignada a ${Number(s.personasAsignadas)} persona(s).</p>` : ''}${s.nota ? `<p class="panel-caption">Nota: ${esc(s.nota)}</p>` : ''}</div>
        ${s.estado === 'pendiente' ? `<div class="row-actions"><button class="btn btn-primary" type="button" data-campania="${s.id}">Preparar campaña</button><button class="btn" type="button" data-micro="${s.id}">Asignar microlección</button><button class="text-action" type="button" data-descartar="${s.id}">Descartar</button></div>` : ''}
        </article>`).join('') : '<p class="empty-state">No hay sugerencias en este estado.</p>';
}

async function cargarCampanias() {
    const data = await apiCallAuth(conPagina(`${api}/campanias`, paginas.campanias)), rows = data.items;
    paginadorCampanias.innerHTML = renderPaginador(data, 'campanias');
    el('ciclo-campanias').innerHTML = rows.length ? rows.map(c => `<tr><td>${esc(c.titulo)}<small class="t-meta"> #${Number(c.idCampania)} · ${esc(c.estado)}</small></td><td>${c.aperturas}</td><td>${c.clics}</td><td>${c.credenciales}</td><td>${esc(date(c.desde))} – ${esc(date(c.hasta))}</td>
        <td><button class="btn" type="button" data-generar="${Number(c.idCampania)}">Generar conjunto NGINX</button></td></tr>`).join('')
        : '<tr><td colspan="6" class="table-state">Aún no hay interacciones HTTP de simulaciones. Se registran cuando alguien abre, hace clic o envía el formulario señuelo.</td></tr>';
}

async function cargarConjuntos() {
    const data = await apiCallAuth(conPagina(`${api}/conjuntos`, paginas.conjuntos)), rows = data.items;
    paginadorConjuntos.innerHTML = renderPaginador(data, 'conjuntos');
    el('ciclo-conjuntos').innerHTML = rows.length ? `<div class="table-scroll"><table class="event-table"><thead><tr><th>Conjunto</th><th>Líneas</th><th>Estado</th><th>Ejecuciones</th></tr></thead><tbody>${rows.map(d => `<tr><td>${esc(d.name)}<small class="t-meta"> ${esc(d.campania || '')}</small></td><td>${d.lineas}</td><td>${esc(tec(d.state))}</td>
        <td>${d.ejecuciones.length ? d.ejecuciones.map(j => `<button class="text-action" type="button" data-verdad="${esc(j.id)}">${esc(date(j.createdAt))} · ${esc(tec(j.state))}</button>`).join('<br>') : '<span class="t-meta">Confírmalo en Fuentes y registros y ejecuta un análisis.</span>'}</td></tr>`).join('')}</tbody></table></div>`
        : '<p class="empty-state">Todavía no generaste conjuntos desde simulaciones.</p>';
}

async function verdad(id) {
    el('ciclo-verdad').textContent = 'Consultando…';
    try {
        const v = await apiCallAuth(`${api}/ejecuciones/${encodeURIComponent(id)}/verdad-conocida`);
        el('ciclo-verdad').innerHTML = `<h3>${esc(v.dataset.name)} · modelo ${esc(v.modelVersion || 'sin versión')}</h3><p>${v.analizadas} de ${v.total} líneas analizadas.</p>
            <div class="table-scroll"><table class="event-table"><thead><tr><th>Interacción conocida</th><th>Líneas</th><th>Analizadas</th><th>Ruido</th><th>Alerta candidata</th><th>Score medio</th><th>Grupos principales</th></tr></thead><tbody>
            ${v.porTipo.map(t => `<tr><td>${esc(tipos[t.tipo] || t.tipo)}</td><td>${t.lineas}</td><td>${t.analizadas}</td><td>${t.ruido} · ${pct(t.tasaRuido)}</td><td>${t.candidatas} · ${pct(t.tasaCandidatas)}</td><td>${t.scoreMedio == null ? '—' : Number(t.scoreMedio).toFixed(4)}</td><td>${t.grupos.map(g => `${g.cluster === -1 ? 'Ruido' : `C${g.cluster}`}: ${g.cantidad}`).join(', ') || '—'}</td></tr>`).join('')}
            </tbody></table></div><p class="panel-caption">${esc(v.nota)}</p>`;
    } catch (e) { el('ciclo-verdad').textContent = e.message; }
}

el('sug-estado').addEventListener('change', () => {
    paginas.sugerencias = 1;
    cargarSugerencias().catch(e => error(e.message));
});

enlazarPaginador(paginadorSugerencias, 'sugerencias', p => {
    paginas.sugerencias = p;
    cargarSugerencias().catch(e => error(e.message));
});

enlazarPaginador(paginadorCampanias, 'campanias', p => {
    paginas.campanias = p;
    cargarCampanias().catch(e => error(e.message));
});

enlazarPaginador(paginadorConjuntos, 'conjuntos', p => {
    paginas.conjuntos = p;
    cargarConjuntos().catch(e => error(e.message));
});

el('sugerencias').addEventListener('click', async event => {
    const campania = event.target.closest('[data-campania]'),
    micro = event.target.closest('[data-micro]'),
    descartar = event.target.closest('[data-descartar]');
    if (campania) {
        await conCarga(campania, async () => {
            try {
                const r = await apiCallAuth(`${api}/sugerencias/${Number(campania.dataset.campania)}/campania`, { method: 'POST' });
                const q = new URLSearchParams({ refuerzo: '1' });
                if (r.idSucursal) q.set('idSucursal', r.idSucursal);
                if (r.idArea) q.set('idArea', r.idArea);
                location.href = `campanias-phishing.html?${q}`;
            } catch (e) { error(e.message); }
        })();
    }

    if (micro) {
        actual = sugerencias.find(s => s.id === Number(micro.dataset.micro));
        el('micro-error').textContent = '';
        try {
            microlecciones ??= (await apiCallAuth('/concientizacion-admin/catalogo')).filter(a => a.esMicroleccion && a.habilitada);
            el('micro-actividad').innerHTML = microlecciones.length
                ? microlecciones.map(a => `<option value="${a.id}" ${a.idCategoria === actual.idCategoria ? 'selected' : ''}>${esc(a.titulo)} · ${esc(a.tema)}</option>`).join('')
                : '<option value="">No hay microlecciones habilitadas</option>';
            el('micro-alcance').textContent = `Se asignará a ${Number(actual.alcance) || 0} colaborador(es) de ${actual.sucursal || 'toda la organización'}${actual.area ? ` · ${actual.area}` : ''}. Aparecerá primero en su portal de aprendizaje.`;
            el('dialog-micro').showModal();
        } catch (e) { error(e.message); }
    }

    if (descartar) {
        const nota = prompt('¿Por qué descartas esta sugerencia? (obligatorio)', '');
        if (nota === null) return;

        await conCarga(descartar, async () => {
            try {
                await apiCallAuth(
                    `${api}/sugerencias/${Number(descartar.dataset.descartar)}/descartar`,
                    { method: 'POST', body: { nota } });
                await cargarSugerencias();
            }
            catch (e) { error(e.message); }
        })();
    }
});

el('form-micro').addEventListener('submit', async event => {
    event.preventDefault();
    const button = event.submitter;
    button.disabled = true;
    try {
        const r = await apiCallAuth(`${api}/sugerencias/${actual.id}/microleccion`, {
            method: 'POST',
            body: { idActividad: Number(el('micro-actividad').value) }
        });

        el('dialog-micro').close();
        toast(`Microlección asignada a ${r.asignadas} persona(s).`);
        await cargarSugerencias();
    } catch (e) { el('micro-error').textContent = e.message; } finally { button.disabled = false; }
});

el('ciclo-campanias').addEventListener('click', async event => {
    const b = event.target.closest('[data-generar]');
    if (!b) return;

    b.disabled = true;
    try {
        const r = await apiCallAuth(`${api}/campanias/${Number(b.dataset.generar)}/conjunto`, { method: 'POST' });
        toast(`Conjunto generado con ${r.lineas} líneas. Confírmalo en Fuentes y registros cuando termine la validación.`);
        await cargarConjuntos();
    } catch (e) { error(e.message); } finally { b.disabled = false; }
});

el('ciclo-conjuntos').addEventListener('click', event => {
    const b = event.target.closest('[data-verdad]');
    if (b) verdad(b.dataset.verdad);
});

try { await Promise.all([cargarSugerencias(), cargarCampanias(), cargarConjuntos()]); } catch (e) { error(e.message); }
