using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace DocGenApp.Services;

/// <summary>
/// Guarda la password maestra cifrada con DPAPI (ámbito usuario actual).
/// El archivo resultante solo puede descifrarlo el mismo usuario de Windows;
/// nunca se escribe en texto plano.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DocGenApp.MasterPassword.v1");

    private readonly string _ruta;

    public CredentialStore()
    {
        var carpeta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DocGenApp");
        Directory.CreateDirectory(carpeta);
        _ruta = Path.Combine(carpeta, "credential.bin");
    }

    public bool TienePassword => File.Exists(_ruta);

    public string? Cargar()
    {
        if (!File.Exists(_ruta))
        {
            return null;
        }

        try
        {
            var cifrado = File.ReadAllBytes(_ruta);
            var claro = ProtectedData.Unprotect(cifrado, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(claro);
        }
        catch (CryptographicException)
        {
            // El archivo viene de otro usuario/máquina o está corrupto: se descarta
            // y la app vuelve a pedir la password.
            Borrar();
            return null;
        }
    }

    public void Guardar(string password)
    {
        var cifrado = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_ruta, cifrado);
    }

    public void Borrar()
    {
        if (File.Exists(_ruta))
        {
            File.Delete(_ruta);
        }
    }
}
