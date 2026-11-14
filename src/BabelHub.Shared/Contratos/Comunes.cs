namespace BabelHub.Shared.Contratos;

/// <summary>Versión de los contratos entre servicios. Un cambio incompatible crea <c>v2</c> y <c>v1</c> convive al menos 30 días (PLA-12).</summary>
public static class VersionDeContrato
{
    public const string V1 = "v1";
}

/// <summary>Prefijos de ruta de cada servicio: <c>/api/&lt;servicio&gt;/v1/&lt;recurso&gt;</c> (RNF28).</summary>
public static class RutasDeServicio
{
    public const string Inventario = "/api/inventario/" + VersionDeContrato.V1;

    public const string Ecommerce = "/api/ecommerce/" + VersionDeContrato.V1;
}

/// <summary>Cabeceras HTTP que forman parte de los contratos entre servicios.</summary>
public static class CabecerasDeServicio
{
    /// <summary>Clave de idempotencia de las operaciones con efecto repetible (reservas, cierres, reingresos, ajustes).</summary>
    public const string ClaveDeIdempotencia = "Idempotency-Key";

    /// <summary>Credencial de servicio del alcance de la operación.</summary>
    public const string CredencialDeServicio = "X-Credencial-De-Servicio";

    /// <summary>Credencial del alcance «origen reenviado» (SEG-17, SEG-32).</summary>
    public const string CredencialDeOrigen = "X-Credencial-De-Origen";

    /// <summary>Dirección del cliente final, solo válida con la credencial de origen.</summary>
    public const string OrigenReenviado = "X-Origen-Reenviado";

    /// <summary>Identificador de seguimiento de la respuesta (igual al <c>traceId</c> de los errores).</summary>
    public const string Seguimiento = "X-Trace-Id";

    /// <summary>Espera sugerida de un <c>429</c>, en segundos.</summary>
    public const string EsperaSugerida = "Retry-After";
}

/// <summary>Forma de la clave de idempotencia: de 16 a 80 caracteres de letras, dígitos, guion y guion bajo.</summary>
public static class ClaveDeIdempotencia
{
    public const int LongitudMinima = 16;

    public const int LongitudMaxima = 80;

    public const string Patron = "^[A-Za-z0-9_-]{16,80}$";
}

/// <summary>Topes que forman parte de los contratos (los valida cada servicio que los recibe).</summary>
public static class LimitesDeContrato
{
    public const int TamanoDePaginaPorDefecto = 20;

    public const int TamanoMaximoDePagina = 100;

    /// <summary>Ediciones por consulta de disponibilidad o de existencias.</summary>
    public const int MaximoDeEdicionesPorConsulta = 50;

    /// <summary>Líneas de una reserva o de una consulta previa (el tope del carrito, CAR-29).</summary>
    public const int MaximoDeLineasPorReserva = 50;

    /// <summary>Cantidad de un ingreso de mercadería: entero entre 1 y 5 000 (AJU-19, CAT-23).</summary>
    public const int IngresoMinimo = 1;

    public const int IngresoMaximo = 5000;

    public const int LongitudMaximaDeNota = 500;

    public const int LongitudMaximaDeReferencia = 80;

    public const int LongitudMaximaDeMotivo = 300;

    public const int LongitudMaximaDeGuia = 60;

    /// <summary>Días que cuentan las unidades vendidas (FUE-21).</summary>
    public const int DiasDeUnidadesVendidas = 30;
}
