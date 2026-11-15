using BabelHub.Inventario.Application.Plataforma;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Reservas;

/// <summary>
/// Cómo entra la Infraestructura de <c>Reservas</c> a su esquema: cada operación corre <b>dentro de la transacción de la unidad de trabajo</b> y con <c>SET LOCAL ROLE rol_reservas</c> (<c>EjecutarComo</c>), porque
/// el rol de la aplicación por sí solo no lee ningún esquema (AD18). A diferencia de <c>stock</c>, las tablas de reservas no llevan seguridad por filas: no hay un alcance de datos que, sin actor, devuelva cero filas
/// sin error. El actor lo exige la Aplicación en cada caso de uso (cliente, sistema o integración) antes de llegar aquí.
/// </summary>
internal sealed class AccesoAReservas(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion)
{
    private static readonly EsquemaDeModulo Esquema = EsquemaDeModulo.Crear(DefinicionDeReservas.Esquema);

    /// <summary>Corre el bloque con el rol del módulo, sobre la conexión y la transacción en curso.</summary>
    public async Task<T> Ejecutar<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(bloque);

        var conexionActiva = (NpgsqlConnection)conexion.Conexion;
        var transaccion = (NpgsqlTransaction)conexion.Transaccion;
        T resultado = default!;

        await unidad.EjecutarComo(
            Esquema,
            async token => resultado = await bloque(conexionActiva, transaccion, token).ConfigureAwait(false),
            cancelacion).ConfigureAwait(false);

        return resultado;
    }
}
