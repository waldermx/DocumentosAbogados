using System.Text.Json.Serialization;

namespace DocApi.Services;

/// <summary>
/// Una fila tal como viene de la hoja: el valor de la columna principal (el que pasa por el
/// regex) más los valores de las columnas extra, que se copian tal cual.
/// </summary>
public sealed record FilaCruda(string Valor, IReadOnlyDictionary<string, string> Extra)
{
    private static readonly IReadOnlyDictionary<string, string> Vacio =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fila sin columnas extra. Es un método y no un segundo constructor porque
    /// System.Text.Json exige un único constructor público para deserializar el caché.</summary>
    public static FilaCruda Simple(string valor) => new(valor, Vacio);

    /// <summary>
    /// Texto que representa toda la fila. El diff de sync compara esto: así una columna extra
    /// editada también invalida la fila, igual que un cambio en la columna principal.
    /// </summary>
    [JsonIgnore]
    public string Firma => Extra.Count == 0
        ? Valor
        : string.Join('\u001F', Extra.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}={kv.Value}")
            .Prepend(Valor));
}

/// <summary>
/// Origen de datos de la hoja. Abstraído para poder correr sin credenciales de Google
/// (ver <see cref="SheetSourceNoConfigurado"/>) y para poder testear el diff de sync.
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
    /// Paso 2 del sync: lee la columna principal y las columnas extra configuradas,
    /// en una sola llamada. Devuelve las filas indexadas por número de fila (1-based en la hoja).
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
