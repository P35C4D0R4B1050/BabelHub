using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Biblioteca;

/// <summary>La ayuda, su listado y cierre y la restitución de descargas (T08; BIB-08, BIB-16, AC-BIB-07, AC-BIB-14).</summary>
public sealed class AyudaYRestitucionTests
{
    [Fact]
    [Trait("Criterio", "AC-BIB-14")]
    public async Task Un_acceso_sin_descargas_restantes_pide_ayuda_y_queda_visible_para_la_administracion_con_el_acceso_y_el_motivo()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro", descargas: 5);

        var ayuda = await entorno.PedirAyuda.Ejecutar(new PedirAyudaEntrada(acceso.Id, "descargas-agotadas"));

        Assert.True(ayuda.EsExito);
        Assert.Equal(acceso.Id, ayuda.Valor.AccesoId);
        Assert.Equal("descargas-agotadas", ayuda.Valor.Motivo);
        Assert.Equal(EstadoDeLaAyudaDeBiblioteca.Abierta, ayuda.Valor.Estado);
        Assert.Equal(entorno.Reloj.Ahora, ayuda.Valor.CreadaEn);

        var admin = new EntornoDeBiblioteca(ActorDePrueba.DeAdministradorCentral());
        var lista = await new ListarAyudas(admin.Autorizador, entorno.Descargas).Ejecutar(new ListarAyudasEntrada(EstadoDeLaAyudaDeBiblioteca.Abierta, 1, 20));
        Assert.Equal(ayuda.Valor.AyudaId, Assert.Single(lista.Valor.Ayudas).AyudaId);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-14")]
    public async Task Pedir_ayuda_otra_vez_devuelve_la_abierta_y_el_motivo_es_de_lista_cerrada()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro");

        var primera = await entorno.PedirAyuda.Ejecutar(new PedirAyudaEntrada(acceso.Id, "archivo-no-abre"));
        var segunda = await entorno.PedirAyuda.Ejecutar(new PedirAyudaEntrada(acceso.Id, "otro"));

        Assert.Equal(primera.Valor.AyudaId, segunda.Valor.AyudaId);
        Assert.Single(entorno.Descargas.Ayudas);
        foreach (var texto in new[] { "mi telefono es 999999999", "", null })
        {
            Assert.Equal(CategoriaDeError.Validacion, (await entorno.PedirAyuda.Ejecutar(new PedirAyudaEntrada(acceso.Id, texto))).Error!.Categoria);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-14")]
    public async Task Se_puede_pedir_ayuda_con_el_acceso_suspendido_pero_no_con_el_revocado_ni_sobre_uno_ajeno()
    {
        var entorno = new EntornoDeBiblioteca();
        var suspendido = entorno.SembrarLibro("Suspendido");
        suspendido.Suspender(entorno.Reloj.Ahora);
        var revocado = entorno.SembrarLibro("Revocado");
        revocado.Revocar(revocado.PedidoId, CausaDeRevocacion.Reembolso, entorno.Reloj.Ahora);
        var ajeno = entorno.SembrarLibro("Ajeno", cuenta: Guid.NewGuid());

        Assert.True((await entorno.PedirAyuda.Ejecutar(new PedirAyudaEntrada(suspendido.Id, "archivo-no-abre"))).EsExito);
        Assert.Equal(ClavesDeBiblioteca.AccesoNoDisponible, (await entorno.PedirAyuda.Ejecutar(new PedirAyudaEntrada(revocado.Id, "archivo-no-abre"))).Error!.Clave);
        Assert.Equal(ClavesDeBiblioteca.NoEncontrado, (await entorno.PedirAyuda.Ejecutar(new PedirAyudaEntrada(ajeno.Id, "archivo-no-abre"))).Error!.Clave);
        Assert.Equal(ClavesDeBiblioteca.NoEncontrado, (await entorno.PedirAyuda.Ejecutar(new PedirAyudaEntrada(Guid.NewGuid(), "archivo-no-abre"))).Error!.Clave);
        Assert.Equal(2, entorno.Denegaciones.Eventos.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-14")]
    public async Task Pedir_ayuda_con_textos_pendientes_o_otro_rol_se_rechaza()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro");
        entorno.Cuentas.TextosPendientes = true;
        Assert.Equal(ClavesDeBiblioteca.TextosPendientes, (await entorno.PedirAyuda.Ejecutar(new PedirAyudaEntrada(acceso.Id, "otro"))).Error!.Clave);

        var logistica = new EntornoDeBiblioteca(ActorDePrueba.DePersonalDeLogistica());
        Assert.Equal(CategoriaDeError.Prohibido, (await logistica.PedirAyuda.Ejecutar(new PedirAyudaEntrada(Guid.NewGuid(), "otro"))).Error!.Categoria);
        Assert.Empty(entorno.Descargas.Ayudas);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-14")]
    public async Task La_administracion_restituye_una_descarga_el_titular_puede_descargar_de_nuevo_y_queda_la_auditoria_con_el_motivo()
    {
        var entorno = new EntornoDeBiblioteca(ActorDePrueba.DeAdministradorCentral());
        var acceso = entorno.SembrarLibro("Libro", cuenta: Guid.NewGuid(), descargas: 5);
        var ayuda = SolicitudDeAyuda.Abrir(acceso.Id, MotivoDeAyuda.DescargasAgotadas, entorno.Reloj.Ahora);
        entorno.Descargas.SembrarAyuda(ayuda);

        var resultado = await entorno.Restituir.Ejecutar(new RestituirDescargasEntrada(acceso.Id, 1, "descarga-fallida", ayuda.Id));

        Assert.True(resultado.EsExito);
        Assert.Equal(4, resultado.Valor.DescargasUsadas);
        Assert.Equal(1, resultado.Valor.DescargasRestantes);
        var viva = entorno.Accesos.Buscar(acceso.Id)!;
        Assert.Equal(4, viva.DescargasUsadas);
        Assert.Equal(5, viva.EnlacesEntregados);
        var entrada = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.DescargasRestituidas, entrada.Accion);
        Assert.Equal("descarga-fallida", entrada.Detalle["motivo"]);
        Assert.Equal("1", entrada.Detalle["cantidad"]);
        Assert.Equal(acceso.EdicionId.ToString(), entrada.Detalle["edicionId"]);
        Assert.Equal(EstadoDeAyuda.Cerrada, entorno.Descargas.Ayudas.Single().Estado);
        Assert.Equal(ResultadoDeConsumo.Consumida, viva.ConsumirDescarga());
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-07")]
    public async Task El_titular_descarga_de_nuevo_tras_la_restitucion_con_una_clave_nueva()
    {
        var cuenta = Guid.NewGuid();
        var entorno = new EntornoDeBiblioteca(ActorDePrueba.DeCliente(cuenta));
        var acceso = entorno.SembrarLibro("Libro", descargas: 5);
        var antes = await entorno.Descarga.SolicitarEnlace(acceso.Id, "0123456789abcdef0123456789abcdef");
        Assert.Equal(ClavesDeBiblioteca.LimiteDeDescargas, antes.Error!.Clave);

        var admin = new RestituirDescargas(
            new AutorizadorDeBiblioteca(ActorDePrueba.DeAdministradorCentral(), entorno.Denegaciones, entorno.Cuentas),
            entorno.Accesos,
            entorno.Descargas,
            entorno.Proyector,
            entorno.Auditoria,
            entorno.Unidad,
            entorno.Reloj);
        Assert.True((await admin.Ejecutar(new RestituirDescargasEntrada(acceso.Id, 1, "descarga-fallida"))).EsExito);

        var despues = await entorno.Descarga.SolicitarEnlace(acceso.Id, "fedcba9876543210fedcba9876543210");

        Assert.True(despues.EsExito);
        Assert.Equal(5, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-07")]
    public async Task Restituir_con_otra_cantidad_o_motivo_invalido_o_sobre_un_revocado_se_rechaza_sin_cambiar_nada()
    {
        var entorno = new EntornoDeBiblioteca(ActorDePrueba.DeAdministradorCentral());
        var acceso = entorno.SembrarLibro("Libro", cuenta: Guid.NewGuid(), descargas: 2);
        var revocado = entorno.SembrarLibro("Revocado", cuenta: Guid.NewGuid(), descargas: 2);
        revocado.Revocar(revocado.PedidoId, CausaDeRevocacion.Reembolso, entorno.Reloj.Ahora);

        foreach (var cantidad in new[] { 0, -1, 3 })
        {
            Assert.Equal(CategoriaDeError.Validacion, (await entorno.Restituir.Ejecutar(new RestituirDescargasEntrada(acceso.Id, cantidad, "otro"))).Error!.Categoria);
        }

        Assert.Equal(CategoriaDeError.Validacion, (await entorno.Restituir.Ejecutar(new RestituirDescargasEntrada(acceso.Id, 1, "porque si"))).Error!.Categoria);
        Assert.Equal(ClavesDeBiblioteca.AccesoNoDisponible, (await entorno.Restituir.Ejecutar(new RestituirDescargasEntrada(revocado.Id, 1, "otro"))).Error!.Clave);
        Assert.Equal(CategoriaDeError.NoEncontrado, (await entorno.Restituir.Ejecutar(new RestituirDescargasEntrada(Guid.NewGuid(), 1, "otro"))).Error!.Categoria);
        Assert.Equal(2, acceso.DescargasUsadas);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-07")]
    public async Task Una_ayuda_de_otro_acceso_no_se_cierra_con_la_restitucion()
    {
        var entorno = new EntornoDeBiblioteca(ActorDePrueba.DeAdministradorCentral());
        var acceso = entorno.SembrarLibro("Libro", cuenta: Guid.NewGuid(), descargas: 2);
        var otro = entorno.SembrarLibro("Otro", cuenta: Guid.NewGuid(), descargas: 2);
        var ayudaAjena = SolicitudDeAyuda.Abrir(otro.Id, MotivoDeAyuda.Otro, entorno.Reloj.Ahora);
        entorno.Descargas.SembrarAyuda(ayudaAjena);

        var resultado = await entorno.Restituir.Ejecutar(new RestituirDescargasEntrada(acceso.Id, 1, "otro", ayudaAjena.Id));

        Assert.Equal(CategoriaDeError.NoEncontrado, resultado.Error!.Categoria);
        Assert.Equal(2, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
        Assert.Equal(EstadoDeAyuda.Abierta, entorno.Descargas.Ayudas.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-07")]
    public async Task Sin_poder_auditar_la_restitucion_no_se_aplica()
    {
        var entorno = new EntornoDeBiblioteca(ActorDePrueba.DeAdministradorCentral());
        var acceso = entorno.SembrarLibro("Libro", cuenta: Guid.NewGuid(), descargas: 3);
        var ayuda = SolicitudDeAyuda.Abrir(acceso.Id, MotivoDeAyuda.Otro, entorno.Reloj.Ahora);
        entorno.Descargas.SembrarAyuda(ayuda);
        entorno.Auditoria.Caida = true;

        var resultado = await entorno.Restituir.Ejecutar(new RestituirDescargasEntrada(acceso.Id, 2, "error-del-sistema", ayuda.Id));

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(3, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
        Assert.Equal(EstadoDeAyuda.Abierta, entorno.Descargas.Ayudas.Single().Estado);
    }

    [Theory]
    [InlineData("cliente")]
    [InlineData("logistica")]
    [InlineData("encargado")]
    [Trait("Criterio", "AC-BIB-07")]
    public async Task Solo_la_administracion_central_restituye_lista_y_cierra_ayudas(string rol)
    {
        var actor = rol switch
        {
            "cliente" => ActorDePrueba.DeCliente(),
            "logistica" => ActorDePrueba.DePersonalDeLogistica(),
            _ => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
        };
        var entorno = new EntornoDeBiblioteca(actor);
        var acceso = entorno.SembrarLibro("Libro", cuenta: Guid.NewGuid(), descargas: 2);
        var ayuda = SolicitudDeAyuda.Abrir(acceso.Id, MotivoDeAyuda.Otro, entorno.Reloj.Ahora);
        entorno.Descargas.SembrarAyuda(ayuda);

        Assert.Equal(CategoriaDeError.Prohibido, (await entorno.Restituir.Ejecutar(new RestituirDescargasEntrada(acceso.Id, 1, "otro"))).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, (await entorno.ListarAyudas.Ejecutar(new ListarAyudasEntrada(null, 1, 20))).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, (await entorno.CerrarAyuda.Ejecutar(ayuda.Id)).Error!.Categoria);

        Assert.Equal(3, entorno.Denegaciones.Eventos.Count);
        Assert.Equal(2, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
        Assert.Equal(EstadoDeAyuda.Abierta, entorno.Descargas.Ayudas.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-14")]
    public async Task Cerrar_una_ayuda_la_cierra_una_sola_vez_y_listar_filtra_por_estado_sin_datos_de_la_cuenta()
    {
        var entorno = new EntornoDeBiblioteca(ActorDePrueba.DeAdministradorCentral());
        var acceso = entorno.SembrarLibro("Libro", cuenta: Guid.NewGuid());
        var abierta = SolicitudDeAyuda.Abrir(acceso.Id, MotivoDeAyuda.ArchivoNoAbre, entorno.Reloj.Ahora);
        entorno.Descargas.SembrarAyuda(abierta);
        entorno.Descargas.SembrarAyuda(SolicitudDeAyuda.Reconstruir(Guid.NewGuid(), Guid.NewGuid(), MotivoDeAyuda.Otro, EstadoDeAyuda.Cerrada, entorno.Reloj.Ahora.AddDays(-1), entorno.Reloj.Ahora));
        Assert.Equal(1, (await entorno.ListarAyudas.Ejecutar(new ListarAyudasEntrada(EstadoDeLaAyudaDeBiblioteca.Abierta, 1, 20))).Valor.Total);

        var cerrada = await entorno.CerrarAyuda.Ejecutar(abierta.Id);
        var repetida = await entorno.CerrarAyuda.Ejecutar(abierta.Id);
        var inexistente = await entorno.CerrarAyuda.Ejecutar(Guid.NewGuid());

        Assert.Equal(EstadoDeLaAyudaDeBiblioteca.Cerrada, cerrada.Valor.Estado);
        Assert.Equal(CategoriaDeError.Conflicto, repetida.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
        Assert.Equal(2, (await entorno.ListarAyudas.Ejecutar(new ListarAyudasEntrada(null, 1, 20))).Valor.Total);
        Assert.Equal(2, (await entorno.ListarAyudas.Ejecutar(new ListarAyudasEntrada(EstadoDeLaAyudaDeBiblioteca.Cerrada, 1, 20))).Valor.Total);
        Assert.Equal(0, (await entorno.ListarAyudas.Ejecutar(new ListarAyudasEntrada(EstadoDeLaAyudaDeBiblioteca.Abierta, 1, 20))).Valor.Total);
        Assert.Equal(CategoriaDeError.Validacion, (await entorno.ListarAyudas.Ejecutar(new ListarAyudasEntrada(null, 0, 20))).Error!.Categoria);
        Assert.DoesNotContain(typeof(AyudaDeBiblioteca).GetProperties(), p => p.Name.Contains("Cuenta", StringComparison.Ordinal) || p.Name.Contains("Usuario", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-14")]
    public async Task Las_fachadas_delegan_en_los_casos_de_uso()
    {
        var cliente = new EntornoDeBiblioteca();
        var acceso = cliente.SembrarLibro("Libro", descargas: 1);
        IAyudaDeLaBiblioteca ayuda = new AyudaDeLaBibliotecaDeLaAplicacion(cliente.PedirAyuda);
        Assert.True((await ayuda.Pedir(acceso.Id, "otro")).EsExito);

        var admin = new EntornoDeBiblioteca(ActorDePrueba.DeAdministradorCentral());
        IAdministracionDeLaBiblioteca administracion = new AdministracionDeLaBibliotecaDeLaAplicacion(admin.ConsultarDeUnCliente, admin.ListarAyudas, admin.Restituir, admin.CerrarAyuda);
        Assert.Equal(0, (await administracion.ListarAyudas(null, 1, 20)).Valor.Total);
    }
}
