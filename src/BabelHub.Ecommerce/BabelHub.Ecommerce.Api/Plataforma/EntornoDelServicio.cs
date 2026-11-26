namespace BabelHub.Ecommerce.Api.Plataforma;

/// <summary>
/// Los entornos con los que corre el E-commerce y qué endurece cada uno (PAG-34, D14 de <c>ecommerce-pagos</c>). <c>Production</c> y <c>Staging</c> son entornos <b>de tipo producción</b>: TLS entre servicios, Redis con TLS,
/// SMTP y almacenes reales, proxies conocidos, hosts permitidos y sin versiones provisionales ni dobles. La <b>única</b> diferencia es el cobro: <c>Staging</c> (el entorno de revisión de la tienda por la pasarela de pago, antes de
/// tener sus llaves) admite que no haya pasarela (el pago en línea se anuncia como disponible pronto, DD-110) o la simulada, que no cobra nada y se anuncia en pantalla; <c>Production</c> exige la pasarela real y falla cerrado sin ella. <c>Development</c> y <c>Test</c> relajan el endurecimiento.
/// </summary>
public static class EntornoDelServicio
{
    public const string Staging = "Staging";

    /// <summary>
    /// <see langword="true"/> en <c>Production</c> y en <c>Staging</c>: el entorno se endurece como producción. Un entorno de revisión no debe aflojar nada más que el cobro: sin esto, <c>Staging</c> (que no es <c>Production</c>
    /// para el marco) aceptaría HTTP entre servicios, sin Redis ni SMTP reales y con versiones provisionales de los textos legales.
    /// </summary>
    public static bool EsDeTipoProduccion(this IHostEnvironment entorno)
    {
        ArgumentNullException.ThrowIfNull(entorno);
        return entorno.IsProduction() || entorno.IsEnvironment(Staging);
    }
}
