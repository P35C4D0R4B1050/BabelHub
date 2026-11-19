namespace BabelHub.Ecommerce.Domain.Pagos;

/// <summary>Quién pidió el reembolso. Cada origen tiene su clave natural (D08).</summary>
public enum OrigenDeReembolso
{
    Compensacion,
    Anulacion,
    CancelacionDelCliente,
    Devolucion,
}

/// <summary><c>Pendiente</c> falta pedirlo, <c>Solicitado</c> se reclamó y se pidió a la pasarela, <c>Reembolsado</c> concluyó. Pendiente y solicitado cuentan como pendientes (PAG-24).</summary>
public enum EstadoDeReembolso
{
    Pendiente,
    Solicitado,
    Reembolsado,
}

/// <summary>Por qué un reembolso volvió a pendiente: la pasarela no respondió, lo rechazó o no admite reembolsar con ese medio (p. ej. Yape).</summary>
public enum MotivoPendienteDeReembolso
{
    SinRespuesta,
    RechazadoPorPasarela,
    NoSoportado,
}

/// <summary>Lo que se reembolsa de una línea de la compra.</summary>
public sealed record LineaDeReembolso(Guid LineaId, Importe Monto);

/// <summary>Las claves naturales de los reembolsos (D08): únicas por origen, de hasta 100 caracteres con GUID reales.</summary>
public static class ClaveDeReembolso
{
    public static string Compensacion(Guid intentoId) => $"comp:{intentoId:D}";

    public static string Anulacion(Guid pedidoId) => $"anul:{pedidoId:D}";

    public static string Cancelacion(Guid pedidoId) => $"canc:{pedidoId:D}";

    public static string Devolucion(Guid devolucionId, Guid lineaId) => $"dev:{devolucionId:D}:{lineaId:D}";
}

/// <summary>
/// Un reembolso total o parcial de un cargo (PAG-10, D08): de una compensación, una anulación, la cancelación del cliente o una devolución. Su <c>Id</c> viaja en los metadatos de la pasarela
/// para reconocerlo al consultar. Una compensación reembolsa el cargo completo sin líneas; los demás llevan sus líneas y, a lo más una vez, el envío. El tope se valida contra los demás
/// reembolsos del mismo cargo con <see cref="ReglasDeReembolso"/>, bajo el bloqueo de la intención. No guarda datos de pago.
/// </summary>
public sealed class Reembolso
{
    public const int LongitudMaximaDeLaClave = 100;

    /// <summary>Tras un <c>sin_respuesta</c> se espera más que el tiempo de espera de la pasarela (10 s) más un margen antes de pedir otra vez.</summary>
    public static readonly TimeSpan EsperaTrasSinRespuesta = PlazosDelPago.TiempoDeRespuesta + TimeSpan.FromSeconds(20);

    /// <summary>Un reembolso <c>solicitado</c> sin resolver tras este tiempo es huérfano: un trabajo lo reanuda consultando a la pasarela.</summary>
    public static readonly TimeSpan PlazoDeHuerfano = TimeSpan.FromMinutes(2);

    private Reembolso(
        Guid id,
        OrigenDeReembolso origen,
        string claveNatural,
        Guid intencionId,
        Guid intentoId,
        Importe monto,
        IReadOnlyList<LineaDeReembolso> lineas,
        Importe envio,
        DateTimeOffset ahora)
    {
        Id = id;
        Origen = origen;
        ClaveNatural = claveNatural;
        IntencionId = intencionId;
        IntentoId = intentoId;
        Monto = monto;
        Lineas = lineas;
        Envio = envio;
        CreadoEn = ahora;
        Estado = EstadoDeReembolso.Pendiente;
    }

    public Guid Id { get; }

    public OrigenDeReembolso Origen { get; }

    public string ClaveNatural { get; }

    public Guid IntencionId { get; }

    /// <summary>El cargo que se reembolsa: el intento aprobado que originó el pedido, o el sobrante.</summary>
    public Guid IntentoId { get; }

    public Importe Monto { get; }

    public IReadOnlyList<LineaDeReembolso> Lineas { get; }

    /// <summary>La parte del envío incluida en el monto (cero si no se reembolsa el envío).</summary>
    public Importe Envio { get; }

    public EstadoDeReembolso Estado { get; private set; }

    public string? ReferenciaDePasarela { get; private set; }

    public int Intentos { get; private set; }

    public DateTimeOffset? UltimoIntentoEn { get; private set; }

    public MotivoPendienteDeReembolso? MotivoPendiente { get; private set; }

    public DateTimeOffset CreadoEn { get; }

    public DateTimeOffset? ResueltoEn { get; private set; }

    public bool CuentaComoPendiente => Estado is EstadoDeReembolso.Pendiente or EstadoDeReembolso.Solicitado;

    /// <summary>Un reembolso de líneas (y a lo más una vez el envío). El monto es la suma de las líneas más el envío y es mayor que cero.</summary>
    public static Reembolso Crear(
        Guid id,
        OrigenDeReembolso origen,
        string claveNatural,
        Guid intencionId,
        Guid intentoId,
        IReadOnlyList<LineaDeReembolso> lineas,
        Importe envio,
        DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        if (lineas.Select(l => l.LineaId).Distinct().Count() != lineas.Count)
        {
            throw new ArgumentException("Una línea aparece una sola vez en un reembolso: el tope por línea se compara contra su suma.", nameof(lineas));
        }

        var monto = envio;
        foreach (var linea in lineas)
        {
            if (linea.LineaId == Guid.Empty)
            {
                throw new ArgumentException("Cada línea reembolsada necesita su identificador.", nameof(lineas));
            }

            monto += linea.Monto;
        }

        return Construir(id, origen, claveNatural, intencionId, intentoId, monto, lineas.ToArray(), envio, ahora);
    }

    /// <summary>El reembolso del cargo completo, sin líneas: el de una compensación o un cobro sobrante (<c>solo_reembolso</c>).</summary>
    public static Reembolso CrearDeCargo(Guid id, OrigenDeReembolso origen, string claveNatural, Guid intencionId, Guid intentoId, Importe monto, DateTimeOffset ahora)
    {
        if (origen != OrigenDeReembolso.Compensacion)
        {
            throw new ArgumentException("Solo la compensación reembolsa el cargo completo sin líneas.", nameof(origen));
        }

        return Construir(id, origen, claveNatural, intencionId, intentoId, monto, Array.Empty<LineaDeReembolso>(), Importe.Cero, ahora);
    }

    /// <summary>
    /// Reclama el reembolso para pedirlo a la pasarela (transición condicional): solo uno que está pendiente y que <see cref="PuedeReintentarse"/> (tras un <c>sin_respuesta</c> se
    /// respeta la espera, también cuando lo reintenta la administración). Cuenta el intento.
    /// </summary>
    public bool Reclamar(DateTimeOffset ahora)
    {
        if (!PuedeReintentarse(ahora))
        {
            return false;
        }

        Estado = EstadoDeReembolso.Solicitado;
        Intentos++;
        UltimoIntentoEn = ahora;
        MotivoPendiente = null;
        return true;
    }

    public bool MarcarReembolsado(string referenciaDePasarela, DateTimeOffset ahora)
    {
        if (Estado != EstadoDeReembolso.Solicitado)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(referenciaDePasarela))
        {
            throw new ArgumentException("Un reembolso concluido trae su referencia de la pasarela.", nameof(referenciaDePasarela));
        }

        Estado = EstadoDeReembolso.Reembolsado;
        ReferenciaDePasarela = referenciaDePasarela.Trim();
        ResueltoEn = ahora;
        MotivoPendiente = null;
        return true;
    }

    /// <summary>La pasarela no respondió, lo rechazó o no lo soporta: vuelve a pendiente, visible para la administración y reintentable.</summary>
    public bool DejarPendiente(MotivoPendienteDeReembolso motivo, DateTimeOffset ahora)
    {
        if (Estado != EstadoDeReembolso.Solicitado)
        {
            return false;
        }

        Estado = EstadoDeReembolso.Pendiente;
        MotivoPendiente = motivo;
        UltimoIntentoEn ??= ahora;
        return true;
    }

    /// <summary>Un reembolso pendiente se puede pedir otra vez; tras un <c>sin_respuesta</c> solo pasada la espera, para no pedirlo a ciegas dos veces.</summary>
    public bool PuedeReintentarse(DateTimeOffset ahora)
    {
        if (Estado != EstadoDeReembolso.Pendiente)
        {
            return false;
        }

        return MotivoPendiente != MotivoPendienteDeReembolso.SinRespuesta || UltimoIntentoEn is null || ahora - UltimoIntentoEn.Value > EsperaTrasSinRespuesta;
    }

    public bool EsHuerfano(DateTimeOffset ahora) => Estado == EstadoDeReembolso.Solicitado && UltimoIntentoEn is { } ultimo && ahora - ultimo >= PlazoDeHuerfano;

    internal Reembolso Copiar() => (Reembolso)MemberwiseClone();

    /// <summary>Sin la referencia de la pasarela ni la clave.</summary>
    public override string ToString() => $"Reembolso {{ Id = {Id}, Origen = {Origen}, Estado = {Estado} }}";

    private static Reembolso Construir(
        Guid id,
        OrigenDeReembolso origen,
        string claveNatural,
        Guid intencionId,
        Guid intentoId,
        Importe monto,
        IReadOnlyList<LineaDeReembolso> lineas,
        Importe envio,
        DateTimeOffset ahora)
    {
        if (id == Guid.Empty || intencionId == Guid.Empty || intentoId == Guid.Empty)
        {
            throw new ArgumentException("El reembolso necesita su identificador, su intención y su cargo.");
        }

        if (string.IsNullOrWhiteSpace(claveNatural) || claveNatural.Trim().Length > LongitudMaximaDeLaClave)
        {
            throw new ArgumentException("La clave natural del reembolso va de 1 a 100 caracteres.", nameof(claveNatural));
        }

        if (monto.Valor <= 0m)
        {
            throw new ArgumentException("Un reembolso es por un monto mayor que cero.", nameof(monto));
        }

        return new Reembolso(id, origen, claveNatural.Trim(), intencionId, intentoId, monto, lineas, envio, ahora);
    }
}

public enum ResultadoDeSolicitud
{
    Nueva,
    Repetida,
    MontoDistinto,
}

public enum ResultadoDeTope
{
    Dentro,
    SuperaElCargo,
    SuperaLaLinea,
    EnvioRepetido,
    LineaDesconocida,
}

/// <summary>Las reglas que comparan un reembolso con los demás del mismo cargo; la Aplicación las evalúa bajo el bloqueo de la intención (no hay un <c>CHECK</c> sobre una suma).</summary>
public static class ReglasDeReembolso
{
    /// <summary>Registrar de nuevo el mismo origen y clave es idempotente con el mismo monto; con otro monto se rechaza.</summary>
    public static ResultadoDeSolicitud ResolverSolicitud(Reembolso? existente, OrigenDeReembolso origen, string claveNatural, Importe monto)
    {
        if (existente is null)
        {
            return ResultadoDeSolicitud.Nueva;
        }

        return existente.Origen == origen && string.Equals(existente.ClaveNatural, claveNatural.Trim(), StringComparison.Ordinal) && existente.Monto == monto
            ? ResultadoDeSolicitud.Repetida
            : ResultadoDeSolicitud.MontoDistinto;
    }

    /// <summary>
    /// El tope del nuevo reembolso: la suma de los reembolsos de un cargo (pendientes, solicitados y reembolsados) no supera su importe; el acumulado por línea no supera el importe de la
    /// línea; y el envío se reembolsa a lo más una vez. Solo cuentan los reembolsos del mismo cargo: el de un cobro sobrante no consume el tope del cargo del pedido.
    /// </summary>
    public static ResultadoDeTope ValidarTope(
        Importe importeDelCargo,
        Importe envioDeLaCompra,
        IReadOnlyDictionary<Guid, Importe> importesDeLasLineas,
        IEnumerable<Reembolso> existentesDeLaIntencion,
        Reembolso nuevo)
    {
        ArgumentNullException.ThrowIfNull(importesDeLasLineas);
        ArgumentNullException.ThrowIfNull(existentesDeLaIntencion);
        ArgumentNullException.ThrowIfNull(nuevo);

        var delCargo = existentesDeLaIntencion.Where(r => r.IntentoId == nuevo.IntentoId && !ReferenceEquals(r, nuevo)).ToArray();

        foreach (var linea in nuevo.Lineas)
        {
            if (!importesDeLasLineas.TryGetValue(linea.LineaId, out var importeDeLaLinea))
            {
                return ResultadoDeTope.LineaDesconocida;
            }

            var acumulado = delCargo.SelectMany(r => r.Lineas).Where(l => l.LineaId == linea.LineaId).Aggregate(Importe.Cero, (suma, l) => suma + l.Monto) + linea.Monto;
            if (acumulado > importeDeLaLinea)
            {
                return ResultadoDeTope.SuperaLaLinea;
            }
        }

        var envioAcumulado = delCargo.Aggregate(Importe.Cero, (suma, r) => suma + r.Envio) + nuevo.Envio;
        if (envioAcumulado > envioDeLaCompra)
        {
            return ResultadoDeTope.EnvioRepetido;
        }

        var totalAcumulado = delCargo.Aggregate(Importe.Cero, (suma, r) => suma + r.Monto) + nuevo.Monto;
        return totalAcumulado > importeDelCargo ? ResultadoDeTope.SuperaElCargo : ResultadoDeTope.Dentro;
    }
}
