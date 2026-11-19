using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Infrastructure.Notificaciones;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.Configuration;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>Cómo sale un aviso (NOT-11): por n8n, simulado (solo desarrollo y pruebas) o nulo (ensayo y restauración, que nunca envían).</summary>
public enum ModoDelEnvioDeAvisos
{
    N8n,
    Simulado,
    Nulo,
}

/// <summary>
/// La configuración de Notificaciones (T14, T15, T13; NOT-11, NOT-12, NOT-13): cómo sale el aviso, la dirección del webhook de n8n con su clave saliente y la clave de la huella de los números desactivados. <b>Todo secreto sale del
/// entorno</b> (<c>BABELHUB__Notificaciones__…</c>) y los problemas nombran la variable, nunca su valor. La clave que firma los estados de entrega de n8n (sentido entrante) es la de la sección <c>Integraciones</c> de
/// <c>Plataforma</c> (<c>BABELHUB__Integraciones__n8n__Secreto</c>, con <c>SecretoAnterior</c> durante una rotación). En el ensayo y al restaurar el envío es siempre el nulo, aunque la configuración diga otra cosa.
/// </summary>
public sealed class OpcionesDeNotificaciones
{
    public const string RutaDelEnvio = "Notificaciones:Envio";
    public const string RutaDelWebhook = "Notificaciones:N8n:Webhook";
    public const string RutaDeLaClaveSaliente = "Notificaciones:N8n:ClaveSaliente";
    public const string RutaDeLaClaveDeHuella = "Notificaciones:ClaveDeHuellaDeNumero";
    public const string RutaDeLaClaveDeHuellaAnterior = "Notificaciones:ClaveDeHuellaDeNumeroAnterior";

    public OpcionesDeNotificaciones(ModoDelEnvioDeAvisos envio, OpcionesDeHuellaDeNumero huella, OpcionesDeEnvioPorN8n? n8n)
    {
        ArgumentNullException.ThrowIfNull(huella);
        if (envio == ModoDelEnvioDeAvisos.N8n && n8n is null)
        {
            throw new ArgumentException("El envío por n8n necesita su webhook y su clave.", nameof(n8n));
        }

        Envio = envio;
        Huella = huella;
        N8n = n8n;
    }

    public ModoDelEnvioDeAvisos Envio { get; }

    public OpcionesDeHuellaDeNumero Huella { get; }

    public OpcionesDeEnvioPorN8n? N8n { get; }

    /// <summary>El proveedor cuya clave de firma entrante hay que exigir en <c>Integraciones</c> (vacío si no se envía por n8n): se decide leyendo la configuración antes de cargarla.</summary>
    public static IReadOnlyCollection<string> ProveedoresDeFirma(IConfiguration configuracion, bool soloEnModoNormal)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        return soloEnModoNormal && string.Equals(configuracion[RutaDelEnvio]?.Trim(), "n8n", StringComparison.OrdinalIgnoreCase) ? ["n8n"] : [];
    }

    public static OpcionesDeNotificaciones Leer(IConfiguration configuracion, bool esProduccion, bool soloEnModoNormal)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var lector = new LectorDeSecretos(configuracion);
        var huella = LeerLaHuella(configuracion, lector, esProduccion);

        var envio = ModoDelEnvioDeAvisos.Nulo;
        OpcionesDeEnvioPorN8n? n8n = null;
        if (soloEnModoNormal)
        {
            var texto = configuracion[RutaDelEnvio]?.Trim();
            if (string.IsNullOrEmpty(texto))
            {
                // Sin elegir, en desarrollo y pruebas se simula; en producción el arranque no sigue sin decidirlo.
                if (esProduccion)
                {
                    lector.Problema(RutaDelEnvio, "falta: en producción se elige «n8n»");
                }
                else
                {
                    envio = ModoDelEnvioDeAvisos.Simulado;
                }
            }
            else if (string.Equals(texto, "n8n", StringComparison.OrdinalIgnoreCase))
            {
                envio = ModoDelEnvioDeAvisos.N8n;
                n8n = LeerN8n(configuracion, lector, esProduccion);
            }
            else if (string.Equals(texto, "simulado", StringComparison.OrdinalIgnoreCase))
            {
                envio = ModoDelEnvioDeAvisos.Simulado;
                if (esProduccion)
                {
                    lector.Problema(RutaDelEnvio, "el envío simulado no vale en producción");
                }
            }
            else
            {
                lector.Problema(RutaDelEnvio, "no es un valor conocido (n8n o simulado)");
            }
        }

        lector.ExigirValida();
        return new OpcionesDeNotificaciones(envio, huella!, n8n);
    }

    private static OpcionesDeEnvioPorN8n? LeerN8n(IConfiguration configuracion, LectorDeSecretos lector, bool esProduccion)
    {
        var direccion = lector.Obligatorio(RutaDelWebhook);
        var clave = lector.Obligatorio(RutaDeLaClaveSaliente);
        if (clave is not null)
        {
            lector.Comprobar(RutaDeLaClaveSaliente, clave);
        }

        if (direccion is null || clave is null)
        {
            return null;
        }

        try
        {
            return new OpcionesDeEnvioPorN8n(new Uri(direccion, UriKind.Absolute), Encoding.UTF8.GetBytes(clave));
        }
        catch (Exception excepcion) when (excepcion is ArgumentException or UriFormatException)
        {
            // El mensaje describe la regla incumplida, nunca el valor.
            lector.Problema(RutaDelWebhook, excepcion is UriFormatException ? "no es una dirección válida" : "no cumple las reglas del webhook de n8n (http o https absoluta, http solo interna)");
            _ = esProduccion;
            _ = configuracion;
            return null;
        }
    }

    private static OpcionesDeHuellaDeNumero? LeerLaHuella(IConfiguration configuracion, LectorDeSecretos lector, bool esProduccion)
    {
        var actual = configuracion[RutaDeLaClaveDeHuella];
        var anterior = configuracion[RutaDeLaClaveDeHuellaAnterior];
        if (string.IsNullOrWhiteSpace(actual))
        {
            if (esProduccion)
            {
                lector.Problema(RutaDeLaClaveDeHuella, "falta");
                return null;
            }

            // Una clave de desarrollo, distinta en cada arranque: las desactivaciones de una sesión de desarrollo no sobreviven al reinicio. En producción no se llama.
            return new OpcionesDeHuellaDeNumero(1, RandomNumberGenerator.GetBytes(OpcionesDeHuellaDeNumero.LongitudMinimaDeLaClave));
        }

        var (versionActual, claveActual) = Analizar(actual, RutaDeLaClaveDeHuella, lector);
        var hayAnterior = !string.IsNullOrWhiteSpace(anterior);
        var (versionAnterior, claveAnterior) = hayAnterior ? Analizar(anterior!, RutaDeLaClaveDeHuellaAnterior, lector) : (0, null);
        if (claveActual is null || (hayAnterior && claveAnterior is null))
        {
            return null;
        }

        try
        {
            return hayAnterior ? new OpcionesDeHuellaDeNumero(versionActual, claveActual, versionAnterior, claveAnterior) : new OpcionesDeHuellaDeNumero(versionActual, claveActual);
        }
        catch (Exception excepcion) when (excepcion is ArgumentException)
        {
            lector.Problema(RutaDeLaClaveDeHuellaAnterior, "la clave anterior debe tener su propia versión y ser distinta de la actual");
            return null;
        }
    }

    /// <summary>Lee <c>versión:clave</c>; la clave es de al menos 32 bytes y varía.</summary>
    private static (int Version, byte[]? Clave) Analizar(string valor, string ruta, LectorDeSecretos lector)
    {
        var corte = valor.IndexOf(':', StringComparison.Ordinal);
        if (corte < 1 || !int.TryParse(valor.AsSpan(0, corte), NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
        {
            lector.Problema(ruta, "debe tener la forma «versión:clave», con la versión un número desde 1");
            return (0, null);
        }

        var clave = valor[(corte + 1)..];
        var problemas = lector.Problemas.Count;
        lector.Comprobar(ruta, clave);
        return lector.Problemas.Count > problemas ? (version, null) : (version, Encoding.UTF8.GetBytes(clave));
    }

    public override string ToString() => "[opciones de notificaciones]";
}
