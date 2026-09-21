namespace DocApi.Models;

/// <summary>
/// Campos extraídos de un valor crudo por <see cref="Services.RowParser"/>.
/// Los grupos se exponen como diccionario para que cambiar los nombres de grupo
/// del regex en configuración no obligue a recompilar.
/// </summary>
public sealed class ParsedFields
{
    private readonly IReadOnlyDictionary<string, string> _grupos = new Dictionary<string, string>();

    /// <summary>
    /// Indexado sin distinguir mayúsculas. El <c>init</c> reconstruye el diccionario porque
    /// al restaurar el caché desde disco System.Text.Json crea uno con el comparador por
    /// defecto, y el nombre de una columna extra lo escribe quien configura el servidor.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Grupos
    {
        get => _grupos;
        init
        {
            var destino = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (clave, valor) in value ?? (IReadOnlyDictionary<string, string>)destino)
            {
                // Gana la primera: un caché corrupto con claves duplicadas no debe lanzar.
                destino.TryAdd(clave, valor);
            }

            _grupos = destino;
        }
    }

    /// <summary>Atajos para los grupos del regex por defecto; vacíos si el patrón configurado no los define.</summary>
    public string Consecutivo => Obtener("consecutivo");
    public string Colegio => Obtener("colegio");
    public string Circuito => Obtener("circuito");

    /// <summary>Atajo para la columna extra <c>nombre</c>; vacío si no está configurada.</summary>
    public string Nombre => Obtener("nombre");

    private string Obtener(string nombre) =>
        Grupos.TryGetValue(nombre, out var valor) ? valor : string.Empty;
}
