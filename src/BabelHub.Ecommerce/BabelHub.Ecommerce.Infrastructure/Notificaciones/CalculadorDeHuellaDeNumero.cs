using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Notificaciones.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Notificaciones;

namespace BabelHub.Ecommerce.Infrastructure.Notificaciones;

/// <summary>
/// Las claves de la huella de un número desactivado (T13; NOT-13): una <b>versión actual</b> y, durante una rotación, la anterior; cada una de al menos 32 bytes. Esta clave <b>queda fuera de la rotación de la huella de
/// <c>Plataforma</c></b> (PLA-43): el número no se conserva y no se puede recalcular, así que perder una versión reactivaría a quien pidió no recibir avisos. Se conserva una versión vieja mientras existan huellas de ella.
/// </summary>
public sealed class OpcionesDeHuellaDeNumero
{
    public const int LongitudMinimaDeLaClave = 32;

    public OpcionesDeHuellaDeNumero(int versionActual, byte[] claveActual, int? versionAnterior = null, byte[]? claveAnterior = null)
    {
        ArgumentNullException.ThrowIfNull(claveActual);
        if (versionActual < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(versionActual), "La versión de la clave empieza en 1.");
        }

        if (claveActual.Length < LongitudMinimaDeLaClave)
        {
            throw new ArgumentException($"La clave debe tener al menos {LongitudMinimaDeLaClave} bytes.", nameof(claveActual));
        }

        Versiones = [new(versionActual, claveActual)];
        if (versionAnterior is not null || claveAnterior is not null)
        {
            if (versionAnterior is not { } anterior || claveAnterior is null || anterior < 1 || anterior == versionActual)
            {
                throw new ArgumentException("La clave anterior necesita su propia versión, distinta de la actual.", nameof(versionAnterior));
            }

            if (claveAnterior.Length < LongitudMinimaDeLaClave || claveAnterior.AsSpan().SequenceEqual(claveActual))
            {
                throw new ArgumentException("La clave anterior debe ser de al menos 32 bytes y distinta de la actual.", nameof(claveAnterior));
            }

            Versiones.Add(new(anterior, claveAnterior));
        }
    }

    internal List<KeyValuePair<int, byte[]>> Versiones { get; } = [];

    public int VersionActual => Versiones[0].Key;

    public override string ToString() => "[claves de la huella de número]";
}

/// <summary>
/// <see cref="IHuellasDeNumeros"/> (T13; NOT-13, NOT-14): la huella de un número E.164 es un HMAC-SHA256 con una clave y un propósito propios y la <b>versión</b> de la clave; el número se normaliza antes (la desactivación no se elude
/// cambiando su formato). <see cref="Vigentes"/> da la huella con <b>todas</b> las versiones, para reconocer un número desactivado con una versión anterior. El sujeto del tope por número usa en cambio la huella de <c>Plataforma</c>
/// (<see cref="ICalculadorDeHuella"/>) con su propio propósito: sus contadores sí rotan con ella.
/// </summary>
public sealed class CalculadorDeHuellaDeNumero(OpcionesDeHuellaDeNumero opciones, ICalculadorDeHuella plataforma) : IHuellasDeNumeros
{
    /// <summary>El propósito de la huella del sujeto del tope en el motor de límites.</summary>
    public const string PropositoDelTope = "tope-de-avisos-por-numero";

    private const string PropositoDeLaDesactivacion = "babelhub.notificaciones.numero-desactivado";

    public HuellaDeNumero Actual(NumeroDeDestino numero) => Calcular(opciones.Versiones[0], numero);

    public IReadOnlyList<HuellaDeNumero> Vigentes(NumeroDeDestino numero) => [.. opciones.Versiones.Select(v => Calcular(v, numero))];

    public HuellaDeSujeto ParaElTope(NumeroDeDestino numero) => plataforma.DeSujeto(numero.E164, PropositoDelTope);

    private static HuellaDeNumero Calcular(KeyValuePair<int, byte[]> version, NumeroDeDestino numero) =>
        new(version.Key, Convert.ToHexString(HMACSHA256.HashData(version.Value, Encoding.UTF8.GetBytes(PropositoDeLaDesactivacion + "|" + numero.E164))).ToLowerInvariant());
}

/// <summary>
/// <see cref="IContactoDeSucursal"/> sobre el cliente REST del Inventario (T16; NOT-09): el número de contacto de una sucursal por <c>GET /sucursales/{codigo}/contacto-de-notificacion</c> con la credencial de alcance
/// <c>lectura-de-contacto</c>. La espera de 10 segundos, el cortacircuitos y la credencial son del cliente del Inventario. <c>INV-NO-ENCONTRADO</c> (código inexistente o sin contacto) e <c>INV-NO-AUTENTICADO</c> son errores permanentes que
/// cuentan como un intento; el servicio caído, el límite excedido o una respuesta que el contrato no admite dejan el aviso pendiente <b>sin consumir un intento</b>. El número nunca se registra.
/// </summary>
public sealed class ClienteDeContactoDelInventario(IClienteDelInventario inventario) : IContactoDeSucursal
{
    public async Task<ContactoDeSucursal> Resolver(string codigoDeSucursal, CancellationToken cancelacion = default)
    {
        if (!PlantillaDeAviso.ReferenciaValida(codigoDeSucursal) || !codigoDeSucursal.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            // Un código que no tiene la forma de una sucursal no es una sucursal: no se pregunta al Inventario.
            return new ContactoDeSucursal(EstadoDelContactoDeSucursal.NoEncontrada);
        }

        var resultado = await inventario.ConsultarContactoDeSucursal(codigoDeSucursal, cancelacion).ConfigureAwait(false);
        return resultado.Estado switch
        {
            EstadoDeLlamadaAlInventario.Exitosa => new ContactoDeSucursal(EstadoDelContactoDeSucursal.Encontrado, resultado.Valor.Numero),
            EstadoDeLlamadaAlInventario.Rechazada when resultado.EstadoHttp == 404 => new ContactoDeSucursal(EstadoDelContactoDeSucursal.NoEncontrada),
            EstadoDeLlamadaAlInventario.Rechazada when resultado.EstadoHttp is 401 or 403 => new ContactoDeSucursal(EstadoDelContactoDeSucursal.NoAutenticado),
            _ => new ContactoDeSucursal(EstadoDelContactoDeSucursal.NoDisponible),
        };
    }
}
