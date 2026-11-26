import { apiCallAuth } from '../services/api.js';
import { el, escapeHtml as esc, errorMessage as mensajeDeError, conCarga } from '../utils/ui.js';
import { renderPaginador, enlazarPaginador, conPagina, ultimaPagina } from '../components/paginacion.js';
import { icon } from '../components/icons.js';

document.querySelectorAll('[data-icon]').forEach(node => { node.innerHTML = icon(node.dataset.icon); });

// CRUD del banco de preguntas

(function () {
    const dialogPregunta = el('dialog-pregunta');
    const formPregunta = el('form-pregunta');
    const contenedorOpciones = el('contenedor-opciones');

    // banco de preguntas paginado en el servidor
    let paginaPreguntas = 1;

    function contenedorPaginador() {
        let contenedor = el('preguntas-paginador');
        if (!contenedor) {
            contenedor = document.createElement('div');
            contenedor.id = 'preguntas-paginador';
            el('tbody-preguntas').closest('table').after(contenedor);
            enlazarPaginador(contenedor, 'preguntas', pagina => cargarTablaPreguntas(pagina));
        }

        return contenedor;
    }

    // escapa texto para innerHTML y atributos

    async function inicializar() {
        // los listeners de cierre se registran primero porque no dependen de la red

        await cargarCategorias();
        await cargarTablaPreguntas();
    }

    // cargas principales
    async function cargarCategorias() {
        const select = el('q-categoria');
        try {
            const categorias = await apiCallAuth('/campanias-phishing/categorias-educativas');

            // opcion vacia para que el required del select funcione
            select.innerHTML = `<option value="">Selecciona una categoría…</option>` +
                categorias.map(c =>
                    `<option value="${c.idCategoria}">${esc(c.nombre)}</option>`
                ).join('');
        } catch (err) {
            console.error('Error cargando categorías:', err);
            select.innerHTML = `<option value="">Error al cargar categorías</option>`;
        }
    }

    async function cargarTablaPreguntas(pagina = paginaPreguntas) {
        const tbody = el('tbody-preguntas');
        const paginador = contenedorPaginador();
        try {
            const datos = await apiCallAuth(conPagina('/preguntas-admin', pagina));
            // si la pagina quedo vacia tras eliminar, vuelve a la ultima con datos
            if (!datos.items.length && datos.total > 0 && datos.pagina > 1)
                return cargarTablaPreguntas(ultimaPagina(datos));

            paginaPreguntas = datos.pagina;
            paginador.innerHTML = renderPaginador(datos, 'preguntas');
            const listaPreguntas = datos.items;
            const contador = el('q-count');
            if (contador) contador.textContent = `${datos.total} ${datos.total === 1 ? 'pregunta' : 'preguntas'}`;

            if (listaPreguntas.length === 0) {
                tbody.innerHTML = `<tr><td colspan="5" class="empty-state">No hay preguntas registradas.</td></tr>`;
                return;
            }

            tbody.innerHTML = listaPreguntas.map(p => {
                const numOpciones = (p.opciones || []).length;
                const numCorrectas = (p.opciones || []).filter(o => o.esCorrecta).length;

                // aviso si la pregunta no tiene respuesta correcta
                const avisoCorrectas = numCorrectas === 0
                    ? `<span class="q-flag">${icon('alert')}Sin respuesta correcta</span>`
                    : '';

                return `
                <tr>
                    <td class="q-id">#${p.idPregunta}</td>
                    <td><span class="q-category">${esc(p.categoria)}</span></td>
                    <td class="q-text">${esc(p.textoPregunta)}</td>
                    <td class="q-options"><span>${numOpciones} opciones</span>${avisoCorrectas}</td>
                    <td class="t-acciones">
                        <div class="row-actions">
                            <button type="button" class="q-action js-editar" data-id="${p.idPregunta}" title="Editar" aria-label="Editar pregunta ${p.idPregunta}">${icon('edit')}</button>
                            <button type="button" class="q-action is-danger js-eliminar" data-id="${p.idPregunta}" title="Eliminar" aria-label="Eliminar pregunta ${p.idPregunta}">${icon('trash')}</button>
                        </div>
                    </td>
                </tr>
                `;
            }).join('');
        } catch (err) {
            console.error('Error al cargar preguntas:', err);
            tbody.innerHTML = `<tr><td colspan="5" class="empty-state" style="color:var(--error);">Error al cargar: ${esc(mensajeDeError(err, 'desconocido'))}</td></tr>`;
        }
    }

    // delegacion de eventos: sobrevive a que la tabla se regenere
    el('tbody-preguntas').addEventListener('click', (e) => {
        const btnEditar = e.target.closest('.js-editar');
        if (btnEditar) return editarPregunta(parseInt(btnEditar.dataset.id, 10));

        const btnEliminar = e.target.closest('.js-eliminar');
        if (btnEliminar) return eliminarPregunta(parseInt(btnEliminar.dataset.id, 10), btnEliminar);
    });

    // manejo dinamico de opciones
    function agregarFilaOpcion(idOpcion = '', texto = '', esCorrecta = false) {
        const row = document.createElement('div');
        row.className = `opcion-row ${esCorrecta ? 'correcta' : ''}`;

        row.innerHTML = `
            <input type="hidden" class="opcion-id" value="${esc(idOpcion)}">
            <input type="radio" name="opcion_correcta" class="radio-correcta" title="Marcar como respuesta correcta" ${esCorrecta ? 'checked' : ''} required>
            <input type="text" class="opcion-texto" placeholder="Escribe la opción aquí..." value="${esc(texto)}" required>
            <button type="button" class="q-action is-danger btn-quitar-opcion" title="Eliminar opción" aria-label="Eliminar opción">${icon('close')}</button>
        `;

        const radio = row.querySelector('.radio-correcta');
        radio.addEventListener('change', () => {
            contenedorOpciones.querySelectorAll('.opcion-row')
                .forEach(r => r.classList.remove('correcta'));
            if (radio.checked) row.classList.add('correcta');
        });

        row.querySelector('.btn-quitar-opcion').addEventListener('click', () => {
            if (contenedorOpciones.querySelectorAll('.opcion-row').length <= 2) {
                alert('Una pregunta debe tener al menos 2 opciones.');
                return;
            }

            const eraCorrecta = radio.checked;
            row.remove();

            // si se borro la opcion correcta, se marca la primera
            if (eraCorrecta) {
                const primera = contenedorOpciones.querySelector('.opcion-row');
                if (primera) {
                    primera.querySelector('.radio-correcta').checked = true;
                    contenedorOpciones.querySelectorAll('.opcion-row')
                        .forEach(r => r.classList.remove('correcta'));
                    primera.classList.add('correcta');
                }
            }
        });

        contenedorOpciones.appendChild(row);
    }

    el('btn-agregar-opcion').addEventListener('click', () => agregarFilaOpcion());

    // CRUD
    el('btn-nueva-pregunta').addEventListener('click', () => {
        formPregunta.reset();
        el('q-id').value = '';
        el('modal-titulo').textContent = 'Nueva pregunta';
        el('q-error').style.display = 'none';
        el('q-categoria').value = '';

        contenedorOpciones.innerHTML = '';
        agregarFilaOpcion('', '', true);
        agregarFilaOpcion('', '', false);

        dialogPregunta.showModal();
    });

    async function editarPregunta(id) {
        try {
            const preg = await apiCallAuth(`/preguntas-admin/${id}`);

            el('q-id').value = preg.idPregunta;
            el('q-categoria').value = preg.idCategoria;
            el('q-texto').value = preg.textoPregunta || '';
            el('q-explicacion').value = preg.explicacionEducativa || '';

            contenedorOpciones.innerHTML = '';
            const opciones = preg.opciones || [];

            // los radios admiten una sola marcada, se avisa si en BD hay varias correctas
            const correctas = opciones.filter(o => o.esCorrecta);
            opciones.forEach(o => agregarFilaOpcion(o.idOpcion, o.textoOpcion, o.esCorrecta));

            const errorEl = el('q-error');
            if (correctas.length > 1) {
                errorEl.textContent = `Atención: esta pregunta tiene ${correctas.length} opciones correctas en la base de datos, pero este formulario solo permite marcar una. Si guardas, se conservará únicamente la última marcada.`;
                errorEl.style.display = 'block';
            } else {
                errorEl.style.display = 'none';
            }

            el('modal-titulo').textContent = 'Editar pregunta';
            dialogPregunta.showModal();
        } catch (err) {
            console.error('Error al cargar la pregunta:', err);
            alert('Error al cargar datos: ' + mensajeDeError(err, 'intenta de nuevo.'));
        }
    }

    async function eliminarPregunta(id, boton) {
        if (!confirm('¿Seguro que deseas eliminar esta pregunta?')) return;

        // boton deshabilitado durante la peticion para evitar doble eliminacion
        await conCarga(boton, async () => {
            try {
                await apiCallAuth(`/preguntas-admin/${id}`, { method: 'DELETE' });
                await cargarTablaPreguntas();
            } catch (err) {
                console.error('Error al eliminar:', err);
                alert('Atención: ' + mensajeDeError(err, 'no se pudo eliminar la pregunta.'));
            }
        })();
    }

    // guardar
    formPregunta.addEventListener('submit', async (e) => {
        e.preventDefault();

        const id = el('q-id').value;
        const btnSubmit = el('q-submit');
        const errorEl = el('q-error');
        errorEl.style.display = 'none';

        const filas = Array.from(contenedorOpciones.querySelectorAll('.opcion-row'));

        const opcionesData = filas.map(fila => {
            const idOpc = fila.querySelector('.opcion-id').value;
            return {
                idOpcion: idOpc ? parseInt(idOpc, 10) : null,
                textoOpcion: fila.querySelector('.opcion-texto').value.trim(),
                esCorrecta: fila.querySelector('.radio-correcta').checked
            };
        });

        // validaciones espejo del backend para fallar antes de la red
        if (opcionesData.length < 2) {
            errorEl.textContent = 'Debe proporcionar al menos 2 opciones de respuesta.';
            errorEl.style.display = 'block';
            return;
        }

        if (!opcionesData.some(o => o.esCorrecta)) {
            errorEl.textContent = 'Debe marcar una opción como correcta.';
            errorEl.style.display = 'block';
            return;
        }

        const textos = opcionesData.map(o => o.textoOpcion.toLowerCase());
        if (new Set(textos).size !== textos.length) {
            errorEl.textContent = 'Hay opciones de respuesta repetidas.';
            errorEl.style.display = 'block';
            return;
        }

        const payload = {
            textoPregunta: el('q-texto').value.trim(),
            explicacionEducativa: el('q-explicacion').value.trim(),
            idCategoria: parseInt(el('q-categoria').value, 10),
            opciones: opcionesData
        };

        btnSubmit.disabled = true;
        btnSubmit.textContent = 'Guardando…';

        try {
            if (id) {
                await apiCallAuth(`/preguntas-admin/${id}`, { method: 'PUT', body: payload });
            } else {
                await apiCallAuth('/preguntas-admin', { method: 'POST', body: payload });
            }

            dialogPregunta.close();
            await cargarTablaPreguntas();
        } catch (err) {
            console.error('Error al guardar:', err);
            errorEl.textContent = mensajeDeError(err, 'Error al guardar.');
            errorEl.style.display = 'block';
        } finally {
            btnSubmit.disabled = false;
            btnSubmit.textContent = 'Guardar Pregunta';
        }
    });

    // logout

    inicializar();
})();