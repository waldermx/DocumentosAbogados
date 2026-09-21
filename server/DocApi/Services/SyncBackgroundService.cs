using DocApi.Models;
using Microsoft.Extensions.Options;

namespace DocApi.Services;

/// <summary>
/// Dispara el paso 1 (chequeo barato de modifiedTime) cada
/// <c>Sync:PollingIntervalMinutes</c>. Solo cuando la hoja cambió se paga
/// el costo de leer y reparsear.
/// </summary>
public sealed class SyncBackgroundService : BackgroundService
{
    private readonly SyncCoordinator _coordinator;
    private readonly ISheetSource _source;
    private readonly SyncOptions _options;
    private readonly ILogger<SyncBackgroundService> _logger;

    public SyncBackgroundService(
        SyncCoordinator coordinator,
        ISheetSource source,
        IOptions<SyncOptions> options,
        ILogger<SyncBackgroundService> logger)
    {
        _coordinator = coordinator;
        _source = source;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_source.EstaConfigurado)
        {
            _logger.LogWarning("Polling deshabilitado: {Mensaje}", SheetSourceNoConfigurado.Mensaje);
            return;
        }

        var intervalo = TimeSpan.FromMinutes(Math.Max(1, _options.PollingIntervalMinutes));
        _logger.LogInformation("Polling de Google Sheets cada {Intervalo}.", intervalo);

        using var timer = new PeriodicTimer(intervalo);
        do
        {
            try
            {
                await _coordinator.SyncAsync(forzarLecturaCompleta: false, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // El coordinator ya captura sus propios errores; esto es una red de seguridad
                // para que el bucle de polling nunca muera.
                _logger.LogError(ex, "Error inesperado en el ciclo de polling.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
