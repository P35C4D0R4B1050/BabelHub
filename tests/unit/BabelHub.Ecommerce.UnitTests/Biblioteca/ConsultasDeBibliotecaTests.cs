using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Biblioteca;

/// <summary>La biblioteca del cliente, un acceso y la consulta de la administración (T05; BIB-02, BIB-03, BIB-07, BIB-09, BIB-12).</summary>
public sealed class ConsultasDeBibliotecaTests
{
    [Fact]
    [Trait("Criterio", "AC-BIB-02")]
    public async Task Un_cliente_con_cuatro_libros_los_ve_con_formato_fecha_y_dos_de_cinco()
    {
        var entorno = new EntornoDeBiblioteca();
        entorno.SembrarLibro("Cien años de soledad", descargas: 2, formato: FormatoDeArchivoDigital.Pdf, compra: new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero));
        entorno.SembrarLibro("El Aleph", compra: new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero));
        entorno.SembrarLibro("Ficciones", compra: new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero));
        entorno.SembrarLibro("Rayuela", compra: new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero));
        entorno.SembrarLibro("De otra persona", cuenta: Guid.NewGuid());

        var resultado = await entorno.Listar.Ejecutar(new ListarMiBibliotecaEntrada(1, 20));

        Assert.True(resultado.EsExito);
        Assert.Equal(4, resultado.Valor.Total);
        Assert.Equal(["Rayuela", "Ficciones", "El Aleph", "Cien años de soledad"], resultado.Valor.Accesos.Select(a => a.Titulo));
        var primero = resultado.Valor.Accesos[3];
        Assert.Equal(2, primero.DescargasUsadas);
        Assert.Equal(5, primero.DescargasMaximas);
        Assert.Equal(3, primero.DescargasRestantes);
        Assert.Equal(FormatoDeArchivoDigital.Pdf, primero.Formato);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero), primero.FechaDeCompra);
        Assert.Equal(EstadoDelAccesoDigital.Vigente, primero.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-02")]
    public async Task La_lista_pagina_con_tope_de_cincuenta_y_rechaza_valores_invalidos()
    {
        var entorno = new EntornoDeBiblioteca();
        for (var i = 0; i < 3; i++)
        {
            entorno.SembrarLibro($"Libro {i}", compra: new DateTimeOffset(2026, 11, 1 + i, 0, 0, 0, TimeSpan.Zero));
        }

        var segunda = await entorno.Listar.Ejecutar(new ListarMiBibliotecaEntrada(2, 2));
        Assert.Single(segunda.Valor.Accesos);
        Assert.Equal(3, segunda.Valor.Total);

        foreach (var (pagina, tamano) in new[] { (0, 20), (1, 0), (1, 51), (-1, 5) })
        {
            Assert.Equal(CategoriaDeError.Validacion, (await entorno.Listar.Ejecutar(new ListarMiBibliotecaEntrada(pagina, tamano))).Error!.Categoria);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-08")]
    public async Task Una_edicion_con_la_licencia_vencida_o_retirada_sigue_en_la_biblioteca_y_se_puede_obtener()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro con licencia vencida");
        entorno.Catalogo.Poner(acceso.EdicionId, FormatoDeEdicion.Digital, 25m, "Libro con licencia vencida", EstadoDeEdicionConsultado.RetiradaDeVenta, MotivoDeNoDisponibleDeEdicion.LicenciaVencida);

        var lista = await entorno.Listar.Ejecutar(new ListarMiBibliotecaEntrada(1, 20));
        var uno = await entorno.Obtener.Ejecutar(acceso.Id);

        Assert.Equal("Libro con licencia vencida", Assert.Single(lista.Valor.Accesos).Titulo);
        Assert.Equal(EstadoDelAccesoDigital.Vigente, lista.Valor.Accesos[0].Estado);
        Assert.Equal(acceso.Id, uno.Valor.AccesoId);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-09")]
    public async Task Los_accesos_revocados_y_suspendidos_se_ven_con_su_estado_y_la_ayuda_abierta_se_marca()
    {
        var entorno = new EntornoDeBiblioteca();
        var revocado = entorno.SembrarLibro("Revocado");
        revocado.Revocar(revocado.PedidoId, CausaDeRevocacion.Reembolso, entorno.Reloj.Ahora);
        var suspendido = entorno.SembrarLibro("Suspendido");
        suspendido.Suspender(entorno.Reloj.Ahora);
        entorno.Descargas.SembrarAyuda(SolicitudDeAyuda.Abrir(suspendido.Id, MotivoDeAyuda.ArchivoNoAbre, entorno.Reloj.Ahora));

        var lista = (await entorno.Listar.Ejecutar(new ListarMiBibliotecaEntrada(1, 20))).Valor.Accesos;

        Assert.Equal(EstadoDelAccesoDigital.Revocado, lista.Single(a => a.Titulo == "Revocado").Estado);
        var visto = lista.Single(a => a.Titulo == "Suspendido");
        Assert.Equal(EstadoDelAccesoDigital.Suspendido, visto.Estado);
        Assert.True(visto.TieneAyudaAbierta);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-02")]
    public async Task Si_el_formato_no_responde_se_muestra_sin_formato_y_si_el_catalogo_cae_se_falla_cerrado()
    {
        var entorno = new EntornoDeBiblioteca();
        entorno.SembrarLibro("Libro");
        entorno.Referencias.Caida = true;

        var sinFormato = await entorno.Listar.Ejecutar(new ListarMiBibliotecaEntrada(1, 20));
        Assert.Null(Assert.Single(sinFormato.Valor.Accesos).Formato);

        entorno.Catalogo.Caido = true;
        Assert.Equal(CategoriaDeError.NoDisponible, (await entorno.Listar.Ejecutar(new ListarMiBibliotecaEntrada(1, 20))).Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-03")]
    public async Task Un_acceso_ajeno_o_inexistente_responde_lo_mismo_y_registra_un_acceso_denegado_por_intento()
    {
        var entorno = new EntornoDeBiblioteca();
        var ajeno = entorno.SembrarLibro("Ajeno", cuenta: Guid.NewGuid());

        var deAjeno = await entorno.Obtener.Ejecutar(ajeno.Id);
        var inexistente = await entorno.Obtener.Ejecutar(Guid.NewGuid());
        var vacio = await entorno.Obtener.Ejecutar(Guid.Empty);

        foreach (var resultado in new[] { deAjeno, inexistente, vacio })
        {
            Assert.Equal(CategoriaDeError.NoEncontrado, resultado.Error!.Categoria);
            Assert.Equal(ClavesDeBiblioteca.NoEncontrado, resultado.Error.Clave);
        }

        Assert.Equal(3, entorno.Denegaciones.Eventos.Count);
        Assert.All(entorno.Denegaciones.Eventos, e => Assert.Equal("ECO-NO-ENCONTRADO", e.CodigoDeError));
    }

    [Theory]
    [InlineData("encargado")]
    [InlineData("logistica")]
    [InlineData("administrador")]
    [Trait("Criterio", "AC-BIB-10")]
    public async Task Otro_rol_que_el_cliente_no_ve_la_biblioteca_propia_y_se_registra(string rol)
    {
        var actor = rol switch
        {
            "encargado" => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
            "logistica" => ActorDePrueba.DePersonalDeLogistica(),
            _ => ActorDePrueba.DeAdministradorCentral(),
        };
        var entorno = new EntornoDeBiblioteca(actor);

        var resultado = await entorno.Listar.Ejecutar(new ListarMiBibliotecaEntrada(1, 20));

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal("ECO-PROHIBIDO", Assert.Single(entorno.Denegaciones.Eventos).CodigoDeError);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-10")]
    public async Task Un_visitante_o_sin_contexto_no_ve_nada_y_se_registra()
    {
        foreach (var actor in new[] { ActorDePrueba.Anonimo(), ActorDePrueba.SinContexto() })
        {
            var entorno = new EntornoDeBiblioteca(actor);

            var resultado = await entorno.Listar.Ejecutar(new ListarMiBibliotecaEntrada(1, 20));

            Assert.Equal(CategoriaDeError.NoAutenticado, resultado.Error!.Categoria);
            Assert.Single(entorno.Denegaciones.Eventos);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-03")]
    public async Task Con_textos_legales_pendientes_la_biblioteca_se_rechaza_en_la_aplicacion_y_si_identidad_cae_falla_cerrado()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro");
        entorno.Cuentas.TextosPendientes = true;

        Assert.Equal(ClavesDeBiblioteca.TextosPendientes, (await entorno.Listar.Ejecutar(new ListarMiBibliotecaEntrada(1, 20))).Error!.Clave);
        Assert.Equal(ClavesDeBiblioteca.TextosPendientes, (await entorno.Obtener.Ejecutar(acceso.Id)).Error!.Clave);

        entorno.Cuentas.TextosPendientes = false;
        entorno.Cuentas.NoResponde = true;
        Assert.Equal(CategoriaDeError.NoDisponible, (await entorno.Listar.Ejecutar(new ListarMiBibliotecaEntrada(1, 20))).Error!.Categoria);

        entorno.Cuentas.NoResponde = false;
        entorno.Cuentas.Cancelada = true;
        Assert.Equal(CategoriaDeError.NoAutenticado, (await entorno.Listar.Ejecutar(new ListarMiBibliotecaEntrada(1, 20))).Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-10")]
    public async Task El_administrador_central_ve_la_biblioteca_de_un_cliente_y_queda_una_entrada_de_auditoria_sin_los_libros()
    {
        var cliente = Guid.NewGuid();
        var entorno = new EntornoDeBiblioteca(ActorDePrueba.DeAdministradorCentral());
        entorno.SembrarLibro("Secreto del cliente", cuenta: cliente);

        var resultado = await entorno.ConsultarDeUnCliente.Ejecutar(cliente);

        Assert.Equal("Secreto del cliente", Assert.Single(resultado.Valor).Titulo);
        var entrada = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.BibliotecaDeClienteConsultada, entrada.Accion);
        Assert.Equal("cliente", entrada.ObjetoTipo);
        Assert.Equal(cliente.ToString(), entrada.ObjetoId);
        Assert.DoesNotContain(entrada.Detalle.Values, v => v is not null && v.Contains("Secreto", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-10")]
    public async Task La_consulta_de_administracion_falla_cerrada_si_no_se_puede_auditar_y_no_muestra_nada()
    {
        var cliente = Guid.NewGuid();
        var entorno = new EntornoDeBiblioteca(ActorDePrueba.DeAdministradorCentral());
        entorno.SembrarLibro("Libro", cuenta: cliente);
        entorno.Auditoria.Caida = true;

        var resultado = await entorno.ConsultarDeUnCliente.Ejecutar(cliente);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-10")]
    public async Task La_lectura_de_catalogo_ocurre_antes_de_la_transaccion_de_auditoria()
    {
        var cliente = Guid.NewGuid();
        var entorno = new EntornoDeBiblioteca(ActorDePrueba.DeAdministradorCentral());
        entorno.SembrarLibro("Libro", cuenta: cliente);

        await entorno.ConsultarDeUnCliente.Ejecutar(cliente);

        Assert.Equal(1, entorno.Unidad.Ejecuciones);
        Assert.Equal(1, entorno.Catalogo.Consultas);
    }

    [Theory]
    [InlineData("cliente")]
    [InlineData("logistica")]
    [InlineData("encargado")]
    [Trait("Criterio", "AC-BIB-10")]
    public async Task Otro_cliente_o_personal_que_no_es_administracion_central_no_ve_la_biblioteca_de_un_cliente(string rol)
    {
        var actor = rol switch
        {
            "cliente" => ActorDePrueba.DeCliente(),
            "logistica" => ActorDePrueba.DePersonalDeLogistica(),
            _ => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
        };
        var cliente = Guid.NewGuid();
        var entorno = new EntornoDeBiblioteca(actor);
        entorno.SembrarLibro("Libro", cuenta: cliente);

        var resultado = await entorno.ConsultarDeUnCliente.Ejecutar(cliente);

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal("ECO-PROHIBIDO", Assert.Single(entorno.Denegaciones.Eventos).CodigoDeError);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-10")]
    public async Task La_consulta_de_una_cuenta_inexistente_o_con_identidad_caida_no_se_audita()
    {
        var entorno = new EntornoDeBiblioteca(ActorDePrueba.DeAdministradorCentral());

        entorno.Cuentas.NoExiste = true;
        Assert.Equal(CategoriaDeError.NoEncontrado, (await entorno.ConsultarDeUnCliente.Ejecutar(Guid.NewGuid())).Error!.Categoria);
        entorno.Cuentas.NoExiste = false;
        entorno.Cuentas.NoResponde = true;
        Assert.Equal(CategoriaDeError.NoDisponible, (await entorno.ConsultarDeUnCliente.Ejecutar(Guid.NewGuid())).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, (await entorno.ConsultarDeUnCliente.Ejecutar(Guid.Empty)).Error!.Categoria);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-02")]
    public async Task La_fachada_del_cliente_delega_en_los_casos_de_uso()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro");
        IBibliotecaDelCliente fachada = new BibliotecaDelClienteDeLaAplicacion(entorno.Listar, entorno.Obtener);

        Assert.Single((await fachada.Listar(1, 20)).Valor.Accesos);
        Assert.Equal(acceso.Id, (await fachada.Obtener(acceso.Id)).Valor.AccesoId);
    }
}
