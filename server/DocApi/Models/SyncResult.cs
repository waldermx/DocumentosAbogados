namespace DocApi.Models;

/// <summary>En qué terminó un intento de sincronización.</summary>
public enum SyncOutcome
{
    /// <summary>La hoja no cambió desde la última sync: se cortó en el chequeo barato de modifiedTime.</summary>
    SinCambios,

    /// <summary>La hoja cambió y se releyó.</summary>
    Actualizado,

    /// <summary>No se pudo sincronizar (sin credenciales, cuota, red, estructura inesperada...).</summary>
    Error
}

public sealed class SyncResult
{
    public required SyncOutcome Resultado { get; init; }
    public required DateTimeOffset? UltimaSync { get; init; }
    public int TotalRegistros { get; init; }
    public string? Mensaje { get; init; }
}

/// <summary>Payload de <c>GET /health</c> y del estado que consume la UI.</summary>
public sealed class SyncStatusDto
{
    public required DateTimeOffset? UltimaSync { get; init; }
    public required int TotalRegistros { get; init; }
    public required string Estado { get; init; }
}
