import { mountLearningJourney, refreshJourney } from '../features/learning/journey.js';
import { mountMessageReports } from '../features/learning/reports.js';
import { mountProgram, refreshProgram } from '../features/learning/program.js';
import { mountHome, publish } from '../features/learning/home.js';
import { questionMarkup } from '../components/quiz.js';
import { LearningApi } from '../services/dashboard-api.js?v=20261123-paginacion';
import { el, escapeHtml as esc, formatDate, toast } from '../utils/ui.js';
import { icon } from '../components/icons.js';
import { renderPaginador, enlazarPaginador, paginarEnNavegador } from '../components/paginacion.js';

document.querySelectorAll('[data-icon]').forEach(node => { node.innerHTML = icon(node.dataset.icon); });
mountHome();
let courses = [], attempt = null, starting = false, evaluating = false, loadVersion = 0;
// historial paginado en el servidor, refuerzos llegan completos y se paginan aqui
let paginaHistorial = 1, paginaRefuerzos = 1, refuerzos = null;

function pintarHistorial(history) {
    el('learning-history').innerHTML = history?.items?.length
        ? history.items.map(row => `<div class="history-item"><div>${esc(row.nombreCampania)}<small>${esc(row.temaAprendido)} · ${esc(formatDate(row.fechaCompletado))}</small></div><span>+${Number(row.puntosObtenidos)} pts</span></div>`).join('')
            + renderPaginador(history, 'historial')
        : `<p class="empty-state">${history ? 'Las capacitaciones completadas de tus simulaciones aparecerán aquí.' : 'No se pudo consultar tu historial.'}</p>`;
}

function pintarRefuerzos() {
    const vista = paginarEnNavegador(refuerzos ?? [], paginaRefuerzos);
    paginaRefuerzos = vista.pagina;
    el('learning-reinforcements').innerHTML = vista.items.length
        ? vista.items.map(r => `<article class="course-card"><span class="course-number">${icon('book')}</span><div><h3>${esc(r.tema)}</h3><p>${esc(r.campania)} · Refuerza lo aprendido en esta simulación.</p></div><a class="btn btn-primary" href="${esc(r.enlace)}">Continuar</a></article>`).join('')
            + renderPaginador(vista, 'refuerzos')
        : `<p class="empty-state">${refuerzos ? 'No tienes capacitaciones pendientes de simulaciones.' : 'No se pudieron consultar tus actividades pendientes.'}</p>`;
}

enlazarPaginador(el('learning-history'), 'historial', async pagina => {
    paginaHistorial = pagina;
    try {
        pintarHistorial(await LearningApi.history(paginaHistorial));
    } catch (error) {
        toast(error.message, 'error');
    }
});

enlazarPaginador(el('learning-reinforcements'), 'refuerzos', pagina => {
    paginaRefuerzos = pagina;
    pintarRefuerzos();
});

const dialog = el('dialog-curso');

async function loadLearning() {
    const version = ++loadVersion;
    // tras completar algo se vuelve a la primera pagina para ver lo mas reciente
    paginaHistorial = 1;
    const results = await Promise.allSettled([
        LearningApi.profile(),
        LearningApi.courses(),
        LearningApi.history(paginaHistorial),
        LearningApi.reinforcements()
    ]);
    if (version !== loadVersion) return;

    const [profile, available, history, reinforcements] = results.map(result => result.status === 'fulfilled' ? result.value : null);
    if (profile) {
        el('welcome-name').textContent = profile.nombre || 'colaborador';
        el('learning-branch').textContent = profile.sucursal || 'Mi aprendizaje';
        el('learning-score').textContent = Number(profile.puntosEducativos ?? 0).toLocaleString('es-BO');
        const badges = profile.insigniasObtenidas ?? [];
        el('learning-badges-count').textContent = badges.length;
        // sellos nuevos desde la ultima visita se animan una vez, la primera visita solo los registra
        let seen = null;
        try { seen = JSON.parse(localStorage.getItem('pasaporte.sellos') ?? 'null'); } catch { }
        const isNew = badge => Array.isArray(seen) && !seen.includes(badge.nombre);
        try { localStorage.setItem('pasaporte.sellos', JSON.stringify(badges.map(b => b.nombre))); } catch { }
        const recent = [...badges].sort((a, b) => new Date(b.fecha) - new Date(a.fecha)).slice(0, 3);
        el('home-stamps').innerHTML = recent.map(badge => `<span class="mini-stamp${isNew(badge) ? ' is-new' : ''}"><b>${esc(badge.nombre)}</b><small>${esc(formatDate(badge.fecha))}</small></span>`).join('');
        el('learning-badges').innerHTML = badges.length
            ? badges.map(badge => `<article class="achievement${isNew(badge) ? ' is-new' : ''}"><span class="medal">${icon('trophy')}</span><h3>${esc(badge.nombre)}</h3><small>${esc(formatDate(badge.fecha))}</small></article>`).join('')
            : '<p class="empty-state">Tu colección empieza aquí.<br>Completa retos y acumula puntos educativos para desbloquear insignias.</p>';
    } else {
        el('learning-badges').innerHTML = '<p class="empty-state">No se pudieron cargar tus logros.</p>';
    }

    if (available) {
        courses = available;
        el('courses-count').textContent = `${courses.length} ${courses.length === 1 ? 'tema' : 'temas'}`;
        el('learning-courses').innerHTML = courses.length
            ? courses.map((course, index) => `<article class="course-card"><span class="course-number">${String(index + 1).padStart(2, '0')}</span><div><h3>${esc(course.nombre)}</h3><span class="course-tag">${course.otorgaPuntos ? 'Puede otorgar puntos' : 'Modo repaso'}</span><p>${esc(course.mensajeCooldown)}</p></div><button class="btn ${course.otorgaPuntos ? 'btn-primary' : 'btn-ghost'}" data-course="${Number(course.idCategoria)}">${course.otorgaPuntos ? 'Empezar' : 'Repasar'} ${icon('arrow')}</button></article>`).join('')
            : '<p class="empty-state">Aún no hay cursos publicados. Vuelve pronto para empezar.</p>';
    } else el('learning-courses').innerHTML = '<p class="empty-state">No se pudieron cargar los retos. <button class="text-action" data-retry>Volver a intentar</button></p>';
    pintarHistorial(history);
    publish('reinforcements', reinforcements ?? []);
    refuerzos = reinforcements;
    pintarRefuerzos();
    const failed = results.find(result => result.status === 'rejected');
    el('learning-error').classList.toggle('hidden', !failed);
    if (failed) {
        el('learning-error').replaceChildren(document.createTextNode(failed.reason.message + ' '));
        const retry = document.createElement('button');
        retry.className = 'text-action';
        retry.textContent = 'Volver a intentar';
        retry.addEventListener('click', loadLearning);
        el('learning-error').append(retry);
    }
}

async function startCourse(id, button) {
    if (starting || evaluating) return;

    starting = true;
    button.disabled = true;
    attempt = null;
    el('course-submit').disabled = true;
    el('course-submit').classList.remove('hidden');
    el('course-feedback').className = 'quiz-feedback hidden';
    el('course-questions').innerHTML = '<p class="empty-state">Preparando tus preguntas…</p>';
    el('course-title').textContent = courses.find(course => course.idCategoria === id)?.nombre ?? 'Reto de seguridad';
    el('course-instructions').textContent = 'Selecciona una respuesta por pregunta.';
    dialog.showModal();
    try {
        const result = await LearningApi.start(id);
        if (!dialog.open) return;
        if (!result.preguntas?.length) throw new Error('Este tema aún no tiene preguntas disponibles. Elige otro reto.');

        attempt = result;
        el('course-questions').innerHTML = questionMarkup(result.preguntas);
        el('course-submit').disabled = false;
    } catch (error) {
        el('course-questions').replaceChildren();
        showFeedback(error.message, true);
    } finally {
        starting = false;
        button.disabled = false;
    }
}

function showFeedback(message, error = false) {
    el('course-feedback').textContent = message;
    el('course-feedback').className = `quiz-feedback${error ? ' is-error' : ''}`;
}

el('learning-courses').addEventListener('click', event => {
    const button = event.target.closest('[data-course]');
    if (button) startCourse(Number(button.dataset.course), button);
    if (event.target.closest('[data-retry]')) loadLearning();
});

dialog.addEventListener('cancel', event => { if (evaluating) event.preventDefault(); });
el('course-form').addEventListener('submit', async event => {
    event.preventDefault();
    if (!attempt || evaluating || !event.target.reportValidity()) return;

    const answers = [...el('course-questions').querySelectorAll('input:checked')].map(input => Number(input.value));
    if (answers.length !== attempt.preguntas.length) return;

    evaluating = true;
    el('course-submit').disabled = true;
    el('course-submit').textContent = 'Comprobando…';
    dialog.querySelectorAll('[data-close-dialog]').forEach(button => { button.disabled = true; });
    try {
        const result = await LearningApi.complete(attempt.idIntento, answers);
        showFeedback([result.mensaje, result.puntosGanados ? `+${result.puntosGanados} puntos educativos.` : '',
            result.insigniasDesbloqueadas?.length
                ? `Nuevas insignias: ${result.insigniasDesbloqueadas.join(', ')}.`
                : ''].filter(Boolean).join(' '));
        el('course-submit').classList.add('hidden');
        el('course-questions').querySelectorAll('input').forEach(input => { input.disabled = true; });
        attempt = null;
    } catch (error) {
        showFeedback(error.message, true);
        if (error.status === 400) {
            el('course-questions').querySelectorAll('.question-explanation').forEach(node => node.classList.remove('hidden'));
            el('course-questions').querySelectorAll('input').forEach(input => { input.disabled = true; });
            el('course-submit').classList.add('hidden');
            attempt = null;
            showFeedback(`${error.message} Revisa las explicaciones y vuelve a abrir el reto para iniciar otro intento.`, true);
        } else {
            // tras un fallo de conexion se reconcilia con el servidor en vez de reenviar a ciegas
            el('course-submit').classList.add('hidden');
            attempt = null;
            showFeedback(`${error.message} Cierra y vuelve a abrir el reto para comprobar su estado.`, true);
        }
    } finally {
        evaluating = false;
        el('course-submit').textContent = 'Comprobar respuestas';
        dialog.querySelectorAll('[data-close-dialog]').forEach(button => { button.disabled = false; });
    }

    // el intento cambio el progreso, se reconcilia todo con el servidor
    await refrescarAprendizaje();
});

// refresca todo lo afectado sin recargar, las llamadas simultaneas se agrupan en una
let refrescando = null, repetir = false, ultimoRefresco = 0, recargarMensajes = null;

function refrescarAprendizaje() {
    if (refrescando) {
        repetir = true;
        return refrescando;
    }

    refrescando = (async () => {
        do {
            repetir = false;
            ultimoRefresco = Date.now();
            await Promise.allSettled([loadLearning(), refreshJourney(), refreshProgram()]);
        } while (repetir);
    })().finally(() => { refrescando = null; });
    return refrescando;
}

// al volver desde otra pagina o a la pestana del navegador
function alVolver(forzar) {
    if (document.hidden || document.querySelector('dialog[open]')) return;
    if (!forzar && Date.now() - ultimoRefresco < 10000) return;

    refrescarAprendizaje();
    recargarMensajes?.();
}

await loadLearning();

await mountLearningJourney(refrescarAprendizaje);
recargarMensajes = await mountMessageReports(refrescarAprendizaje);
await mountProgram();
document.addEventListener('visibilitychange', () => alVolver(false));
window.addEventListener('pageshow', event => { if (event.persisted) alVolver(true); });
