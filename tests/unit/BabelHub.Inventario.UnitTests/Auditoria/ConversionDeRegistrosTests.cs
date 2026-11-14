using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Domain.Auditoria;
using TipoDeActor = BabelHub.Inventario.Domain.Auditoria.TipoDeActor;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>El paso del registro del Dominio al modelo del contrato (T09): conserva todos los campos y mapea los dos <c>TipoDeActor</c> sin ambigüedad.</summary>
public sealed class ConversionDeRegistrosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    private static readonly AccionDelCatalogo IngresoFallido = new(
        "IngresoFallido",
        ClaseDeAccion.NoConcede,
        TipoDeRegistro.Seguridad,
        true,
        [new("cuenta", true)],
        false,
        false,
        ["credenciales-invalidas"],
        true);

    private static readonly AccionDelCatalogo CambioDePrecio = new(
        "CambioDePrecio",
        ClaseDeAccion.ConcedeOCambia,
        TipoDeRegistro.Auditoria,
        false,
        [new("precioNuevo", false)],
        false,
        false,
        [],
        false);

    private static RegistroDeAuditoria Crear(AccionDelCatalogo accion, Actor? actor = null, string? motivo = null, string? origen = null) =>
        RegistroDeAuditoria.Crear(
            Guid.NewGuid(),
            ServicioDeOrigen.Ecommerce,
            accion,
            actor ?? Actor.Usuario(Guid.NewGuid(), "cliente"),
            Ahora,
            "cuenta",
            "u-1",
            null,
            null,
            motivo,
            origen,
            PlantillaDeRuta.ResolverComoRuta("/api/x", ["/api/x"]),
            DetalleSeguro.Construir(accion.Detalle, accion.Detalle.ToDictionary(c => c.Nombre, c => (string?)"Ana Perez")),
            accion.Tipo == TipoDeRegistro.Auditoria ? ResultadoDeOperacion.Exitoso : ResultadoDeOperacion.Rechazado,
            "corr-12345678");

    [Fact]
    [Trait("Criterio", "AC-AUD-15")]
    public void La_conversion_al_modelo_del_contrato_conserva_todos_los_campos()
    {
        var actor = Actor.Usuario(Guid.NewGuid(), "cliente");
        var registro = Crear(IngresoFallido, actor, "credenciales-invalidas", "v1:huella");

        var modelo = registro.AModeloDeContrato();

        Assert.Equal(registro.Id, modelo.Id);
        Assert.Equal(ServicioAuditado.Ecommerce, modelo.Servicio);
        Assert.Equal(TipoDeRegistroAuditado.Seguridad, modelo.Tipo);
        Assert.Equal(BabelHub.Inventario.Application.Plataforma.TipoDeActor.Usuario, modelo.ActorTipo);
        Assert.Equal(actor.Id, modelo.ActorId);
        Assert.Equal("cliente", modelo.ActorRol);
        Assert.Equal(Ahora, modelo.InstanteUtc);
        Assert.Equal("IngresoFallido", modelo.Accion);
        Assert.Equal(ResultadoAuditado.Rechazado, modelo.Resultado);
        Assert.Equal("credenciales-invalidas", modelo.Motivo);
        Assert.Equal("v1:huella", modelo.Origen);
        Assert.Equal("/api/x", modelo.Ruta);
        Assert.Equal(registro.Detalle.Texto, modelo.Detalle);
        Assert.Equal(["cuenta"], modelo.ClavesPersonales);
        Assert.Equal("corr-12345678", modelo.Correlacion);
        Assert.False(modelo.Truncado);
        Assert.Null(modelo.AnonimizadoEn);
    }

    [Theory]
    [InlineData(TipoDeActor.Sistema)]
    [InlineData(TipoDeActor.Integracion)]
    [InlineData(TipoDeActor.Anonimo)]
    public void El_tipo_de_actor_de_cada_servicio_se_mapea_sin_ambiguedad(TipoDeActor tipo)
    {
        var actor = tipo switch { TipoDeActor.Sistema => Actor.Sistema(), TipoDeActor.Integracion => Actor.Integracion(), _ => Actor.Anonimo() };

        var modelo = Crear(CambioDePrecio, actor).AModeloDeContrato();

        Assert.Equal(tipo.ToString(), modelo.ActorTipo.ToString());
        Assert.Null(modelo.ActorId);
    }
}
