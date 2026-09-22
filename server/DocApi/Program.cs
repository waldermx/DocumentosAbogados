using DocApi.Auth;
using DocApi.Endpoints;
using DocApi.Models;
using DocApi.Services;

using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Los enums viajan como texto ("Actualizado"), no como el número de su posición.
// Con números, añadir un valor al enum cambiaría el significado de los ya publicados,
// y el cliente —que declara Resultado como string— no podía deserializar la respuesta.
builder.Services.ConfigureHttpJsonOptions(opciones =>
    opciones.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// --- Configuración (appsettings + env vars con doble guion bajo: Auth__MasterPassword, etc.) ---
builder.Services.Configure<GoogleSheetsOptions>(builder.Configuration.GetSection(GoogleSheetsOptions.Section));
builder.Services.Configure<ParsingOptions>(builder.Configuration.GetSection(ParsingOptions.Section));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.Section));
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.Section));

// --- Servicios ---
builder.Services.AddSingleton<RowParser>();
builder.Services.AddSingleton<SheetCache>();
builder.Services.AddSingleton<ImpresosStore>();

// Sin credenciales configuradas la app igual arranca: /sync responde con un error explicativo
// en vez de impedir levantar el servidor.
var sheetsOptions = builder.Configuration
    .GetSection(GoogleSheetsOptions.Section)
    .Get<GoogleSheetsOptions>() ?? new GoogleSheetsOptions();

if (sheetsOptions.EstaConfigurado)
{
    builder.Services.AddSingleton<ISheetSource, GoogleSheetSource>();
}
else
{
    builder.Services.AddSingleton<ISheetSource, SheetSourceNoConfigurado>();
}

builder.Services.AddSingleton<SyncCoordinator>();
builder.Services.AddHostedService<SyncBackgroundService>();

var app = builder.Build();

if (!sheetsOptions.EstaConfigurado)
{
    app.Logger.LogWarning("Arrancando sin Google Sheets. {Mensaje}", SheetSourceNoConfigurado.Mensaje);
}

app.UseMiddleware<MasterPasswordMiddleware>();

app.MapRegistrosEndpoints();
app.MapSyncEndpoints();

app.Run();

/// <summary>Expuesto para los tests de integración con WebApplicationFactory.</summary>
public partial class Program;
