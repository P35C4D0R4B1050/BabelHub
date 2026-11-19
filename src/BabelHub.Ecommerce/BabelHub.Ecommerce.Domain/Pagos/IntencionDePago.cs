namespace BabelHub.Ecommerce.Domain.Pagos;

/// <summary>
/// El estado de la intención (D23). <c>Abierta</c> no tiene intento activo; <c>ConCobro</c> sí (iniciado o pendiente de confirmación); <c>Aprobada</c> tiene el cobro aprobado y falta
/// completar la compra; <c>Completada</c>, <c>Compensada</c> y <c>CerradaSinCobro</c> son finales. Un intento activo es entonces «el estado es <c>ConCobro</c> o <c>Aprobada</c>».
/// </summary>
public enum EstadoDeIntencion
{
    Abierta,
    ConCobro,
    Aprobada,
    Completada,
    Compensada,
    CerradaSinCobro,
}

/// <summary>Por qué se cerró una intención sin cobro. Una «rechazada sin reintento» (PAG-27) es <see cref="RechazadaSinReintento"/>.</summary>
public enum MotivoDeCierre
{
    Vencida,
    Cancelada,
    RechazadaSinReintento,
}

/// <summary>El resultado de <c>Crear</c> de Carrito sobre una intención existente (D21).</summary>
public enum ResultadoDeCrear
{
    /// <summary>Misma huella y no obsoleta en un estado vivo: idempotente, no cambia nada (también con un intento activo).</summary>
    Creada,

    /// <summary>Se reemplazó la instantánea y se renovaron los plazos.</summary>
    Reemplazada,

    /// <summary>Otra huella u obsoleta con un intento activo: no se puede reemplazar.</summary>
    Conflicto,

    /// <summary>Cerrada sin cobro o compensada.</summary>
    Cerrada,

    /// <summary>Aprobada o completada, con cualquier huella: una compra aprobada no se aborta aunque el precio cambie.</summary>
    YaEnCurso,
}

public enum ResultadoDeReapertura
{
    Reabierta,
    Cerrada,
    ConIntentoActivo,
    YaEnCurso,
}

public enum ResultadoDeCancelacion
{
    Cancelada,
    Cerrada,
    ConIntentoActivo,
}

/// <summary>Si se puede cobrar ahora, o el motivo por el que se niega (PAG-08, PAG-17, D21).</summary>
public enum ValidacionDelCobro
{
    Permitido,
    ReservaExpirada,
    IntencionCerrada,
    InstantaneaObsoleta,
    CobroEnCurso,
}

public enum ResultadoDeCompletar
{
    Completada,

    /// <summary>La intención se desvinculó por la cancelación de la cuenta mientras se confirmaba: se compensa (PAG-29).</summary>
    Desvinculada,
}

/// <summary>
/// La intención de pago (D01, D23): su identificador es el <c>pedidoId</c>. Guarda la instantánea que Carrito congeló y gobierna los plazos, las transiciones y la retención del
/// contacto; los intentos de cobro son otra entidad. No lee la hora ni habla con nada externo: el reloj entra por parámetro. La Aplicación la bloquea (<c>FOR UPDATE</c>) antes de
/// cambiarla.
/// </summary>
public sealed class IntencionDePago
{
    private IntencionDePago(Guid id, InstantaneaDeIntencion instantanea, DateTimeOffset? reservaExpiraEn, DateTimeOffset ahora)
    {
        Id = id;
        CreadaEn = ahora;
        Estado = EstadoDeIntencion.Abierta;
        Aplicar(instantanea, reservaExpiraEn, ahora);
    }

    public Guid Id { get; }

    /// <summary>Nulo tras desvincular la cuenta (PAG-26).</summary>
    public Guid? CuentaId { get; private set; }

    public EstadoDeIntencion Estado { get; private set; }

    public MotivoDeCierre? MotivoDeCierre { get; private set; }

    public bool TieneImpresos { get; private set; }

    public TipoDeEntrega? TipoDeEntrega { get; private set; }

    public string? CodigoDeSucursalDeRecojo { get; private set; }

    public string? RegionDeEntrega { get; private set; }

    public Importe Total { get; private set; }

    public Importe Envio { get; private set; }

    public string? HuellaDeInstantanea { get; private set; }

    /// <summary>La marca de <c>Reabrir</c>: no se puede cobrar hasta que un <c>Crear</c> la reemplace.</summary>
    public bool InstantaneaObsoleta { get; private set; }

    public IReadOnlyList<LineaDeIntencion> Lineas { get; private set; } = Array.Empty<LineaDeIntencion>();

    public TextoAceptado? Terminos { get; private set; }

    public TextoAceptado? Privacidad { get; private set; }

    public TextoAceptado? ReembolsoDigital { get; private set; }

    public ContactoDeEntrega? Contacto { get; private set; }

    public DateTimeOffset? ContactoLiberadoEn { get; private set; }

    public DateTimeOffset? DesvinculadaEn { get; private set; }

    public DateTimeOffset CreadaEn { get; }

    public DateTimeOffset IniciadaEn { get; private set; }

    public DateTimeOffset VenceEn { get; private set; }

    public DateTimeOffset LimitePendienteEn { get; private set; }

    public DateTimeOffset? ReservaExpiraEn { get; private set; }

    public bool ReservaExtendida { get; private set; }

    public DateTimeOffset? AprobadoEn { get; private set; }

    public DateTimeOffset? ConfirmacionIniciadaEn { get; private set; }

    public DateTimeOffset? ReservasConfirmadasEn { get; private set; }

    /// <summary>Lo que devolvió <c>reservas.confirmar</c> por cada línea impresa (la fuente y el traslado, D02): se guarda para que Pedidos cree el pedido con ello, aun si la compra se retoma.</summary>
    public IReadOnlyList<ReservaConfirmadaDeLinea> LineasConfirmadas { get; private set; } = Array.Empty<ReservaConfirmadaDeLinea>();

    public DateTimeOffset? ReclamadaHasta { get; private set; }

    public DateTimeOffset? CerradaEn { get; private set; }

    public bool EstaDesvinculada => DesvinculadaEn is not null;

    /// <summary>Hay un intento <c>iniciado</c>, <c>pendiente_de_confirmacion</c> o <c>aprobado</c>.</summary>
    public bool TieneIntentoActivo => Estado is EstadoDeIntencion.ConCobro or EstadoDeIntencion.Aprobada;

    /// <summary>Se aprobó pasados los 30 minutos desde el inicio o con la reserva expirada (PAG-15): no se confirma el pedido y se reembolsa.</summary>
    public bool EsTardia => AprobadoEn is { } aprobado && PlazosDelPago.EsTardio(IniciadaEn, aprobado, ReservaExpiraEn);

    /// <summary>Se deben confirmar las reservas de los impresos: aprobada a tiempo, con impresos y sin desvincular (la compra solo digital no consulta ni reserva).</summary>
    public bool DebeConfirmarReservas => Estado == EstadoDeIntencion.Aprobada && TieneImpresos && !EsTardia && !EstaDesvinculada;

    /// <summary>Crea la intención <c>abierta</c> con su instantánea. Con impresos hace falta el instante en que expira la reserva.</summary>
    public static IntencionDePago Crear(Guid pedidoId, InstantaneaDeIntencion instantanea, DateTimeOffset? reservaExpiraEn, DateTimeOffset ahora)
    {
        if (pedidoId == Guid.Empty)
        {
            throw new ArgumentException("La intención necesita su identificador.", nameof(pedidoId));
        }

        ArgumentNullException.ThrowIfNull(instantanea);
        return new IntencionDePago(pedidoId, instantanea, reservaExpiraEn, ahora);
    }

    /// <summary>Una copia de la intención (para que las pruebas simulen la reversión de una transacción).</summary>
    internal IntencionDePago Copiar() => (IntencionDePago)MemberwiseClone();

    public bool EsDeLaCuenta(Guid cuentaId) => cuentaId != Guid.Empty && CuentaId == cuentaId;

    /// <summary>
    /// <c>Crear</c> sobre una intención existente (D21). Reemplaza la instantánea solo si está <c>abierta</c> sin intento activo y la huella difiere o está obsoleta, y renueva
    /// <c>iniciada_en</c>, <c>vence_en</c>, <c>limite_pendiente_en</c> y <c>reserva_expira_en</c>.
    /// </summary>
    public ResultadoDeCrear Reemplazar(InstantaneaDeIntencion instantanea, DateTimeOffset? reservaExpiraEn, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(instantanea);
        if (!EstaDesvinculada && CuentaId != instantanea.CuentaId)
        {
            throw new InvalidOperationException("La instantánea es de otra cuenta: la propiedad la comprueba quien llama.");
        }

        // Una intención desvinculada por la cancelación de la cuenta nunca se reconstruye (PAG-26).
        if (EstaDesvinculada)
        {
            return ResultadoDeCrear.Cerrada;
        }

        switch (Estado)
        {
            case EstadoDeIntencion.CerradaSinCobro or EstadoDeIntencion.Compensada:
                return ResultadoDeCrear.Cerrada;
            case EstadoDeIntencion.Aprobada or EstadoDeIntencion.Completada:
                return ResultadoDeCrear.YaEnCurso;
        }

        var mismaInstantanea = !InstantaneaObsoleta && string.Equals(HuellaDeInstantanea, instantanea.Huella, StringComparison.Ordinal);
        if (mismaInstantanea)
        {
            return ResultadoDeCrear.Creada;
        }

        if (Estado == EstadoDeIntencion.ConCobro)
        {
            return ResultadoDeCrear.Conflicto;
        }

        Aplicar(instantanea, reservaExpiraEn, ahora);
        return ResultadoDeCrear.Reemplazada;
    }

    /// <summary>La llama Carrito antes de editar con el pago abierto: deja la intención abierta, elimina su contacto y la marca obsoleta (D21).</summary>
    public ResultadoDeReapertura Reabrir()
    {
        if (EstaDesvinculada)
        {
            return ResultadoDeReapertura.Cerrada;
        }

        switch (Estado)
        {
            case EstadoDeIntencion.CerradaSinCobro or EstadoDeIntencion.Compensada:
                return ResultadoDeReapertura.Cerrada;
            case EstadoDeIntencion.ConCobro:
                return ResultadoDeReapertura.ConIntentoActivo;
            case EstadoDeIntencion.Aprobada or EstadoDeIntencion.Completada:
                return ResultadoDeReapertura.YaEnCurso;
        }

        Contacto = null;
        HuellaDeInstantanea = null;
        InstantaneaObsoleta = true;
        return ResultadoDeReapertura.Reabierta;
    }

    /// <summary>Carrito cancela el pago: solo una intención abierta sin intento activo se cierra.</summary>
    public ResultadoDeCancelacion Cancelar(DateTimeOffset ahora)
    {
        switch (Estado)
        {
            case EstadoDeIntencion.CerradaSinCobro or EstadoDeIntencion.Compensada:
                return ResultadoDeCancelacion.Cerrada;
            case EstadoDeIntencion.ConCobro or EstadoDeIntencion.Aprobada or EstadoDeIntencion.Completada:
                return ResultadoDeCancelacion.ConIntentoActivo;
        }

        CerrarSinCobro(Pagos.MotivoDeCierre.Cancelada, ahora);
        return ResultadoDeCancelacion.Cancelada;
    }

    /// <summary><c>vence_en</c> solo cierra una intención <c>abierta</c> sin intento activo (D23): una con cobro se resuelve por su intento.</summary>
    public bool CerrarPorVencimiento(DateTimeOffset ahora)
    {
        // También la cierra la expiración de la reserva de los impresos (plan §1), que puede llegar antes que la vigencia si la extensión falló.
        var reservaExpirada = TieneImpresos && PlazosDelPago.ReservaExpirada(ReservaExpiraEn, ahora);
        if (Estado != EstadoDeIntencion.Abierta || (ahora < VenceEn && !reservaExpirada))
        {
            return false;
        }

        CerrarSinCobro(Pagos.MotivoDeCierre.Vencida, ahora);
        return true;
    }

    /// <summary>Si se puede cobrar ahora: en orden, cerrada, con un intento activo o aprobada, obsoleta, o con la reserva expirada (PAG-08, PAG-17).</summary>
    public ValidacionDelCobro ValidarCobro(DateTimeOffset ahora)
    {
        switch (Estado)
        {
            case EstadoDeIntencion.CerradaSinCobro or EstadoDeIntencion.Compensada:
                return ValidacionDelCobro.IntencionCerrada;
            case EstadoDeIntencion.ConCobro or EstadoDeIntencion.Aprobada or EstadoDeIntencion.Completada:
                return ValidacionDelCobro.CobroEnCurso;
        }

        if (InstantaneaObsoleta)
        {
            return ValidacionDelCobro.InstantaneaObsoleta;
        }

        return PlazosDelPago.ReservaExpirada(ReservaExpiraEn, ahora) || ahora >= VenceEn ? ValidacionDelCobro.ReservaExpirada : ValidacionDelCobro.Permitido;
    }

    /// <summary>Si se puede cobrar, pasa a <c>con_cobro</c> (hay un intento activo); si no, devuelve el motivo y no cambia nada.</summary>
    public ValidacionDelCobro IniciarCobro(DateTimeOffset ahora)
    {
        var validacion = ValidarCobro(ahora);
        if (validacion == ValidacionDelCobro.Permitido)
        {
            Estado = EstadoDeIntencion.ConCobro;
        }

        return validacion;
    }

    /// <summary>El intento quedó pendiente de confirmación: la vigencia de la intención pasa a <c>max(vence_en, limite_pendiente_en)</c> (D23).</summary>
    public void PasarAPendiente()
    {
        ExigirEstado(EstadoDeIntencion.ConCobro);
        if (LimitePendienteEn > VenceEn)
        {
            VenceEn = LimitePendienteEn;
        }
    }

    /// <summary>
    /// El Inventario extendió la reserva de los impresos y su respuesta trae el instante real (el contrato v1 no recibe uno: lo calcula el Inventario): se anota una sola vez y no aplica a una compra
    /// solo digital. Devuelve si se anotó.
    /// </summary>
    public bool ExtenderReserva(DateTimeOffset nuevoInstante)
    {
        if (!TieneImpresos || ReservaExtendida || ReservaExpiraEn is null)
        {
            return false;
        }


        ReservaExpiraEn = nuevoInstante;
        ReservaExtendida = true;
        return true;
    }

    /// <summary>
    /// El intento se rechazó o venció sin cobro: si la reserva sigue viva (en una solo digital, si no pasaron los 30 minutos) la intención queda <c>abierta</c> y reintentable; si no, se
    /// cierra sin cobro como «rechazada sin reintento». Devuelve el estado resultante.
    /// </summary>
    public EstadoDeIntencion ResolverIntentoSinCobro(DateTimeOffset ahora)
    {
        ExigirEstado(EstadoDeIntencion.ConCobro);
        var viva = TieneImpresos ? !PlazosDelPago.ReservaExpirada(ReservaExpiraEn, ahora) : ahora < VenceEn;
        if (viva)
        {
            Estado = EstadoDeIntencion.Abierta;
        }
        else
        {
            CerrarSinCobro(Pagos.MotivoDeCierre.RechazadaSinReintento, ahora);
        }

        return Estado;
    }

    /// <summary>
    /// Registra la aprobación del intento activo: <c>con_cobro → aprobada</c> con su <c>aprobado_en</c>. Idempotente en una ya aprobada o completada. Devuelve <c>false</c> si la
    /// intención no tiene un intento activo (ya estaba cerrada o abierta): entonces el cobro es un sobrante, que nunca reabre ni completa la intención (D23).
    /// </summary>
    internal bool MarcarAprobada(DateTimeOffset ahora)
    {
        switch (Estado)
        {
            case EstadoDeIntencion.Aprobada or EstadoDeIntencion.Completada:
                return true;
            case EstadoDeIntencion.ConCobro:
                Estado = EstadoDeIntencion.Aprobada;
                AprobadoEn = ahora;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// La única forma de aprobar (D23): aplica una aprobación confirmada de la pasarela a <paramref name="intento"/> y, solo si ese intento es el activo y sigue sin resolver, pasa la
    /// intención a <c>aprobada</c>. Un aprobado de un intento rechazado o vencido, con otro intento activo, completado o ninguno, es <c>Sobrante</c> y no toca la intención; repetir el
    /// mismo cargo es <c>YaAprobado</c> y otra referencia para el mismo intento es <c>CargoDuplicado</c>.
    /// </summary>
    public ResultadoDeAprobacion AplicarAprobacion(IntentoDeCobro intento, string referenciaDePasarela, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(intento);
        if (intento.IntencionId != Id)
        {
            throw new InvalidOperationException("El intento no es de esta intención.");
        }

        var laAcepta = Estado == EstadoDeIntencion.ConCobro && intento.EstaSinResolver;
        var resultado = intento.AplicarAprobacion(referenciaDePasarela, ahora, laAcepta);
        if (resultado == ResultadoDeAprobacion.Aprobado)
        {
            MarcarAprobada(ahora);
        }

        return resultado;
    }

    /// <summary>Anota que se va a llamar a <c>reservas.confirmar</c>, <b>antes</b> de llamar (D24). Solo si hay que confirmar; devuelve si corresponde confirmar.</summary>
    public bool MarcarConfirmacionIniciada(DateTimeOffset ahora)
    {
        if (!DebeConfirmarReservas)
        {
            return false;
        }

        ConfirmacionIniciadaEn ??= ahora;
        return true;
    }

    /// <summary>Anota que las reservas de los impresos se confirmaron y guarda la fuente y los traslados de cada línea. Idempotente: la primera confirmación es la que vale.</summary>
    public void MarcarReservasConfirmadas(DateTimeOffset ahora, IReadOnlyList<ReservaConfirmadaDeLinea>? lineas = null)
    {
        ExigirEstado(EstadoDeIntencion.Aprobada);
        if (ReservasConfirmadasEn is not null)
        {
            return;
        }

        ReservasConfirmadasEn = ahora;
        LineasConfirmadas = lineas is null ? Array.Empty<ReservaConfirmadaDeLinea>() : lineas.ToArray();
    }

    /// <summary>Reclama la compra aprobada durante dos minutos para completarla; devuelve <c>false</c> si otro ejecutor la tiene reclamada.</summary>
    public bool Reclamar(DateTimeOffset ahora)
    {
        ExigirEstado(EstadoDeIntencion.Aprobada);
        if (EstaReclamada(ahora))
        {
            return false;
        }

        ReclamadaHasta = ahora + PlazosDelPago.DuracionDeLaReclamacion;
        return true;
    }

    public bool EstaReclamada(DateTimeOffset ahora) => ReclamadaHasta is { } hasta && hasta > ahora;

    public void SoltarReclamacion() => ReclamadaHasta = null;

    /// <summary>El paso (c): <c>aprobada → completada</c>. Si la intención se desvinculó mientras tanto (PAG-29) no se completa y se compensa.</summary>
    public ResultadoDeCompletar Completar(DateTimeOffset ahora)
    {
        if (Estado == EstadoDeIntencion.Completada)
        {
            return ResultadoDeCompletar.Completada;
        }

        ExigirEstado(EstadoDeIntencion.Aprobada);
        if (EstaDesvinculada)
        {
            return ResultadoDeCompletar.Desvinculada;
        }

        Estado = EstadoDeIntencion.Completada;
        ReclamadaHasta = null;
        return ResultadoDeCompletar.Completada;
    }

    /// <summary>La compra aprobada no se pudo completar: <c>aprobada → compensada</c> y se elimina el contacto (PAG-27). No se compensa una completada.</summary>
    public void Compensar(DateTimeOffset ahora)
    {
        if (Estado == EstadoDeIntencion.Compensada)
        {
            return;
        }

        ExigirEstado(EstadoDeIntencion.Aprobada);
        Estado = EstadoDeIntencion.Compensada;
        CerradaEn = ahora;
        ReclamadaHasta = null;
        LiberarContacto(ahora);
        HuellaDeInstantanea = null;
    }

    /// <summary>Elimina el contacto de entrega y la huella de la instantánea (PAG-27). Idempotente: devuelve si había algo que eliminar.</summary>
    public bool LiberarContacto(DateTimeOffset ahora)
    {
        if (Contacto is null)
        {
            return false;
        }

        Contacto = null;
        ContactoLiberadoEn = ahora;
        HuellaDeInstantanea = null;
        return true;
    }

    /// <summary>El contacto vence a los 30 días de creada la intención, haya o no pedido (PAG-27).</summary>
    public bool ContactoVencido(DateTimeOffset ahora) => Contacto is not null && ahora >= CreadaEn + PlazosDelPago.RetencionDelContacto;

    /// <summary>
    /// Desvincula la intención de la cuenta (PAG-26): se anulan la cuenta, los textos aceptados, la entrega, la edición, el título y el ISBN de las líneas, el contacto y la huella;
    /// quedan los importes, las fechas y los estados. Idempotente: devuelve si hizo el cambio.
    /// </summary>
    public bool Desvincular(DateTimeOffset ahora)
    {
        if (EstaDesvinculada)
        {
            return false;
        }

        CuentaId = null;
        DesvinculadaEn = ahora;
        Terminos = null;
        Privacidad = null;
        ReembolsoDigital = null;
        TipoDeEntrega = null;
        RegionDeEntrega = null;
        CodigoDeSucursalDeRecojo = null;
        Lineas = Lineas.Select(l => l with { EdicionId = null, Titulo = null, Isbn = null }).ToArray();
        HuellaDeInstantanea = null;
        if (Contacto is not null)
        {
            ContactoLiberadoEn = ahora;
            Contacto = null;
        }

        return true;
    }

    /// <summary>Sin el contacto ni los títulos.</summary>
    public override string ToString() => $"IntencionDePago {{ Id = {Id}, Estado = {Estado} }}";

    private void Aplicar(InstantaneaDeIntencion instantanea, DateTimeOffset? reservaExpiraEn, DateTimeOffset ahora)
    {
        var tieneImpresos = instantanea.TieneImpresos;
        var vence = PlazosDelPago.VenceEn(ahora, tieneImpresos, reservaExpiraEn);

        CuentaId = instantanea.CuentaId;
        TieneImpresos = tieneImpresos;
        TipoDeEntrega = instantanea.Entrega;
        CodigoDeSucursalDeRecojo = instantanea.CodigoDeSucursalDeRecojo;
        RegionDeEntrega = instantanea.RegionDeEntrega;
        Total = instantanea.Total;
        Envio = instantanea.Envio;
        HuellaDeInstantanea = instantanea.Huella;
        InstantaneaObsoleta = false;
        Lineas = instantanea.Lineas;
        Terminos = instantanea.Terminos;
        Privacidad = instantanea.Privacidad;
        ReembolsoDigital = instantanea.ReembolsoDigital;
        Contacto = instantanea.Contacto;
        ContactoLiberadoEn = null;
        IniciadaEn = ahora;
        VenceEn = vence;
        LimitePendienteEn = PlazosDelPago.LimiteDePendienteEn(ahora);
        ReservaExpiraEn = tieneImpresos ? reservaExpiraEn : null;
        ReservaExtendida = false;
    }

    private void CerrarSinCobro(MotivoDeCierre motivo, DateTimeOffset ahora)
    {
        Estado = EstadoDeIntencion.CerradaSinCobro;
        MotivoDeCierre = motivo;
        CerradaEn = ahora;
        LiberarContacto(ahora);
        HuellaDeInstantanea = null;
    }

    private void ExigirEstado(EstadoDeIntencion esperado)
    {
        if (Estado != esperado)
        {
            throw new InvalidOperationException($"La operación exige una intención en el estado {esperado}.");
        }
    }
}
