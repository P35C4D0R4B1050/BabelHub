using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using BabelHub.Ecommerce.Domain.Reclamaciones;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Reclamaciones;

/// <summary>
/// La función de anonimización de la base, imitada sobre el repositorio en memoria con la misma regla (dos años por día de Lima): anonimiza nombre, documento, domicilio y correo, deja el resumen del contenido en
/// nulo, redacta el detalle y la respuesta y conserva el resto.
/// </summary>
internal sealed class AnonimizadorDeHojasDoble(RepositorioDeHojasEnMemoria hojas, RelojFijo reloj) : IAnonimizadorDeHojas
{
    public bool Falla { get; set; }

    public DateTimeOffset? Ultima { get; set; }

    public int Corridas { get; private set; }

    public Task<int> Anonimizar(CancellationToken cancelacion = default)
    {
        Corridas++;
        if (Falla)
        {
            throw new HojasNoDisponiblesException("lock_timeout");
        }

        var ahora = reloj.Ahora;
        var cantidad = 0;
        foreach (var h in hojas.Todas.ToList())
        {
            if (h.EstaAnonimizada || !PoliticaDeConservacion.DebeAnonimizarse(h.FechaRegistro, ahora))
            {
                continue;
            }

            hojas.Sembrar(HojaDeReclamacion.Rehidratar(
                h.Id, h.Numero, h.Tipo, DatosDelConsumidor.Anonimizados(), h.BienContratado, h.MontoReclamado, HojaDeReclamacion.TextoRedactado, h.PedidoReferencia, null, h.FechaRegistro, h.ClaveHash,
                h.Estado, h.Respuesta is null ? null : HojaDeReclamacion.TextoRedactado, h.RespondidaEn, h.RespondidaPor, h.AvisoDeRespuesta, h.AvisoEnviadoEn, h.AvisoEnCursoHasta, h.DescartadaEn, h.DeCuentaCancelada, ahora));
            cantidad++;
        }

        Ultima = ahora;
        return Task.FromResult(cantidad);
    }

    public Task<DateTimeOffset?> UltimaCorridaEn(CancellationToken cancelacion = default) => Falla ? throw new HojasNoDisponiblesException("sin respuesta") : Task.FromResult(Ultima);
}

public sealed class ConservacionDeHojasTests
{
    private static readonly DateTimeOffset Registro = new(2024, 11, 18, 15, 0, 0, TimeSpan.Zero);

    private sealed class Mundo
    {
        public required EntornoDeReclamaciones E { get; init; }

        public required AnonimizadorDeHojasDoble Anonimizador { get; init; }

        public required AnonimizarHojasVencidas Anonimizar { get; init; }

        public required RevisarHojasSinAnonimizar Revisar { get; init; }

        public required AlertasDeReclamacionesEspia Alertas { get; init; }
    }

    private static async Task<Mundo> Armar(ActorDePrueba? actor = null)
    {
        // La hoja se registra el 18/11/2024 y el sistema corre el 18/11/2026 a las 15:00 (hora UTC).
        var e = new EntornoDeReclamaciones(actor ?? ActorDePrueba.DelSistema(), Registro);
        await e.RegistrarUna();
        var anonimizador = new AnonimizadorDeHojasDoble(e.Hojas, e.Reloj);
        var alertas = new AlertasDeReclamacionesEspia();
        return new Mundo
        {
            E = e,
            Anonimizador = anonimizador,
            Anonimizar = new AnonimizarHojasVencidas(new AutorizadorDeReclamaciones(e.Actor, e.Denegaciones), anonimizador, e.Unidad, e.Auditoria),
            Revisar = new RevisarHojasSinAnonimizar(anonimizador, alertas, e.Reloj),
            Alertas = alertas,
        };
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public async Task Una_hoja_que_cumplio_dos_anos_se_anonimiza_y_conserva_numero_fecha_tipo_estado_bien_monto_y_pedido()
    {
        var m = await Armar();
        m.E.Reloj.Ahora = new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
        var antes = m.E.Hojas.Todas.Single();

        var resultado = await m.Anonimizar.Ejecutar();

        Assert.Equal(1, resultado.Valor);
        var hoja = m.E.Hojas.Todas.Single();
        Assert.True(hoja.EstaAnonimizada);
        Assert.Equal(DatosDelConsumidor.ValorAnonimo, hoja.Datos.Nombre);
        Assert.Equal(DatosDelConsumidor.ValorAnonimo, hoja.Datos.Documento);
        Assert.Equal(DatosDelConsumidor.ValorAnonimo, hoja.Datos.Domicilio);
        Assert.Null(hoja.Datos.CorreoNormalizado);
        Assert.Null(hoja.ContenidoHash);
        Assert.Equal(HojaDeReclamacion.TextoRedactado, hoja.Detalle);
        Assert.Equal((antes.Numero, antes.FechaRegistro, antes.Tipo, antes.Estado, antes.BienContratado, antes.MontoReclamado, antes.PedidoReferencia), (hoja.Numero, hoja.FechaRegistro, hoja.Tipo, hoja.Estado, hoja.BienContratado, hoja.MontoReclamado, hoja.PedidoReferencia));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public async Task La_respuesta_tambien_se_redacta_y_la_hoja_respondida_conserva_su_estado()
    {
        var m = await Armar();
        var admin = new EntornoDeReclamaciones(ActorDePrueba.DeAdministradorCentral(), Registro);
        admin.Hojas.Sembrar(m.E.Hojas.Todas.Single());
        await admin.Administracion.Responder(new Application.Reclamaciones.ResponderHojaEntrada(m.E.Hojas.Todas.Single().Id, "Escriba a ana@correo.com"));
        m.E.Hojas.Sembrar(admin.Hojas.Todas.Single());
        m.E.Reloj.Ahora = new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

        await m.Anonimizar.Ejecutar();

        var hoja = m.E.Hojas.Todas.Single();
        Assert.Equal(HojaDeReclamacion.TextoRedactado, hoja.Respuesta);
        Assert.Equal(EstadoDeHoja.Respondido, hoja.Estado);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-15")]
    [InlineData(2026, 11, 17)]
    [InlineData(2026, 11, 18)]
    public async Task La_regla_es_de_dos_anos_por_dia_de_Lima_ni_un_dia_antes(int anio, int mes, int dia)
    {
        var m = await Armar();
        // 18/11/2024 15:00 UTC es 10:00 en Lima: el día 18. Dos años después, el primer día anonimizable es el 18/11/2026 de Lima.
        m.E.Reloj.Ahora = new DateTimeOffset(anio, mes, dia, dia == 18 ? 4 : 23, 0, 0, TimeSpan.Zero);

        var resultado = await m.Anonimizar.Ejecutar();

        // A las 04:00 UTC del 18 todavía es el 17 en Lima; el 17 a las 23:00 UTC es el 17 en Lima.
        Assert.Equal(0, resultado.Valor);
        Assert.False(m.E.Hojas.Todas.Single().EstaAnonimizada);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public async Task Repetir_la_corrida_es_idempotente_y_no_audita_si_no_hay_nada_que_anonimizar()
    {
        var m = await Armar();
        m.E.Reloj.Ahora = new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

        await m.Anonimizar.Ejecutar();
        var segunda = await m.Anonimizar.Ejecutar();

        Assert.Equal(0, segunda.Valor);
        Assert.Single(m.E.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public async Task La_auditoria_lleva_la_causa_y_el_conteo_sin_datos_personales_ni_objeto()
    {
        var m = await Armar();
        m.E.Reloj.Ahora = new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

        await m.Anonimizar.Ejecutar();

        var auditoria = Assert.Single(m.E.Auditoria.Solicitudes);
        Assert.Equal("HojaDeReclamacionAnonimizada", auditoria.Accion.Identificador);
        Assert.Equal("conservacion", auditoria.Detalle["causa"]);
        Assert.Equal("1", auditoria.Detalle["cantidad"]);
        Assert.Equal(["causa", "cantidad"], auditoria.Detalle.Keys);
        Assert.Null(auditoria.ObjetoId);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public async Task Si_no_se_puede_auditar_se_revierte_la_anonimizacion_y_ese_dia_no_pasa_nada()
    {
        var m = await Armar();
        m.E.Reloj.Ahora = new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
        m.E.Auditoria.Caida = true;

        var resultado = await m.Anonimizar.Ejecutar();

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.False(m.E.Hojas.Todas.Single().EstaAnonimizada);
        Assert.Equal("Ana Pérez Quispe", m.E.Hojas.Todas.Single().Datos.Nombre);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public async Task Si_la_base_no_responde_no_se_anonimiza_ni_se_audita_nada()
    {
        var m = await Armar();
        m.E.Reloj.Ahora = new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
        m.Anonimizador.Falla = true;

        var resultado = await m.Anonimizar.Ejecutar();

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Empty(m.E.Auditoria.Solicitudes);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-18")]
    [InlineData("administrador")]
    [InlineData("cliente")]
    [InlineData("integracion")]
    public async Task Solo_el_sistema_corre_la_anonimizacion(string quien)
    {
        var actor = quien switch
        {
            "administrador" => ActorDePrueba.DeAdministradorCentral(),
            "cliente" => ActorDePrueba.DeCliente(),
            _ => ActorDePrueba.DeIntegracion("culqi"),
        };
        var m = await Armar(actor);
        m.E.Reloj.Ahora = new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

        var resultado = await m.Anonimizar.Ejecutar();

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(0, m.Anonimizador.Corridas);
        Assert.Single(m.E.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public async Task La_hoja_de_una_cuenta_cancelada_se_anonimiza_igual_y_conserva_su_marca()
    {
        var m = await Armar();
        var hoja = m.E.Hojas.Todas.Single();
        hoja.MarcarDeCuentaCancelada();
        m.E.Hojas.Sembrar(hoja);
        m.E.Reloj.Ahora = new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

        await m.Anonimizar.Ejecutar();

        Assert.True(m.E.Hojas.Todas.Single().EstaAnonimizada);
        Assert.True(m.E.Hojas.Todas.Single().DeCuentaCancelada);
    }

    // ---------- La revisión de que corre ----------

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public async Task Sin_corrida_en_24_horas_se_publica_una_alerta_tecnica_con_solo_motivo_e_instante()
    {
        var m = await Armar();
        m.Anonimizador.Ultima = m.E.Reloj.Ahora.AddHours(-25);

        var resultado = await m.Revisar.Ejecutar();

        Assert.True(resultado.Valor);
        var alerta = Assert.Single(m.Alertas.Publicadas);
        Assert.Equal(RevisarHojasSinAnonimizar.MotivoDeAlerta, alerta.Motivo);
        Assert.Equal(m.E.Reloj.Ahora, alerta.Instante);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public async Task Con_una_corrida_reciente_no_se_alerta()
    {
        var m = await Armar();
        m.Anonimizador.Ultima = m.E.Reloj.Ahora.AddHours(-23);

        var resultado = await m.Revisar.Ejecutar();

        Assert.False(resultado.Valor);
        Assert.Empty(m.Alertas.Publicadas);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public async Task Si_nunca_corrio_o_no_se_puede_saber_cuando_corrio_se_alerta()
    {
        var m = await Armar();

        Assert.True((await m.Revisar.Ejecutar()).Valor);

        m.Anonimizador.Falla = true;
        Assert.True((await m.Revisar.Ejecutar()).Valor);
        Assert.Equal(2, m.Alertas.Publicadas.Count);
    }
}

internal sealed class AlertasDeReclamacionesEspia : IPublicadorDeAlertasTecnicas
{
    public List<AlertaTecnica> Publicadas { get; } = [];

    public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
    {
        Publicadas.Add(alerta);
        return Task.CompletedTask;
    }
}
