// componentes compartidos de CyberScore v3: niveles, colores y tabla por area
import { escapeHtml as esc } from '../../utils/ui.js';

export const LEVELS = [['Bajo', 'is-low', 'Reconocen bien los engaños'], ['Medio', 'is-medium', 'Necesitan repasar algunos temas'],
    ['Alto', 'is-high', 'Cayeron varias veces recientemente'], ['Crítico', 'is-critical', 'Con plan de refuerzo asignado'], ['Sin evaluación suficiente', 'is-none', 'Menos simulaciones que el mínimo']];
export const cls = nivel => LEVELS.find(l => l[0] === nivel)?.[1] ?? 'is-none';
export const color = nivel => `var(--cs-${({ 'is-low': 'low', 'is-medium': 'medium', 'is-high': 'high', 'is-critical': 'critical' })[cls(nivel)] ?? 'none'})`;
// nivel por area segun CyberScore promedio, con distribucion y % en Alto/Critico
export function areasPanel(areas, clickable) {
    if (!areas?.length) return '';
    const rows = areas.map(a => {
        if (a.oculto) return `<tr><td><strong>${esc(a.area)}</strong></td><td colspan="5" class="panel-caption">Menos colaboradores que el umbral de agregación: se oculta para proteger su identidad.</td></tr>`;
        const total = Math.max(1, a.evaluados), seg = [['Bajo', a.bajo], ['Medio', a.medio], ['Alto', a.alto], ['Crítico', a.critico]];
        return `<tr><td><strong>${esc(a.area)}</strong><small>Criticidad ${esc(a.criticidad ?? '—')}</small></td>
            <td><span class="cs-level ${cls(a.nivel)}">${esc(a.promedio == null ? 'Sin evaluación' : a.nivel)}</span></td>
            <td>${a.promedio == null ? '—' : `<span class="cs-score">${Number(a.promedio).toFixed(0)}</span><span class="cs-bar"><span style="width:${Number(a.promedio)}%;background:${color(a.nivel)}"></span></span>`}</td>
            <td>${a.evaluados ? `<span class="cs-distribution cs-mini" role="img" aria-label="Bajo ${a.bajo}, Medio ${a.medio}, Alto ${a.alto}, Crítico ${a.critico}">${seg.filter(([, n]) => n).map(([n, c]) => `<span style="width:${100 * c / total}%;background:${color(n)}" title="${n}: ${c}"></span>`).join('')}</span><small>${a.bajo} B · ${a.medio} M · ${a.alto} A · ${a.critico} C</small>` : '—'}</td>
            <td>${a.porcentajeAltoCritico == null ? '—' : `<strong class="${a.porcentajeAltoCritico >= 30 ? 'score-down' : ''}">${Number(a.porcentajeAltoCritico).toFixed(0)}%</strong>`}${a.planesPendientes ? `<small>${a.planesPendientes} refuerzos pendientes</small>` : ''}</td>
            <td>${a.evaluados} de ${a.colaboradores}${clickable ? `<small><button class="text-action" type="button" data-area="${Number(a.idArea)}">Ver personas</button></small>` : ''}</td></tr>`;
    }).join('');
    return `<section class="panel"><div class="panel-heading"><h2>Por área operativa</h2></div>
        <div class="table-scroll"><table class="event-table"><thead><tr><th>Área</th><th>Nivel del área</th><th>CyberScore promedio</th><th>Personas por nivel</th><th>En Alto o Crítico</th><th>Evaluados</th></tr></thead><tbody>${rows}</tbody></table></div>
        <p class="panel-caption">El nivel del área es el nivel de su CyberScore promedio (solo personas con evaluación suficiente). El porcentaje en Alto o Crítico evita que un buen promedio oculte a personas que necesitan refuerzo. Se usan las personas asignadas hoy a cada área.</p></section>`;
}
