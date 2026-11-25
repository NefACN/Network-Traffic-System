using System;
using System.Collections.Generic;
using System.Text;

namespace proyect.domain.Entidades
{
    public class IntentoCursoVoluntario
    {
        public int Id_Intento { get; set; }
        public int Id_Usuario { get; set; }
        public int Id_Categoria { get; set; }
        public int? IdSucursalRegistro { get; set; }
        public int? IdAreaRegistro { get; set; }
        public bool ContextoRegistrado { get; set; }

        public DateTime FechaInicio { get; set; }
        public DateTime? FechaCompletado { get; set; }
        public bool? Exitoso { get; set; }
        public int PuntosObtenidos { get; set; }
        public int? IdActividad { get; set; }
        public int RevisionActividad { get; set; }
        public string? TituloRegistro { get; set; }
        public string? ContenidoRegistro { get; set; }
        public string? CuestionarioRegistro { get; set; }
        public string? RespuestasRegistro { get; set; }
        public DateTime? FechaEvaluacion { get; set; }
        public bool ContenidoRevisado { get; set; }
        public int Aciertos { get; set; }
        public int TotalPreguntas { get; set; }
        public decimal? Porcentaje { get; set; }
        public int PuntosConfigurados { get; set; }
        public int DiasRepeticionConfigurados { get; set; }
        public int AprobacionConfigurada { get; set; }

        public ApplicationUser Usuario { get; set; } = null!;
        public CategoriaPregunta Categoria { get; set; } = null!;

        public List<PreguntaAsignadaCurso> PreguntasAsignadas { get; set; } = new();


    }
}
