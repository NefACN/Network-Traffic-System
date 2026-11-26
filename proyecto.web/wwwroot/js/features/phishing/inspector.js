// inspeccion interactiva tras caer en una simulacion: correo, senales y anatomia del enlace
import { escapeHtml as esc } from '../../utils/ui.js';

const TIPS = {
    remitente: ['Quién lo envía', 'El nombre visible lo elige quien envía el correo. Revisa siempre la dirección completa: el dominio después de «@» debe ser el oficial.'],
    enlace: ['A dónde lleva el botón', 'Antes de pulsar, pasa el ratón por encima (o mantén pulsado en el celular) y lee el dominio real del destino.'],
    errores: ['Errores', 'Faltas de ortografía, frases raras o datos que no cuadran son típicos de correos falsos.'],
    tecnico: ['Indicadores técnicos', 'Remitentes, dominios, enlaces o adjuntos que no coinciden con la organización que dice escribir.'],
    visual: ['Presentación', 'Logotipos pixelados, colores o formatos distintos a los habituales.'],
    lenguaje: ['Lenguaje', 'Saludos genéricos («Estimado cliente») o peticiones de datos que una organización real no haría por correo.'],
    tacticas: ['Presión', 'Urgencia, amenazas, premios o autoridad para que actúes sin pensar.'],
};

// dominio registrable aproximado: dos ultimos niveles, o tres para .com.bo o .co.uk
export function registrableDomain(host) {
    const labels = String(host).toLowerCase().replace(/\.$/, '').split('.');
    if (labels.length < 2) return host;
    const second = labels.at(-2), last = labels.at(-1);
    const compuesto = labels.length >= 3 && last.length === 2 && ['com', 'org', 'net', 'gob', 'gov', 'edu', 'co', 'ac', 'mil', 'int', 'nom'].includes(second);
    return labels.slice(compuesto ? -3 : -2).join('.');
}
function urlParts(raw) {
    try {
        const u = new URL(raw), domain = registrableDomain(u.hostname);
        return { protocol: `${u.protocol}//`, user: u.username ? `${decodeURIComponent(u.username)}@` : '', sub: u.hostname.slice(0, Math.max(0, u.hostname.length - domain.length)),
            domain, port: u.port ? `:${u.port}` : '', rest: `${u.pathname === '/' && !u.search ? '' : u.pathname}${u.search}` };
    } catch { return null; }
}
const highlightUrl = raw => { const p = urlParts(raw); return p ? `${esc(p.protocol + p.user + p.sub)}<b>${esc(p.domain)}</b>${esc(p.port + p.rest)}` : esc(raw); };
const highlightEmail = mail => { const [user, domain] = String(mail).split('@'); return domain ? `${esc(user)}@<b>${esc(domain)}</b>` : esc(mail); };

function sanitize(html) {
    const doc = new DOMParser().parseFromString(String(html ?? '').replace(/\{\{\s*NombreUsuario\s*\}\}/gi, 'Colaborador'), 'text/html');
    doc.querySelectorAll('script,iframe,object,embed,form,input,button,textarea,select,meta,base,link,svg,math,style').forEach(n => n.remove());
    doc.querySelectorAll('*').forEach(node => {
        for (const attr of [...node.attributes]) if (!['style', 'colspan', 'rowspan'].includes(attr.name)) node.removeAttribute(attr.name);
        // sin recursos externos ni posicionamiento que se salga del visor
        if (node.hasAttribute('style') && /url\(|position\s*:\s*(fixed|absolute)|expression\(/i.test(node.getAttribute('style'))) node.removeAttribute('style');
    });
    return doc;
}
// envuelve la primera aparicion de cada fragmento en un objetivo invisible
function wrapFragments(doc, root, targets) {
    const placed = new Set();
    for (const t of targets) {
        const needle = t.fragment.trim().toLowerCase();
        const walker = doc.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        for (let node = walker.nextNode(); node; node = walker.nextNode()) {
            const i = node.nodeValue.toLowerCase().indexOf(needle);
            if (i < 0 || node.parentElement?.closest('[data-target]')) continue;
            const match = node.splitText(i); match.splitText(needle.length);
            const span = doc.createElement('span'); span.dataset.target = t.id; span.className = 't';
            span.append(match.cloneNode()); match.replaceWith(span); placed.add(t.id); break;
        }
    }
    return placed;
}

// monta el inspector, onReveal recibe el resumen al revelar las respuestas
export function mountInspector(root, { asunto, html, senales = [], ensenanza = null, onReveal }) {
    const remitente = ensenanza?.remitenteCorreo ? { nombre: ensenanza.remitenteNombre || ensenanza.remitenteCorreo, correo: ensenanza.remitenteCorreo } : null;
    const enlace = ensenanza?.enlaceMostrado || null;
    const fragmentTargets = senales.map((s, i) => ({ id: `s${i}`, fragment: s.fragmento, tipo: s.tipo, nombre: s.nombreTipo, descripcion: s.descripcion })).filter(t => t.fragment?.trim());
    root.innerHTML = `<section class="insp" aria-labelledby="insp-title">
        <div class="insp-head"><div><p class="insp-step">PASO 1 DE 2</p><h3 id="insp-title">Encuentra las señales</h3></div><span class="insp-count" aria-live="polite"></span></div>
        <p class="insp-help">Este es el correo en el que hiciste clic. Investígalo como deberías hacerlo antes de pulsar: <strong>toca el remitente</strong>, <strong>pasa el ratón o mantén pulsado el botón</strong> para ver a dónde lleva y <strong>haz clic en las frases sospechosas</strong>.</p>
        <div class="mail">
            <div class="mail-row"><span class="mail-label">De:</span>${remitente ? `<button type="button" class="mail-sender" data-target="remitente" aria-expanded="false">${esc(remitente.nombre)} <span aria-hidden="true">▾</span></button><span class="mail-sender-real" hidden>&lt;${highlightEmail(remitente.correo)}&gt;</span>` : '<span class="mail-muted">Remitente de la simulación</span>'}</div>
            <div class="mail-row"><span class="mail-label">Asunto:</span><span class="mail-subject">${esc(asunto ?? '')}</span></div>
            <iframe class="mail-body" sandbox="allow-same-origin" title="Correo de la simulación (interactivo)"></iframe>
            <div class="mail-status" aria-live="polite"><span class="mail-status-text">Pasa el ratón por el botón o enlace para ver a dónde lleva.</span></div>
            ${enlace ? '<button type="button" class="mail-link-check" data-link-check>¿A dónde lleva el botón?</button>' : ''}
        </div>
        <p class="insp-feedback" aria-live="polite"></p>
        <div class="insp-actions"><button type="button" class="btn-enviar" data-reveal>Revelar respuestas</button></div>
        <div class="insp-result" hidden></div></section>`;
    const found = new Set(); let misses = 0, revealed = false;
    const targets = [...(remitente ? [{ id: 'remitente', tipo: 'remitente' }] : []), ...(enlace ? [{ id: 'enlace', tipo: 'enlace' }] : [])];
    const subject = root.querySelector('.mail-subject'), status = root.querySelector('.mail-status-text'), feedback = root.querySelector('.insp-feedback');
    const frame = root.querySelector('.mail-body');
    const doc = sanitize(html);
    const placed = new Set([...wrapFragments(document, subject, fragmentTargets), ...wrapFragments(doc, doc.body, fragmentTargets)]);
    targets.push(...fragmentTargets.filter(t => placed.has(t.id)));
    const count = () => { root.querySelector('.insp-count').textContent = `${found.size} de ${targets.length} señales`; };
    const showLink = () => { if (!enlace) return; status.innerHTML = `Este enlace lleva a: <span class="mail-url">${highlightUrl(enlace)}</span>`; status.parentElement.classList.add('is-active'); };
    const hideLink = () => { if (revealed) return; status.textContent = 'Pasa el ratón por el botón o enlace para ver a dónde lleva.'; status.parentElement.classList.remove('is-active'); };
    function mark(id, message) {
        if (revealed || found.has(id) || !targets.some(t => t.id === id)) return;
        found.add(id); count();
        root.querySelectorAll(`[data-target="${id}"]`).forEach(n => n.classList.add('found'));
        frame.contentDocument?.querySelectorAll(`[data-target="${id}"]`).forEach(n => n.classList.add('found'));
        feedback.textContent = message ?? '¡Bien! Esa es una señal.';
        if (found.size === targets.length) feedback.textContent = '¡Encontraste todas las señales! Pulsa «Revelar respuestas» para ver la explicación.';
    }
    function miss() { if (revealed) return; misses++; feedback.textContent = 'Ahí no hay una señal. Sigue buscando.'; }
    frame.srcdoc = `<!doctype html><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src 'none'; form-action 'none'; base-uri 'none'">
        <style>body{font:14px/1.7 system-ui,sans-serif;color:#26382f;background:#fff;padding:15px;margin:0;overflow-wrap:anywhere;cursor:crosshair}img{display:none}table{max-width:100%}
        a{color:#1d4ed8;text-decoration:underline;cursor:help}.t{border-radius:3px;transition:background .2s}.t.found{background:#bbf7d0;outline:2px solid #16a34a}
        .t.missed{background:#fecaca;outline:2px dashed #dc2626}a.found{outline:2px solid #16a34a;outline-offset:2px}a.missed{outline:2px dashed #dc2626;outline-offset:2px}</style>${doc.body.innerHTML}`;
    frame.addEventListener('load', () => {
        const d = frame.contentDocument; if (!d) return;
        frame.style.height = `${Math.min(900, Math.max(220, d.documentElement.scrollHeight + 4))}px`;
        d.querySelectorAll('a').forEach(a => {
            a.dataset.target = enlace ? 'enlace' : ''; a.tabIndex = 0;
            let timer, press;
            a.addEventListener('mouseenter', () => { showLink(); timer = setTimeout(() => mark('enlace', 'Bien: revisaste a dónde lleva el botón. ¿Coincide el dominio con quien dice escribir?'), 700); });
            a.addEventListener('mouseleave', () => { clearTimeout(timer); hideLink(); });
            a.addEventListener('focus', () => { showLink(); mark('enlace'); });
            a.addEventListener('touchstart', () => { press = setTimeout(() => { showLink(); mark('enlace'); }, 450); }, { passive: true });
            a.addEventListener('touchend', () => clearTimeout(press));
        });
        d.addEventListener('click', event => {
            event.preventDefault();
            const t = event.target.closest('[data-target]');
            if (t?.dataset.target === 'enlace') { showLink(); mark('enlace', 'Bien: revisaste a dónde lleva el botón. ¿Coincide el dominio con quien dice escribir?'); }
            else if (t?.dataset.target) mark(t.dataset.target); else miss();
        });
    });
    subject.addEventListener('click', event => { const t = event.target.closest('[data-target]'); if (t) mark(t.dataset.target); else miss(); });
    root.querySelector('.mail-sender')?.addEventListener('click', event => {
        const b = event.currentTarget; b.setAttribute('aria-expanded', 'true'); root.querySelector('.mail-sender-real').hidden = false;
        mark('remitente', `El nombre dice «${remitente.nombre}», pero la dirección real es de ${registrableDomain(remitente.correo.split('@')[1] ?? '')}. Cualquiera puede escribir el nombre que quiera.`);
    });
    root.querySelector('[data-link-check]')?.addEventListener('click', () => { showLink(); mark('enlace', 'Bien: revisaste a dónde lleva el botón. ¿Coincide el dominio con quien dice escribir?'); });
    root.querySelector('[data-reveal]').addEventListener('click', event => {
        if (revealed) return; revealed = true; event.currentTarget.hidden = true;
        const missed = targets.filter(t => !found.has(t.id));
        missed.forEach(t => { root.querySelectorAll(`[data-target="${t.id}"]`).forEach(n => n.classList.add('missed')); frame.contentDocument?.querySelectorAll(`[data-target="${t.id}"]`).forEach(n => n.classList.add('missed')); });
        if (remitente) root.querySelector('.mail-sender-real').hidden = false;
        if (enlace) { showLink(); status.parentElement.classList.add('is-active'); }
        const perfect = missed.length === 0 && targets.length > 0;
        const tip = t => { const [title, text] = TIPS[t.tipo] ?? [t.nombre, '']; return `<li><strong>${esc(title)}${t.fragment ? `: «${esc(t.fragment)}»` : ''}</strong><span>${esc(t.descripcion || text)}</span></li>`; };
        const result = root.querySelector('.insp-result');
        result.innerHTML = `<p class="insp-score ${perfect ? 'is-perfect' : ''}">${targets.length ? `Encontraste <strong>${found.size} de ${targets.length}</strong> señales.` : 'Este correo no tiene señales marcables.'} ${perfect ? '¡Excelente ojo!' : ''}</p>
            ${missed.length ? `<h4>Lo que se te pasó (marcado en rojo)</h4><ul class="insp-missed">${missed.map(tip).join('')}</ul>` : ''}
            ${remitente || enlace ? `<div class="insp-truth">${remitente ? `<p><strong>Remitente real:</strong> ${highlightEmail(remitente.correo)} — el nombre visible era «${esc(remitente.nombre)}».</p>` : ''}${enlace ? `<p><strong>El botón llevaba a:</strong> ${highlightUrl(enlace)}</p>` : ''}</div>` : ''}`;
        result.hidden = false; feedback.textContent = '';
        onReveal?.({ encontradas: found.size, total: targets.length, fallos: misses, omitidas: [...new Set(missed.map(t => t.tipo))] });
    });
    count();
}

// anatomia del enlace y tres preguntas, onDone recibe { aciertos, total }
export function mountUrlAnatomy(root, { url, remitenteNombre, onDone }) {
    const ejemplo = !url, raw = url || 'https://seguridad.miempresa-verificacion.com/login?cuenta=12345';
    const p = urlParts(raw);
    if (!p) { root.hidden = true; onDone?.({ aciertos: 0, total: 0 }); return; }
    const slug = String(remitenteNombre ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9]/g, '');
    const tld = p.domain.split('.').slice(1).join('.') || 'com';
    let marca = `${slug || p.domain.split('.')[0].split('-')[0]}.${tld}`;
    if (marca === p.domain) marca = `www.${p.domain.split('.')[0]}.org`;
    // el engano tipico: lo que el subdominio aparenta ser
    const subLabels = p.sub.replace(/\.$/, '').split('.').filter(Boolean);
    const enganio = subLabels.length >= 2 ? subLabels.slice(-2).join('.') : `${subLabels[0] ?? 'www'}.${p.domain}`;
    const preguntas = [
        { url: raw, opciones: [...new Set([marca, p.domain, enganio])], correcta: p.domain,
          explicacion: `El dominio real es lo que va justo antes de la primera «/»: ${p.domain}. Todo lo anterior (${p.sub || 'nada'}) lo elige quien controla ese dominio.` },
        { url: 'https://portal.empresa.com.acceso-seguro.net/inicio', opciones: ['empresa.com', 'acceso-seguro.net', 'portal.empresa.com'], correcta: 'acceso-seguro.net',
          explicacion: '«empresa.com» aparece al principio solo como subdominio. El dominio real, al final, es acceso-seguro.net.' },
        { url: 'https://empresa.com@inicio-sesion.info/login', opciones: ['empresa.com', 'login', 'inicio-sesion.info'], correcta: 'inicio-sesion.info',
          explicacion: 'Todo lo que va antes de «@» se ignora: el navegador abre inicio-sesion.info.' },
    ];
    root.innerHTML = `<section class="anat" aria-labelledby="anat-title"><p class="insp-step">PASO 2 DE 2</p><h3 id="anat-title">Anatomía del enlace</h3>
        <p>${ejemplo ? 'Un ejemplo de enlace falso' : 'El enlace del botón en el que hiciste clic'}, por partes:</p>
        <div class="anat-url" role="img" aria-label="Partes de la dirección ${esc(raw)}">
            <span class="anat-part p-proto"><code>${esc(p.protocol)}</code><small>protocolo</small></span>
            ${p.user ? `<span class="anat-part p-user"><code>${esc(p.user)}</code><small>se ignora</small></span>` : ''}
            ${p.sub ? `<span class="anat-part p-sub"><code>${esc(p.sub)}</code><small>subdominio: lo inventa el dueño</small></span>` : ''}
            <span class="anat-part p-domain"><code>${esc(p.domain)}</code><small>DOMINIO REAL</small></span>
            ${p.port + p.rest ? `<span class="anat-part p-path"><code>${esc(p.port + p.rest)}</code><small>ruta</small></span>` : ''}</div>
        <p class="anat-rule"><strong>Regla:</strong> busca la primera «/» después de «https://» y lee hacia la izquierda: lo último antes de esa barra es el dominio real. Si no es el sitio oficial, no pulses.</p>
        <form class="anat-quiz">${preguntas.map((q, i) => `<fieldset class="question-card"><legend>${i + 1}. ¿A qué sitio lleva realmente <code>${esc(q.url)}</code>?</legend>
            ${q.opciones.map(o => `<label class="quiz-option"><input type="radio" name="anat${i}" value="${esc(o)}" required> ${esc(o)}</label>`).join('')}
            <p class="question-explanation hidden"></p></fieldset>`).join('')}
            <button type="submit" class="btn-enviar">Comprobar</button><p class="anat-score" aria-live="polite"></p></form></section>`;
    root.querySelector('.anat-quiz').addEventListener('submit', event => {
        event.preventDefault(); const form = event.currentTarget; if (form.dataset.done) return;
        let aciertos = 0;
        preguntas.forEach((q, i) => {
            const elegida = form.querySelector(`input[name="anat${i}"]:checked`)?.value, ok = elegida === q.correcta; if (ok) aciertos++;
            const box = form.querySelectorAll('.question-explanation')[i];
            box.textContent = `${ok ? '✔ Correcto.' : `✘ Era ${q.correcta}.`} ${q.explicacion}`; box.classList.remove('hidden'); box.classList.toggle('is-ok', ok);
        });
        form.dataset.done = '1'; form.querySelector('button').disabled = true;
        form.querySelector('.anat-score').textContent = `${aciertos} de ${preguntas.length} correctas.`;
        onDone?.({ aciertos, total: preguntas.length });
    });
}
