using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>Áreas por rol, pareja rol y host, pasos pendientes del ingreso y capacidades con textos pendientes (T03, WEB-02, WEB-34, WEB-42, WEB-43, WEB-46).</summary>
public sealed class PermisosDeAreaTests
{
    [Theory]
    [InlineData(RolDeInterfaz.Visitante, AreaDeInterfaz.Tienda, true)]
    [InlineData(RolDeInterfaz.Cliente, AreaDeInterfaz.Tienda, true)]
    [InlineData(RolDeInterfaz.Cliente, AreaDeInterfaz.Sucursal, false)]
    [InlineData(RolDeInterfaz.Cliente, AreaDeInterfaz.Administracion, false)]
    [InlineData(RolDeInterfaz.Encargado, AreaDeInterfaz.Sucursal, true)]
    [InlineData(RolDeInterfaz.Encargado, AreaDeInterfaz.Administracion, false)]
    [InlineData(RolDeInterfaz.Encargado, AreaDeInterfaz.Tienda, false)]
    [InlineData(RolDeInterfaz.Logistica, AreaDeInterfaz.Sucursal, true)]
    [InlineData(RolDeInterfaz.Logistica, AreaDeInterfaz.Administracion, false)]
    [InlineData(RolDeInterfaz.AdministradorCentral, AreaDeInterfaz.Sucursal, true)]
    [InlineData(RolDeInterfaz.AdministradorCentral, AreaDeInterfaz.Administracion, true)]
    [InlineData(RolDeInterfaz.AdministradorCentral, AreaDeInterfaz.Tienda, false)]
    [Trait("Criterio", "AC-WEB-02")]
    public void Cada_rol_ve_solo_las_areas_que_le_corresponden(RolDeInterfaz rol, AreaDeInterfaz area, bool esperado) =>
        Assert.Equal(esperado, PermisosDeArea.VeLaArea(rol, area));

    [Fact]
    [Trait("Criterio", "AC-WEB-02")]
    public void Un_encargado_no_tiene_Administracion_en_su_lista_de_areas()
    {
        Assert.Equal([AreaDeInterfaz.Sucursal], PermisosDeArea.Areas(RolDeInterfaz.Encargado));
        Assert.Equal([AreaDeInterfaz.Sucursal, AreaDeInterfaz.Administracion], PermisosDeArea.Areas(RolDeInterfaz.AdministradorCentral));
    }

    [Theory]
    [InlineData(AreaDeInterfaz.Tienda, HostDeSesion.Tienda)]
    [InlineData(AreaDeInterfaz.Sucursal, HostDeSesion.Gestion)]
    [InlineData(AreaDeInterfaz.Administracion, HostDeSesion.Gestion)]
    [Trait("Criterio", "AC-WEB-02")]
    public void La_tienda_se_sirve_en_su_host_y_Sucursal_y_Administracion_en_la_gestion(AreaDeInterfaz area, HostDeSesion host) =>
        Assert.Equal(host, PermisosDeArea.HostDeLaArea(area));

    [Theory]
    [InlineData(RolDeInterfaz.Cliente, HostDeSesion.Tienda, true)]
    [InlineData(RolDeInterfaz.Cliente, HostDeSesion.Gestion, false)]
    [InlineData(RolDeInterfaz.Encargado, HostDeSesion.Gestion, true)]
    [InlineData(RolDeInterfaz.Encargado, HostDeSesion.Tienda, false)]
    [InlineData(RolDeInterfaz.Logistica, HostDeSesion.Tienda, false)]
    [InlineData(RolDeInterfaz.AdministradorCentral, HostDeSesion.Tienda, false)]
    [InlineData(RolDeInterfaz.AdministradorCentral, HostDeSesion.Gestion, true)]
    [InlineData(RolDeInterfaz.Visitante, HostDeSesion.Gestion, false)]
    [Trait("Criterio", "AC-WEB-23")]
    public void La_pareja_rol_y_host_que_no_corresponde_se_rechaza(RolDeInterfaz rol, HostDeSesion host, bool valida) =>
        Assert.Equal(valida, PermisosDeArea.ParejaDeRolYHostValida(rol, host));

    [Theory]
    [InlineData(RolDeInterfaz.Visitante, false)]
    [InlineData(RolDeInterfaz.Cliente, false)]
    [InlineData(RolDeInterfaz.Encargado, true)]
    [InlineData(RolDeInterfaz.Logistica, true)]
    [InlineData(RolDeInterfaz.AdministradorCentral, true)]
    [Trait("Criterio", "AC-WEB-22")]
    public void El_personal_tiene_el_tope_de_8_horas(RolDeInterfaz rol, bool personal) =>
        Assert.Equal(personal, rol.EsPersonal());

    [Fact]
    [Trait("Criterio", "AC-WEB-28")]
    public void Los_pasos_del_ingreso_tienen_un_orden_y_el_primero_pendiente_va_antes_que_cualquier_pantalla()
    {
        Assert.True(PasoPendienteDeIngreso.ContrasenaTemporal.Orden() < PasoPendienteDeIngreso.ConfigurarSegundoFactor.Orden());
        Assert.True(PasoPendienteDeIngreso.ConfigurarSegundoFactor.Orden() < PasoPendienteDeIngreso.PresentarSegundoFactor.Orden());
        Assert.Equal(PasoPendienteDeIngreso.ContrasenaTemporal, PasosDelIngreso.Primero([PasoPendienteDeIngreso.PresentarSegundoFactor, PasoPendienteDeIngreso.ContrasenaTemporal]));
        Assert.Equal(PasoPendienteDeIngreso.Ninguno, PasosDelIngreso.Primero([]));
    }

    [Theory]
    [InlineData(PasoPendienteDeIngreso.Ninguno, false)]
    [InlineData(PasoPendienteDeIngreso.ContrasenaTemporal, true)]
    [InlineData(PasoPendienteDeIngreso.ConfigurarSegundoFactor, true)]
    [InlineData(PasoPendienteDeIngreso.PresentarSegundoFactor, true)]
    [Trait("Criterio", "AC-WEB-28")]
    public void Un_paso_pendiente_del_ingreso_manda_sobre_los_textos_legales_y_aceptarlos_nunca_lo_resuelve(PasoPendienteDeIngreso paso, bool prevalece) =>
        Assert.Equal(prevalece, PasosDelIngreso.PrevaleceSobreLosTextos(paso));

    private static readonly CapacidadDeAcceso[] Todas = Enum.GetValues<CapacidadDeAcceso>();

    [Fact]
    [Trait("Criterio", "AC-WEB-36")]
    public void Un_cliente_con_textos_pendientes_ve_su_cuenta_y_sus_pedidos_acepta_cierra_sesion_lee_los_textos_y_usa_el_Libro()
    {
        var permitidas = PoliticaDeAccesoConTextosPendientes.Capacidades(RolDeInterfaz.Cliente, identidadCaida: false);

        Assert.Equal(
            [CapacidadDeAcceso.Cuenta, CapacidadDeAcceso.Pedidos, CapacidadDeAcceso.Aceptar, CapacidadDeAcceso.CerrarSesion, CapacidadDeAcceso.TextosLegales, CapacidadDeAcceso.LibroDeReclamaciones, CapacidadDeAcceso.EnviarHoja],
            permitidas.Order());
    }

    [Theory]
    [InlineData(RolDeInterfaz.Encargado)]
    [InlineData(RolDeInterfaz.Logistica)]
    [InlineData(RolDeInterfaz.AdministradorCentral)]
    [Trait("Criterio", "AC-WEB-37")]
    public void El_personal_con_textos_pendientes_ve_solo_su_cuenta_y_no_sus_pedidos(RolDeInterfaz rol)
    {
        var permitidas = PoliticaDeAccesoConTextosPendientes.Capacidades(rol, identidadCaida: false);

        Assert.Contains(CapacidadDeAcceso.Cuenta, permitidas);
        Assert.DoesNotContain(CapacidadDeAcceso.Pedidos, permitidas);
        Assert.Contains(CapacidadDeAcceso.Aceptar, permitidas);
    }

    [Theory]
    [InlineData(RolDeInterfaz.Cliente)]
    [InlineData(RolDeInterfaz.Encargado)]
    [Trait("Criterio", "AC-WEB-39")]
    public void Con_Identidad_caida_solo_queda_cerrar_sesion_los_textos_el_Libro_y_enviar_una_hoja(RolDeInterfaz rol)
    {
        var permitidas = PoliticaDeAccesoConTextosPendientes.Capacidades(rol, identidadCaida: true);

        Assert.Equal(
            [CapacidadDeAcceso.CerrarSesion, CapacidadDeAcceso.TextosLegales, CapacidadDeAcceso.LibroDeReclamaciones, CapacidadDeAcceso.EnviarHoja],
            permitidas.Order());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-38")]
    public void Cada_capacidad_es_de_lectura_o_de_accion_y_las_acciones_son_una_lista_cerrada()
    {
        var acciones = Todas.Where(c => c.Clase() == ClaseDeCapacidad.Accion).Order().ToArray();

        Assert.Equal([CapacidadDeAcceso.Aceptar, CapacidadDeAcceso.CerrarSesion, CapacidadDeAcceso.EnviarHoja], acciones);
        Assert.All(Todas.Except(acciones), c => Assert.Equal(ClaseDeCapacidad.Lectura, c.Clase()));
    }

    [Theory]
    [InlineData(CapacidadDeAcceso.Cuenta, "GET", true)]
    [InlineData(CapacidadDeAcceso.Cuenta, "POST", false)]
    [InlineData(CapacidadDeAcceso.Cuenta, "HEAD", false)]
    [InlineData(CapacidadDeAcceso.Pedidos, "DELETE", false)]
    [InlineData(CapacidadDeAcceso.TextosLegales, "GET", true)]
    [InlineData(CapacidadDeAcceso.TextosLegales, "get", false)]
    [InlineData(CapacidadDeAcceso.Aceptar, "POST", true)]
    [InlineData(CapacidadDeAcceso.Aceptar, "GET", false)]
    [InlineData(CapacidadDeAcceso.CerrarSesion, "POST", true)]
    [InlineData(CapacidadDeAcceso.EnviarHoja, "POST", true)]
    [InlineData(CapacidadDeAcceso.EnviarHoja, "PUT", false)]
    [InlineData(CapacidadDeAcceso.LibroDeReclamaciones, "OPTIONS", false)]
    [InlineData(CapacidadDeAcceso.LibroDeReclamaciones, "TRACE", false)]
    [Trait("Criterio", "AC-WEB-38")]
    public void Una_lectura_admite_solo_GET_y_una_accion_solo_POST_con_comparacion_ordinal_y_lo_demas_se_niega(CapacidadDeAcceso capacidad, string metodo, bool admitido) =>
        Assert.Equal(admitido, capacidad.AdmiteElMetodo(metodo));

    [Fact]
    [Trait("Criterio", "AC-WEB-38")]
    public void Un_visitante_no_tiene_ninguna_capacidad_con_textos_pendientes()
    {
        Assert.Empty(PoliticaDeAccesoConTextosPendientes.Capacidades(RolDeInterfaz.Visitante, identidadCaida: false));
    }
}
