using System;
using System.Collections.Generic;
using System.Text;

namespace proyect.domain.Entidades
{
    public class AreaOperativa
    {
        public int Id_Area { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public bool Activo { get; set; } = true;
        public int Id_Criticidad { get; set; }

        public NivelCriticidad Criticidad { get; set; } = null!;
    }
}
