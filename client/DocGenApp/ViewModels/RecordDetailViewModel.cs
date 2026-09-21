using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DocGenApp.Models;

namespace DocGenApp.ViewModels;

/// <summary>Detalle de un registro: los campos ya parseados, listos para el documento.</summary>
public sealed partial class RecordDetailViewModel : ViewModelBase
{
    public RecordDetailViewModel(RegistroDto registro)
    {
        Registro = registro;
        Campos = new ObservableCollection<CampoViewModel>(
            registro.Campos.Grupos.Select(kv => new CampoViewModel(kv.Key, kv.Value)));
    }

    public RegistroDto Registro { get; }

    public int Fila => Registro.Fila;
    public string ValorCrudo => Registro.ValorCrudo;
    public string Consecutivo => Registro.Campos.Consecutivo;
    public string Colegio => Registro.Campos.Colegio;
    public string Circuito => Registro.Campos.Circuito;
    public string Nombre => Registro.Campos.Nombre;

    /// <summary>
    /// Marca de la casilla para la generación masiva. Es independiente de la selección
    /// de la lista (esa solo decide qué registro se muestra en el panel de detalle).
    /// </summary>
    [ObservableProperty]
    private bool _marcado;

    /// <summary>
    /// Se lo inyecta <see cref="MainViewModel"/> para enterarse de cada marca sin
    /// suscribirse a un evento por fila.
    /// </summary>
    public Action? AlCambiarMarcado { get; set; }

    partial void OnMarcadoChanged(bool value) => AlCambiarMarcado?.Invoke();

    /// <summary>Todos los grupos del regex y las columnas extra, sean los de por defecto u otros configurados.</summary>
    public ObservableCollection<CampoViewModel> Campos { get; }

    /// <summary>
    /// Línea principal de la lista: el nombre, que es por lo que se busca un expediente.
    /// Si esa columna no está configurada o la celda está vacía, se cae al consecutivo
    /// para que la fila siga siendo identificable.
    /// </summary>
    public string Titulo
    {
        get
        {
            if (!string.IsNullOrEmpty(Nombre))
            {
                return Nombre;
            }

            return string.IsNullOrEmpty(Consecutivo) ? $"Fila {Fila}" : Consecutivo;
        }
    }

    /// <summary>Segunda línea: el resto de la identificación del expediente.</summary>
    public string Subtitulo
    {
        get
        {
            // Si el nombre ya ocupa el título, el consecutivo baja aquí; si no, no se repite.
            var partes = string.IsNullOrEmpty(Nombre)
                ? new[] { Colegio, Circuito }
                : [Consecutivo, Colegio, Circuito];

            var visibles = partes.Where(p => !string.IsNullOrEmpty(p)).ToArray();
            return visibles.Length == 0 ? ValorCrudo : string.Join(" · ", visibles);
        }
    }

    /// <summary>Texto sobre el que filtra la búsqueda de la lista.</summary>
    public bool Coincide(string termino) =>
        ValorCrudo.Contains(termino, StringComparison.OrdinalIgnoreCase) ||
        Nombre.Contains(termino, StringComparison.OrdinalIgnoreCase) ||
        Fila.ToString().Contains(termino, StringComparison.OrdinalIgnoreCase);

    /// <summary>Diccionario que consume <see cref="Services.WordDocumentGenerator"/>.</summary>
    public Dictionary<string, string> ValoresParaPlantilla(DatosAbogado abogado)
    {
        var valores = new Dictionary<string, string>(Registro.Campos.Grupos, StringComparer.OrdinalIgnoreCase)
        {
            ["valorCrudo"] = ValorCrudo,
            ["fila"] = Fila.ToString(),
            ["fecha"] = DateTime.Now.ToString("dd/MM/yyyy")
        };

        // Los datos del abogado son locales y los mismos para todos los registros.
        foreach (var (clave, valor) in abogado.ValoresParaPlantilla())
        {
            valores[clave] = valor;
        }

        return valores;
    }

    /// <summary>Nombre sugerido para el archivo, sin caracteres inválidos en Windows.</summary>
    public string NombreArchivoSugerido()
    {
        var baseNombre = string.IsNullOrEmpty(Consecutivo) ? $"registro-{Fila}" : Consecutivo;
        var limpio = string.Concat(baseNombre.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
        return $"{limpio}.docx";
    }
}

public sealed partial class CampoViewModel(string nombre, string valor) : ObservableObject
{
    public string Nombre { get; } = nombre;
    public string Valor { get; } = valor;

    /// <summary>El marcador que hay que escribir en la plantilla Word para este campo.</summary>
    public string Placeholder { get; } = $"{{{{{nombre}}}}}";
}
