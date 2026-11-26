import { apiCallAuth } from '../../services/api.js';
import { TokenStore } from '../../core/session.js';
import { el, escapeHtml as esc, formatDate, toast } from '../../utils/ui.js';
import { publish } from './home.js';
import { renderPaginador, enlazarPaginador, conPagina } from '../../components/paginacion.js';

const estados = { vencido: 'Vencida', por_vencer: 'Vence pronto', pendiente: 'Pendiente', al_dia: 'Al día' };
const clases = { vencido: 'is-overdue', por_vencer: 'is-soon', pendiente: '', al_dia: 'is-done' };
const escala = ['Muy en desacuerdo', 'En desacuerdo', 'Neutral', 'De acuerdo', 'Muy de acuerdo'];

async function ruta() {
    try {
        const r = await apiCallAuth('/aprendizaje/mi-ruta');
        publish('route', r);
        el('mi-ruta-resumen').textContent = r.total ? `${r.alDia} de ${r.total} al día` : '';
        el('mi-ruta-lista').innerHTML = r.total ? `<ul class="route-list">${r.items.map(i => `<li class="route-item ${clases[i.estado]}">
            <div><strong>${esc(i.actividad)}</strong><small>${esc(i.ruta)} · ${esc(i.tema)}${i.esInduccion ? ' · inducción' : ''}${i.frecuenciaDias ? ` · se renueva cada ${i.frecuenciaDias} días` : ''}</small></div>
            <div class="route-state"><span>${esc(estados[i.estado])}</span><small>${i.vence ? `${i.estado === 'al_dia' ? 'Próximo refresco' : 'Fecha límite'}: ${esc(formatDate(i.vence))}` : i.ultimaAprobacion ? `Aprobada el ${esc(formatDate(i.ultimaAprobacion))}` : ''}</small>
            ${i.estado !== 'al_dia' ? `<button class="btn btn-primary" type="button" data-activity="${Number(i.idActividad)}">Empezar</button>` : ''}</div></li>`).join('')}</ul>`
            : '<p class="empty-state">Tu área todavía no tiene una ruta obligatoria. Puedes aprender por iniciativa propia más abajo.</p>';
    } catch (e) { el('mi-ruta-lista').innerHTML = `<p class="empty-state">${esc(e.message)}</p>`; }
}

let paginaConstancias = 1;

async function constancias() {
    try {
        const data = await apiCallAuth(conPagina('/aprendizaje/constancias', paginaConstancias)), rows = data.items;
        el('constancias-lista').innerHTML = rows.length ? rows.map(c => `<div class="history-item"><div>${esc(c.actividad)}<small>${esc(c.tema)} · aprobada el ${esc(formatDate(c.fechaCompletado))}${c.porcentaje == null ? '' : ` · ${Number(c.porcentaje).toFixed(0)} %`}</small></div>
            ${c.codigo ? `<a class="text-action" href="constancia.html?codigo=${encodeURIComponent(c.codigo)}" target="_blank" rel="noopener">Ver constancia</a>` : `<button class="text-action" type="button" data-emitir="${Number(c.idIntento)}">Obtener constancia</button>`}</div>`).join('') + renderPaginador(data, 'constancias')
            : '<p class="empty-state">Cuando apruebes una actividad podrás descargar aquí su constancia.</p>';
    } catch (e) { el('constancias-lista').innerHTML = `<p class="empty-state">${esc(e.message)}</p>`; }
}

async function encuesta() {
    const box = el('encuesta-cultura');
    try {
        const e = await apiCallAuth('/aprendizaje/encuesta');
        publish('survey', Boolean(e));
        if (!e) {
            box.hidden = true;
            return;
        }

        box.innerHTML = `<div class="panel-heading"><div><p class="eyebrow">TU OPINIÓN CUENTA · ANÓNIMA</p><h2 id="encuesta-titulo">${esc(e.titulo)}</h2></div></div>
            <p>Unos 3 minutos. Tus respuestas no se vinculan con tu nombre y solo se informan agregadas en grupos de varias personas. Disponible hasta el ${esc(formatDate(e.fechaFin))}.</p>
            <form id="survey-form">${e.preguntas.map((p, n) => `<fieldset class="survey-question"><legend>${n + 1}. ${esc(p.texto)}</legend><div class="survey-scale">${escala.map((label, i) => `<label><input type="radio" name="${esc(p.id)}" value="${i + 1}" required><span>${i + 1}</span><small>${label}</small></label>`).join('')}</div></fieldset>`).join('')}
            <button class="btn btn-primary" type="submit">Enviar respuestas</button><p id="survey-feedback" role="status"></p></form>`;
        box.hidden = false;
        el('survey-form').addEventListener('submit', async event => {
            event.preventDefault();
            const button = event.submitter;
            button.disabled = true;
            const respuestas = Object.fromEntries(e.preguntas.map(p => [
                p.id,
                Number(event.target.querySelector(`input[name="${CSS.escape(p.id)}"]:checked`)?.value)
            ]));
            try {
                await apiCallAuth(`/aprendizaje/encuesta/${e.id}`, { method: 'POST', body: respuestas });
                box.innerHTML = '<h2>¡Gracias por responder!</h2><p>Tu opinión nos ayuda a mejorar el programa de seguridad.</p>';
                publish('survey', false);
                setTimeout(() => { box.hidden = true; }, 6000);
            } catch (err) {
                el('survey-feedback').textContent = err.message;
                button.disabled = false;
            }
        });
    } catch { box.hidden = true; }
}

// vuelve a consultar ruta y constancias sin recargar la pagina
export async function refreshProgram() {
    if (TokenStore.esGerente()) return;

    // la constancia recien obtenida aparece primero, se vuelve a la primera pagina
    paginaConstancias = 1;
    await Promise.all([ruta(), constancias()]);
}

export async function mountProgram() {
    // la encuesta y la ruta aplican solo al personal colaborador
    if (TokenStore.esGerente()) return;

    enlazarPaginador(el('constancias-lista'), 'constancias', p => {
        paginaConstancias = p;
        constancias();
    });

    el('constancias-lista').addEventListener('click', async event => {
        const b = event.target.closest('[data-emitir]');
        if (!b) return;

        b.disabled = true;
        try {
            const r = await apiCallAuth(`/aprendizaje/constancias/${Number(b.dataset.emitir)}`, { method: 'POST' });
            toast('Constancia emitida.');
            window.open(`constancia.html?codigo=${encodeURIComponent(r.codigo)}`, '_blank', 'noopener');
            await constancias();
        } catch (e) {
            toast(e.message, 'error');
            b.disabled = false;
        }
    });

    await Promise.all([ruta(), constancias(), encuesta()]);
}
