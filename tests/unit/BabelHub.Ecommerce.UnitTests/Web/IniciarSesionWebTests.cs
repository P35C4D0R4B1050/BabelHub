using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>Abrir la sesión web tras un ingreso correcto (T05, WEB-28).</summary>
public sealed class IniciarSesionWebTests
{
    private static SolicitudDeInicioDeSesion Solicitud(HostWeb host, RolWeb rol, string? anterior = null, bool marca = false) =>
        new(host, DoblesDeSesion.Ingreso(rol), anterior, marca);

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task El_ingreso_de_un_cliente_en_la_tienda_crea_una_sesion_con_un_identificador_opaco_y_solo_guarda_su_hash()
    {
        var d = new DoblesDeSesion();

        var resultado = await d.Iniciar.Ejecutar(Solicitud(HostWeb.Tienda, RolWeb.Cliente), default);

        Assert.True(resultado.EsExito);
        var cookie = resultado.Valor.ValorDeLaCookie;
        Assert.True(ValorDeCookieDeSesion.TieneFormaValida(cookie));
        var fila = Assert.Single(d.Almacen.Filas.Values);
        Assert.Equal(ValorDeCookieDeSesion.Hash(cookie), fila.Sesion.Id);
        Assert.DoesNotContain(cookie, d.Almacen.Filas.Keys);
        Assert.Equal(HostDeSesion.Tienda, fila.Sesion.Host);
        Assert.Equal(DoblesDeSesion.Usuario, fila.Sesion.UsuarioId);
        Assert.NotNull(fila.CredencialCifrada);
        Assert.NotNull(fila.RenovacionCifrada);
        Assert.Equal(DoblesDeSesion.Inicio.AddMinutes(15), fila.CredencialVenceEn);
        Assert.Equal(DoblesDeSesion.Inicio.AddDays(7), fila.Sesion.ExpiraAbsoluta);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task El_personal_recibe_el_tope_de_8_horas()
    {
        var d = new DoblesDeSesion();

        await d.Iniciar.Ejecutar(Solicitud(HostWeb.Gestion, RolWeb.Encargado), default);

        Assert.Equal(DoblesDeSesion.Inicio.AddHours(8), Assert.Single(d.Almacen.Filas.Values).Sesion.ExpiraAbsoluta);
    }

    [Theory]
    [InlineData(HostWeb.Gestion, RolWeb.Cliente)]
    [InlineData(HostWeb.Tienda, RolWeb.Encargado)]
    [InlineData(HostWeb.Tienda, RolWeb.AdministradorCentral)]
    [InlineData(HostWeb.Tienda, RolWeb.Visitante)]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task La_pareja_rol_y_host_que_no_corresponde_falla_como_una_credencial_invalida_y_revoca_la_renovacion(HostWeb host, RolWeb rol)
    {
        var d = new DoblesDeSesion();

        var resultado = await d.Iniciar.Ejecutar(Solicitud(host, rol), default);

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.NoAutenticado, resultado.Error!.Categoria);
        Assert.Equal("credenciales-invalidas", resultado.Error.Clave);
        Assert.Empty(d.Almacen.Filas);
        Assert.Equal(["renov-1"], d.Identidad.Revocadas);
        Assert.Equal(MotivoDeDenegacionWeb.ParejaDeRolYHostInvalida, Assert.Single(d.Seguridad.Eventos).Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task El_error_de_la_pareja_invalida_es_igual_al_de_cualquier_otra_credencial_invalida_y_no_depende_de_que_Identidad_responda_al_revocar()
    {
        var d = new DoblesDeSesion();
        d.Identidad.ConfirmaRevocaciones = false;

        var resultado = await d.Iniciar.Ejecutar(Solicitud(HostWeb.Gestion, RolWeb.Cliente), default);

        Assert.Equal("credenciales-invalidas", resultado.Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Cada_ingreso_emite_un_identificador_nuevo_y_cierra_y_revoca_la_sesion_que_presento_la_cookie_sin_tocar_otras_del_usuario()
    {
        var d = new DoblesDeSesion();
        var anterior = await d.Abrir(renovacion: "renov-vieja");
        var otraDeOtroEquipo = await d.Abrir(renovacion: "renov-otra");
        d.Identidad.Revocadas.Clear();

        var nueva = (await d.Iniciar.Ejecutar(Solicitud(HostWeb.Tienda, RolWeb.Cliente, anterior), default)).Valor.ValorDeLaCookie;

        Assert.NotEqual(anterior, nueva);
        var cerrada = d.Fila(anterior);
        Assert.Equal(EstadoDeSesion.Cerrada, cerrada.Sesion.Estado);
        Assert.Null(cerrada.CredencialCifrada);
        Assert.Null(cerrada.RenovacionCifrada);
        Assert.Equal(["renov-vieja"], d.Identidad.Revocadas);
        Assert.Equal(EstadoDeSesion.Abierta, d.Fila(otraDeOtroEquipo).Sesion.Estado);
        Assert.Equal(EstadoDeSesion.Abierta, d.Fila(nueva).Sesion.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Si_Identidad_no_confirma_la_revocacion_de_la_anterior_la_fila_queda_con_la_revocacion_pendiente()
    {
        var d = new DoblesDeSesion();
        var anterior = await d.Abrir();
        d.Identidad.ConfirmaRevocaciones = false;

        await d.Iniciar.Ejecutar(Solicitud(HostWeb.Tienda, RolWeb.Cliente, anterior), default);

        var fila = d.Fila(anterior);
        Assert.Equal(EstadoDeSesion.Cerrada, fila.Sesion.Estado);
        Assert.Null(fila.CredencialCifrada);
        Assert.NotNull(fila.RenovacionCifrada);
        Assert.True(fila.RevocacionPendiente);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Una_cookie_anterior_mal_formada_o_desconocida_no_estorba_el_ingreso()
    {
        var d = new DoblesDeSesion();

        var mala = await d.Iniciar.Ejecutar(Solicitud(HostWeb.Tienda, RolWeb.Cliente, "no-es-una-cookie"), default);
        var desconocida = await d.Iniciar.Ejecutar(Solicitud(HostWeb.Tienda, RolWeb.Cliente, new string('A', 43)), default);

        Assert.True(mala.EsExito);
        Assert.True(desconocida.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-37")]
    public async Task La_marca_de_dispositivo_se_emite_si_no_hay_una_valida_y_una_valida_no_se_rota()
    {
        var d = new DoblesDeSesion();

        var sinMarca = (await d.Iniciar.Ejecutar(Solicitud(HostWeb.Tienda, RolWeb.Cliente, marca: false), default)).Valor;
        var conMarca = (await d.Iniciar.Ejecutar(Solicitud(HostWeb.Tienda, RolWeb.Cliente, marca: true), default)).Valor;

        Assert.NotNull(sinMarca.MarcaDeDispositivoNueva);
        Assert.Equal(43, sinMarca.MarcaDeDispositivoNueva.Length);
        Assert.Null(conMarca.MarcaDeDispositivoNueva);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task La_credencial_y_la_renovacion_se_guardan_cifradas_y_ligadas_a_su_fila()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(credencial: "jwt-ok-secreto", renovacion: "renov-secreta");
        var fila = d.Fila(cookie);

        // Con los datos asociados de otra fila no se descifran: mover el texto cifrado a otra sesión no sirve.
        var ajeno = ServiciosDeSesion.DatosAsociados(ValorDeCookieDeSesion.Hash(new string('B', 43)), PropositoDeSecreto.Credencial, DoblesDeSesion.Usuario);
        Assert.Throws<SecretoDeSesionIlegibleException>(() => d.Cifrador.Descifrar(fila.CredencialCifrada!, fila.VersionDeClave, PropositoDeSecreto.Credencial, ajeno));
        Assert.Equal(
            "jwt-ok-secreto",
            d.Cifrador.Descifrar(fila.CredencialCifrada!, fila.VersionDeClave, PropositoDeSecreto.Credencial, ServiciosDeSesion.DatosAsociados(fila.Sesion.Id, PropositoDeSecreto.Credencial, DoblesDeSesion.Usuario)));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task El_ingreso_corre_en_una_sola_unidad_de_trabajo()
    {
        var d = new DoblesDeSesion();

        await d.Iniciar.Ejecutar(Solicitud(HostWeb.Tienda, RolWeb.Cliente), default);

        Assert.Equal(1, d.Unidad.Ejecuciones);
    }
}
