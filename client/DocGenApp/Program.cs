using Avalonia;
using DocGenApp.Services;

namespace DocGenApp;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Utilidad: regenera la plantilla de ejemplo con los marcadores soportados.
        //   DocGenApp.exe --crear-plantilla [ruta.docx]
        if (args.Length > 0 && args[0] is "--crear-plantilla")
        {
            var ruta = args.Length > 1
                ? args[1]
                : Path.Combine(AppContext.BaseDirectory, "Templates", "plantilla.docx");

            WordDocumentGenerator.CrearPlantillaDeEjemplo(ruta);
            Console.WriteLine($"Plantilla de ejemplo creada en: {Path.GetFullPath(ruta)}");
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Lo usa el previewer de Avalonia además del arranque normal.
    // El backend se declara explícitamente (en vez de UsePlatformDetect) porque
    // solo se referencia Avalonia.Win32: la app es exclusivamente para Windows.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseWin32()
            .UseSkia()
            .WithInterFont()
            .LogToTrace();
}
