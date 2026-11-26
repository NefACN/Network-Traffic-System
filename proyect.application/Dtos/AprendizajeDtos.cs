using System.ComponentModel.DataAnnotations;

namespace proyect.application.Dtos;

public sealed record SeccionEducativa(
    [Required, StringLength(120)] string Titulo,
    [Required, StringLength(12000)] string Contenido
    );

public sealed record ActividadEducativaRequest(
    [Required, StringLength(150)] string Titulo,
    [Required, StringLength(2000)] string Descripcion,
    [Range(1, int.MaxValue)] int IdCategoria,
    List<SeccionEducativa> Secciones, List<int> IdsPreguntas,
    bool Habilitada,
    [Range(0, 500)] int Puntos = 20,
    [Range(1, 365)] int DiasRepeticion = 7,
    [Range(1, 100)] int PorcentajeAprobacion = 70,
    bool PermitirRepaso = true,
    bool EsMicroleccion = false,
    bool ParaCredenciales = false
    );

public sealed record PoliticaCyberScoreRequest(
    [Range(0, 100)] decimal PesoClic,
    [Range(0, 100)] decimal PesoCredenciales,
    [Range(0, 100)] decimal BonoReporte,
    [Range(0, 100)] decimal BonoCuestionario,
    [Range(0, 100)] decimal BonoMicroleccion,
    [Range(1, 365)] int VidaMediaDias,
    [Range(7, 730)] int VentanaDias,
    [Range(1, 20)] int MinimoSimulaciones,
    [Range(1, 1.9)] decimal FactorPocoDificil,
    [Range(0.1, 1)] decimal FactorMuyDificil,
    [Range(0.1, 3)] decimal ExposicionBaja,
    [Range(0.1, 3)] decimal ExposicionMedia,
    [Range(0.1, 3)] decimal ExposicionAlta,
    [Range(1, 100)] decimal UmbralBajo = 80,
    [Range(1, 100)] decimal UmbralMedio = 60,
    [Range(1, 100)] decimal UmbralAlto = 40,
    [Range(0, 10)] int MaximoPlanCritico = 3
    );

public sealed record EstadoActividadRequest(bool Habilitada);

public sealed record ReglasGamificacionRequest(
    [Range(0, 500)] int PuntosCursoLibre,
    [Range(1, 365)] int DiasCursoLibre,
    [Range(0, 100)] int PuntosRefuerzo,
    [Range(1, 10)] int MetaSemanalTemas,
    [Range(10, 1000)] int PuntosPorNivel,
    [Range(0, 500)] int PuntosReporte = 15,
    [Range(0, 500)] int PuntosMicroleccion = 10,
    [Range(0, 500)] int PuntosInspeccion = 10
    );

public sealed record LogroRequest(
    [Required, StringLength(50)] string Nombre,
    [Required, StringLength(150)] string Descripcion,
    [Required] string TipoCondicion,
    [Range(1, 100000)] int Objetivo,
    bool Activa = true
    );

public sealed record OpcionPublicada(int IdOpcion, string TextoOpcion, bool EsCorrecta);

public sealed record PreguntaPublicada(int IdPregunta, string TextoPregunta, string Explicacion, List<OpcionPublicada> Opciones);

public sealed record RespuestaEvaluada(
    int IdPregunta,
    string Pregunta,
    int IdOpcionSeleccionada,
    bool Correcta,
    string RespuestaCorrecta,
    string Explicacion);

public sealed record ActividadAdminDto(
    int Id,
    string Titulo,
    string Descripcion,
    int IdCategoria,
    string Tema,
    bool Habilitada,
    int Revision,
    int Puntos,
    int DiasRepeticion,
    int PorcentajeAprobacion,
    bool PermitirRepaso,
    bool EsMicroleccion,
    bool ParaCredenciales,
    string Secciones,
    List<int> IdsPreguntas
    );
