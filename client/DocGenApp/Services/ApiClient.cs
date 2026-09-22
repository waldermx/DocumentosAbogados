using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocGenApp.Models;

namespace DocGenApp.Services;

/// <summary>
/// Cliente HTTP hacia el servidor. Nunca lanza hacia la UI: todo error de red,
/// timeout o respuesta no exitosa vuelve como <see cref="ApiResult{T}"/> fallido.
/// </summary>
public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ApiClient(AppSettings settings)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(settings.ServerBaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(Math.Max(5, settings.TimeoutSeconds))
        };
    }

    /// <summary>Password maestra que se envía como Bearer. Se puede cambiar en caliente.</summary>
    public string? MasterPassword { get; set; }

    public Task<ApiResult<RegistrosResponseDto>> GetRegistrosAsync(CancellationToken ct) =>
        EnviarAsync<RegistrosResponseDto>(HttpMethod.Get, "registros", ct);

    // forzar=true: el botón manual siempre debe traer datos frescos, sin depender
    // del chequeo barato de modifiedTime (que puede no reflejar ediciones como
    // "buscar y reemplazar" hechas en bloque dentro de Sheets).
    public Task<ApiResult<SyncResultDto>> PostSyncAsync(CancellationToken ct) =>
        EnviarAsync<SyncResultDto>(HttpMethod.Post, "sync?forzar=true", ct);

    private async Task<ApiResult<T>> EnviarAsync<T>(HttpMethod metodo, string ruta, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(metodo, ruta);
            if (!string.IsNullOrEmpty(MasterPassword))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MasterPassword);
            }

            using var respuesta = await _http.SendAsync(request, ct).ConfigureAwait(false);

            if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
            {
                return ApiResult<T>.Falla("Password maestra incorrecta o no configurada.");
            }

            if (!respuesta.IsSuccessStatusCode)
            {
                var detalle = await LeerDetalleErrorAsync(respuesta, ct).ConfigureAwait(false);
                return ApiResult<T>.Falla($"El servidor respondió {(int)respuesta.StatusCode}. {detalle}".Trim());
            }

            var valor = await respuesta.Content.ReadFromJsonAsync<T>(JsonOpts, ct).ConfigureAwait(false);
            return valor is null
                ? ApiResult<T>.Falla("El servidor devolvió una respuesta vacía.")
                : ApiResult<T>.Ok(valor);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ApiResult<T>.Falla("El servidor no respondió a tiempo.");
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<T>.Falla($"No se pudo conectar con el servidor. {ex.Message}");
        }
        catch (Exception ex)
        {
            return ApiResult<T>.Falla($"Error inesperado al llamar al servidor. {ex.Message}");
        }
    }

    private static async Task<string> LeerDetalleErrorAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(cuerpo))
            {
                return string.Empty;
            }

            // El server manda { "error": "..." } o un ProblemDetails con "detail".
            using var doc = JsonDocument.Parse(cuerpo);
            if (doc.RootElement.TryGetProperty("error", out var e))
            {
                return e.GetString() ?? string.Empty;
            }

            if (doc.RootElement.TryGetProperty("detail", out var d))
            {
                return d.GetString() ?? string.Empty;
            }

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Dispose() => _http.Dispose();
}
