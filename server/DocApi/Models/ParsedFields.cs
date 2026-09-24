namespace DocApi.Models;

/// <summary>
/// Los valores de las columnas configuradas para una fila, copiados tal cual de la hoja.
/// Se exponen como diccionario para que añadir o renombrar una columna en configuración
/// no obligue a recompilar.
/// </summary>
public sealed class ParsedFields
{
    private readonly IReadOnlyDictionary<string, string> _grupos = new Dictionary<string, string>();

    /// <summary>
    /// Indexado sin distinguir mayúsculas. El <c>init</c> reconstruye el diccionario porque
    /// al restaurar el caché desde disco System.Text.Json crea uno con el comparador por
    /// defecto, y el nombre de una columna lo escribe quien configura el servidor.
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

    /// <summary>Atajos para las columnas por defecto; vacíos si la columna no está configurada.</summary>
    public string Consecutivo => Obtener("consecutivo");
    public string Nombre => Obtener("nombre");
    public string Circuito => Obtener("circuito");

    private string Obtener(string nombre) =>
        Grupos.TryGetValue(nombre, out var valor) ? valor : string.Empty;
}
