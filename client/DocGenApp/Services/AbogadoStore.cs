using System.Text.Json;
using DocGenApp.Models;

namespace DocGenApp.Services;

/// <summary>
/// Guarda los datos del abogado en %LOCALAPPDATA%\DocGenApp\abogado.json.
/// Van en claro a propósito: son datos identificativos (nombre, usuario FIREL, cédula),
/// no credenciales — la password maestra sigue siendo lo único cifrado con DPAPI
/// (ver <see cref="CredentialStore"/>).
/// </summary>
public sealed class AbogadoStore
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _ruta;

    public AbogadoStore()
    {
        var carpeta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DocGenApp");
        Directory.CreateDirectory(carpeta);
        _ruta = Path.Combine(carpeta, "abogado.json");
    }

    public string Ruta => _ruta;

    public DatosAbogado Cargar()
    {
        if (!File.Exists(_ruta))
        {
            return new DatosAbogado();
        }

        try
        {
            return JsonSerializer.Deserialize<DatosAbogado>(File.ReadAllText(_ruta), JsonOpts)
                ?? new DatosAbogado();
        }
        catch
        {
            // Un archivo corrupto no debe impedir arrancar: se vuelve a capturar y se sobrescribe.
            return new DatosAbogado();
        }
    }

    /// <summary>Escritura atómica, para no dejar el archivo a medias si la app muere guardando.</summary>
    public void Guardar(DatosAbogado datos)
    {
        var temp = _ruta + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(datos, JsonOpts));
        File.Move(temp, _ruta, overwrite: true);
    }
}
