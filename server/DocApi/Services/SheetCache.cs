using System.Text.Json;
using DocApi.Models;
using Microsoft.Extensions.Options;

namespace DocApi.Services;

/// <summary>
/// Estado cacheado de la hoja. Todas las escrituras vienen del <see cref="SyncCoordinator"/>,
/// que ya está serializado por su semáforo; las lecturas de los endpoints son concurrentes,
/// así que el estado se reemplaza de forma atómica por un snapshot inmutable.
/// </summary>
public sealed class SheetCache
{
    private readonly ILogger<SheetCache> _logger;
    private readonly string? _rutaArchivo;
    private volatile Snapshot _actual = Snapshot.Vacio;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public SheetCache(IOptions<SyncOptions> options, ILogger<SheetCache> logger)
    {
        _logger = logger;
        var ruta = options.Value.CacheFilePath;
        _rutaArchivo = string.IsNullOrWhiteSpace(ruta) ? null : Path.GetFullPath(ruta);
        CargarDesdeDisco();
    }

    public Snapshot Actual => _actual;

    public void Reemplazar(Snapshot nuevo)
    {
        _actual = nuevo;
        GuardarEnDisco(nuevo);
    }

    /// <summary>Vista inmutable del caché. Se reemplaza entera en cada sync exitosa.</summary>
    public sealed record Snapshot(
        IReadOnlyList<RegistroDto> Registros,
        IReadOnlyList<ErrorParseoDto> ErroresParseo,
        IReadOnlyDictionary<int, string> ValoresRawPorFila,
        DateTimeOffset? UltimaSync,
        DateTimeOffset? UltimoModifiedTime)
    {
        public static readonly Snapshot Vacio = new(
            Array.Empty<RegistroDto>(),
            Array.Empty<ErrorParseoDto>(),
            new Dictionary<int, string>(),
            null,
            null);
    }

    // --- Persistencia opcional a JSON, para sobrevivir reinicios sin re-leer la hoja ---

    private sealed record EstadoPersistido(
        List<RegistroDto> Registros,
        List<ErrorParseoDto> ErroresParseo,
        Dictionary<int, string> ValoresRawPorFila,
        DateTimeOffset? UltimaSync,
        DateTimeOffset? UltimoModifiedTime);

    private void CargarDesdeDisco()
    {
        if (_rutaArchivo is null || !File.Exists(_rutaArchivo))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_rutaArchivo);
            var estado = JsonSerializer.Deserialize<EstadoPersistido>(json, JsonOpts);
            if (estado is null)
            {
                return;
            }

            _actual = new Snapshot(
                estado.Registros,
                estado.ErroresParseo,
                estado.ValoresRawPorFila,
                estado.UltimaSync,
                estado.UltimoModifiedTime);

            _logger.LogInformation(
                "Caché restaurado desde {Ruta}: {Total} registros, última sync {UltimaSync}.",
                _rutaArchivo, estado.Registros.Count, estado.UltimaSync);
        }
        catch (Exception ex)
        {
            // Un caché corrupto no debe impedir arrancar: se descarta y la primera sync lo rehace.
            _logger.LogWarning(ex, "No se pudo restaurar el caché desde {Ruta}. Se arranca vacío.", _rutaArchivo);
        }
    }

    private void GuardarEnDisco(Snapshot snapshot)
    {
        if (_rutaArchivo is null)
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(_rutaArchivo);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var estado = new EstadoPersistido(
                snapshot.Registros.ToList(),
                snapshot.ErroresParseo.ToList(),
                snapshot.ValoresRawPorFila.ToDictionary(kv => kv.Key, kv => kv.Value),
                snapshot.UltimaSync,
                snapshot.UltimoModifiedTime);

            // Escritura atómica: archivo temporal + move, para no dejar un JSON a medias.
            var temp = _rutaArchivo + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(estado, JsonOpts));
            File.Move(temp, _rutaArchivo, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo persistir el caché en {Ruta}.", _rutaArchivo);
        }
    }
}
