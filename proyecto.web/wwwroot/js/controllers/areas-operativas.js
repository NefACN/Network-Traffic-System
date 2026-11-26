import { icon } from '../components/icons.js';
import { apiCallAuth } from '../services/api.js';
import { el, escapeHtml as escapar, toast } from '../utils/ui.js';
import { renderPaginador, enlazarPaginador, paginarEnNavegador } from '../components/paginacion.js';

(function () {
    const ENDPOINT_CRITICIDADES = '/campanias-phishing/criticidades';

    let areas = [];
    let filtroTexto = '';
    // catalogo pequeno, se pagina en el navegador tras filtrar
    let pagina = 1;

    let niveles = new Map();
    const CS = { Bajo: 'is-low', Medio: 'is-medium', Alto: 'is-high', 'Crítico': 'is-critical' };
    function celdaCyberScore(idArea) {
        const n = niveles.get(idArea);
        if (!n || n.promedio == null)
            return `<span class="cs-level is-none">Sin evaluación</span>${n ? `<small>${n.colaboradores} personas</small>` : ''}`;

        return `<a class="cs-level ${CS[n.nivel] ?? 'is-none'}" href="cyberscore.html" title="Ver en CyberScore de colaboradores">${escapar(n.nivel)} · ${Number(n.promedio).toFixed(0)}</a><small>${Number(n.porcentajeAltoCritico ?? 0).toFixed(0)}% en Alto o Crítico · ${n.evaluados}/${n.colaboradores} evaluados</small>`;
    }

    async function cargarAreas() {
        el('tabla-body').innerHTML = '<tr><td colspan="5" class="table-state">Cargando áreas…</td></tr>';
        el('page-error').classList.remove('is-visible');
        try {
            areas = await apiCallAuth('/areas-operativas');
            // nivel CyberScore por area, opcional si la consulta falla
            try {
                niveles = new Map((await apiCallAuth('/cyberscore/areas')).map(n => [n.idArea, n]));
            } catch {
                niveles = new Map();
            }

            renderTabla();
        } catch (err) {
            el('tabla-body').innerHTML = '';
            el('page-error').textContent = err.message;
            el('page-error').classList.add('is-visible');
        }
    }

    function badgeNivel(nivel) {
        const n = (nivel || '').toLowerCase();
        if (n.includes('alta') || n.includes('crít')) return 'badge-critico';
        if (n.includes('media')) return 'badge-alto';
        if (n.includes('baja')) return 'badge-info';

        return 'badge-medio';
    }

    function renderTabla() {
        const texto = filtroTexto.trim().toLowerCase();
        const filas = areas.filter((a) => !texto || a.nombre.toLowerCase().includes(texto));

        const cuerpo = el('tabla-body');
        const paginador = contenedorPaginador();
        if (!filas.length) {
            cuerpo.innerHTML = '<tr><td colspan="5" class="table-state">No se encontraron áreas operativas.</td></tr>';
            paginador.innerHTML = '';
            return;
        }

        const datos = paginarEnNavegador(filas, pagina);
        pagina = datos.pagina;
        paginador.innerHTML = renderPaginador(datos, 'areas');
        cuerpo.innerHTML = datos.items.map((a) => `
      <tr data-id="${a.idArea}">
        <td class="t-primary">${escapar(a.nombre)}</td>
        <td><span class="badge ${badgeNivel(a.nivelCriticidad)}">${escapar(a.nivelCriticidad || '—')}</span></td>
        <td class="cs-cell">${celdaCyberScore(a.idArea)}</td>
        <td><span class="badge ${a.activo !== false ? 'badge-activo' : 'badge-inactivo'}">${a.activo !== false ? 'Activa' : 'Inactiva'}</span></td>
        <td class="t-actions">
          <button type="button" class="icon-action btn-editar" data-id="${a.idArea}" aria-label="Editar">
            ${icon('edit')}
          </button>
          <button type="button" class="btn btn-ghost btn-estado" data-id="${a.idArea}">${a.activo !== false ? 'Desactivar' : 'Activar'}</button>
        </td>
      </tr>
    `).join('');

        cuerpo.querySelectorAll('.btn-editar').forEach((btn) => {
            btn.addEventListener('click', () => abrirModal(Number(btn.dataset.id)));
        });

        cuerpo.querySelectorAll('.btn-estado').forEach(btn => btn.addEventListener('click', async () => {
            const area = areas.find(a => a.idArea === Number(btn.dataset.id));
            const activo = area.activo === false;
            if (!confirm(`${activo ? 'Activar' : 'Desactivar'} el área «${area.nombre}»?${activo ? '' : ' Se conservarán las asignaciones e historial existentes; no se permitirán nuevas asignaciones ni campañas en esta área.'}`))
                return;

            btn.disabled = true;
            try {
                await apiCallAuth(`/areas-operativas/${area.idArea}/estado`, { method: 'PATCH', body: { activo } });
                toast(`Área ${activo ? 'activada' : 'desactivada'}.`, 'success');
                await cargarAreas();
            } catch (err) {
                toast(err.message, 'error');
                btn.disabled = false;
            }
        }));
    }

    function contenedorPaginador() {
        let contenedor = el('areas-paginador');
        if (!contenedor) {
            contenedor = document.createElement('div');
            contenedor.id = 'areas-paginador';
            el('tabla-body').closest('table').after(contenedor);
            enlazarPaginador(contenedor, 'areas', nueva => {
                pagina = nueva;
                renderTabla();
            });
        }

        return contenedor;
    }

    el('buscador').addEventListener('input', (e) => {
        filtroTexto = e.target.value;
        pagina = 1;
        renderTabla();
    });

    // catalogo de criticidad, siempre muestra el nombre y no el id
    async function cargarCriticidades() {
        try {
            const criticidades = await apiCallAuth(ENDPOINT_CRITICIDADES);
            const select = el('f-criticidad');
            if (criticidades.length > 0) {
                select.innerHTML = criticidades.map((c) => `<option value="${c.idCriticidad}">${escapar(c.nivel)}</option>`).join('');
            } else {
                select.innerHTML = '<option value="">No hay niveles registrados</option>';
            }
        } catch (err) {
            console.warn('Error cargando criticidades:', err);
            el('f-criticidad').innerHTML = '<option value="">No se pudo cargar el catálogo</option>';
        }
    }

    // modal crear / editar
    const modal = el('modal-area');
    const form = el('form-area');

    async function abrirModal(id) {
        el('modal-error').classList.remove('is-visible');
        form.reset();
        el('f-id').value = id ?? '';
        el('modal-titulo').textContent = id ? 'Editar área operativa' : 'Nueva área operativa';

        if (id) {
            const a = areas.find((x) => x.idArea === id);
            if (a) {
                el('f-nombre').value = a.nombre;
                el('f-criticidad').value = a.idCriticidad ?? '';
            }
        }

        modal.showModal();
    }

    function cerrarModal() { modal.close(); }

    el('btn-nueva').addEventListener('click', () => abrirModal(null));
    el('modal-cancelar').addEventListener('click', cerrarModal);
    modal.addEventListener('click', (e) => { if (e.target === modal) cerrarModal(); });

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const id = el('f-id').value ? Number(el('f-id').value) : null;
        const idCriticidad = el('f-criticidad').value ? Number(el('f-criticidad').value) : null;
        const errorEl = el('modal-error');
        errorEl.classList.remove('is-visible');

        if (!idCriticidad) {
            errorEl.textContent = 'Selecciona (o ingresa el ID de) el nivel de criticidad.';
            errorEl.classList.add('is-visible');
            return;
        }

        const cuerpo = { nombre: el('f-nombre').value.trim(), idCriticidad };

        const btn = el('modal-guardar');
        const label = btn.querySelector('.btn-label');
        const original = label.textContent;
        btn.disabled = true;
        label.innerHTML = '<span class="spinner" aria-hidden="true"></span> Guardando…';

        try {
            if (id) {
                await apiCallAuth(`/areas-operativas/${id}`, { method: 'PUT', body: cuerpo });
            } else {
                await apiCallAuth('/areas-operativas', { method: 'POST', body: cuerpo });
            }

            cerrarModal();
            cargarAreas();
        } catch (err) {
            errorEl.textContent = err.message;
            errorEl.classList.add('is-visible');
        } finally {
            btn.disabled = false;
            label.textContent = original;
        }
    });

    cargarAreas();
    cargarCriticidades();
})();
