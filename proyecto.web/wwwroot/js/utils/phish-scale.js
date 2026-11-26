// replica del calculo de PhishScale.cs para la vista previa, el servidor es la referencia
export const TIPOS = {
    errores: 'Errores',
    tecnico: 'Indicadores técnicos',
    visual: 'Presentación visual',
    lenguaje: 'Lenguaje y contenido',
    tacticas: 'Tácticas comunes',
};

export const TIPOS_AYUDA = {
    errores: 'Ortografía, gramática, inconsistencias en fechas o nombres.',
    tecnico: 'Remitente o dominio extraño, enlace que no coincide, adjunto inesperado.',
    visual: 'Logotipo pixelado, formato poco profesional, diseño distinto al habitual.',
    lenguaje: 'Saludo genérico, petición de datos o claves, tono impropio.',
    tacticas: 'Urgencia, autoridad, recompensa, amenaza o curiosidad.',
};

export const ALINEACIONES = { debil: 'Débil', media: 'Media', fuerte: 'Fuerte' };
export const ETIQUETAS = { muy_dificil: 'Muy difícil', moderada: 'Moderadamente difícil', poco_dificil: 'Poco difícil' };

export function categoriaSenales(cantidad) {
    return cantidad <= 8 ? 'pocas' : cantidad <= 14 ? 'algunas' : 'muchas';
}

export function dificultad(cantidad, alineacion) {
    if (cantidad < 1 || !ALINEACIONES[alineacion]) return null;

    const c = categoriaSenales(cantidad);
    if (c === 'pocas') return alineacion === 'debil' ? 'moderada' : 'muy_dificil';
    if (c === 'algunas') return ({ fuerte: 'muy_dificil', media: 'moderada', debil: 'poco_dificil' })[alineacion];

    return alineacion === 'fuerte' ? 'moderada' : 'poco_dificil';
}

export const etiqueta = valor => ETIQUETAS[valor] || 'Sin calificar';
export const claseDificultad = valor => ({ muy_dificil: 'nist-alta', moderada: 'nist-media', poco_dificil: 'nist-baja' })[valor]
    || 'nist-sin';
