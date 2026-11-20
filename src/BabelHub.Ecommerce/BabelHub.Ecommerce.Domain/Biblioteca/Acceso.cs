namespace BabelHub.Ecommerce.Domain.Biblioteca;

/// <summary>El estado de un acceso (BIB-03, BIB-10, BIB-20): vigente, suspendido por un reporte de archivo defectuoso abierto o revocado por una anulación, un reembolso o un reporte aprobado.</summary>
public enum EstadoDeAcceso
{
    Vigente,
    Suspendido,
    Revocado,
}

/// <summary>Por qué se revocó un acceso (BIB-24). Enumeración de Dominio; el contrato público tiene la suya y un mapeo interno las une.</summary>
public enum CausaDeRevocacion
{
    AnulacionDePedido,
    Reembolso,
    ReporteAprobado,
}

public enum ResultadoDeConcesion
{
    Concedido,
    Reactivado,
    YaConcedidoPorEstePedido,
    YaPoseidoPorOtraCompra,
}

public enum ResultadoDeRevocacion
{
    Revocado,
    YaRevocado,
}

public enum ResultadoDeRevocacionSinEnlace
{
    Revocado,
    EnlaceYaEntregado,
    YaRevocado,

    /// <summary>El acceso es de otra compra (una recompra lo reactivó) o no existe.</summary>
    SinAcceso,

    /// <summary>El acceso está suspendido: no se revoca por esta vía.</summary>
    NoDisponible,
}

public enum ResultadoDeSuspension
{
    Suspendido,
    YaSuspendido,
    NoDisponible,
}

public enum ResultadoDeReanudacion
{
    Reanudado,
    NoDisponible,
}

public enum ResultadoDeConsumo
{
    Consumida,

    /// <summary>El acceso está revocado o suspendido.</summary>
    NoDisponible,

    SinCupo,
}

public enum ResultadoDeRestitucion
{
    Restituida,
    CantidadInvalida,
    NoDisponible,
}

public static class CodigosDeBiblioteca
{
    public static string Codigo(this EstadoDeAcceso estado) => estado switch
    {
        EstadoDeAcceso.Vigente => "vigente",
        EstadoDeAcceso.Suspendido => "suspendido",
        EstadoDeAcceso.Revocado => "revocado",
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };

    public static string Codigo(this CausaDeRevocacion causa) => causa switch
    {
        CausaDeRevocacion.AnulacionDePedido => "anulacion-de-pedido",
        CausaDeRevocacion.Reembolso => "reembolso",
        CausaDeRevocacion.ReporteAprobado => "reporte-aprobado",
        _ => throw new ArgumentOutOfRangeException(nameof(causa)),
    };

    public static EstadoDeAcceso? EstadoDesdeCodigo(string? codigo) => codigo switch
    {
        "vigente" => EstadoDeAcceso.Vigente,
        "suspendido" => EstadoDeAcceso.Suspendido,
        "revocado" => EstadoDeAcceso.Revocado,
        _ => null,
    };

    public static CausaDeRevocacion? CausaDesdeCodigo(string? codigo) => codigo switch
    {
        "anulacion-de-pedido" => CausaDeRevocacion.AnulacionDePedido,
        "reembolso" => CausaDeRevocacion.Reembolso,
        "reporte-aprobado" => CausaDeRevocacion.ReporteAprobado,
        _ => null,
    };
}

/// <summary>
/// El acceso de una cuenta a un libro digital (uno por cuenta y edición, personal e intransferible). Cada transición espeja la sentencia condicional que la infraestructura ejecuta de forma atómica (plan §5): el Dominio decide con
/// el estado que tiene y la base lo repite en una sola sentencia, de modo que la primera que confirma gana. La licencia de la edición no se consulta nunca (BIB-09). El Dominio no lee la hora: llega por parámetro.
/// </summary>
public sealed class Acceso
{
    private Acceso(
        Guid id,
        Guid cuentaId,
        Guid edicionId,
        Guid pedidoId,
        EstadoDeAcceso estado,
        int descargasUsadas,
        int enlacesEntregados,
        DateTimeOffset concedidoEn,
        DateTimeOffset? reactivadoEn,
        DateTimeOffset? revocadoEn,
        CausaDeRevocacion? causaDeRevocacion,
        DateTimeOffset? suspendidoEn)
    {
        Id = id;
        CuentaId = cuentaId;
        EdicionId = edicionId;
        PedidoId = pedidoId;
        Estado = estado;
        DescargasUsadas = descargasUsadas;
        EnlacesEntregados = enlacesEntregados;
        ConcedidoEn = concedidoEn;
        ReactivadoEn = reactivadoEn;
        RevocadoEn = revocadoEn;
        CausaDeRevocacion = causaDeRevocacion;
        SuspendidoEn = suspendidoEn;
    }

    public Guid Id { get; }

    /// <summary>La cuenta titular (opaca: el mismo identificador del usuario, sin más datos).</summary>
    public Guid CuentaId { get; }

    public Guid EdicionId { get; }

    /// <summary>La compra que concedió o reactivó el acceso.</summary>
    public Guid PedidoId { get; private set; }

    public EstadoDeAcceso Estado { get; private set; }

    /// <summary>De 0 a 5; la restitución la baja.</summary>
    public int DescargasUsadas { get; private set; }

    /// <summary>Enlaces entregados desde la concesión o la última reactivación; nunca baja (BIB-22, D03).</summary>
    public int EnlacesEntregados { get; private set; }

    public DateTimeOffset ConcedidoEn { get; private set; }

    public DateTimeOffset? ReactivadoEn { get; private set; }

    public DateTimeOffset? RevocadoEn { get; private set; }

    public CausaDeRevocacion? CausaDeRevocacion { get; private set; }

    public DateTimeOffset? SuspendidoEn { get; private set; }

    public int DescargasRestantes => PoliticaDeDescargas.MaximoDeDescargas - DescargasUsadas;

    /// <summary>La fecha de compra que ve el cliente: la última concesión o reactivación.</summary>
    public DateTimeOffset FechaDeCompra => ReactivadoEn ?? ConcedidoEn;

    public static Acceso Conceder(Guid cuentaId, Guid edicionId, Guid pedidoId, DateTimeOffset ahora, Guid? id = null)
    {
        Exigir(cuentaId, nameof(cuentaId));
        Exigir(edicionId, nameof(edicionId));
        Exigir(pedidoId, nameof(pedidoId));
        return new Acceso(id ?? Guid.NewGuid(), cuentaId, edicionId, pedidoId, EstadoDeAcceso.Vigente, 0, 0, ahora, null, null, null, null);
    }

    /// <summary>Reconstruye el acceso guardado; valida las invariantes que la base también impone con <c>CHECK</c>.</summary>
    public static Acceso Reconstruir(
        Guid id,
        Guid cuentaId,
        Guid edicionId,
        Guid pedidoId,
        EstadoDeAcceso estado,
        int descargasUsadas,
        int enlacesEntregados,
        DateTimeOffset concedidoEn,
        DateTimeOffset? reactivadoEn,
        DateTimeOffset? revocadoEn,
        CausaDeRevocacion? causaDeRevocacion,
        DateTimeOffset? suspendidoEn)
    {
        if (descargasUsadas is < 0 or > PoliticaDeDescargas.MaximoDeDescargas || enlacesEntregados < 0)
        {
            throw new ArgumentException("Las descargas usadas van de 0 a 5 y los enlaces entregados no son negativos.");
        }

        if (estado == EstadoDeAcceso.Revocado && (revocadoEn is null || causaDeRevocacion is null))
        {
            throw new ArgumentException("Un acceso revocado necesita su fecha y su causa.");
        }

        if (estado == EstadoDeAcceso.Suspendido && suspendidoEn is null)
        {
            throw new ArgumentException("Un acceso suspendido necesita su fecha.");
        }

        return new Acceso(id, cuentaId, edicionId, pedidoId, estado, descargasUsadas, enlacesEntregados, concedidoEn, reactivadoEn, revocadoEn, causaDeRevocacion, suspendidoEn);
    }

    /// <summary>
    /// La repetición de una concesión sobre un acceso que ya existe (BIB-19, BIB-20, D13): un revocado de <b>otra</b> compra se reactiva; el del mismo pedido devuelve «ya concedido» aunque esté revocado (la revocación manda sobre
    /// una repetición); un vigente o suspendido de otro pedido es «ya poseído por otra compra».
    /// </summary>
    public ResultadoDeConcesion Reconceder(Guid pedidoId, DateTimeOffset ahora)
    {
        Exigir(pedidoId, nameof(pedidoId));
        if (pedidoId == PedidoId)
        {
            return ResultadoDeConcesion.YaConcedidoPorEstePedido;
        }

        if (Estado != EstadoDeAcceso.Revocado)
        {
            return ResultadoDeConcesion.YaPoseidoPorOtraCompra;
        }

        PedidoId = pedidoId;
        Estado = EstadoDeAcceso.Vigente;
        DescargasUsadas = 0;
        EnlacesEntregados = 0;
        ReactivadoEn = ahora;
        RevocadoEn = null;
        CausaDeRevocacion = null;
        SuspendidoEn = null;
        return ResultadoDeConcesion.Reactivado;
    }

    /// <summary>Revoca una sola vez, desde vigente o suspendido, y solo si la orden es de la compra vigente del acceso (BIB-24, D16).</summary>
    public ResultadoDeRevocacion Revocar(Guid pedidoId, CausaDeRevocacion causa, DateTimeOffset ahora)
    {
        if (pedidoId != PedidoId || Estado == EstadoDeAcceso.Revocado)
        {
            return ResultadoDeRevocacion.YaRevocado;
        }

        Marcar(causa, ahora);
        return ResultadoDeRevocacion.Revocado;
    }

    /// <summary>Revoca solo si no se entregó ningún enlace y el acceso está vigente (DEV-04, BIB-22, BIB-23).</summary>
    public ResultadoDeRevocacionSinEnlace RevocarSiNoHayEnlace(Guid pedidoId, CausaDeRevocacion causa, DateTimeOffset ahora)
    {
        if (pedidoId != PedidoId)
        {
            return ResultadoDeRevocacionSinEnlace.SinAcceso;
        }

        if (Estado == EstadoDeAcceso.Revocado)
        {
            return ResultadoDeRevocacionSinEnlace.YaRevocado;
        }

        if (Estado == EstadoDeAcceso.Suspendido)
        {
            return ResultadoDeRevocacionSinEnlace.NoDisponible;
        }

        if (EnlacesEntregados > 0)
        {
            return ResultadoDeRevocacionSinEnlace.EnlaceYaEntregado;
        }

        Marcar(causa, ahora);
        return ResultadoDeRevocacionSinEnlace.Revocado;
    }

    /// <summary>Solo desde vigente (BIB-10); un revocado no pasa a suspendido.</summary>
    public ResultadoDeSuspension Suspender(DateTimeOffset ahora)
    {
        switch (Estado)
        {
            case EstadoDeAcceso.Vigente:
                Estado = EstadoDeAcceso.Suspendido;
                SuspendidoEn = ahora;
                return ResultadoDeSuspension.Suspendido;
            case EstadoDeAcceso.Suspendido:
                return ResultadoDeSuspension.YaSuspendido;
            default:
                return ResultadoDeSuspension.NoDisponible;
        }
    }

    /// <summary>Solo desde suspendido (BIB-21).</summary>
    public ResultadoDeReanudacion Reanudar()
    {
        if (Estado != EstadoDeAcceso.Suspendido)
        {
            return ResultadoDeReanudacion.NoDisponible;
        }

        Estado = EstadoDeAcceso.Vigente;
        SuspendidoEn = null;
        return ResultadoDeReanudacion.Reanudado;
    }

    /// <summary>Cuenta una descarga al entregar el enlace (BIB-05, BIB-17): exige vigente y cupo.</summary>
    public ResultadoDeConsumo ConsumirDescarga()
    {
        if (Estado != EstadoDeAcceso.Vigente)
        {
            return ResultadoDeConsumo.NoDisponible;
        }

        if (DescargasUsadas >= PoliticaDeDescargas.MaximoDeDescargas)
        {
            return ResultadoDeConsumo.SinCupo;
        }

        DescargasUsadas++;
        EnlacesEntregados++;
        return ResultadoDeConsumo.Consumida;
    }

    /// <summary>La administración devuelve de 1 hasta las usadas (BIB-08, D03); no toca un acceso revocado ni baja <c>EnlacesEntregados</c>.</summary>
    public ResultadoDeRestitucion Restituir(int cantidad)
    {
        if (Estado == EstadoDeAcceso.Revocado)
        {
            return ResultadoDeRestitucion.NoDisponible;
        }

        if (cantidad < 1 || cantidad > DescargasUsadas)
        {
            return ResultadoDeRestitucion.CantidadInvalida;
        }

        DescargasUsadas -= cantidad;
        return ResultadoDeRestitucion.Restituida;
    }

    private void Marcar(CausaDeRevocacion causa, DateTimeOffset ahora)
    {
        Estado = EstadoDeAcceso.Revocado;
        RevocadoEn = ahora;
        CausaDeRevocacion = causa;
        SuspendidoEn = null;
    }

    private static void Exigir(Guid valor, string nombre)
    {
        if (valor == Guid.Empty)
        {
            throw new ArgumentException("El identificador no puede ser vacío.", nombre);
        }
    }
}
