using System.Globalization;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Catalogo;
using EstadoDeDisponibilidad = BabelHub.Ecommerce.Api.Web.Estados.EstadoDeDisponibilidad;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>Una línea de la revisión lista para pintar: textos con formato peruano y su disponibilidad (texto, icono y color) con el plazo informado.</summary>
public sealed record LineaDeRevisionPintada(
    string Titulo,
    string FormatoTexto,
    int Cantidad,
    string PrecioTexto,
    string ImporteTexto,
    DisponibilidadVista Disponibilidad,
    string? Plazo,
    bool Retirada);

/// <summary>Un paso de la barra de progreso: nunca depende solo del color (el actual lleva <c>aria-current</c> y el hecho su texto).</summary>
public sealed record PasoPintado(string Nombre, bool Actual, bool Hecho);

/// <summary>Un texto legal que la persona debe aceptar: su nombre en el formulario, su título, la ruta donde se lee y la versión vigente que vio (se envía para compararla).</summary>
public sealed record TextoParaAceptar(string Campo, string Titulo, string Ruta, string Version, bool Aceptado);

/// <summary>
/// La pantalla del checkout (revisión, entrega, datos y aceptaciones): lo que la persona ve de su compra en curso. Los precios, el envío y el total salen del servidor; la pantalla solo los pinta y devuelve el total visto para compararlo. Los errores por campo y lo escrito
/// (<see cref="Valores"/>) solo viven en la respuesta de un <c>POST</c> fallido: nunca se guardan en un borrador ni en la caché.
/// </summary>
public sealed class CheckoutVista : ModeloBaseDePagina
{
    public VistaDelCheckout Checkout { get; set; } = null!;

    public IReadOnlyList<PasoPintado> Pasos { get; set; } = [];

    public IReadOnlyList<LineaDeRevisionPintada> Lineas { get; set; } = [];

    public string SubtotalTexto { get; set; } = string.Empty;

    public string EnvioTexto { get; set; } = string.Empty;

    public string TotalTexto { get; set; } = string.Empty;

    /// <summary>El total que la persona ve, con punto decimal: lo que viaja oculto para compararlo al pagar (D17).</summary>
    public string TotalVisto { get; set; } = "0.00";

    public bool RequiereEntrega { get; set; }

    public bool HayImpresos { get; set; }

    public bool HayDigitales { get; set; }

    /// <summary>Algún impreso no se pudo validar contra el Inventario: no se avanza ni se cobra (CAR-06).</summary>
    public bool StockNoValidable { get; set; }

    /// <summary>Alguna línea impide pagar (retirada o agotada): se dice y se ofrece volver al carrito.</summary>
    public bool HayProblema { get; set; }

    /// <summary>Las sucursales de recojo que se pueden elegir (código y nombre), de la lista de sucursales.</summary>
    public IReadOnlyList<SucursalElegible> SucursalesDeRecojo { get; set; } = [];

    public IReadOnlyList<TextoParaAceptar> Textos { get; set; } = [];

    /// <summary>El mensaje de un bloqueo (por ejemplo «el total cambió») y su título, arriba de la pantalla y con foco.</summary>
    public string? AlertaTitulo { get; set; }

    public string? AlertaMensaje { get; set; }

    /// <summary>Errores por campo (clave del formulario → texto de la persona), solo tras un <c>POST</c> fallido.</summary>
    public IReadOnlyDictionary<string, string> Errores { get; set; } = new Dictionary<string, string>();

    /// <summary>Lo que la persona escribió en el <c>POST</c> fallido (el contacto no se guarda en ningún borrador).</summary>
    public IReadOnlyDictionary<string, string> Valores { get; set; } = new Dictionary<string, string>();

    public string Valor(string campo) => Valores.TryGetValue(campo, out var v) ? v : string.Empty;

    public string? Error(string campo) => Errores.TryGetValue(campo, out var e) ? e : null;
}

/// <summary>Arma <see cref="CheckoutVista"/> desde <see cref="VistaDelCheckout"/>: solo presentación (textos, formato peruano, pasos); ninguna regla.</summary>
public static class ConstructorDeLaVistaDelCheckout
{
    public static CheckoutVista Armar(CheckoutVista modelo, VistaDelCheckout vista)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        ArgumentNullException.ThrowIfNull(vista);
        modelo.Checkout = vista;
        modelo.HayImpresos = vista.Lineas.Any(l => l.Formato == FormatoDeEdicion.Impresa);
        modelo.HayDigitales = vista.Lineas.Any(l => l.Formato == FormatoDeEdicion.Digital);
        modelo.RequiereEntrega = modelo.HayImpresos;
        modelo.Lineas = [.. vista.Lineas.Select(ALaLinea)];
        modelo.StockNoValidable = vista.Lineas.Any(l => l.Disponibilidad == DisponibilidadDeLineaDeCheckout.NoSePuedeValidar);
        modelo.HayProblema = vista.Lineas.Any(l => l.Retirada || l.Disponibilidad == DisponibilidadDeLineaDeCheckout.Agotada);
        modelo.SubtotalTexto = FormatoPeruano.Soles(vista.Subtotal);
        modelo.TotalTexto = FormatoPeruano.Soles(vista.Total);
        modelo.TotalVisto = vista.Total.ToString("0.00", CultureInfo.InvariantCulture);
        modelo.EnvioTexto = !modelo.RequiereEntrega
            ? "No aplica (solo digitales)"
            : vista.Envio is not { } envio
                ? "Se calcula al elegir la entrega"
                : envio == 0m ? "Gratis (recojo)" : FormatoPeruano.Soles(envio);
        modelo.Pasos = Pasos(vista, modelo.RequiereEntrega);
        return modelo;
    }

    private static IReadOnlyList<PasoPintado> Pasos(VistaDelCheckout vista, bool conEntrega)
    {
        string[] nombres = conEntrega ? ["Revisar", "Entrega", "Tus datos", "Pago", "Listo"] : ["Revisar", "Tus datos", "Pago", "Listo"];
        var actual = Math.Clamp(vista.Paso, 1, nombres.Length) - 1;
        return [.. nombres.Select((n, i) => new PasoPintado(n, i == actual, i < actual))];
    }

    private static LineaDeRevisionPintada ALaLinea(LineaDeRevision l)
    {
        var digital = l.Formato == FormatoDeEdicion.Digital;
        var (disponibilidad, plazo) = l.Disponibilidad switch
        {
            DisponibilidadDeLineaDeCheckout.EnSuSucursal => (new DisponibilidadVista(EstadoDeDisponibilidad.EnTuSucursal, "En tu sucursal", "exito", "badge-success"), null),
            DisponibilidadDeLineaDeCheckout.PorTraslado => (
                new DisponibilidadVista(EstadoDeDisponibilidad.EnOtrasSucursales, "Se trae de otra sucursal", "sucursal", "badge-info"),
                l.PlazoAConfirmar ? "Plazo a confirmar" : l.PlazoInformadoDias is { } dias ? $"Plazo estimado: {dias} {(dias == 1 ? "día" : "días")}" : null),
            DisponibilidadDeLineaDeCheckout.Agotada => (DisponibilidadVista.Agotado(), null),
            DisponibilidadDeLineaDeCheckout.DisponibleAlInstante => (DisponibilidadVista.DisponibleAlInstante(), null),
            DisponibilidadDeLineaDeCheckout.NoSePuedeValidar => (new DisponibilidadVista(EstadoDeDisponibilidad.NoSePuedeConfirmar, "No se puede validar el stock ahora", "aviso", "badge-warning"), null),
            _ => (new DisponibilidadVista(EstadoDeDisponibilidad.EnLaRed, "Hay stock; la fuente se confirma al elegir la entrega", "exito", "badge-success"), null),
        };
        return new LineaDeRevisionPintada(l.Titulo, digital ? "Libro digital" : "Libro impreso", l.Cantidad, FormatoPeruano.Soles(l.PrecioUnitario), FormatoPeruano.Soles(l.Importe), disponibilidad, plazo, l.Retirada);
    }
}
