using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

public sealed class EnviarAvisosDePagoTests
{
    private static readonly Guid Pedido = InstantaneasDeCompraDePrueba.Pedido;

    /// <summary>Un correo que responde lo que la prueba fije y anota lo que recibió.</summary>
    private sealed class CorreoDoble : IEnviadorDeCorreo
    {
        public ResultadoDeEnvio Resultado { get; set; } = ResultadoDeEnvio.Enviado;

        public bool Lanza { get; set; }

        public List<CorreoSaliente> Enviados { get; } = [];

        public Task<ResultadoDeEnvio> Enviar(CorreoSaliente correo, CancellationToken cancelacion = default)
        {
            if (Lanza)
            {
                throw new InvalidOperationException("SMTP caído.");
            }

            Enviados.Add(correo);
            return Task.FromResult(Resultado);
        }

        public Task<ResultadoDeEnvio> EnviarCodigo(CorreoDeCodigo correo, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class Entorno
    {
        public required EntornoDeConfirmacion Base { get; init; }

        public required CorreoDoble Correo { get; init; }

        public required EnviarAvisosDePago Enviar { get; init; }

        public EntornoDePagos Pagos => Base.Entorno;

        public IEnumerable<AvisoDePago> Avisos => Pagos.Repositorio.AvisosDePago;

        public Task Anotar(EventoDeAvisoDePago evento, Guid? intentoId = null) =>
            Pagos.Repositorio.AgregarAvisoDePago(AvisoDePago.Crear(Pedido, intentoId ?? Base.IntentoId, evento, Pagos.Reloj.Ahora));
    }

    private static async Task<Entorno> Nuevo()
    {
        var b = await EntornoDeConfirmacion.ConIntentoIniciado();
        var correo = new CorreoDoble();
        var p = b.Entorno;
        return new Entorno { Base = b, Correo = correo, Enviar = new EnviarAvisosDePago(p.Repositorio, p.Unidad, p.Cuenta, correo, p.Alertas, p.Reloj) };
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-12")]
    [InlineData(EventoDeAvisoDePago.Pendiente, "pago-pendiente")]
    [InlineData(EventoDeAvisoDePago.Aprobado, "pago-aprobado")]
    [InlineData(EventoDeAvisoDePago.Rechazado, "pago-rechazado")]
    [InlineData(EventoDeAvisoDePago.Vencido, "pago-vencido")]
    [InlineData(EventoDeAvisoDePago.ReembolsoAutomatico, "reembolso-automatico")]
    public async Task Cada_evento_sale_por_correo_con_su_plantilla_a_la_direccion_del_titular_y_queda_enviado(EventoDeAvisoDePago evento, string plantilla)
    {
        var e = await Nuevo();
        await e.Anotar(evento);

        var atendidos = await e.Enviar.Ejecutar(10);

        Assert.Equal(1, atendidos);
        var correo = Assert.Single(e.Correo.Enviados);
        Assert.Equal(CategoriaDeCorreo.Pago, correo.Categoria);
        Assert.Equal(plantilla, correo.Plantilla);
        Assert.Equal("ana@correo.com", correo.Destinatario);
        Assert.Equal(EstadoDeAvisoDePago.Enviado, e.Avisos.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-12")]
    public async Task Los_avisos_no_llevan_datos_de_pago_solo_el_motivo_de_un_rechazo_y_el_importe_de_un_reembolso()
    {
        var e = await Nuevo();
        var intento = e.Base.Intento;
        intento.MarcarRechazado(MotivoDeRechazo.FondosInsuficientes, e.Pagos.Reloj.Ahora);
        await e.Pagos.Repositorio.GuardarIntento(intento);
        await e.Anotar(EventoDeAvisoDePago.Rechazado);
        await e.Anotar(EventoDeAvisoDePago.ReembolsoAutomatico);
        await e.Anotar(EventoDeAvisoDePago.Aprobado);

        await e.Enviar.Ejecutar(10);

        var rechazo = e.Correo.Enviados.Single(c => c.Plantilla == "pago-rechazado");
        Assert.Equal(["motivo"], rechazo.Datos.Keys);
        Assert.Equal("los fondos no fueron suficientes", rechazo.Datos["motivo"]);
        Assert.Equal("S/ 140,00", e.Correo.Enviados.Single(c => c.Plantilla == "reembolso-automatico").Datos["importe"]);
        Assert.Empty(e.Correo.Enviados.Single(c => c.Plantilla == "pago-aprobado").Datos);
        var todo = string.Join(' ', e.Correo.Enviados.SelectMany(c => c.Datos.Values.Concat(c.Datos.Keys)));
        Assert.DoesNotContain("ch_", todo);
        Assert.DoesNotContain("tkn", todo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-19")]
    public async Task El_segundo_rechazo_de_una_intencion_avisa_otra_vez_porque_el_aviso_es_por_intento()
    {
        var e = await Nuevo();
        var segundoIntento = IntentoDeCobro.Iniciar(Guid.NewGuid(), Pedido, Domain.Pagos.MedioDePago.Tarjeta, Importe.De(140m), e.Pagos.Reloj.Ahora);
        e.Base.Intento.MarcarRechazado(MotivoDeRechazo.Otro, e.Pagos.Reloj.Ahora);
        await e.Pagos.Repositorio.GuardarIntento(e.Base.Intento);
        await e.Pagos.Repositorio.AgregarIntento(segundoIntento);
        await e.Anotar(EventoDeAvisoDePago.Rechazado);
        await e.Anotar(EventoDeAvisoDePago.Rechazado, segundoIntento.Id);

        await e.Enviar.Ejecutar(10);

        Assert.Equal(2, e.Correo.Enviados.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-12")]
    public async Task Una_falla_del_correo_no_cambia_el_estado_del_cobro_y_se_reintenta_con_espera()
    {
        var e = await Nuevo();
        await e.Anotar(EventoDeAvisoDePago.Aprobado);
        e.Correo.Lanza = true;
        var estadoDeLaIntencion = e.Base.Intencion.Estado;
        var estadoDelIntento = e.Base.Intento.Estado;

        await e.Enviar.Ejecutar(10);

        Assert.Equal(estadoDeLaIntencion, e.Base.Intencion.Estado);
        Assert.Equal(estadoDelIntento, e.Base.Intento.Estado);
        var aviso = e.Avisos.Single();
        Assert.Equal(EstadoDeAvisoDePago.Pendiente, aviso.Estado);
        Assert.Equal(1, aviso.Intentos);
        Assert.Contains(e.Pagos.Alertas.Publicadas, a => a.Motivo == EnviarAvisosDePago.MotivoDeAlertaAvisoDePagoFallido);

        // Antes de la espera no se reintenta; pasada, sale.
        e.Correo.Lanza = false;
        Assert.Equal(0, await e.Enviar.Ejecutar(10));
        e.Pagos.Reloj.Ahora = e.Pagos.Reloj.Ahora.AddMinutes(1);
        Assert.Equal(1, await e.Enviar.Ejecutar(10));
        Assert.Equal(EstadoDeAvisoDePago.Enviado, e.Avisos.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-12")]
    public async Task Un_correo_encolado_por_la_cuota_de_Plataforma_cuenta_como_enviado_porque_Plataforma_lo_reintenta()
    {
        var e = await Nuevo();
        await e.Anotar(EventoDeAvisoDePago.Aprobado);
        e.Correo.Resultado = ResultadoDeEnvio.Encolado;

        await e.Enviar.Ejecutar(10);

        Assert.Equal(EstadoDeAvisoDePago.Enviado, e.Avisos.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-12")]
    public async Task Un_correo_suprimido_se_reintenta_en_lugar_de_perderse()
    {
        var e = await Nuevo();
        await e.Anotar(EventoDeAvisoDePago.Aprobado);
        e.Correo.Resultado = ResultadoDeEnvio.Suprimido;

        await e.Enviar.Ejecutar(10);

        Assert.Equal(EstadoDeAvisoDePago.Pendiente, e.Avisos.Single().Estado);
        Assert.Equal(1, e.Avisos.Single().Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-26")]
    public async Task Una_intencion_desvinculada_omite_el_aviso_sin_leer_el_correo()
    {
        var e = await Nuevo();
        await e.Anotar(EventoDeAvisoDePago.ReembolsoAutomatico);
        e.Base.Intencion.Desvincular(e.Pagos.Reloj.Ahora);
        await e.Pagos.Repositorio.GuardarIntencion(e.Base.Intencion);

        await e.Enviar.Ejecutar(10);

        Assert.Equal(EstadoDeAvisoDePago.Omitido, e.Avisos.Single().Estado);
        Assert.Equal(0, e.Pagos.Cuenta.CorreosLeidos);
        Assert.Empty(e.Correo.Enviados);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-12")]
    public async Task Una_cuenta_sin_correo_omite_el_aviso()
    {
        var e = await Nuevo();
        await e.Anotar(EventoDeAvisoDePago.Aprobado);
        e.Pagos.Cuenta.Correo = null;

        await e.Enviar.Ejecutar(10);

        Assert.Equal(EstadoDeAvisoDePago.Omitido, e.Avisos.Single().Estado);
        Assert.Empty(e.Correo.Enviados);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-12")]
    public async Task Si_Identidad_no_responde_al_leer_el_correo_el_aviso_se_reintenta_y_no_se_pierde()
    {
        var e = await Nuevo();
        await e.Anotar(EventoDeAvisoDePago.Aprobado);
        e.Pagos.Cuenta.Falla = true;

        await e.Enviar.Ejecutar(10);

        Assert.Equal(EstadoDeAvisoDePago.Pendiente, e.Avisos.Single().Estado);
        Assert.Equal(1, e.Avisos.Single().Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-12")]
    public async Task Un_aviso_ya_enviado_no_se_reenvia_y_el_limite_se_respeta()
    {
        var e = await Nuevo();
        await e.Anotar(EventoDeAvisoDePago.Pendiente);
        await e.Anotar(EventoDeAvisoDePago.Vencido);

        var primero = await e.Enviar.Ejecutar(1);
        var segundo = await e.Enviar.Ejecutar(10);
        var tercero = await e.Enviar.Ejecutar(10);

        Assert.Equal(1, primero);
        Assert.Equal(1, segundo);
        Assert.Equal(0, tercero);
        Assert.Equal(2, e.Correo.Enviados.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-12")]
    public async Task El_texto_del_correo_no_repite_el_destinatario()
    {
        var e = await Nuevo();
        await e.Anotar(EventoDeAvisoDePago.Aprobado);

        await e.Enviar.Ejecutar(10);

        Assert.DoesNotContain("ana@correo.com", e.Correo.Enviados.Single().ToString());
    }
}

public sealed class PlantillasDeCorreoDePagosTests
{
    private static readonly string[] Prohibidos = ["http", "www.", "contraseña", "token", "tarjeta número", "referencia", "ch_", "cvv", "clic", "haz clic"];

    [Fact]
    [Trait("Criterio", "AC-PAG-12")]
    public void Hay_una_plantilla_por_evento_con_nombres_unicos()
    {
        var eventos = Enum.GetValues<EventoDeAvisoDePago>();

        Assert.Equal(eventos.Length, PlantillasDeCorreoDePagos.Catalogo.Count);
        Assert.Equal(eventos.Length, PlantillasDeCorreoDePagos.Catalogo.Select(p => p.Nombre).Distinct().Count());
        Assert.All(eventos, e => Assert.Contains(PlantillasDeCorreoDePagos.Para(e), PlantillasDeCorreoDePagos.Catalogo));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-12")]
    public void Cada_campo_del_texto_esta_declarado_y_cada_campo_declarado_se_usa()
    {
        foreach (var p in PlantillasDeCorreoDePagos.Catalogo)
        {
            var usados = System.Text.RegularExpressions.Regex.Matches(p.Asunto + " " + p.Cuerpo, @"\{(\w+)\}").Select(m => m.Groups[1].Value).Distinct().Order().ToArray();
            Assert.Equal(p.Campos.Order().ToArray(), usados);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-12")]
    public void Ninguna_plantilla_lleva_datos_de_pago_enlaces_ni_peticiones_de_datos_y_sigue_la_guia_de_contenido()
    {
        foreach (var p in PlantillasDeCorreoDePagos.Catalogo)
        {
            var texto = (p.Asunto + " " + p.Cuerpo).ToLowerInvariant();
            Assert.All(Prohibidos, prohibido => Assert.DoesNotContain(prohibido, texto));
            Assert.True(p.Asunto.Length <= 60, p.Nombre);
            Assert.Contains("tu", texto);
            Assert.DoesNotContain("usted", texto);
            Assert.Matches("^[a-z]+(-[a-z]+)*$", p.Nombre);
        }
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-12")]
    [InlineData(0.5, "S/ 0,50")]
    [InlineData(140, "S/ 140,00")]
    [InlineData(1234.5, "S/ 1 234,50")]
    public void El_importe_se_escribe_en_soles_con_el_formato_de_la_guia(double valor, string esperado)
    {
        Assert.Equal(esperado, PlantillasDeCorreoDePagos.ImporteEnSoles((decimal)valor));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-12")]
    public void Todo_motivo_de_rechazo_tiene_un_texto_para_la_persona_y_nunca_el_de_la_pasarela()
    {
        foreach (var motivo in Enum.GetValues<MotivoDeRechazo>())
        {
            var texto = PlantillasDeCorreoDePagos.TextoDelMotivo(motivo);
            Assert.False(string.IsNullOrWhiteSpace(texto));
            Assert.DoesNotContain("_", texto);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-12")]
    public void Un_aviso_se_omite_tras_diez_fallas_y_no_antes()
    {
        var aviso = AvisoDePago.Crear(Guid.NewGuid(), Guid.NewGuid(), EventoDeAvisoDePago.Aprobado, IntencionesDePrueba.Inicio);

        for (var i = 1; i < AvisoDePago.MaximoDeIntentos; i++)
        {
            aviso.RegistrarFalla(IntencionesDePrueba.Inicio);
            Assert.Equal(EstadoDeAvisoDePago.Pendiente, aviso.Estado);
        }

        aviso.RegistrarFalla(IntencionesDePrueba.Inicio);

        Assert.Equal(EstadoDeAvisoDePago.Omitido, aviso.Estado);
    }
}
