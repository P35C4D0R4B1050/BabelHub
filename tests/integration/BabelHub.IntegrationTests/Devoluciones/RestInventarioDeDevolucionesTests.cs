using System.Reflection;
using BabelHub.Ecommerce.Application.Devoluciones.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Devoluciones;

namespace BabelHub.IntegrationTests.Devoluciones;

/// <summary>
/// El Inventario de Devoluciones sobre el cliente de Plataforma (T20; DEV-01, DEV-07, DEV-17; AC-DEV-07, AC-DEV-16): la carga inmutable del reingreso, la clasificación cerrada de las respuestas (rechazo de negocio, conflicto de idempotencia o incierto), la validación de la respuesta
/// (SEG-13), que una carga inadmisible no se envíe y las sucursales activas con caché de cinco minutos.
/// </summary>
public sealed class RestInventarioDeDevolucionesTests
{
    private static readonly Guid Devolucion = Guid.Parse("d1000000-0000-0000-0000-000000000001");
    private static readonly Guid Pedido = Guid.Parse("d2000000-0000-0000-0000-000000000002");

    public class ClienteDelInventarioEspia : DispatchProxy
    {
        public static List<(Guid DevolucionId, Guid PedidoId, string Fuente, IReadOnlyList<LineaAReingresar> Lineas)> Reingresos { get; } = [];

        public static int ConsultasDeSucursales { get; set; }

        public static Func<ResultadoDelInventario<ReingresoDeDevolucion>> RespuestaDelReingreso { get; set; } = () => throw new InvalidOperationException();

        public static Func<ResultadoDelInventario<IReadOnlyList<SucursalPublica>>> RespuestaDeSucursales { get; set; } = () => throw new InvalidOperationException();

        protected override object? Invoke(MethodInfo? metodo, object?[]? argumentos)
        {
            switch (metodo!.Name)
            {
                case nameof(IClienteDelInventario.ReingresarDevolucion):
                    Reingresos.Add(((Guid)argumentos![0]!, (Guid)argumentos[1]!, (string)argumentos[2]!, (IReadOnlyList<LineaAReingresar>)argumentos[3]!));
                    return Task.FromResult(RespuestaDelReingreso());
                case nameof(IClienteDelInventario.ListarSucursales):
                    ConsultasDeSucursales++;
                    return Task.FromResult(RespuestaDeSucursales());
                default:
                    throw new NotSupportedException(metodo.Name);
            }
        }
    }

    private sealed class RelojDePrueba(DateTimeOffset ahora) : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private static (RestInventarioDeDevoluciones Adaptador, RelojDePrueba Reloj) Crear(Func<ResultadoDelInventario<ReingresoDeDevolucion>>? reingreso = null, Func<ResultadoDelInventario<IReadOnlyList<SucursalPublica>>>? sucursales = null)
    {
        ClienteDelInventarioEspia.Reingresos.Clear();
        ClienteDelInventarioEspia.ConsultasDeSucursales = 0;
        ClienteDelInventarioEspia.RespuestaDelReingreso = reingreso ?? (() => ResultadoDelInventario<ReingresoDeDevolucion>.ConValor(new ReingresoDeDevolucion(Devolucion, false)));
        ClienteDelInventarioEspia.RespuestaDeSucursales = sucursales ?? (() => ResultadoDelInventario<IReadOnlyList<SucursalPublica>>.ConValor([new SucursalPublica("lima-central", "Lima Central", "lima"), new SucursalPublica("arequipa", "Arequipa", "arequipa")]));
        var reloj = new RelojDePrueba(new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero));
        return (new RestInventarioDeDevoluciones(DispatchProxy.Create<IClienteDelInventario, ClienteDelInventarioEspia>(), new CacheDeSucursalesActivas(reloj)), reloj);
    }

    private static ReingresoPorDevolucion Carga(int lineas = 2) =>
        new(Devolucion, Pedido, "lima-central", [.. Enumerable.Range(0, lineas).Select(i => new LineaDeReingreso(Guid.NewGuid(), i + 1))]);

    // ---------- El reingreso ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-07")]
    public async Task Reingresa_con_la_devolucion_el_pedido_la_sucursal_receptora_y_las_lineas_tal_cual_y_lo_confirmado_es_reingresado()
    {
        var (adaptador, _) = Crear();
        var carga = Carga(3);

        var resultado = await adaptador.Reingresar(carga);

        Assert.IsType<ResultadoDelReingreso.Reingresado>(resultado);
        var llamada = Assert.Single(ClienteDelInventarioEspia.Reingresos);
        Assert.Equal((Devolucion, Pedido, "lima-central"), (llamada.DevolucionId, llamada.PedidoId, llamada.Fuente));
        Assert.Equal(carga.Lineas.Select(l => (l.EdicionId, l.Cantidad)), llamada.Lineas.Select(l => (l.EdicionId, l.Cantidad)));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-16")]
    public async Task Ya_aplicado_es_un_hecho_que_se_acepta_y_una_repeticion_con_la_misma_carga_vuelve_a_enviarse_sin_clave()
    {
        var (adaptador, _) = Crear(() => ResultadoDelInventario<ReingresoDeDevolucion>.ConValor(new ReingresoDeDevolucion(Devolucion, true)));
        var carga = Carga();

        var primera = await adaptador.Reingresar(carga);
        var segunda = await adaptador.Reingresar(carga);

        Assert.IsType<ResultadoDelReingreso.Reingresado>(primera);
        Assert.IsType<ResultadoDelReingreso.Reingresado>(segunda);
        Assert.Equal(2, ClienteDelInventarioEspia.Reingresos.Count);
        Assert.Equal(ClienteDelInventarioEspia.Reingresos[0].Lineas, ClienteDelInventarioEspia.Reingresos[1].Lineas);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-07")]
    [InlineData("INV-NO-ENCONTRADO", 404)]
    [InlineData("INV-EDICION-NO-IMPRESA", 422)]
    [InlineData("INV-VALIDACION-INVALIDA", 400)]
    public async Task Los_tres_rechazos_de_negocio_garantizan_que_no_se_reingreso_nada(string codigo, int estado)
    {
        var (adaptador, _) = Crear(() => ResultadoDelInventario<ReingresoDeDevolucion>.Rechazo(estado, codigo));

        Assert.IsType<ResultadoDelReingreso.RechazoDeNegocio>(await adaptador.Reingresar(Carga()));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-16")]
    public async Task El_conflicto_de_idempotencia_es_un_reingreso_previo_con_otra_carga()
    {
        var (adaptador, _) = Crear(() => ResultadoDelInventario<ReingresoDeDevolucion>.Rechazo(409, "INV-IDEMPOTENCIA-EN-CONFLICTO"));

        Assert.IsType<ResultadoDelReingreso.ConflictoDeIdempotencia>(await adaptador.Reingresar(Carga()));
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-16")]
    [InlineData("INV-PROHIBIDO", 403)]
    [InlineData("INV-NO-AUTENTICADO", 401)]
    [InlineData("INV-SERVICIO-NO-DISPONIBLE", 503)]
    [InlineData(null, 408)]
    public async Task Todo_lo_demas_incluidos_el_permiso_la_sesion_y_los_5xx_es_incierto_porque_el_reingreso_pudo_aplicarse(string? codigo, int estado)
    {
        var (adaptador, _) = Crear(() => ResultadoDelInventario<ReingresoDeDevolucion>.Rechazo(estado, codigo));

        Assert.IsType<ResultadoDelReingreso.Incierto>(await adaptador.Reingresar(Carga()));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-16")]
    public async Task Sin_respuesta_con_limite_o_con_una_excepcion_es_incierto()
    {
        var (sinRespuesta, _) = Crear(() => ResultadoDelInventario<ReingresoDeDevolucion>.SinRespuesta);
        var (limitada, _) = Crear(() => ResultadoDelInventario<ReingresoDeDevolucion>.Limitada("INV-LIMITE-EXCEDIDO"));
        Assert.IsType<ResultadoDelReingreso.Incierto>(await sinRespuesta.Reingresar(Carga()));
        Assert.IsType<ResultadoDelReingreso.Incierto>(await limitada.Reingresar(Carga()));
        var (lanza, _) = Crear(() => throw new HttpRequestException("el texto remoto no debe salir"));

        Assert.IsType<ResultadoDelReingreso.Incierto>(await lanza.Reingresar(Carga()));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-16")]
    public async Task Una_respuesta_que_no_es_de_la_devolucion_pedida_es_incierta()
    {
        var (adaptador, _) = Crear(() => ResultadoDelInventario<ReingresoDeDevolucion>.ConValor(new ReingresoDeDevolucion(Guid.NewGuid(), false)));

        Assert.IsType<ResultadoDelReingreso.Incierto>(await adaptador.Reingresar(Carga()));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-07")]
    public async Task Una_carga_que_el_contrato_no_admite_no_se_envia_y_es_un_rechazo_de_negocio()
    {
        var (adaptador, _) = Crear();
        var repetida = Guid.NewGuid();
        var inadmisibles = new[]
        {
            Carga(0),
            Carga(21),
            Carga() with { CodigoDeFuente = "  " },
            Carga() with { DevolucionId = Guid.Empty },
            Carga() with { PedidoId = Guid.Empty },
            Carga() with { Lineas = [new LineaDeReingreso(repetida, 1), new LineaDeReingreso(repetida, 2)] },
            Carga() with { Lineas = [new LineaDeReingreso(Guid.NewGuid(), 0)] },
        };

        foreach (var carga in inadmisibles)
        {
            Assert.IsType<ResultadoDelReingreso.RechazoDeNegocio>(await adaptador.Reingresar(carga));
        }

        Assert.Empty(ClienteDelInventarioEspia.Reingresos);
    }

    // ---------- Las sucursales activas ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-01")]
    public async Task Las_sucursales_activas_se_guardan_cinco_minutos_y_pasado_el_plazo_se_vuelven_a_pedir()
    {
        var (adaptador, reloj) = Crear();

        var primera = await adaptador.ListarSucursalesActivas();
        reloj.Ahora = reloj.Ahora.AddMinutes(4);
        var enCache = await adaptador.ListarSucursalesActivas();
        reloj.Ahora = reloj.Ahora.AddMinutes(2);
        var vencida = await adaptador.ListarSucursalesActivas();

        Assert.Equal(["lima-central", "arequipa"], Assert.IsType<ResultadoDeSucursalesActivas.Lista>(primera).Codigos);
        Assert.IsType<ResultadoDeSucursalesActivas.Lista>(enCache);
        Assert.IsType<ResultadoDeSucursalesActivas.Lista>(vencida);
        Assert.Equal(2, ClienteDelInventarioEspia.ConsultasDeSucursales);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-01")]
    public async Task Una_respuesta_fallida_o_con_un_codigo_invalido_es_no_disponible_y_no_se_guarda()
    {
        var invalida = new List<Func<ResultadoDelInventario<IReadOnlyList<SucursalPublica>>>>
        {
            () => ResultadoDelInventario<IReadOnlyList<SucursalPublica>>.SinRespuesta,
            () => ResultadoDelInventario<IReadOnlyList<SucursalPublica>>.Limitada(null),
            () => ResultadoDelInventario<IReadOnlyList<SucursalPublica>>.ConValor([new SucursalPublica("lima-central", "x", "y"), new SucursalPublica(" ", "x", "y")]),
            () => ResultadoDelInventario<IReadOnlyList<SucursalPublica>>.ConValor([new SucursalPublica(new string('a', 41), "x", "y")]),
            () => throw new HttpRequestException("el texto remoto no debe salir"),
        };

        foreach (var respuesta in invalida)
        {
            var (adaptador, _) = Crear(sucursales: respuesta);

            Assert.IsType<ResultadoDeSucursalesActivas.NoDisponible>(await adaptador.ListarSucursalesActivas());
            Assert.IsType<ResultadoDeSucursalesActivas.NoDisponible>(await adaptador.ListarSucursalesActivas());
            Assert.Equal(2, ClienteDelInventarioEspia.ConsultasDeSucursales);
        }
    }
}
