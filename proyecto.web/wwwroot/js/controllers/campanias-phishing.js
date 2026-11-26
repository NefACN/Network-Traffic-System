import { icon } from '../components/icons.js';
import { apiCallAuth } from '../services/api.js';
import { el, escapeHtml as esc, errorMessage as mensajeDeError, conCarga } from '../utils/ui.js';
import { renderPaginador, enlazarPaginador, conPagina, ultimaPagina } from '../components/paginacion.js';
import { renderEmailPreview } from '../components/quiz.js';
import {
    TIPOS,
    TIPOS_AYUDA,
    ALINEACIONES,
    dificultad,
    etiqueta,
    categoriaSenales,
    claseDificultad
} from '../utils/phish-scale.js';

(function () {
    const dialogCampania = el('dialog-campania');
    const formCampania = el('form-campania');
    const dialogPlantilla = el('dialog-plantilla');
    const formPlantilla = el('form-plantilla');

    const ESTADO_EN_EJECUCION = 'En Ejecución';
    let revisionValida = false;
    let governanceNote = '';
    let revisionVersion = 0;
    let participantesDisponibles = [];
    let areasActivas = [];
    const plantillasCache = new Map();
    let gobernanza = null;

    // listados paginados en el servidor
    let paginaCampanias = 1, paginaPlantillas = 1, campaniasRequestId = 0, detalleCampania = null;

    function contenedorPaginador(tbody, clave, alCambiar) {
        const id = `${clave}-paginador`;
        let contenedor = el(id);
        if (!contenedor) {
            contenedor = document.createElement('div');
            contenedor.id = id;
            tbody.closest('table').after(contenedor);
            enlazarPaginador(contenedor, clave, alCambiar);
        }

        return contenedor;
    }

    // deshabilita el boton durante la accion para evitar envios dobles
    const protegido = (boton, accion) => boton ? conCarga(boton, accion)() : accion();
    const aprobacionLabel = {
        borrador: 'Sin solicitar',
        pendiente: 'Aprobación pendiente',
        aprobada: 'Aprobada',
        rechazada: 'Rechazada'
    };
    const aprobacionClase = { borrador: 'is-muted', pendiente: 'nist-media', aprobada: 'nist-baja', rechazada: 'nist-alta' };
    const chipNist = valor => `<span class="chip ${claseDificultad(valor)}">${esc(etiqueta(valor))}</span>`;
    const tipoLabel = tipo => ({
        inicial: 'Inicial · pretest',
        posterior: 'Posterior · postest',
        seguimiento: 'Seguimiento'
    })[tipo] || 'Sin clasificar';
    const fecha = value => value ? new Date(value).toLocaleString('es-BO', { timeZone: 'America/La_Paz' }) : 'Sin fecha';
    const fechaLocal = value => {
        if (!value) return '';
        const d = new Date(value);
        return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
    };

    function vistaCorreo(container, html, asunto) {
        renderEmailPreview(container, html);
        const subject = document.createElement('p');
        subject.textContent = `Asunto: ${asunto || 'Sin asunto'}`;
        container.prepend(subject);
    }

    function localAIso(valorDatetimeLocal) {
        if (!valorDatetimeLocal) return null;

        const fecha = new Date(valorDatetimeLocal);
        return isNaN(fecha.getTime()) ? null : fecha.toISOString();
    }

    // inicializacion y pestanas
    async function inicializar() {
        try {
            gobernanza = await apiCallAuth('/gobernanza/politica');
        } catch {
            gobernanza = null;
        }

        await Promise.all([
            cargarCampanias(),
            cargarTablaPlantillas(),
            cargarCatalogos()
        ]);

        document.querySelectorAll('.tab-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('is-active'));
                document.querySelectorAll('.tab-panel').forEach(p => p.classList.remove('is-active'));

                btn.classList.add('is-active');
                el(`panel-${btn.dataset.tab}`).classList.add('is-active');

                if (btn.dataset.tab === 'plantillas') cargarTablaPlantillas();
                if (btn.dataset.tab === 'campanias') cargarCampanias();
            });
        });

        if (new URLSearchParams(location.search).has('refuerzo')) await abrirNuevaCampania();

        // delegacion de eventos en las tablas para evitar onclick inline
        el('tbody-campanias').addEventListener('click', (e) => {
            const btnDetalle = e.target.closest('.js-ver-detalle');
            if (btnDetalle) return verDetalle(parseInt(btnDetalle.dataset.id, 10), btnDetalle.dataset.branch);

            const btnEjecutar = e.target.closest('.js-ejecutar');
            if (btnEjecutar) return ejecutarMotor(parseInt(btnEjecutar.dataset.id, 10), btnEjecutar);

            const editar = e.target.closest('.js-editar-campania');
            if (editar) return editarCampania(Number(editar.dataset.id));

            const finalizar = e.target.closest('.js-finalizar');
            if (finalizar) return finalizarCampania(Number(finalizar.dataset.id), finalizar);

            const solicitar = e.target.closest('.js-solicitar');
            if (solicitar) return solicitarAprobacion(Number(solicitar.dataset.id), solicitar);

            const revisar = e.target.closest('.js-revisar');
            if (revisar) return revisarCampania(Number(revisar.dataset.id), revisar.dataset.aprobar === 'true', revisar);
        });

        el('tbody-plantillas').addEventListener('click', (e) => {
            const btnEditar = e.target.closest('.js-editar-plantilla');
            if (btnEditar) return editarPlantilla(parseInt(btnEditar.dataset.id, 10));

            const btnEliminar = e.target.closest('.js-eliminar-plantilla');
            if (btnEliminar) return eliminarPlantilla(parseInt(btnEliminar.dataset.id, 10), btnEliminar);

            const estado = e.target.closest('.js-estado-plantilla');
            if (estado) return estadoPlantilla(Number(estado.dataset.id), estado.dataset.habilitada !== 'true', estado);

            const preview = e.target.closest('.js-preview-plantilla');
            if (preview) return vistaPlantilla(Number(preview.dataset.id));
        });

        // refresco solo con la pestana visible
        const refrescoCampanias = setInterval(() => {
            if (!document.hidden && !document.querySelector('dialog[open]')) cargarCampanias();
        }, 15000);
        document.addEventListener('visibilitychange', () => {
            if (!document.hidden && !document.querySelector('dialog[open]')) cargarCampanias();
        });
        window.addEventListener('pagehide', e => { if (!e.persisted) clearInterval(refrescoCampanias); });
        window.addEventListener('pageshow', e => {
            if (e.persisted && !document.querySelector('dialog[open]')) cargarCampanias();
        });
    }

    // campanias
    el('filtro-estado-campania')?.addEventListener('change', () => cargarCampanias(1));

    // sin pagina se conserva la actual
    async function cargarCampanias(pagina = paginaCampanias) {
        const tbody = el('tbody-campanias');
        const requestId = ++campaniasRequestId;
        const paginador = contenedorPaginador(tbody, 'campanias', nueva => cargarCampanias(nueva));
        try {
            // HU-09: campanias filtradas por estado en el servidor
            const filtro = el('filtro-estado-campania')?.value ?? '';
            const datos = await apiCallAuth(conPagina(
                filtro ? `/campanias-phishing?estado=${encodeURIComponent(filtro)}` : '/campanias-phishing',
                pagina));
            if (requestId !== campaniasRequestId) return;

            // si la pagina queda vacia se vuelve a la ultima con datos
            if (!datos.items.length && datos.total > 0 && datos.pagina > 1)
                return cargarCampanias(ultimaPagina(datos));

            paginaCampanias = datos.pagina;
            paginador.innerHTML = renderPaginador(datos, 'campanias');
            const campanias = datos.items;
            if (campanias.length === 0) {
                tbody.innerHTML = `<tr><td colspan="7" class="empty-state">${filtro ? 'No hay campañas en este estado.' : 'No hay campañas registradas.'}</td></tr>`;
                return;
            }

            tbody.innerHTML = campanias.map(c => {
                const enEjecucion = c.idEstado ? c.idEstado === 2 : c.estado === ESTADO_EN_EJECUCION;
                const pendiente = c.idEstado ? c.idEstado === 1 : c.estado === 'Pendiente';
                const aprobacion = c.estadoAprobacion || 'borrador';
                const requiere = gobernanza?.requiereAprobacion !== false;
                const ejecutable = pendiente && (!requiere || aprobacion === 'aprobada');

                return `
                <tr>
                    <td>#${c.idCampania}</td>
                    <td style="font-weight: 500;">${esc(c.titulo)}<div class="t-meta">${esc(tipoLabel(c.tipoEvaluacion))}</div></td>
                    <td>${esc(c.plantilla)}</td>
                    <td><span class="chip">${c.totalAreasSegmentadas} áreas</span><div class="t-meta">${esc(c.sucursalObjetivo || 'Todas las sucursales')}</div></td>
                    <td>
                        <span class="chip ${enEjecucion ? 'badge-activo' : 'is-muted'}">
                            ${esc(c.estado)}
                        </span>
                        <div class="t-meta">${c.enviosAceptados || 0} aceptados · ${c.enviosFallidos || 0} fallidos · ${c.enviosPendientes || 0} pendientes</div>
                        ${pendiente && requiere ? `<span class="chip ${aprobacionClase[aprobacion] || ''}">${esc(aprobacionLabel[aprobacion] || aprobacion)}</span>` : ''}
                    </td>
                    <td>${c.fechaInicio ? new Date(c.fechaInicio).toLocaleDateString() : 'No definida'}</td>
                    <td class="t-acciones">
                        <div class="row-actions">
                            <button type="button" class="btn-icon js-ver-detalle" data-id="${c.idCampania}" data-branch="${c.idSucursalObjetivo || ''}" title="Ver seguimiento" aria-label="Ver seguimiento">
                                ${icon('chart')}
                            </button>
                            ${pendiente ? `<button type="button" class="btn js-editar-campania" data-id="${c.idCampania}">Editar</button>` : ''}
                            ${pendiente && requiere && (aprobacion === 'borrador' || aprobacion === 'rechazada') ? `<button type="button" class="btn js-solicitar" data-id="${c.idCampania}">Solicitar aprobación</button>` : ''}
                            ${pendiente && c.puedeRevisar ? `<button type="button" class="btn js-revisar" data-id="${c.idCampania}" data-aprobar="true">Aprobar</button><button type="button" class="btn js-revisar" data-id="${c.idCampania}" data-aprobar="false">Rechazar</button>` : ''}
                            ${ejecutable ? `<button type="button" class="btn-icon is-danger js-ejecutar" data-id="${c.idCampania}" title="Lanzar Simulación (Enviar correos)">
                                ${icon('play')}
                            </button>` : ''}
                            ${enEjecucion ? `<button type="button" class="btn js-finalizar" data-id="${c.idCampania}">Finalizar</button>` : ''}
                        </div>
                    </td>
                </tr>
            `;
            }).join('');
        } catch (err) {
            if (requestId !== campaniasRequestId) return;
            console.error('Error al cargar campañas:', err);
            paginador.innerHTML = '';
            tbody.innerHTML = `<tr><td colspan="7" class="empty-state" style="color:var(--error);">Error: ${esc(mensajeDeError(err, 'desconocido'))}</td></tr>`;
        }
    }

    async function cargarCatalogos() {
        try {
            const branches = await apiCallAuth('/sucursales?activo=true');
            el('c-sucursal').innerHTML = '<option value="">Todas las sucursales activas</option>'
                + branches.map(b => `<option value="${b.idSucursal}">${esc(b.nombre)}</option>`).join('');
        } catch { el('c-sucursal').innerHTML = '<option value="">Todas las sucursales activas</option>'; }

        try {
            const plantillas = await apiCallAuth('/campanias-phishing/plantillas/catalogo?habilitada=true');
            const select = el('c-plantilla');
            if (plantillas.length > 0) {
                select.innerHTML = `<option value="">Selecciona una plantilla…</option>` +
                    plantillas.map(p => `<option value="${p.idPlantilla}">${esc(p.nombre)}</option>`).join('');
            } else {
                select.innerHTML = `<option value="">Debes crear una plantilla primero</option>`;
            }
        } catch (err) {
            console.error('Error cargando plantillas:', err);
        }

        try {
            const areas = await apiCallAuth('/areas-operativas?activo=true');
            areasActivas = areas.map(a => ({ idArea: a.idArea, area: a.nombre, activa: true }));
            const select = el('c-areas');
            if (areas.length > 0) {
                select.innerHTML = areas.map(a =>
                    `<option value="${a.idArea}">${esc(a.nombre)} (Criticidad del negocio: ${esc(a.nivelCriticidad || 'N/A')})</option>`
                ).join('');
            } else {
                select.innerHTML = `<option value="">No hay áreas creadas</option>`;
            }
        } catch (err) {
            console.warn('Endpoint de areas-operativas no encontrado aún.');
        }

        try {
            const criticidades = await apiCallAuth('/campanias-phishing/criticidades');
            const selectCrit = el('p-criticidad');
            if (criticidades.length > 0) {
                selectCrit.innerHTML = `<option value="">Selecciona un nivel…</option>` +
                    criticidades.map(c => `<option value="${c.idCriticidad}">${esc(c.nivel)}</option>`).join('');
            } else {
                selectCrit.innerHTML = `<option value="">No hay niveles registrados</option>`;
            }
        } catch (err) {
            console.warn('Error cargando criticidades:', err);
        }

        try {
            const categorias = await apiCallAuth('/campanias-phishing/categorias-educativas');
            const selectCat = el('p-categoria');
            if (categorias && categorias.length > 0) {
                selectCat.innerHTML = `<option value="">Selecciona una categoría…</option>` +
                    categorias.map(cat => `<option value="${cat.idCategoria}">${esc(cat.nombre)}</option>`).join('');
            } else {
                selectCat.innerHTML = `<option value="">No hay categorías registradas</option>`;
            }
        } catch (err) {
            console.warn('Error cargando categorías educativas:', err);
        }
    }

    async function abrirNuevaCampania() {
        await cargarCatalogos();
        formCampania.reset();
        el('c-id').value = '';
        el('modal-campania-titulo').textContent = 'Configurar nueva campaña';
        invalidarRevision();
        el('c-preview').hidden = true;
        el('c-error').style.display = 'none';
        plantillasCache.clear();
        mostrarNistCampania();

        const params = new URLSearchParams(location.search);
        if (params.has('refuerzo')) {
            el('c-sucursal').value = params.get('idSucursal') || '';
            for (const option of el('c-areas').options) option.selected = option.value === params.get('idArea');
            el('c-titulo').value = 'Seguimiento preventivo';
            el('c-objetivo').value = 'Reforzar el comportamiento preventivo en el área seleccionada.';
        }

        dialogCampania.showModal();
        el('c-titulo').focus();
        dialogCampania.querySelector('.modal-body').scrollTop = 0;
    }

    el('btn-nueva-campania').addEventListener('click', abrirNuevaCampania);

    function invalidarRevision() {
        revisionVersion++;
        revisionValida = false;
        participantesDisponibles = [];
        el('c-participantes').replaceChildren();
        el('c-submit').disabled = true;
        el('c-resumen').textContent = 'Revisa los participantes y la programación antes de guardar.';
    }

    function mostrarErrorCampania(err) {
        el('c-error').textContent = mensajeDeError(err, 'No se pudo completar la operación.');
        el('c-error').style.display = 'block';
    }

    function resumen() {
        const seleccionados = el('c-participantes').selectedOptions.length;
        el('c-resumen').textContent = `${seleccionados || participantesDisponibles.length} participantes · ${tipoLabel(el('c-tipo').value)}\nInicio: ${fecha(localAIso(el('c-fecha-inicio').value))} · Fin: ${el('c-fecha-fin').value ? fecha(localAIso(el('c-fecha-fin').value)) : 'Cierre manual'}\nLa elegibilidad se comprobará de nuevo al guardar y ejecutar.${governanceNote ? `\n${governanceNote}` : ''}`;
    }

    async function revisarParticipantes(ids = []) {
        const version = revisionVersion;
        el('c-error').style.display = 'none';
        const areas = [...el('c-areas').selectedOptions].map(o => Number(o.value)).filter(Boolean);
        const button = el('c-revisar');
        button.disabled = true;
        try {
            const data = await apiCallAuth('/campanias-phishing/participantes', {
                method: 'POST',
                body: {
                    areasSeleccionadas: areas,
                    idSucursalObjetivo: el('c-sucursal').value ? Number(el('c-sucursal').value) : null
                }
            });
            if (version !== revisionVersion) return;

            participantesDisponibles = data.participantes;
            el('c-participantes').innerHTML = data.participantes
                .map(p => `<option value="${p.idUsuario}" ${ids.includes(p.idUsuario) ? 'selected' : ''}>${esc(p.nombre)} · ${esc(p.correo)} · ${esc(p.sucursal)} / ${esc(p.area)}${p.limiteFrecuencia ? ' · límite de frecuencia: se omitirá' : ''}</option>`)
                .join('');
            governanceNote = [
                data.excluidosPorLista ? `${data.excluidosPorLista} persona(s) en la lista de exclusión no se incluyen.` : '',
                data.alcanzaronLimite ? `${data.alcanzaronLimite} persona(s) ya recibieron ${data.limiteFrecuencia} simulación(es) en 30 días y se omitirán al enviar.` : ''].filter(Boolean).join(' ');
            revisionValida = data.total > 0;
            if (ids.some(id => !data.participantes.some(p => p.idUsuario === id))) {
                revisionValida = false;
                throw new Error('Algunos participantes anteriores ya no están disponibles. Revisa y confirma la selección de nuevo.');
            }

            if (!revisionValida) throw new Error('No existen colaboradores válidos para esta selección.');
            resumen();
        } catch (err) {
            if (version === revisionVersion) {
                revisionValida = false;
                mostrarErrorCampania(err);
            }
        } finally {
            button.disabled = false;
            if (version === revisionVersion) el('c-submit').disabled = !revisionValida;
        }
    }

    el('c-revisar').addEventListener('click', () => revisarParticipantes(
        [...el('c-participantes').selectedOptions].map(o => Number(o.value))));
    for (const id of ['c-sucursal', 'c-areas', 'c-plantilla'])
        el(id).addEventListener('change', () => {
            invalidarRevision();
            el('c-preview').hidden = true;
        });
    for (const id of ['c-areas', 'c-plantilla']) el(id).addEventListener('change', () => mostrarNistCampania());

    // dificultad NIST de cada area seleccionada con la plantilla elegida
    let nistVersion = 0;
    async function mostrarNistCampania() {
        const version = ++nistVersion, box = el('c-nist');
        const id = Number(el('c-plantilla').value);
        const areas = [...el('c-areas').selectedOptions].map(o => Number(o.value)).filter(Boolean);
        if (!id) {
            box.innerHTML = '<p class="field-hint">Selecciona una plantilla y las áreas para ver la dificultad NIST Phish Scale que enfrentará cada área.</p>';
            return;
        }

        try {
            if (!plantillasCache.has(id)) plantillasCache.set(id, await apiCallAuth(`/campanias-phishing/plantillas/${id}`));
            if (version !== nistVersion) return;

            const ps = plantillasCache.get(id).phishScale;
            if (!ps.cantidadSenales) {
                box.innerHTML = '<p class="form-error">Esta plantilla no tiene señales de alerta calificadas: la dificultad quedará «Sin calificar» y sus resultados no podrán ajustarse por dificultad. Califícala en la pestaña Plantillas.</p>';
                return;
            }

            const filas = ps.porArea.filter(a => areas.includes(a.idArea));
            box.innerHTML = `<p><strong>NIST Phish Scale:</strong> ${ps.cantidadSenales} señales (${esc(ps.categoriaSenales)}).</p>`
                + (filas.length
                    ? `<ul class="nist-lista">${filas.map(a => `<li>${esc(a.area)}: alineación ${esc(ALINEACIONES[a.alineacion])} → ${chipNist(a.dificultad)}</li>`).join('')}</ul>`
                    : '<p class="field-hint">Selecciona áreas para ver su dificultad.</p>');
        } catch (err) {
            if (version === nistVersion) box.textContent = mensajeDeError(err, 'No se pudo calcular la dificultad.');
        }
    }

    for (const id of ['c-fecha-inicio', 'c-fecha-fin', 'c-tipo'])
        el(id).addEventListener('change', () => { if (revisionValida) resumen(); });
    el('c-participantes').addEventListener('change', resumen);
    el('c-todos').addEventListener('click', () => {
        for (const o of el('c-participantes').options) o.selected = false;
        if (revisionValida) resumen();
    });
    el('c-ver-plantilla').addEventListener('click', async () => {
        try {
            if (!el('c-plantilla').value) throw new Error('Selecciona una plantilla para ver el correo.');
            const p = await apiCallAuth(`/campanias-phishing/plantillas/${Number(el('c-plantilla').value)}`);
            el('c-preview').hidden = false;
            vistaCorreo(el('c-preview'), p.cuerpoHtml, p.asuntoCorreo);
        } catch (err) { mostrarErrorCampania(err); }
    });

    async function editarCampania(id) {
        try {
            await cargarCatalogos();
            const c = await apiCallAuth(`/campanias-phishing/${id}`);
            if (c.idEstado !== 1) throw new Error('La campaña ya no está pendiente.');

            formCampania.reset();
            invalidarRevision();
            el('c-error').style.display = 'none';
            el('c-preview').hidden = true;
            el('c-id').value = id;
            el('c-titulo').value = c.titulo;
            el('c-objetivo').value = c.objetivo;
            el('c-tipo').value = ['inicial', 'posterior', 'seguimiento'].includes(c.tipoEvaluacion) ? c.tipoEvaluacion : 'seguimiento';
            el('c-plantilla').value = c.idPlantilla;
            el('c-sucursal').value = c.idSucursalObjetivo || '';
            el('c-fecha-inicio').value = fechaLocal(c.fechaInicio);
            el('c-fecha-fin').value = fechaLocal(c.fechaFin);
            for (const o of el('c-areas').options) o.selected = c.idsAreas.includes(Number(o.value));
            plantillasCache.delete(c.idPlantilla);
            mostrarNistCampania();
            el('modal-campania-titulo').textContent = 'Editar campaña pendiente';
            dialogCampania.showModal();
            el('c-titulo').focus();
            dialogCampania.querySelector('.modal-body').scrollTop = 0;
            await revisarParticipantes(c.idsParticipantes || []);
        } catch (err) { alert(mensajeDeError(err)); }
    }

    async function solicitarAprobacion(id, boton) {
        if (!confirm('¿Solicitar la aprobación de esta campaña? Otro administrador deberá revisarla antes de enviarla. Si la editas después, deberás solicitarla de nuevo.')) return;

        await protegido(boton, async () => {
            try {
                await apiCallAuth(`/campanias-phishing/${id}/solicitar-aprobacion`, { method: 'POST' });
                await cargarCampanias();
            } catch (err) { alert(mensajeDeError(err)); }
        });
    }

    async function revisarCampania(id, aprobar, boton) {
        const comentario = prompt(aprobar
            ? 'Comentario de aprobación (opcional). Confirma que el escenario es ético, no punitivo y adecuado para el área.'
            : 'Motivo del rechazo (obligatorio):', '');
        if (comentario === null) return;
        if (!aprobar && !comentario.trim()) {
            alert('Indica el motivo del rechazo.');
            return;
        }

        await protegido(boton, async () => {
            try {
                await apiCallAuth(`/campanias-phishing/${id}/revisar`, {
                    method: 'POST',
                    body: { aprobar, comentario: comentario.trim() || null }
                });
                await cargarCampanias();
            } catch (err) { alert(mensajeDeError(err)); }
        });
    }

    async function finalizarCampania(id, boton) {
        if (!confirm('¿Finalizar esta campaña? Si su periodo aún no concluyó, se cerrará anticipadamente. Se conservarán los resultados y se cancelarán los envíos aún pendientes.')) return;

        await protegido(boton, async () => {
            try {
                await apiCallAuth(`/campanias-phishing/${id}/finalizar`, { method: 'POST', body: { confirmar: true } });
                await cargarCampanias();
            } catch (err) { alert(mensajeDeError(err)); }
        });
    }

    formCampania.addEventListener('submit', async (e) => {
        e.preventDefault();
        const errorEl = el('c-error');
        errorEl.style.display = 'none';
        if (!revisionValida) {
            mostrarErrorCampania(new Error('Revisa los participantes antes de guardar.'));
            return;
        }

        const idPlantilla = el('c-plantilla').value;
        if (!idPlantilla) {
            errorEl.textContent = 'Selecciona una plantilla.';
            errorEl.style.display = 'block';
            return;
        }

        const selectAreas = el('c-areas');
        const areasSeleccionadas = Array.from(selectAreas.selectedOptions)
            .map(opt => parseInt(opt.value, 10))
            .filter(v => !isNaN(v));

        if (areasSeleccionadas.length === 0) {
            errorEl.textContent = 'Selecciona al menos un área operativa.';
            errorEl.style.display = 'block';
            return;
        }

        const fechaInicioIso = localAIso(el('c-fecha-inicio').value);
        const fechaFinIso = localAIso(el('c-fecha-fin').value);

        if (el('c-fecha-fin').value && !fechaFinIso) {
            errorEl.textContent = 'La fecha de finalización no es válida.';
            errorEl.style.display = 'block';
            return;
        }

        const btnSubmit = el('c-submit');
        btnSubmit.disabled = true;

        try {
            const id = el('c-id').value;
            await apiCallAuth(id ? `/campanias-phishing/${Number(id)}` : '/campanias-phishing', {
                method: id ? 'PUT' : 'POST',
                body: {
                    titulo: el('c-titulo').value.trim(),
                    objetivo: el('c-objetivo').value.trim(),
                    tipoEvaluacion: el('c-tipo').value,
                    participantesSeleccionados: [...el('c-participantes').selectedOptions].map(o => Number(o.value)),
                    idPlantilla: parseInt(idPlantilla, 10),
                    fechaInicioProgramada: fechaInicioIso,
                    fechaFinProgramada: fechaFinIso,
                    idSucursalObjetivo: el('c-sucursal').value ? Number(el('c-sucursal').value) : null,
                    areasSeleccionadas: areasSeleccionadas
                }
            });
            dialogCampania.close();
            await cargarCampanias();
        } catch (err) {
            console.error('Error al crear campaña:', err);
            errorEl.textContent = mensajeDeError(err, 'Error al crear la campaña.');
            errorEl.style.display = 'block';
        } finally {
            btnSubmit.disabled = false;
        }
    });

    async function ejecutarMotor(idCampania, boton) {
        if (!confirm('🚨 ALERTA: ¿Estás seguro de lanzar esta simulación?\n\nSe enviarán correos reales a colaboradores de las áreas y sucursal seleccionadas. Los participantes deben tener sucursal activa, área y correo asignados.')) return;

        await protegido(boton, async () => {
            try {
                const res = await apiCallAuth(`/campanias-phishing/${idCampania}/ejecutar`, { method: 'POST' });
                alert(`${res.mensaje}\nColaboradores omitidos por sucursal sin asignar o inactiva: ${res.colaboradoresSinAsignacion || 0}.`);
                await cargarCampanias();
            } catch (err) {
                console.error('Error al ejecutar campaña:', err);
                alert('Error al ejecutar: ' + mensajeDeError(err, 'intenta de nuevo.'));
            }
        });
    }

    async function verDetalle(idCampania, branch) {
        try {
            const c = await apiCallAuth(`/campanias-phishing/${idCampania}`);
            const participantes = await apiCallAuth(conPagina(`/campanias-phishing/${idCampania}/participantes`, 1));
            detalleCampania = idCampania;
            el('campania-detalle').innerHTML = `<h3>${esc(c.titulo)}</h3><p>${esc(c.objetivo || 'Objetivo sin registrar')}</p>
                <p>${esc(tipoLabel(c.tipoEvaluacion))} · ${esc(c.estado)}</p><p>Inicio: ${esc(fecha(c.fechaInicio))}<br>Fin programado: ${esc(fecha(c.fechaFin))}<br>Cierre real: ${esc(fecha(c.fechaFinalizacion))}${c.finalizacionAnticipada ? ' (anticipado)' : ''}</p>
                <p>${esc(c.sucursalObjetivo || 'Todas las sucursales')} · ${(c.areasSegmentadas || []).map(esc).join(', ')}</p>
                <p>${c.totalEnviosGenerados} participantes · ${c.enviosAceptados || 0} aceptados · ${c.enviosFallidos || 0} fallidos · ${c.enviosPendientes || 0} pendientes.</p>
                ${detalleNist(c)}
                <p>Aprobación: ${esc(aprobacionLabel[c.estadoAprobacion] || c.estadoAprobacion || 'Sin solicitar')}${c.solicitante ? ` · solicitada por ${esc(c.solicitante)} el ${esc(fecha(c.fechaSolicitud))}` : ''}${c.revisor ? ` · revisada por ${esc(c.revisor)} el ${esc(fecha(c.fechaRevision))}` : ''}${c.comentarioRevision ? `<br>Comentario: ${esc(c.comentarioRevision)}` : ''}</p>
                <p>Aperturas: ${c.totalCorreosAbiertos} · Clics: ${c.totalClicsVulnerados} · Credenciales en página señuelo: ${c.paginaSenuelo === 'credenciales' ? Number(c.totalCredenciales) || 0 : 'no aplica'} · Reportes: ${c.totalReportes ?? 0} (${c.reportesSinClickPrevio ?? 0} sin clic previo). ${c.idEstado === 2 ? 'Resultados parciales.' : c.fechaFinalizacion ? 'Evaluación finalizada.' : c.idEstado === 1 ? 'Campaña pendiente.' : 'Resultados conservados.'}</p>
                <h3>Participantes y avance de envío</h3><div id="detalle-participantes">${tablaParticipantes(participantes)}</div>
                <a class="btn" href="seguimiento-preventivo.html?campania=${idCampania}${branch ? `&idSucursal=${Number(branch)}` : ''}">Consultar indicadores</a> <a class="btn" href="resultados-campanias.html?campania=${idCampania}">Ver interacciones y resultados individuales</a><div id="detalle-correo"></div>`;
            if (c.cuerpoHtmlRegistro) vistaCorreo(el('detalle-correo'), c.cuerpoHtmlRegistro, c.asuntoRegistro);
            el('dialog-campania-detalle').showModal();
        } catch (err) { alert(mensajeDeError(err)); }
    }

    // tabla de envios de la campania abierta, con su paginador
    function tablaParticipantes(datos) {
        if (!datos.total) return '<p>Todavía no hay envíos registrados. Revisa el alcance desde la edición de la campaña pendiente.</p>';

        return `<div class="table-wrap"><table class="log-table"><thead><tr><th>Colaborador</th><th>Sucursal / área al enviar</th><th>Envío</th></tr></thead><tbody>${datos.items.map(p => `<tr><td>${esc(p.nombre)}<br>${esc(p.correo)}</td><td>${esc(p.sucursal || 'Sin contexto')} / ${esc(p.area || 'Sin contexto')}</td><td>${esc(({ pending: 'Pendiente', accepted: 'Aceptado por el proveedor', failed: 'Fallido', canceled: 'Cancelado al cerrar', legacy: 'Histórico' })[p.estadoEntrega] || p.estadoEntrega)}<br>${esc(fecha(p.fechaEnvio))}</td></tr>`).join('')}</tbody></table></div>${renderPaginador(datos, 'participantes')}`;
    }

    enlazarPaginador(el('campania-detalle'), 'participantes', async pagina => {
        const id = detalleCampania;
        try {
            const datos = await apiCallAuth(conPagina(`/campanias-phishing/${id}/participantes`, pagina));
            if (id === detalleCampania) el('detalle-participantes').innerHTML = tablaParticipantes(datos);
        } catch (err) { alert(mensajeDeError(err)); }
    });

    function detalleNist(c) {
        if (!c.phishScaleRegistro) return '<p>Dificultad NIST: sin captura (campaña pendiente o anterior a la calificación).</p>';

        let ps;
        try {
            ps = JSON.parse(c.phishScaleRegistro);
        } catch { return ''; }

        const senales = ps.senales || [];
        return `<p>NIST Phish Scale capturado al iniciar: ${senales.length} señales (${esc(ps.categoriaSenales || '')}).</p>
            ${(ps.porArea || []).length ? `<ul class="nist-lista">${ps.porArea.map(a => `<li>${esc(a.area || `Área ${Number(a.idArea)}`)}: alineación ${esc(ALINEACIONES[a.alineacion] || a.alineacion)} → ${chipNist(a.dificultad)}</li>`).join('')}</ul>` : ''}
            ${senales.length ? `<details><summary>Señales de alerta del mensaje</summary><ul>${senales.map(x => `<li><strong>${esc(TIPOS[x.tipo] || x.tipo)}:</strong> ${esc(x.descripcion)}${x.fragmento ? ` <q>${esc(x.fragmento)}</q>` : ''}</li>`).join('')}</ul></details>` : ''}`;
    }

    async function cargarTablaPlantillas(pagina = paginaPlantillas) {
        const tbody = el('tbody-plantillas');
        const paginador = contenedorPaginador(tbody, 'plantillas', nueva => cargarTablaPlantillas(nueva));
        try {
            const datos = await apiCallAuth(conPagina('/campanias-phishing/plantillas', pagina));
            if (!datos.items.length && datos.total > 0 && datos.pagina > 1)
                return cargarTablaPlantillas(ultimaPagina(datos));

            paginaPlantillas = datos.pagina;
            paginador.innerHTML = renderPaginador(datos, 'plantillas');
            const plantillas = datos.items;
            if (plantillas.length === 0) {
                tbody.innerHTML = `<tr><td colspan="7" class="empty-state">No hay plantillas creadas.</td></tr>`;
                return;
            }

            tbody.innerHTML = plantillas.map(p => `
                <tr>
                    <td>#${p.idPlantilla}</td>
                    <td style="font-weight: 500;">${esc(p.nombre)}</td>
                    <td>${esc(p.asuntoCorreo || p.asunto)}</td>
                    <td><span class="chip">${esc(p.nivelCriticidad || p.criticidad || 'Asignado')}</span></td>
                    <td>${chipNist(p.dificultadGeneral)}<div class="t-meta">${Number(p.cantidadSenales) || 0} señales · alineación ${esc(ALINEACIONES[p.alineacionGeneral] || 'Media')}${p.paginaSenuelo === 'credenciales' ? ' · con señuelo' : ''}</div></td>
                    <td>${p.habilitada !== false ? 'Habilitada' : 'Deshabilitada'}</td>
                    <td class="t-acciones">
                        <div class="row-actions">
                            <button type="button" class="btn js-preview-plantilla" data-id="${p.idPlantilla}">Vista previa</button>
                            <button type="button" class="btn js-estado-plantilla" data-id="${p.idPlantilla}" data-habilitada="${p.habilitada !== false}">${p.habilitada !== false ? 'Deshabilitar' : 'Habilitar'}</button>
                            <button type="button" class="btn-icon js-editar-plantilla" data-id="${p.idPlantilla}" title="Editar">
                                ${icon('edit')}
                            </button>
                            <button type="button" class="btn-icon is-danger js-eliminar-plantilla" data-id="${p.idPlantilla}" title="Eliminar">
                                ${icon('trash')}
                            </button>
                        </div>
                    </td>
                </tr>
            `).join('');
        } catch (err) {
            console.error('Error al cargar plantillas:', err);
            tbody.innerHTML = `<tr><td colspan="7" class="empty-state" style="color:var(--error);">Error: ${esc(mensajeDeError(err, 'desconocido'))}</td></tr>`;
        }
    }

    // senales de alerta y alineacion (NIST Phish Scale)
    function filaSenal(s = { tipo: 'tecnico', descripcion: '', fragmento: '' }) {
        const row = document.createElement('div');
        row.className = 'nist-senal';
        row.innerHTML = `<select class="js-senal-tipo" aria-label="Tipo de señal">${Object.entries(TIPOS).map(([k, v]) => `<option value="${k}" title="${esc(TIPOS_AYUDA[k])}">${esc(v)}</option>`).join('')}</select>
            <input class="js-senal-desc" maxlength="300" placeholder="Qué debería notar el colaborador" aria-label="Descripción de la señal">
            <input class="js-senal-frag" maxlength="500" placeholder="Fragmento del correo (opcional)" aria-label="Fragmento del correo">
            <button type="button" class="btn-icon is-danger js-quitar-senal" title="Quitar señal" aria-label="Quitar señal">×</button>`;
        row.querySelector('.js-senal-tipo').value = s.tipo;
        row.querySelector('.js-senal-desc').value = s.descripcion || '';
        row.querySelector('.js-senal-frag').value = s.fragmento || '';

        return row;
    }

    function renderSenales(lista) {
        el('p-senales').replaceChildren(...lista.map(filaSenal));
        actualizarNist();
    }

    function leerSenales() {
        return [...el('p-senales').querySelectorAll('.nist-senal')].map(r => ({
            tipo: r.querySelector('.js-senal-tipo').value,
            descripcion: r.querySelector('.js-senal-desc').value.trim(),
            fragmento: r.querySelector('.js-senal-frag').value.trim() || null,
        }));
    }

    function renderAreasNist(areas, alineaciones) {
        const mapa = new Map(alineaciones.map(a => [a.idArea, a.nivel]));
        el('p-areas-nist').innerHTML = areas.length
            ? `<div class="table-wrap"><table class="log-table"><caption class="field-hint">Ajusta la alineación de las áreas donde el mensaje resulta más o menos creíble.</caption>
            <thead><tr><th>Área</th><th>Alineación</th><th>Dificultad</th></tr></thead><tbody>${areas.map(a => `<tr>
            <td>${esc(a.area)}${a.activa ? '' : ' <span class="t-meta">(inactiva)</span>'}</td>
            <td><select class="js-alineacion-area" data-area="${Number(a.idArea)}" aria-label="Alineación para ${esc(a.area)}"><option value="">Usar general</option>${Object.entries(ALINEACIONES).map(([k, v]) => `<option value="${k}" ${mapa.get(a.idArea) === k ? 'selected' : ''}>${v}</option>`).join('')}</select></td>
            <td class="js-dificultad-area" data-area="${Number(a.idArea)}"></td></tr>`).join('')}</tbody></table></div>`
            : '<p class="field-hint">No hay áreas activas para ajustar la alineación.</p>';
        actualizarNist();
    }

    function leerAlineaciones() {
        return [...el('p-areas-nist').querySelectorAll('.js-alineacion-area')]
            .filter(s => s.value)
            .map(s => ({ idArea: Number(s.dataset.area), nivel: s.value }));
    }

    function actualizarNist() {
        const n = el('p-senales').querySelectorAll('.nist-senal').length, general = el('p-alineacion').value;
        el('p-nist-resumen').innerHTML = n
            ? `${n} señales (${categoriaSenales(n)}) · alineación general ${esc(ALINEACIONES[general])} → ${chipNist(dificultad(n, general))}`
            : 'Sin señales registradas: la plantilla quedará «Sin calificar».';
        for (const sel of el('p-areas-nist').querySelectorAll('.js-alineacion-area')) {
            const cell = el('p-areas-nist').querySelector(`.js-dificultad-area[data-area="${sel.dataset.area}"]`);
            cell.innerHTML = chipNist(dificultad(n, sel.value || general));
        }
    }

    el('p-agregar-senal').addEventListener('click', () => {
        if (el('p-senales').children.length >= 40) return;

        const row = filaSenal();
        el('p-senales').append(row);
        row.querySelector('.js-senal-desc').focus();
        actualizarNist();
    });
    el('p-senales').addEventListener('click', e => {
        const quitar = e.target.closest('.js-quitar-senal');
        if (quitar) {
            quitar.closest('.nist-senal').remove();
            actualizarNist();
        }
    });
    el('p-alineacion').addEventListener('change', actualizarNist);
    el('p-areas-nist').addEventListener('change', actualizarNist);

    el('btn-nueva-plantilla').addEventListener('click', () => {
        formPlantilla.reset();
        el('p-id').value = '';
        el('p-habilitada').checked = true;
        el('p-preview').hidden = true;
        el('modal-plantilla-titulo').textContent = 'Crear Nueva Plantilla';
        el('p-error').style.display = 'none';
        // evita un valor preseleccionado de una edicion previa
        el('p-criticidad').value = '';
        el('p-categoria').value = '';
        el('p-senuelo').value = 'ninguna';
        el('p-alineacion').value = 'media';
        el('p-rem-nombre').value = '';
        el('p-rem-correo').value = '';
        el('p-enlace').value = '';
        renderSenales([]);
        renderAreasNist(areasActivas, []);
        dialogPlantilla.showModal();
    });

    async function editarPlantilla(id) {
        try {
            const data = await apiCallAuth(`/campanias-phishing/plantillas/${id}`);

            el('p-id').value = data.idPlantilla;
            el('p-nombre').value = data.nombre || '';
            el('p-criticidad').value = data.idCriticidad;
            el('p-asunto').value = data.asuntoCorreo || '';
            el('p-cuerpo').value = data.cuerpoHtml || '';
            el('p-habilitada').checked = data.habilitada !== false;
            el('p-preview').hidden = true;
            el('p-senuelo').value = data.paginaSenuelo || 'ninguna';
            el('p-alineacion').value = data.alineacionGeneral || 'media';
            el('p-rem-nombre').value = data.remitenteNombre || '';
            el('p-rem-correo').value = data.remitenteCorreo || '';
            el('p-enlace').value = data.enlaceMostrado || '';
            renderSenales(data.senales || []);
            renderAreasNist(
                data.phishScale?.porArea?.map(a => ({ idArea: a.idArea, area: a.area, activa: a.activa })) ?? areasActivas,
                data.alineaciones || []);

            const errorEl = el('p-error');
            if (data.idCategoriaPregunta) {
                el('p-categoria').value = data.idCategoriaPregunta;
                errorEl.style.display = 'none';
            } else {
                // sin categoria se avisa en vez de guardar un valor incorrecto
                el('p-categoria').value = '';
                errorEl.textContent = 'Esta plantilla no tiene categoría educativa asignada. Selecciona una antes de guardar.';
                errorEl.style.display = 'block';
            }

            el('modal-plantilla-titulo').textContent = 'Editar Plantilla';
            dialogPlantilla.showModal();
        } catch (err) {
            console.error('Error al cargar la plantilla:', err);
            alert('Error al cargar la plantilla: ' + mensajeDeError(err, 'intenta de nuevo.'));
        }
    }

    async function eliminarPlantilla(id, boton) {
        if (!confirm('¿Estás seguro de eliminar esta plantilla?')) return;

        await protegido(boton, async () => {
            try {
                await apiCallAuth(`/campanias-phishing/plantillas/${id}`, { method: 'DELETE' });
                await cargarTablaPlantillas();
                await cargarCatalogos();
            } catch (err) {
                console.error('Error al eliminar plantilla:', err);
                alert('No se pudo eliminar: ' + mensajeDeError(err, 'intenta de nuevo.'));
            }
        });
    }

    async function estadoPlantilla(id, habilitada, boton) {
        if (!confirm(`${habilitada ? 'Habilitar' : 'Deshabilitar'} esta plantilla?${habilitada ? '' : ' Las campañas pendientes deberán elegir una plantilla habilitada antes de ejecutarse.'}`)) return;

        await protegido(boton, async () => {
            try {
                await apiCallAuth(`/campanias-phishing/plantillas/${id}/estado`, { method: 'PATCH', body: { habilitada } });
                await cargarTablaPlantillas();
                await cargarCatalogos();
            } catch (err) { alert(mensajeDeError(err)); }
        });
    }

    async function vistaPlantilla(id) {
        try {
            const p = await apiCallAuth(`/campanias-phishing/plantillas/${id}`);
            el('correo-preview-asunto').textContent = p.asuntoCorreo;
            renderEmailPreview(el('correo-preview-contenido'), p.cuerpoHtml);
            el('dialog-correo-preview').showModal();
        } catch (err) { alert(mensajeDeError(err)); }
    }

    el('p-ver-previa').addEventListener('click', () => {
        el('p-preview').hidden = false;
        vistaCorreo(el('p-preview'), el('p-cuerpo').value, el('p-asunto').value);
    });

    formPlantilla.addEventListener('submit', async (e) => {
        e.preventDefault();
        const errorEl = el('p-error');
        errorEl.style.display = 'none';

        const idCriticidad = el('p-criticidad').value;
        const idCategoria = el('p-categoria').value;

        if (!idCriticidad || !idCategoria) {
            errorEl.textContent = 'Selecciona un nivel de criticidad y una categoría educativa.';
            errorEl.style.display = 'block';
            return;
        }

        const btnSubmit = el('p-submit');
        btnSubmit.disabled = true;

        const id = el('p-id').value;
        const payload = {
            nombre: el('p-nombre').value.trim(),
            asuntoCorreo: el('p-asunto').value.trim(),
            cuerpoHtml: el('p-cuerpo').value,
            idCriticidad: parseInt(idCriticidad, 10),
            idCategoriaPregunta: parseInt(idCategoria, 10),
            habilitada: el('p-habilitada').checked,
            paginaSenuelo: el('p-senuelo').value,
            alineacionGeneral: el('p-alineacion').value,
            senales: leerSenales(),
            alineaciones: leerAlineaciones(),
            remitenteNombre: el('p-rem-nombre').value.trim() || null,
            remitenteCorreo: el('p-rem-correo').value.trim() || null,
            enlaceMostrado: el('p-enlace').value.trim() || null
        };

        if (payload.senales.some(s => !s.descripcion)) {
            errorEl.textContent = 'Describe cada señal de alerta o quita las filas vacías.';
            errorEl.style.display = 'block';
            btnSubmit.disabled = false;
            return;
        }

        try {
            if (id && id.trim() !== '') {
                await apiCallAuth(`/campanias-phishing/plantillas/${id}`, {
                    method: 'PUT',
                    body: payload
                });
            } else {
                await apiCallAuth('/campanias-phishing/plantillas', {
                    method: 'POST',
                    body: payload
                });
            }

            dialogPlantilla.close();
            plantillasCache.clear();
            await cargarTablaPlantillas();
            await cargarCatalogos();
        } catch (err) {
            console.error('Error al guardar plantilla:', err);
            errorEl.textContent = mensajeDeError(err, 'Error al procesar la solicitud.');
            errorEl.style.display = 'block';
        } finally {
            btnSubmit.disabled = false;
        }
    });

    inicializar();
})();
