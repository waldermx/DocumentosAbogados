using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DocGenApp.Models;
using DocGenApp.Services;
using DocGenApp.ViewModels;
using DocGenApp.Views;

namespace DocGenApp;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = ConfigLoader.Cargar();

            var api = new ApiClient(settings);
            var viewModel = new MainViewModel(
                api,
                new LocalCacheService(),
                new CredentialStore(),
                new WordDocumentGenerator(),
                settings);

            var ventana = new MainWindow { DataContext = viewModel };

            // La vista provee el diálogo de guardado; el ViewModel no conoce la UI.
            viewModel.PedirRutaDeGuardado = ventana.PedirRutaDeGuardadoAsync;

            desktop.MainWindow = ventana;
            desktop.Exit += (_, _) => api.Dispose();

            // Se dispara tras mostrar la ventana para que la UI no arranque congelada.
            ventana.Opened += async (_, _) => await viewModel.InicializarAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
