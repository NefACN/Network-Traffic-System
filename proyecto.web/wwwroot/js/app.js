import { TokenStore, ReportNavigation } from './core/session.js';
import { intentarRenovarSesion } from './services/api.js';
import { installDialogControls, installPasswordControls, toast } from './utils/ui.js';
import { mountShell } from './components/shell.js';
import { installAppearance } from './core/appearance.js';
const page = document.body.dataset.page;
const httpPages = new Set([
    'http-analisis',
    'http-fuentes',
    'http-ejecuciones',
    'http-monitoreo',
    'http-eventos',
    'http-reportes',
    'http-comparacion',
    'http-modelo'
]);
const publicPages = new Set(['login', 'recuperar-password', 'phishing-educacion']);
document.addEventListener('session:expired', () => {
    if (!publicPages.has(page)) window.location.replace('login.html');
});

async function start() {
    if (page === 'dashboard-usuario') ReportNavigation.remember(location.search);
    installDialogControls();
    installPasswordControls();
    if (!publicPages.has(page)) {
        if (!TokenStore.isValid() && !await intentarRenovarSesion()) {
            window.location.replace('login.html');
            return;
        }

        if (!TokenStore.puedeAbrir(page)) {
            window.location.replace(TokenStore.home());
            return;
        }

        mountShell(page);
        const identity = TokenStore.identity();
        installAppearance(TokenStore.getDecodedToken()?.sub || identity.email || identity.name);
    } else if (page === 'login' && TokenStore.isValid()) {
        window.location.replace(ReportNavigation.take() || TokenStore.home());
        return;
    }

    document.body.classList.add('app-ready');
    if (httpPages.has(page)) {
        await import('./features/http/controller.js');
        return;
    }

    const controllers = {
        reportes: () => import('./controllers/reportes-auditoria.js'),
        auditoria: () => import('./controllers/reportes-auditoria.js'),
        'resultados-campanias': () => import('./controllers/resultados-preventivos.js'),
        'comparacion-evaluaciones': () => import('./controllers/resultados-preventivos.js'),
        'concientizacion-admin': () => import('./controllers/concientizacion-admin.js'),
        gobernanza: () => import('./controllers/gobernanza.js'),
        programa: () => import('./controllers/programa.js'),
        'http-ciclo': () => import('./controllers/http-ciclo.js'),
        incidentes: () => import('./controllers/incidentes.js'),
        'seguimiento-preventivo': () => import('./controllers/seguimiento-preventivo.js'),
        cyberscore: () => import('./controllers/cyberscore.js'),
        dashboard: () => import('./controllers/dashboard.js'), 'dashboard-usuario': () => import('./controllers/dashboard-usuario.js'),
        'usuarios-roles': () => import('./controllers/usuarios-roles.js'), sucursales: () => import('./controllers/sucursales.js'),
        'areas-operativas': () => import('./controllers/areas-operativas.js'),
        'campanias-phishing': () => import('./controllers/campanias-phishing.js'),
        'preguntas-admin': () => import('./controllers/preguntas-admin.js'),
        'seguridad-mfa': () => import('./controllers/seguridad-mfa.js'),
        login: () => import('./controllers/login.js'), 'recuperar-password': () => import('./controllers/recuperar-password.js'),
        'phishing-educacion': () => import('./controllers/phishing-educacion.js'),
    };

    await controllers[page]?.();
}

start().catch(error => {
    document.body.classList.add('app-ready');
    toast(error.message ?? 'No se pudo iniciar esta pantalla. Recarga para volver a intentar.', 'error');
    console.error('No se pudo iniciar la pantalla', error);
});
