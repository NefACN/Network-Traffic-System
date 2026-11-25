using System;
using System.Collections.Generic;
using System.Text;

namespace proyect.domain.Entidades
{
    public class CampaniaSimulacion
    {
        public int Id_Campania { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string Objetivo { get; set; } = string.Empty;
        public string TipoEvaluacion { get; set; } = "sin_clasificar";
        public string ParticipantesSeleccionadosJson { get; set; } = "[]";
        public DateTime? FechaFinalizacion { get; set; }
        public bool FinalizacionAnticipada { get; set; }
        public string? AsuntoRegistro { get; set; }
        public string? CuerpoHtmlRegistro { get; set; }
        public string? VersionMedicionRegistro { get; set; }
        public int? NivelDificultadRegistro { get; set; }
        public int? IdCategoriaRegistro { get; set; }
        // copia JSON de senales y alineaciones NIST al iniciar la campania
        public string? PhishScaleRegistro { get; set; }
        // copia JSON de los datos de ensenanza de la plantilla al iniciar la campania
        public string? EnsenanzaRegistro { get; set; }
        public string? PaginaSenueloRegistro { get; set; }
        // aprobacion en dos pasos: borrador, pendiente, aprobada, rechazada
        public string EstadoAprobacion { get; set; } = "borrador";
        public int? SolicitadaPor { get; set; }
        public DateTime? FechaSolicitud { get; set; }
        public int? RevisadaPor { get; set; }
        public DateTime? FechaRevision { get; set; }
        public string? ComentarioRevision { get; set; }
        public int Id_Plantilla { get; set; }
        public int Id_Estado { get; set; }
        public int CreadoPor { get; set; }
        public int? IdSucursalObjetivo { get; set; }
        public DateTime? FechaInicioProgramada { get; set; }
        public DateTime? FechaFinProgramada { get; set; }
        public PlantillaPhishing Plantilla { get; set; } = null!;
        public EstadoFlujo Estado { get; set; } = null!;
        public ApplicationUser Creador { get; set; } = null!;

        public ICollection<SegmentacionCampaniArea> Segmentaciones { get; set; } = new List<SegmentacionCampaniArea>();
        public ICollection<EnvioSimulacion> Envios { get; set; } = new List<EnvioSimulacion>();

        public int NivelDificultad { get; set; }

    }
}
