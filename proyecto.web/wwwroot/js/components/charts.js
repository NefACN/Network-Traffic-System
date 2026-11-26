import { escapeHtml as esc } from '../utils/ui.js';

// colores con significado fijo, validados para modo oscuro y daltonismo
export const COLORS = { normal: '#4f8fd6', atypical: '#8f9bb3', threshold: '#dca569', benign: '#3f9a78', incident: '#e5686f' };
const fmt = value => Number(value ?? 0).toLocaleString('es-BO', { maximumFractionDigits: 1 });

const niceMax = value => {
    if (value <= 5) return 5;

    const step = 10 ** Math.floor(Math.log10(value)) / 2;
    return Math.ceil(value / step) * step;
};

// linea temporal con varias series en una escala, cruz de referencia y tooltip
export function lineChart(container, { labels, series, height = 220, ariaLabel }) {
    const max = niceMax(Math.max(1, ...series.flatMap(s => s.values)));
    const w = 600, h = height, n = labels.length;
    const x = i => n === 1 ? w / 2 : i / (n - 1) * w, y = v => h - v / max * (h - 8);
    const path = values => values.map((v, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join('');
    const grid = [0.25, 0.5, 0.75, 1].map(f => `<line class="ch-grid" x1="0" x2="${w}" y1="${y(max * f)}" y2="${y(max * f)}"/>`).join('');
    const axis = [1, 0.5, 0].map(f => `<span style="top:${(1 - f) * 100}%">${fmt(max * f)}</span>`).join('');
    const lines = series.map(s => `<path class="ch-line" d="${path(s.values)}" stroke="${s.color}"/>`
        + (n <= 40 ? s.values.map((v, i) => `<circle class="ch-dot" cx="${x(i)}" cy="${y(v)}" r="3" fill="${s.color}"/>`).join('') : '')).join('');
    container.setAttribute('role', 'img');
    container.setAttribute('aria-label', ariaLabel);
    container.innerHTML = `<div class="ch-plot" style="height:${h}px"><div class="ch-axis">${axis}</div>
        <svg viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" aria-hidden="true">${grid}<line class="ch-base" x1="0" x2="${w}" y1="${h}" y2="${h}"/>${lines}
        <line class="ch-cross" x1="0" x2="0" y1="0" y2="${h}" hidden/></svg><div class="ch-tip" hidden></div></div>
        <div class="ch-xlabels"><span>${esc(labels[0])}</span><span>${esc(labels[Math.floor((n - 1) / 2)])}</span><span>${esc(labels.at(-1))}</span></div>`;
    const plot = container.querySelector('.ch-plot'), svg = plot.querySelector('svg');
    const cross = svg.querySelector('.ch-cross'), tip = plot.querySelector('.ch-tip');
    const show = clientX => {
        const box = svg.getBoundingClientRect();
        const index = Math.max(0, Math.min(n - 1, Math.round((clientX - box.left) / box.width * (n - 1))));
        cross.setAttribute('x1', x(index));
        cross.setAttribute('x2', x(index));
        cross.hidden = false;
        tip.innerHTML = `<strong>${esc(labels[index])}</strong>${series.map(s => `<span><i style="background:${s.color}"></i>${esc(s.name)} <b>${fmt(s.values[index])}</b></span>`).join('')}`;
        tip.hidden = false;
        const left = (index / Math.max(1, n - 1)) * box.width;
        tip.style.left = `${Math.min(Math.max(left, 70), box.width - 70)}px`;
    };

    svg.addEventListener('pointermove', event => show(event.clientX));
    svg.addEventListener('pointerleave', () => {
        cross.hidden = true;
        tip.hidden = true;
    });
}

// mini tendencia de un KPI sin ejes
export function sparkline(values, color = COLORS.normal) {
    if (!values?.length || values.length < 2) return '';

    const max = Math.max(1, ...values), w = 120, h = 32;
    const points = values.map((v, i) => `${(i / (values.length - 1) * w).toFixed(1)},${(h - 2 - v / max * (h - 4)).toFixed(1)}`).join(' ');
    return `<svg class="kpi-spark" viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" aria-hidden="true"><polyline points="${points}" fill="none" stroke="${color}"/></svg>`;
}

// lista de medidores: nombre, barra y cifra por fila
export function meterList(items, { max, format = fmt, suffix = '' } = {}) {
    const top = max ?? Math.max(1, ...items.map(item => item.value));
    return `<div class="meter-list">${items.map(item => `<div class="meter-row" title="${esc(item.tooltip ?? `${item.label}: ${format(item.value)}${suffix}`)}">
        <span class="meter-name">${esc(item.label)}${item.detail ? `<small>${esc(item.detail)}</small>` : ''}</span>
        <span class="meter-track"><span class="meter-fill" style="width:${Math.max(item.value > 0 ? 1.5 : 0, item.value / top * 100)}%;background:${item.color ?? COLORS.normal}"></span></span>
        <span class="meter-value">${esc(format(item.value))}${suffix}</span></div>`).join('')}</div>`;
}

// barra segmentada (<= 6 partes) con leyenda, cantidades y porcentajes
export function segmentedBar(parts) {
    const total = parts.reduce((sum, part) => sum + part.value, 0);
    if (!total) return '<p class="empty-state">Todavía no hay resultados revisados ni pendientes.</p>';

    const pct = value => value / total * 100;
    return `<div class="seg-bar" role="img" aria-label="${esc(parts.map(p => `${p.label}: ${p.value}`).join(', '))}">${parts.filter(p => p.value > 0)
        .map(p => `<span style="flex:${p.value};background:${p.color}" title="${esc(`${p.label}: ${fmt(p.value)} (${fmt(pct(p.value))} %)`)}"></span>`).join('')}</div>
        <ul class="seg-legend">${parts.map(p => `<li><i style="background:${p.color}"></i><span>${esc(p.label)}</span><b>${fmt(p.value)}</b><small>${fmt(pct(p.value))} %</small></li>`).join('')}</ul>`;
}

// histograma del puntaje de anomalia con atipicos bajo y sobre el umbral
export function histogram(container, { min, max, bins, threshold, counts, ariaLabel }) {
    const byBin = new Map(counts.map(c => [c.bin, c]));
    const rows = Array.from({ length: bins }, (_, i) => byBin.get(i) ?? { bin: i, count: 0, candidates: 0 });
    const top = niceMax(Math.max(1, ...rows.map(r => r.count))), w = 600, h = 180, gap = 2, bw = w / bins;
    const span = max > min ? max - min : 1, edge = i => min + span * i / bins;
    const score = v => v.toLocaleString('es-BO', { maximumFractionDigits: 2 });
    const bars = rows.map((row, i) => {
        const over = Math.min(row.candidates, row.count), under = row.count - over;
        const hu = under / top * (h - 8), ho = over / top * (h - 8), x = i * bw + gap / 2, width = bw - gap;
        const label = `Puntaje ${score(edge(i))} a ${score(edge(i + 1))}: ${fmt(row.count)} atípicos (${fmt(over)} sobre el umbral)`;
        return `<g><title>${esc(label)}</title><rect class="ch-hit" x="${i * bw}" y="0" width="${bw}" height="${h}"/>`
            + (hu > 0 ? `<rect x="${x}" y="${h - hu}" width="${width}" height="${hu}" rx="3" fill="${COLORS.atypical}"/>` : '')
            + (ho > 0 ? `<rect x="${x}" y="${h - hu - ho - (hu > 0 ? gap : 0)}" width="${width}" height="${ho}" rx="3" fill="${COLORS.threshold}"/>` : '')
            + '</g>';
    }).join('');
    const ratio = threshold == null ? null : Math.min(1, Math.max(0, (threshold - min) / span));
    container.setAttribute('role', 'img');
    container.setAttribute('aria-label', ariaLabel);
    container.innerHTML = `<div class="ch-plot" style="height:${h}px"><div class="ch-axis"><span style="top:0">${fmt(top)}</span><span style="top:50%">${fmt(top / 2)}</span><span style="top:100%">0</span></div>
        <svg viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" aria-hidden="true"><line class="ch-grid" x1="0" x2="${w}" y1="${h / 2}" y2="${h / 2}"/>${bars}
        <line class="ch-base" x1="0" x2="${w}" y1="${h}" y2="${h}"/>${ratio == null ? '' : `<line class="ch-threshold" x1="${ratio * w}" x2="${ratio * w}" y1="0" y2="${h}"/>`}</svg>
        ${ratio == null ? '' : `<span class="ch-threshold-label ${ratio > 0.75 ? 'is-left' : ''}" style="left:${ratio * 100}%">Umbral ${score(threshold)}</span>`}</div>
        <div class="ch-xlabels"><span>${score(min)} · menos atípico</span><span>${score(min + span / 2)}</span><span>${score(max)} · más atípico</span></div>`;
}
