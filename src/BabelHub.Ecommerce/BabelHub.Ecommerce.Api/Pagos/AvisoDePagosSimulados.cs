using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Pagos;

/// <summary>
/// Si la pasarela que cobra es la simulada (PAG-33). Lo compone el anfitrión con las mismas opciones con las que compuso la pasarela, de modo que el aviso nunca se desincroniza de lo que de verdad ocurre: si la pasarela es el simulador,
/// la pantalla lo dice; si no, no hay nada que decir. Es solo presentación: no decide nada de Pagos.
/// </summary>
public sealed record ModoDePagosDeLaWeb(bool Simulado)
{
    /// <summary>El texto del aviso (PAG-33): se repite igual en el pie de toda página y en el pago y el checkout.</summary>
    public const string Texto = "Pagos simulados: no se realiza ningún cobro";

    /// <summary>La explicación que acompaña al aviso en el checkout y en el pago.</summary>
    public const string Explicacion = "Esta tienda está en revisión: puedes recorrer la compra completa, pero ningún medio de pago es real y a nadie se le cobra.";
}

/// <summary>Lo que dibuja <see cref="BhAvisoDePagosSimuladosViewComponent"/>.</summary>
public sealed record AvisoDePagosSimuladosVista(bool EnContexto);

/// <summary>
/// <c>&lt;vc:bh-aviso-de-pagos-simulados /&gt;</c>: el aviso «Pagos simulados: no se realiza ningún cobro» (PAG-33). No dibuja nada si la pasarela no es la simulada. El aviso es un bloque con icono y texto (el estado no depende solo del
/// color) y sin región viva: es permanente, no un cambio que anunciar. Con <c>enContexto</c> (checkout y pago) añade la explicación. Sin el servicio compuesto (una prueba de la interfaz sola) no dibuja nada.
/// </summary>
public sealed class BhAvisoDePagosSimuladosViewComponent(ModoDePagosDeLaWeb? modo = null) : ViewComponent
{
    public IViewComponentResult Invoke(bool enContexto = false) =>
        modo is { Simulado: true } ? View(new AvisoDePagosSimuladosVista(enContexto)) : Content(string.Empty);
}
