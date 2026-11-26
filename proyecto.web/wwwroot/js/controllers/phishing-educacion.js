import { apiCall } from '../services/api.js';
import { el, escapeHtml as esc } from '../utils/ui.js';
import { questionMarkup } from '../components/quiz.js';
import { mountInspector, mountUrlAnatomy } from '../features/phishing/inspector.js';

const token = new URLSearchParams(location.search).get('token');
const loading = el('pantalla-carga'), error = el('pantalla-error'), success = el('pantalla-exito'), content = el('modulo-concientizacion');
const questions = el('contenedor-preguntas');
let count = 0;

function showError(message) {
    loading.classList.remove('visible');
    content.style.display = 'none';
    el('msg-error-texto').textContent = message;
    error.classList.add('visible');
}

if (!token || !/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(token)) {
    showError('El enlace no contiene un identificador válido. Utiliza el enlace original de tu capacitación.');
} else {
    try {
        const data = await apiCall(`/concientizacion/rastrear/${encodeURIComponent(token)}`, { method: 'GET' });
        loading.classList.remove('visible');
        content.style.display = 'block';
        if (data.ingresoCredenciales) {
            const aviso = document.createElement('p');
            aviso.className = 'aviso-credenciales';
            aviso.textContent = 'Además, enviaste el formulario de inicio de sesión de la página falsa. Lo que escribiste no se guardó ni salió de tu navegador. '
                + 'Si usaste tu contraseña real, cámbiala ahora desde Mi cuenta y activa la verificación en dos pasos. '
                + 'Antes de escribir una clave, revisa siempre la dirección del sitio en la barra del navegador.';
            content.querySelector('.alert-box').append(aviso);
        }

        // la persona investiga el correo primero y despues se explica lo que se le paso
        const senales = Array.isArray(data.senales) ? data.senales : [];
        if (senales.length) {
            el('senales-dificultad').textContent = `Dificultad de detección de este mensaje (NIST Phish Scale): ${data.dificultad}.`;
            el('lista-senales').innerHTML = senales.map(s => `<li><strong>${esc(s.nombreTipo)}:</strong> ${esc(s.descripcion)}${s.fragmento ? ` <q>${esc(s.fragmento)}</q>` : ''}</li>`).join('');
        }

        el('senales-mensaje').hidden = true; // sin adelantar las respuestas
        const ensenanza = data.ensenanza ?? null;
        let resultado = null;
        const enviar = async (anatomia) => {
            try {
                const r = await apiCall(
                    `/concientizacion/inspeccion/${encodeURIComponent(token)}`,
                    { body: { ...resultado, aciertosEnlace: anatomia.aciertos, preguntasEnlace: anatomia.total } });
                const box = el('inspeccion-puntos');
                if (r.puntosGanados > 0) {
                    box.innerHTML = `<strong>+${Number(r.puntosGanados)} puntos educativos</strong> por encontrar todas las señales.${r.insigniasDesbloqueadas?.length ? ` Nuevo logro: ${esc(r.insigniasDesbloqueadas.join(', '))}.` : ''}`;
                    box.hidden = false;
                }
            } catch { /* el registro es opcional, la capacitacion continua aunque falle */ }
        };

        mountInspector(el('inspector-correo'), { asunto: data.asunto, html: data.cuerpoOriginal, senales, ensenanza,
            onReveal: r => {
                resultado = r;
                if (senales.length) el('senales-mensaje').hidden = false;
                if (data.inspeccion) {
                    el('anatomia-enlace').hidden = true;
                    return;
                } // ya registrada en una visita anterior
                el('anatomia-enlace').hidden = false;
                mountUrlAnatomy(el('anatomia-enlace'), {
                    url: ensenanza?.enlaceMostrado,
                    remitenteNombre: ensenanza?.remitenteNombre,
                    onDone: enviar
                });

                el('anatomia-enlace').scrollIntoView({ behavior: 'smooth', block: 'start' });
            } });
        const micro = data.microleccion;
        if (micro) {
            el('microleccion-asignada').innerHTML = `<h3>Tu microlección (menos de 2 minutos)</h3><p>Te asignamos <strong>${esc(micro.titulo)}</strong>${micro.motivo === 'credenciales' ? ', enfocada en proteger tus credenciales' : ''}. ${micro.completada ? 'Ya la completaste. ¡Bien hecho!' : 'La encontrarás primero en <em>Mi aprendizaje</em> al iniciar sesión.'}</p>${micro.completada ? '' : '<a class="btn-enviar" href="/dashboard-usuario.html">Ir a Mi aprendizaje</a>'}`;
            el('microleccion-asignada').hidden = false;
        }

        if (data.planRefuerzoAsignado > 0) {
            // CyberScore v3: la caida llevo a nivel Critico y se asigno un plan de refuerzo
            el('microleccion-asignada').insertAdjacentHTML(
                'beforeend',
                `<p><strong>Plan de refuerzo:</strong> te asignamos ${Number(data.planRefuerzoAsignado)} microlecciones adicionales. Completarlas recupera tu CyberScore y suma puntos.</p>`);
            el('microleccion-asignada').hidden = false;
        }

        count = data.preguntas?.length ?? 0;
        questions.innerHTML = count
            ? questionMarkup(data.preguntas)
            : '<p class="empty-state">Esta capacitación aún no tiene preguntas configuradas.</p>';
        el('btn-evaluar').disabled = !count;
    } catch (failure) { showError(failure.message); }
}

el('form-cuestionario').addEventListener('submit', async event => {
    event.preventDefault();
    const button = el('btn-evaluar'), feedback = el('mensaje-feedback');
    const answers = [...questions.querySelectorAll('input:checked')].map(input => Number(input.value));
    if (!count || answers.length !== count || button.disabled) return;

    button.disabled = true;
    button.textContent = 'Comprobando…';
    feedback.textContent = '';
    try {
        const data = await apiCall(
            `/concientizacion/completar/${encodeURIComponent(token)}`,
            { body: { idsOpcionesSeleccionadas: answers } });
        content.style.display = 'none';
        success.classList.add('visible');
        el('msg-exito-texto').innerHTML = `${esc(data.mensaje)}<br><strong>+${Number(data.puntosGanados ?? 0)} puntos educativos</strong>${data.insigniasDesbloqueadas?.length ? `<p>Nuevas insignias: ${esc(data.insigniasDesbloqueadas.join(', '))}</p>` : ''}`;
    } catch (failure) {
        feedback.textContent = failure.message;
        feedback.className = 'feedback error';
        if (failure.status === 400) questions.querySelectorAll('.question-explanation').forEach(node => node.classList.remove('hidden'));
        button.disabled = false;
        button.textContent = 'Comprobar respuestas';
    }
});
