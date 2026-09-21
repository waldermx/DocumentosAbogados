using CommunityToolkit.Mvvm.ComponentModel;

namespace DocGenApp.Models;

/// <summary>
/// Datos de un abogado que firma. No vienen de la hoja: se capturan en la app y se guardan
/// en el equipo. Puede haber varios (un despacho con más de un abogado); al generar se usa
/// el que esté seleccionado en <see cref="ViewModels.MainViewModel"/>.
///
/// Es <see cref="ObservableObject"/> (no un record ni propiedades simples) para que el
/// ComboBox de selección y los TextBox del formulario reflejen los cambios de inmediato
/// sin pasar por el ViewModel contenedor.
/// </summary>
public sealed partial class DatosAbogado : ObservableObject
{
    /// <summary>
    /// Estable mientras el registro existe: identifica la fila seleccionada aunque el
    /// nombre cambie, y es lo que persiste <see cref="Services.AbogadoStore"/> como
    /// "abogado activo" entre arranques.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstaCompleto))]
    [NotifyPropertyChangedFor(nameof(EtiquetaCorta))]
    private string _nombre = string.Empty;

    /// <summary>Usuario de FIREL (firma electrónica del Poder Judicial). No es una contraseña.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstaCompleto))]
    private string _usuarioFirel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstaCompleto))]
    private string _cedulaProfesional = string.Empty;

    /// <summary>Marcadores que estos campos aportan a la plantilla Word.</summary>
    public IReadOnlyDictionary<string, string> ValoresParaPlantilla() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["abogadoNombre"] = Nombre.Trim(),
            ["abogadoFirel"] = UsuarioFirel.Trim(),
            ["abogadoCedula"] = CedulaProfesional.Trim()
        };

    public bool EstaCompleto =>
        !string.IsNullOrWhiteSpace(Nombre) &&
        !string.IsNullOrWhiteSpace(UsuarioFirel) &&
        !string.IsNullOrWhiteSpace(CedulaProfesional);

    /// <summary>Para el ComboBox de selección: nunca una línea vacía, aunque el registro esté a medio llenar.</summary>
    public string EtiquetaCorta => string.IsNullOrWhiteSpace(Nombre) ? "(sin nombre)" : Nombre;
}
