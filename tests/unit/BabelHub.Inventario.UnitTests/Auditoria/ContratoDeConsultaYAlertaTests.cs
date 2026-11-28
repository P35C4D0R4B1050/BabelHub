using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>
/// Los contratos de consulta y de alerta de la raíz de <c>Auditoria</c> (T08): modelos propios (sin tipos del Dominio, lo vigila
/// <c>ContratoDeRegistroTests</c>), páginas de 20, motivos de alerta de catálogo cerrado y la denegación de acceso con un motivo de código.
/// </summary>
public sealed class ContratoDeConsultaYAlertaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    private static RegistroAuditado Registro(Guid? id = null, DateTimeOffset? instante = null, string accion = "CambioDePrecio", string? detalle = null) =>
        new(
            id ?? Guid.NewGuid(),
            ServicioAuditado.Ecommerce,
            TipoDeRegistroAuditado.Auditoria,
            TipoDeActor.Sistema,
            actorId: null,
            actorRol: null,
            instante ?? Ahora,
            accion,
            ResultadoAuditado.Exitoso) { Detalle = detalle };

    [Fact]
    [Trait("Criterio", "AC-AUD-15")]
    public void Una_pagina_tiene_como_maximo_20_registros()
    {
        var veinte = Enumerable.Range(0, 20).Select(_ => Registro()).ToList();
        var veintiuno = Enumerable.Range(0, 21).Select(_ => Registro()).ToList();

        var pagina = new PaginaDeRegistros(veinte, "cursor-opaco");

        Assert.Equal(20, PaginaDeRegistros.TamanoDePagina);
        Assert.Equal(20, pagina.Registros.Count);
        Assert.False(pagina.FaltanDelInventario);
        Assert.Throws<ArgumentException>(() => new PaginaDeRegistros(veintiuno, null));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-16")]
    public void La_pagina_indica_que_faltan_los_registros_del_Inventario()
    {
        var pagina = new PaginaDeRegistros([Registro()], null, faltanDelInventario: true);

        Assert.True(pagina.FaltanDelInventario);
        Assert.Null(pagina.SiguienteCursor);
    }

    [Fact]
    public void Un_cursor_desmesurado_no_cabe_en_una_pagina()
    {
        Assert.Throws<ArgumentException>(() => new PaginaDeRegistros([], new string('a', PaginaDeRegistros.MaximoDeCaracteresDelCursor + 1)));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-22")]
    public void Un_registro_guarda_su_instante_en_UTC_y_lo_presenta_en_hora_de_Lima()
    {
        var registro = Registro(instante: Ahora);

        Assert.Equal(TimeSpan.Zero, registro.InstanteUtc.Offset);
        Assert.Equal(TimeSpan.FromHours(-5), registro.InstanteEnLima.Offset);
        Assert.Equal(10, registro.InstanteEnLima.Hour);
        Assert.Equal(registro.InstanteUtc, registro.InstanteEnLima);
        Assert.Throws<ArgumentException>(() => Registro(instante: Ahora.ToOffset(TimeSpan.FromHours(-5))));
    }

    [Fact]
    public void Un_registro_exige_identificador_y_accion_y_acota_el_detalle()
    {
        Assert.Throws<ArgumentException>(() => Registro(id: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Registro(accion: ""));
        Assert.Throws<ArgumentException>(() => Registro(detalle: new string('x', 1001)));
        Assert.Equal(1000, Registro(detalle: new string('x', 1000)).Detalle!.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("Auditoria")]
    [InlineData("el pago de 4111 1111 1111 1111 fallo")]
    [InlineData("error: Host=db;Password=x")]
    [InlineData("motivo\n")]
    public void Un_codigo_de_alerta_que_no_es_un_codigo_de_catalogo_se_rechaza(string codigo) =>
        Assert.Throws<ArgumentException>(() => new CodigoDeAlerta(codigo));

    [Theory]
    [InlineData("fuera-de-sucursal")]
    [InlineData("sin-sesion")]
    public void Una_denegacion_lleva_su_motivo_de_catalogo_y_la_ruta_tal_como_llego(string motivo)
    {
        var denegacion = new DenegacionDeAcceso(motivo, "/api/inventario/v1/fuentes/9f3c?x=1");

        Assert.Equal(motivo, denegacion.Motivo);
        Assert.Equal("/api/inventario/v1/fuentes/9f3c?x=1", denegacion.Ruta);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Fuera de sucursal")]
    [InlineData("el usuario ana@correo.pe pidio otra sucursal")]
    public void Una_denegacion_con_un_motivo_que_no_es_un_codigo_se_rechaza(string motivo) =>
        Assert.Throws<ArgumentException>(() => new DenegacionDeAcceso(motivo, "/api/x"));

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public void Una_alerta_consultada_deriva_su_estado_de_lo_revisado()
    {
        var pendiente = Alerta(ocurrencias: 4, revisadasHasta: 3);
        var revisada = Alerta(ocurrencias: 3, revisadasHasta: 3);

        Assert.Equal(EstadoDeAlertaConsultada.Pendiente, pendiente.Estado);
        Assert.Equal(EstadoDeAlertaConsultada.Revisada, revisada.Estado);
        Assert.Throws<ArgumentOutOfRangeException>(() => Alerta(ocurrencias: 3, revisadasHasta: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => Alerta(ocurrencias: 0, revisadasHasta: 0));
    }

    [Fact]
    public void La_alerta_consultada_no_pierde_el_prefijo_del_servicio_en_su_identificador()
    {
        Assert.Throws<ArgumentException>(() => Alerta(id: "sin-prefijo"));
        Assert.Equal("inv:3f2a", Alerta(id: "inv:3f2a", servicio: ServicioAuditado.Inventario).Id);
        Assert.Throws<ArgumentException>(() => Alerta(id: "inv:3f2a"));
        Assert.Equal("eco:3f2a", Alerta(id: "eco:3f2a").Id);
    }

    [Fact]
    public void Una_pagina_de_alertas_indica_cuando_faltan_las_del_Inventario()
    {
        var pagina = new PaginaDeAlertas([Alerta()], null, faltanDelInventario: true);

        Assert.True(pagina.FaltanDelInventario);
        Assert.Single(pagina.Alertas);
    }

    private static AlertaConsultada Alerta(string id = "eco:3f2a", int ocurrencias = 3, int revisadasHasta = 0, ServicioAuditado servicio = ServicioAuditado.Ecommerce) =>
        new(
            id,
            servicio,
            new CodigoDeAlerta("limite-de-ingresos"),
            ClaseDeAlertaConsultada.Seguridad,
            Ahora,
            Ahora.AddMinutes(10),
            ocurrencias,
            revisadasHasta);
}
