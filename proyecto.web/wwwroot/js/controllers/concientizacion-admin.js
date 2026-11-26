import { apiCallAuth } from '../services/api.js';
import { el, escapeHtml as esc, toast } from '../utils/ui.js';
import { renderPaginador, enlazarPaginador, paginarEnNavegador, conPagina, ultimaPagina } from '../components/paginacion.js';
let activities = [], questions = [], badges = [], selected = new Set();
// actividades paginadas en el servidor, logros en el navegador
let paginaActividades = 1, paginaLogros = 1;
const api = '/concientizacion-admin';

// contenedor del paginador tras `ancla`, se crea y enlaza una sola vez
function contenedorPaginador(ancla, clave, alCambiar) {
    let contenedor = el(`${clave}-paginador`);
    if (!contenedor) {
        contenedor = document.createElement('div');
        contenedor.id = `${clave}-paginador`;
        ancla.after(contenedor);
        enlazarPaginador(contenedor, clave, alCambiar);
    }

    return contenedor;
}

async function load() {
    // el selector de preguntas usa el banco completo sin paginar
    const results = await Promise.all([
        apiCallAuth(conPagina(api, paginaActividades)),
        apiCallAuth('/campanias-phishing/categorias-educativas'),
        apiCallAuth('/preguntas-admin/catalogo'),
        apiCallAuth(api + '/gamificacion')
    ]);
    loadCyberScore();
    [, , questions] = results;
    const categories = results[1];
    const game = results[3];
    badges = game.logros;
    el('actividad-tema').innerHTML = categories.map(c => `<option value="${c.idCategoria}">${esc(c.nombre)}</option>`).join('');
    renderActividades(results[0]);
    const r = game.reglas;
    for (const [id, value] of Object.entries({
        'regla-puntos': r.puntosCursoLibre,
        'regla-dias': r.diasCursoLibre,
        'regla-refuerzo': r.puntosRefuerzo,
        'regla-reporte': r.puntosReporte ?? 15,
        'regla-microleccion': r.puntosMicroleccion ?? 10,
        'regla-inspeccion': r.puntosInspeccion ?? 10,
        'regla-meta': r.metaSemanalTemas,
        'regla-nivel': r.puntosPorNivel
    }))
        el(id).value = value;
    renderLogros();
}

async function loadActividades(pagina) {
    try {
        const datos = await apiCallAuth(conPagina(api, pagina));
        if (!datos.items.length && datos.total > 0 && datos.pagina > 1) return loadActividades(ultimaPagina(datos));

        renderActividades(datos);
    } catch (e) { error(e.message); }
}

function renderActividades(datos) {
    paginaActividades = datos.pagina;
    activities = datos.items;
    const tabla = el('actividades-admin-body').closest('table');
    contenedorPaginador(
        tabla.parentElement.classList.contains('table-scroll') ? tabla.parentElement : tabla,
        'actividades',
        loadActividades).innerHTML = renderPaginador(datos, 'actividades');
    el('actividades-admin-body').innerHTML = activities.length
        ? activities.map(a => `<tr><td><strong>${esc(a.titulo)}</strong><small class="t-meta"> · Revisión ${a.revision}${a.esMicroleccion ? ` · Microlección${a.paraCredenciales ? ' (credenciales)' : ''}` : ''}</small></td><td>${esc(a.tema)}</td><td>${a.habilitada ? 'Habilitada' : 'Borrador / deshabilitada'}</td><td>${a.puntos}</td><td>${a.diasRepeticion} días</td><td><button type="button" class="text-action" data-edit="${a.id}">Editar</button> <button type="button" class="text-action" data-state="${a.id}">${a.habilitada ? 'Deshabilitar' : 'Habilitar'}</button></td></tr>`).join('')
        : '<tr><td colspan="6" class="table-state">Todavía no hay actividades educativas. Crea una para publicar contenido y un cuestionario.</td></tr>';
}

function renderLogros() {
    const labels = {
        puntos: 'puntos educativos',
        actividades: 'actividades aprobadas',
        temas: 'temas distintos',
        semanas: 'semanas con aprendizaje',
        reportes: 'simulaciones reportadas',
        cyberscore: 'de CyberScore'
    };

    const datos = paginarEnNavegador(badges, paginaLogros);
    paginaLogros = datos.pagina;
    contenedorPaginador(el('logros-admin'), 'logros', pagina => {
        paginaLogros = pagina;
        renderLogros();
    }).innerHTML = renderPaginador(datos, 'logros');
    el('logros-admin').innerHTML = datos.items.map(b => `<div class="history-item"><div><strong>${esc(b.nombre)}</strong><small>${esc(b.descripcion)} · ${b.puntosRequeridos} ${labels[b.tipoCondicion] || ''} · ${b.activa ? 'Habilitado' : 'Deshabilitado'}</small></div><button class="text-action" data-badge="${b.id_Insignia}" type="button">Editar</button></div>`).join('')
        || '<p class="empty-state">No hay reconocimientos configurados.</p>';
}

const csFields = p => ({
    'cs-clic': p.pesoClic,
    'cs-credenciales': p.pesoCredenciales,
    'cs-reporte': p.bonoReporte,
    'cs-cuestionario': p.bonoCuestionario,
    'cs-micro': p.bonoMicroleccion,
    'cs-vida': p.vidaMediaDias,
    'cs-ventana': p.ventanaDias,
    'cs-minimo': p.minimoSimulaciones,
    'cs-facil': p.factorPocoDificil,
    'cs-dificil': p.factorMuyDificil,
    'cs-exp-baja': p.exposicionBaja,
    'cs-exp-media': p.exposicionMedia,
    'cs-exp-alta': p.exposicionAlta,
    'cs-umbral-bajo': p.umbralBajo ?? 80,
    'cs-umbral-medio': p.umbralMedio ?? 60,
    'cs-umbral-alto': p.umbralAlto ?? 40,
    'cs-plan': p.maximoPlanCritico ?? 3
});

// seccion independiente, si falla el resto sigue disponible
async function loadCyberScore() {
    try {
        const cs = await apiCallAuth(api + '/cyberscore'), pol = cs.politica;
        el('cyberscore-formula').textContent = cs.formula;
        el('cyberscore-revision').textContent = `${cs.version} · revisión ${pol.revision}`;
        for (const [id, value] of Object.entries(csFields(pol))) el(id).value = value;
    } catch (e) {
        el('cyberscore-formula').textContent = `No se pudieron cargar los pesos del CyberScore: ${e.message}`;
    }
}

function error(message) {
    el('aprendizaje-admin-error').textContent = message;
    el('aprendizaje-admin-error').classList.remove('hidden');
}

function section(value = {}) {
    const node = document.createElement('div');
    node.className = 'section-editor';
    node.innerHTML = `<label>Título de sección<input data-section-title maxlength="120" value="${esc(value.titulo || value.Titulo || '')}" required></label><label>Material de aprendizaje<textarea data-section-content maxlength="12000" required>${esc(value.contenido || value.Contenido || '')}</textarea></label><button class="text-action" type="button" data-remove-section>Quitar sección</button>`;
    node.querySelector('[data-remove-section]').addEventListener('click', () => node.remove());
    el('actividad-secciones').append(node);
}

function pickQuestions() {
    el('actividad-preguntas').innerHTML = questions.filter(q => q.idCategoria === Number(el('actividad-tema').value))
        .map(q => `<label class="quiz-option"><input type="checkbox" value="${q.idPregunta}" ${selected.has(q.idPregunta) ? 'checked' : ''}><span>${esc(q.textoPregunta)}</span></label>`)
        .join('')
        || '<p class="empty-state">Este tema no tiene preguntas. Crea preguntas en el banco y actualiza la lista.</p>';
}

function openActivity(a) {
    el('form-actividad-admin').reset();
    el('actividad-error').textContent = '';
    el('actividad-id').value = a?.id || '';
    el('actividad-editor-title').textContent = a ? 'Editar actividad' : 'Nueva actividad';
    el('actividad-titulo').value = a?.titulo || '';
    el('actividad-descripcion').value = a?.descripcion || '';
    if (a) el('actividad-tema').value = a.idCategoria;
    el('actividad-puntos').value = a?.puntos ?? 20;
    el('actividad-dias').value = a?.diasRepeticion ?? 7;
    el('actividad-aprobacion').value = a?.porcentajeAprobacion ?? 70;
    el('actividad-habilitada').checked = a?.habilitada ?? false;
    el('actividad-repaso').checked = a?.permitirRepaso ?? true;
    el('actividad-micro').checked = a?.esMicroleccion ?? false;
    el('actividad-credenciales').checked = a?.paraCredenciales ?? false;
    el('actividad-secciones').replaceChildren();
    const sections = a ? JSON.parse(a.secciones) : [];
    if (sections.length) sections.forEach(section);
    else section();
    selected = new Set(a?.idsPreguntas || []);
    pickQuestions();
    el('dialog-actividad-admin').showModal();
}

function payload() {
    return {
        titulo: el('actividad-titulo').value.trim(),
        descripcion: el('actividad-descripcion').value.trim(),
        idCategoria: Number(el('actividad-tema').value),
        secciones: [...el('actividad-secciones').children].map(n => ({
            titulo: n.querySelector('[data-section-title]').value.trim(),
            contenido: n.querySelector('[data-section-content]').value.trim()
        })),
        idsPreguntas: [...el('actividad-preguntas').querySelectorAll('input:checked')].map(i => Number(i.value)),
        habilitada: el('actividad-habilitada').checked,
        puntos: Number(el('actividad-puntos').value),
        diasRepeticion: Number(el('actividad-dias').value),
        porcentajeAprobacion: Number(el('actividad-aprobacion').value),
        permitirRepaso: el('actividad-repaso').checked,
        esMicroleccion: el('actividad-micro').checked,
        paraCredenciales: el('actividad-micro').checked && el('actividad-credenciales').checked
    };
}

el('nueva-actividad').addEventListener('click', () => openActivity());
el('agregar-seccion').addEventListener('click', () => section());
el('actividad-tema').addEventListener('change', () => {
    selected.clear();
    pickQuestions();
});

el('actualizar-preguntas').addEventListener('click', async () => {
    try {
        selected = new Set(payload().idsPreguntas);
        questions = await apiCallAuth('/preguntas-admin/catalogo');
        pickQuestions();
    } catch (e) {
        el('actividad-error').textContent = e.message;
    }
});

el('form-actividad-admin').addEventListener('submit', async event => {
    event.preventDefault();
    const id = el('actividad-id').value;
    el('guardar-actividad').disabled = true;
    try {
        await apiCallAuth(api + (id ? '/' + id : ''), { method: id ? 'PUT' : 'POST', body: payload() });
        el('dialog-actividad-admin').close();
        await load();
        toast('Actividad guardada.');
    }
    catch (e) { el('actividad-error').textContent = e.message; } finally { el('guardar-actividad').disabled = false; }
});

el('previsualizar-actividad').addEventListener('click', () => {
    const a = payload();
    el('actividad-preview').innerHTML = `<h2>${esc(a.titulo)}</h2><p>${esc(a.descripcion)}</p>${a.secciones.map(s => `<h3>${esc(s.titulo)}</h3><p class="material-copy">${esc(s.contenido)}</p>`).join('')}<h3>Cuestionario</h3><ol>${questions.filter(q => a.idsPreguntas.includes(q.idPregunta)).map(q => `<li>${esc(q.textoPregunta)}</li>`).join('')}</ol>`;
    el('dialog-preview').showModal();
});

el('actividades-admin-body').addEventListener('click', async event => {
    const edit = event.target.closest('[data-edit]');
    if (edit) return openActivity(activities.find(a => a.id === Number(edit.dataset.edit)));

    const state = event.target.closest('[data-state]');
    if (!state) return;

    const a = activities.find(a => a.id === Number(state.dataset.state));
    state.disabled = true;
    try {
        await apiCallAuth(`${api}/${a.id}/estado`, { method: 'PATCH', body: { habilitada: !a.habilitada } });
        await load();
    } catch (e) {
        error(e.message);
    } finally {
        state.disabled = false;
    }
});

el('form-reglas').addEventListener('submit', async event => {
    event.preventDefault();
    const button = event.submitter;
    button.disabled = true;
    try {
        await apiCallAuth(api + '/reglas', {
            method: 'PUT',
            body: {
                puntosCursoLibre: Number(el('regla-puntos').value),
                diasCursoLibre: Number(el('regla-dias').value),
                puntosRefuerzo: Number(el('regla-refuerzo').value),
                puntosReporte: Number(el('regla-reporte').value),
                puntosMicroleccion: Number(el('regla-microleccion').value),
                puntosInspeccion: Number(el('regla-inspeccion').value),
                metaSemanalTemas: Number(el('regla-meta').value),
                puntosPorNivel: Number(el('regla-nivel').value)
            }
        });

        toast('Reglas guardadas para las actividades posteriores.');
    } catch (e) {
        error(e.message);
    } finally {
        button.disabled = false;
    }
});

el('form-cyberscore').addEventListener('submit', async event => {
    event.preventDefault();
    const button = event.submitter;
    button.disabled = true;
    const n = id => Number(el(id).value);
    try {
        await apiCallAuth(api + '/cyberscore', {
            method: 'PUT',
            body: {
                pesoClic: n('cs-clic'),
                pesoCredenciales: n('cs-credenciales'),
                bonoReporte: n('cs-reporte'),
                bonoCuestionario: n('cs-cuestionario'),
                bonoMicroleccion: n('cs-micro'),
                vidaMediaDias: n('cs-vida'),
                ventanaDias: n('cs-ventana'),
                minimoSimulaciones: n('cs-minimo'),
                factorPocoDificil: n('cs-facil'),
                factorMuyDificil: n('cs-dificil'),
                exposicionBaja: n('cs-exp-baja'),
                exposicionMedia: n('cs-exp-media'),
                exposicionAlta: n('cs-exp-alta'),
                umbralBajo: n('cs-umbral-bajo'),
                umbralMedio: n('cs-umbral-medio'),
                umbralAlto: n('cs-umbral-alto'),
                maximoPlanCritico: n('cs-plan')
            } });
        toast('Pesos del CyberScore guardados.');
        await loadCyberScore();
    } catch (e) { error(e.message); } finally { button.disabled = false; }
});

function openBadge(b) {
    el('form-logro').reset();
    el('logro-error').textContent = '';
    el('logro-id').value = b?.id_Insignia || '';
    el('logro-nombre').value = b?.nombre || '';
    el('logro-descripcion').value = b?.descripcion || '';
    el('logro-condicion').value = b?.tipoCondicion || 'puntos';
    el('logro-objetivo').value = b?.puntosRequeridos || 1;
    el('logro-activo').checked = b?.activa ?? true;
    el('dialog-logro').showModal();
}

el('nuevo-logro').addEventListener('click', () => openBadge());
el('logros-admin').addEventListener('click', event => {
    const b = event.target.closest('[data-badge]');
    if (b) openBadge(badges.find(i => i.id_Insignia === Number(b.dataset.badge)));
});

el('form-logro').addEventListener('submit', async event => {
    event.preventDefault();
    const id = el('logro-id').value;
    const button = event.submitter;
    button.disabled = true;
    try {
        await apiCallAuth(`${api}/logros${id ? '/' + id : ''}`, {
            method: id ? 'PUT' : 'POST',
            body: {
                nombre: el('logro-nombre').value.trim(),
                descripcion: el('logro-descripcion').value.trim(),
                tipoCondicion: el('logro-condicion').value,
                objetivo: Number(el('logro-objetivo').value),
                activa: el('logro-activo').checked
            }
        });

        el('dialog-logro').close();
        await load();
    } catch (e) {
        el('logro-error').textContent = e.message;
    } finally {
        button.disabled = false;
    }
});

try { await load(); } catch (e) { error(e.message); }
