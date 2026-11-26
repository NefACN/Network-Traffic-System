import { icon } from '../components/icons.js';
import { ApiUsuariosRoles } from '../services/usuarios-roles-api.js';

import {
    escapeHtml,
    toast as mostrarToast,
    openDialog as abrirDialog,
    closeDialog as cerrarDialog,
    conCarga
} from '../utils/ui.js';
import { renderPaginador, enlazarPaginador, paginarEnNavegador, TAMANIO_PAGINA } from '../components/paginacion.js';

(function () {
    const actorLabel = name => ({
        Admin: 'Administrador',
        GerenteSucursal: 'Gerente de sucursal',
        Colaborador: 'Colaborador'
    })[name] || name;

    // estado
    const estado = {
        usuarios: [],
        roles: [],
        sucursales: [],
        areas: [],
        usuarioSeleccionadoId: null,
        buscarDebounce: null,
        // usuarios paginados en el servidor, roles paginados en el navegador
        paginaUsuarios: { items: [], total: 0, pagina: 1, tamanio: TAMANIO_PAGINA },
        paginaRoles: 1,
    };

    // contenedor del paginador debajo de la tabla, se crea una sola vez
    function contenedorPaginador(tbody, clave, alCambiar) {
        const id = `${clave}-paginador`;
        let contenedor = document.getElementById(id);
        if (!contenedor) {
            contenedor = document.createElement('div');
            contenedor.id = id;
            tbody.closest('table').after(contenedor);
            enlazarPaginador(contenedor, clave, alCambiar);
        }

        return contenedor;
    }

    // utilidades generales

    function iniciales(nombre, apellido) {
        const n = (nombre || '').trim()[0] || '';
        const a = (apellido || '').trim()[0] || '';
        return (n + a).toUpperCase() || '--';
    }

    function formatFecha(iso) {
        if (!iso) return '—';

        const fecha = new Date(iso);
        if (Number.isNaN(fecha.getTime())) return '—';

        return fecha.toLocaleDateString('es-BO', { year: 'numeric', month: 'short', day: 'numeric' });
    }

    // tabs
    document.querySelectorAll('.tab-btn').forEach((btn) => {
        btn.addEventListener('click', () => {
            document.querySelectorAll('.tab-btn').forEach((b) => {
                b.classList.remove('is-active');
                b.setAttribute('aria-selected', 'false');
            });
            btn.classList.add('is-active');
            btn.setAttribute('aria-selected', 'true');

            document.querySelectorAll('.tab-panel').forEach((p) => p.classList.remove('is-active'));
            document.getElementById(`panel-${btn.dataset.tab}`).classList.add('is-active');
        });
    });

    // sucursales y areas
    async function cargarSucursalesYAreas() {
        try {
            // conserva la asignacion actual aunque la unidad este desactivada
            const [sucs, areas] = await Promise.all([
                ApiUsuariosRoles.listarSucursales(),
                ApiUsuariosRoles.listarAreas()
            ]);
            estado.sucursales = sucs || [];
            estado.areas = areas || [];
        } catch (err) {
            console.error("No se pudieron cargar las sucursales o áreas", err);
            throw err;
        }
    }

    function poblarSelectSucursales(select, idSeleccionado) {
        select.innerHTML = '<option value="">Sin sucursal</option>';
        estado.sucursales.forEach(s => {
            const opt = document.createElement('option');
            opt.value = s.idSucursal;
            opt.textContent = s.nombre + (s.activo === false ? ' (inactiva)' : '');
            opt.disabled = s.activo === false && s.idSucursal != idSeleccionado;
            if (idSeleccionado && s.idSucursal == idSeleccionado) opt.selected = true;
            select.appendChild(opt);
        });
    }

    function poblarSelectAreas(select, idSeleccionado) {
        select.innerHTML = '<option value="">Sin área</option>';
        estado.areas.forEach(a => {
            const opt = document.createElement('option');
            opt.value = a.idArea;
            opt.textContent = a.nombre + (a.activo === false ? ' (inactiva)' : '');
            opt.disabled = a.activo === false && a.idArea != idSeleccionado;
            if (idSeleccionado && a.idArea == idSeleccionado) opt.selected = true;
            select.appendChild(opt);
        });
    }

    // usuarios
    const tbodyUsuarios = document.getElementById('tabla-usuarios-body');
    const usuariosCargando = document.getElementById('usuarios-cargando');
    const usuariosVacio = document.getElementById('usuarios-vacio');
    const inputBuscar = document.getElementById('buscar-usuarios');
    const filtroEstadoUsuarios = document.getElementById('filtro-estado-usuarios');

    let usersRequestId = 0;
    // sin pagina se conserva la actual, para recargar tras guardar
    async function cargarUsuarios(pagina = estado.paginaUsuarios.pagina) {
        const requestId = ++usersRequestId;
        usuariosCargando.hidden = false;
        usuariosVacio.hidden = true;
        tbodyUsuarios.innerHTML = '';

        try {
            const filtros = {
                busqueda: inputBuscar.value.trim() || undefined,
                activo: filtroEstadoUsuarios.value === '' ? undefined : filtroEstadoUsuarios.value === 'true',
                pagina,
                tamanio: TAMANIO_PAGINA,
            };
            const datos = await ApiUsuariosRoles.listarUsuarios(filtros);
            if (requestId !== usersRequestId) return;

            // si la pagina quedo fuera de rango, vuelve a la ultima con datos
            if (!datos?.items?.length && datos?.total > 0 && datos.pagina > 1) {
                return cargarUsuarios(Math.ceil(datos.total / datos.tamanio));
            }

            estado.paginaUsuarios = datos ?? { items: [], total: 0, pagina: 1, tamanio: TAMANIO_PAGINA };
            estado.usuarios = datos?.items || [];
            renderUsuarios();
        } catch (err) {
            if (requestId !== usersRequestId) return;
            mostrarToast(err.message || 'No se pudieron cargar los usuarios.', 'error');
        } finally {
            if (requestId === usersRequestId) usuariosCargando.hidden = true;
        }
    }

    function renderUsuarios() {
        tbodyUsuarios.innerHTML = '';
        contenedorPaginador(tbodyUsuarios, 'usuarios', pagina => cargarUsuarios(pagina)).innerHTML =
            renderPaginador(estado.paginaUsuarios, 'usuarios');
        if (!estado.usuarios.length) {
            usuariosVacio.hidden = false;
            return;
        }

        usuariosVacio.hidden = true;

        estado.usuarios.forEach((u) => {
            const idUsuario = u.idUsuario;
            const userName = u.nombreUsuario;
            const rolesHtml = (u.roles && u.roles.length)
                ? u.roles.map((r) => `<span class="chip">${escapeHtml(actorLabel(r))}</span>`).join('')
                : `<span class="chip is-muted">Colaborador</span>`;

            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td>
                    <div class="t-usuario">${escapeHtml(u.nombre)} ${escapeHtml(u.apellido)}</div>
                    <div class="t-meta">@${escapeHtml(userName)} · ${escapeHtml(u.email)}</div>
                </td>
                <td class="t-meta">${escapeHtml(u.sucursal || '—')}${u.area ? ' · ' + escapeHtml(u.area) : ''}</td>
                <td><div class="chip-list">${rolesHtml}</div></td>
                <td><span class="badge ${u.mfaActivo ? 'badge-activo' : 'badge-inactivo'}">${u.mfaActivo ? 'Sí' : 'No'}</span></td>
                <td><span class="badge ${u.activo ? 'badge-activo' : 'badge-inactivo'}">${u.activo ? 'Activo' : 'Inactivo'}</span></td>
                <td class="t-acciones">
                    <div class="row-actions">
                        <button type="button" class="btn-icon" data-accion="ver" title="Ver detalle">
                            ${icon('eye')}
                        </button>
                    </div>
                </td>
            `;
            tr.querySelector('[data-accion="ver"]').addEventListener('click', () => abrirDetalleUsuario(idUsuario));
            tbodyUsuarios.appendChild(tr);
        });
    }

    inputBuscar.addEventListener('input', () => {
        clearTimeout(estado.buscarDebounce);
        estado.buscarDebounce = setTimeout(() => cargarUsuarios(1), 350);
    });

    filtroEstadoUsuarios.addEventListener('change', () => cargarUsuarios(1));

    // crear usuario
    const dialogUsuarioNuevo = 'dialog-usuario-nuevo';
    const formUsuarioNuevo = document.getElementById('form-usuario-nuevo');
    const nuError = document.getElementById('nu-error');

    document.getElementById('btn-nuevo-usuario').addEventListener('click', async () => {
        try {
            await cargarSucursalesYAreas();
        } catch {
            mostrarToast('No se pudieron cargar las unidades organizacionales.', 'error');
            return;
        }

        formUsuarioNuevo.reset();
        nuError.textContent = '';
        poblarSelectRoles(document.getElementById('nu-rol'), { incluirVacio: true });
        poblarSelectSucursales(document.getElementById('nu-sucursal'));
        poblarSelectAreas(document.getElementById('nu-area'));
        abrirDialog(dialogUsuarioNuevo);
    });

    formUsuarioNuevo.addEventListener('submit', async (e) => {
        e.preventDefault();
        nuError.textContent = '';
        const boton = document.getElementById('nu-submit');
        boton.disabled = true;

        const payload = {
            nombre: document.getElementById('nu-nombre').value.trim(),
            apellido: document.getElementById('nu-apellido').value.trim(),
            nombreUsuario: document.getElementById('nu-usuario').value.trim(),
            email: document.getElementById('nu-email').value.trim(),
            telefono: document.getElementById('nu-telefono').value.trim() || null,
            idRolInicial: document.getElementById('nu-rol').value
                ? Number(document.getElementById('nu-rol').value)
                : null,
            idSucursal: document.getElementById('nu-sucursal').value
                ? Number(document.getElementById('nu-sucursal').value)
                : null,
            idArea: document.getElementById('nu-area').value ? Number(document.getElementById('nu-area').value) : null,
        };

        try {
            const respuesta = await ApiUsuariosRoles.crearUsuario(payload);
            cerrarDialog(dialogUsuarioNuevo);
            mostrarToast('Usuario creado correctamente.', 'success');
            mostrarCredencial(respuesta.passwordTemporal);
            cargarUsuarios();
        } catch (err) {
            nuError.textContent = err.message || 'No se pudo crear el usuario.';
        } finally {
            boton.disabled = false;
        }
    });

    // detalle y edicion de usuario
    const dialogDetalle = 'dialog-usuario-detalle';
    const formUsuarioEditar = document.getElementById('form-usuario-editar');
    const dtError = document.getElementById('dt-error');
    const dtRolError = document.getElementById('dt-rol-error');

    async function abrirDetalleUsuario(idUsuario) {
        try {
            await cargarSucursalesYAreas();
            const u = await ApiUsuariosRoles.obtenerUsuario(idUsuario);

            // acepta datos en camelCase o PascalCase
            estado.usuarioSeleccionadoId = u.idUsuario || u.IdUsuario;

            document.getElementById('dt-id').value = u.idUsuario || u.IdUsuario;
            document.getElementById('dt-avatar').textContent = iniciales(u.nombre || u.Nombre, u.apellido || u.Apellido);
            document.getElementById('dt-nombre-completo').textContent = `${u.nombre || u.Nombre} ${u.apellido || u.Apellido}`;

            const userName = u.nombreUsuario || u.NombreUsuario;
            const fechaCreacion = u.fechaCreacion || u.FechaCreacion;
            document.getElementById('dt-usuario-meta').textContent = `@${userName} · miembro desde ${formatFecha(fechaCreacion)}`;

            document.getElementById('dt-nombre').value = u.nombre || u.Nombre || '';
            document.getElementById('dt-apellido').value = u.apellido || u.Apellido || '';
            document.getElementById('dt-email').value = u.email || u.Email || '';
            document.getElementById('dt-telefono').value = u.telefono || u.Telefono || '';

            poblarSelectSucursales(document.getElementById('dt-sucursal'), u.idSucursal ?? u.IdSucursal);
            poblarSelectAreas(document.getElementById('dt-area'), u.idArea ?? u.IdArea);

            const mfaActivo = u.mfaActivo ?? u.MfaActivo;
            const mfaBadge = document.getElementById('dt-mfa-badge');
            mfaBadge.textContent = mfaActivo ? 'Configurado' : 'No configurado';
            mfaBadge.className = `badge ${mfaActivo ? 'badge-activo' : 'badge-inactivo'}`;

            const activo = u.activo ?? u.Activo;
            const toggleBtn = document.getElementById('dt-toggle-estado');
            toggleBtn.textContent = activo ? 'Desactivar usuario' : 'Activar usuario';
            toggleBtn.dataset.activo = String(activo);

            const rolesData = u.roles || u.Roles || [];
            renderRolesUsuario(rolesData);

            poblarSelectRoles(document.getElementById('dt-nuevo-rol'), {
                incluirVacio: true,
                excluirIds: rolesData.filter((r) => {
                    const rActivo = r.activo ?? r.Activo;
                    const rFechaFin = r.fechaFin ?? r.FechaFin;
                    return rActivo && (!rFechaFin || new Date(rFechaFin) > new Date());
                }).map((r) => r.idRol ?? r.IdRol),
            });

            dtError.textContent = '';
            dtRolError.textContent = '';
            abrirDialog(dialogDetalle);
        } catch (err) {
            mostrarToast(err.message || 'No se pudo cargar el detalle del usuario.', 'error');
        }
    }

    function renderRolesUsuario(roles) {
        const cont = document.getElementById('dt-roles-lista');
        if (!roles || !roles.length) {
            cont.innerHTML = '<p class="field-hint">Acceso predeterminado: Colaborador.</p>';
            return;
        }

        cont.innerHTML = '';
        roles.forEach((r) => {
            const activo = r.activo ?? r.Activo ?? false;
            const fechaFin = r.fechaFin ?? r.FechaFin;
            const fechaInicio = r.fechaInicio ?? r.FechaInicio;
            const nombreRol = r.nombreRol ?? r.NombreRol;
            const idRol = r.idRol ?? r.IdRol;

            const vigente = activo && (!fechaFin || new Date(fechaFin) > new Date());
            const row = document.createElement('div');
            row.className = 'role-row';
            row.innerHTML = `
                <div>
                    <div>${escapeHtml(actorLabel(nombreRol))} <span class="badge ${vigente ? 'badge-activo' : 'badge-inactivo'}">${vigente ? 'Vigente' : 'Finalizado'}</span></div>
                    <div class="role-row__meta">Desde ${formatFecha(fechaInicio)}${fechaFin ? ' · hasta ' + formatFecha(fechaFin) : ''}</div>
                </div>
            `;
            if (vigente) {
                const btnRevocar = document.createElement('button');
                btnRevocar.type = 'button';
                btnRevocar.className = 'btn-icon is-danger';
                btnRevocar.title = 'Revocar rol';
                btnRevocar.innerHTML = `${icon('close')}`;
                btnRevocar.addEventListener('click', () => revocarRol(idRol, nombreRol, btnRevocar));
                row.appendChild(btnRevocar);
            }

            cont.appendChild(row);
        });
    }

    formUsuarioEditar.addEventListener('submit', async (e) => {
        e.preventDefault();
        dtError.textContent = '';
        const id = Number(document.getElementById('dt-id').value);
        const boton = document.getElementById('dt-guardar');
        boton.disabled = true;

        const payload = {
            nombre: document.getElementById('dt-nombre').value.trim(),
            apellido: document.getElementById('dt-apellido').value.trim(),
            email: document.getElementById('dt-email').value.trim(),
            telefono: document.getElementById('dt-telefono').value.trim() || null,
            idSucursal: document.getElementById('dt-sucursal').value
                ? Number(document.getElementById('dt-sucursal').value)
                : null,
            idArea: document.getElementById('dt-area').value ? Number(document.getElementById('dt-area').value) : null,
        };

        try {
            await ApiUsuariosRoles.actualizarUsuario(id, payload);
            mostrarToast('Cambios guardados.', 'success');
            cargarUsuarios();
        } catch (err) {
            dtError.textContent = err.message || 'No se pudieron guardar los cambios.';
        } finally {
            boton.disabled = false;
        }
    });

    document.getElementById('dt-toggle-estado').addEventListener('click', async (e) => {
        const id = Number(document.getElementById('dt-id').value);
        const activoActual = e.target.dataset.activo === 'true';
        const nuevoEstado = !activoActual;
        const accion = nuevoEstado ? 'activar' : 'desactivar';

        if (!confirm(`¿Seguro que deseas ${accion} este usuario?${!nuevoEstado ? ' Se cerrarán sus sesiones activas.' : ''}`)) return;

        await conCarga(e.currentTarget, async () => {
            try {
                await ApiUsuariosRoles.cambiarEstadoUsuario(id, nuevoEstado);
                mostrarToast(`Usuario ${nuevoEstado ? 'activado' : 'desactivado'} correctamente.`, 'success');
                cerrarDialog(dialogDetalle);
                cargarUsuarios();
            } catch (err) {
                mostrarToast(err.message || `No se pudo ${accion} el usuario.`, 'error');
            }
        })();
    });

    document.getElementById('dt-resetear-password').addEventListener('click', async (e) => {
        const id = Number(document.getElementById('dt-id').value);
        if (!confirm('¿Generar una nueva contraseña temporal para este usuario? La contraseña actual dejará de funcionar.')) return;

        await conCarga(e.currentTarget, async () => {
            try {
                const respuesta = await ApiUsuariosRoles.resetearPassword(id);
                mostrarCredencial(respuesta.passwordTemporal);
                mostrarToast('Contraseña reestablecida.', 'success');
            } catch (err) {
                mostrarToast(err.message || 'No se pudo resetear la contraseña.', 'error');
            }
        })();
    });

    const botonAsignarRol = document.getElementById('dt-asignar-rol');
    botonAsignarRol.addEventListener('click', conCarga(botonAsignarRol, async () => {
        dtRolError.textContent = '';
        const id = Number(document.getElementById('dt-id').value);
        const idRol = Number(document.getElementById('dt-nuevo-rol').value);
        const fechaFin = document.getElementById('dt-nuevo-rol-fecha-fin').value;

        if (!idRol) {
            dtRolError.textContent = 'Selecciona un rol.';
            return;
        }

        try {
            await ApiUsuariosRoles.asignarRol(id, {
                idRol,
                fechaFin: fechaFin ? new Date(fechaFin).toISOString() : null,
            });
            mostrarToast('Actor actualizado. Las sesiones anteriores se cerrarán.', 'success');
            document.getElementById('dt-nuevo-rol-fecha-fin').value = '';
            abrirDetalleUsuario(id);
            cargarUsuarios();
        } catch (err) {
            dtRolError.textContent = err.message || 'No se pudo asignar el rol.';
        }
    }));

    async function revocarRol(idRol, nombreRol, boton) {
        const id = estado.usuarioSeleccionadoId;
        if (!confirm(`¿Revocar el rol "${nombreRol}"? El usuario volverá al acceso de colaborador.`)) return;

        await conCarga(boton, async () => {
            try {
                await ApiUsuariosRoles.revocarRol(id, idRol);
                mostrarToast('Rol revocado.', 'success');
                abrirDetalleUsuario(id);
                cargarUsuarios();
            } catch (err) {
                mostrarToast(err.message || 'No se pudo revocar el rol.', 'error');
            }
        })();
    }

    // credenciales generadas
    function mostrarCredencial(password) {
        document.getElementById('cred-valor').textContent = password;
        abrirDialog('dialog-credenciales');
    }

    document.getElementById('cred-copiar').addEventListener('click', async () => {
        const valor = document.getElementById('cred-valor').textContent;
        try {
            await navigator.clipboard.writeText(valor);
            mostrarToast('Contraseña copiada al portapapeles.', 'success');
        } catch {
            mostrarToast('No se pudo copiar automáticamente. Selecciónala manualmente.', 'error');
        }
    });

    // roles
    const tbodyRoles = document.getElementById('tabla-roles-body');
    const rolesCargando = document.getElementById('roles-cargando');
    const rolesVacio = document.getElementById('roles-vacio');
    const filtroEstadoRoles = document.getElementById('filtro-estado-roles');

    async function cargarRoles() {
        rolesCargando.hidden = false;
        rolesVacio.hidden = true;
        tbodyRoles.innerHTML = '';

        try {
            const roles = await ApiUsuariosRoles.listarRoles();
            estado.roles = roles || [];
            renderRoles();
        } catch (err) {
            mostrarToast(err.message || 'No se pudieron cargar los roles.', 'error');
        } finally {
            rolesCargando.hidden = true;
        }
    }

    function renderRoles() {
        const permissions = {
            Admin: 'Acceso completo a la administración y al módulo de IA.',
            GerenteSucursal: 'Seguimiento preventivo de su sucursal. Solo resultados agregados y consulta.',
            Colaborador: 'Portal personal de concientización, cursos y logros.',
        };
        const filtrados = estado.roles.filter(r => filtroEstadoRoles.value === ''
            || r.activo === (filtroEstadoRoles.value === 'true'));
        const datos = paginarEnNavegador(filtrados, estado.paginaRoles);
        estado.paginaRoles = datos.pagina;
        const rows = datos.items;
        contenedorPaginador(tbodyRoles, 'roles', pagina => {
            estado.paginaRoles = pagina;
            renderRoles();
        }).innerHTML = renderPaginador(datos, 'roles');
        tbodyRoles.innerHTML = rows
            .map(r => `<tr><td class="t-usuario">${escapeHtml(actorLabel(r.nombre))}</td><td class="t-meta">${escapeHtml(permissions[r.nombre] || r.descripcion || '')}</td><td><span class="badge ${r.activo ? 'badge-activo' : 'badge-inactivo'}">${r.activo ? 'Activo' : 'Inactivo'}</span></td><td>Actor del sistema</td></tr>`)
            .join('');
        rolesVacio.hidden = rows.length > 0;
    }

    filtroEstadoRoles.addEventListener('change', () => {
        estado.paginaRoles = 1;
        renderRoles();
    });

    function poblarSelectRoles(select, { incluirVacio = false, excluirIds = [] } = {}) {
        const activos = estado.roles.filter((r) => r.activo && !excluirIds.includes(r.idRol));
        select.innerHTML = '';
        if (incluirVacio) {
            const opt = document.createElement('option');
            opt.value = '';
            opt.textContent = select.id === 'nu-rol' ? 'Colaborador (predeterminado)' : 'Seleccionar actor…';
            select.appendChild(opt);
        }

        activos.forEach((r) => {
            const opt = document.createElement('option');
            opt.value = r.idRol;
            opt.textContent = actorLabel(r.nombre);
            select.appendChild(opt);
        });
    }

    // carga inicial
    (async function init() {
        await cargarRoles();
        try {
            await cargarSucursalesYAreas();
        } catch {
            mostrarToast('No se pudieron cargar las unidades organizacionales.', 'error');
        }

        await cargarUsuarios();
    })();
})();
