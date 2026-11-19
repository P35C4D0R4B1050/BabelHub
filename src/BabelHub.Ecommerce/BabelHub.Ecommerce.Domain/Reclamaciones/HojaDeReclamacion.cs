namespace BabelHub.Ecommerce.Domain.Reclamaciones;

public enum ResultadoDeTransicion
{
    Hecha,

    /// <summary>La hoja no está pendiente, o ya se anonimizó: no se responde ni se descarta (REC-16, D13).</summary>
    EstadoInvalido,
}

/// <summary>El resultado de tomar el arrendamiento del aviso para reintentar el envío (REC-13, D11).</summary>
public enum ResultadoDeReintento
{
    Tomado,

    /// <summary>Otro envío (el primero tras responder, o un reintento) tiene el arrendamiento.</summary>
    EnCurso,

    YaEnviado,

    NoRespondida,

    Anonimizada,
}

/// <summary>
/// La hoja de reclamación (REC-03, REC-06): inmutable una vez registrada. Solo la respuesta, el estado, el aviso y la marca de cuenta cancelada se agregan, sin tocar lo que el consumidor
/// escribió; la base de datos lo garantiza también (D02). Una hoja se responde o se descarta solo desde <c>pendiente</c> y sin anonimizar. El Dominio no lee la hora ni envía el correo.
/// </summary>
public sealed class HojaDeReclamacion
{
    public const int LongitudMaximaDeLaRespuesta = 3000;

    /// <summary>El texto fijo en que se redactan el detalle y la respuesta de una hoja anonimizada (REC-17, D05).</summary>
    public const string TextoRedactado = "[redactado]";

    /// <summary>El arrendamiento del aviso: cubre el primer envío tras responder y cada reintento (D11).</summary>
    public static readonly TimeSpan ArrendamientoDelAviso = TimeSpan.FromSeconds(60);

    private HojaDeReclamacion(
        Guid id,
        NumeroDeHoja numero,
        TipoDeHoja tipo,
        DatosDelConsumidor datos,
        string bienContratado,
        decimal montoReclamado,
        string detalle,
        string? pedidoReferencia,
        string? contenidoHash,
        DateTimeOffset fechaRegistro,
        string claveHash)
    {
        Id = id;
        Numero = numero;
        Tipo = tipo;
        Datos = datos;
        BienContratado = bienContratado;
        MontoReclamado = montoReclamado;
        Detalle = detalle;
        PedidoReferencia = pedidoReferencia;
        ContenidoHash = contenidoHash;
        FechaRegistro = fechaRegistro;
        ClaveHash = claveHash;
        Estado = EstadoDeHoja.Pendiente;
    }

    public Guid Id { get; }

    public NumeroDeHoja Numero { get; }

    public TipoDeHoja Tipo { get; }

    public DatosDelConsumidor Datos { get; private set; }

    public string BienContratado { get; }

    public decimal MontoReclamado { get; }

    public string Detalle { get; private set; }

    public string? PedidoReferencia { get; }

    /// <summary>El resumen del contenido para comparar una clave repetida; nulo tras anonimizar.</summary>
    public string? ContenidoHash { get; }

    public DateTimeOffset FechaRegistro { get; }

    /// <summary>El resumen de la clave de idempotencia del formulario.</summary>
    public string ClaveHash { get; }

    public EstadoDeHoja Estado { get; private set; }

    public string? Respuesta { get; private set; }

    public DateTimeOffset? RespondidaEn { get; private set; }

    public Guid? RespondidaPor { get; private set; }

    public AvisoDeRespuesta? AvisoDeRespuesta { get; private set; }

    public DateTimeOffset? AvisoEnviadoEn { get; private set; }

    public DateTimeOffset? AvisoEnCursoHasta { get; private set; }

    public DateTimeOffset? DescartadaEn { get; private set; }

    public bool DeCuentaCancelada { get; private set; }

    public DateTimeOffset? AnonimizadaEn { get; private set; }

    public bool EstaPendiente => Estado == EstadoDeHoja.Pendiente;

    public bool EstaAnonimizada => AnonimizadaEn is not null;

    /// <summary>El plazo de respuesta que se informó en la constancia: 15 días calendario desde el día de Lima del registro.</summary>
    public DateOnly FechaLimiteDeRespuesta => PlazoDeRespuesta.FechaLimite(FechaRegistro);

    /// <summary>Registra una hoja nueva, <c>pendiente</c>. El año del número debe ser el de Lima de la fecha de registro.</summary>
    public static HojaDeReclamacion Registrar(
        Guid id,
        NumeroDeHoja numero,
        TipoDeHoja tipo,
        DatosDelConsumidor datos,
        string bienContratado,
        decimal montoReclamado,
        string detalle,
        string? pedidoReferencia,
        string contenidoHash,
        string claveHash,
        DateTimeOffset fechaRegistro)
    {
        ArgumentNullException.ThrowIfNull(datos);
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(contenidoHash) || string.IsNullOrWhiteSpace(claveHash))
        {
            throw new ArgumentException("La hoja necesita su identificador, el resumen de su contenido y el de su clave.");
        }

        if (string.IsNullOrWhiteSpace(bienContratado) || string.IsNullOrWhiteSpace(detalle))
        {
            throw new ArgumentException("La hoja necesita el bien contratado y el detalle.");
        }

        if (montoReclamado < 0m || decimal.Round(montoReclamado, 2, MidpointRounding.ToZero) != montoReclamado)
        {
            throw new ArgumentOutOfRangeException(nameof(montoReclamado), "El monto reclamado no es negativo y tiene a lo más dos decimales.");
        }

        if (numero.Anio != DiaDeLima.Anio(fechaRegistro))
        {
            throw new ArgumentException("El número de la hoja es del año de Lima de su registro.", nameof(numero));
        }

        return new HojaDeReclamacion(
            id,
            numero,
            tipo,
            datos,
            bienContratado.Trim(),
            montoReclamado,
            detalle.Trim(),
            string.IsNullOrWhiteSpace(pedidoReferencia) ? null : pedidoReferencia.Trim(),
            contenidoHash.Trim(),
            fechaRegistro,
            claveHash.Trim());
    }

    /// <summary>Reconstruye una hoja guardada.</summary>
    public static HojaDeReclamacion Rehidratar(
        Guid id,
        NumeroDeHoja numero,
        TipoDeHoja tipo,
        DatosDelConsumidor datos,
        string bienContratado,
        decimal montoReclamado,
        string detalle,
        string? pedidoReferencia,
        string? contenidoHash,
        DateTimeOffset fechaRegistro,
        string claveHash,
        EstadoDeHoja estado,
        string? respuesta,
        DateTimeOffset? respondidaEn,
        Guid? respondidaPor,
        AvisoDeRespuesta? avisoDeRespuesta,
        DateTimeOffset? avisoEnviadoEn,
        DateTimeOffset? avisoEnCursoHasta,
        DateTimeOffset? descartadaEn,
        bool deCuentaCancelada,
        DateTimeOffset? anonimizadaEn)
    {
        var hoja = new HojaDeReclamacion(id, numero, tipo, datos, bienContratado, montoReclamado, detalle, pedidoReferencia, contenidoHash, fechaRegistro, claveHash)
        {
            Estado = estado,
            Respuesta = respuesta,
            RespondidaEn = respondidaEn,
            RespondidaPor = respondidaPor,
            AvisoDeRespuesta = avisoDeRespuesta,
            AvisoEnviadoEn = avisoEnviadoEn,
            AvisoEnCursoHasta = avisoEnCursoHasta,
            DescartadaEn = descartadaEn,
            DeCuentaCancelada = deCuentaCancelada,
            AnonimizadaEn = anonimizadaEn,
        };
        return hoja;
    }

    /// <summary>
    /// Responde una hoja pendiente (REC-07): guarda la respuesta, pasa a <c>respondido</c> con el aviso <c>pendiente</c> y toma el arrendamiento del aviso, que cubre el primer envío. Una
    /// hoja respondida, descartada o ya anonimizada devuelve <see cref="ResultadoDeTransicion.EstadoInvalido"/> y no cambia.
    /// </summary>
    public ResultadoDeTransicion Responder(string respuesta, Guid respondidaPor, DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(respuesta) || respuesta.Trim().Length > LongitudMaximaDeLaRespuesta)
        {
            throw new ArgumentException("La respuesta no está vacía y tiene a lo más 3 000 caracteres.", nameof(respuesta));
        }

        if (respondidaPor == Guid.Empty)
        {
            throw new ArgumentException("La respuesta necesita a quien la da.", nameof(respondidaPor));
        }

        if (!EstaPendiente || EstaAnonimizada)
        {
            return ResultadoDeTransicion.EstadoInvalido;
        }

        Estado = EstadoDeHoja.Respondido;
        Respuesta = respuesta.Trim();
        RespondidaEn = ahora;
        RespondidaPor = respondidaPor;
        AvisoDeRespuesta = Reclamaciones.AvisoDeRespuesta.Pendiente;
        AvisoEnCursoHasta = ahora + ArrendamientoDelAviso;
        return ResultadoDeTransicion.Hecha;
    }

    /// <summary>Descarta una hoja pendiente por ser spam (REC-12): la marca, no la elimina. Una no pendiente o anonimizada devuelve <see cref="ResultadoDeTransicion.EstadoInvalido"/>.</summary>
    public ResultadoDeTransicion Descartar(DateTimeOffset ahora)
    {
        if (!EstaPendiente || EstaAnonimizada)
        {
            return ResultadoDeTransicion.EstadoInvalido;
        }

        Estado = EstadoDeHoja.Descartado;
        DescartadaEn = ahora;
        return ResultadoDeTransicion.Hecha;
    }

    /// <summary>
    /// Toma el arrendamiento corto del aviso para reintentar el envío (REC-13, D11): solo de una hoja respondida y sin anonimizar, con el aviso <c>pendiente</c> y sin otro envío en curso.
    /// </summary>
    public ResultadoDeReintento TomarArrendamientoDeAviso(DateTimeOffset ahora)
    {
        if (Estado != EstadoDeHoja.Respondido)
        {
            return ResultadoDeReintento.NoRespondida;
        }

        if (EstaAnonimizada)
        {
            return ResultadoDeReintento.Anonimizada;
        }

        if (AvisoDeRespuesta == Reclamaciones.AvisoDeRespuesta.Enviado)
        {
            return ResultadoDeReintento.YaEnviado;
        }

        if (AvisoEnCursoHasta is { } hasta && hasta > ahora)
        {
            return ResultadoDeReintento.EnCurso;
        }

        AvisoEnCursoHasta = ahora + ArrendamientoDelAviso;
        return ResultadoDeReintento.Tomado;
    }

    /// <summary>El correo se envió o se encoló: el aviso pasa a <c>enviado</c> y se suelta el arrendamiento. Idempotente.</summary>
    public void MarcarAvisoEnviado(DateTimeOffset ahora)
    {
        if (Estado != EstadoDeHoja.Respondido)
        {
            throw new InvalidOperationException("Solo se avisa la respuesta de una hoja respondida.");
        }

        if (AvisoDeRespuesta == Reclamaciones.AvisoDeRespuesta.Enviado)
        {
            return;
        }

        AvisoDeRespuesta = Reclamaciones.AvisoDeRespuesta.Enviado;
        AvisoEnviadoEn = ahora;
        AvisoEnCursoHasta = null;
    }

    /// <summary>El envío falló o fue suprimido: se suelta el arrendamiento y el aviso sigue <c>pendiente</c> para reintentarlo (REC-13).</summary>
    public void LiberarArrendamientoDeAviso() => AvisoEnCursoHasta = null;

    /// <summary>La cancelación de la cuenta marca la hoja (REC-20): solo de falso a verdadero, sin tocar nada más. Devuelve si hizo el cambio.</summary>
    public bool MarcarDeCuentaCancelada()
    {
        if (DeCuentaCancelada)
        {
            return false;
        }

        DeCuentaCancelada = true;
        return true;
    }

    /// <summary>Sin datos personales, detalle ni respuesta.</summary>
    public override string ToString() => $"HojaDeReclamacion {{ Numero = {Numero}, Estado = {Estado} }}";
}
