namespace proyect.domain.Entidades;

// senal de alerta de una plantilla segun las cinco categorias de NIST Phish Scale
public sealed class SenalAlertaPlantilla
{
    public int Id { get; set; }
    public int IdPlantilla { get; set; }
    public string Tipo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    // fragmento literal del correo donde aparece la senal (opcional)
    public string? Fragmento { get; set; }
    public int Orden { get; set; }
    public PlantillaPhishing Plantilla { get; set; } = null!;
}

// alineacion de la premisa con un area, sustituye la alineacion general de la plantilla
public sealed class AlineacionPlantillaArea
{
    public int IdPlantilla { get; set; }
    public int IdArea { get; set; }
    public string Nivel { get; set; } = "media";
    public PlantillaPhishing Plantilla { get; set; } = null!;
    public AreaOperativa Area { get; set; } = null!;
}
