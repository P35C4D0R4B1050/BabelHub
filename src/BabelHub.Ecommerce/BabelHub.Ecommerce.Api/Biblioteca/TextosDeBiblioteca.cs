using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Api.Web.Formato;

namespace BabelHub.Ecommerce.Api.Biblioteca;

/// <summary>Un libro de la biblioteca listo para pintar: el estado con texto e icono (nunca solo con color), el formato del archivo, la fecha de compra y las descargas «usadas de 5». Sin la clave del archivo ni ninguna URL.</summary>
public sealed record LibroDeBibliotecaVista(
    Guid AccesoId,
    string Titulo,
    string Formato,
    string CompradoTexto,
    string EstadoTexto,
    string EstadoIcono,
    string? EstadoMensaje,
    bool PuedeDescargar,
    bool PuedePedirAyuda,
    int Usadas,
    int Maximas,
    int Restantes,
    bool TieneAyudaAbierta)
{
    public string IdCorto => AccesoId.ToString("N")[..8];

    public bool Agotado => Restantes <= 0;

    public string DescargasTexto => $"{Usadas} de {Maximas} descargas usadas";
}

/// <summary>Los textos en español de lo que ofrecen las pantallas de Biblioteca: estados, formatos y los motivos de lista cerrada (D06). Presentación pura: la regla es del módulo.</summary>
public static class TextosDeBiblioteca
{
    public static readonly IReadOnlyList<(string Codigo, string Texto)> MotivosDeAyuda =
    [
        ("archivo-no-abre", "El archivo no abre"),
        ("archivo-no-disponible", "El archivo no está disponible"),
        ("descargas-agotadas", "Se agotaron mis descargas"),
        ("otro", "Otro problema"),
    ];

    public static readonly IReadOnlyList<(string Codigo, string Texto)> MotivosDeRestitucion =
    [
        ("descarga-fallida", "La descarga falló o se interrumpió"),
        ("archivo-defectuoso", "El archivo era defectuoso"),
        ("error-del-sistema", "Error del sistema"),
        ("otro", "Otro motivo"),
    ];

    public static string Motivo(string codigo) =>
        MotivosDeAyuda.Concat(MotivosDeRestitucion).FirstOrDefault(m => m.Codigo == codigo).Texto ?? "Otro";

    public static string Formato(FormatoDeArchivoDigital? formato) => formato switch
    {
        FormatoDeArchivoDigital.Epub => "EPUB",
        FormatoDeArchivoDigital.Pdf => "PDF",
        _ => "Formato digital",
    };

    public static LibroDeBibliotecaVista AVista(AccesoDeBiblioteca a)
    {
        var (texto, icono, mensaje) = a.Estado switch
        {
            EstadoDelAccesoDigital.Vigente => ("Disponible", "exito", (string?)null),
            EstadoDelAccesoDigital.Suspendido => ("En revisión", "reloj", "La descarga está pausada mientras revisamos un reporte de este archivo."),
            _ => ("Revocado", "candado", "Ya no puedes descargarlo: el acceso se revocó por un reembolso o una anulación."),
        };
        var vigente = a.Estado == EstadoDelAccesoDigital.Vigente;
        return new LibroDeBibliotecaVista(
            a.AccesoId,
            a.Titulo,
            Formato(a.Formato),
            FormatoPeruano.FechaYHora(a.FechaDeCompra),
            texto,
            icono,
            mensaje,
            vigente && a.DescargasRestantes > 0,
            a.Estado != EstadoDelAccesoDigital.Revocado,
            a.DescargasUsadas,
            a.DescargasMaximas,
            a.DescargasRestantes,
            a.TieneAyudaAbierta);
    }
}
