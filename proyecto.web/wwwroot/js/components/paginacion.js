// paginacion compartida, en servidor o en el navegador para catalogos pequenos

export const TAMANIO_PAGINA = 25;

// numero de la ultima pagina (minimo 1)
export function ultimaPagina({ total, tamanio }) {
    return Math.max(1, Math.ceil((Number(total) || 0) / Math.max(1, Number(tamanio) || TAMANIO_PAGINA)));
}

// pagina valida dentro de [1, ultima]
export function acotarPagina(pagina, datos) {
    return Math.min(Math.max(1, Number(pagina) || 1), ultimaPagina(datos));
}

// pagina en el navegador una lista con la misma forma que la API
export function paginarEnNavegador(lista, pagina = 1, tamanio = TAMANIO_PAGINA) {
    const items = Array.isArray(lista) ? lista : [];
    const datos = { total: items.length, tamanio };
    const actual = acotarPagina(pagina, datos);
    return { items: items.slice((actual - 1) * tamanio, actual * tamanio), total: items.length, pagina: actual, tamanio };
}

// anade pagina y tamanio a una ruta que puede tener parametros
export function conPagina(ruta, pagina = 1, tamanio = TAMANIO_PAGINA) {
    const separador = ruta.includes('?') ? '&' : '?';
    return `${ruta}${separador}pagina=${encodeURIComponent(Math.max(1, Number(pagina) || 1))}&tamanio=${encodeURIComponent(tamanio)}`;
}

// HTML del paginador, se oculta si todo cabe en una pagina
export function renderPaginador(datos, clave) {
    const total = Number(datos?.total) || 0;
    const ultima = ultimaPagina({ total, tamanio: datos?.tamanio });
    if (ultima <= 1)
        return total > 0
            ? `<nav class="table-pagination" data-paginador="${clave}" aria-label="Paginación"><span>${total} ${total === 1 ? 'registro' : 'registros'}</span></nav>`
            : '';

    const actual = acotarPagina(datos.pagina, { total, tamanio: datos.tamanio });
    return `<nav class="table-pagination" data-paginador="${clave}" aria-label="Paginación">
        <span>Página ${actual} de ${ultima} · ${total} registros</span>
        <div>
            <button type="button" class="btn btn-ghost" data-pagina="${actual - 1}" ${actual <= 1 ? 'disabled' : ''} aria-label="Página anterior">Anterior</button>
            <button type="button" class="btn btn-ghost" data-pagina="${actual + 1}" ${actual >= ultima ? 'disabled' : ''} aria-label="Página siguiente">Siguiente</button>
        </div>
    </nav>`;
}

// delegacion de clics del paginador, se enlaza una vez por contenedor y clave
export function enlazarPaginador(contenedor, clave, alCambiar) {
    if (!contenedor) return;

    const marca = `paginador${clave.replace(/[^a-z0-9]/gi, '')}`;
    if (contenedor.dataset[marca]) return;

    contenedor.dataset[marca] = '1';
    contenedor.addEventListener('click', evento => {
        const boton = evento.target.closest(`[data-paginador="${clave}"] [data-pagina]`);
        if (!boton || boton.disabled) return;

        alCambiar(Number(boton.dataset.pagina));
    });
}
