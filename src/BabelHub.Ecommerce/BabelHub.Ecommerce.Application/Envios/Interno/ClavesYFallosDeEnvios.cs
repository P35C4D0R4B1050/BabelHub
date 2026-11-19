using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Envios.Interno;

/// <summary>Las claves simbólicas de los fallos del módulo; la Api las traduce a los códigos <c>ECO-…</c> y <c>ECO-ENV-…</c> (plan §4).</summary>
internal static class ClavesDeEnvios
{
    public const string ValidacionFallida = "validacion-fallida";

    public const string ServicioNoDisponible = "servicio-no-disponible";

    public const string PedidoNoEncontrado = "pedido-no-encontrado";

    /// <summary>El envío no existe, o es de un pedido ajeno: la misma respuesta (PED-41).</summary>
    public const string EnvioNoEncontrado = "envio-no-encontrado";

    public const string Prohibido = "prohibido";

    public const string NoAutenticado = "no-autenticado";

    /// <summary><c>ECO-ENV-NO-ES-A-DOMICILIO</c>: el pedido es de recojo y no crea envío.</summary>
    public const string PedidoNoEsADomicilio = "pedido-no-es-a-domicilio";

    public const string LineaNoEsDelPedido = "linea-no-es-del-pedido";

    public const string LineaNoEstaLista = "linea-no-esta-lista";

    public const string LineaDeOtraFuente = "linea-de-otra-fuente";

    /// <summary><c>ECO-ENV-LINEA-DIGITAL</c>: todas las líneas pedidas son digitales y no se despachan (ENV-09).</summary>
    public const string LineaDigital = "linea-digital";

    /// <summary><c>ECO-ENV-LINEAS-EN-OTRO-ENVIO</c> (409): las líneas pertenecen a otro envío o se solapan parcialmente con uno.</summary>
    public const string LineasEnOtroEnvio = "lineas-en-otro-envio";
}

internal static class FallosDeEnvios
{
    public static Resultado<T> De<T>(CategoriaDeError categoria, string clave, IReadOnlyList<ErrorDeCampo>? errores = null) =>
        Resultado<T>.Fallo(new ErrorDeAplicacion(categoria, clave, errores));

    public static Resultado<T> Fallo<T>(ErrorDeAplicacion error) => Resultado<T>.Fallo(error);

    public static Resultado<T> NoDisponible<T>() => De<T>(CategoriaDeError.NoDisponible, ClavesDeEnvios.ServicioNoDisponible);
}
