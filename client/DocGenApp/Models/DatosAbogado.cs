namespace DocGenApp.Models;

/// <summary>
/// Datos del abogado que firma. No vienen de la hoja: se capturan una vez en la app
/// y se guardan en el equipo, porque son los mismos para todos los documentos.
/// </summary>
public sealed class DatosAbogado
{
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Usuario de FIREL (firma electrónica del Poder Judicial). No es una contraseña.</summary>
    public string UsuarioFirel { get; set; } = string.Empty;

    public string CedulaProfesional { get; set; } = string.Empty;

    /// <summary>Marcadores que estos campos aportan a la plantilla Word.</summary>
    public IReadOnlyDictionary<string, string> ValoresParaPlantilla() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["abogadoNombre"] = Nombre.Trim(),
            ["abogadoFirel"] = UsuarioFirel.Trim(),
            ["abogadoCedula"] = CedulaProfesional.Trim()
        };

    public bool EstaCompleto =>
        !string.IsNullOrWhiteSpace(Nombre) &&
        !string.IsNullOrWhiteSpace(UsuarioFirel) &&
        !string.IsNullOrWhiteSpace(CedulaProfesional);

    public DatosAbogado Clonar() => new()
    {
        Nombre = Nombre,
        UsuarioFirel = UsuarioFirel,
        CedulaProfesional = CedulaProfesional
    };
}
