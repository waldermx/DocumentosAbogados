using DocGenApp.Models;
using Microsoft.Extensions.Configuration;

namespace DocGenApp.Services;

/// <summary>
/// Lee appsettings.json del directorio de la app. Todo es opcional: si falta el archivo
/// se usan los valores por defecto de <see cref="AppSettings"/>.
/// </summary>
public static class ConfigLoader
{
    public static AppSettings Cargar()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables("DOCGEN_")
            .Build();

        return config.Get<AppSettings>() ?? new AppSettings();
    }
}
