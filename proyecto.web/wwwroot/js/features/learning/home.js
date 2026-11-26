// inicio del colaborador: combina eventos learning:* en una lista priorizada sin consultar la API
import { el, escapeHtml as esc, formatDate } from '../../utils/ui.js';

const TABS = ['inicio', 'aprender', 'cyberscore', 'logros', 'reportar'];
// enlaces anteriores (menu, correos, marcadores) siguen funcionando
const ALIAS = { 'actividades-voluntarias': 'aprender', cursos: 'aprender', 'mi-ruta': 'aprender', 'mi-progreso': 'cyberscore',
    logros: 'logros', 'mis-constancias': 'logros', 'mensajes-sospechosos': 'reportar'
};

const LEVEL_CLASS = { Bajo: 'is-low', Medio: 'is-medium', Alto: 'is-high', 'Crítico': 'is-critical' };
const data = { reinforcements: null, activities: null, progress: null, route: null, messages: null, survey: false };

export function showTab(name, { focus = false, scrollTo, scroll = true } = {}) {
    const tab = TABS.includes(name) ? name : 'inicio';
    for (const t of TABS) {
        const selected = t === tab, button = el(`tab-btn-${t}`);
        button.setAttribute('aria-selected', String(selected));
        button.tabIndex = selected ? 0 : -1;
        el(`tab-${t}`).hidden = !selected;
    }

    keepTabInView(el(`tab-btn-${tab}`));
    if (focus) el(`tab-btn-${tab}`).focus();
    const target = scrollTo && document.getElementById(scrollTo);
    if (target) target.scrollIntoView?.({ block: 'start' });
    else if (scroll) document.querySelector('.learn-tabs').scrollIntoView?.({ block: 'nearest' });
    // conserva la consulta (p. ej. ?reportar=) y solo cambia el fragmento
    try {
        history.replaceState(history.state, '', `${location.pathname}${location.search}${tab === 'inicio' ? '' : `#${tab}`}`);
    } catch { }
}

// en pantallas estrechas mantiene visible la pestana elegida e indica si hay mas
function keepTabInView(button) {
    const nav = button.closest('.learn-tabs');
    if (!nav || nav.scrollWidth <= nav.clientWidth) return;

    const left = button.offsetLeft, right = left + button.offsetWidth;
    if (left < nav.scrollLeft) nav.scrollLeft = Math.max(0, left - 16);
    else if (right > nav.scrollLeft + nav.clientWidth) nav.scrollLeft = right - nav.clientWidth + 16;
    updateTabEdges(nav);
}

function updateTabEdges(nav) {
    nav.classList.toggle('has-more-start', nav.scrollLeft > 4);
    nav.classList.toggle('has-more-end', nav.scrollLeft + nav.clientWidth < nav.scrollWidth - 4);
}

function fromHash() {
    const hash = decodeURIComponent(location.hash.slice(1));
    if (!hash && new URLSearchParams(location.search).has('reportar')) {
        showTab('reportar', { scroll: false });
        return;
    }

    // al cargar no se desplaza la pagina salvo para enlaces antiguos a una seccion
    if (TABS.includes(hash)) showTab(hash, { scroll: false });
    else if (ALIAS[hash]) showTab(ALIAS[hash], { scrollTo: hash });
    else showTab('inicio', { scroll: false });
}

function scoreCard(v) {
    const box = el('home-score');
    if (!v) {
        box.innerHTML = '<p class="empty-state">Tu CyberScore aún no está disponible.</p>';
        return;
    }

    const value = v.cyberScore ?? v.valor, evaluable = v.evaluable && value != null;
    box.className = `panel home-score ${evaluable ? LEVEL_CLASS[v.nivel] ?? '' : 'is-none'}`;
    box.innerHTML = `<p class="eyebrow">MI CYBERSCORE</p>
        <div class="home-score-body"><div class="cyberscore-gauge" style="--score:${evaluable ? Number(value) : 0}" role="img" aria-label="${evaluable ? `CyberScore ${Number(value).toFixed(0)} de 100` : 'Sin evaluación suficiente'}"><strong>${evaluable ? Number(value).toFixed(0) : '—'}</strong><span>de 100</span></div>
        <div><h2>${evaluable ? `Riesgo ${esc(v.nivel)}` : 'Aún sin evaluación'}</h2><p>${evaluable
            ? (v.nivel === 'Bajo' ? 'Reconoces bien los engaños. ¡Sigue así!' : 'Completa tus refuerzos y reporta los mensajes sospechosos para subir.')
            : `Necesitas ${v.minimoSimulaciones} simulaciones recientes; llevas ${v.simulaciones}.`}</p>
        <button class="text-action" type="button" data-go-tab="cyberscore">Ver qué lo movió</button></div></div>`;
}

// une todos los pendientes ordenados por urgencia
function todo() {
    const items = [];
    for (const a of (data.activities ?? []).filter(x => x.asignada && x.puedeIniciar !== false))
        items.push({ prioridad: a.motivoAsignacion === 'critico' ? 0 : 1, icono: 'is-urgent', titulo: a.titulo,
            detalle: a.motivoAsignacion === 'critico'
                ? 'Plan de refuerzo: sube tu CyberScore'
                : a.motivoAsignacion === 'sugerencia'
                ? 'Asignada a tu sucursal'
                : 'Asignada tras una simulación',
            accion: `<button class="btn btn-primary" type="button" data-activity="${Number(a.id)}">${a.idIntento ? 'Continuar' : 'Empezar'}</button>`
        });
    for (const r of data.reinforcements ?? [])
        items.push({ prioridad: 1, icono: 'is-urgent', titulo: `Refuerzo: ${r.tema}`, detalle: `Tras la simulación «${r.campania}»`,
            accion: `<a class="btn btn-primary" href="${esc(r.enlace)}">Empezar</a>`
        });
    for (const i of (data.route?.items ?? []).filter(x => x.estado !== 'al_dia'))
        items.push({
            prioridad: i.estado === 'vencido' ? 1 : i.estado === 'por_vencer' ? 2 : 4,
            icono: i.estado === 'vencido' ? 'is-urgent' : '',
            titulo: i.actividad,
            detalle: `Ruta de tu puesto · ${i.estado === 'vencido' ? 'vencida' : i.estado === 'por_vencer' ? 'vence pronto' : 'pendiente'}${i.vence ? ` · ${formatDate(i.vence)}` : ''}`,
            accion: `<button class="btn" type="button" data-activity="${Number(i.idActividad)}">Empezar</button>`
        });
    const pendientes = (data.messages ?? []).filter(m => m.puedeReportar);
    if (pendientes.length)
        items.push({
            prioridad: 3,
            icono: '',
            titulo: `${pendientes.length === 1 ? 'Un mensaje puede' : `${pendientes.length} mensajes pueden`} reportarse`,
            detalle: '¿Te pareció sospechoso? Reportarlo suma puntos y mejora tu CyberScore',
            accion: '<button class="btn" type="button" data-go-tab="reportar">Revisar</button>'
        });

    for (const a of data.progress?.enCurso ?? [])
        items.push({
            prioridad: 3,
            icono: '',
            titulo: a.titulo,
            detalle: a.evaluado ? 'Ya respondiste: falta guardar el resultado' : 'Actividad sin terminar',
            accion: `<button class="btn" type="button" data-attempt="${Number(a.idIntento)}">Continuar</button>`
        });
    if (data.survey)
        items.push({
            prioridad: 5,
            icono: '',
            titulo: 'Encuesta de cultura de seguridad',
            detalle: 'Anónima · unos 3 minutos',
            accion: '<a class="btn btn-ghost" href="#encuesta-cultura">Responder</a>'
        });
    // sin duplicados, una actividad asignada puede estar tambien en la ruta
    const seen = new Set();
    const list = items.sort((a, b) => a.prioridad - b.prioridad).filter(i => {
        const key = i.accion.match(/data-activity="(\d+)"/)?.[1];
        if (!key) return true;
        if (seen.has(key)) return false;

        seen.add(key);
        return true;
    });

    el('home-todo-count').textContent = list.length ? `${list.length} pendiente${list.length === 1 ? '' : 's'}` : '';
    // solo la primera fila (mas urgente) usa boton primario, el resto va con contorno
    const asAction = (accion, index) => accion.replace(
        /class="btn(?: btn-primary| btn-ghost)?"/,
        `class="btn ${index === 0 ? 'btn-primary' : 'btn-ghost'}"`);
    el('home-todo').innerHTML = list.length
        ? list.slice(0, 6)
            .map((i, index) => `<li class="todo-item ${i.icono}"><div><strong>${esc(i.titulo)}</strong><small>${esc(i.detalle)}</small></div>${asAction(i.accion, index)}</li>`)
            .join('')
        + (list.length > 6 ? `<li class="todo-more"><button class="text-action" type="button" data-go-tab="aprender">Ver ${list.length - 6} más</button></li>` : '')
        : `<li class="todo-done"><strong>¡Estás al día!</strong><small>No tienes pendientes. Explora una actividad nueva o practica un tema.</small><button class="btn" type="button" data-go-tab="aprender">Explorar actividades</button></li>`;
    const assigned = (data.activities ?? []).filter(a => a.asignada).length + (data.reinforcements?.length ?? 0);
    el('tab-count-aprender').hidden = !assigned;
    el('tab-count-aprender').textContent = assigned;
    el('tab-count-reportar').hidden = !pendientes.length;
    el('tab-count-reportar').textContent = pendientes.length;
}

export function mountHome() {
    document.querySelector('.learn-tabs').addEventListener('click', event => {
        const b = event.target.closest('[data-tab]');
        if (b) showTab(b.dataset.tab);
    });

    // navegacion con flechas entre pestanas (patron WAI-ARIA)
    document.querySelector('.learn-tabs').addEventListener('keydown', event => {
        if (!['ArrowRight', 'ArrowLeft', 'Home', 'End'].includes(event.key)) return;

        const current = TABS.findIndex(t => el(`tab-btn-${t}`).getAttribute('aria-selected') === 'true');
        const next = event.key === 'Home'
            ? 0
            : event.key === 'End'
            ? TABS.length - 1
            : (current + (event.key === 'ArrowRight' ? 1 : -1) + TABS.length) % TABS.length;
        event.preventDefault();
        showTab(TABS[next], { focus: true });
    });

    document.addEventListener('click', event => {
        const b = event.target.closest('[data-go-tab]');
        if (b) {
            event.preventDefault();
            showTab(b.dataset.goTab);
        }
    });

    // enlaces internos antiguos (#mi-progreso, #cursos) abren su pestana
    document.addEventListener('click', event => {
        const a = event.target.closest('a[href^="#"], a[href^="dashboard-usuario.html#"]');
        if (!a) return;

        const hash = a.getAttribute('href').split('#')[1];
        if (hash === 'encuesta-cultura') {
            event.preventDefault();
            showTab('inicio', { scrollTo: hash });
            return;
        }

        if (TABS.includes(hash) || ALIAS[hash]) {
            event.preventDefault();
            showTab(ALIAS[hash] ?? hash, { scrollTo: ALIAS[hash] ? hash : undefined });
        }
    });

    window.addEventListener('hashchange', fromHash);
    const tabsNav = document.querySelector('.learn-tabs');
    tabsNav.addEventListener('scroll', () => updateTabEdges(tabsNav), { passive: true });
    window.addEventListener('resize', () => updateTabEdges(tabsNav));
    const on = (name, key, transform = x => x) => document.addEventListener(`learning:${name}`, e => {
        data[key] = transform(e.detail);
        if (key === 'progress') scoreCard(e.detail?.cyberScoreV2);
        todo();
    });

    on('reinforcements', 'reinforcements');
    on('activities', 'activities');
    on('progress', 'progress');
    on('route', 'route');
    on('messages', 'messages');
    on('survey', 'survey', Boolean);
    fromHash();
}

export const publish = (name, detail) => document.dispatchEvent(new CustomEvent(`learning:${name}`, { detail }));
