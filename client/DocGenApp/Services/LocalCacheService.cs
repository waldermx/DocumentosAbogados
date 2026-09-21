using System.Text.Json;
using DocGenApp.Models;

namespace DocGenApp.Services;

/// <summary>
/// Persiste el último payload recibido del servidor en %LOCALAPPDATA%\DocGenApp\cache.json,
/// para que la app siga siendo útil en modo solo lectura cuando el servidor no responde (RNF-09).
/// </summary>
public sealed class LocalCacheService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _ruta;

    public LocalCacheService()
    {
        var carpeta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DocGenApp");
        Directory.CreateDirectory(carpeta);
        _ruta = Path.Combine(carpeta, "cache.json");
    }

    public DateTime? FechaCache => File.Exists(_ruta) ? File.GetLastWriteTime(_ruta) : null;

    public void Guardar(RegistrosResponseDto payload)
    {
        try
        {
            var temp = _ruta + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(payload, JsonOpts));
            File.Move(temp, _ruta, overwrite: true);
        }
        catch
        {
            // Un fallo al cachear no debe romper el flujo principal: los datos ya están en memoria.
        }
    }

    public RegistrosResponseDto? Cargar()
    {
        if (!File.Exists(_ruta))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RegistrosResponseDto>(File.ReadAllText(_ruta), JsonOpts);
        }
        catch
        {
            return null;
        }
    }
}
