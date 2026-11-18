using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>RetirarEdicionDigital (T10; CAT-08, CAT-15, AC-CAT-05): solo digitales, motivo obligatorio, deja de ofrecerse, audita, sube la versión y conserva el archivo y los accesos concedidos.</summary>
public sealed class RetirarEdicionDigitalTests
{
    [Fact]
    [Trait("Criterio", "AC-CAT-05")]
    public async Task Retirar_una_digital_deja_de_ofrecerla_audita_sube_la_version_y_conserva_el_archivo_sin_tocar_carritos()
    {
        var entorno = new EntornoDelCatalogo();
        var obra = await entorno.ObraRegistrada("Con compradores", entorno.EdicionImpresa(), entorno.EdicionDigital());
        var digital = obra.Ediciones.Single(e => e.EsDigital);
        entorno.Auditoria.Solicitudes.Clear();

        var resultado = await entorno.RetirarEdicionDigital().Ejecutar(new RetirarEdicionDigitalEntrada(obra.Id, digital.Id, "  Fin del contrato con la editorial "));

        Assert.True(resultado.EsExito);
        Assert.Equal(obra.Version + 1, resultado.Valor.Version);
        var guardada = (await entorno.Obras.Obtener(obra.Id, false))!.BuscarEdicion(digital.Id)!;
        Assert.Equal(EstadoDeEdicion.RetiradaDeVenta, guardada.Estado);
        Assert.Equal("Fin del contrato con la editorial", guardada.MotivoDeRetiro);
        Assert.Equal(entorno.Reloj.Ahora, guardada.RetiradaEn);
        Assert.False(guardada.SeOfrece(entorno.Hoy));
        Assert.Equal(MotivoDeNoDisponible.RetiradaDeLaVenta, guardada.MotivoDeNoDisponiblePara(entorno.Hoy));
        Assert.Equal(digital.Archivo!.ClaveDelAlmacen, guardada.Archivo!.ClaveDelAlmacen);
        var auditoria = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal("EdicionDigitalRetirada", auditoria.Accion.Identificador);
        Assert.Equal(digital.Id.ToString("D"), auditoria.ObjetoId);
        Assert.DoesNotContain("contrato", string.Join(' ', auditoria.Detalle.Values.Select(v => v ?? string.Empty)), StringComparison.Ordinal);
        Assert.Empty(entorno.Carritos.Retiros);
        Assert.Empty(entorno.Almacen.Borrados);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-05")]
    public async Task Retirar_la_unica_edicion_digital_de_la_obra_se_permite_y_la_obra_conserva_su_edicion()
    {
        var entorno = new EntornoDelCatalogo();
        var obra = await entorno.ObraRegistrada("Solo digital", entorno.EdicionDigital());

        var resultado = await entorno.RetirarEdicionDigital().Ejecutar(new RetirarEdicionDigitalEntrada(obra.Id, obra.Ediciones[0].Id, "Licencia retirada"));

        Assert.True(resultado.EsExito);
        var guardada = (await entorno.Obras.Obtener(obra.Id, false))!;
        Assert.Equal(EstadoDeObra.Activa, guardada.Estado);
        Assert.Single(guardada.Ediciones, e => !e.EstaDadaDeBaja);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-05")]
    public async Task Retirar_una_impresa_o_una_ya_retirada_o_con_motivo_vacio_se_rechaza_sin_cambiar_nada()
    {
        var entorno = new EntornoDelCatalogo();
        var obra = await entorno.ObraRegistrada("Ambas", entorno.EdicionImpresa(), entorno.EdicionDigital());
        var impresa = obra.Ediciones.Single(e => !e.EsDigital);
        var digital = obra.Ediciones.Single(e => e.EsDigital);

        var deImpresa = await entorno.RetirarEdicionDigital().Ejecutar(new RetirarEdicionDigitalEntrada(obra.Id, impresa.Id, "No aplica"));
        var sinMotivo = await entorno.RetirarEdicionDigital().Ejecutar(new RetirarEdicionDigitalEntrada(obra.Id, digital.Id, "  "));
        var largo = await entorno.RetirarEdicionDigital().Ejecutar(new RetirarEdicionDigitalEntrada(obra.Id, digital.Id, new string('m', 301)));
        var inexistente = await entorno.RetirarEdicionDigital().Ejecutar(new RetirarEdicionDigitalEntrada(obra.Id, Guid.NewGuid(), "x"));
        Assert.Equal(obra.Version, (await entorno.Obras.Obtener(obra.Id, false))!.Version);
        var primera = await entorno.RetirarEdicionDigital().Ejecutar(new RetirarEdicionDigitalEntrada(obra.Id, digital.Id, "Retiro"));
        var repetida = await entorno.RetirarEdicionDigital().Ejecutar(new RetirarEdicionDigitalEntrada(obra.Id, digital.Id, "Otra vez"));

        Assert.Equal(CategoriaDeError.Validacion, deImpresa.Error!.Categoria);
        Assert.Contains(new ErrorDeCampo("edicionId", ClavesDelCatalogo.SoloEdicionDigital), deImpresa.Error.Errores);
        Assert.Contains(new ErrorDeCampo("motivo", "obligatorio"), sinMotivo.Error!.Errores);
        Assert.Contains(new ErrorDeCampo("motivo", "demasiado-largo"), largo.Error!.Errores);
        Assert.Equal(ClavesDelCatalogo.EdicionNoEncontrada, inexistente.Error!.Clave);
        Assert.True(primera.EsExito);
        Assert.Equal(ClavesDelCatalogo.EdicionYaRetirada, repetida.Error!.Clave);
        Assert.Equal(CategoriaDeError.Conflicto, repetida.Error.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-05")]
    public async Task Una_edicion_retirada_sigue_legible_con_su_archivo_para_la_biblioteca_y_no_se_vuelve_a_ofrecer_al_cambiar_la_licencia()
    {
        var entorno = new EntornoDelCatalogo();
        var obra = await entorno.ObraRegistrada("Retirada", entorno.EdicionImpresa(), entorno.EdicionDigital());
        var digital = obra.Ediciones.Single(e => e.EsDigital);
        await entorno.RetirarEdicionDigital().Ejecutar(new RetirarEdicionDigitalEntrada(obra.Id, digital.Id, "Retiro"));
        var actual = (await entorno.Obras.Obtener(obra.Id, false))!;

        var cambio = await entorno.EditarEdicion().Ejecutar(new EditarEdicionEntrada(obra.Id, digital.Id, actual.Version, null, entorno.Licencia(hasta: entorno.Hoy.AddYears(3)), null));

        Assert.True(cambio.EsExito);
        var leida = (await entorno.Obras.ObtenerPorEdicion(digital.Id, false))!.BuscarEdicion(digital.Id)!;
        Assert.Equal(EstadoDeEdicion.RetiradaDeVenta, leida.Estado);
        Assert.False(leida.SeOfrece(entorno.Hoy));
        Assert.Equal(digital.Archivo!.ClaveDelAlmacen, leida.Archivo!.ClaveDelAlmacen);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-11")]
    [InlineData("encargado")]
    [InlineData("cliente")]
    [InlineData("anonimo")]
    public async Task Quien_no_es_la_administracion_central_no_retira_una_edicion_y_queda_el_acceso_denegado(string quien)
    {
        var admin = new EntornoDelCatalogo();
        var obra = await admin.ObraRegistrada("Base", admin.EdicionImpresa(), admin.EdicionDigital());
        var otro = new EntornoDelCatalogo(quien switch
        {
            "encargado" => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
            "cliente" => ActorDePrueba.DeCliente(),
            _ => ActorDePrueba.Anonimo(),
        });
        otro.Obras.Sembrar(obra);
        var digital = obra.Ediciones.Single(e => e.EsDigital);

        var resultado = await otro.RetirarEdicionDigital().Ejecutar(new RetirarEdicionDigitalEntrada(obra.Id, digital.Id, "Intento"));

        Assert.NotNull(resultado.Error);
        Assert.Single(otro.Denegaciones.Eventos);
        Assert.Equal(EstadoDeEdicion.Activa, (await otro.Obras.Obtener(obra.Id, false))!.BuscarEdicion(digital.Id)!.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-05")]
    public async Task Con_la_auditoria_caida_el_retiro_no_se_aplica()
    {
        var entorno = new EntornoDelCatalogo();
        var obra = await entorno.ObraRegistrada("Base", entorno.EdicionDigital());
        entorno.Auditoria.Caida = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.RetirarEdicionDigital().Ejecutar(new RetirarEdicionDigitalEntrada(obra.Id, obra.Ediciones[0].Id, "Retiro")));

        var intacta = (await entorno.Obras.Obtener(obra.Id, false))!;
        Assert.Equal(EstadoDeEdicion.Activa, intacta.Ediciones[0].Estado);
        Assert.Equal(obra.Version, intacta.Version);
    }
}
