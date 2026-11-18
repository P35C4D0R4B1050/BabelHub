using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Ajustes;

namespace BabelHub.Inventario.Application.Ajustes.Interno;

/// <summary>Las claves simbólicas de los fallos del módulo; la Api las traduce a los códigos <c>INV-…</c> de <c>Shared</c> (D11).</summary>
internal static class ClavesDeAjustes
{
    public const string ValidacionFallida = "validacion-fallida";

    public const string BloqueoOcupado = "bloqueo-ocupado";

    public const string FuenteNoEncontrada = "fuente-no-encontrada";

    public const string EdicionNoEncontrada = "edicion-no-encontrada";

    public const string EdicionNoImpresa = "edicion-no-impresa";

    /// <summary>La cantidad de una merma o un daño no es positiva (AJU-03, AJU-12).</summary>
    public const string CantidadNoPositiva = "cantidad-no-positiva";

    /// <summary>Un ingreso o un reingreso con una cantidad fuera del rango (1 a 5 000) o que pasa el tope técnico (AJU-19, D08).</summary>
    public const string CantidadFueraDeRango = "cantidad-fuera-de-rango";

    public const string ReferenciaInvalida = "referencia-invalida";

    /// <summary>Un conteo negativo o por encima del tope técnico.</summary>
    public const string ConteoFueraDeRango = "conteo-fuera-de-rango";

    /// <summary>Merma o daño por encima del vendible (AJU-04).</summary>
    public const string StockInsuficiente = "stock-insuficiente";

    public const string ConteoMenorQueReservas = "conteo-menor-que-reservas";

    public const string SinCambios = "sin-cambios";

    public const string FuenteNoEsDeRecepcion = "fuente-no-es-de-recepcion";

    public const string IdempotenciaEnConflicto = "idempotencia-en-conflicto";

    public const string IdempotenciaEnCurso = "idempotencia-en-curso";

    public const string LimitesNoDisponibles = "limites-no-disponibles";
}

/// <summary>Los fallos esperados del módulo como <see cref="Resultado{T}"/> (la Api los traduce a los códigos <c>INV-…</c>).</summary>
internal static class FallosDeAjustes
{
    public static Resultado<T> Fallo<T>(ErrorDeAplicacion error) => Resultado<T>.Fallo(error);

    public static Resultado<T> Validacion<T>(ResultadoDeValidacion validacion) =>
        Resultado<T>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDeAjustes.ValidacionFallida, validacion.Errores));

    public static Resultado<T> De<T>(CategoriaDeError categoria, string clave) => Resultado<T>.Fallo(new ErrorDeAplicacion(categoria, clave));

    public static Resultado<T> BloqueoOcupado<T>() => De<T>(CategoriaDeError.NoDisponible, ClavesDeAjustes.BloqueoOcupado);
}

/// <summary>La operación idempotente guardada: la huella de la carga y los identificadores pregenerados del resultado (nunca la nota ni la referencia).</summary>
internal sealed record OperacionIdempotenteGuardada(string HuellaDeLaCarga, string Resultado);

/// <summary>Una violación de la unicidad de <c>(devolución, edición)</c> o de la clave de idempotencia al insertar: la última defensa; nunca un error del servidor.</summary>
public sealed class AjusteDuplicadoException : Exception
{
    public AjusteDuplicadoException()
        : base("Ya existe un ajuste con esa devolución y edición.")
    {
    }

    public AjusteDuplicadoException(Exception interna)
        : base("Ya existe un ajuste con esa devolución y edición.", interna)
    {
    }

    public AjusteDuplicadoException(string message, Exception interna)
        : base(message, interna)
    {
    }
}

/// <summary>Una página del historial leída del repositorio.</summary>
internal sealed record PaginaDeAjustesGuardados(IReadOnlyList<Ajuste> Elementos, int Total);

/// <summary>Los ajustes: solo inserción y lectura (la depuración de los 2 años la hace una función propia de la base, no este repositorio).</summary>
internal interface IRepositorioDeAjustes
{
    Task Insertar(Ajuste ajuste, CancellationToken cancelacion = default);

    Task<Ajuste?> PorId(Guid ajusteId, CancellationToken cancelacion = default);

    /// <summary>El ajuste de una devolución para una edición (el reingreso es idempotente por esa pareja, AJU-16), leído en la transacción del reingreso.</summary>
    Task<Ajuste?> PorDevolucionYEdicion(Guid devolucionId, Guid edicionId, CancellationToken cancelacion = default);

    /// <summary>El historial de una fuente: fecha descendente e identificador (AJU-17), con página de 1 a 100.</summary>
    Task<PaginaDeAjustesGuardados> Historial(Guid fuenteId, int pagina, int tamano, CancellationToken cancelacion = default);

    /// <summary>
    /// Las unidades reingresadas por devolución de esos pedidos, por pedido y edición, sumadas en <b>todas</b> las sucursales: las ventas de una sucursal restan lo devuelto en cualquiera (FUE-21). Lee con el
    /// alcance de la red (<c>ConAlcanceDeRed</c>, D09).
    /// </summary>
    Task<IReadOnlyList<UnidadesReingresadas>> ReingresadasPorPedidos(IReadOnlyCollection<Guid> pedidoIds, CancellationToken cancelacion = default);
}

/// <summary>Los ingresos de mercadería.</summary>
internal interface IRepositorioDeIngresos
{
    Task Insertar(IngresoDeMercaderia ingreso, CancellationToken cancelacion = default);

    Task<IngresoDeMercaderia?> PorId(Guid ingresoId, CancellationToken cancelacion = default);

    /// <summary>Los últimos ingresos (más reciente primero), de una edición si se indica.</summary>
    Task<IReadOnlyList<IngresoDeMercaderia>> Ultimos(Guid? edicionId, int limite, CancellationToken cancelacion = default);
}

/// <summary>
/// Las operaciones idempotentes del ajuste manual y del ingreso (AJU-22): clave por <c>(usuario, operación, clave)</c>, que se conserva 30 días. Se inserta <b>primero</b> con la clave primaria en la misma transacción del
/// efecto, de modo que una repetición concurrente espera y relee.
/// </summary>
internal interface IRepositorioDeOperacionesIdempotentes
{
    /// <summary>
    /// Intenta guardar la operación (<c>INSERT … ON CONFLICT DO NOTHING</c>): devuelve <see langword="null"/> si era nueva y quedó guardada, o la que ya existía (la repetición espera a que la primera confirme).
    /// </summary>
    Task<OperacionIdempotenteGuardada?> Registrar(Guid usuarioId, string operacion, string clave, string huellaDeLaCarga, string resultado, CancellationToken cancelacion = default);
}
