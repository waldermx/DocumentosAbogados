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
    private readonly WordDocumentGenerator _generador;
    private readonly AppSettings _settings;

    /// <summary>Lo inyecta la vista para poder abrir el diálogo de guardado desde el comando.</summary>
    public Func<string, Task<string?>>? PedirRutaDeGuardado { get; set; }

    public MainViewModel(
        ApiClient api,
        LocalCacheService cacheLocal,
        CredentialStore credenciales,
        WordDocumentGenerator generador,
        AppSettings settings)
    {
        _api = api;
        _cacheLocal = cacheLocal;
        _credenciales = credenciales;
        _generador = generador;
        _settings = settings;
    }

    [ObservableProperty]
    private ObservableCollection<RecordDetailViewModel> _registros = [];

    [ObservableProperty]
    private ObservableCollection<ErrorParseoDto> _erroresParseo = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayRegistroSeleccionado))]
    [NotifyCanExecuteChangedFor(nameof(GenerarDocumentoCommand))]
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayErroresParseo))]
    private int _totalErroresParseo;

    public bool HayRegistroSeleccionado => RegistroSeleccionado is not null;
    public bool HayErroresParseo => TotalErroresParseo > 0;
    public bool EsSoloLectura => Estado == EstadoApp.SinConexion;
    public bool EstaOcupado => Estado is EstadoApp.Cargando or EstadoApp.Sincronizando;
    public bool PidePassword => Estado == EstadoApp.PidiendoPassword;

    partial void OnEstadoChanged(EstadoApp value) => OnPropertyChanged(nameof(PidePassword));

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

    /// <summary>Botón "Actualizar": fuerza sync en el servidor y luego relee los registros.</summary>
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
        MensajeEstado = $"{Registros.Count} registros cargados.";
        UltimaSyncTexto = payload.UltimaSync is { } s
            ? $"Última sincronización: {s.ToLocalTime():dd/MM/yyyy HH:mm}"
            : "Sin sincronizar todavía.";
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

        Registros = new ObservableCollection<RecordDetailViewModel>(
            payload.Registros.Select(r => new RecordDetailViewModel(r)));
        ErroresParseo = new ObservableCollection<ErrorParseoDto>(payload.ErroresParseo);
        TotalErroresParseo = payload.ErroresParseo.Count;

        // Se intenta conservar la selección tras un refresco.
        RegistroSeleccionado = filaSeleccionada is null
            ? Registros.FirstOrDefault()
            : Registros.FirstOrDefault(r => r.Fila == filaSeleccionada) ?? Registros.FirstOrDefault();
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
            var plantilla = Path.IsPathRooted(_settings.TemplatePath)
                ? _settings.TemplatePath
                : Path.Combine(AppContext.BaseDirectory, _settings.TemplatePath);

            var destino = await ResolverDestinoAsync(registro);
            if (destino is null)
            {
                return; // el usuario canceló
            }

            var faltantes = _generador.Generar(plantilla, registro.ValoresParaPlantilla(), destino);

            MensajeExito = faltantes.Count == 0
                ? $"Documento generado: {destino}"
                : $"Documento generado en {destino}, pero sin valor para: {string.Join(", ", faltantes)}.";
        }
        catch (Exception ex)
        {
            Estado = EstadoApp.Error;
            MensajeEstado = $"No se pudo generar el documento. {ex.Message}";
        }
    }

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
}
