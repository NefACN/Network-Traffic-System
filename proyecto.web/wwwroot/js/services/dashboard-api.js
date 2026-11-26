import { apiCallAuth } from './api.js';
import { conPagina, TAMANIO_PAGINA } from '../components/paginacion.js';

// recorre todas las paginas de un listado para agregados que necesitan el conjunto completo
async function todas(ruta) {
    const items = [];
    for (let pagina = 1; ; pagina++) {
        const data = await apiCallAuth(conPagina(ruta, pagina, 100));
        items.push(...data.items);
        if (!data.items.length || items.length >= data.total) return items;
    }
}

export const LearningApi = {
    reinforcements: () => apiCallAuth('/dashboard/mis-refuerzos'),
    profile: () => apiCallAuth('/dashboard/mi-perfil'), courses: () => apiCallAuth('/dashboard/cursos-disponibles'),
    ranking: () => apiCallAuth('/dashboard/ranking-sucursales'),
    history: (pagina = 1) => apiCallAuth(conPagina('/dashboard/mi-historial', pagina, TAMANIO_PAGINA)),
    start: id => apiCallAuth(`/dashboard/cursos/${Number(id)}/iniciar`, { method: 'POST' }),
    complete: (id, answers) => apiCallAuth(
        `/dashboard/cursos/${Number(id)}/completar`,
        { method: 'POST', body: { idsOpcionesSeleccionadas: answers } }),
};

export const AuditApi = {
    list(filters = {}, signal) {
        const params = new URLSearchParams(Object.entries(filters).filter(([, value]) => value !== '' && value != null));
        return apiCallAuth(`/auditoria?${params}`, { signal });
    },
};

export const AdminApi = {
    users: () => apiCallAuth('/usuarios/catalogo'), branches: () => apiCallAuth('/sucursales'),
    campaigns: () => apiCallAuth('/campanias-phishing/catalogo'), campaign: id => apiCallAuth(`/campanias-phishing/${Number(id)}`),
};

export const PreventionApi = {
    areas: () => apiCallAuth('/cyberscore/areas'),
    // el centro de control cuenta todas las sugerencias pendientes
    pendingSuggestions: () => todas('/ciclo-http/sugerencias?estado=pendiente'),
};
