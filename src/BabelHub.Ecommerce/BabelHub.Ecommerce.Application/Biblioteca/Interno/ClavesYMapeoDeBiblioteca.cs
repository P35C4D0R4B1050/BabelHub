using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;

namespace BabelHub.Ecommerce.Application.Biblioteca.Interno;

/// <summary>Las claves simbólicas de los fallos del módulo; la Api las traduce a los códigos <c>ECO-…</c> y <c>ECO-BIB-…</c> (plan §4).</summary>
internal static class ClavesDeBiblioteca
{
    public const string ValidacionFallida = "validacion-fallida";

    public const string ServicioNoDisponible = "servicio-no-disponible";

    public const string NoAutenticado = "no-autenticado";

    public const string Prohibido = "prohibido";

    /// <summary><c>ECO-NO-ENCONTRADO</c>: el acceso o la cuenta no existen o son de otra persona (la misma respuesta, BIB-07).</summary>
    public const string NoEncontrado = "no-encontrado";

    public const string TextosPendientes = "textos-pendientes";

    /// <summary><c>ECO-BIB-ACCESO-NO-DISPONIBLE</c> (409): el acceso está revocado o suspendido.</summary>
    public const string AccesoNoDisponible = "acceso-no-disponible";

    /// <summary><c>ECO-BIB-LIMITE-DE-DESCARGAS</c> (409): el acceso agotó sus cinco descargas; ofrece pedir ayuda (BIB-06).</summary>
    public const string LimiteDeDescargas = "limite-de-descargas";

    /// <summary><c>ECO-BIB-ARCHIVO-NO-DISPONIBLE</c> (503): el archivo falta en el almacén; no se consumió nada y se ofrece reportarlo (BIB-13).</summary>
    public const string ArchivoNoDisponible = "archivo-no-disponible";

    /// <summary><c>ECO-CONFLICTO</c> (409): la reserva de la clave está en curso en el almacén de contadores.</summary>
    public const string Conflicto = "conflicto";

    /// <summary><c>ECO-LIMITE-EXCEDIDO</c> (429), con el tiempo de espera, el motivo y el instante de nueva disponibilidad (SEG-07).</summary>
    public const string LimiteExcedido = "limite-excedido";

    public const string EsperaEnSegundos = "espera-en-segundos";

    public const string Motivo = "motivo";

    public const string DisponibleDesde = "disponibleDesde";

    /// <summary>El motivo cerrado de la alerta técnica de un archivo que falta en el almacén (copia del catálogo de Auditoría: un módulo no referencia el interior de otro).</summary>
    public const string AlertaArchivoFaltante = "biblioteca-archivo-faltante";
}

internal static class FallosDeBiblioteca
{
    public static Resultado<T> De<T>(CategoriaDeError categoria, string clave, IReadOnlyList<ErrorDeCampo>? errores = null) =>
        Resultado<T>.Fallo(new ErrorDeAplicacion(categoria, clave, errores));

    public static Resultado<T> Fallo<T>(ErrorDeAplicacion error) => Resultado<T>.Fallo(error);

    public static Resultado<T> NoDisponible<T>() => De<T>(CategoriaDeError.NoDisponible, ClavesDeBiblioteca.ServicioNoDisponible);

    public static Resultado<T> NoEncontrado<T>() => De<T>(CategoriaDeError.NoEncontrado, ClavesDeBiblioteca.NoEncontrado);

    public static Resultado<T> Validacion<T>() => De<T>(CategoriaDeError.Validacion, ClavesDeBiblioteca.ValidacionFallida);
}

/// <summary>El mapeo entre los tipos del contrato (Aplicación) y los del Dominio; no sale del módulo.</summary>
internal static class MapeoDeBiblioteca
{
    public static CausaDeRevocacion Causa(MotivoDeRevocacionDeAcceso motivo) => motivo switch
    {
        MotivoDeRevocacionDeAcceso.PedidoAnulado => CausaDeRevocacion.AnulacionDePedido,
        MotivoDeRevocacionDeAcceso.Reembolso => CausaDeRevocacion.Reembolso,
        MotivoDeRevocacionDeAcceso.ReporteAprobado => CausaDeRevocacion.ReporteAprobado,
        _ => throw new ArgumentOutOfRangeException(nameof(motivo)),
    };

    public static EstadoDelAccesoDigital Estado(EstadoDeAcceso estado) => estado switch
    {
        EstadoDeAcceso.Vigente => EstadoDelAccesoDigital.Vigente,
        EstadoDeAcceso.Suspendido => EstadoDelAccesoDigital.Suspendido,
        EstadoDeAcceso.Revocado => EstadoDelAccesoDigital.Revocado,
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };

    public static EstadoDelAccesoParaDevoluciones EstadoParaDevoluciones(EstadoDeAcceso estado) => estado switch
    {
        EstadoDeAcceso.Vigente => EstadoDelAccesoParaDevoluciones.Vigente,
        EstadoDeAcceso.Suspendido => EstadoDelAccesoParaDevoluciones.Suspendido,
        EstadoDeAcceso.Revocado => EstadoDelAccesoParaDevoluciones.Revocado,
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };

    public static AyudaDeBiblioteca Ayuda(SolicitudDeAyuda a) =>
        new(a.Id, a.AccesoId, a.Motivo.Codigo(), a.Estado == EstadoDeAyuda.Abierta ? EstadoDeLaAyudaDeBiblioteca.Abierta : EstadoDeLaAyudaDeBiblioteca.Cerrada, a.CreadaEn, a.CerradaEn);
}
