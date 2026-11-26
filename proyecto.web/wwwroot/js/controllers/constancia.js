// constancia imprimible con codigo y QR, solo para su titular o un administrador
import { apiCallAuth } from '../services/api.js';
import { escapeHtml as esc } from '../utils/ui.js';
import qrcode from '../vendor/qrcode.js';

const box = document.getElementById('constancia');
const codigo = new URLSearchParams(location.search).get('codigo') || '';
const fecha = v => new Date(v).toLocaleDateString('es-BO', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'America/La_Paz' });

function qr(texto) {
    const q = qrcode(0, 'M');
    q.addData(texto);
    q.make();
    const canvas = document.createElement('canvas'), ctx = canvas.getContext('2d');
    const margin = 2, count = q.getModuleCount(), cell = 4;
    canvas.width = canvas.height = (count + margin * 2) * cell;
    ctx.fillStyle = '#fff';
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    ctx.fillStyle = '#000';
    for (let r = 0; r < count; r++)
        for (let c = 0; c < count; c++) if (q.isDark(r, c)) ctx.fillRect((c + margin) * cell, (r + margin) * cell, cell, cell);
    canvas.setAttribute('role', 'img');
    canvas.setAttribute('aria-label', 'Código QR para verificar esta constancia');
    return canvas;
}

if (!/^DSM-[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$/i.test(codigo)) {
    box.innerHTML = '<p class="status">El enlace no contiene un código de constancia válido.</p>';
} else {
    try {
        const c = await apiCallAuth(`/constancias/${encodeURIComponent(codigo)}`);
        const verificacion = `${location.origin}/verificar-constancia.html?codigo=${encodeURIComponent(c.codigo)}`;
        document.title = `Constancia ${c.codigo} · DISMATEC`;
        box.innerHTML = `<p class="brand">DISMATEC · Programa de concientización en seguridad de la información</p>
            <h1>Constancia de aprobación</h1>
            <p class="lead">Se deja constancia de que</p>
            <p class="name">${esc(c.nombreCompleto)}</p>
            <p class="lead">aprobó la actividad</p>
            <p class="activity">${esc(c.actividad)}</p>
            <p class="detail">Tema: ${esc(c.tema)}${c.porcentaje == null ? '' : ` · Calificación: ${Number(c.porcentaje).toFixed(0)} %`}<br>Fecha de aprobación: ${esc(fecha(c.fechaAprobacion))}</p>
            <div class="verify"><div id="qr"></div><p>Código de verificación<br><strong>${esc(c.codigo)}</strong><br><small>Verifica su autenticidad en<br>${esc(verificacion)}</small></p></div>
            <p class="footer">Emitida el ${esc(fecha(c.fechaEmision))}. Evidencia de competencia según ISO/IEC 27001, cláusula 7.2.</p>`;
        document.getElementById('qr').append(qr(verificacion));
        const imprimir = document.getElementById('imprimir');
        imprimir.disabled = false;
        imprimir.addEventListener('click', () => window.print());
    } catch (error) {
        box.innerHTML = `<p class="status">${esc(error.status === 401 ? 'Inicia sesión para ver tu constancia.' : error.message)}</p>${error.status === 401 ? '<p class="status"><a href="login.html">Iniciar sesión</a></p>' : ''}`;
    }
}
