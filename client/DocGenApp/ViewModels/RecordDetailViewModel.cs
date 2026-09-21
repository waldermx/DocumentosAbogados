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

    /// <summary>Todos los grupos del regex, sean los tres por defecto u otros configurados.</summary>
    public ObservableCollection<CampoViewModel> Campos { get; }

    /// <summary>Etiqueta corta para la lista lateral.</summary>
    public string Titulo => string.IsNullOrEmpty(Consecutivo)
        ? $"Fila {Fila}"
        : $"{Consecutivo} — {Colegio}";

    public string Subtitulo => string.IsNullOrEmpty(Circuito) ? ValorCrudo : Circuito;

    /// <summary>Diccionario que consume <see cref="Services.WordDocumentGenerator"/>.</summary>
    public Dictionary<string, string> ValoresParaPlantilla()
    {
        var valores = new Dictionary<string, string>(Registro.Campos.Grupos, StringComparer.OrdinalIgnoreCase)
        {
            ["valorCrudo"] = ValorCrudo,
            ["fila"] = Fila.ToString(),
            ["fecha"] = DateTime.Now.ToString("dd/MM/yyyy")
        };
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
