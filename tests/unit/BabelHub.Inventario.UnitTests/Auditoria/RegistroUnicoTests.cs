using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Auditoria;
using BabelHub.Inventario.UnitTests.Plataforma;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>
/// Un hecho que es a la vez operación sensible y evento de seguridad (un cambio de rol, la declaración de una clave comprometida) deja <b>un único
/// registro</b> con las marcas de auditoría y de seguridad, por <c>IAuditoria</c> (AUD-20, AC-AUD-17, T14).
/// </summary>
public sealed class RegistroUnicoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    private static readonly CatalogoDeAcciones Catalogo = CatalogoDeAccionesDeAuditoria.Crear();

    public static TheoryData<string> AccionesDeAmbosTipos => new(Catalogo.Acciones.Where(a => a.Tipo == TipoDeRegistro.Ambos).Select(a => a.Identificador));

    [Fact]
    [Trait("Criterio", "AC-AUD-17")]
    public void El_catalogo_tiene_acciones_de_ambos_tipos_y_todas_conceden_o_cambian_algo_con_motivo()
    {
        var ambas = Catalogo.Acciones.Where(a => a.Tipo == TipoDeRegistro.Ambos).ToList();

        Assert.Contains(ambas, a => a.Identificador == "CambioDeRol");
        Assert.All(ambas, a =>
        {
            Assert.Equal(ClaseDeAccion.ConcedeOCambia, a.Clase);
            Assert.True(a.ExigeMotivo);
            Assert.NotEmpty(a.Motivos);
        });
    }

    [Theory]
    [MemberData(nameof(AccionesDeAmbosTipos))]
    [Trait("Criterio", "AC-AUD-17")]
    public async Task Un_hecho_de_ambos_tipos_escribe_un_solo_registro_y_no_dos(string identificador)
    {
        var accion = Catalogo.Obtener(identificador);
        var almacen = new AlmacenDeRegistrosEnMemoria();
        var constructor = new ConstructorDeRegistros(ServicioDeOrigen.Ecommerce, Catalogo, new RelojFijo(Ahora), ActorDePrueba.DeAdministradorCentral(), new OrigenFijo(OrigenDeSolicitud.Desconocido));
        var auditoria = new AuditoriaDeAplicacion(constructor, almacen);
        var seguridad = new RegistroDeSeguridadDeAplicacion(constructor, almacen, new AlertasTecnicasEspia(), new RelojFijo(Ahora));
        var solicitud = new SolicitudDeRegistro(new AccionAuditable(identificador, accion.Aislada)) { Motivo = accion.Motivos[0] };

        await auditoria.Registrar(solicitud);
        await Assert.ThrowsAsync<ArgumentException>(() => seguridad.Registrar(solicitud));

        var registro = Assert.Single(almacen.Registros);
        Assert.Equal(TipoDeRegistro.Ambos, registro.Tipo);
        Assert.NotNull(registro.Motivo);
        Assert.NotNull(registro.Origen);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-17")]
    public async Task Si_no_se_puede_registrar_el_hecho_de_ambos_tipos_no_queda_ninguna_marca_a_medias()
    {
        var almacen = new AlmacenDeRegistrosEnMemoria { Falla = new InvalidOperationException("la base no responde") };
        var constructor = new ConstructorDeRegistros(ServicioDeOrigen.Ecommerce, Catalogo, new RelojFijo(Ahora), ActorDePrueba.DeAdministradorCentral(), new OrigenFijo(OrigenDeSolicitud.Desconocido));
        var auditoria = new AuditoriaDeAplicacion(constructor, almacen);

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(
            () => auditoria.Registrar(new SolicitudDeRegistro(AccionesAuditables.CambioDeRol) { Motivo = "asignacion-de-administracion" }));

        Assert.Empty(almacen.Registros);
    }

    [Fact]
    public void Ninguna_accion_se_declara_dos_veces_con_una_marca_de_auditoria_y_otra_de_seguridad()
    {
        // Un hecho de ambos tipos no se parte en «Algo» y «AlgoSeguridad»: si dos acciones difieren solo por ese sufijo, es el defecto que AUD-20 evita.
        var nombres = Catalogo.Acciones.Select(a => a.Identificador).ToHashSet();

        Assert.DoesNotContain(nombres, n => nombres.Contains(n + "Seguridad") || nombres.Contains(n + "DeSeguridad"));
    }
}
