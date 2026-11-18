using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>DarDeBajaObra y DarDeBajaEdicion (T09; CAT-13, CAT-18, CAT-19, AC-CAT-10, AC-CAT-15): los datos siguen legibles, las ediciones se quitan de los carritos en la misma transacción y la última edición no se da de baja sola.</summary>
public sealed class BajasDeObraYEdicionTests
{
    private static async Task<(EntornoDelCatalogo Entorno, Obra Obra)> ConAmbas()
    {
        var entorno = new EntornoDelCatalogo();
        var obra = await entorno.ObraRegistrada("Con pedidos", entorno.EdicionImpresa(), entorno.EdicionDigital());
        entorno.Auditoria.Solicitudes.Clear();
        return (entorno, obra);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-10")]
    public async Task Dar_de_baja_una_obra_deja_sus_datos_legibles_por_identificador_con_su_titulo_y_sube_la_version()
    {
        var (entorno, obra) = await ConAmbas();

        var resultado = await entorno.DarDeBajaObra().Ejecutar(new DarDeBajaObraEntrada(obra.Id, obra.Version));

        Assert.True(resultado.EsExito);
        Assert.Equal(obra.Version + 1, resultado.Valor.Version);
        var guardada = (await entorno.Obras.Obtener(obra.Id, false))!;
        Assert.Equal(EstadoDeObra.Baja, guardada.Estado);
        Assert.Equal("Con pedidos", guardada.Titulo);
        Assert.All(guardada.Ediciones, e => Assert.Equal(EstadoDeEdicion.Baja, e.Estado));
        Assert.Equal(entorno.Reloj.Ahora, guardada.DadaDeBajaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-15")]
    public async Task Dar_de_baja_una_obra_quita_todas_sus_ediciones_de_los_carritos_y_la_audita()
    {
        var (entorno, obra) = await ConAmbas();

        await entorno.DarDeBajaObra().Ejecutar(new DarDeBajaObraEntrada(obra.Id, obra.Version));

        var retiro = Assert.Single(entorno.Carritos.Retiros);
        Assert.Equal(obra.Ediciones.Select(e => e.Id).Order(), retiro.Order());
        Assert.Equal(["ObraDadaDeBaja", "EdicionDadaDeBaja", "EdicionDadaDeBaja"], entorno.Auditoria.Solicitudes.Select(s => s.Accion.Identificador).ToList());
        Assert.Equal("Con pedidos", entorno.Auditoria.Solicitudes[0].Detalle["titulo"]);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-10")]
    public async Task La_baja_de_la_obra_conserva_el_archivo_y_la_licencia_de_la_digital_para_los_accesos_concedidos()
    {
        var (entorno, obra) = await ConAmbas();
        var archivo = obra.Ediciones.Single(e => e.EsDigital).Archivo!.ClaveDelAlmacen;

        await entorno.DarDeBajaObra().Ejecutar(new DarDeBajaObraEntrada(obra.Id, obra.Version));

        var digital = (await entorno.Obras.Obtener(obra.Id, false))!.Ediciones.Single(e => e.EsDigital);
        Assert.Equal(archivo, digital.Archivo!.ClaveDelAlmacen);
        Assert.NotNull(digital.Licencia);
        Assert.Empty(entorno.Almacen.Borrados);
        Assert.DoesNotContain(entorno.Referencias.Todas, r => r.Estado == EstadoDeReferencia.Descarte);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-10")]
    public async Task Una_obra_ya_dada_de_baja_o_inexistente_se_rechaza_y_una_version_vieja_da_conflicto()
    {
        var (entorno, obra) = await ConAmbas();

        var vieja = await entorno.DarDeBajaObra().Ejecutar(new DarDeBajaObraEntrada(obra.Id, obra.Version + 3));
        var primera = await entorno.DarDeBajaObra().Ejecutar(new DarDeBajaObraEntrada(obra.Id, obra.Version));
        var repetida = await entorno.DarDeBajaObra().Ejecutar(new DarDeBajaObraEntrada(obra.Id, primera.Valor.Version));
        var inexistente = await entorno.DarDeBajaObra().Ejecutar(new DarDeBajaObraEntrada(Guid.NewGuid(), 1));

        Assert.Equal(ClavesDelCatalogo.ConflictoDeVersion, vieja.Error!.Clave);
        Assert.Equal(ClavesDelCatalogo.ObraDadaDeBaja, repetida.Error!.Clave);
        Assert.Equal(ClavesDelCatalogo.ObraNoEncontrada, inexistente.Error!.Clave);
        Assert.Single(entorno.Carritos.Retiros);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-15")]
    public async Task Dar_de_baja_una_edicion_impresa_la_quita_de_los_carritos_audita_y_deja_la_obra_con_su_digital()
    {
        var (entorno, obra) = await ConAmbas();
        var impresa = obra.Ediciones.Single(e => !e.EsDigital);

        var resultado = await entorno.DarDeBajaEdicion().Ejecutar(new DarDeBajaEdicionEntrada(obra.Id, impresa.Id, obra.Version));

        Assert.True(resultado.EsExito);
        Assert.Equal([impresa.Id], Assert.Single(entorno.Carritos.Retiros));
        var guardada = (await entorno.Obras.Obtener(obra.Id, false))!;
        Assert.Equal(EstadoDeObra.Activa, guardada.Estado);
        Assert.Equal(EstadoDeEdicion.Baja, guardada.BuscarEdicion(impresa.Id)!.Estado);
        Assert.Equal(obra.Version + 1, guardada.Version);
        var auditoria = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal("EdicionDadaDeBaja", auditoria.Accion.Identificador);
    }

    [Fact]
    public async Task La_baja_de_la_ultima_edicion_no_dada_de_baja_se_rechaza_con_obra_sin_ediciones_y_no_cambia_nada()
    {
        var entorno = new EntornoDelCatalogo();
        var obra = await entorno.ObraRegistrada("Una sola", entorno.EdicionImpresa());

        var resultado = await entorno.DarDeBajaEdicion().Ejecutar(new DarDeBajaEdicionEntrada(obra.Id, obra.Ediciones[0].Id, obra.Version));

        Assert.Equal(ClavesDelCatalogo.ObraSinEdiciones, resultado.Error!.Clave);
        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error.Categoria);
        Assert.Empty(entorno.Carritos.Retiros);
        Assert.Equal(EstadoDeEdicion.Activa, (await entorno.Obras.Obtener(obra.Id, false))!.Ediciones[0].Estado);
    }

    [Fact]
    public async Task Una_digital_retirada_cuenta_como_edicion_existente_y_la_otra_si_se_puede_dar_de_baja()
    {
        var (entorno, obra) = await ConAmbas();
        var digital = obra.Ediciones.Single(e => e.EsDigital);
        var impresa = obra.Ediciones.Single(e => !e.EsDigital);
        var copia = RepositorioDeObrasEnMemoria.Clonar(obra);
        copia.RetirarEdicionDigital(digital.Id, "Fin del contrato", entorno.Reloj.Ahora);
        entorno.Obras.Sembrar(copia);

        var resultado = await entorno.DarDeBajaEdicion().Ejecutar(new DarDeBajaEdicionEntrada(obra.Id, impresa.Id, copia.Version));
        var ultima = await entorno.DarDeBajaEdicion().Ejecutar(new DarDeBajaEdicionEntrada(obra.Id, digital.Id, resultado.Valor.Version));

        Assert.True(resultado.EsExito);
        Assert.Equal(ClavesDelCatalogo.ObraSinEdiciones, ultima.Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-10")]
    public async Task Dar_de_baja_una_edicion_digital_conserva_su_archivo_y_la_edicion_sigue_legible()
    {
        var (entorno, obra) = await ConAmbas();
        var digital = obra.Ediciones.Single(e => e.EsDigital);

        await entorno.DarDeBajaEdicion().Ejecutar(new DarDeBajaEdicionEntrada(obra.Id, digital.Id, obra.Version));

        var leida = (await entorno.Obras.ObtenerPorEdicion(digital.Id, false))!.BuscarEdicion(digital.Id)!;
        Assert.Equal(EstadoDeEdicion.Baja, leida.Estado);
        Assert.Equal(digital.Archivo!.ClaveDelAlmacen, leida.Archivo!.ClaveDelAlmacen);
        Assert.Equal(MotivoDeNoDisponible.DadaDeBaja, leida.MotivoDeNoDisponiblePara(entorno.Hoy));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-15")]
    public async Task Si_el_retiro_de_carritos_falla_la_baja_no_se_aplica()
    {
        var (entorno, obra) = await ConAmbas();
        entorno.Carritos.Falla = true;
        var impresa = obra.Ediciones.Single(e => !e.EsDigital);

        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.DarDeBajaEdicion().Ejecutar(new DarDeBajaEdicionEntrada(obra.Id, impresa.Id, obra.Version)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.DarDeBajaObra().Ejecutar(new DarDeBajaObraEntrada(obra.Id, obra.Version)));

        var intacta = (await entorno.Obras.Obtener(obra.Id, false))!;
        Assert.Equal(EstadoDeObra.Activa, intacta.Estado);
        Assert.All(intacta.Ediciones, e => Assert.Equal(EstadoDeEdicion.Activa, e.Estado));
        Assert.Equal(obra.Version, intacta.Version);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-15")]
    public async Task Con_la_auditoria_caida_la_baja_no_se_aplica_aunque_los_carritos_ya_respondieron()
    {
        var (entorno, obra) = await ConAmbas();
        entorno.Auditoria.Caida = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.DarDeBajaObra().Ejecutar(new DarDeBajaObraEntrada(obra.Id, obra.Version)));

        var intacta = (await entorno.Obras.Obtener(obra.Id, false))!;
        Assert.Equal(EstadoDeObra.Activa, intacta.Estado);
        Assert.Equal(obra.Version, intacta.Version);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-11")]
    [InlineData("encargado")]
    [InlineData("cliente")]
    [InlineData("anonimo")]
    public async Task Quien_no_es_la_administracion_central_no_da_de_baja_y_queda_el_acceso_denegado(string quien)
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

        var baja = await otro.DarDeBajaObra().Ejecutar(new DarDeBajaObraEntrada(obra.Id, obra.Version));
        var bajaEdicion = await otro.DarDeBajaEdicion().Ejecutar(new DarDeBajaEdicionEntrada(obra.Id, obra.Ediciones[0].Id, obra.Version));

        Assert.NotNull(baja.Error);
        Assert.Equal(baja.Error.Categoria, bajaEdicion.Error!.Categoria);
        Assert.Equal(2, otro.Denegaciones.Eventos.Count);
        Assert.Empty(otro.Carritos.Retiros);
        Assert.Equal(EstadoDeObra.Activa, (await otro.Obras.Obtener(obra.Id, false))!.Estado);
    }
}
