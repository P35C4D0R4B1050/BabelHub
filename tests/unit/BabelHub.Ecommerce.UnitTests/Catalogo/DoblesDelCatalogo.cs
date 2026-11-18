using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

// Los dobles de los puertos del Catálogo (T05): el Inventario, la memoria de disponibilidad, el emisor de claves, el almacén y el retiro de carritos. Las pruebas de Dominio y de
// Aplicación no usan base de datos ni red; la integración real del Inventario simulado, del almacén S3 y de la auditoría llega en ECO-2c.

/// <summary>Un Inventario simulado: stock por edición, edición registrada o no, respuestas de ingreso a pedido y fallas («no responde», tiempo agotado tras aplicar). Anota cada llamada.</summary>
internal sealed class InventarioDelCatalogoDoble : IInventarioDelCatalogo
{
    private readonly object _bloqueo = new();

    /// <summary>Las ediciones que el Inventario conoce, con su stock por fuente.</summary>
    public Dictionary<Guid, List<DisponibilidadPorFuente>> Ediciones { get; } = [];

    public List<IngresoDelInventario> Ingresos { get; } = [];

    /// <summary>Si es verdadero, el Inventario no responde a nada (caído).</summary>
    public bool Caido { get; set; }

    /// <summary>Dice si hay una transacción abierta (el entorno la fija): llamar al Inventario dentro de una transacción es un error de diseño (D15).</summary>
    public Func<bool>? EnTransaccion { get; set; }

    /// <summary>Si es verdadero, toda llamada espera su token de cancelación (un Inventario colgado) y lanza al cancelarse, como un cliente HTTP.</summary>
    public bool EsperaElToken { get; set; }

    /// <summary>Como <see cref="EsperaElToken"/> pero solo para el ingreso (la ficha responde bien y el ingreso se cuelga).</summary>
    public bool EsperaElTokenEnIngresos { get; set; }

    public async Task<bool> RegistrarEdicionImpresa(Guid edicionId, CancellationToken cancelacion = default)
    {
        await Comprobar(cancelacion).ConfigureAwait(false);
        return await RegistrarEdicionImpresaInterno(edicionId).ConfigureAwait(false);
    }

    public async Task<RespuestaDeDisponibilidad?> ConsultarDisponibilidad(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        await Comprobar(cancelacion).ConfigureAwait(false);
        return await ConsultarDisponibilidadInterno(edicionIds).ConfigureAwait(false);
    }

    public async Task<RespuestaDeIngreso> RegistrarIngreso(SolicitudDeIngresoAlInventario solicitud, CancellationToken cancelacion = default)
    {
        await Comprobar(cancelacion, esIngreso: true).ConfigureAwait(false);
        return await RegistrarIngresoInterno(solicitud).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IngresoDelInventario>?> ListarIngresos(Guid edicionId, int limite, CancellationToken cancelacion = default)
    {
        await Comprobar(cancelacion).ConfigureAwait(false);
        return await ListarIngresosInterno(edicionId, limite).ConfigureAwait(false);
    }

    private async Task Comprobar(CancellationToken cancelacion, bool esIngreso = false)
    {
        if (EnTransaccion?.Invoke() == true)
        {
            throw new InvalidOperationException("Se llamó al Inventario dentro de una transacción.");
        }

        if (EsperaElToken || (esIngreso && EsperaElTokenEnIngresos))
        {
            await Task.Delay(Timeout.Infinite, cancelacion).ConfigureAwait(false);
        }
    }

    public bool NoConfirmaLaFicha { get; set; }

    /// <summary>Si se fija, la siguiente respuesta de ingreso que se devuelve (por ejemplo, un rechazo conocido); se consume en cada llamada.</summary>
    public Queue<RespuestaDeIngreso> RespuestasDeIngreso { get; } = [];

    /// <summary>Tiempo agotado <b>tras aplicar</b>: el ingreso se aplica en el Inventario pero la respuesta no llega (<see cref="RespuestaDeIngreso.SinRespuesta"/>).</summary>
    public bool AplicaYNoResponde { get; set; }

    public List<Guid> FichasRegistradas { get; } = [];

    public List<IReadOnlyCollection<Guid>> LotesConsultados { get; } = [];

    public List<SolicitudDeIngresoAlInventario> SolicitudesDeIngreso { get; } = [];

    public int LlamadasAListarIngresos { get; private set; }

    public DateTimeOffset Ahora { get; set; } = new(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(-5));

    private Task<bool> RegistrarEdicionImpresaInterno(Guid edicionId)
    {
        if (Caido || NoConfirmaLaFicha)
        {
            return Task.FromResult(false);
        }

        lock (_bloqueo)
        {
            FichasRegistradas.Add(edicionId);
            Ediciones.TryAdd(edicionId, []);
        }

        return Task.FromResult(true);
    }

    private Task<RespuestaDeDisponibilidad?> ConsultarDisponibilidadInterno(IReadOnlyCollection<Guid> edicionIds)
    {
        lock (_bloqueo)
        {
            LotesConsultados.Add(edicionIds.ToList());
            if (Caido)
            {
                return Task.FromResult<RespuestaDeDisponibilidad?>(null);
            }

            var conocidas = edicionIds.Where(Ediciones.ContainsKey).Select(id =>
                new DisponibilidadDelInventario(id, Ediciones[id].Sum(f => f.Vendible), Ediciones[id].ToList(), Ahora)).ToList();
            var desconocidas = edicionIds.Where(id => !Ediciones.ContainsKey(id)).ToList();
            return Task.FromResult<RespuestaDeDisponibilidad?>(new RespuestaDeDisponibilidad(conocidas, desconocidas));
        }
    }

    private Task<RespuestaDeIngreso> RegistrarIngresoInterno(SolicitudDeIngresoAlInventario solicitud)
    {
        lock (_bloqueo)
        {
            SolicitudesDeIngreso.Add(solicitud);
            if (Caido)
            {
                return Task.FromResult(RespuestaDeIngreso.SinRespuesta);
            }

            if (RespuestasDeIngreso.TryDequeue(out var preparada))
            {
                return Task.FromResult(preparada);
            }

            // Idempotencia del Inventario: la misma clave con la misma carga devuelve el mismo ingreso; con otra carga, un conflicto.
            var previo = Ingresos.FirstOrDefault(i => i.IngresoId == IdDeLaClave(solicitud.ClaveDeIdempotencia));
            if (previo is not null)
            {
                return Task.FromResult(previo.Cantidad == solicitud.Cantidad && previo.EdicionId == solicitud.EdicionId
                    ? RespuestaDeIngreso.Creado(previo.IngresoId)
                    : RespuestaDeIngreso.Rechazado(409, "INV-IDEMPOTENCIA-EN-CONFLICTO"));
            }

            var id = IdDeLaClave(solicitud.ClaveDeIdempotencia);
            Ediciones.TryAdd(solicitud.EdicionId, []);
            var fuente = Ediciones[solicitud.EdicionId].FirstOrDefault(f => f.CodigoDeFuente == "RECEPCION");
            Ediciones[solicitud.EdicionId].RemoveAll(f => f.CodigoDeFuente == "RECEPCION");
            Ediciones[solicitud.EdicionId].Add(new DisponibilidadPorFuente("RECEPCION", "Recepción", (fuente?.Vendible ?? 0) + solicitud.Cantidad));
            Ingresos.Add(new IngresoDelInventario(id, solicitud.EdicionId, "RECEPCION", solicitud.Cantidad, solicitud.Referencia, solicitud.Nota, UsuarioDelIngreso, Ahora));
            return Task.FromResult(AplicaYNoResponde ? RespuestaDeIngreso.SinRespuesta : RespuestaDeIngreso.Creado(id));
        }
    }

    public Guid? UsuarioDelIngreso { get; set; }

    public bool ListarIngresosCaido { get; set; }

    /// <summary>Algo que ocurre justo cuando se pide la lista de ingresos (por ejemplo, otra conciliación que resuelve la fila antes de que esta termine).</summary>
    public Action? AlListarIngresos { get; set; }

    private Task<IReadOnlyList<IngresoDelInventario>?> ListarIngresosInterno(Guid edicionId, int limite)
    {
        lock (_bloqueo)
        {
            LlamadasAListarIngresos++;
            AlListarIngresos?.Invoke();
            if (Caido || ListarIngresosCaido)
            {
                return Task.FromResult<IReadOnlyList<IngresoDelInventario>?>(null);
            }

            return Task.FromResult<IReadOnlyList<IngresoDelInventario>?>(Ingresos.Where(i => i.EdicionId == edicionId).OrderByDescending(i => i.Instante).Take(limite).ToList());
        }
    }

    /// <summary>Fija el stock de una edición en una fuente.</summary>
    public void ConStock(Guid edicionId, int vendible, string codigoDeFuente = "LIMA", string nombre = "Lima")
    {
        lock (_bloqueo)
        {
            if (!Ediciones.TryGetValue(edicionId, out var fuentes))
            {
                fuentes = [];
                Ediciones[edicionId] = fuentes;
            }

            fuentes.RemoveAll(f => f.CodigoDeFuente == codigoDeFuente);
            fuentes.Add(new DisponibilidadPorFuente(codigoDeFuente, nombre, vendible));
        }
    }

    private static Guid IdDeLaClave(Guid clave) => clave;
}

/// <summary>La memoria de disponibilidad en un diccionario, con la semántica del puerto (la invalidación conserva el dato como respaldo).</summary>
internal sealed class MemoriaDeDisponibilidadDoble : IMemoriaDeDisponibilidad
{
    private readonly ConcurrentDictionary<Guid, EntradaDeDisponibilidad> _entradas = new();

    public int Guardados { get; private set; }

    public EntradaDeDisponibilidad? Obtener(Guid edicionId) => _entradas.TryGetValue(edicionId, out var entrada) ? entrada : null;

    public void Guardar(EntradaDeDisponibilidad entrada)
    {
        Guardados++;
        _entradas[entrada.EdicionId] = entrada with { Invalidada = false };
    }

    public void Invalidar(Guid edicionId)
    {
        if (_entradas.TryGetValue(edicionId, out var entrada))
        {
            _entradas[edicionId] = entrada with { Invalidada = true };
        }
    }
}

/// <summary>Un emisor de tokens de ingreso con una firma HMAC propia de la prueba: el token es <c>nonce.usuario.edicion.vence.firma</c> y vence a los 30 minutos exactos según el reloj que recibe.</summary>
internal sealed class EmisorDeClavesDeIngresoDoble : IEmisorDeClavesDeIngreso
{
    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(30);

    private static readonly byte[] Clave = Encoding.UTF8.GetBytes("clave-de-prueba-solo-para-las-pruebas-unitarias");

    public int Emisiones { get; private set; }

    public TokenDeIngresoEmitido Emitir(Guid usuarioId, Guid edicionId, DateTimeOffset ahora)
    {
        Emisiones++;
        var nonce = Guid.NewGuid();
        var vence = ahora + Vigencia;
        var cuerpo = $"{nonce:N}.{usuarioId:N}.{edicionId:N}.{vence.ToUnixTimeSeconds()}";
        return new TokenDeIngresoEmitido($"{cuerpo}.{Firmar(cuerpo)}", nonce, vence);
    }

    public Guid? Verificar(string? token, Guid usuarioId, Guid edicionId, DateTimeOffset ahora)
    {
        var partes = token?.Split('.');
        if (partes is not { Length: 5 }
            || !Guid.TryParseExact(partes[0], "N", out var nonce)
            || !Guid.TryParseExact(partes[1], "N", out var usuario)
            || !Guid.TryParseExact(partes[2], "N", out var edicion)
            || !long.TryParse(partes[3], out var vence))
        {
            return null;
        }

        var cuerpo = string.Join('.', partes[..4]);
        var esperada = Encoding.ASCII.GetBytes(Firmar(cuerpo));
        var recibida = Encoding.ASCII.GetBytes(partes[4]);
        if (!CryptographicOperations.FixedTimeEquals(esperada, recibida) || usuario != usuarioId || edicion != edicionId || ahora.ToUnixTimeSeconds() >= vence)
        {
            return null;
        }

        return nonce;
    }

    private static string Firmar(string cuerpo) => Convert.ToHexStringLower(HMACSHA256.HashData(Clave, Encoding.UTF8.GetBytes(cuerpo)));
}

/// <summary>
/// Un almacén de objetos en memoria con la verificación del puerto: firma de los primeros bytes antes de escribir, tope por bytes realmente leídos, huella calculada durante la escritura
/// y fallas a pedido («no responde»). No verifica la estructura de un EPUB ni las dimensiones (eso es del adaptador real, T17), salvo que la prueba las fije con <see cref="DimensionesDeLaProxima"/>
/// o <see cref="EstructuraInvalidaEnLaProxima"/>.
/// </summary>
internal sealed class AlmacenDeArchivosDoble : IAlmacenDeArchivos
{
    private readonly ConcurrentDictionary<string, byte[]> _objetos = new();

    public bool NoResponde { get; set; }

    /// <summary>Dice si hay una transacción abierta: el streaming de un archivo nunca corre con una conexión abierta (D14).</summary>
    public Func<bool>? EnTransaccion { get; set; }

    /// <summary>Si es verdadero, <see cref="Borrar"/> falla como un almacén que no responde (la escritura sí funciona).</summary>
    public bool BorrarFalla { get; set; }

    public bool EstructuraInvalidaEnLaProxima { get; set; }

    public (int Ancho, int Alto)? DimensionesDeLaProxima { get; set; }

    /// <summary>Los objetos que existen ahora, por clave.</summary>
    public IReadOnlyDictionary<string, byte[]> Objetos => _objetos;

    public List<string> Borrados { get; } = [];

    public int Escrituras { get; private set; }

    public async Task<ResultadoDeEscrituraEnAlmacen> Escribir(string claveEnAlmacen, FinalidadDeArchivo finalidad, Stream contenido, CancellationToken cancelacion = default)
    {
        Escrituras++;
        if (EnTransaccion?.Invoke() == true)
        {
            throw new InvalidOperationException("Se escribió en el almacén con una transacción abierta.");
        }

        if (NoResponde)
        {
            throw new AlmacenNoDisponibleException();
        }

        var inicio = new byte[TiposDeArchivo.BytesDeFirma];
        var leidos = 0;
        while (leidos < inicio.Length)
        {
            var n = await contenido.ReadAsync(inicio.AsMemory(leidos), cancelacion).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }

            leidos += n;
        }

        var tipo = TiposDeArchivo.PorPrimerosBytes(inicio.AsSpan(0, leidos));
        if (tipo is not { } detectado || !TiposDeArchivo.EsAcordeALaFinalidad(detectado, finalidad))
        {
            return ResultadoDeEscrituraEnAlmacen.Rechazado(MotivoDeRechazoDeArchivo.TipoNoValido);
        }

        if (finalidad == FinalidadDeArchivo.Portada && DimensionesDeLaProxima is { } d && !PoliticaDeArchivos.DimensionesValidas(d.Ancho, d.Alto))
        {
            DimensionesDeLaProxima = null;
            return ResultadoDeEscrituraEnAlmacen.Rechazado(MotivoDeRechazoDeArchivo.DimensionesInvalidas);
        }

        using var memoria = new MemoryStream();
        memoria.Write(inicio, 0, leidos);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(inicio, 0, leidos);
        long total = leidos;
        var buffer = new byte[8192];
        while (true)
        {
            var n = await contenido.ReadAsync(buffer, cancelacion).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }

            total += n;
            if (PoliticaDeArchivos.SuperaElTope(finalidad, total))
            {
                return ResultadoDeEscrituraEnAlmacen.Rechazado(MotivoDeRechazoDeArchivo.DemasiadoGrande);
            }

            sha.AppendData(buffer, 0, n);
            memoria.Write(buffer, 0, n);
        }

        if (EstructuraInvalidaEnLaProxima)
        {
            EstructuraInvalidaEnLaProxima = false;
            return ResultadoDeEscrituraEnAlmacen.Rechazado(MotivoDeRechazoDeArchivo.EstructuraInvalida);
        }

        _objetos[claveEnAlmacen] = memoria.ToArray();
        return ResultadoDeEscrituraEnAlmacen.Correcto(new ArchivoEscrito(detectado, total, HuellaDeArchivo.Desde(sha.GetHashAndReset())));
    }


    public Task<Stream?> AbrirPortada(string claveEnAlmacen, CancellationToken cancelacion = default)
    {
        if (NoResponde)
        {
            throw new AlmacenNoDisponibleException();
        }

        return Task.FromResult<Stream?>(_objetos.TryGetValue(claveEnAlmacen, out var datos) ? new MemoryStream(datos, writable: false) : null);
    }

    public Task Borrar(string claveEnAlmacen, CancellationToken cancelacion = default)
    {
        if (NoResponde || BorrarFalla)
        {
            throw new AlmacenNoDisponibleException();
        }

        _objetos.TryRemove(claveEnAlmacen, out _);
        Borrados.Add(claveEnAlmacen);
        return Task.CompletedTask;
    }
}

/// <summary>El retiro de carritos que anota lo que se le pidió y puede fallar a pedido (la baja no se aplica si falla, D07).</summary>
internal sealed class RetiroDeCarritosEspia : IRetiroDeEdicionDeCarritos
{
    public List<IReadOnlyCollection<Guid>> Retiros { get; } = [];

    public bool Falla { get; set; }

    public Task Retirar(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            return Task.FromException(new InvalidOperationException("Carrito no responde."));
        }

        Retiros.Add(edicionIds.ToList());
        return Task.CompletedTask;
    }
}

/// <summary>Una denegación de acceso que guarda lo registrado.</summary>
internal sealed class DenegacionesDelCatalogoEspia : IDenegacionDeAcceso
{
    public List<DenegacionDeAcceso> Eventos { get; } = [];

    public Task Registrar(DenegacionDeAcceso denegacion, CancellationToken cancelacion = default)
    {
        Eventos.Add(denegacion);
        return Task.CompletedTask;
    }
}

/// <summary>Una auditoría que guarda lo registrado y puede fallar como una auditoría caída (AUD-04).</summary>
internal sealed class AuditoriaDelCatalogoEspia : IAuditoria
{
    public List<SolicitudDeRegistro> Solicitudes { get; } = [];

    public bool Caida { get; set; }

    public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
    {
        if (Caida)
        {
            return Task.FromException(new AuditoriaNoDisponibleException("sin auditoría"));
        }

        Solicitudes.Add(solicitud);
        return Task.CompletedTask;
    }
}

/// <summary>Un publicador de eventos de seguridad que guarda lo publicado (y puede fallar) para probar que el límite deja su evento sin interrumpir la respuesta.</summary>
internal sealed class EventosDeSeguridadEspia : IPublicadorDeEventosDeSeguridad
{
    public List<EventoDeSeguridad> Publicados { get; } = [];

    public bool Falla { get; set; }

    public Task Publicar(EventoDeSeguridad evento, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            return Task.FromException(new InvalidOperationException("Auditoría no responde."));
        }

        Publicados.Add(evento);
        return Task.CompletedTask;
    }
}

/// <summary>Un origen conocido y fijo para las pruebas.</summary>
internal sealed class OrigenDelCatalogoDoble : IOrigenDeSolicitud
{
    public OrigenDeSolicitud Actual { get; } = new(HuellaDeOrigen.Crear("k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE", "v1"), "2800:200:1234:5678::1");
}
