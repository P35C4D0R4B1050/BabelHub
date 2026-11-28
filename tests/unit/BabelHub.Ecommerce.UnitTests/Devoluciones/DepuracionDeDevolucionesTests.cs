using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>
/// La depuración diaria de los dos años y su vigilancia (T17; DEV-27, DEV-30; AC-DEV-29, AC-DEV-32): redacta, como «sistema» y por día de Lima, lo que cumplió dos años con todas sus líneas finales; si no puede redactar o auditar, no hace nada ese día; y avisa si pasan 24 horas sin una corrida correcta.
/// </summary>
public sealed class DepuracionDeDevolucionesTests
{
    /// <summary>El instante del último cambio: las 21:00 del 19 de noviembre de 2024 en Lima (02:00 UTC del 20).</summary>
    private static readonly DateTimeOffset Cambio = new(2024, 11, 20, 2, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset LimaMedianoche(int anio, int mes, int dia) => new(anio, mes, dia, 0, 0, 0, TimeSpan.FromHours(-5));

    /// <summary>Una devolución impresa solicitada por un cliente y rechazada por la administración en <see cref="Cambio"/>, vista por el «sistema» con el reloj en la fecha dada.</summary>
    private static async Task<(EntornoDeDevoluciones Sistema, Guid DevolucionId)> ConRechazada(DateTimeOffset ahora, bool rechazar = true)
    {
        var (sistema, ids) = await ConRechazadas(1, ahora, rechazar);
        return (sistema, ids[0]);
    }

    private static async Task<(EntornoDeDevoluciones Sistema, Guid[] DevolucionIds)> ConRechazadas(int cantidad, DateTimeOffset ahora, bool rechazar = true)
    {
        var cliente = new EntornoDeDevoluciones(ahora: Cambio);
        var admin = new EntornoDeDevoluciones(ActorDePrueba.DeAdministradorCentral()).ConLosDatosDe(cliente);
        var ids = new List<Guid>();
        for (var i = 0; i < cantidad; i++)
        {
            var linea = EntornoDeDevoluciones.Entregada(Cambio.AddDays(-2), 3, 50m);
            var pedido = cliente.SembrarPedido(linea);
            var solicitada = await cliente.SolicitarImpreso.Ejecutar(new SolicitarDevolucionDeImpresoEntrada(pedido.PedidoId, [new LineaPedidaParaDevolver(linea.LineaId, 1)], new TextoLibre("MOTIVO-SEMBRADO"), "lima-central"));
            Assert.True(solicitada.EsExito);
            if (rechazar)
            {
                Assert.True((await admin.Gestion.Rechazar(solicitada.Valor.DevolucionId)).EsExito);
            }

            ids.Add(solicitada.Valor.DevolucionId);
        }

        var sistema = new EntornoDeDevoluciones(ActorDePrueba.DelSistema()).ConLosDatosDe(cliente);
        sistema.Reloj.Ahora = ahora;
        return (sistema, [.. ids]);
    }

    private static Devolucion Guardada(EntornoDeDevoluciones entorno, Guid id) => entorno.Repositorio.Obtener(id, false).GetAwaiter().GetResult()!;

    [Fact]
    [Trait("Criterio", "AC-DEV-29")]
    public async Task Redacta_a_los_dos_anios_por_dia_de_Lima_el_motivo_y_quita_al_cliente_como_sistema_con_el_conteo_sin_datos_personales()
    {
        var (sistema, id) = await ConRechazada(LimaMedianoche(2026, 11, 19));

        var resultado = await sistema.Depuracion.Ejecutar();

        Assert.Equal(1, resultado.Valor);
        var devolucion = Guardada(sistema, id);
        Assert.True(devolucion.Motivo.EstaRedactado);
        Assert.NotNull(devolucion.RedactadaEn);
        Assert.DoesNotContain(devolucion.Lineas.SelectMany(l => l.Cambios), c => c.Responsable is { Tipo: TipoDeResponsable.Cliente, Id: not null });
        Assert.Equal(1, devolucion.Lineas[0].Cantidad);
        Assert.Equal(EstadoDeLineaDeDevolucion.Rechazada, devolucion.Lineas[0].Estado);
        var entrada = Assert.Single(sistema.Auditoria.Solicitudes, s => s.Accion.ToString() == "DevolucionesDepuradas");
        Assert.Equal(["cantidad"], entrada.Detalle.Keys);
        Assert.Equal("1", entrada.Detalle["cantidad"]);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-29")]
    [InlineData(2026, 11, 19, true)]
    [InlineData(2026, 11, 20, true)]
    [InlineData(2026, 11, 18, false)]
    [InlineData(2025, 11, 20, false)]
    public async Task El_dia_de_Lima_decide_dos_anios_y_un_dia_si_dos_anios_menos_un_dia_no(int anio, int mes, int dia, bool vencida)
    {
        var (sistema, id) = await ConRechazada(LimaMedianoche(anio, mes, dia));

        await sistema.Depuracion.Ejecutar();

        Assert.Equal(vencida, Guardada(sistema, id).Motivo.EstaRedactado);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-32")]
    public async Task Una_devolucion_con_una_linea_no_final_no_se_redacta_aunque_hayan_pasado_los_dos_anios()
    {
        var (sistema, id) = await ConRechazada(LimaMedianoche(2027, 1, 1), rechazar: false);

        var resultado = await sistema.Depuracion.Ejecutar();

        Assert.Equal(0, resultado.Valor);
        Assert.False(Guardada(sistema, id).Motivo.EstaRedactado);
        Assert.DoesNotContain(sistema.Auditoria.Solicitudes, s => s.Accion.ToString() == "DevolucionesDepuradas");
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-29")]
    public async Task Es_idempotente_repetir_la_depuracion_no_cambia_nada_ni_audita_otra_vez()
    {
        var (sistema, _) = await ConRechazada(LimaMedianoche(2026, 11, 19));
        await sistema.Depuracion.Ejecutar();
        var auditorias = sistema.Auditoria.Solicitudes.Count;

        var repetida = await sistema.Depuracion.Ejecutar();

        Assert.Equal(0, repetida.Valor);
        Assert.Equal(auditorias, sistema.Auditoria.Solicitudes.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-32")]
    public async Task Si_no_puede_auditar_o_el_almacen_cae_no_hace_nada_ese_dia_y_reintenta_al_siguiente()
    {
        var (sistema, id) = await ConRechazada(LimaMedianoche(2026, 11, 19));
        sistema.Auditoria.Caida = true;

        var sinAuditoria = await sistema.Depuracion.Ejecutar();
        sistema.Auditoria.Caida = false;
        sistema.Repositorio.NoDisponible = true;
        var sinAlmacen = await sistema.Depuracion.Ejecutar();
        sistema.Repositorio.NoDisponible = false;

        Assert.Equal(CategoriaDeError.NoDisponible, sinAuditoria.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, sinAlmacen.Error!.Categoria);
        Assert.False(Guardada(sistema, id).Motivo.EstaRedactado);
        sistema.Reloj.Ahora = sistema.Reloj.Ahora.AddDays(1);
        Assert.Equal(1, (await sistema.Depuracion.Ejecutar()).Valor);
        Assert.True(Guardada(sistema, id).Motivo.EstaRedactado);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-29")]
    [InlineData("cliente")]
    [InlineData("integracion")]
    [InlineData("anonimo")]
    public async Task Solo_el_sistema_depura_y_cada_intento_de_otro_actor_deja_un_evento(string quien)
    {
        var (sistema, id) = await ConRechazada(LimaMedianoche(2026, 11, 19));
        var actor = quien switch
        {
            "cliente" => ActorDePrueba.DeCliente(),
            "administracion" => ActorDePrueba.DeAdministradorCentral(),
            "integracion" => ActorDePrueba.DeIntegracion("n8n"),
            _ => ActorDePrueba.Anonimo(),
        };
        var otro = new EntornoDeDevoluciones(actor).ConLosDatosDe(sistema);

        var resultado = await otro.Depuracion.Ejecutar();

        Assert.True(resultado.Error!.Categoria is CategoriaDeError.Prohibido or CategoriaDeError.NoAutenticado);
        Assert.Single(otro.Denegaciones.Eventos);
        Assert.False(Guardada(otro, id).Motivo.EstaRedactado);
    }

    // ---------- La vigilancia de las 24 horas ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-32")]
    public async Task A_las_24_horas_sin_una_corrida_correcta_publica_la_alerta_tecnica_y_antes_no()
    {
        var (sistema, _) = await ConRechazada(LimaMedianoche(2026, 11, 19));
        var arranque = sistema.Reloj.Ahora;
        sistema.ControlDeDepuracion.RegistrarCorridaCorrecta(arranque);
        sistema.Reloj.Ahora = arranque.AddHours(23).AddMinutes(59);

        var antes = await sistema.RevisionDeLaDepuracion.Ejecutar();
        sistema.Reloj.Ahora = arranque.AddHours(24);
        var despues = await sistema.RevisionDeLaDepuracion.Ejecutar();

        Assert.False(antes.Valor);
        Assert.True(despues.Valor);
        Assert.Single(sistema.Alertas.Alertas);
    }
}
