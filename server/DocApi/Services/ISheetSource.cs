using System.Text.Json.Serialization;

namespace DocApi.Services;

/// <summary>Una fila tal como viene de la hoja: nombre de columna → valor de la celda.</summary>
public sealed record FilaCruda(IReadOnlyDictionary<string, string> Campos)
{
    /// <summary>
    /// Texto que representa toda la fila. Las marcas de impreso lo guardan y lo comparan
    /// en cada sync: si cualquier columna de la fila se edita, la marca deja de valer.
    /// </summary>
    [JsonIgnore]
    public string Firma => string.Join('\u001F', Campos
        .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
        .Select(kv => $"{kv.Key}={kv.Value}"));
}

/// <summary>
/// Origen de datos de la hoja. Abstraído para poder correr sin credenciales de Google
/// (ver <see cref="SheetSourceNoConfigurado"/>) y para poder testear la sync.
/// </summary>
public interface ISheetSource
{
    /// <summary>¿Hay credenciales y spreadsheet configurados?</summary>
    bool EstaConfigurado { get; }

    /// <summary>
    /// Paso 1 del sync: chequeo barato vía Drive (<c>files.get?fields=modifiedTime</c>).
    /// Devuelve <c>null</c> si Drive no reporta el dato.
    /// </summary>
    Task<DateTimeOffset?> GetModifiedTimeAsync(CancellationToken ct);

    /// <summary>
    /// Paso 2 del sync: lee las columnas configuradas en una sola llamada. Devuelve las filas
    /// con algún valor, indexadas por número de fila (1-based en la hoja).
    /// </summary>
    Task<IReadOnlyDictionary<int, FilaCruda>> LeerFilasAsync(CancellationToken ct);
}

/// <summary>Implementación usada cuando no hay credenciales: falla con un mensaje claro en vez de tumbar la app.</summary>
public sealed class SheetSourceNoConfigurado : ISheetSource
{
    public const string Mensaje =
        "Google Sheets no está configurado. Define GoogleSheets__SpreadsheetId y " +
        "GoogleSheets__ServiceAccountJson (o GoogleSheets__ServiceAccountJsonPath).";

    public bool EstaConfigurado => false;

    public Task<DateTimeOffset?> GetModifiedTimeAsync(CancellationToken ct) =>
        throw new InvalidOperationException(Mensaje);

    public Task<IReadOnlyDictionary<int, FilaCruda>> LeerFilasAsync(CancellationToken ct) =>
        throw new InvalidOperationException(Mensaje);
}
