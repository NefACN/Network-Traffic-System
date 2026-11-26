import { apiCallAuth } from '../services/api.js';
import { el, escapeHtml as esc } from '../utils/ui.js';

// seccion mi cuenta: perfil y actividad reciente
const roleNames = { Admin: 'Administrador', GerenteSucursal: 'Gerente de sucursal', Colaborador: 'Colaborador' };
const actionNames = {
    Login: 'Inicio de sesión',
    LoginFallido: 'Intento de inicio fallido',
    CambioPassword: 'Cambio de contraseña',
    Logout: 'Cierre de sesión'
};

const longDate = value => value
    ? new Date(value).toLocaleString('es-BO', { day: '2-digit', month: 'long', year: 'numeric', hour: '2-digit', minute: '2-digit' })
    : '—';

export async function loadAccountProfile() {
    try {
        const profile = await apiCallAuth('/cuenta/perfil');
        const name = `${profile.nombre} ${profile.apellido}`.trim() || profile.userName || 'Mi cuenta';
        el('perfil-avatar').textContent = name.split(/\s+/).slice(0, 2).map(part => part[0]).join('').toUpperCase();
        el('perfil-titulo').textContent = name;
        el('perfil-email').textContent = profile.email ?? '';
        el('perfil-roles').textContent = profile.roles.length
            ? profile.roles.map(role => roleNames[role] ?? role).join(', ')
            : 'Sin rol asignado';
        el('perfil-usuario').textContent = profile.userName ?? '—';
        el('perfil-sucursal').textContent = profile.sucursal ?? 'Sin asignar';
        el('perfil-area').textContent = profile.area ?? 'Sin asignar';
        el('perfil-creada').textContent = longDate(profile.fechaCreacion);
        el('perfil-ultimo').textContent = profile.ultimoAcceso ? longDate(profile.ultimoAcceso) : 'Primer acceso registrado';
        el('perfil-actividad').innerHTML = profile.actividadReciente.length ? profile.actividadReciente.map(event => `
            <li class="${event.exitoso ? '' : 'is-failed'}"><span><strong>${esc(actionNames[event.accion] ?? event.accion)}</strong>
            <small>${esc(longDate(event.fechaHora))}${event.ipOrigen ? ` · IP ${esc(event.ipOrigen)}` : ''}</small></span>
            <span class="pill">${event.exitoso ? 'Correcto' : 'Fallido'}</span></li>`).join('')
            : '<li class="field-hint">Todavía no hay actividad registrada.</li>';
    } catch (error) {
        el('perfil-titulo').textContent = 'No se pudo cargar tu perfil';
        el('perfil-email').textContent = error?.message ?? 'Recarga la página para volver a intentar.';
        el('perfil-actividad').innerHTML = '<li class="field-hint">Actividad no disponible.</li>';
    }
}
