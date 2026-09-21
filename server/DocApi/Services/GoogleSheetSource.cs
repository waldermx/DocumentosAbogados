using DocApi.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Options;

namespace DocApi.Services;

/// <summary>
/// Lee la hoja real vía las APIs de Google, siempre en modo solo lectura.
/// Drive se usa exclusivamente para el chequeo barato de <c>modifiedTime</c>.
/// </summary>
public sealed class GoogleSheetSource : ISheetSource, IDisposable
{
    private readonly GoogleSheetsOptions _options;
    private readonly ILogger<GoogleSheetSource> _logger;
    private readonly SheetsService _sheets;
    private readonly DriveService _drive;

    public bool EstaConfigurado => true;

    public GoogleSheetSource(IOptions<GoogleSheetsOptions> options, ILogger<GoogleSheetSource> logger)
    {
        _options = options.Value;
        _logger = logger;

        var credential = CrearCredencial(_options)
            .CreateScoped(SheetsService.Scope.SpreadsheetsReadonly, DriveService.Scope.DriveReadonly);

        var init = new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "DocumentosAbogados"
        };

        _sheets = new SheetsService(init);
        _drive = new DriveService(init);

        _logger.LogInformation(
            "Google Sheets configurado. Spreadsheet {Id}, rango {Rango} (columna '{Columna}').",
            _options.SpreadsheetId, _options.Range, _options.SourceColumnName);
    }

    private static GoogleCredential CrearCredencial(GoogleSheetsOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ServiceAccountJson))
        {
            return GoogleCredential.FromJson(options.ServiceAccountJson);
        }

        var ruta = options.ServiceAccountJsonPath!;
        if (!File.Exists(ruta))
        {
            throw new FileNotFoundException(
                $"No se encontró el JSON de service account en '{ruta}'.", ruta);
        }

        return GoogleCredential.FromFile(ruta);
    }

    public async Task<DateTimeOffset?> GetModifiedTimeAsync(CancellationToken ct)
    {
        var request = _drive.Files.Get(_options.SpreadsheetId);
        request.Fields = "modifiedTime";
        request.SupportsAllDrives = true;

        var archivo = await request.ExecuteAsync(ct).ConfigureAwait(false);
        return archivo.ModifiedTimeDateTimeOffset;
    }

    public async Task<IReadOnlyDictionary<int, string>> LeerColumnaAsync(CancellationToken ct)
    {
        var request = _sheets.Spreadsheets.Values.Get(_options.SpreadsheetId, _options.Range);
        request.MajorDimension = SpreadsheetsResource.ValuesResource.GetRequest.MajorDimensionEnum.ROWS;

        var respuesta = await request.ExecuteAsync(ct).ConfigureAwait(false);
        var filas = respuesta.Values;

        var resultado = new Dictionary<int, string>();
        if (filas is null)
        {
            _logger.LogWarning("El rango {Rango} no devolvió valores.", _options.Range);
            return resultado;
        }

        // El índice de fila es 1-based y relativo al inicio del rango configurado,
        // de modo que coincide con lo que el usuario ve en la hoja cuando el rango
        // arranca en la fila 1 (p. ej. "Hoja1!C:C").
        var primeraFila = _options.TieneEncabezado ? 1 : 0;
        for (var i = primeraFila; i < filas.Count; i++)
        {
            var celda = filas[i].Count > 0 ? filas[i][0]?.ToString() : null;
            if (!string.IsNullOrWhiteSpace(celda))
            {
                resultado[i + 1] = celda;
            }
        }

        return resultado;
    }

    public void Dispose()
    {
        _sheets.Dispose();
        _drive.Dispose();
    }
}
