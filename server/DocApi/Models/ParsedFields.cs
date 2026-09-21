namespace DocApi.Models;

/// <summary>
/// Campos extraídos de un valor crudo por <see cref="Services.RowParser"/>.
/// Los grupos se exponen como diccionario para que cambiar los nombres de grupo
/// del regex en configuración no obligue a recompilar.
/// </summary>
public sealed class ParsedFields
{
    public required IReadOnlyDictionary<string, string> Grupos { get; init; }

    /// <summary>Atajos para los grupos del regex por defecto; vacíos si el patrón configurado no los define.</summary>
    public string Consecutivo => Obtener("consecutivo");
    public string Colegio => Obtener("colegio");
    public string Circuito => Obtener("circuito");

    /// <summary>Atajo para la columna extra <c>nombre</c>; vacío si no está configurada.</summary>
    public string Nombre => Obtener("nombre");

    private string Obtener(string nombre) =>
        Grupos.TryGetValue(nombre, out var valor) ? valor : string.Empty;
}
