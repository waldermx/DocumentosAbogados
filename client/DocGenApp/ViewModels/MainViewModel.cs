using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocGenApp.Models;
using DocGenApp.Services;

namespace DocGenApp.ViewModels;

public enum EstadoApp
{
    Cargando,
    Sincronizando,
    Ok,
    SinConexion,
    Error,
    PidiendoPassword
}

public sealed partial class MainViewModel : ViewModelBase
{
    private readonly ApiClient _api;
    private readonly LocalCacheService _cacheLocal;
    private readonly CredentialStore _credenciales;
    private readonly AbogadoStore _abogadoStore;
    private readonly WordDocumentGenerator _generador;
    private readonly AppSettings _settings;

    /// <summary>Lista completa; <see cref="Registros"/> es la vista ya filtrada que ve la UI.</summary>
    private List<RecordDetailViewModel> _todos = [];

    /// <summary>Evita que marcar en bloque dispare el recálculo una vez por fila.</summary>
    private bool _marcandoEnBloque;

    /// <summary>Lo inyecta la vista para poder abrir el diálogo de guardado desde el comando.</summary>
    public Func<string, Task<string?>>? PedirRutaDeGuardado { get; set; }

    /// <summary>Ídem para el diálogo de carpeta, que usa la generación masiva.</summary>
    public Func<Task<string?>>? PedirCarpetaDeSalida { get; set; }

    /// <summary>Lo inyecta la vista: abre un archivo o carpeta con la app asociada del sistema.</summary>
    public Action<string>? AbrirEnElSistema { get; set; }

    public MainViewModel(
        ApiClient api,
        LocalCacheService cacheLocal,
        CredentialStore credenciales,
        AbogadoStore abogadoStore,
        WordDocumentGenerator generador,
        AppSettings settings)
    {
        _api = api;
        _cacheLocal = cacheLocal;
        _credenciales = credenciales;
        _abogadoStore = abogadoStore;
        _generador = generador;
        _settings = settings;

        var persistido = _abogadoStore.Cargar();
        _abogados = new ObservableCollection<DatosAbogado>(persistido.Abogados);
        foreach (var abogado in _abogados)
        {
            abogado.PropertyChanged += AlCambiarUnAbogado;
        }

        _abogadosParaMostrar = ConstruirVistaDeAbogados();
        _abogadoExpandido = _abogados.Count == 0 || _abogados.Any(a => !a.EstaCompleto);
    }

    [ObservableProperty]
    private ObservableCollection<RecordDetailViewModel> _registros = [];

    /// <summary>Registros ya marcados como impresos: se ocultan de <see cref="Registros"/>
    /// y solo se ven a través del desplegable "Procesados" de la lista.</summary>
    [ObservableProperty]
    private ObservableCollection<RecordDetailViewModel> _registrosImpresos = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayImpresos))]
    [NotifyCanExecuteChangedFor(nameof(DesmarcarTodosImpresosCommand))]
    private int _totalImpresos;

    [ObservableProperty]
    private bool _mostrarImpresos;

    public bool HayImpresos => TotalImpresos > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayRegistroSeleccionado))]
    [NotifyCanExecuteChangedFor(nameof(GenerarDocumentoCommand))]
    [NotifyCanExecuteChangedFor(nameof(MarcarImpresoCommand))]
    [NotifyCanExecuteChangedFor(nameof(DesmarcarImpresoCommand))]
    private RecordDetailViewModel? _registroSeleccionado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsSoloLectura))]
    [NotifyPropertyChangedFor(nameof(EstaOcupado))]
    private EstadoApp _estado = EstadoApp.Cargando;

    [ObservableProperty]
    private string _mensajeEstado = "Iniciando…";

    [ObservableProperty]
    private string? _mensajeExito;

    [ObservableProperty]
    private string _passwordIngresada = string.Empty;

    [ObservableProperty]
    private string? _ultimaSyncTexto;

    public bool HayRegistroSeleccionado => RegistroSeleccionado is not null;
    public bool EsSoloLectura => Estado == EstadoApp.SinConexion;
    public bool EstaOcupado => Estado is EstadoApp.Cargando or EstadoApp.Sincronizando;
    public bool PidePassword => Estado == EstadoApp.PidiendoPassword;

    partial void OnEstadoChanged(EstadoApp value) => OnPropertyChanged(nameof(PidePassword));

    // ---------------------------------------------------------------- Datos de los abogados
    //
    // No hay "elegir uno": TODOS los abogados de la lista se aplican a cada documento
    // generado, cada uno con sus marcadores numerados según su posición
    // (abogado1Nombre, abogado2Nombre, ...) — para plantillas con más de un firmante.

    [ObservableProperty]
    private ObservableCollection<DatosAbogado> _abogados;

    /// <summary>Vista para la UI: un envoltorio por abogado con su número y sus marcadores,
    /// recalculada cada vez que la lista cambia de tamaño u orden.</summary>
    [ObservableProperty]
    private ObservableCollection<AbogadoItemViewModel> _abogadosParaMostrar;

    [ObservableProperty]
    private bool _abogadoExpandido;

    [ObservableProperty]
    private string? _mensajeAbogado;

    /// <summary>
    /// Mutar <see cref="Abogados"/> con Add/Remove no reasigna la propiedad, así que el
    /// setter generado por [ObservableProperty] no dispara — de ahí que este y
    /// <see cref="ResumenAbogado"/> se notifiquen a mano en cada Agregar/Eliminar.
    /// </summary>
    public bool HayAlgunAbogado => Abogados.Count > 0;

    public string ResumenAbogado
    {
        get
        {
            if (Abogados.Count == 0)
            {
                return "No hay ningún abogado capturado — se dejarán los marcadores sin sustituir.";
            }

            var incompletos = Abogados.Count(a => !a.EstaCompleto);
            var listado = string.Join(", ", Abogados.Select((a, i) => $"{i + 1}) {a.EtiquetaCorta}"));

            return incompletos == 0
                ? $"{Abogados.Count} abogado(s) — {listado}"
                : $"{Abogados.Count} abogado(s) — {listado} ({incompletos} con datos incompletos)";
        }
    }

    /// <summary>Reenvía el cambio de cualquier campo de cualquier abogado al resumen,
    /// que si no solo se refrescaría al agregar o quitar uno de la lista.</summary>
    private void AlCambiarUnAbogado(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        OnPropertyChanged(nameof(ResumenAbogado));

    private ObservableCollection<AbogadoItemViewModel> ConstruirVistaDeAbogados() =>
        new(Abogados.Select((a, i) => new AbogadoItemViewModel(a, i + 1)));

    [RelayCommand]
    private void AgregarAbogado()
    {
        var nuevo = new DatosAbogado();
        nuevo.PropertyChanged += AlCambiarUnAbogado;
        Abogados.Add(nuevo);
        AbogadosParaMostrar = ConstruirVistaDeAbogados();
        AbogadoExpandido = true;
        MensajeAbogado = null;
        OnPropertyChanged(nameof(ResumenAbogado));
        OnPropertyChanged(nameof(HayAlgunAbogado));
    }

    [RelayCommand]
    private void EliminarAbogado(AbogadoItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        item.Datos.PropertyChanged -= AlCambiarUnAbogado;
        Abogados.Remove(item.Datos);
        AbogadosParaMostrar = ConstruirVistaDeAbogados();
        OnPropertyChanged(nameof(ResumenAbogado));
        OnPropertyChanged(nameof(HayAlgunAbogado));

        // Eliminar es difícil de deshacer (no hay "deshacer" en la app): se persiste
        // de inmediato, para que un cierre accidental no lo resucite.
        GuardarAbogados();
    }

    /// <summary>Sube un abogado un puesto: cambia si es <c>{{abogado1...}}</c> o <c>{{abogado2...}}</c>.</summary>
    [RelayCommand]
    private void MoverAbogadoArriba(AbogadoItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var indice = Abogados.IndexOf(item.Datos);
        if (indice <= 0)
        {
            return;
        }

        Abogados.Move(indice, indice - 1);
        AbogadosParaMostrar = ConstruirVistaDeAbogados();
    }

    [RelayCommand]
    private void MoverAbogadoAbajo(AbogadoItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var indice = Abogados.IndexOf(item.Datos);
        if (indice < 0 || indice >= Abogados.Count - 1)
        {
            return;
        }

        Abogados.Move(indice, indice + 1);
        AbogadosParaMostrar = ConstruirVistaDeAbogados();
    }

    [RelayCommand]
    private void GuardarAbogados()
    {
        try
        {
            _abogadoStore.Guardar(new AbogadosPersistidos { Abogados = Abogados.ToList() });

            MensajeAbogado = Abogados.Count switch
            {
                0 => "Sin abogados guardados.",
                _ when Abogados.All(a => a.EstaCompleto) => "Datos guardados en este equipo.",
                _ => "Datos guardados, pero hay campos vacíos."
            };

            AbogadoExpandido = Abogados.Count == 0 || Abogados.Any(a => !a.EstaCompleto);
        }
        catch (Exception ex)
        {
            MensajeAbogado = $"No se pudieron guardar: {ex.Message}";
        }
    }

    // ---------------------------------------------------------------- Selección múltiple

    [ObservableProperty]
    private string _filtro = string.Empty;

    partial void OnFiltroChanged(string value) => AplicarFiltro();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMarcados))]
    [NotifyPropertyChangedFor(nameof(TextoBotonMasivo))]
    private int _totalMarcados;

    public bool HayMarcados => TotalMarcados > 0;

    public string TextoBotonMasivo => TotalMarcados switch
    {
        0 => "Generar seleccionados",
        1 => "Generar 1 documento",
        _ => $"Generar {TotalMarcados} documentos"
    };

    /// <summary>
    /// Estado de la casilla de cabecera. <c>null</c> = algunos marcados, para que
    /// se vea de un vistazo que la selección es parcial.
    /// </summary>
    public bool? TodosMarcados
    {
        get
        {
            if (Registros.Count == 0 || TotalMarcados == 0)
            {
                return false;
            }

            return Registros.All(r => r.Marcado) ? true : null;
        }
        set => MarcarVisibles(value == true);
    }

    [RelayCommand]
    private void MarcarTodos() => MarcarVisibles(true);

    [RelayCommand]
    private void DesmarcarTodos()
    {
        // Se desmarca todo, no solo lo visible: si no, un filtro activo dejaría
        // marcas escondidas que igual se generarían.
        _marcandoEnBloque = true;
        foreach (var registro in _todos)
        {
            registro.Marcado = false;
        }
        _marcandoEnBloque = false;
        RecalcularMarcados();
    }

    /// <summary>Marca o desmarca lo que el filtro deja a la vista: es el «seleccionar todos» útil.</summary>
    private void MarcarVisibles(bool marcado)
    {
        _marcandoEnBloque = true;
        foreach (var registro in Registros)
        {
            registro.Marcado = marcado;
        }
        _marcandoEnBloque = false;
        RecalcularMarcados();
    }

    private void RecalcularMarcados()
    {
        TotalMarcados = _todos.Count(r => r.Marcado);
        OnPropertyChanged(nameof(TodosMarcados));
        GenerarSeleccionadosCommand.NotifyCanExecuteChanged();
    }

    private void AplicarFiltro()
    {
        var termino = Filtro.Trim();
        var coincidentes = termino.Length == 0
            ? _todos
            : _todos.Where(r => r.Coincide(termino)).ToList();

        Registros = new ObservableCollection<RecordDetailViewModel>(coincidentes.Where(r => !r.EstaImpreso));
        RegistrosImpresos = new ObservableCollection<RecordDetailViewModel>(coincidentes.Where(r => r.EstaImpreso));
        TotalImpresos = RegistrosImpresos.Count;
        OnPropertyChanged(nameof(TodosMarcados));

        if (RegistroSeleccionado is null ||
            (!Registros.Contains(RegistroSeleccionado) && !RegistrosImpresos.Contains(RegistroSeleccionado)))
        {
            RegistroSeleccionado = Registros.FirstOrDefault() ?? RegistrosImpresos.FirstOrDefault();
        }
    }

    // ---------------------------------------------------------------- Carga de datos

    /// <summary>Arranque: si hay password guardada se carga directo; si no, se pide.</summary>
    public async Task InicializarAsync()
    {
        var password = _credenciales.Cargar();
        if (string.IsNullOrEmpty(password))
        {
            CargarDesdeCacheLocal(avisar: false);
            Estado = EstadoApp.PidiendoPassword;
            MensajeEstado = "Introduce la password maestra para conectar con el servidor.";
            return;
        }

        _api.MasterPassword = password;
        await CargarRegistrosAsync();
    }

    [RelayCommand]
    private async Task ConfirmarPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(PasswordIngresada))
        {
            MensajeEstado = "La password no puede estar vacía.";
            return;
        }

        _api.MasterPassword = PasswordIngresada;
        Estado = EstadoApp.Cargando;
        MensajeEstado = "Conectando…";

        var resultado = await _api.GetRegistrosAsync(CancellationToken.None);
        if (!resultado.Exito)
        {
            // Solo se guarda una password que el servidor aceptó.
            Estado = EstadoApp.PidiendoPassword;
            MensajeEstado = resultado.Error ?? "No se pudo conectar.";
            return;
        }

        _credenciales.Guardar(PasswordIngresada);
        PasswordIngresada = string.Empty;
        AplicarPayload(resultado.Valor!);
    }

    [RelayCommand]
    private void OlvidarPassword()
    {
        _credenciales.Borrar();
        _api.MasterPassword = null;
        Estado = EstadoApp.PidiendoPassword;
        MensajeEstado = "Introduce la password maestra para conectar con el servidor.";
    }

    /// <summary>
    /// Botón «Sincronizar»: fuerza sync en el servidor y luego relee los registros. Es la
    /// única forma de traer cambios de la hoja: el servidor no sincroniza por su cuenta.
    /// </summary>
    [RelayCommand]
    private async Task RefrescarAsync()
    {
        if (Estado == EstadoApp.PidiendoPassword)
        {
            return;
        }

        Estado = EstadoApp.Sincronizando;
        MensajeEstado = "Sincronizando con Google Sheets…";
        MensajeExito = null;

        var sync = await _api.PostSyncAsync(CancellationToken.None);
        if (!sync.Exito)
        {
            // El sync puede fallar (p. ej. sin credenciales de Google) y aun así
            // haber datos cacheados que vale la pena mostrar.
            MensajeEstado = sync.Error ?? "No se pudo sincronizar.";
        }

        await CargarRegistrosAsync(conservarMensaje: !sync.Exito);
    }

    private async Task CargarRegistrosAsync(bool conservarMensaje = false)
    {
        if (Estado != EstadoApp.Sincronizando)
        {
            Estado = EstadoApp.Cargando;
            MensajeEstado = "Cargando registros…";
        }

        var resultado = await _api.GetRegistrosAsync(CancellationToken.None);

        if (resultado.Exito)
        {
            var mensajePrevio = conservarMensaje ? MensajeEstado : null;
            AplicarPayload(resultado.Valor!);
            if (mensajePrevio is not null)
            {
                Estado = EstadoApp.Error;
                MensajeEstado = mensajePrevio;
            }
            return;
        }

        if (resultado.Error is not null && resultado.Error.Contains("Password maestra incorrecta"))
        {
            Estado = EstadoApp.PidiendoPassword;
            MensajeEstado = resultado.Error;
            return;
        }

        // Sin servidor: se cae al caché local en modo solo lectura (RNF-09).
        CargarDesdeCacheLocal(avisar: true, detalle: resultado.Error);
    }

    private void AplicarPayload(RegistrosResponseDto payload)
    {
        _cacheLocal.Guardar(payload);
        Poblar(payload);

        Estado = EstadoApp.Ok;
        MensajeEstado = $"{_todos.Count} registros cargados.";
        UltimaSyncTexto = payload.UltimaSync is { } s
            ? $"Última sincronización: {s.ToLocalTime():dd/MM/yyyy HH:mm}"
            : "Sin sincronizar todavía: pulsa «Sincronizar».";
    }

    private void CargarDesdeCacheLocal(bool avisar, string? detalle = null)
    {
        var cacheado = _cacheLocal.Cargar();
        if (cacheado is null)
        {
            Estado = EstadoApp.Error;
            MensajeEstado = detalle ?? "No hay conexión con el servidor ni datos guardados localmente.";
            return;
        }

        Poblar(cacheado);

        if (avisar)
        {
            Estado = EstadoApp.SinConexion;
            var fecha = _cacheLocal.FechaCache;
            MensajeEstado = fecha is null
                ? "Sin conexión. Mostrando la última copia local (solo lectura)."
                : $"Sin conexión. Mostrando la copia local del {fecha:dd/MM/yyyy HH:mm} (solo lectura).";
        }
    }

    private void Poblar(RegistrosResponseDto payload)
    {
        var filaSeleccionada = RegistroSeleccionado?.Fila;
        var filasMarcadas = _todos.Where(r => r.Marcado).Select(r => r.Fila).ToHashSet();

        _todos = payload.Registros.Select(r =>
        {
            var vm = new RecordDetailViewModel(r)
            {
                Marcado = filasMarcadas.Contains(r.Fila),
                Impreso = payload.Impresos.TryGetValue(r.Fila, out var fecha) ? fecha : null
            };
            vm.AlCambiarMarcado = () =>
            {
                if (!_marcandoEnBloque)
                {
                    RecalcularMarcados();
                }
            };
            return vm;
        }).ToList();

        AplicarFiltro();
        RecalcularMarcados();

        // Se intenta conservar la selección tras un refresco.
        RegistroSeleccionado = filaSeleccionada is null
            ? Registros.FirstOrDefault()
            : Registros.FirstOrDefault(r => r.Fila == filaSeleccionada) ?? Registros.FirstOrDefault();
    }

    // ---------------------------------------------------------------- Generación

    [ObservableProperty]
    private bool _generando;

    [ObservableProperty]
    private string? _progresoTexto;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayDocumentoParaAbrir))]
    [NotifyCanExecuteChangedFor(nameof(AbrirUltimoDocumentoCommand))]
    private string? _ultimoDocumentoGenerado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayCarpetaParaAbrir))]
    [NotifyCanExecuteChangedFor(nameof(AbrirCarpetaGeneradaCommand))]
    private string? _ultimaCarpetaGenerada;

    public bool HayDocumentoParaAbrir => !string.IsNullOrEmpty(UltimoDocumentoGenerado);
    public bool HayCarpetaParaAbrir => !string.IsNullOrEmpty(UltimaCarpetaGenerada);

    [RelayCommand(CanExecute = nameof(HayDocumentoParaAbrir))]
    private void AbrirUltimoDocumento() => AbrirConElSistema(UltimoDocumentoGenerado);

    [RelayCommand(CanExecute = nameof(HayCarpetaParaAbrir))]
    private void AbrirCarpetaGenerada() => AbrirConElSistema(UltimaCarpetaGenerada);

    private void AbrirConElSistema(string? ruta)
    {
        if (ruta is null)
        {
            return;
        }

        try
        {
            AbrirEnElSistema?.Invoke(ruta);
        }
        catch (Exception ex)
        {
            // No es un error de generación (el documento ya existe en disco); se avisa
            // aparte para no pisar el mensaje de éxito que sigue siendo válido.
            MensajeEstado = $"No se pudo abrir «{ruta}». {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(HayRegistroSeleccionado))]
    private async Task GenerarDocumentoAsync()
    {
        var registro = RegistroSeleccionado;
        if (registro is null)
        {
            return;
        }

        MensajeExito = null;

        try
        {
            var destino = await ResolverDestinoAsync(registro);
            if (destino is null)
            {
                return; // el usuario canceló
            }

            var faltantes = _generador.Generar(RutaPlantilla(), registro.ValoresParaPlantilla(Abogados), destino);

            UltimoDocumentoGenerado = destino;
            MensajeExito = faltantes.Count == 0
                ? $"Documento generado: {destino}"
                : $"Documento generado en {destino}, pero sin valor para: {string.Join(", ", faltantes)}.";

            // Generar saca el registro de la lista: se marca como impreso igual que con
            // el botón manual, así que reaparece bajo «Impresos» y no se genera dos veces.
            if (await MarcarImpresoAsync(registro))
            {
                AplicarFiltro();
                RecalcularMarcados();
            }
        }
        catch (Exception ex)
        {
            Estado = EstadoApp.Error;
            MensajeEstado = $"No se pudo generar el documento. {ex.Message}";
        }
    }

    /// <summary>
    /// Marca manual, para los casos en que el usuario quiere sacar un registro de la
    /// lista sin generar su documento (o volver a sacarlo tras desmarcarlo).
    /// </summary>
    [RelayCommand(CanExecute = nameof(HayRegistroSeleccionado))]
    private async Task MarcarImpresoAsync()
    {
        var registro = RegistroSeleccionado;
        if (registro is null)
        {
            return;
        }

        if (await MarcarImpresoAsync(registro))
        {
            AplicarFiltro();
        }
    }

    /// <summary>
    /// Marca el registro como impreso en el servidor. Es lo que oculta el registro de la
    /// lista principal, y lo llaman tanto el botón manual como la generación de documentos.
    /// El caller refresca la vista (<see cref="AplicarFiltro"/>) para poder agrupar el
    /// refresco de un lote entero en uno solo.
    /// </summary>
    /// <returns><c>true</c> si el registro quedó marcado.</returns>
    private async Task<bool> MarcarImpresoAsync(RecordDetailViewModel registro)
    {
        if (registro.EstaImpreso)
        {
            return false;
        }

        var resultado = await _api.PostMarcarImpresoAsync(registro.Fila, CancellationToken.None);
        if (!resultado.Exito)
        {
            MensajeEstado = resultado.Error ?? "No se pudo marcar como impreso.";
            return false;
        }

        registro.Impreso = resultado.Valor!.Impreso;
        registro.Marcado = false; // ya no está en la lista: no debe seguir contando para el lote
        return true;
    }

    /// <summary>
    /// Devuelve a la lista principal todo lo marcado como impreso. Es la salida para
    /// cuando hay que rehacer un lote entero, en vez de desmarcar fila por fila.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HayImpresos))]
    private async Task DesmarcarTodosImpresosAsync()
    {
        var resultado = await _api.DeleteTodosImpresosAsync(CancellationToken.None);
        if (!resultado.Exito)
        {
            MensajeEstado = resultado.Error ?? "No se pudieron desmarcar los impresos.";
            return;
        }

        foreach (var registro in _todos)
        {
            registro.Impreso = null;
        }

        AplicarFiltro();
        MostrarImpresos = false; // el desplegable se queda vacío: no tiene sentido abierto
        MensajeExito = resultado.Valor!.Desmarcados switch
        {
            0 => "No había registros marcados como impresos.",
            1 => "1 registro devuelto a la lista.",
            var n => $"{n} registros devueltos a la lista."
        };
    }

    [RelayCommand(CanExecute = nameof(HayRegistroSeleccionado))]
    private async Task DesmarcarImpresoAsync()
    {
        var registro = RegistroSeleccionado;
        if (registro is null)
        {
            return;
        }

        var resultado = await _api.DeleteMarcarImpresoAsync(registro.Fila, CancellationToken.None);
        if (resultado.Exito)
        {
            registro.Impreso = resultado.Valor!.Impreso;
            AplicarFiltro();
        }
        else
        {
            MensajeEstado = resultado.Error ?? "No se pudo desmarcar.";
        }
    }

    /// <summary>
    /// Generación masiva: un .docx por registro marcado, todos en la misma carpeta.
    /// Un fallo en una fila no aborta el lote; se cuentan y se reportan al final.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HayMarcados))]
    private async Task GenerarSeleccionadosAsync()
    {
        var marcados = _todos.Where(r => r.Marcado).OrderBy(r => r.Fila).ToList();
        if (marcados.Count == 0)
        {
            return;
        }

        MensajeExito = null;

        var carpeta = await ResolverCarpetaAsync();
        if (carpeta is null)
        {
            return; // el usuario canceló
        }

        var plantilla = RutaPlantilla();
        var abogados = Abogados.ToList(); // instantánea: la lista no debe cambiar a medio lote

        Generando = true;
        var generados = 0;
        var generadosOk = new List<RecordDetailViewModel>();
        var fallidos = new List<string>();
        var sinValor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            Directory.CreateDirectory(carpeta);

            for (var i = 0; i < marcados.Count; i++)
            {
                var registro = marcados[i];
                ProgresoTexto = $"Generando {i + 1} de {marcados.Count}: {registro.Titulo}";

                var destino = Path.Combine(carpeta, NombreUnico(registro, usados));
                var valores = registro.ValoresParaPlantilla(abogados);

                try
                {
                    // Fuera del hilo de UI: con muchos registros, abrir y reescribir
                    // cada .docx bloquearía la ventana.
                    var faltantes = await Task.Run(() => _generador.Generar(plantilla, valores, destino));
                    foreach (var f in faltantes)
                    {
                        sinValor.Add(f);
                    }
                    generados++;
                    generadosOk.Add(registro);
                }
                catch (Exception ex)
                {
                    fallidos.Add($"fila {registro.Fila}: {ex.Message}");
                }
            }
        }
        finally
        {
            Generando = false;
            ProgresoTexto = null;
        }

        if (generados > 0)
        {
            UltimaCarpetaGenerada = carpeta;
        }

        // Igual que en la generación individual: lo generado sale de la lista. Los que
        // fallaron se quedan a la vista para poder reintentarlos. Un solo refresco al
        // final, no uno por registro.
        var ocultados = false;
        _marcandoEnBloque = true; // un recuento al final, no uno por cada registro del lote
        foreach (var registro in generadosOk)
        {
            ocultados |= await MarcarImpresoAsync(registro);
        }
        _marcandoEnBloque = false;

        if (ocultados)
        {
            AplicarFiltro();
            RecalcularMarcados();
        }

        MensajeExito = ResumenDelLote(carpeta, generados, fallidos, sinValor);

        if (fallidos.Count > 0)
        {
            Estado = EstadoApp.Error;
            MensajeEstado = $"{fallidos.Count} documento(s) no se pudieron generar. {string.Join(" | ", fallidos.Take(3))}";
        }
    }

    private static string ResumenDelLote(
        string carpeta, int generados, List<string> fallidos, HashSet<string> sinValor)
    {
        var resumen = $"{generados} documento(s) generados en {carpeta}.";

        if (fallidos.Count > 0)
        {
            resumen += $" {fallidos.Count} con error.";
        }

        if (sinValor.Count > 0)
        {
            resumen += $" Marcadores sin valor en alguna plantilla: {string.Join(", ", sinValor.Order())}.";
        }

        return resumen;
    }

    /// <summary>
    /// Dos registros distintos pueden compartir consecutivo; sin esto, el segundo
    /// sobrescribiría al primero en silencio.
    /// </summary>
    private static string NombreUnico(RecordDetailViewModel registro, HashSet<string> usados)
    {
        var nombre = registro.NombreArchivoSugerido();
        if (usados.Add(nombre))
        {
            return nombre;
        }

        var baseNombre = Path.GetFileNameWithoutExtension(nombre);
        var candidato = $"{baseNombre}-fila{registro.Fila}.docx";
        for (var n = 2; !usados.Add(candidato); n++)
        {
            candidato = $"{baseNombre}-fila{registro.Fila}-{n}.docx";
        }

        return candidato;
    }

    private string RutaPlantilla() =>
        Path.IsPathRooted(_settings.TemplatePath)
            ? _settings.TemplatePath
            : Path.Combine(AppContext.BaseDirectory, _settings.TemplatePath);

    private async Task<string?> ResolverDestinoAsync(RecordDetailViewModel registro)
    {
        var sugerido = registro.NombreArchivoSugerido();

        // Con carpeta configurada se guarda directo; si no, se pregunta.
        if (!string.IsNullOrWhiteSpace(_settings.DefaultOutputFolder))
        {
            Directory.CreateDirectory(_settings.DefaultOutputFolder);
            return Path.Combine(_settings.DefaultOutputFolder, sugerido);
        }

        return PedirRutaDeGuardado is null ? null : await PedirRutaDeGuardado(sugerido);
    }

    private async Task<string?> ResolverCarpetaAsync()
    {
        if (!string.IsNullOrWhiteSpace(_settings.DefaultOutputFolder))
        {
            return _settings.DefaultOutputFolder;
        }

        return PedirCarpetaDeSalida is null ? null : await PedirCarpetaDeSalida();
    }
}

/// <summary>
/// Envoltorio de presentación: un <see cref="DatosAbogado"/> más su posición en la lista,
/// que es lo que decide sus marcadores (<c>{{abogado2Nombre}}</c> para el segundo, etc.).
/// Se reconstruye entera cada vez que la lista cambia de tamaño u orden, así que
/// <see cref="Numero"/> nunca queda desactualizado.
/// </summary>
public sealed class AbogadoItemViewModel(DatosAbogado datos, int numero)
{
    public DatosAbogado Datos { get; } = datos;
    public int Numero { get; } = numero;

    public string Etiqueta => $"Abogado {Numero}";
    public string MarcadorNombre => $"{{{{abogado{Numero}Nombre}}}}";
    public string MarcadorFirel => $"{{{{abogado{Numero}Firel}}}}";
    public string MarcadorCedula => $"{{{{abogado{Numero}Cedula}}}}";
}
