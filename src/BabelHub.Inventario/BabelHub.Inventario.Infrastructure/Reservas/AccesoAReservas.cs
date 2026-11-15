using BabelHub.Inventario.Application.Plataforma;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Reservas;

/// <summary>
/// Cómo entra la Infraestructura de <c>Reservas</c> a su esquema: cada operación corre <b>dentro de la transacción de la unidad de trabajo</b> y con <c>SET LOCAL ROLE rol_reservas</c> (<c>EjecutarComo</c>), porque
/// el rol de la aplicación por sí solo no lee ningún esquema (AD18). A diferencia de <c>stock</c>, las tablas de reservas no llevan seguridad por filas: no hay un alcance de datos que, sin actor, devuelva cero filas
/// sin error. Aun así, <b>falla cerrado sin actor</b>, como <c>AccesoAFuentes</c>: si un caso de uso futuro olvida autorizar antes, el acceso no pasa en silencio. Una espera de bloqueo agotada (<c>55P03</c>, la
/// <c>lock_timeout</c> de la transacción) se traduce a <see cref="EsperaDeBloqueoAgotadaException"/> para que el caso de uso responda «bloqueo ocupado» y no un error del servidor.
/// </summary>
internal sealed class AccesoAReservas(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion, IActorActual actores)
{
    private const string EsperaAgotada = "55P03";

    private static readonly EsquemaDeModulo Esquema = EsquemaDeModulo.Crear(DefinicionDeReservas.Esquema);

    /// <summary>Corre el bloque con el rol del módulo, sobre la conexión y la transacción en curso.</summary>
    public async Task<T> Ejecutar<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(bloque);

        if (actores.Actual is not { Tipo: not TipoDeActor.Anonimo })
        {
            throw new ContextoDeActorAusenteException("Se intentó acceder a las reservas sin un actor con contexto: cada caso de uso autoriza antes y un trabajo de fondo corre como sistema.");
        }

        var conexionActiva = (NpgsqlConnection)conexion.Conexion;
        var transaccion = (NpgsqlTransaction)conexion.Transaccion;
        T resultado = default!;

        try
        {
            await unidad.EjecutarComo(
                Esquema,
                async token => resultado = await bloque(conexionActiva, transaccion, token).ConfigureAwait(false),
                cancelacion).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == EsperaAgotada)
        {
            throw new EsperaDeBloqueoAgotadaException(ex);
        }

        return resultado;
    }
}
