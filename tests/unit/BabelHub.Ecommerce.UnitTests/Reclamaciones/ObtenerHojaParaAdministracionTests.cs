using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Reclamaciones;

/// <summary>Abrir una hoja con sus datos personales: solo la administración central, con la consulta auditada en la misma transacción y fallando cerrada (T07; REC-10, REC-15).</summary>
public sealed class ObtenerHojaParaAdministracionTests
{
    private static readonly Guid Admin = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    [Trait("Criterio", "AC-REC-13")]
    public async Task La_administracion_central_abre_la_hoja_con_sus_datos_personales_y_queda_la_auditoria_con_el_numero()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DeAdministradorCentral(Admin));
        var id = await e.RegistrarUna();

        var resultado = await e.Administracion.Obtener(id);

        Assert.True(resultado.EsExito);
        var detalle = resultado.Valor;
        Assert.Equal("2026-000001", detalle.Numero);
        Assert.Equal("Ana Pérez Quispe", detalle.Nombre);
        Assert.Equal("12345678", detalle.Documento);
        Assert.Equal("Av. Los Olivos 123, Ayacucho", detalle.Domicilio);
        Assert.Equal("Ana.Perez@Correo.COM", detalle.Correo);
        Assert.Equal(49.90m, detalle.MontoReclamado);
        Assert.Equal(EstadoDeLaHoja.Pendiente, detalle.Estado);
        var auditoria = Assert.Single(e.Auditoria.Solicitudes);
        Assert.Equal("HojaDeReclamacionConsultada", auditoria.Accion.Identificador);
        Assert.Equal("hoja-de-reclamacion", auditoria.ObjetoTipo);
        Assert.Equal("2026-000001", auditoria.ObjetoId);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-13")]
    public async Task Cada_consulta_deja_su_propia_entrada_de_auditoria()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DeAdministradorCentral(Admin));
        var id = await e.RegistrarUna();

        await e.Administracion.Obtener(id);
        await e.Administracion.Obtener(id);

        Assert.Equal(2, e.Auditoria.Solicitudes.Count(s => s.Accion.Identificador == "HojaDeReclamacionConsultada"));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-13")]
    public async Task Si_no_se_puede_auditar_la_consulta_no_se_muestra_nada()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DeAdministradorCentral(Admin));
        var id = await e.RegistrarUna();
        e.Auditoria.Caida = true;

        var resultado = await e.Administracion.Obtener(id);

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Empty(e.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-13")]
    public async Task Una_hoja_inexistente_no_se_encuentra_y_no_deja_auditoria_de_consulta()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DeAdministradorCentral(Admin));

        var resultado = await e.Administracion.Obtener(Guid.NewGuid());

        Assert.Equal(CategoriaDeError.NoEncontrado, resultado.Error!.Categoria);
        Assert.Empty(e.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-18")]
    public async Task La_administracion_sigue_viendo_con_sus_datos_la_hoja_de_una_cuenta_cancelada()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DeAdministradorCentral(Admin));
        var id = await e.RegistrarUna();
        var hoja = e.Hojas.Todas.Single();
        hoja.MarcarDeCuentaCancelada();
        e.Hojas.Sembrar(hoja);

        var resultado = await e.Administracion.Obtener(id);

        Assert.True(resultado.EsExito);
        Assert.True(resultado.Valor.DeCuentaCancelada);
        Assert.Equal("Ana Pérez Quispe", resultado.Valor.Nombre);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-16")]
    [InlineData("cliente")]
    [InlineData("encargado")]
    [InlineData("logistica")]
    public async Task Ningun_otro_rol_abre_una_hoja_y_cada_intento_deja_un_acceso_denegado(string rol)
    {
        var admin = new EntornoDeReclamaciones(ActorDePrueba.DeAdministradorCentral(Admin));
        var id = await admin.RegistrarUna();
        var actor = rol switch
        {
            "cliente" => ActorDePrueba.DeCliente(),
            "encargado" => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
            _ => ActorDePrueba.DePersonalDeLogistica(),
        };
        var e = new EntornoDeReclamaciones(actor);
        e.Hojas.Sembrar(admin.Hojas.Todas.Single());

        var primero = await e.Administracion.Obtener(id);
        var segundo = await e.Administracion.Obtener(id);

        Assert.Equal(CategoriaDeError.Prohibido, primero.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, segundo.Error!.Categoria);
        Assert.Equal(2, e.Denegaciones.Eventos.Count);
        Assert.Empty(e.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-16")]
    public async Task Un_visitante_sin_sesion_recibe_no_autenticado_y_no_se_consulta_la_hoja()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.Anonimo());

        var resultado = await e.Administracion.Obtener(Guid.NewGuid());

        Assert.Equal(CategoriaDeError.NoAutenticado, resultado.Error!.Categoria);
        Assert.Equal(0, e.Hojas.Operaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-13")]
    public async Task El_texto_y_la_serializacion_del_detalle_no_repiten_los_datos_personales()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DeAdministradorCentral(Admin));
        var id = await e.RegistrarUna();
        var detalle = (await e.Administracion.Obtener(id)).Valor;

        var texto = detalle.ToString() + System.Text.Json.JsonSerializer.Serialize(detalle);

        Assert.DoesNotContain("Ana", texto);
        Assert.DoesNotContain("12345678", texto);
        Assert.DoesNotContain("Olivos", texto);
        Assert.DoesNotContain("Correo.COM", texto);
        Assert.DoesNotContain("Llegó con las hojas", texto);
    }
}
