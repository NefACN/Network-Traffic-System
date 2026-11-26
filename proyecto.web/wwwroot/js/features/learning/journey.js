import { apiCallAuth } from '../../services/api.js';
import { el, escapeHtml as esc, formatDate } from '../../utils/ui.js';
import { questionMarkup } from '../../components/quiz.js';
import { publish } from './home.js';
import { renderPaginador, enlazarPaginador, paginarEnNavegador } from '../../components/paginacion.js';

const api = '/aprendizaje';
// onChanged refresca todo lo afectado, dirty marca un intento iniciado
let current = null, sectionIndex = 0, busy = false, activities = [], onChanged = async () => {}, dirty = false;
// el catalogo de actividades llega completo y se pagina en el navegador
let paginaActividades = 1;
const dialog = () => el('activity-dialog');
const nist = { muy_dificil: 'muy difícil', moderada: 'moderada', poco_dificil: 'poco difícil' };
const typeLabel = {
    clic: 'Clic',
    credenciales: 'Credenciales',
    reporte: 'Reporte',
    cuestionario: 'Aprendizaje',
    microleccion: 'Microlección',
    reporte_real: 'Reporte real'
};

const signed = n => `${n > 0 ? '+' : ''}${Number(n).toLocaleString('es-BO', { maximumFractionDigits: 1 })}`;
const LEVEL_TEXT = {
    Bajo: 'Reconoces bien los engaños. Sigue reportando los mensajes sospechosos para mantenerte aquí.',
    Medio: 'Vas bien, pero algunos mensajes te engañaron. Repasa las actividades recomendadas de esos temas.',
    Alto: 'Varias simulaciones te engañaron hace poco. Completa los refuerzos asignados: cada uno recupera tu CyberScore.',
    'Crítico': 'Te asignamos un plan de refuerzo con microlecciones breves. Completarlo y reportar los próximos mensajes sospechosos te ayudará a subir de nivel.',
};

const levelClass = nivel => ({ Bajo: 'is-low', Medio: 'is-medium', Alto: 'is-high', 'Crítico': 'is-critical' })[nivel] ?? 'is-none';

// CyberScore v3 (0-100, mas es mejor): nivel, que hacer y que hechos lo movieron
export function cyberScoreCard(v) {
    const value = v.cyberScore ?? v.valor, evaluable = v.evaluable && value != null;
    const u = v.umbrales ?? { bajo: 80, medio: 60, alto: 40 };
    const rows = v.contribuciones.map(c => {
        const impacto = c.impacto ?? -c.aporte;
        return `<tr><td>${esc(formatDate(c.fecha))}</td><td>${esc(typeLabel[c.tipo] || c.tipo)}</td><td>${esc(c.concepto)}${c.dificultad ? `<small> · dificultad ${esc(nist[c.dificultad] || c.dificultad)}</small>` : ''}</td><td class="${impacto < 0 ? 'score-down' : 'score-up'}">${signed(impacto)}</td></tr>`;
    }).join('');
    return `<div class="cyberscore ${evaluable ? levelClass(v.nivel) : 'is-none'}">
        <div class="cyberscore-gauge" role="img" aria-label="${evaluable ? `CyberScore ${Number(value).toFixed(0)} de 100, riesgo ${esc(v.nivel)}` : 'CyberScore sin evaluación suficiente'}" style="--score:${evaluable ? Number(value) : 0}">
            <strong>${evaluable ? Number(value).toFixed(0) : '—'}</strong><span>de 100</span></div>
        <div class="cyberscore-text"><p class="eyebrow">TU NIVEL DE RIESGO</p><h3>${evaluable ? `Riesgo ${esc(v.nivel)}` : 'Sin evaluación suficiente'}</h3>
            <p>${evaluable ? esc(LEVEL_TEXT[v.nivel] ?? '') : `Se necesitan al menos ${v.minimoSimulaciones} simulaciones recientes para calcular tu CyberScore; llevas ${v.simulaciones}. Los hechos registrados ya se muestran abajo.`}</p>
            <ol class="cyberscore-scale" aria-label="Escala de niveles"><li class="is-low">Bajo · ${u.bajo}–100</li><li class="is-medium">Medio · ${u.medio}–${u.bajo - 1}</li><li class="is-high">Alto · ${u.alto}–${u.medio - 1}</li><li class="is-critical">Crítico · 0–${u.alto - 1}</li></ol>
            <p class="panel-caption">${v.clics ?? 0} clics, ${v.credenciales ?? 0} envíos de credenciales y ${v.reportes ?? 0} reportes en ${v.simulaciones} simulaciones del periodo.</p></div></div>
        <details class="score-details"><summary>Qué movió tu CyberScore (${v.contribuciones.length} hechos)</summary>
        <p class="panel-caption">${esc(v.explicacion)}</p>
        ${rows ? `<div class="table-scroll"><table class="event-table"><thead><tr><th>Fecha</th><th>Tipo</th><th>Hecho</th><th>Efecto*</th></tr></thead><tbody>${rows}</tbody></table></div><p class="panel-caption">* Efecto vigente antes de aplicar la exposición de tu área (${esc(v.nivelExposicion)} · ×${Number(v.exposicion).toFixed(2)}). ${esc(v.formula)} Versión ${esc(v.version)}, revisión ${Number(v.revision)}.</p>` : '<p class="empty-state">Aún no hay hechos en el periodo.</p>'}</details>`;
}

export async function refreshJourney() {
    try {
        const [available, progress] = await Promise.all([apiCallAuth(api + '/actividades'), apiCallAuth(api + '/mi-progreso')]);
        activities = Array.isArray(available) ? available : [];
        publish('activities', activities);
        const vista = paginarEnNavegador(activities, paginaActividades);
        paginaActividades = vista.pagina;
        el('voluntary-activities').innerHTML = vista.items.length
            ? vista.items.map(a => `<article class="course-card"><span class="course-number">${a.puntos}</span><div><h3>${esc(a.titulo)}</h3>${a.asignada ? `<span class="course-tag is-assigned">${a.motivoAsignacion === 'critico' ? 'Plan de refuerzo · sube tu CyberScore' : a.motivoAsignacion === 'sugerencia' ? 'Asignada a tu sucursal tras un incidente detectado' : `Asignada tras ${a.motivoAsignacion === 'credenciales' ? 'enviar credenciales en' : 'un clic en'} una simulación`}</span>` : ''}${a.esMicroleccion ? '<span class="course-tag">Microlección · menos de 2 minutos</span>' : ''}<span class="course-tag">${esc(a.tema)} · ${a.otorgaPuntos ? `${a.puntos} puntos al aprobar` : 'Repaso sin puntos'}</span><p>${esc(a.descripcion)}</p><small>${a.otorgaPuntos ? `Nueva recompensa cada ${a.diasRepeticion} días.` : `Próxima recompensa: ${esc(formatDate(a.proximaRecompensa))}.`}</small></div><button class="btn btn-primary" type="button" data-activity="${a.id}" ${a.puedeIniciar ? '' : 'disabled'}>${a.idIntento ? 'Continuar' : a.puedeIniciar ? 'Empezar' : 'Disponible después'}</button></article>`).join('')
                + renderPaginador(vista, 'actividades')
            : '<p class="empty-state">Todavía no hay actividades publicadas. Puedes practicar con los temas disponibles más abajo.</p>';
        if (progress && !Array.isArray(progress)) publish('progress', progress);
        if (!progress || Array.isArray(progress)) {
            el('personal-risk').textContent = 'Tu progreso detallado aún no está disponible.';
            return;
        }

        el('journey-level').textContent = `Nivel ${progress.nivel} · ${progress.puntosEducativos} puntos`;
        el('learning-practiced').textContent = progress.temasPracticados;
        el('learning-progress-caption').textContent = 'Temas con al menos una actividad aprobada, desde que empezaste';
        el('learning-progress-caption').textContent = 'Temas con una actividad aprobada';
        el('journey-progress').max = progress.progresoNivel + progress.puntosSiguienteNivel;
        el('journey-progress').value = progress.progresoNivel;
        el('journey-next').textContent = `Te faltan ${progress.puntosSiguienteNivel} puntos para alcanzar el próximo nivel.`;
        const m = progress.metaSemanal;
        el('weekly-count').textContent = `${m.temasCompletados} / ${m.objetivo} temas`;
        el('weekly-caption').textContent = m.cumplida
            ? '¡Meta cumplida! Esta semana ampliaste tus conocimientos.'
            : `Aprueba actividades de ${m.objetivo} temas distintos esta semana. De lunes a domingo, hora de Bolivia.`;
        const plural = (n, one, many) => `${n} ${n === 1 ? one : many}`;
        el('journey-completed').textContent = `${plural(progress.actividadesAprobadas, 'actividad aprobada', 'actividades distintas aprobadas')}`;
        el('journey-weeks').textContent = `${plural(progress.temasPracticados, 'tema', 'temas')} y ${plural(progress.semanasActivas, 'semana', 'semanas')} con aprendizaje.`;
        el('unfinished-activities').innerHTML = progress.enCurso.length
            ? `<h3>Continúa donde lo dejaste</h3>${progress.enCurso.map(a => `<p><button class="text-action" type="button" data-attempt="${a.idIntento}">${esc(a.titulo)} · ${a.evaluado ? 'Finalizar y guardar resultado' : 'Continuar'}</button></p>`).join('')}`
            : '';
        const r = progress.cyberScore;
        // un solo indicador CyberScore v3, los puntos educativos nunca se descuentan
        el('personal-risk').innerHTML = progress.cyberScoreV2
            ? cyberScoreCard(progress.cyberScoreV2)
            : '<p class="empty-state">Tu CyberScore aún no está disponible.</p>';
        el('personal-factors').innerHTML = r.factores.length
            ? r.factores.map(f => `<div class="history-item"><div>${esc(f.campania)}<small>${esc(formatDate(f.fechaEnvio))} · ${f.hizoClick ? 'Clic registrado' : 'Sin clic registrado'} · ${f.capacitacionCompleta ? 'Capacitación completada' : f.hizoClick ? 'Capacitación pendiente' : 'Sin capacitación asignada por clic'}${f.fechaReporte ? ` · Reportado el ${esc(formatDate(f.fechaReporte))}${f.reporteSinClickPrevio ? ' sin clic previo' : ' después de un clic'}` : ' · Sin reporte registrado'}</small></div></div>`).join('')
            : '<p class="empty-state">Aún no hay simulaciones recientes con trazabilidad suficiente.</p>';
        el('personal-recommendations').innerHTML = progress.recomendaciones.length
            ? '<h3>Recomendado para reforzar</h3>'
                + progress.recomendaciones.map(a => `<p><button class="text-action" data-activity="${a.id}" type="button">${esc(a.titulo)} · ${esc(a.tema)}</button></p>`).join('')
            : '<p class="panel-caption">No hay recomendaciones asociadas a tus resultados actuales. Puedes explorar las actividades por iniciativa propia.</p>';
        el('personal-history').innerHTML = progress.historial.length
            ? progress.historial.map(h => `<div class="history-item"><div>${esc(h.titulo)}<small>${esc(formatDate(h.fechaCompletado))} · ${h.exitoso ? 'Aprobada' : 'Por reforzar'}${h.porcentaje == null ? '' : ` · ${Number(h.porcentaje).toFixed(1)} %`} · ${h.puntosObtenidos} puntos</small></div>${h.idActividad ? `<button class="text-action" data-attempt="${h.idIntento}" type="button">Ver resultado</button>` : ''}</div>`).join('')
            : '<p class="empty-state">Aquí aparecerán las actividades que finalices. Elige un tema para comenzar.</p>';
        el('personal-points').innerHTML = progress.movimientos.length
            ? progress.movimientos.map(p => `<div class="history-item"><div>${esc(p.concepto)}<small>${esc(formatDate(p.fecha))}</small></div><strong>+${p.puntos}</strong></div>`).join('')
            : '<p class="empty-state">Tus puntos aparecerán aquí cuando completes una actividad con recompensa.</p>';
        const next = progress.logros.filter(l => !l.obtenido)
            .sort((a, b) => (b.avance / Math.max(1, b.objetivo)) - (a.avance / Math.max(1, a.objetivo)))[0];
        const slot = document.getElementById('home-next-stamp');
        const pending = progress.logros.filter(l => !l.obtenido);
        const slots = document.getElementById('learning-slots');
        if (slots)
            slots.innerHTML = pending.map(l => `<article class="achievement is-slot" title="${esc(l.descripcion)}"><h3>${esc(l.nombre)}</h3><small>${Number(l.avance)} / ${Number(l.objetivo)}</small></article>`).join('');
        if (slot)
            slot.innerHTML = next
                ? `<span class="mini-stamp is-slot" title="${esc(next.descripcion)}"><b>${esc(next.nombre)}</b><small>${Number(next.avance)} / ${Number(next.objetivo)}</small></span>`
                : '';
        el('badge-goals').innerHTML = progress.logros.filter(l => !l.obtenido)
            .map(l => `<article class="badge-progress"><strong>${esc(l.nombre)}</strong><p>${esc(l.descripcion)} · ${l.avance} / ${l.objetivo}</p><progress value="${l.avance}" max="${l.objetivo}" aria-label="Avance hacia ${esc(l.nombre)}"></progress></article>`)
            .join('');
    } catch (e) {
        el('voluntary-activities').textContent = e.message;
        el('personal-risk').textContent = 'No se pudo consultar el progreso. Recarga para volver a intentar.';
    }
}

function material() {
    const sections = current.secciones || [];
    const s = sections[sectionIndex];
    el('activity-stage').innerHTML = s
        ? `<p class="eyebrow">SECCIÓN ${sectionIndex + 1} DE ${sections.length}</p><h3>${esc(s.titulo ?? s.Titulo)}</h3><p class="material-copy">${esc(s.contenido ?? s.Contenido)}</p><div class="material-nav"><button class="btn btn-ghost" data-prev-section ${sectionIndex === 0 ? 'disabled' : ''}>Anterior</button>${sectionIndex < sections.length - 1 ? '<button class="btn btn-primary" data-next-section>Siguiente sección</button>' : '<button class="btn btn-primary" data-open-quiz>Responder cuestionario</button>'}</div>`
        : '<p>No hay material disponible para este intento.</p>';
}

async function openActivity(id, attemptId) {
    if (busy) return;

    busy = true;
    el('activity-feedback').textContent = '';
    el('activity-stage').textContent = 'Preparando tu actividad…';
    dialog().showModal();
    try {
        current = attemptId
            ? await apiCallAuth(`${api}/intentos/${attemptId}`)
            : await apiCallAuth(`${api}/actividades/${id}/iniciar`, { method: 'POST' });
        if (!attemptId) dirty = true;
        el('activity-dialog-title').textContent = current.titulo;
        sectionIndex = 0;
        if (current.evaluado || current.finalizado) await result();
        else material();
    } catch (e) { el('activity-stage').textContent = e.message; } finally { busy = false; }
}

async function quiz() {
    const data = await apiCallAuth(`${api}/intentos/${current.idIntento}/cuestionario`, { method: 'POST' });
    el('activity-stage').innerHTML = `<p>Selecciona una opción por pregunta. Para aprobar necesitas ${current.aprobacion} %.</p><form id="activity-quiz-form">${questionMarkup(data.preguntas.map(p => ({ ...p, explicacion: '' })))}<button class="btn btn-primary" type="submit">Finalizar cuestionario</button></form>`;
}

async function result(data) {
    data ??= await apiCallAuth(`${api}/intentos/${current.idIntento}/resultado`);
    el('activity-stage').innerHTML = `<h3>${data.aprobado ? '¡Buen trabajo!' : 'Sigue practicando: cada intento enseña'}</h3><p><strong>${Number(data.porcentaje).toFixed(1)} %</strong> · ${data.aciertos} de ${data.totalPreguntas} respuestas correctas. Mínimo para aprobar: ${data.aprobacionConfigurada} %.</p>${data.respuestas.map(r => `<div class="quiz-review ${r.correcta ? '' : 'is-error'}"><strong>${esc(r.pregunta)}</strong><p>${r.correcta ? 'Respuesta correcta.' : `Para recordar: ${esc(r.respuestaCorrecta)}`}</p><p>${esc(r.explicacion)}</p></div>`).join('')}${data.finalizado ? `<p>Actividad finalizada · ${data.puntosObtenidos} puntos educativos.</p>` : '<button class="btn btn-primary" data-finish-activity>Finalizar actividad y guardar progreso</button>'}`;
}

export async function mountLearningJourney(onRefresh) {
    onChanged = onRefresh;
    enlazarPaginador(el('voluntary-activities'), 'actividades', p => {
        paginaActividades = p;
        refreshJourney();
    });

    document.addEventListener('click', async event => {
        const activity = event.target.closest('[data-activity]');
        if (activity) return openActivity(Number(activity.dataset.activity));

        const attempt = event.target.closest('[data-attempt]');
        if (attempt) return openActivity(null, Number(attempt.dataset.attempt));
        if (!event.target.closest('#activity-dialog') || busy) return;

        if (event.target.closest('[data-prev-section]')) {
            sectionIndex--;
            material();
        }

        if (event.target.closest('[data-next-section]')) {
            sectionIndex++;
            material();
        }

        if (event.target.closest('[data-open-quiz]')) {
            busy = true;
            try {
                await quiz();
            } catch (e) {
                el('activity-feedback').textContent = e.message;
            } finally {
                busy = false;
            }
        }

        if (event.target.closest('[data-finish-activity]')) {
            busy = true;
            try {
                const response = await apiCallAuth(`${api}/intentos/${current.idIntento}/finalizar`, { method: 'POST' });
                const saved = `Progreso guardado. ${response.puntosGanados ? `+${response.puntosGanados} puntos educativos.` : 'Esta actividad no otorgó puntos adicionales.'} ${response.insigniasDesbloqueadas?.length ? 'Nuevos logros: ' + response.insigniasDesbloqueadas.join(', ') : ''}`;
                // la actividad ya finalizo, las listas se actualizan aunque falle la vista del resultado
                try {
                    await result();
                    el('activity-feedback').textContent = saved;
                }
                catch {
                    el('activity-stage').innerHTML = '<p>Actividad finalizada. Podrás ver el detalle del resultado en tu historial.</p>';
                    el('activity-feedback').textContent = saved;
                }
                finally {
                    dirty = false;
                    await onChanged();
                }
            } catch (e) { el('activity-feedback').textContent = e.message; } finally { busy = false; }
        }
    });

    dialog().addEventListener('cancel', event => { if (busy) event.preventDefault(); });
    // un intento cerrado sin terminar debe aparecer en Continua donde lo dejaste
    dialog().addEventListener('close', () => {
        if (dirty) {
            dirty = false;
            onChanged();
        }
    });

    el('activity-stage').addEventListener('submit', async event => {
        if (event.target.id !== 'activity-quiz-form') return;

        event.preventDefault();
        if (busy || !event.target.reportValidity()) return;

        busy = true;
        try {
            const ids = [...event.target.querySelectorAll('input:checked')].map(i => Number(i.value));
            const data = await apiCallAuth(
                `${api}/intentos/${current.idIntento}/evaluar`,
                { method: 'POST', body: { idsOpcionesSeleccionadas: ids } });
            try {
                await result(data);
            } finally {
                dirty = false;
                await onChanged();
            }
        }
        catch (e) { el('activity-feedback').textContent = e.message; } finally { busy = false; }
    });

    await refreshJourney();
}
