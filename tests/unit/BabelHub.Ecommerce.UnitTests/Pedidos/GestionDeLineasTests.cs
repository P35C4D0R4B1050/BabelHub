using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T04 (PED-09, PED-10, PED-15, PED-28, PED-29, AC-PED-07, AC-PED-08, AC-PED-13, AC-PED-24): preparar, marcar lista y entregar una línea, y la política de no recogidos.</summary>
public sealed class GestionDeLineasTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static DatosDeLineaNueva Impresa(string titulo, bool traslado = false, decimal precio = 50m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), false, titulo, "9780000000001", 1, precio, precio, "LIM", traslado, traslado ? Guid.NewGuid() : null, null);

    private static Pedido Crear(TipoDeEntregaDelPedido tipo, params DatosDeLineaNueva[] lineas)
    {
        var contacto = tipo == TipoDeEntregaDelPedido.Domicilio ? ContactoDeEntrega.Crear("Ana", "987654321", "Jr. Lima 123") : null;
        var envio = tipo == TipoDeEntregaDelPedido.Domicilio ? 12m : 0m;
        var datos = new DatosDePedidoNuevo(
            Guid.NewGuid(), "ABCD234567", Cuenta, tipo, tipo == TipoDeEntregaDelPedido.Recojo ? "LIM" : null, tipo == TipoDeEntregaDelPedido.Domicilio ? "ayacucho" : null, envio, lineas.Sum(l => l.Importe) + envio, contacto,
            new AceptacionDeTexto("t-1", Inicio), new AceptacionDeTexto("p-1", Inicio), null, lineas, Inicio);
        var resultado = Pedido.Crear(datos);
        Assert.True(resultado.Creado, resultado.Rechazo?.ToString());
        return resultado.Pedido!;
    }

    private static LineaDePedido De(Pedido pedido, string titulo) => pedido.Lineas.Single(l => l.Titulo == titulo);

    [Fact]
    [Trait("Criterio", "AC-PED-07")]
    public void El_encargado_prepara_y_luego_marca_lista_y_el_pedido_pasa_por_en_preparacion_y_listo_para_recojo()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Uno"));
        var linea = De(pedido, "Uno");

        var preparada = pedido.PrepararLineas([linea.Id], Inicio.AddMinutes(5));
        Assert.Equal(EstadoDePedido.EnPreparacion, pedido.Estado);
        var lista = pedido.MarcarListas([linea.Id], Inicio.AddMinutes(10));

        Assert.Equal(ResultadoDeOperacionDeLinea.Aplicada, Assert.Single(preparada).Resultado);
        Assert.Equal(ResultadoDeOperacionDeLinea.Aplicada, Assert.Single(lista).Resultado);
        Assert.Equal(EstadoDeLineaImpresa.ListoParaRecojo, linea.EstadoImpreso);
        Assert.Equal(Inicio.AddMinutes(10), linea.ListoEn);
        Assert.Equal(EstadoDePedido.ListoParaRecojo, pedido.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-08")]
    public void Una_linea_que_espera_un_traslado_no_se_prepara_y_la_otra_del_mismo_pedido_si()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Espera", traslado: true), Impresa("Aqui"));
        var espera = De(pedido, "Espera");
        var aqui = De(pedido, "Aqui");

        var resultado = pedido.PrepararLineas([espera.Id, aqui.Id], Inicio.AddMinutes(5));

        Assert.Equal(ResultadoDeOperacionDeLinea.EsperaElTraslado, resultado.Single(r => r.LineaId == espera.Id).Resultado);
        Assert.Equal(ResultadoDeOperacionDeLinea.Aplicada, resultado.Single(r => r.LineaId == aqui.Id).Resultado);
        Assert.Equal(EstadoDeLineaImpresa.Confirmado, espera.EstadoImpreso);
        Assert.Equal(EstadoDeLineaImpresa.EnPreparacion, aqui.EstadoImpreso);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-03")]
    public void Los_estados_de_traslado_llevan_la_linea_a_en_transito_y_recibida_con_su_fecha_y_recalculan_el_pedido()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Uno", traslado: true), Impresa("Dos", traslado: true), Impresa("Tres"));
        var uno = De(pedido, "Uno");
        var dos = De(pedido, "Dos");
        var llegada = new DateOnly(2026, 11, 25);

        var transito = pedido.RegistrarTraslado(uno.Id, EstadoDeLineaImpresa.EnTransito, llegada, Inicio);
        var recibidoSinTransito = pedido.RegistrarTraslado(dos.Id, EstadoDeLineaImpresa.Recibido, null, Inicio);
        var sinTraslado = pedido.RegistrarTraslado(De(pedido, "Tres").Id, EstadoDeLineaImpresa.EnTransito, null, Inicio);
        var repetido = pedido.RegistrarTraslado(uno.Id, EstadoDeLineaImpresa.EnTransito, llegada, Inicio);

        Assert.Equal(ResultadoDeOperacionDeLinea.Aplicada, transito);
        Assert.Equal((EstadoDeLineaImpresa.EnTransito, llegada), (uno.EstadoImpreso!.Value, uno.FechaEstimadaDeLlegada!.Value));
        Assert.Equal(ResultadoDeOperacionDeLinea.Aplicada, recibidoSinTransito);
        Assert.Equal(EstadoDeLineaImpresa.Recibido, dos.EstadoImpreso);
        Assert.Equal(ResultadoDeOperacionDeLinea.EstadoInvalido, sinTraslado);
        Assert.Equal(ResultadoDeOperacionDeLinea.EstadoInvalido, repetido);
        Assert.Equal(ResultadoDeOperacionDeLinea.NoEncontrada, pedido.RegistrarTraslado(Guid.NewGuid(), EstadoDeLineaImpresa.EnTransito, null, Inicio));
        Assert.Equal(EstadoDePedido.Confirmado, pedido.Estado);
    }

    [Fact]
    public void Asignar_el_envio_marca_solo_las_lineas_impresas_pedidas_una_vez()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, Impresa("Uno"), Impresa("Dos"));
        var envio = Guid.NewGuid();

        var primera = pedido.AsignarEnvio([De(pedido, "Uno").Id], envio);
        var repetida = pedido.AsignarEnvio([De(pedido, "Uno").Id], envio);

        Assert.Equal(1, primera);
        Assert.Equal(0, repetida);
        Assert.Equal(envio, De(pedido, "Uno").EnvioId);
        Assert.Null(De(pedido, "Dos").EnvioId);
        Assert.Throws<ArgumentException>(() => pedido.AsignarEnvio([], Guid.Empty));
    }

    [Fact]
    public void Una_linea_con_traslado_en_transito_espera_y_una_recibida_se_prepara()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Uno", traslado: true));
        var linea = De(pedido, "Uno");
        Assert.True(linea.PasarA(EstadoDeLineaImpresa.EnTransito, Inicio));

        var enTransito = pedido.PrepararLineas([linea.Id], Inicio);
        Assert.True(linea.PasarA(EstadoDeLineaImpresa.Recibido, Inicio));
        var recibida = pedido.PrepararLineas([linea.Id], Inicio);

        Assert.Equal(ResultadoDeOperacionDeLinea.EsperaElTraslado, Assert.Single(enTransito).Resultado);
        Assert.Equal(ResultadoDeOperacionDeLinea.Aplicada, Assert.Single(recibida).Resultado);
    }

    [Fact]
    public void Preparar_una_linea_que_no_esta_o_ya_preparada_o_digital_se_informa_sin_cambiar_nada()
    {
        var digital = new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), true, "Digital", null, 1, 20m, 20m, null, false, null, null);
        var datos = new DatosDePedidoNuevo(Guid.NewGuid(), "ABCD234567", Cuenta, TipoDeEntregaDelPedido.Recojo, "LIM", null, 0m, 70m, null, new("t-1", Inicio), new("p-1", Inicio), new("r-1", Inicio), [Impresa("Uno"), digital], Inicio);
        var pedido = Pedido.Crear(datos).Pedido!;
        var uno = De(pedido, "Uno");
        Assert.Single(pedido.PrepararLineas([uno.Id], Inicio));

        var repetida = pedido.PrepararLineas([uno.Id], Inicio);
        var ajena = pedido.PrepararLineas([Guid.NewGuid()], Inicio);
        var deDigital = pedido.PrepararLineas([De(pedido, "Digital").Id], Inicio);

        Assert.Equal(ResultadoDeOperacionDeLinea.EstadoInvalido, Assert.Single(repetida).Resultado);
        Assert.Equal(ResultadoDeOperacionDeLinea.NoEncontrada, Assert.Single(ajena).Resultado);
        Assert.Equal(ResultadoDeOperacionDeLinea.EstadoInvalido, Assert.Single(deDigital).Resultado);
        Assert.Equal(EstadoDeLineaImpresa.EnPreparacion, uno.EstadoImpreso);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-23")]
    public void En_domicilio_marcar_listas_es_un_hito_con_fecha_y_la_linea_sigue_en_preparacion()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, Impresa("Uno"));
        var linea = De(pedido, "Uno");
        pedido.PrepararLineas([linea.Id], Inicio);

        var lista = pedido.MarcarListas([linea.Id], Inicio.AddMinutes(30));
        var repetida = pedido.MarcarListas([linea.Id], Inicio.AddMinutes(40));

        Assert.Equal(ResultadoDeOperacionDeLinea.Aplicada, Assert.Single(lista).Resultado);
        Assert.Equal(EstadoDeLineaImpresa.EnPreparacion, linea.EstadoImpreso);
        Assert.Equal(Inicio.AddMinutes(30), linea.ListoEn);
        Assert.Equal(ResultadoDeOperacionDeLinea.EstadoInvalido, Assert.Single(repetida).Resultado);
        Assert.Equal(Inicio.AddMinutes(30), linea.ListoEn);
        Assert.Equal(EstadoDePedido.EnPreparacion, pedido.Estado);
    }

    [Fact]
    public void Marcar_lista_una_linea_que_no_esta_en_preparacion_se_rechaza()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Uno"));

        var resultado = pedido.MarcarListas([De(pedido, "Uno").Id], Inicio);

        Assert.Equal(ResultadoDeOperacionDeLinea.EstadoInvalido, Assert.Single(resultado).Resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-24")]
    public void Una_linea_despachada_se_entrega_con_la_fecha_del_envio_y_el_pedido_queda_entregado()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, Impresa("Uno"));
        var linea = De(pedido, "Uno");
        pedido.PrepararLineas([linea.Id], Inicio);
        pedido.MarcarListas([linea.Id], Inicio.AddHours(1));
        Assert.Equal(ResultadoDeOperacionDeLinea.Aplicada, Assert.Single(pedido.DespacharLineas([linea.Id], Inicio.AddHours(2))).Resultado);
        Assert.Equal(EstadoDePedido.Despachado, pedido.Estado);

        var entregada = pedido.EntregarLineas([linea.Id], Inicio.AddDays(2));

        Assert.Equal(ResultadoDeOperacionDeLinea.Aplicada, Assert.Single(entregada).Resultado);
        Assert.Equal(EstadoDeLineaImpresa.Entregado, linea.EstadoImpreso);
        Assert.Equal(Inicio.AddDays(2), linea.EntregadaEn);
        Assert.Equal(EstadoDePedido.Entregado, pedido.Estado);
        Assert.Equal(Inicio.AddDays(2), pedido.FechaEstadoFinal);
    }

    [Fact]
    public void Despachar_exige_que_la_linea_este_marcada_lista_y_en_preparacion()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, Impresa("Uno"));
        var linea = De(pedido, "Uno");
        pedido.PrepararLineas([linea.Id], Inicio);

        var sinListo = pedido.DespacharLineas([linea.Id], Inicio);

        Assert.Equal(ResultadoDeOperacionDeLinea.EstadoInvalido, Assert.Single(sinListo).Resultado);
        Assert.Equal(EstadoDeLineaImpresa.EnPreparacion, linea.EstadoImpreso);
    }

    [Fact]
    public void Entregar_en_recojo_exige_listo_para_recojo_y_un_pedido_a_medias_no_esta_entregado()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Uno"), Impresa("Dos"));
        var uno = De(pedido, "Uno");
        var dos = De(pedido, "Dos");
        pedido.PrepararLineas([uno.Id, dos.Id], Inicio);
        pedido.MarcarListas([uno.Id], Inicio.AddHours(1));

        var resultado = pedido.EntregarLineas([uno.Id, dos.Id], Inicio.AddHours(2));

        Assert.Equal(ResultadoDeOperacionDeLinea.Aplicada, resultado.Single(r => r.LineaId == uno.Id).Resultado);
        Assert.Equal(ResultadoDeOperacionDeLinea.EstadoInvalido, resultado.Single(r => r.LineaId == dos.Id).Resultado);
        Assert.Equal(EstadoDePedido.EnPreparacion, pedido.Estado);
        Assert.Null(pedido.FechaEstadoFinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PED-13")]
    [InlineData(14, 0, false)]
    [InlineData(15, 0, false)]
    [InlineData(15, 1, true)]
    [InlineData(20, 0, true)]
    public void La_politica_de_no_recogido_vence_con_mas_de_360_horas_en_listo_para_recojo(int dias, int horas, bool vencida)
    {
        var listoEn = Inicio;

        Assert.Equal(vencida, PoliticaDeNoRecogido.EstaVencida(listoEn, listoEn.AddDays(dias).AddHours(horas)));
        Assert.Equal(360, PoliticaDeNoRecogido.PlazoEnHoras);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-13")]
    public void La_revision_marca_no_recogida_solo_la_linea_lista_con_mas_de_15_dias_y_omite_las_en_curso()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Vieja"), Impresa("Reciente"), Impresa("EnCurso"));
        var vieja = De(pedido, "Vieja");
        var reciente = De(pedido, "Reciente");
        var enCurso = De(pedido, "EnCurso");
        pedido.PrepararLineas([vieja.Id, reciente.Id, enCurso.Id], Inicio);
        pedido.MarcarListas([vieja.Id, enCurso.Id], Inicio);
        pedido.MarcarListas([reciente.Id], Inicio.AddDays(10));
        var ahora = Inicio.AddDays(15).AddHours(1);

        var marcadas = pedido.MarcarNoRecogidas(ahora, new HashSet<Guid> { enCurso.Id });

        Assert.Equal([vieja.Id], marcadas);
        Assert.Equal(EstadoDeLineaImpresa.NoRecogido, vieja.EstadoImpreso);
        Assert.Equal(ahora, vieja.NoRecogidaEn);
        Assert.Equal(EstadoDeLineaImpresa.ListoParaRecojo, enCurso.EstadoImpreso);
        Assert.Equal(EstadoDeLineaImpresa.ListoParaRecojo, reciente.EstadoImpreso);
        Assert.Equal(EstadoDePedido.ListoParaRecojo, pedido.Estado);
    }

    [Fact]
    public void Un_pedido_a_domicilio_nunca_tiene_lineas_no_recogidas_y_repetir_la_revision_no_cambia_nada()
    {
        var recojo = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Uno"));
        var linea = De(recojo, "Uno");
        recojo.PrepararLineas([linea.Id], Inicio);
        recojo.MarcarListas([linea.Id], Inicio);
        var ahora = Inicio.AddDays(16);
        Assert.Single(recojo.MarcarNoRecogidas(ahora, new HashSet<Guid>()));

        var domicilio = Crear(TipoDeEntregaDelPedido.Domicilio, Impresa("Dos"));
        domicilio.PrepararLineas([domicilio.Lineas[0].Id], Inicio);
        domicilio.MarcarListas([domicilio.Lineas[0].Id], Inicio);

        Assert.Empty(recojo.MarcarNoRecogidas(ahora.AddDays(1), new HashSet<Guid>()));
        Assert.Empty(domicilio.MarcarNoRecogidas(ahora, new HashSet<Guid>()));
        Assert.Equal(EstadoDePedido.NoRecogido, recojo.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-09")]
    public void Una_linea_no_recogida_no_se_entrega_y_solo_se_anula()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Uno"));
        var linea = De(pedido, "Uno");
        pedido.PrepararLineas([linea.Id], Inicio);
        pedido.MarcarListas([linea.Id], Inicio);
        pedido.MarcarNoRecogidas(Inicio.AddDays(16), new HashSet<Guid>());

        var entregada = pedido.EntregarLineas([linea.Id], Inicio.AddDays(17));

        Assert.Equal(ResultadoDeOperacionDeLinea.EstadoInvalido, Assert.Single(entregada).Resultado);
        Assert.Equal(EstadoDeLineaImpresa.NoRecogido, linea.EstadoImpreso);
        Assert.Equal(EstadoDePedido.NoRecogido, pedido.Estado);
    }
}
