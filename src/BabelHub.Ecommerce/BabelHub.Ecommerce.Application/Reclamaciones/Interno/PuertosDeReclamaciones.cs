using BabelHub.Ecommerce.Domain.Reclamaciones;

namespace BabelHub.Ecommerce.Application.Reclamaciones.Interno;

internal enum ResultadoDeAgregado
{
    Agregada,

    /// <summary>Ya existe una hoja con ese <c>clave_hash</c> (el índice único, la autoridad final de la idempotencia, REC-18).</summary>
    ClaveRepetida,
}

internal sealed record ListadoDeHojas(IReadOnlyList<HojaDeReclamacion> Hojas, int Total);

/// <summary>El almacén (Infraestructura) no pudo atender a tiempo (un <c>lock_timeout</c> de la secuencia, una conexión caída): se responde «servicio no disponible» sin dejar nada a medias.</summary>
internal sealed class HojasNoDisponiblesException : Exception
{
    public HojasNoDisponiblesException()
    {
    }

    public HojasNoDisponiblesException(string message)
        : base(message)
    {
    }

    public HojasNoDisponiblesException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Un registro revertido: otra solicitud con la misma clave ganó la carrera. Se atrapa fuera de la transacción para devolver la constancia ya creada.</summary>
internal sealed class ClaveDeHojaRepetidaException : Exception
{
    public ClaveDeHojaRepetidaException()
    {
    }

    public ClaveDeHojaRepetidaException(string message)
        : base(message)
    {
    }

    public ClaveDeHojaRepetidaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>El repositorio de las hojas y de la secuencia por año (D10). Lo implementa la Infraestructura bajo <c>EjecutarComo(reclamaciones)</c> (T13).</summary>
internal interface IRepositorioDeHojas
{
    /// <summary>Avanza y devuelve el siguiente número del año de Lima, con <c>UPDATE … RETURNING</c> bajo su bloqueo: serializa los registros y un registro revertido devuelve su número.</summary>
    Task<int> SiguienteSecuencia(int anio, CancellationToken cancelacion = default);

    Task<ResultadoDeAgregado> Agregar(HojaDeReclamacion hoja, CancellationToken cancelacion = default);

    Task<HojaDeReclamacion?> BuscarPorClave(IReadOnlyList<string> clavesHash, CancellationToken cancelacion = default);

    /// <summary>Lee una hoja; con <paramref name="paraActualizar"/>, con <c>FOR UPDATE</c> y <c>lock_timeout</c>.</summary>
    Task<HojaDeReclamacion?> Obtener(Guid id, bool paraActualizar, CancellationToken cancelacion = default);

    /// <summary>Guarda solo lo que cambia: la respuesta, el estado, el aviso y la marca de cuenta cancelada.</summary>
    Task Guardar(HojaDeReclamacion hoja, CancellationToken cancelacion = default);

    /// <summary>Lista por fecha de registro descendente. Sin estado, las pendientes y las respondidas; con un estado, solo ese.</summary>
    Task<ListadoDeHojas> Listar(EstadoDeHoja? estado, int pagina, int tamano, CancellationToken cancelacion = default);
}
