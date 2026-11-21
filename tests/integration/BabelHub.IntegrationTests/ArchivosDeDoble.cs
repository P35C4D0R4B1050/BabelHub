namespace BabelHub.IntegrationTests;

/// <summary>
/// Escribe los dobles de comandos (scripts de shell que sustituyen a <c>docker</c>, <c>stat</c>, <c>oci</c>…) como ejecutables: el script bajo prueba los invoca directamente
/// y en Linux un archivo recién creado no tiene el bit de ejecución (en Windows no existe).
/// </summary>
internal static class ArchivosDeDoble
{
    public static void Escribir(string ruta, string contenido)
    {
        File.WriteAllText(ruta, contenido);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(ruta, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }
}
