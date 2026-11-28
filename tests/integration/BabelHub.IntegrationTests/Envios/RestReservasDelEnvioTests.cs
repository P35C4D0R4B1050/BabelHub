using System.Reflection;
using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Envios;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Envios;

/// <summary>El cierre de reservas del envío sobre el cliente del Inventario (T13; ENV-11, ENV-12, ENV-16; AC-ENV-10, AC-ENV-11, AC-ENV-16): una sola llamada con todas las líneas, la clave del envío, la validación de la respuesta y la lista cerrada de resultados.</summary>
public sealed class RestReservasDelEnvioTests
{
    private static readonly Guid Pedido = Guid.Parse("b1000000-0000-0000-0000-000000000001");
    private static readonly Guid Envio = Guid.Parse("b2000000-0000-0000-0000-000000000002");

    public class ClienteDelInventarioEspia : DispatchProxy
    {
        public static List<(Guid PedidoId, string Motivo, IReadOnlyList<Guid> Lineas, Guid? EnvioId, string Clave)> Llamadas { get; } = [];

        public static Func<ResultadoDelInventario<CierreDeLaReserva>> Respuesta { get; set; } = () => throw new InvalidOperationException();

        protected override object? Invoke(MethodInfo? metodo, object?[]? argumentos)
        {
            if (metodo!.Name != nameof(IClienteDelInventario.CerrarReserva))
            {
                throw new NotSupportedException(metodo.Name);
            }

            Llamadas.Add(((Guid)argumentos![0]!, (string)argumentos[1]!, (IReadOnlyList<Guid>)argumentos[2]!, (Guid?)argumentos[3], (string)argumentos[4]!));
            return Task.FromResult(Respuesta());
        }
    }

    private static RestReservasDelEnvio Crear(Func<ResultadoDelInventario<CierreDeLaReserva>> respuesta)
    {
        ClienteDelInventarioEspia.Llamadas.Clear();
        ClienteDelInventarioEspia.Respuesta = respuesta;
        return new RestReservasDelEnvio(DispatchProxy.Create<IClienteDelInventario, ClienteDelInventarioEspia>());
    }

    private static ResultadoDelInventario<CierreDeLaReserva> Cierre(Guid pedido, params (Guid Linea, string Resultado)[] lineas) =>
        ResultadoDelInventario<CierreDeLaReserva>.ConValor(new CierreDeLaReserva(pedido, [.. lineas.Select(l => new ResultadoDeCierreDeLinea(l.Linea, l.Resultado))]));

    [Fact]
    [Trait("Criterio", "AC-ENV-16")]
    public async Task Cierra_todas_las_lineas_en_una_sola_llamada_con_motivo_reparto_el_envio_y_la_clave_igual_al_envio()
    {
        var lineas = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var adaptador = Crear(() => Cierre(Pedido, lineas.Select(l => (l, "cerrada")).ToArray()));

        var resultado = await adaptador.Cerrar(Pedido, Envio, lineas);

        Assert.IsType<ResultadoDeCierreDeReservas.Cerrado>(resultado);
        var llamada = Assert.Single(ClienteDelInventarioEspia.Llamadas);
        Assert.Equal(Pedido, llamada.PedidoId);
        Assert.Equal("reparto", llamada.Motivo);
        Assert.Equal(lineas, llamada.Lineas);
        Assert.Equal(Envio, llamada.EnvioId);
        Assert.Equal(Envio.ToString("D"), llamada.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-11")]
    public async Task Una_linea_ya_cerrada_es_un_hecho_que_se_acepta()
    {
        var lineas = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var adaptador = Crear(() => Cierre(Pedido, (lineas[0], "ya-cerrada"), (lineas[1], "cerrada")));

        Assert.IsType<ResultadoDeCierreDeReservas.Cerrado>(await adaptador.Cerrar(Pedido, Envio, lineas));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-11")]
    public async Task Una_respuesta_con_lineas_de_mas_de_menos_repetidas_de_otro_pedido_o_con_un_resultado_desconocido_es_incierta()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var respuestas = new Func<ResultadoDelInventario<CierreDeLaReserva>>[]
        {
            () => Cierre(Pedido, (a, "cerrada"), (b, "cerrada"), (Guid.NewGuid(), "cerrada")),
            () => Cierre(Pedido, (a, "cerrada")),
            () => Cierre(Pedido, (a, "cerrada"), (a, "cerrada")),
            () => Cierre(Pedido, (a, "cerrada"), (Guid.NewGuid(), "cerrada")),
            () => Cierre(Guid.NewGuid(), (a, "cerrada"), (b, "cerrada")),
            () => Cierre(Pedido, (a, "cerrada"), (b, "pendiente")),
            () => Cierre(Pedido, (a, "cerrada"), (b, string.Empty)),
            () => Cierre(Pedido),
        };

        foreach (var respuesta in respuestas)
        {
            var adaptador = Crear(respuesta);

            Assert.IsType<ResultadoDeCierreDeReservas.Incierto>(await adaptador.Cerrar(Pedido, Envio, [a, b]));
            Assert.Single(ClienteDelInventarioEspia.Llamadas);
        }
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-11")]
    [InlineData("INV-RESERVA-ESTADO-INVALIDO")]
    [InlineData("INV-VALIDACION-INVALIDA")]
    [InlineData("INV-NO-ENCONTRADO")]
    public async Task Los_tres_rechazos_de_negocio_de_la_lista_cerrada_garantizan_que_no_cambio_nada(string codigo)
    {
        var adaptador = Crear(() => ResultadoDelInventario<CierreDeLaReserva>.Rechazo(409, codigo));

        Assert.IsType<ResultadoDeCierreDeReservas.RechazoDeNegocio>(await adaptador.Cerrar(Pedido, Envio, [Guid.NewGuid()]));
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-11")]
    [InlineData(409, "INV-IDEMPOTENCIA-EN-CURSO")]
    [InlineData(401, "INV-NO-AUTENTICADO")]
    [InlineData(403, "INV-PROHIBIDO")]
    [InlineData(408, null)]
    [InlineData(503, "INV-SERVICIO-NO-DISPONIBLE")]
    public async Task Todo_otro_rechazo_es_incierto_porque_el_primer_cierre_pudo_aplicarse(int estado, string? codigo)
    {
        var adaptador = Crear(() => ResultadoDelInventario<CierreDeLaReserva>.Rechazo(estado, codigo));

        Assert.IsType<ResultadoDeCierreDeReservas.Incierto>(await adaptador.Cerrar(Pedido, Envio, [Guid.NewGuid()]));
        Assert.Single(ClienteDelInventarioEspia.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-11")]
    public async Task Sin_respuesta_un_limite_o_una_excepcion_del_cliente_son_inciertos_y_la_cancelacion_se_propaga()
    {
        var linea = new[] { Guid.NewGuid() };

        Assert.IsType<ResultadoDeCierreDeReservas.Incierto>(await Crear(() => ResultadoDelInventario<CierreDeLaReserva>.SinRespuesta).Cerrar(Pedido, Envio, linea));
        Assert.IsType<ResultadoDeCierreDeReservas.Incierto>(await Crear(() => ResultadoDelInventario<CierreDeLaReserva>.Limitada("INV-LIMITE-EXCEDIDO")).Cerrar(Pedido, Envio, linea));
        Assert.IsType<ResultadoDeCierreDeReservas.Incierto>(await Crear(() => throw new InvalidOperationException("texto remoto con la dirección")).Cerrar(Pedido, Envio, linea));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await Crear(() => throw new OperationCanceledException()).Cerrar(Pedido, Envio, linea));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-16")]
    public async Task Una_llamada_que_el_contrato_no_admite_no_se_envia()
    {
        var adaptador = Crear(() => throw new InvalidOperationException("no debe llamarse"));
        var linea = Guid.NewGuid();

        foreach (var (pedido, envio, lineas) in new (Guid, Guid, Guid[])[] { (Guid.Empty, Envio, [linea]), (Pedido, Guid.Empty, [linea]), (Pedido, Envio, []), (Pedido, Envio, [linea, linea]), (Pedido, Envio, [Guid.Empty]), (Pedido, Envio, [.. Enumerable.Range(0, 51).Select(_ => Guid.NewGuid())]) })
        {
            Assert.IsType<ResultadoDeCierreDeReservas.RechazoDeNegocio>(await adaptador.Cerrar(pedido, envio, lineas));
        }

        Assert.Empty(ClienteDelInventarioEspia.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-10")]
    public void Con_el_cliente_del_Inventario_registrado_la_composicion_usa_el_adaptador_real_y_la_guarda_de_produccion_ya_no_lo_rechaza()
    {
        var servicios = ComposicionYTrabajosDeEnviosTests.ConDependenciasPublicas();
        servicios.AddSingleton(DispatchProxy.Create<IClienteDelInventario, ClienteDelInventarioEspia>());

        servicios.AgregarEnvios(esProduccion: true);

        using var proveedor = servicios.BuildServiceProvider();
        using var alcance = proveedor.CreateScope();
        Assert.IsType<RestReservasDelEnvio>(alcance.ServiceProvider.GetRequiredService<IReservasDelEnvio>());
    }
}
