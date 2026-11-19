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

    /// <summary>Una hoja por su número (sin bloqueo): el consumidor consulta por número y la Aplicación comprueba el correo y la marca de cuenta cancelada.</summary>
    Task<HojaDeReclamacion?> ObtenerPorNumero(NumeroDeHoja numero, CancellationToken cancelacion = default);

    /// <summary>Las hojas de un correo normalizado, también las descartadas y sin las de una cuenta cancelada, por fecha de registro descendente (REC-14, REC-20).</summary>
    Task<ListadoDeHojas> ListarDelCorreo(string correoNormalizado, int pagina, int tamano, CancellationToken cancelacion = default);

    /// <summary>Cuántas hojas pendientes tiene un correo normalizado, sin las descartadas ni las de una cuenta cancelada (REC-19).</summary>
    Task<int> ContarPendientesDelCorreo(string correoNormalizado, CancellationToken cancelacion = default);

    /// <summary>Marca como de una cuenta cancelada todas las hojas de un correo normalizado: todo o nada (la transacción de Identidad lo revierte si algo falla), idempotente. Devuelve cuántas cambió (REC-20).</summary>
    Task<int> MarcarDeCuentaCancelada(string correoNormalizado, CancellationToken cancelacion = default);
}

/// <summary>
/// El correo <b>verificado</b> de una cuenta, normalizado (recortado y en minúsculas), o <c>null</c> si la cuenta no tiene un correo verificado (D04). Lo implementa un adaptador sobre el contrato de Identidad; si Identidad no responde,
/// lanza y la operación falla cerrada. Solo un correo verificado da acceso a las hojas y solo uno verificado marca las hojas al cancelar la cuenta: un correo sin verificar podría ser de otra persona.
/// </summary>
internal interface ICorreoVerificadoDeLaCuenta
{
    Task<string?> De(Guid usuarioId, CancellationToken cancelacion = default);
}

/// <summary>
/// La anonimización de las hojas que cumplieron dos años (D10, REC-17): invoca la <b>función de base de datos</b> sin parámetros, bajo el rol del esquema y <b>dentro de la transacción actual</b> (la misma que la auditoría),
/// que anonimiza nombre, documento, domicilio y correo, deja el correo normalizado y el resumen del contenido en nulo, redacta el detalle y la respuesta, y conserva número, fecha, tipo, estado, bien, monto y
/// referencia del pedido. Lo implementa la Infraestructura (T13 y siguientes).
/// </summary>
internal interface IAnonimizadorDeHojas
{
    /// <summary>Anonimiza las hojas con dos años por día de Lima (idempotente: una ya anonimizada no se toca) y devuelve cuántas.</summary>
    Task<int> Anonimizar(CancellationToken cancelacion = default);

    /// <summary>El instante de la última corrida completa de la anonimización, o <c>null</c> si nunca corrió.</summary>
    Task<DateTimeOffset?> UltimaCorridaEn(CancellationToken cancelacion = default);
}
