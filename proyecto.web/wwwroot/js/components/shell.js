import { TokenStore } from '../core/session.js';
import { apiCallAuth, cerrarSesion } from '../services/api.js';
import { escapeHtml as esc, toast } from '../utils/ui.js';
import { icon } from './icons.js';

// navegacion del administrador, cada pagina aparece una sola vez
const adminSections = [
    { id: 'general', label: 'General', links: [['dashboard.html', 'Centro de control', 'grid']] },
    { id: 'analisis-http', label: 'Análisis HTTP', links: [                      // HU-19 a HU-26
        ['http-analisis.html', 'Indicadores y agrupamientos', 'radar'],             // HU-24
        ['http-eventos.html', 'Comportamientos atípicos', 'alert'],                 // HU-23
        ['http-monitoreo.html', 'Monitoreo en tiempo real', 'clock'],               // HU-22
        ['http-ejecuciones.html', 'Análisis e historial', 'layers'],                // HU-21, HU-25
        ['http-comparacion.html', 'Comparación de periodos', 'activity'],           // HU-26
        ['http-fuentes.html', 'Conjuntos de registros', 'database'],                // HU-19
        ['http-modelo.html', 'Modelo de análisis', 'cpu'],                          // HU-20
        ['http-reportes.html', 'Reporte de un análisis', 'file'],
    ] },
    { id: 'simulaciones', label: 'Simulaciones de phishing', links: [            // HU-07 a HU-11
        ['campanias-phishing.html', 'Campañas y plantillas', 'mail'],               // HU-07, HU-08, HU-09
        ['resultados-campanias.html', 'Resultados de campañas', 'activity'],        // HU-10
        ['comparacion-evaluaciones.html', 'Comparación de evaluaciones', 'target'], // HU-11
        ['gobernanza.html', 'Política y exclusiones', 'settings'],
    ] },
    { id: 'concientizacion', label: 'Concientización', links: [                  // HU-12, HU-13, HU-15
        ['concientizacion-admin.html', 'Contenidos, puntos y logros', 'trophy'],    // HU-12, HU-15
        ['preguntas-admin.html', 'Cuestionarios', 'book'],                          // HU-13
        ['programa.html', 'Programa anual', 'clock'],
    ] },
    { id: 'prevencion', label: 'Prevención', links: [
        ['seguimiento-preventivo.html', 'Seguimiento preventivo', 'target'],        // HU-27 (consulta por sucursal)
        ['cyberscore.html', 'CyberScore de colaboradores', 'star'],
        ['http-ciclo.html', 'Decisiones preventivas', 'compass'],
        ['incidentes.html', 'Incidentes reportados', 'shield'],
    ] },
    { id: 'organizacion', label: 'Organización y accesos', links: [               // HU-03, HU-05, HU-06
        ['usuarios-roles.html', 'Usuarios y roles', 'users'], ['sucursales.html', 'Sucursales', 'building'],
        ['areas-operativas.html', 'Áreas operativas', 'layers'],
    ] },
    { id: 'control', label: 'Reportes y auditoría', links: [                      // HU-28, HU-29
        ['reportes.html', 'Reportes y exportación', 'file'], ['auditoria.html', 'Historial de operaciones', 'clock'],
    ] },
];
// paginas sin entrada propia se resaltan bajo su pagina principal
const parentOf = { 'http-patrones': 'http-eventos.html' };
// gerente (HU-27): solo informacion consolidada de su sucursal
const managerSections = [{ id: 'sucursal', label: 'Mi sucursal', links: [
    ['seguimiento-preventivo.html', 'Seguimiento preventivo', 'grid'],
    ['seguimiento-preventivo.html#areas-preventivas', 'Áreas operativas', 'target'],
    ['seguimiento-preventivo.html#campanias', 'Campañas de simulación', 'mail'],
    ['seguimiento-preventivo.html#actividades', 'Actividades de concientización', 'book'],
    ['cyberscore.html', 'CyberScore de la sucursal', 'star'],
] }];
const employeeSections = [{ id: 'aprendizaje', label: 'Mi espacio', links: [
    ['dashboard-usuario.html', 'Inicio', 'grid'], ['dashboard-usuario.html#aprender', 'Aprender', 'book'],
    ['dashboard-usuario.html#cyberscore', 'Mi CyberScore', 'shield'], ['dashboard-usuario.html#logros', 'Logros', 'trophy'],
    ['dashboard-usuario.html#reportar', 'Reportar mensajes', 'mail'],
] }];
const roleLabel = roles => roles.includes('Admin')
    ? 'Administrador'
    : roles.includes('GerenteSucursal')
    ? 'Gerente de sucursal'
    : 'Colaborador';
const PROFILE_KEY = 'account.profile';
const GROUPS_KEY = 'nav.collapsed';

const readStore = (store, key, fallback) => {
    try {
        return JSON.parse(store.getItem(key) ?? 'null') ?? fallback;
    } catch {
        return fallback;
    }
};

const writeStore = (store, key, value) => {
    try {
        store.setItem(key, JSON.stringify(value));
    } catch { /* preferencia opcional */ }
};

const initials = text => (text.trim().split(/\s+/).slice(0, 2).map(part => part[0]).join('') || '?').toUpperCase();

function isCurrent(href, page) {
    const [file, hash] = href.split('#');
    if (parentOf[page] === file) return true;
    if (file !== `${page}.html`) return false;

    return (hash ?? '') === location.hash.slice(1);
}

function renderSections(sections, page) {
    const collapsed = new Set(readStore(localStorage, GROUPS_KEY, []));
    return sections.map(section => {
        const active = section.links.some(([href]) => isCurrent(href, page));
        const links = section.links.map(([href, label, symbol]) =>
            `<a href="${href}" ${isCurrent(href, page) ? 'aria-current="page"' : ''}>${icon(symbol)}<span>${esc(label)}</span></a>`).join('');
        if (sections.length === 1 || section.links.length === 1)
            return `<div class="nav-group" data-module="${section.id}"><p class="nav-group-label">${esc(section.label)}</p>${links}</div>`;

        const open = active || !collapsed.has(section.id);
        return `<details class="nav-group" data-group="${section.id}" data-module="${section.id}" ${open ? 'open' : ''}><summary class="nav-group-label">${esc(section.label)}${icon('chevron')}</summary>${links}</details>`;
    }).join('');
}

function sectionOf(sections, page) {
    for (const section of sections) {
        const link = section.links.find(([href]) => isCurrent(href, page));
        if (link) return section.label;
    }

    return 'Mi cuenta';
}

function accountMarkup(profile, fallback, roles) {
    const name = profile ? `${profile.nombre} ${profile.apellido}`.trim() : fallback.name;
    const detail = profile?.email || fallback.email || roleLabel(roles);
    return `<span class="avatar" aria-hidden="true">${esc(initials(name))}</span><span class="account-text"><strong>${esc(name)}</strong><small>${esc(detail)}</small></span>`;
}

export function mountShell(page) {
    const gerente = TokenStore.esGerente();
    const admin = TokenStore.esAdmin() && page !== 'dashboard-usuario';
    const roles = TokenStore.roles();
    document.body.dataset.theme = admin || gerente ? 'admin' : 'employee';
    const sidebar = document.querySelector('[data-shell-sidebar]');
    const header = document.querySelector('[data-shell-header]');
    const identity = TokenStore.identity();
    const sections = admin ? adminSections : gerente ? managerSections : employeeSections;
    const cached = readStore(sessionStorage, PROFILE_KEY, null);
    const profile = cached?.id && cached.user === identity.name ? cached : null;

    const employee = !admin && !gerente;
    if (employee) {
        // colaborador: una sola barra superior, las pestanas del panel son su navegacion
        if (sidebar) {
            sidebar.innerHTML = '';
            sidebar.hidden = true;
        }

        if (header) {
            header.classList.add('employee-bar');
            const links = employeeSections[0].links.map(([href, label, symbol]) =>
                `<a href="${href}" ${isCurrent(href, page) ? 'aria-current="page"' : ''}>${icon(symbol)}<span>${esc(label)}</span></a>`).join('');
            header.innerHTML = `
                <a class="app-brand" href="${TokenStore.home()}"><span>DISMATEC</span></a>
                <div class="employee-nav" data-employee-nav></div>
                <div class="employee-account">
                    <a class="account-card" href="seguridad-mfa.html" ${page === 'seguridad-mfa' ? 'aria-current="page"' : ''} title="Mi cuenta" data-account-card>${accountMarkup(profile, identity, roles)}</a>
                    <button type="button" class="icon-button" data-logout aria-label="Cerrar sesión" title="Cerrar sesión">${icon('logout')}</button>
                </div>`;
            const slot = header.querySelector('[data-employee-nav]');
            const tabs = document.querySelector('.learn-tabs');
            if (tabs) slot.append(tabs);
            else slot.innerHTML = `<nav class="employee-links" aria-label="Navegación principal">${links}</nav>`;
        }
    } else {
        // el grupo de la pagina actual define el color del modulo
        document.body.dataset.module = sections.find(section => section.links.some(([href]) => isCurrent(href, page)))?.id ?? 'general';
        if (sidebar) sidebar.innerHTML = `
            <a class="app-brand" href="${TokenStore.home()}"><span>DISMATEC<small>${admin ? 'Centro de seguridad' : gerente ? 'Gestión de sucursal' : 'Aprende · Protege'}</small></span></a>
            <nav class="app-navigation" aria-label="Navegación principal">${renderSections(sections, page)}</nav>
            <div class="sidebar-account">
                <a class="account-card" href="seguridad-mfa.html" ${page === 'seguridad-mfa' ? 'aria-current="page"' : ''} title="Mi cuenta" data-account-card>${accountMarkup(profile, identity, roles)}</a>
                <button type="button" class="icon-button" data-logout aria-label="Cerrar sesión" title="Cerrar sesión">${icon('logout')}</button>
            </div>`;
        if (header) header.innerHTML = `
            <div class="topbar-context"><button class="menu-button" id="nav-toggle" aria-label="Abrir navegación" aria-expanded="false" aria-controls="app-sidebar">${icon('menu')}</button>
                <nav aria-label="Ruta"><span>${esc(sectionOf(sections, page))}</span>${icon('chevron')}<b>${esc(document.body.dataset.title ?? 'Mi cuenta')}</b></nav></div>
            <div class="topbar-account"><span class="workspace-pill">${esc(roleLabel(roles))}</span></div>`;
    }

    sidebar?.querySelectorAll('details[data-group]').forEach(group => group.addEventListener('toggle', () => {
        const collapsed = new Set(readStore(localStorage, GROUPS_KEY, []));
        if (group.open) collapsed.delete(group.dataset.group);
        else collapsed.add(group.dataset.group);
        writeStore(localStorage, GROUPS_KEY, [...collapsed]);
    }));

    if (sections.some(section => section.links.some(([href]) => href.startsWith(`${page}.html#`))))
        window.addEventListener('hashchange', () => sidebar?.querySelectorAll('.app-navigation a').forEach(link => {
            if (isCurrent(link.getAttribute('href'), page)) link.setAttribute('aria-current', 'page');
            else link.removeAttribute('aria-current');
        }));

    const toggle = document.getElementById('nav-toggle');
    const closeMenu = () => {
        document.getElementById('dash')?.classList.remove('nav-open');
        toggle?.setAttribute('aria-expanded', 'false');
    };

    toggle?.addEventListener('click', () => {
        const open = document.getElementById('dash').classList.toggle('nav-open');
        toggle.setAttribute('aria-expanded', String(open));
        if (open) sidebar?.querySelector('a')?.focus();
    });

    document.getElementById('dash-scrim')?.addEventListener('click', closeMenu);
    document.addEventListener('keydown', event => {
        if (!document.getElementById('dash')?.classList.contains('nav-open')) return;

        if (event.key === 'Escape') {
            closeMenu();
            toggle?.focus();
        }

        if (event.key === 'Tab') {
            const focusable = [...sidebar.querySelectorAll('a[href], button:not(:disabled), summary')];
            const first = focusable[0], last = focusable.at(-1);
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last?.focus();
            }
            else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first?.focus();
            }
        }
    });

    sidebar?.querySelectorAll('a').forEach(link => link.addEventListener('click', closeMenu));
    document.querySelectorAll('[data-logout]').forEach(button => button.addEventListener('click', async () => {
        button.disabled = true;
        try { sessionStorage.removeItem(PROFILE_KEY); } catch { /* sin almacenamiento */ }
        await cerrarSesion();
        window.location.replace('login.html');
    }));

    // datos reales de la cuenta, guardados solo durante la sesion
    if (!profile) apiCallAuth('/cuenta/perfil').then(data => {
        writeStore(sessionStorage, PROFILE_KEY, { ...data, user: identity.name });
        const card = document.querySelector('[data-account-card]');
        if (card) card.innerHTML = accountMarkup(data, identity, roles);
    }).catch(() => { /* se mantiene la identidad del token */ });

    if (page !== 'seguridad-mfa' && !page.startsWith('http-')) apiCallAuth('/mfa/estado').then(state => {
        if (state.authenticatorConfigurado) return;

        const notice = document.createElement('a');
        notice.className = 'account-notice';
        notice.href = 'seguridad-mfa.html';
        notice.innerHTML = `${icon('shield')}<span>Refuerza tu cuenta: configura tu app autenticadora.</span>${icon('arrow')}`;
        document.querySelector('.dash-content')?.prepend(notice);
    }).catch(() => toast('No se pudo comprobar la protección de tu cuenta.', 'error'));
}
