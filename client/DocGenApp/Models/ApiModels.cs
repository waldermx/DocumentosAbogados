namespace DocGenApp.Models;

/// <summary>Espejo de los DTOs del servidor. Se mantienen deliberadamente simples y tolerantes.</summary>
public sealed class ParsedFields
{
    public Dictionary<string, string> Grupos { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string Consecutivo => Obtener("consecutivo");
    public string Colegio => Obtener("colegio");
    public string Circuito => Obtener("circuito");

    /// <summary>Columna extra de la hoja, unida al registro por número de fila.</summary>
    public string Nombre => Obtener("nombre");

    private string Obtener(string nombre) =>
        Grupos.TryGetValue(nombre, out var v) ? v : string.Empty;
}

public sealed class RegistroDto
{
    public int Fila { get; set; }
    public string ValorCrudo { get; set; } = string.Empty;

    /// <summary>Índice del patrón que reconoció la fila; &gt; 0 significa que cayó en uno de respaldo.</summary>
    public int PatronUsado { get; set; }

    public ParsedFields Campos { get; set; } = new();
}

public sealed class ErrorParseoDto
{
    public int Fila { get; set; }
    public string ValorCrudo { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;
}

public sealed class RegistrosResponseDto
{
    public List<RegistroDto> Registros { get; set; } = [];
    public List<ErrorParseoDto> ErroresParseo { get; set; } = [];
    public DateTimeOffset? UltimaSync { get; set; }
}

public sealed class SyncResultDto
{
    public string Resultado { get; set; } = string.Empty;
    public DateTimeOffset? UltimaSync { get; set; }
    public int TotalRegistros { get; set; }
    public int TotalErroresParseo { get; set; }
    public int FilasReparseadas { get; set; }
    public int FilasReutilizadas { get; set; }
    public string? Mensaje { get; set; }
}

/// <summary>Resultado de una llamada a la API: éxito con valor, o fallo con mensaje legible.</summary>
public readonly record struct ApiResult<T>(bool Exito, T? Valor, string? Error)
{
    public static ApiResult<T> Ok(T valor) => new(true, valor, null);
    public static ApiResult<T> Falla(string error) => new(false, default, error);
}
