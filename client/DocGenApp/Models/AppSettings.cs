namespace DocGenApp.Models;

public sealed class AppSettings
{
    public string ServerBaseUrl { get; set; } = "http://localhost:5000";

    /// <summary>Carpeta por defecto para los .docx generados. Vacío = preguntar siempre.</summary>
    public string DefaultOutputFolder { get; set; } = string.Empty;

    /// <summary>Ruta a la plantilla Word. Relativa se resuelve contra el directorio de la app.</summary>
    public string TemplatePath { get; set; } = "Templates/plantilla.docx";

    public int TimeoutSeconds { get; set; } = 15;
}
