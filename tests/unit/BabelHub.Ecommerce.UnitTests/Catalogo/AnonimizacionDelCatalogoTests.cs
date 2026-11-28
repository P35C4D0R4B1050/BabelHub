using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>AnonimizarIngresosSolicitados (T23; AC-CAT-16, CAT-19): solo el sistema, límite de 2 años, auditoría sin datos personales en la misma transacción y falla cerrada.</summary>
public sealed class AnonimizacionDelCatalogoTests
{
    private sealed class AnonimizadorEspia(AnonimizacionDelCatalogoRealizada resultado) : IAnonimizadorDelCatalogo
    {
        public List<DateTimeOffset> Limites { get; } = [];

        public Task<AnonimizacionDelCatalogoRealizada> Anonimizar(DateTimeOffset limite, CancellationToken cancelacion = default)
        {
            Limites.Add(limite);
            return Task.FromResult(resultado);
        }
    }

    private static AnonimizarIngresosSolicitados Crear(EntornoDelCatalogo e, ActorDePrueba actor, AnonimizadorEspia anonimizador) =>
        new(actor, anonimizador, e.Unidad, e.Auditoria, e.Reloj);

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Solo_el_sistema_anonimiza_un_administrador_un_cliente_o_un_visitante_reciben_prohibido_sin_tocar_nada()
    {
        var e = new EntornoDelCatalogo();
        foreach (var actor in new[] { ActorDePrueba.DeAdministradorCentral(), ActorDePrueba.DeCliente(), ActorDePrueba.Anonimo(), ActorDePrueba.SinContexto(), ActorDePrueba.DeIntegracion("x") })
        {
            var anonimizador = new AnonimizadorEspia(new AnonimizacionDelCatalogoRealizada(3, 0, 0));

            var resultado = await Crear(e, actor, anonimizador).Ejecutar();

            Assert.False(resultado.EsExito);
            Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
            Assert.Empty(anonimizador.Limites);
        }

        Assert.Empty(e.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task El_limite_es_hace_dos_anios_y_la_auditoria_lleva_solo_la_cantidad_en_la_misma_transaccion()
    {
        var e = new EntornoDelCatalogo();
        var anonimizador = new AnonimizadorEspia(new AnonimizacionDelCatalogoRealizada(3, 2, 1));

        var resultado = await Crear(e, ActorDePrueba.DelSistema(), anonimizador).Ejecutar();

        Assert.True(resultado.EsExito);
        Assert.Equal(6, resultado.Valor.Total);
        Assert.Equal(e.Reloj.Ahora.AddYears(-2), Assert.Single(anonimizador.Limites));
        var registro = Assert.Single(e.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.IngresosSolicitadosAnonimizados, registro.Accion);
        Assert.Equal(new Dictionary<string, string?> { ["cantidad"] = "6" }, registro.Detalle);
        Assert.Null(registro.ObjetoId);
        Assert.Equal(1, e.Unidad.Ejecuciones);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Una_corrida_sin_cambios_no_deja_registro_de_auditoria()
    {
        var e = new EntornoDelCatalogo();
        var anonimizador = new AnonimizadorEspia(new AnonimizacionDelCatalogoRealizada(0, 0, 0));

        var resultado = await Crear(e, ActorDePrueba.DelSistema(), anonimizador).Ejecutar();

        Assert.True(resultado.EsExito);
        Assert.Empty(e.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Con_la_auditoria_caida_falla_cerrada_la_excepcion_sale_y_no_queda_registro()
    {
        var e = new EntornoDelCatalogo();
        e.Auditoria.Caida = true;
        var anonimizador = new AnonimizadorEspia(new AnonimizacionDelCatalogoRealizada(4, 0, 0));

        await Assert.ThrowsAnyAsync<Exception>(() => Crear(e, ActorDePrueba.DelSistema(), anonimizador).Ejecutar());

        Assert.Empty(e.Auditoria.Solicitudes);
    }
}
