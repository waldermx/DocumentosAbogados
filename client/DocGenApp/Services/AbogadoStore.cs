using System.Text.Json;
using DocGenApp.Models;

namespace DocGenApp.Services;

/// <summary>Lo que se persiste: la lista de abogados y cuál estaba activo al cerrar la app.</summary>
public sealed class AbogadosPersistidos
{
    public List<DatosAbogado> Abogados { get; set; } = [];
    public Guid? SeleccionadoId { get; set; }
}

/// <summary>
/// Guarda los datos de los abogados en %LOCALAPPDATA%\DocGenApp\abogados.json.
/// Van en claro a propósito: son datos identificativos (nombre, usuario FIREL, cédula),
/// no credenciales — la password maestra sigue siendo lo único cifrado con DPAPI
/// (ver <see cref="CredentialStore"/>).
/// </summary>
public sealed class AbogadoStore
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _ruta;

    /// <summary>Ruta del formato anterior (un único abogado), solo para migrarla una vez.</summary>
    private readonly string _rutaAnterior;

    public AbogadoStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocGenApp"))
    {
    }

    /// <summary>Para los tests: permite apuntar a una carpeta temporal en vez de LOCALAPPDATA.</summary>
    public AbogadoStore(string carpeta)
    {
        Directory.CreateDirectory(carpeta);
        _ruta = Path.Combine(carpeta, "abogados.json");
        _rutaAnterior = Path.Combine(carpeta, "abogado.json");
    }

    public string Ruta => _ruta;

    /// <summary>
    /// Carga la lista de abogados y cuál seleccionar. Si nunca se guardó nada en el
    /// formato nuevo pero existe el archivo del formato anterior (un solo abogado, sin
    /// lista), se migra una sola vez: se envuelve en una lista de un elemento y se
    /// persiste ya en <c>abogados.json</c>. El archivo viejo no se borra, por si acaso.
    /// </summary>
    public AbogadosPersistidos Cargar()
    {
        if (File.Exists(_ruta))
        {
            try
            {
                var persistido = JsonSerializer.Deserialize<AbogadosPersistidos>(File.ReadAllText(_ruta), JsonOpts);
                if (persistido is not null)
                {
                    return persistido;
                }
            }
            catch
            {
                // Un archivo corrupto no debe impedir arrancar: se cae a "no hay nada guardado".
            }

            return new AbogadosPersistidos();
        }

        var migrado = MigrarFormatoAnterior();
        if (migrado is not null)
        {
            Guardar(migrado);
            return migrado;
        }

        return new AbogadosPersistidos();
    }

    private AbogadosPersistidos? MigrarFormatoAnterior()
    {
        if (!File.Exists(_rutaAnterior))
        {
            return null;
        }

        try
        {
            var anterior = JsonSerializer.Deserialize<DatosAbogadoAnterior>(File.ReadAllText(_rutaAnterior), JsonOpts);
            if (anterior is null)
            {
                return null;
            }

            var abogado = new DatosAbogado
            {
                Nombre = anterior.Nombre,
                UsuarioFirel = anterior.UsuarioFirel,
                CedulaProfesional = anterior.CedulaProfesional
            };

            return new AbogadosPersistidos { Abogados = [abogado], SeleccionadoId = abogado.Id };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Forma del archivo anterior a que existieran varios abogados.</summary>
    private sealed class DatosAbogadoAnterior
    {
        public string Nombre { get; set; } = string.Empty;
        public string UsuarioFirel { get; set; } = string.Empty;
        public string CedulaProfesional { get; set; } = string.Empty;
    }

    /// <summary>Escritura atómica, para no dejar el archivo a medias si la app muere guardando.</summary>
    public void Guardar(AbogadosPersistidos datos)
    {
        var temp = _ruta + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(datos, JsonOpts));
        File.Move(temp, _ruta, overwrite: true);
    }
}
