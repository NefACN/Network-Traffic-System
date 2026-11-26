// graficas y exportaciones del modulo HTTP, todo texto variable pasa por esc()
import { escapeHtml as esc } from '../../utils/ui.js';

export const n = x => Number(x ?? 0).toLocaleString('es-BO');
export const pct = (part, total) => total
    ? `${(100 * Number(part) / Number(total)).toLocaleString('es-BO', { maximumFractionDigits: 1 })}%`
    : '0%';
export const groupName = cluster => cluster === -1 ? 'Atípicos' : `Grupo ${cluster}`;
// grupos normales en tonos azules, solo el grupo atipico usa ambar de revision
const color = cluster => cluster === -1 ? 'var(--ml-atypical)' : `hsl(${200 + (cluster * 23) % 45} 58% ${50 + (cluster * 13) % 18}%)`;
const when = value => value
    ? new Date(value).toLocaleString('es-BO', { timeZone: 'America/La_Paz', dateStyle: 'short', timeStyle: 'short' })
    : '—';
const section = (title, body, caption = '') => `<section class="panel"><h2>${title}</h2>${caption ? `<p class="ml-caption">${caption}</p>` : ''}${body}</section>`;

// barras de volumen por grupo, los atipicos se destacan y van primero
export function groupBars(summary, runId) {
    if (!summary.total) return '<p class="ml-empty">Todavía no hay resultados para representar.</p>';

    const groups = [...summary.groups].sort((a, b) => (a.cluster === -1 ? -1 : b.cluster === -1 ? 1 : b.count - a.count));
    const largest = Math.max(1, ...groups.map(g => g.count));
    return `<div class="ml-group-bars">${groups.map(g => `<a class="ml-group-bar ${g.cluster === -1 ? 'is-atypical' : ''}" href="http-eventos.html?run=${encodeURIComponent(runId)}&cluster=${g.cluster}" style="--group-color:${color(g.cluster)}">
        <span class="ml-group-name">${groupName(g.cluster)}</span><span class="ml-group-track"><span style="width:${(100 * g.count / largest).toFixed(2)}%"></span></span>
        <span class="ml-group-value">${n(g.count)} · ${pct(g.count, summary.total)}</span></a>`).join('')}</div>`;
}

// barras apiladas de peticiones habituales y atipicas por intervalo
export function timelineChart(summary, limit = 96) {
    const values = summary.windows.slice(-limit);
    if (!values.length) return '<p class="ml-empty">Todavía no hay resultados en este periodo.</p>';

    const peak = Math.max(1, ...values.map(w => w.count));
    return `<div class="ml-timeline" role="img" aria-label="Peticiones por intervalo; la parte naranja son comportamientos atípicos. Los valores están en la tabla.">
        ${values.map(w => `<div title="${esc(when(w.from))}: ${n(w.count)} peticiones, ${n(w.noise)} atípicas, ${n(w.candidates)} alertas"><span class="ml-tl-total" style="height:${(100 * w.count / peak).toFixed(2)}%"><span class="ml-tl-noise" style="height:${w.count ? (100 * w.noise / w.count).toFixed(2) : 0}%"></span></span></div>`).join('')}</div>
        <div class="ml-legend"><span><i class="ml-key-normal"></i>Habituales</span><span><i class="ml-key-atypical"></i>Atípicas</span><span>${esc(when(values[0].from))} → ${esc(when(values.at(-1).from))}</span></div>
        <details><summary>Ver valores (${n(values.length)} intervalos)</summary><div class="table-scroll"><table class="event-table"><thead><tr><th>Inicio</th><th>Peticiones</th><th>Atípicas</th><th>Alertas</th></tr></thead><tbody>
        ${values.map(w => `<tr><td>${esc(when(w.from))}</td><td>${n(w.count)}</td><td>${n(w.noise)}</td><td>${n(w.candidates)}</td></tr>`).join('')}</tbody></table></div></details>`;
}

export function alertPanel(report, runId) {
    const a = report.alertSummary;
    return section('Alertas para revisar', `<div class="ml-alert-counts">
        <span><strong>${n(a.pending)}</strong> pendientes</span><span><strong>${n(a.inReview)}</strong> en revisión</span>
        <span><strong>${n(a.benign)}</strong> descartadas</span><span><strong>${n(a.incidents)}</strong> incidentes confirmados</span></div>
        <div class="ml-actions"><a class="btn btn-primary" href="http-eventos.html?run=${encodeURIComponent(runId)}&candidate=true&review=pending">Revisar alertas pendientes</a>
        <a class="text-action" href="http-eventos.html?run=${encodeURIComponent(runId)}&candidate=true">Ver todas las alertas (${n(a.total)})</a></div>`,
        'Una alerta es un comportamiento atípico con una puntuación de rareza superior al umbral. Un analista decide si es un incidente.');
}

// mapa tecnico rareza x pertenencia, solo en el reporte completo
export function densityMap(report) {
    const d = report.density;
    if (!report.summary.total) return '';

    const maximum = Math.max(1, ...d.cells.map(c => c.count));
    const label = (bin, span) => (d.minScore + bin * span / 20).toFixed(3);
    const cells = d.cells.map(c => `<rect x="${60 + c.scoreBin * 25}" y="${30 + (9 - c.strengthBin) * 24}" width="24" height="23" fill="var(--ml-accent)" fill-opacity="${(.12 + .88 * Math.log1p(c.count) / Math.log1p(maximum)).toFixed(3)}"><title>Rareza ${label(c.scoreBin, d.scoreSpan)} a ${label(c.scoreBin + 1, d.scoreSpan)}; pertenencia ${(c.strengthBin / 10).toFixed(1)}–${((c.strengthBin + 1) / 10).toFixed(1)}; ${c.count} peticiones, ${c.noise} atípicas, ${c.candidates} alertas</title></rect>`).join('');
    return `<div class="ml-density"><svg viewBox="0 0 620 330" role="img" aria-label="Concentración de peticiones según puntuación de rareza y pertenencia al grupo"><path d="M60 25V275H565" fill="none" stroke="currentColor"/>${cells}<text x="60" y="295">${d.minScore.toFixed(3)}</text><text x="500" y="295">${(d.minScore + d.scoreSpan).toFixed(3)}</text><text x="220" y="320">Puntuación de rareza →</text><text x="16" y="40">1.0</text><text x="16" y="268">0.0</text><text transform="translate(12 235) rotate(-90)">Pertenencia →</text></svg></div>
        <p class="ml-caption">Cada celda suma peticiones (color en escala logarítmica). Abajo a la derecha: poco parecidas a un grupo y con rareza alta.</p>`;
}

// barras pareadas para comparar dos periodos, valores ya en %
export function pairedBars(rows) {
    const peak = Math.max(1, ...rows.flatMap(r => [r.a, r.b]));
    return `<div class="ml-paired">${rows.map(r => `<div class="ml-paired-row"><span class="ml-paired-label">${esc(r.label)}</span>
        <span class="ml-paired-bars"><span class="ml-bar-a" style="width:${(100 * r.a / peak).toFixed(2)}%"></span><span class="ml-bar-b" style="width:${(100 * r.b / peak).toFixed(2)}%"></span></span>
        <span class="ml-paired-values"><b class="ml-tag-a">A</b> ${esc(r.format(r.a))} · <b class="ml-tag-b">B</b> ${esc(r.format(r.b))}</span></div>`).join('')}</div>`;
}

function download(name, type, content) {
    const url = URL.createObjectURL(new Blob([content], { type }));
    const a = document.createElement('a');
    a.href = url;
    a.download = name;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export const downloadJson = (name, value) => download(name, 'application/json', JSON.stringify(value, null, 2));

const csvCell = value => {
    const text = String(value ?? '');
    return /[",\n;]/.test(text) || /^[=+\-@]/.test(text) ? `"${(/^[=+\-@]/.test(text) ? "'" : '') + text.replace(/"/g, '""')}"` : text; };
// CSV con BOM para Excel y celdas protegidas contra inyeccion de formulas
export const downloadCsv = (name, sections) => download(name, 'text/csv;charset=utf-8',
    '﻿' + sections.map(({ title, headers, rows }) => [[title], headers, ...rows].map(row => row.map(csvCell).join(',')).join('\r\n')).join('\r\n\r\n'));
