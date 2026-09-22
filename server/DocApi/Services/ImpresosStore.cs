using System.Text.Json;
using Microsoft.Extensions.Options;
using DocApi.Models;

namespace DocApi.Services;

/// <summary>Cuándo se marcó una fila como impresa y con qué contenido tenía en ese momento.</summary>
public sealed record ImpresoEntry(string Firma, DateTimeOffset Fecha);

/// <summary>
/// Qué filas ya se imprimieron. Es independiente del <see cref="SheetCache"/>: mientras
/// ese se reemplaza entero en cada sync, esto se acumula y solo se limpia cuando una fila
/// marcada cambia de contenido o desaparece de la hoja (ver <see cref="Reconciliar"/>).
/// </summary>
public sealed class ImpresosStore
{
    private readonly ILogger<ImpresosStore> _logger;
    private readonly string? _rutaArchivo;
    private readonly object _gate = new();
    private Dictionary<int, ImpresoEntry> _entradas;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public ImpresosStore(IOptions<SyncOptions> options, ILogger<ImpresosStore> logger)
    {
        _logger = logger;
        var ruta = options.Value.ImpresosFilePath;
        _rutaArchivo = string.IsNullOrWhiteSpace(ruta) ? null : Path.GetFullPath(ruta);
        _entradas = CargarDesdeDisco();
    }

    public IReadOnlyDictionary<int, ImpresoEntry> Actual
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<int, ImpresoEntry>(_entradas);
            }
        }
    }

    public ImpresoEntry Marcar(int fila, string firma)
    {
        var entrada = new ImpresoEntry(firma, DateTimeOffset.UtcNow);
        lock (_gate)
        {
            _entradas[fila] = entrada;
            GuardarEnDisco();
        }

        return entrada;
    }

    /// <returns><c>true</c> si había una marca y se quitó; <c>false</c> si ya no estaba.</returns>
    public bool Desmarcar(int fila)
    {
        lock (_gate)
        {
            if (!_entradas.Remove(fila))
            {
                return false;
            }

            GuardarEnDisco();
            return true;
        }
    }

    /// <summary>
    /// Se llama tras cada sync que leyó la hoja: una fila marcada deja de estar "impresa"
    /// si su firma ya no coincide (se editó, aunque haya sido con buscar y reemplazar) o
    /// si la fila desapareció, para no dar por buena una impresión de datos viejos.
    /// </summary>
    public void Reconciliar(IReadOnlyDictionary<int, FilaCruda> filasActuales)
    {
        lock (_gate)
        {
            var aQuitar = _entradas
                .Where(kv => !filasActuales.TryGetValue(kv.Key, out var fila) ||
                             !string.Equals(fila.Firma, kv.Value.Firma, StringComparison.Ordinal))
                .Select(kv => kv.Key)
                .ToList();

            if (aQuitar.Count == 0)
            {
                return;
            }

            foreach (var fila in aQuitar)
            {
                _entradas.Remove(fila);
            }

            _logger.LogInformation(
                "Se quitó la marca de impreso a {Cantidad} fila(s): contenido editado o fila eliminada.",
                aQuitar.Count);
            GuardarEnDisco();
        }
    }

    private Dictionary<int, ImpresoEntry> CargarDesdeDisco()
    {
        if (_rutaArchivo is null || !File.Exists(_rutaArchivo))
        {
            return new Dictionary<int, ImpresoEntry>();
        }

        try
        {
            var json = File.ReadAllText(_rutaArchivo);
            var estado = JsonSerializer.Deserialize<Dictionary<int, ImpresoEntry>>(json, JsonOpts);
            return estado ?? new Dictionary<int, ImpresoEntry>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudieron restaurar las marcas de impreso desde {Ruta}. Se arranca vacío.", _rutaArchivo);
            return new Dictionary<int, ImpresoEntry>();
        }
    }

    private void GuardarEnDisco()
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

            var temp = _rutaArchivo + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_entradas, JsonOpts));
            File.Move(temp, _rutaArchivo, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo persistir las marcas de impreso en {Ruta}.", _rutaArchivo);
        }
    }
}
