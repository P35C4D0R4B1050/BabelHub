using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Fuentes;

namespace BabelHub.Inventario.Application.Fuentes.Interno;

/// <summary>Las claves simbólicas de los fallos del módulo; la Api las traduce a los códigos <c>INV-…</c> de <c>Shared</c> (D11).</summary>
internal static class ClavesDeFuentes
{
    public const string ValidacionFallida = "validacion-fallida";

    public const string FuenteNoEncontrada = "fuente-no-encontrada";

    public const string FuenteDuplicada = "fuente-duplicada";

    public const string RutaInvalida = "ruta-invalida";

    public const string EdicionNoImpresa = "edicion-no-impresa";

    public const string FormatoEnConflicto = "formato-en-conflicto";

    public const string ContactoNoDefinido = "contacto-no-definido";

    /// <summary>No se obtuvo un bloqueo asesor dentro de la espera máxima: la operación se puede repetir.</summary>
    public const string BloqueoOcupado = "bloqueo-ocupado";
}

internal static class FallosDeFuentes
{
    public static Resultado<T> Fallo<T>(ErrorDeAplicacion error) => Resultado<T>.Fallo(error);

    public static Resultado<T> Validacion<T>(ResultadoDeValidacion validacion) =>
        Resultado<T>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDeFuentes.ValidacionFallida, validacion.Errores));

    public static Resultado<T> De<T>(CategoriaDeError categoria, string clave) => Resultado<T>.Fallo(new ErrorDeAplicacion(categoria, clave));

    public static Resultado<T> NoEncontrada<T>() => De<T>(CategoriaDeError.NoEncontrado, ClavesDeFuentes.FuenteNoEncontrada);

    public static Resultado<T> BloqueoOcupado<T>() => De<T>(CategoriaDeError.NoDisponible, ClavesDeFuentes.BloqueoOcupado);

    /// <summary>El identificador de la fuente tal como lo guarda la auditoría (<c>objetoId</c> y sucursal de origen).</summary>
    public static string Identificador(Guid id) => id.ToString("D");
}

/// <summary>
/// Los totales de stock de una fuente (FUE-11): físico, reservado y vendible sumados de todas sus ediciones, con el vendible de cada una calculado como
/// <c>max(0, físico − reservado + vencidas)</c> (FUE-03). Lee con la elevación a toda la red del adaptador: el llamador ya comprobó el alcance.
/// </summary>
internal sealed class TotalesDeFuente(IRepositorioDeExistencias existencias, IReservasDeStock reservas)
{
    public async Task<(int Fisico, int Reservado, int Vendible)> De(Fuente fuente, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(fuente);

        var filas = await existencias.LeerDeLaFuente(fuente.Id, null, cancelacion).ConfigureAwait(false);
        var conReservas = filas.Where(f => f.Reservado > 0).Select(f => f.EdicionId).Distinct().ToList();
        var vencidas = conReservas.Count == 0
            ? new Dictionary<(Guid, Guid), int>()
            : ConsultaDeExistencias.Sumar(await reservas.VencidasPorEdicion(conReservas, cancelacion).ConfigureAwait(false));

        long fisico = 0, reservado = 0, vendible = 0;
        foreach (var fila in filas)
        {
            var existencia = ConsultaDeExistencias.Existencia(fuente, fila.EdicionId, fila, vencidas);
            fisico += existencia.Fisico;
            reservado += existencia.Reservado;
            vendible += existencia.Vendible;
        }

        return (Tope(fisico), Tope(reservado), Tope(vendible));
    }

    private static int Tope(long valor) => (int)Math.Min(int.MaxValue, valor);
}

/// <summary>
/// Registra una fuente nueva (FUE-01, FUE-06, FUE-16): solo la administración central, con el código y el nombre únicos y stock cero. La primera fuente registrada
/// queda como la de recepción (FUE-19, D07): quien decide eso toma el bloqueo de recepción <b>después</b> de autorizar (es un bloqueo global) y la auditoría va en la
/// misma transacción, de modo que si no se puede auditar la fuente no se crea. Con la transacción de <c>TransaccionPorCasoDeUso</c> a su alrededor.
/// </summary>
internal sealed class RegistrarFuente(
    AutorizadorDeFuentes autorizador,
    IValidador<RegistrarFuenteEntrada> validador,
    IBloqueosAsesores bloqueos,
    IRepositorioDeFuentes fuentes,
    IAuditoria auditoria) : ICasoDeUso<RegistrarFuenteEntrada, DetalleDeFuente>
{
    public async Task<Resultado<DetalleDeFuente>> Ejecutar(RegistrarFuenteEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeFuentes.Fallo<DetalleDeFuente>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeFuentes.Validacion<DetalleDeFuente>(validacion);
        }

        try
        {
            await bloqueos.Recepcion(cancelacion).ConfigureAwait(false);
        }
        catch (EsperaDeBloqueoAgotadaException)
        {
            return FallosDeFuentes.BloqueoOcupado<DetalleDeFuente>();
        }

        var fuente = Fuente.Registrar(
            Guid.NewGuid(),
            entrada.Codigo,
            entrada.Nombre,
            entrada.Region,
            entrada.ContactoDeNotificacion,
            ReglaDeRecepcion.LaNuevaEsRecepcion(await fuentes.Contar(cancelacion).ConfigureAwait(false)));

        if (await fuentes.Agregar(fuente, cancelacion).ConfigureAwait(false) != ResultadoDeEscrituraDeFuente.Escrita)
        {
            return FallosDeFuentes.De<DetalleDeFuente>(CategoriaDeError.Conflicto, ClavesDeFuentes.FuenteDuplicada);
        }

        await auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.FuenteRegistrada)
            {
                ObjetoTipo = "fuente",
                ObjetoId = FallosDeFuentes.Identificador(fuente.Id),
                SucursalOrigen = fuente.Id,
                Detalle = new Dictionary<string, string?> { ["codigoDeFuente"] = fuente.Codigo },
            },
            cancelacion).ConfigureAwait(false);

        return Resultado<DetalleDeFuente>.Exito(Detalle(fuente, 0, 0, 0));
    }

    internal static DetalleDeFuente Detalle(Fuente fuente, int fisico, int reservado, int vendible) =>
        new(fuente.Id, fuente.Codigo, fuente.Nombre, fuente.Region, fuente.ContactoDeNotificacion, fuente.EsRecepcion, fisico, reservado, vendible);
}

/// <summary>
/// Edita nombre, región y contacto de una fuente (FUE-16): solo la administración central; la fuente se lee bloqueada para que dos ediciones simultáneas no se
/// pisen. La auditoría nombra los campos cambiados y <b>nunca el número de contacto</b>; una edición sin cambios no cambia ni audita nada (es idempotente).
/// </summary>
internal sealed class EditarFuente(
    AutorizadorDeFuentes autorizador,
    IValidador<EditarFuenteEntrada> validador,
    IRepositorioDeFuentes fuentes,
    TotalesDeFuente totales,
    IAuditoria auditoria) : ICasoDeUso<EditarFuenteEntrada, DetalleDeFuente>
{
    public async Task<Resultado<DetalleDeFuente>> Ejecutar(EditarFuenteEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeFuentes.Fallo<DetalleDeFuente>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeFuentes.Validacion<DetalleDeFuente>(validacion);
        }

        var actual = await fuentes.PorCodigoParaActualizar(entrada.Codigo, cancelacion).ConfigureAwait(false);
        if (actual is null)
        {
            return FallosDeFuentes.NoEncontrada<DetalleDeFuente>();
        }

        var (editada, campos) = actual.Editar(entrada.Nombre, entrada.Region, entrada.ContactoDeNotificacion);
        if (campos.Count > 0)
        {
            if (await fuentes.Actualizar(editada, cancelacion).ConfigureAwait(false) != ResultadoDeEscrituraDeFuente.Escrita)
            {
                return FallosDeFuentes.De<DetalleDeFuente>(CategoriaDeError.Conflicto, ClavesDeFuentes.FuenteDuplicada);
            }

            await auditoria.Registrar(
                new SolicitudDeRegistro(AccionesAuditables.FuenteEditada)
                {
                    ObjetoTipo = "fuente",
                    ObjetoId = FallosDeFuentes.Identificador(editada.Id),
                    SucursalOrigen = editada.Id,
                    Detalle = new Dictionary<string, string?>
                    {
                        ["codigoDeFuente"] = editada.Codigo,
                        ["camposCambiados"] = string.Join(',', campos),
                    },
                },
                cancelacion).ConfigureAwait(false);
        }

        var (fisico, reservado, vendible) = await totales.De(editada, cancelacion).ConfigureAwait(false);
        return Resultado<DetalleDeFuente>.Exito(RegistrarFuente.Detalle(editada, fisico, reservado, vendible));
    }
}
