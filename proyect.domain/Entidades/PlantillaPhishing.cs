using System;
using System.Collections.Generic;
using System.Text;

namespace proyect.domain.Entidades
{
    public class PlantillaPhishing
    {
        public int Id_Plantilla { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public bool Habilitada { get; set; } = true;
        public string AsuntoCorreo { get; set; } = string.Empty;
        public string CuerpoHtml { get; set; } = string.Empty;
        public int Id_Criticidad { get; set; }

        public NivelCriticidad Criticidad { get; set; } = null!;

        public int Id_CategoriaPegunta { get; set; }
        public CategoriaPregunta CategoriaPregunta { get; set; } = null!;

        // alineacion NIST Phish Scale para areas sin alineacion propia: debil, media o fuerte
        public string AlineacionGeneral { get; set; } = "media";
        // ninguna: el clic lleva a la capacitacion, credenciales: muestra un formulario senuelo
        public string PaginaSenuelo { get; set; } = "ninguna";
        // nombre del remitente falso, solo para ensenanza 
        public string? RemitenteNombre { get; set; }
        // direccion simulada del remitente
        public string? RemitenteCorreo { get; set; }
        // URL que se ve al pasar el raton por el enlace
        public string? EnlaceMostrado { get; set; }
        public List<SenalAlertaPlantilla> Senales { get; set; } = new();
        public List<AlineacionPlantillaArea> Alineaciones { get; set; } = new();
    }
}
