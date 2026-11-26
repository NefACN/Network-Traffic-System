import { apiCallAuth } from '../../services/api.js';
import { publish } from './home.js';
import { TokenStore, ReportNavigation } from '../../core/session.js';
import { el, escapeHtml as esc, formatDate } from '../../utils/ui.js';
import { renderPaginador, enlazarPaginador, conPagina } from '../../components/paginacion.js';

const incidentStates = { recibido: 'Recibido', en_analisis: 'En análisis', resuelto: 'Resuelto' };
const incidentResults = {
    malicioso: 'Mensaje malicioso confirmado',
    spam: 'Correo no deseado',
    legitimo: 'Mensaje legítimo',
    simulacion: 'Simulación de entrenamiento'
};

// reporte de mensajes reales con seguimiento del equipo de seguridad
function mountIncidentReports(reloadSimulations, onProgressRefresh) {
    const box = document.createElement('div');
    box.className = 'incident-report';
    box.innerHTML = `<h3>¿Recibiste un mensaje sospechoso real?</h3>
        <p class="panel-caption">Repórtalo aunque no estés seguro. No reenvíes el mensaje ni respondas al remitente. Si coincide con una simulación de entrenamiento, se registrará como tal.</p>
        <form id="incident-form" class="incident-form">
        <label>¿Por dónde llegó?<select id="incident-channel" required><option value="correo">Correo</option><option value="sms">SMS</option><option value="whatsapp">WhatsApp</option><option value="llamada">Llamada</option><option value="otro">Otro</option></select></label>
        <label>Remitente (correo, número o nombre)<input id="incident-sender" maxlength="200"></label>
        <label>Asunto o tema<input id="incident-subject" maxlength="200" required></label>
        <label>¿Qué hiciste con el mensaje?<select id="incident-interaction" required><option value="ninguna">Nada, solo lo vi</option><option value="clic">Hice clic en un enlace</option><option value="adjunto">Abrí un archivo adjunto</option><option value="datos">Escribí datos o contraseñas</option></select></label>
        <label class="incident-wide">¿Qué te pareció sospechoso?<textarea id="incident-description" maxlength="2000" rows="3" required></textarea></label>
        <div><button class="btn btn-primary" type="submit" id="incident-submit">Enviar reporte</button></div></form>
        <p id="incident-feedback" role="status" aria-live="polite"></p>
        <h3>Mis reportes de mensajes reales</h3><div id="incident-list"><p class="empty-state">Consultando…</p></div>`;
    el('mensajes-sospechosos').append(box);
    let pagina = 1;
    async function list() {
        try {
            const data = await apiCallAuth(conPagina('/incidentes/mios', pagina)), rows = data.items;
            el('incident-list').innerHTML = rows.length ? rows.map(r => `<article class="history-item incident-item"><div><strong>${esc(r.codigo)} · ${esc(r.asunto)}</strong>
                <small>${esc(formatDate(r.fechaCreacion))} · ${esc(incidentStates[r.estado] || r.estado)}${r.clasificacion ? ` · ${esc(incidentResults[r.clasificacion] || r.clasificacion)}` : ''}</small>
                ${r.respuesta ? `<p><strong>Respuesta del equipo de seguridad:</strong> ${esc(r.respuesta)}</p>` : ''}
                ${r.seguimiento.length ? `<details><summary>Seguimiento</summary><ul>${r.seguimiento.map(x => `<li>${esc(formatDate(x.fecha))} · ${esc(incidentStates[x.estadoNuevo] || x.estadoNuevo)}${x.nota ? `: ${esc(x.nota)}` : ''}</li>`).join('')}</ul></details>` : ''}</div></article>`).join('') + renderPaginador(data, 'incidentes-mios')
                : '<p class="empty-state">Aún no reportaste mensajes reales.</p>';
        } catch (error) { el('incident-list').innerHTML = `<p class="empty-state">${esc(error.message)}</p>`; }
    }

    el('incident-form').addEventListener('submit', async event => {
        event.preventDefault();
        const button = el('incident-submit');
        if (button.disabled) return;

        button.disabled = true;
        el('incident-feedback').textContent = 'Enviando…';
        try {
            const result = await apiCallAuth('/incidentes', {
                method: 'POST',
                body: {
                    canal: el('incident-channel').value,
                    remitente: el('incident-sender').value.trim() || null,
                    asunto: el('incident-subject').value.trim(),
                    descripcion: el('incident-description').value.trim(),
                    interaccion: el('incident-interaction').value
                } });
            el('incident-feedback').textContent = result.recomendacion ? `${result.mensaje} ${result.recomendacion}` : result.mensaje;
            event.target.reset();
            if (result.tipo === 'simulacion') {
                await reloadSimulations();
                await onProgressRefresh();
            } else {
                pagina = 1;
                await list();
            }
        } catch (error) { el('incident-feedback').textContent = error.message; } finally { button.disabled = false; }
    });

    enlazarPaginador(el('incident-list'), 'incidentes-mios', p => {
        pagina = p;
        list();
    });

    list();
}

export async function mountMessageReports(onProgressRefresh) {
    if (TokenStore.esAdmin() || TokenStore.esGerente()) {
        el('mensajes-sospechosos').hidden = true;
        return;
    }

    const endpoint = '/reportes-sospechosos';
    const dialog = document.createElement('dialog');
    dialog.id = 'report-dialog';
    dialog.className = 'modal learning-dialog';
    dialog.setAttribute('aria-labelledby', 'report-title');
    dialog.innerHTML = '<div class="modal-head"><h2 id="report-title">Reportar mensaje sospechoso</h2><button class="modal-close" type="button" data-report-close aria-label="Cerrar">×</button></div><div class="modal-body"><p id="report-subject"></p><p id="report-description"></p><p id="report-feedback" role="status" aria-live="polite"></p></div><div class="modal-foot"><button class="btn" type="button" data-report-close>Cancelar</button><button class="btn btn-primary" id="report-confirm" type="button" hidden>Confirmar reporte</button></div>';
    document.body.append(dialog);
    // HU-17: estado del reporte de una simulacion, distinto de incidentes reales
    const reportStatus = message => message.estadoReporte === 'valido' || (message.reportado && message.reporteSinClickPrevio)
        ? 'Válido: reportado sin abrir el enlace. Cuenta a favor de tu CyberScore.'
        : message.reportado ? 'Recibido: reportado después de abrir el enlace. Se considera en tu evaluación.' : '';
    let selected = null, sending = false, viewVersion = 0, pagina = 1;
    mountIncidentReports(load, onProgressRefresh);
    const confirm = el('report-confirm');
    function render(message) {
        selected = message;
        el('report-subject').textContent = `Asunto: ${message.asunto || 'Sin asunto'}`;
        el('report-description').textContent = message.mensaje;
        el('report-feedback').textContent = message.reportado
            ? `Simulación de capacitación · reporte registrado el ${formatDate(message.fechaReporte)}. Estado: ${reportStatus(message)}`
            : '';
        confirm.hidden = !message.puedeReportar;
        dialog.querySelector('.modal-foot [data-report-close]').textContent = message.puedeReportar ? 'Cancelar' : 'Cerrar';
    }

    async function load() {
        try {
            const data = await apiCallAuth(conPagina(endpoint, pagina));
            // si se reporto el ultimo mensaje de la pagina, se vuelve a la anterior
            if (!data.items.length && pagina > 1) {
                pagina--;
                return load();
            }

            const messages = data.items ?? [];
            // la lista de pendientes recibe todos los mensajes reportables, no solo los de esta pagina
            publish('messages', data.pendientes ?? messages);
            el('learning-messages').innerHTML = messages.length
                ? messages.map(m => `<article class="course-card message-card"><div><h3>${esc(m.asunto)}</h3><p>Recibido: ${esc(formatDate(m.fechaEnvio))}${m.reportado ? ` · Reportado: ${esc(formatDate(m.fechaReporte))}` : ''}</p>${m.reportado ? `<p class="report-status"><span class="pill">Simulación</span> ${esc(reportStatus(m))}</p>` : ''}</div><button type="button" class="btn ${m.reportado ? 'btn-ghost' : 'btn-primary'}" data-report-token="${esc(m.token)}">${m.reportado ? 'Ver reporte' : 'Reportar mensaje sospechoso'}</button></article>`).join('')
                    + renderPaginador(data, 'mensajes')
                : '<p class="empty-state">No tienes mensajes disponibles para reportar ni reportes registrados.</p>';
        } catch (error) {
            el('learning-messages').innerHTML = `<p class="empty-state">${esc(error.message)} <button class="text-action" type="button" data-report-retry>Volver a intentar</button></p>`;
        }
    }

    async function open(token) {
        if (sending) return;

        const version = ++viewVersion;
        selected = null;
        confirm.hidden = true;
        el('report-subject').textContent = '';
        el('report-description').textContent = 'Consultando el mensaje…';
        el('report-feedback').textContent = '';
        dialog.showModal();
        ReportNavigation.remember(`?reportar=${encodeURIComponent(token)}`);
        try {
            const data = await apiCallAuth(`${endpoint}/${encodeURIComponent(token)}`);
            if (version !== viewVersion) return;

            ReportNavigation.take();
            if (dialog.open) render(data);
        } catch (error) {
            if (version !== viewVersion) return;

            if (error.status !== 401) ReportNavigation.take();
            el('report-description').textContent = error.message;
        }
    }

    enlazarPaginador(el('learning-messages'), 'mensajes', p => {
        pagina = p;
        load();
    });

    el('learning-messages').addEventListener('click', event => {
        const report = event.target.closest('[data-report-token]');
        if (report) open(report.dataset.reportToken);
        if (event.target.closest('[data-report-retry]')) load();
    });

    dialog.querySelectorAll('[data-report-close]').forEach(button => button.addEventListener('click', () => {
        if (!sending) dialog.close();
    }));

    dialog.addEventListener('cancel', event => { if (sending) event.preventDefault(); });
    dialog.addEventListener('close', () => {
        viewVersion++;
        selected = null;
    });

    confirm.addEventListener('click', async () => {
        if (sending || !selected?.puedeReportar) return;

        sending = true;
        confirm.disabled = true;
        dialog.querySelectorAll('[data-report-close]').forEach(button => { button.disabled = true; });
        try {
            const result = await apiCallAuth(
                `${endpoint}/${encodeURIComponent(selected.token)}`,
                { method: 'POST', body: { confirmar: true } });
            render({
                ...selected,
                puedeReportar: false,
                reportado: true,
                fechaReporte: result.fechaReporte,
                reporteSinClickPrevio: result.reporteSinClickPrevio,
                estadoReporte: result.reporteSinClickPrevio ? 'valido' : 'tras_clic',
                mensaje: result.mensaje
            });

            if (result.insigniasDesbloqueadas?.length)
                el('report-feedback').textContent += ` ¡Nuevo logro: ${result.insigniasDesbloqueadas.join(', ')}!`;
            await load();
            await onProgressRefresh();
        } catch (error) {
            el('report-feedback').textContent = error.message;
            // ante una desconexion se reconcilia con el servidor antes de otro intento
            try {
                const actual = await apiCallAuth(`${endpoint}/${encodeURIComponent(selected.token)}`);
                if (actual.reportado || !actual.puedeReportar) {
                    render(actual);
                    await load();
                    await onProgressRefresh();
                }
            } catch { /* la confirmacion puede reintentarse explicitamente */ }
        } finally {
            sending = false;
            confirm.disabled = false;
            dialog.querySelectorAll('[data-report-close]').forEach(button => { button.disabled = false; });
        }
    });

    const url = new URL(location.href), token = url.searchParams.get('reportar');
    if (token != null) {
        url.searchParams.delete('reportar');
        history.replaceState(null, '', url.pathname + url.search + '#mensajes-sospechosos');
    }

    await load();
    if (token && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(token)) await open(token);
    // permite volver a consultar los mensajes al regresar a la pagina
    return load;
}
