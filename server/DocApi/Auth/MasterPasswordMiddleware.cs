using System.Security.Cryptography;
using System.Text;
using DocApi.Models;
using Microsoft.Extensions.Options;

namespace DocApi.Auth;

/// <summary>
/// Valida <c>Authorization: Bearer &lt;password maestra&gt;</c> contra <c>Auth:MasterPassword</c>.
/// Exime las rutas públicas (health check para Traefik/Dokploy).
/// </summary>
public sealed class MasterPasswordMiddleware
{
    private static readonly string[] RutasPublicas = ["/health"];

    private readonly RequestDelegate _next;
    private readonly byte[]? _esperado;
    private readonly ILogger<MasterPasswordMiddleware> _logger;

    public MasterPasswordMiddleware(
        RequestDelegate next,
        IOptions<AuthOptions> options,
        ILogger<MasterPasswordMiddleware> logger)
    {
        _next = next;
        _logger = logger;

        var password = options.Value.MasterPassword;
        _esperado = string.IsNullOrWhiteSpace(password) ? null : Encoding.UTF8.GetBytes(password);

        if (_esperado is null)
        {
            _logger.LogWarning(
                "Auth:MasterPassword no está configurada: la API rechazará todas las peticiones protegidas. " +
                "Define la env var Auth__MasterPassword.");
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (RutasPublicas.Any(r => path.StartsWithSegments(r, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        if (_esperado is null)
        {
            await Rechazar(context, "El servidor no tiene configurada una password maestra.");
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await Rechazar(context, "Falta el header Authorization: Bearer <password>.");
            return;
        }

        var recibido = Encoding.UTF8.GetBytes(header["Bearer ".Length..].Trim());

        // Comparación de tiempo constante: no filtra información por cuánto tarda en fallar.
        if (!CryptographicOperations.FixedTimeEquals(recibido, _esperado))
        {
            _logger.LogWarning("Intento de acceso con password maestra incorrecta a {Path}.", path);
            await Rechazar(context, "Password maestra incorrecta.");
            return;
        }

        await _next(context);
    }

    private static async Task Rechazar(HttpContext context, string mensaje)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
        await context.Response.WriteAsJsonAsync(new { error = mensaje });
    }
}
