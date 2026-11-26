import { apiCallAuth, ApiError } from './api.js';
export const MAX_HTTP_FILE_BYTES = 2_000_000_000;
const get = (path, options) => apiCallAuth(path, options);
const post = (path, body = {}) => apiCallAuth(path, { method: 'POST', body });
// omite parametros vacios para no enviar from= o to= sin valor
const query = values => new URLSearchParams(Object.entries(values).filter(([, v]) => v !== undefined && v !== null && v !== ''));

// la API pagina, pero selectores y monitoreo necesitan el catalogo completo
async function todas(path) {
    const items = [];
    for (let pagina = 1; ; pagina++) {
        const data = await get(`${path}?pagina=${pagina}&tamanio=100`);
        items.push(...data.items);
        if (!data.items.length || items.length >= data.total) return items;
    }
}

export const MlApi = {
    datasets: () => todas('/http-datasets'), runs: () => todas('/ml/analyses'), models: () => get('/ml/models'),
    training: () => get('/ml/training'),
    train: body => get('/ml/training', { method: 'POST', body, timeoutMs: 600000 }),
    validateModel: id => post(`/ml/training/${encodeURIComponent(id)}/validate`),
    activateModel: version => post(`/ml/training/models/${encodeURIComponent(version)}/activate`),
    upload: async file => {
        if (!file?.size) throw new ApiError('Selecciona un archivo de registros que no esté vacío.', 400);
        if (file.size > MAX_HTTP_FILE_BYTES) throw new ApiError('El archivo supera el límite de 2 GB (2.000.000.000 bytes).', 413);

        const body = new FormData();
        body.append('file', file);
        return postUpload(body);
    },
    confirm: body => post('/http-datasets', body), cancel: id => get(`/http-datasets/validations/${id}`, { method: 'DELETE' }),
    start: (body, replay) => post(replay ? '/ml/streams' : '/ml/analyses', body),
    source: body => post('/ml/sources', body),
    control: (id, action) => post(`/ml/analyses/${id}/${action}`),
    summary: (id, window, period = {}) => get(`/ml/analyses/${id}/summary?${query({ window, ...period })}`),
    report: (id, window, period = {}) => get(`/ml/analyses/${id}/report?${query({ window, ...period })}`),
    results: (id, params) => get(`/ml/analyses/${id}/results?${query(params)}`),
    profile: (id, period = {}) => get(`/ml/analyses/${id}/profile?${query(period)}`),
    correlations: (id, period = {}) => get(`/ml/analyses/${id}/correlations?${query(period)}`),
    compare: params => get(`/ml/comparisons?${query(params)}`),
    review: (id, body) => get(`/ml/results/${id}/review`, { method: 'PUT', body }),
};

const postUpload = body => get('/http-datasets/validations', { method: 'POST', body, timeoutMs: 600000 });
