export function installAppearance(account) {
    const key = 'dismatec.appearance.' + encodeURIComponent(account);
    const system = window.matchMedia('(prefers-color-scheme: dark)');
    const mode = document.querySelector('#appearance-mode');
    const palette = document.querySelector('#appearance-palette');
    const modes = ['portal', 'light', 'dark', 'system', 'custom'];
    const palettes = ['sand', 'slate'];
    let preference = {};
    try { preference = JSON.parse(localStorage.getItem(key)) || {}; } catch { /* usa la paleta del portal */ }
    let selected = modes.includes(preference.mode) ? preference.mode : 'portal';
    let color = palettes.includes(preference.palette) ? preference.palette : 'sand';

    function render() {
        const scheme = selected === 'system' ? (system.matches ? 'dark' : 'light') : selected;
        document.body.dataset.appearance = selected === 'custom' ? color : scheme;
        if (mode && palette) {
            mode.querySelector(`[value="${selected}"]`).checked = true;
            palette.querySelector(`[value="${color}"]`).checked = true;
            mode.querySelector('[value="custom"]').closest('label').querySelector('.appearance-preview').dataset.sample = color;
            palette.hidden = selected !== 'custom';
        }
    }

    function save() {
        selected = mode.querySelector(':checked').value;
        color = palette.querySelector(':checked').value;
        render();
        try { localStorage.setItem(key, JSON.stringify({ mode: selected, palette: color })); }
        catch {
            document.querySelector('#appearance-note').textContent = 'El cambio se aplicó solo a esta página. El navegador no permitió guardarlo.';
        }
    }

    mode?.addEventListener('change', save);
    palette?.addEventListener('change', save);
    system.addEventListener('change', render);
    render();
}
