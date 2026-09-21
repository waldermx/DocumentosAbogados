namespace DocApi.Services;

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
    /// Paso 2 del sync: lee únicamente la columna configurada.
    /// Devuelve los valores crudos indexados por número de fila (1-based en la hoja).
    /// </summary>
    Task<IReadOnlyDictionary<int, string>> LeerColumnaAsync(CancellationToken ct);
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

    public Task<IReadOnlyDictionary<int, string>> LeerColumnaAsync(CancellationToken ct) =>
        throw new InvalidOperationException(Mensaje);
}
