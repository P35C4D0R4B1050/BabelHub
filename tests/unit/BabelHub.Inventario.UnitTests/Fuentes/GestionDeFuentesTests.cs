using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Fuentes;

/// <summary>T09: registrar y editar fuentes (FUE-01, FUE-06, FUE-16): solo la administración central, código y nombre únicos, stock cero, auditoría sin el contacto.</summary>
public sealed class GestionDeFuentesTests
{
    private static RegistrarFuenteEntrada Piura(string? contacto = "+51 999 888 777") => new("piura", "Piura", "piura", contacto);

    // --- Registrar ---

    [Fact]
    [Trait("Criterio", "AC-FUE-13")]
    public async Task Registrar_ComoAdministracion_CreaLaFuenteConStockCeroYLaAudita()
    {
        var entorno = new EntornoDeFuentes();

        var resultado = await entorno.RegistrarFuente().Ejecutar(Piura());

        Assert.True(resultado.EsExito);
        var detalle = resultado.Valor;
        Assert.NotEqual(Guid.Empty, detalle.Id);
        Assert.Equal(("piura", "Piura", "piura"), (detalle.Codigo, detalle.Nombre, detalle.Region));
        Assert.False(detalle.EsRecepcion);
        Assert.Equal((0, 0, 0), (detalle.FisicoTotal, detalle.ReservadoTotal, detalle.VendibleTotal));
        Assert.Contains(entorno.Fuentes.Fuentes, f => f.Id == detalle.Id);

        var solicitud = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.FuenteRegistrada, solicitud.Accion);
        Assert.Equal("fuente", solicitud.ObjetoTipo);
        Assert.Equal(detalle.Id.ToString("D"), solicitud.ObjetoId);
        Assert.Equal(detalle.Id, solicitud.SucursalOrigen);
        Assert.Equal(["codigoDeFuente"], solicitud.Detalle.Keys);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-04")]
    public async Task Registrar_UnaSextaFuente_NoNecesitaRecepcionNiFilasDeStock()
    {
        var entorno = new EntornoDeFuentes();
        entorno.Fuentes.Agregar("arequipa", "arequipa");
        entorno.Fuentes.Agregar("piura", "piura");

        var resultado = await entorno.RegistrarFuente().Ejecutar(new RegistrarFuenteEntrada("tacna", "Tacna", "tacna", null));

        Assert.True(resultado.EsExito);
        Assert.Equal(6, entorno.Fuentes.Fuentes.Count);
        Assert.Single(entorno.Fuentes.Fuentes, f => f.EsRecepcion);
        Assert.Empty(entorno.Existencias.Filas);
        Assert.Empty(entorno.Existencias.Movimientos);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public async Task Registrar_LaPrimeraFuente_QuedaComoRecepcion_BajoElBloqueoDeRecepcion()
    {
        var entorno = new EntornoDeFuentes();
        entorno.Fuentes.Fuentes.Clear();

        var resultado = await entorno.RegistrarFuente().Ejecutar(new RegistrarFuenteEntrada("lima", "Lima", "lima", null));

        Assert.True(resultado.Valor.EsRecepcion);
        Assert.Equal(1, entorno.Bloqueos.Recepciones);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public async Task Registrar_TomaElBloqueoDeRecepcionSoloDespuesDeAutorizar()
    {
        var entorno = new EntornoDeFuentes(ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));

        var resultado = await entorno.RegistrarFuente().Ejecutar(Piura());

        Assert.False(resultado.EsExito);
        Assert.Equal(0, entorno.Bloqueos.Recepciones);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-01")]
    public async Task Registrar_UnCodigoRepetido_EsConflictoYNoCambiaNiAudita()
    {
        var entorno = new EntornoDeFuentes();

        var resultado = await entorno.RegistrarFuente().Ejecutar(new RegistrarFuenteEntrada("cusco", "Otro Cusco", "cusco", null));

        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
        Assert.Equal("fuente-duplicada", resultado.Error.Clave);
        Assert.Equal(3, entorno.Fuentes.Fuentes.Count);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-01")]
    public async Task Registrar_UnNombreRepetido_EsConflicto()
    {
        var entorno = new EntornoDeFuentes();

        var resultado = await entorno.RegistrarFuente().Ejecutar(new RegistrarFuenteEntrada("cusco-2", "Cusco", "cusco", null));

        Assert.Equal("fuente-duplicada", resultado.Error!.Clave);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-01")]
    [InlineData(ResultadoDeEscrituraDeFuente.CodigoRepetido)]
    [InlineData(ResultadoDeEscrituraDeFuente.NombreRepetido)]
    public async Task Registrar_UnaAltaSimultanea_QueGanaLaRestriccionUnica_EsConflictoYNoAudita(ResultadoDeEscrituraDeFuente restriccion)
    {
        var entorno = new EntornoDeFuentes();
        entorno.Fuentes.RespuestaForzada = restriccion;

        var resultado = await entorno.RegistrarFuente().Ejecutar(Piura());

        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-13")]
    public async Task Registrar_SiLaAuditoriaFalla_LaExcepcionSubeParaQueLaTransaccionRevierta()
    {
        var entorno = new EntornoDeFuentes();
        entorno.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.RegistrarFuente().Ejecutar(Piura()));

        // La escritura se hizo antes de auditar, dentro de la misma transacción: la unidad de trabajo la deshace (se prueba con PostgreSQL en la integración).
        Assert.Equal(["auditoria.FuenteRegistrada"], entorno.Bitacora.Llamadas.Where(l => l.StartsWith("auditoria", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public async Task Registrar_SiElBloqueoDeRecepcionSeAgota_NoCambiaNadaYEsNoDisponible()
    {
        var entorno = new EntornoDeFuentes();
        entorno.Bloqueos.EsperaAgotada = true;

        var resultado = await entorno.RegistrarFuente().Ejecutar(Piura());

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(3, entorno.Fuentes.Fuentes.Count);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-06")]
    [MemberData(nameof(ActoresSinPermisoDeAdministracion))]
    public async Task Registrar_ConCualquierOtroRol_SeRechazaConSuEventoYSinTocarNada(string etiqueta, ActorDePrueba actor, CategoriaDeError categoria, string motivo)
    {
        var entorno = new EntornoDeFuentes(actor);

        var resultado = await entorno.RegistrarFuente().Ejecutar(Piura());

        Assert.Equal(categoria, resultado.Error!.Categoria);
        Assert.Equal([motivo], entorno.MotivosDeDenegacion);
        Assert.Equal(3, entorno.Fuentes.Fuentes.Count);
        Assert.Equal(0, entorno.Bloqueos.Recepciones);
        Assert.Empty(entorno.Auditoria.Solicitudes);
        Assert.NotNull(etiqueta);
    }

    public static TheoryData<string, ActorDePrueba, CategoriaDeError, string> ActoresSinPermisoDeAdministracion() => new()
    {
        { "encargado", ActorDePrueba.DeEncargadoDe(Guid.NewGuid()), CategoriaDeError.Prohibido, "fuera-de-rol" },
        { "logística", ActorDePrueba.DePersonalDeLogistica(), CategoriaDeError.Prohibido, "fuera-de-rol" },
        { "cliente", ActorDePrueba.DeCliente(), CategoriaDeError.Prohibido, "fuera-de-rol" },
        { "integración", ActorDePrueba.DeIntegracion("e-commerce"), CategoriaDeError.Prohibido, "fuera-de-rol" },
        { "sistema", ActorDePrueba.DelSistema(), CategoriaDeError.Prohibido, "fuera-de-rol" },
        { "visitante", ActorDePrueba.Anonimo(), CategoriaDeError.NoAutenticado, "sin-sesion" },
        { "sin contexto", ActorDePrueba.SinContexto(), CategoriaDeError.NoAutenticado, "sin-sesion" },
    };

    [Theory]
    [Trait("Criterio", "AC-FUE-01")]
    [InlineData("L", "Piura", "piura", null, "codigo")]
    [InlineData("Piura", "Piura", "piura", null, "codigo")]
    [InlineData("piura\n", "Piura", "piura", null, "codigo")]
    [InlineData("piura", " ", "piura", null, "nombre")]
    [InlineData("piura", "P", "piura", null, "nombre")]
    [InlineData("piura", " Piura", "piura", null, "nombre")]
    [InlineData("piura", "Piura", "Piura", null, "region")]
    [InlineData("piura", "Piura", "atlantida", null, "region")]
    [InlineData("piura", "Piura", "piura", "no es un número", "contactoDeNotificacion")]
    [InlineData("piura", "Piura", "piura", "123", "contactoDeNotificacion")]
    public async Task Registrar_ConDatosInvalidos_DevuelveElCampoYNoCambiaNada(string codigo, string nombre, string region, string? contacto, string campo)
    {
        var entorno = new EntornoDeFuentes();

        var resultado = await entorno.RegistrarFuente().Ejecutar(new RegistrarFuenteEntrada(codigo, nombre, region, contacto));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Contains(resultado.Error.Errores, e => e.Campo == campo);
        Assert.Equal(3, entorno.Fuentes.Fuentes.Count);
        Assert.Empty(entorno.Auditoria.Solicitudes);
        Assert.Equal(0, entorno.Bloqueos.Recepciones);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-13")]
    public async Task Registrar_ConLaAuditoriaReal_ElCatalogoAceptaLaAccionYNoGuardaElContacto()
    {
        var entorno = new EntornoDeFuentes();
        var (auditoria, almacen) = entorno.AuditoriaReal();
        var caso = new RegistrarFuente(entorno.Autorizador, new ValidadorDeRegistrarFuente(), entorno.Bloqueos, entorno.Fuentes, auditoria);

        var resultado = await caso.Ejecutar(Piura("+51 999 888 777"));

        var registro = Assert.Single(almacen.Registros);
        Assert.Equal("FuenteRegistrada", registro.Accion);
        Assert.Equal(resultado.Valor.Id.ToString("D"), registro.ObjetoId);
        Assert.Equal("piura", registro.Detalle.Valores["codigoDeFuente"]);
        Assert.DoesNotContain("999", registro.Detalle.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain("999", registro.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-13")]
    public void LasEntradasYElDetalle_QueLlevanElContacto_NoLoImprimen()
    {
        var detalle = new DetalleDeFuente(Guid.NewGuid(), "piura", "Piura", "piura", "+51 999 888 777", false, 0, 0, 0);

        Assert.DoesNotContain("999", detalle.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("999", Piura().ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("999", new EditarFuenteEntrada("piura", "Piura", "piura", "+51 999 888 777").ToString(), StringComparison.Ordinal);
    }

    // --- Editar ---

    [Fact]
    [Trait("Criterio", "AC-FUE-13")]
    public async Task Editar_ElContacto_AuditaSoloElNombreDelCampoYNoSuValor()
    {
        var entorno = new EntornoDeFuentes();
        var piura = entorno.Fuentes.Agregar("piura", "piura");

        var resultado = await entorno.EditarFuente().Ejecutar(new EditarFuenteEntrada("piura", piura.Nombre, "piura", "+51 944 555 666"));

        Assert.True(resultado.EsExito);
        Assert.Equal("+51 944 555 666", resultado.Valor.ContactoDeNotificacion);
        Assert.Equal("+51 944 555 666", entorno.Fuentes.Fuentes.Single(f => f.Codigo == "piura").ContactoDeNotificacion);
        var solicitud = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.FuenteEditada, solicitud.Accion);
        Assert.Equal("contactoDeNotificacion", solicitud.Detalle["camposCambiados"]);
        Assert.Equal("piura", solicitud.Detalle["codigoDeFuente"]);
        Assert.DoesNotContain(solicitud.Detalle.Values, v => v is not null && v.Contains("944", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-13")]
    public async Task Editar_VariosCampos_LosNombraTodosYElCodigoNoCambia()
    {
        var entorno = new EntornoDeFuentes();

        var resultado = await entorno.EditarFuente().Ejecutar(new EditarFuenteEntrada("cusco", "Cusco Centro", "apurimac", null));

        Assert.Equal("cusco", resultado.Valor.Codigo);
        Assert.Equal("Cusco Centro", resultado.Valor.Nombre);
        Assert.Equal("nombre,region", Assert.Single(entorno.Auditoria.Solicitudes).Detalle["camposCambiados"]);
        Assert.Equal(entorno.Cusco.Id, resultado.Valor.Id);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-13")]
    public async Task Editar_SinCambios_NoEscribeNiAudita()
    {
        var entorno = new EntornoDeFuentes();

        var resultado = await entorno.EditarFuente().Ejecutar(new EditarFuenteEntrada("cusco", entorno.Cusco.Nombre, entorno.Cusco.Region, null));

        Assert.True(resultado.EsExito);
        Assert.Empty(entorno.Auditoria.Solicitudes);
        Assert.Empty(entorno.Fuentes.Escritas);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-13")]
    public async Task Editar_DevuelveLosTotalesDeLaFuenteConLasVencidas()
    {
        var entorno = new EntornoDeFuentes();
        var libro = entorno.Impresa();
        entorno.Existencias.Poner(entorno.Cusco.Id, libro, 5, 5);
        entorno.Reservas.Vencidas.Add(new UnidadesVencidas(entorno.Cusco.Id, libro, 2));

        var resultado = await entorno.EditarFuente().Ejecutar(new EditarFuenteEntrada("cusco", "Cusco Centro", "cusco", null));

        Assert.Equal((5, 5, 2), (resultado.Valor.FisicoTotal, resultado.Valor.ReservadoTotal, resultado.Valor.VendibleTotal));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-13")]
    public async Task Editar_UnaFuenteInexistente_EsNoEncontradaYUnNombreRepetidoEsConflicto()
    {
        var entorno = new EntornoDeFuentes();

        var inexistente = await entorno.EditarFuente().Ejecutar(new EditarFuenteEntrada("piura", "Piura", "piura", null));
        var repetido = await entorno.EditarFuente().Ejecutar(new EditarFuenteEntrada("cusco", entorno.Lima.Nombre, "cusco", null));

        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Conflicto, repetido.Error!.Categoria);
        Assert.Empty(entorno.Auditoria.Solicitudes);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-06")]
    [MemberData(nameof(ActoresSinPermisoDeAdministracion))]
    public async Task Editar_ConCualquierOtroRol_SeRechazaConSuEventoYSinTocarNada(string etiqueta, ActorDePrueba actor, CategoriaDeError categoria, string motivo)
    {
        var entorno = new EntornoDeFuentes(actor);

        var resultado = await entorno.EditarFuente().Ejecutar(new EditarFuenteEntrada("cusco", "Cusco Centro", "cusco", null));

        Assert.Equal(categoria, resultado.Error!.Categoria);
        Assert.Equal([motivo], entorno.MotivosDeDenegacion);
        Assert.Empty(entorno.Fuentes.Escritas);
        Assert.Empty(entorno.Auditoria.Solicitudes);
        Assert.Equal("Cusco", entorno.Cusco.Nombre);
        Assert.NotNull(etiqueta);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-13")]
    public async Task Editar_SiLaAuditoriaFalla_LaExcepcionSubeParaQueLaTransaccionRevierta()
    {
        var entorno = new EntornoDeFuentes();
        entorno.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.EditarFuente().Ejecutar(new EditarFuenteEntrada("cusco", "Cusco Centro", "cusco", null)));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-13")]
    public async Task Editar_ConDatosInvalidos_DevuelveElCampo()
    {
        var entorno = new EntornoDeFuentes();

        var resultado = await entorno.EditarFuente().Ejecutar(new EditarFuenteEntrada("cusco", "Cusco", "narnia", null));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Contains(resultado.Error.Errores, e => e.Campo == "region");
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-13")]
    public async Task Editar_ConLaAuditoriaReal_ElCatalogoAceptaLaAccionYLosCamposCambiados()
    {
        var entorno = new EntornoDeFuentes();
        var (auditoria, almacen) = entorno.AuditoriaReal();
        var caso = new EditarFuente(entorno.Autorizador, new ValidadorDeEditarFuente(), entorno.Fuentes, entorno.Totales, auditoria);

        await caso.Ejecutar(new EditarFuenteEntrada("cusco", "Cusco Centro", "cusco", "+51 944 555 666"));

        var registro = Assert.Single(almacen.Registros);
        Assert.Equal("FuenteEditada", registro.Accion);
        Assert.Equal("nombre,contactoDeNotificacion", registro.Detalle.Valores["camposCambiados"]);
        Assert.DoesNotContain("944", registro.Detalle.Texto, StringComparison.Ordinal);
    }
}
