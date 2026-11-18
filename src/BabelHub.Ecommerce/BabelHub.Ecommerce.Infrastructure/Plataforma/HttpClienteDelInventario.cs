using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Inventario;
using BabelHub.Shared.Errores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;

namespace BabelHub.Ecommerce.Infrastructure.Plataforma;

/// <summary>
/// <see cref="IClienteDelInventario"/> sobre HTTP: traduce entre los modelos propios del puerto y los DTO de <c>BabelHub.Shared</c> (contratos v1 congelados en ECO-0) y llama con el alcance de cada operación
/// (<see cref="OpcionesDeLaSolicitudDeServicio"/>); los manejadores de salida ponen la credencial de servicio y, en las operaciones de usuario, el token verificable de la persona (nunca una cabecera de actor). Los
/// reintentos y el cortacircuitos son los de <see cref="ClientesHttpDeServicio"/> (SEG-14): solo se reintentan las lecturas, los <c>PUT</c> y los <c>POST</c> con clave de idempotencia.
/// </summary>
/// <remarks>
/// <b>Una falla del Inventario nunca es una excepción</b>: un corte, un 5xx, un plazo vencido, un cortacircuitos abierto, una respuesta demasiado grande, que no es JSON o con valores que el contrato no admite son
/// <see cref="EstadoDeLlamadaAlInventario.NoDisponible"/>; un 429 es <see cref="EstadoDeLlamadaAlInventario.LimiteExcedido"/> y un 400, 401, 403, 404, 409 o 422, <see cref="EstadoDeLlamadaAlInventario.Rechazada"/> con
/// el <c>code</c> estable del problema. Nunca se registra el cuerpo, el token ni el texto del error remoto, y cada respuesta se valida contra lo que se pidió (un ingreso de otra edición no es una respuesta válida).
/// </remarks>
public sealed class HttpClienteDelInventario(IHttpClientFactory fabrica) : IClienteDelInventario
{
    /// <summary>El cliente con nombre del Inventario: el mismo al que la Api le agrega el manejador del token de la sesión (<c>TokenDelUsuarioDeLaSesionExtensiones.ClienteDelInventario</c>; una prueba lo vigila).</summary>
    public const string NombreDelCliente = "inventario";

    public const int MaximoDeBytesDeLaRespuesta = 1_048_576;

    private const int MaximoDeBytesDelProblema = 16_384;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.Strict, AllowTrailingCommas = false };

    private static readonly Regex CodigoDeError = new("^(INV|ECO)-[A-Z0-9]+(-[A-Z0-9]+)*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));

    private static readonly Regex ClaveValida = new("^[A-Za-z0-9_-]{16,80}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));

    public async Task<ResultadoDelInventario> RegistrarEdicion(EdicionARegistrar edicion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicion);
        if (edicion.EdicionId == Guid.Empty || !FormatosDeEdicion.Todos.Contains(edicion.Formato))
        {
            throw new ArgumentException("La edición necesita un identificador y un formato del contrato (impreso o digital).", nameof(edicion));
        }

        var cuerpo = JsonSerializer.Serialize(new RegistrarEdicionDto(edicion.Formato), Json);
        var resultado = await Pedir(
            HttpMethod.Put,
            $"{RutasDeServicio.Inventario}/ediciones/{edicion.EdicionId:D}",
            AlcanceDeServicio.OperacionDeUsuario,
            cuerpo,
            claveDeIdempotencia: null,
            conOrigen: false,
            _ => true,
            cancelacion).ConfigureAwait(false);
        return resultado.Exitosa ? ResultadoDelInventario.Correcta : ComoSinValor(resultado);
    }

    public Task<ResultadoDelInventario<DisponibilidadDeVariasEdiciones>> ConsultarDisponibilidad(IReadOnlyList<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);
        if (edicionIds.Count is 0 or > LimitesDeContrato.MaximoDeEdicionesPorConsulta || edicionIds.Any(id => id == Guid.Empty) || edicionIds.Distinct().Count() != edicionIds.Count)
        {
            throw new ArgumentException($"La consulta lleva de 1 a {LimitesDeContrato.MaximoDeEdicionesPorConsulta} ediciones distintas.", nameof(edicionIds));
        }

        var cuerpo = JsonSerializer.Serialize(new ConsultaDeDisponibilidadDto([.. edicionIds]), Json);
        var pedidas = edicionIds.ToHashSet();
        return Pedir(
            HttpMethod.Post,
            $"{RutasDeServicio.Inventario}/disponibilidad/consultas",
            AlcanceDeServicio.LecturaDeDisponibilidad,
            cuerpo,
            claveDeIdempotencia: null,
            conOrigen: true,
            texto => ADisponibilidad(texto, pedidas),
            cancelacion);
    }

    public Task<ResultadoDelInventario<IngresoRegistrado>> RegistrarIngreso(IngresoARegistrar ingreso, string claveDeIdempotencia, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(ingreso);
        if (ingreso.EdicionId == Guid.Empty || ingreso.Cantidad is < LimitesDeContrato.IngresoMinimo or > LimitesDeContrato.IngresoMaximo)
        {
            throw new ArgumentException("El ingreso necesita una edición y una cantidad entre los límites del contrato.", nameof(ingreso));
        }

        if (string.IsNullOrEmpty(claveDeIdempotencia) || !ClaveValida.IsMatch(claveDeIdempotencia))
        {
            throw new ArgumentException("La clave de idempotencia tiene de 16 a 80 caracteres de [A-Za-z0-9_-].", nameof(claveDeIdempotencia));
        }

        var cuerpo = JsonSerializer.Serialize(new RegistrarIngresoDto(ingreso.EdicionId, ingreso.CodigoDeFuente, ingreso.Cantidad, ingreso.Referencia, ingreso.Nota), Json);
        return Pedir(
            HttpMethod.Post,
            $"{RutasDeServicio.Inventario}/ingresos-de-mercaderia",
            AlcanceDeServicio.OperacionDeUsuario,
            cuerpo,
            claveDeIdempotencia,
            conOrigen: false,
            texto =>
            {
                var dto = JsonSerializer.Deserialize<IngresoDeMercaderiaDto>(texto, Json) ?? throw new JsonException("La respuesta no es un ingreso.");
                var registrado = AIngreso(dto);
                return registrado.EdicionId == ingreso.EdicionId && registrado.Cantidad == ingreso.Cantidad
                    ? registrado
                    : throw new JsonException("El ingreso devuelto no es el pedido.");
            },
            cancelacion);
    }

    public Task<ResultadoDelInventario<IReadOnlyList<IngresoRegistrado>>> ListarIngresos(Guid edicionId, int limite, CancellationToken cancelacion = default)
    {
        if (edicionId == Guid.Empty || limite is < 1 or > 50)
        {
            throw new ArgumentException("La consulta necesita una edición y un límite de 1 a 50.");
        }

        return Pedir<IReadOnlyList<IngresoRegistrado>>(
            HttpMethod.Get,
            $"{RutasDeServicio.Inventario}/ingresos-de-mercaderia?edicionId={edicionId:D}&limite={limite.ToString(CultureInfo.InvariantCulture)}",
            AlcanceDeServicio.OperacionDeUsuario,
            cuerpo: null,
            claveDeIdempotencia: null,
            conOrigen: false,
            texto =>
            {
                var lista = JsonSerializer.Deserialize<ListaDeIngresosDto>(texto, Json) ?? throw new JsonException("La respuesta no es una lista de ingresos.");
                var ingresos = (lista.Ingresos ?? throw new JsonException("Faltan los ingresos.")).Select(AIngreso).ToList();
                return ingresos.Count <= limite && ingresos.All(i => i.EdicionId == edicionId)
                    ? ingresos
                    : throw new JsonException("La lista trae más de lo pedido o ingresos de otra edición.");
            },
            cancelacion);
    }

    public Task<ResultadoDelInventario<IReadOnlyList<SucursalPublica>>> ListarSucursales(CancellationToken cancelacion = default) =>
        Pedir<IReadOnlyList<SucursalPublica>>(
            HttpMethod.Get,
            $"{RutasDeServicio.Inventario}/sucursales",
            AlcanceDeServicio.LecturaDeDisponibilidad,
            cuerpo: null,
            claveDeIdempotencia: null,
            conOrigen: false,
            texto =>
            {
                var dto = JsonSerializer.Deserialize<SucursalesDto>(texto, Json) ?? throw new JsonException("La respuesta no es una lista de sucursales.");
                var sucursales = (dto.Sucursales ?? throw new JsonException("Faltan las sucursales.")).Select(ASucursal).ToList();
                return sucursales.Select(s => s.Codigo).Distinct(StringComparer.Ordinal).Count() == sucursales.Count
                    ? sucursales
                    : throw new JsonException("Hay sucursales repetidas.");
            },
            cancelacion);

    private static ResultadoDelInventario ComoSinValor(ResultadoDelInventario<bool> resultado) =>
        resultado.Estado switch
        {
            EstadoDeLlamadaAlInventario.Rechazada => ResultadoDelInventario.Rechazo(resultado.EstadoHttp ?? 400, resultado.Codigo),
            EstadoDeLlamadaAlInventario.LimiteExcedido => ResultadoDelInventario.Limitada(resultado.Codigo),
            _ => ResultadoDelInventario.SinRespuesta,
        };

    private async Task<ResultadoDelInventario<T>> Pedir<T>(
        HttpMethod metodo,
        string ruta,
        AlcanceDeServicio alcance,
        string? cuerpo,
        string? claveDeIdempotencia,
        bool conOrigen,
        Func<string, T> leer,
        CancellationToken cancelacion)
        where T : notnull
    {
        using var solicitud = new HttpRequestMessage(metodo, ruta).ConAlcance(alcance);
        if (claveDeIdempotencia is not null)
        {
            solicitud.Headers.TryAddWithoutValidation(CabecerasDeServicio.ClaveDeIdempotencia, claveDeIdempotencia);
        }

        // Un POST se reintenta solo si lleva clave de idempotencia o es una consulta con cuerpo (EsLectura del contrato, hoy la de disponibilidad): el resto no se repite.
        if (claveDeIdempotencia is not null || (metodo == HttpMethod.Post && alcance == AlcanceDeServicio.LecturaDeDisponibilidad))
        {
            solicitud.ComoIdempotente();
        }

        if (conOrigen)
        {
            solicitud.ConOrigenDelCliente();
        }

        if (cuerpo is not null)
        {
            solicitud.Content = new StringContent(cuerpo, Encoding.UTF8, "application/json");
        }

        try
        {
            var http = fabrica.CreateClient(NombreDelCliente);
            using var respuesta = await http.SendAsync(solicitud, HttpCompletionOption.ResponseHeadersRead, cancelacion).ConfigureAwait(false);
            var estado = (int)respuesta.StatusCode;

            if (respuesta.IsSuccessStatusCode)
            {
                if (typeof(T) == typeof(bool))
                {
                    return (ResultadoDelInventario<T>)(object)ResultadoDelInventario<bool>.ConValor(true);
                }

                if (respuesta.StatusCode == HttpStatusCode.NoContent)
                {
                    return ResultadoDelInventario<T>.SinRespuesta;
                }

                var texto = await LeerAcotado(respuesta, MaximoDeBytesDeLaRespuesta, cancelacion).ConfigureAwait(false);
                return texto is null ? ResultadoDelInventario<T>.SinRespuesta : ResultadoDelInventario<T>.ConValor(leer(texto));
            }

            if (respuesta.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return ResultadoDelInventario<T>.Limitada(await LeerCodigo(respuesta, cancelacion).ConfigureAwait(false));
            }

            if (respuesta.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
            {
                return ResultadoDelInventario<T>.Rechazo(estado, await LeerCodigo(respuesta, cancelacion).ConfigureAwait(false));
            }

            return ResultadoDelInventario<T>.SinRespuesta;
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            // Sin la sesión de la persona (el manejador del token no tiene a quién poner) o sin credencial configurada no se llama: es «no disponible», nunca una cabecera de actor.
            return ResultadoDelInventario<T>.SinRespuesta;
        }
        catch (Exception)
        {
            // Un corte, un plazo vencido, un cortacircuitos abierto o una respuesta que no cumple el contrato: «no disponible», sin el texto del error remoto.
            return ResultadoDelInventario<T>.SinRespuesta;
        }
    }

    /// <summary>El <c>code</c> estable de un <c>problem+json</c>, solo si tiene la forma de un código del contrato; nunca el título ni el detalle.</summary>
    private static async Task<string?> LeerCodigo(HttpResponseMessage respuesta, CancellationToken cancelacion)
    {
        try
        {
            var texto = await LeerAcotado(respuesta, MaximoDeBytesDelProblema, cancelacion).ConfigureAwait(false);
            if (texto is null)
            {
                return null;
            }

            var problema = JsonSerializer.Deserialize<ProblemaDto>(texto, Json);
            return problema is { Code.Length: > 0 and <= 80 } && CodigoDeError.IsMatch(problema.Code) ? problema.Code : null;
        }
        catch (Exception ex) when (ex is JsonException or RegexMatchTimeoutException or IOException or HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>El cuerpo como texto, con un tope de tamaño; <see langword="null"/> si lo supera.</summary>
    private static async Task<string?> LeerAcotado(HttpResponseMessage respuesta, int maximo, CancellationToken cancelacion)
    {
        if (respuesta.Content.Headers.ContentLength is { } largo && largo > maximo)
        {
            return null;
        }

        await using var flujo = await respuesta.Content.ReadAsStreamAsync(cancelacion).ConfigureAwait(false);
        using var memoria = new MemoryStream();
        var bloque = new byte[8192];
        int leidos;
        while ((leidos = await flujo.ReadAsync(bloque, cancelacion).ConfigureAwait(false)) > 0)
        {
            if (memoria.Length + leidos > maximo)
            {
                return null;
            }

            memoria.Write(bloque, 0, leidos);
        }

        return Encoding.UTF8.GetString(memoria.GetBuffer(), 0, (int)memoria.Length);
    }

    // ---------- Cuerpos del contrato (BabelHub.Shared) ----------

    private static DisponibilidadDeVariasEdiciones ADisponibilidad(string texto, HashSet<Guid> pedidas)
    {
        var dto = JsonSerializer.Deserialize<DisponibilidadDeEdicionesDto>(texto, Json) ?? throw new JsonException("La respuesta no es una disponibilidad.");
        var ediciones = (dto.Ediciones ?? throw new JsonException("Faltan las ediciones.")).Select(ADisponibilidadDeUna).ToList();
        var noRegistradas = (dto.NoRegistradas ?? throw new JsonException("Faltan las no registradas.")).ToList();
        var vistas = new HashSet<Guid>();
        var coherente = ediciones.All(e => pedidas.Contains(e.EdicionId) && vistas.Add(e.EdicionId))
            && noRegistradas.All(id => pedidas.Contains(id) && vistas.Add(id));
        return coherente ? new DisponibilidadDeVariasEdiciones(ediciones, noRegistradas) : throw new JsonException("La respuesta trae ediciones que no se pidieron o repetidas.");
    }

    private static DisponibilidadDeLaEdicion ADisponibilidadDeUna(DisponibilidadDeEdicionDto d)
    {
        var fuentes = (d.Fuentes ?? throw new JsonException("Faltan las fuentes.")).Select(f =>
        {
            if (string.IsNullOrWhiteSpace(f.CodigoDeFuente) || f.Vendible < 0 || !EstadosDeStock.Todos.Contains(f.EstadoDeStock))
            {
                throw new JsonException("Una fuente no cumple el contrato.");
            }

            return new FuenteConVendible(f.CodigoDeFuente, f.Nombre ?? string.Empty, f.Vendible, f.EstadoDeStock);
        }).ToList();
        return d.VendibleEnLaRed < 0 ? throw new JsonException("El vendible no puede ser negativo.") : new DisponibilidadDeLaEdicion(d.EdicionId, d.VendibleEnLaRed, fuentes, d.ConsultadaEn);
    }

    private static IngresoRegistrado AIngreso(IngresoDeMercaderiaDto d) =>
        d.IngresoId == Guid.Empty || d.EdicionId == Guid.Empty || string.IsNullOrWhiteSpace(d.CodigoDeFuente) || d.Cantidad is < LimitesDeContrato.IngresoMinimo or > LimitesDeContrato.IngresoMaximo
            ? throw new JsonException("Un ingreso no cumple el contrato.")
            : new IngresoRegistrado(d.IngresoId, d.EdicionId, d.CodigoDeFuente, d.Cantidad, d.Referencia, d.Nota, d.UsuarioId, d.Instante);

    private static SucursalPublica ASucursal(SucursalPublicaDto d) =>
        string.IsNullOrWhiteSpace(d.Codigo) || string.IsNullOrWhiteSpace(d.Nombre)
            ? throw new JsonException("Una sucursal no cumple el contrato.")
            : new SucursalPublica(d.Codigo, d.Nombre, d.Region ?? string.Empty);
}

/// <summary>Composición del cliente del Inventario (<see cref="IClienteDelInventario"/>) en el anfitrión.</summary>
public static class ComposicionDelClienteDelInventario
{
    /// <summary>
    /// Registra el cliente HTTP con nombre del Inventario (<see cref="HttpClienteDelInventario.NombreDelCliente"/>) con el plazo, los reintentos de lo idempotente y el cortacircuitos de
    /// <see cref="ClientesHttpDeServicio.AgregarClienteDeServicio"/>, <b>sin redirecciones</b> (la credencial de servicio y el token del usuario no deben viajar a otro destino) y sin los registros de
    /// petición del cliente. El anfitrión además registra las credenciales de salida (<see cref="OpcionesDeCredencialesDeSalida"/>) y el token de la sesión (<c>AgregarTokenDelUsuarioDeLaSesion</c>).
    /// </summary>
    public static IServiceCollection AgregarClienteDelInventario(this IServiceCollection servicios, Uri direccion, OpcionesDelClienteDeServicio? opciones = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(direccion);
        var cliente = servicios.AgregarClienteDeServicio(HttpClienteDelInventario.NombreDelCliente, direccion, opciones);
        cliente.RemoveAllLoggers();
        cliente.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        servicios.TryAddSingleton<IClienteDelInventario, HttpClienteDelInventario>();
        return servicios;
    }
}
