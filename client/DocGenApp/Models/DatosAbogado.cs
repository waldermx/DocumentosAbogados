using CommunityToolkit.Mvvm.ComponentModel;

namespace DocGenApp.Models;

/// <summary>
/// Datos de un abogado que firma. No vienen de la hoja: se capturan en la app y se guardan
/// en el equipo. Puede haber varios (un despacho con más de un abogado, o un documento
/// firmado por dos) — <b>todos</b> los que estén capturados se aplican a cada documento
/// generado, cada uno con sus propios marcadores numerados según su posición en la lista
/// (<c>{{abogado1Nombre}}</c>, <c>{{abogado2Nombre}}</c>, ...). No hay "seleccionar uno":
/// una plantilla con dos firmantes necesita los marcadores de ambos.
///
/// Es <see cref="ObservableObject"/> (no un record ni propiedades simples) para que los
/// TextBox del formulario reflejen los cambios de inmediato sin pasar por el ViewModel
/// contenedor.
/// </summary>
public sealed partial class DatosAbogado : ObservableObject
{
    /// <summary>Estable mientras el registro existe: identifica la fila en la lista aunque
    /// el nombre cambie o se reordene.</summary>
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

    /// <summary>
    /// Marcadores sin numerar (<c>{{abogadoNombre}}</c>, ...). Los rellena en la plantilla
    /// el <b>primer</b> abogado de la lista, para que una plantilla de un solo firmante
    /// (la de antes de que existiera esta lista) siga funcionando sin cambiarla.
    /// </summary>
    public IReadOnlyDictionary<string, string> ValoresParaPlantilla() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["abogadoNombre"] = Nombre.Trim(),
            ["abogadoFirel"] = UsuarioFirel.Trim(),
            ["abogadoCedula"] = CedulaProfesional.Trim()
        };

    /// <summary>
    /// Marcadores numerados según la posición del abogado en la lista (1-based):
    /// <c>{{abogado2Nombre}}</c> para el segundo, etc. Es lo que hace falta en una
    /// plantilla con más de un firmante.
    /// </summary>
    public IReadOnlyDictionary<string, string> ValoresParaPlantilla(int numero) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [$"abogado{numero}Nombre"] = Nombre.Trim(),
            [$"abogado{numero}Firel"] = UsuarioFirel.Trim(),
            [$"abogado{numero}Cedula"] = CedulaProfesional.Trim()
        };

    public bool EstaCompleto =>
        !string.IsNullOrWhiteSpace(Nombre) &&
        !string.IsNullOrWhiteSpace(UsuarioFirel) &&
        !string.IsNullOrWhiteSpace(CedulaProfesional);

    /// <summary>Para la lista de abogados: nunca una línea vacía, aunque el registro esté a medio llenar.</summary>
    public string EtiquetaCorta => string.IsNullOrWhiteSpace(Nombre) ? "(sin nombre)" : Nombre;
}
