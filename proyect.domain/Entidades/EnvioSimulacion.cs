using System;
using System.Collections.Generic;
using System.Text;

namespace proyect.domain.Entidades
{
    public class EnvioSimulacion
    {
        public int Id_Envio { get; set; }
        public int Id_Campania { get; set; }
        public int Id_Usuario { get; set; }
        public int? IdSucursalRegistro { get; set; }
        public int? IdAreaRegistro { get; set; }
        public int? IdCategoriaRegistro { get; set; }
        public bool ContextoRegistrado { get; set; }
        public DateTime? FechaEnvio { get; set; }
        // aceptado por el proveedor no implica entrega ni lectura confirmada
        public string EstadoEntrega { get; set; } = "legacy";
        public int? PuntosRefuerzoConfigurados { get; set; }

        public Guid TokenUuid { get; set; }

        public bool FueAbierto { get; set; } = false;
        public DateTime? FechaApertura { get; set; }
        public bool HizoClick { get; set; } = false;
        public DateTime? FechaVulneracion { get; set; }
        public DateTime? FechaReporte { get; set; }
        public bool ReporteSinClickPrevio { get; set; }
        // inspeccion interactiva tras el clic, autoinformada por el navegador
        public DateTime? FechaInspeccion { get; set; }
        public int? InspeccionEncontradas { get; set; }
        public int? InspeccionTotal { get; set; }
        public int? InspeccionAciertosEnlace { get; set; }
        // tipos de senal no encontrados (separados por coma) para orientar el refuerzo
        public string? InspeccionOmitidas { get; set; }

        // NIST Phish Scale capturado para el area del destinatario al iniciar
        public string? AlineacionRegistro { get; set; }
        public string? DificultadNistRegistro { get; set; }
        // solo se registra el hecho y la fecha, nunca los valores escritos
        public bool IngresoCredenciales { get; set; }
        public DateTime? FechaIngresoCredenciales { get; set; }

        public CampaniaSimulacion Campania { get; set; } = null!;
        public ApplicationUser Usuario { get; set; } = null!;

        public bool CapacitacionCompleta { get; set; } = false;

        public DateTime? FechaCapacitacion { get; set; }
        public int PuntosRecuperados { get; set; } = 0;
    }
}
