using BabelHub.Inventario.Domain.Auditoria;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>
/// <c>PoliticaDeAnonimizacion</c> (AUD-11, AUD-19): dos años, cuenta cancelada aún no anonimizada, <c>objeto_tipo</c> y roles que identifican según el
/// catálogo, constantes o <c>NULL</c> (nunca un hash estable) y residuo sin plazo; 2 años y un día frente a 2 años menos un día.
/// </summary>
public sealed class PoliticaDeAnonimizacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 3, 0, 0, TimeSpan.Zero);

    private static readonly AccionDelCatalogo IngresoFallido = new(
        "IngresoFallido",
        ClaseDeAccion.NoConcede,
        TipoDeRegistro.Seguridad,
        true,
        [new("nombre", true), new("cuenta", true), new("intentos", false)],
        objetoPersonal: true,
        rolPersonal: false,
        ["contrasena-incorrecta"],
        true);

    private static readonly AccionDelCatalogo AjusteDeUsuario = new(
        "AjusteDeUsuario",
        ClaseDeAccion.ConcedeOCambia,
        TipoDeRegistro.Auditoria,
        false,
        [new("nombre", true), new("nota", false)],
        objetoPersonal: true,
        rolPersonal: true,
        [],
        false);

    private static readonly AccionDelCatalogo AjusteDeStock = new(
        "AjusteDeStock",
        ClaseDeAccion.ConcedeOCambia,
        TipoDeRegistro.Auditoria,
        false,
        [new("cantidad", false), new("referencia", true)],
        objetoPersonal: false,
        rolPersonal: false,
        [],
        false);

    private static RegistroDeAuditoria Registro(
        AccionDelCatalogo accion,
        Actor actor,
        DateTimeOffset instante,
        string? objetoId = "obj-1",
        string? origen = null,
        params (string Clave, string Valor)[] detalle)
    {
        var valores = detalle.ToDictionary(d => d.Clave, d => (string?)d.Valor);
        var motivo = accion.ExigeMotivo ? accion.Motivos[0] : null;
        return RegistroDeAuditoria.Crear(
            Guid.NewGuid(),
            ServicioDeOrigen.Ecommerce,
            accion,
            actor,
            instante,
            "usuario",
            objetoId,
            "suc-lima",
            null,
            motivo,
            accion.Tipo == TipoDeRegistro.Auditoria ? origen : origen ?? "200.37.12.4",
            null,
            DetalleSeguro.Construir(accion.Detalle, valores),
            ResultadoDeOperacion.Rechazado,
            null);
    }

    private static IReadOnlyCollection<string> Cuentas(params string[] ids) => ids;

    private static readonly Guid Ana = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Luis = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Admin = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // ---------- Dos años ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public void UnRegistroDe2AnosYUnDia_SeAnonimizaCompleto_SinNecesitarCoincidenciaConUnaCuentaCancelada()
    {
        var registro = Registro(IngresoFallido, Actor.Usuario(Ana, "cliente"), Ahora.AddYears(-2).AddDays(-1), objetoId: "obj-ana", origen: "200.37.12.4", detalle: [("nombre", "Ana Pérez"), ("intentos", "5")]);

        var anonimizado = PoliticaDeAnonimizacion.Depurar(registro, Ahora, Cuentas());

        Assert.NotNull(anonimizado);
        Assert.Null(anonimizado.Actor.Id);
        Assert.Equal(TipoDeActor.Usuario, anonimizado.Actor.Tipo);
        Assert.Equal("cliente", anonimizado.Actor.Rol);
        Assert.Null(anonimizado.Origen);
        Assert.Null(anonimizado.ObjetoId);
        Assert.Equal(PoliticaDeAnonimizacion.Constante, anonimizado.Detalle.Valores["nombre"]);
        Assert.Equal("5", anonimizado.Detalle.Valores["intentos"]);
        Assert.Equal(Ahora, anonimizado.AnonimizadoEn);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public void LaAnonimizacionConservaAccionFechaTipoYResultado_YNingunValorPersonal()
    {
        var registro = Registro(IngresoFallido, Actor.Usuario(Ana, "cliente"), Ahora.AddYears(-2).AddDays(-1), objetoId: Ana.ToString(), origen: "200.37.12.4", detalle: [("nombre", "Ana Pérez"), ("cuenta", Ana.ToString())]);

        var anonimizado = PoliticaDeAnonimizacion.Depurar(registro, Ahora, Cuentas())!;

        Assert.Equal(registro.Id, anonimizado.Id);
        Assert.Equal("IngresoFallido", anonimizado.Accion);
        Assert.Equal(registro.InstanteUtc, anonimizado.InstanteUtc);
        Assert.Equal(registro.Tipo, anonimizado.Tipo);
        Assert.Equal(ResultadoDeOperacion.Rechazado, anonimizado.Resultado);
        Assert.Equal(registro.Servicio, anonimizado.Servicio);
        Assert.Equal(registro.ObjetoTipo, anonimizado.ObjetoTipo);
        Assert.Equal(registro.SucursalOrigen, anonimizado.SucursalOrigen);
        var todo = anonimizado.Detalle.Texto + anonimizado.Origen + anonimizado.ObjetoId + anonimizado.Actor.Id;
        Assert.DoesNotContain("Ana", todo, StringComparison.Ordinal);
        Assert.DoesNotContain(Ana.ToString(), todo, StringComparison.Ordinal);
        Assert.DoesNotContain("200.37.12.4", todo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public void UnRegistroDe2AnosMenosUnDia_NoCambia()
    {
        var registro = Registro(IngresoFallido, Actor.Usuario(Ana, "cliente"), Ahora.AddYears(-2).AddDays(1), origen: "200.37.12.4", detalle: [("nombre", "Ana Pérez")]);

        Assert.Null(PoliticaDeAnonimizacion.Depurar(registro, Ahora, Cuentas()));
        Assert.False(PoliticaDeAnonimizacion.Evaluar(registro, Ahora, Cuentas()).HayCambios);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, true)]
    [InlineData(1, false)]
    public void ElPlazoSeCuentaEnAniosDeCalendario_ElDiaExactoAunNoVence(int segundos, bool vencido)
    {
        var instante = Ahora.AddYears(-2).AddSeconds(segundos);

        Assert.Equal(vencido, PoliticaDeAnonimizacion.Vencido(instante, Ahora));
    }

    [Fact]
    public void ElPlazoSigueElCalendario_UnAnioBisiestoNoLoAlarga()
    {
        var ahora = new DateTimeOffset(2028, 3, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.True(PoliticaDeAnonimizacion.Vencido(new DateTimeOffset(2026, 2, 28, 23, 59, 59, TimeSpan.Zero), ahora));
        Assert.False(PoliticaDeAnonimizacion.Vencido(new DateTimeOffset(2026, 3, 1, 0, 0, 1, TimeSpan.Zero), ahora));
    }

    // ---------- Roles y objetos que identifican (según el catálogo) ----------

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [Trait("Criterio", "AC-AUD-09")]
    public void ElRolSoloSeAnonimizaPorPlazoCuandoElCatalogoDiceQueIdentifica(bool rolPersonal, bool anonimizado)
    {
        var accion = rolPersonal ? AjusteDeUsuario : AjusteDeStock;
        var registro = Registro(accion, Actor.Usuario(Admin, "encargado-de-sucursal"), Ahora.AddYears(-3), objetoId: "ed-1", detalle: [(rolPersonal ? "nombre" : "referencia", "valor")]);

        var resultado = PoliticaDeAnonimizacion.Depurar(registro, Ahora, Cuentas())!;

        Assert.Null(resultado.Actor.Id);
        Assert.Equal(anonimizado, resultado.Actor.Rol is null);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Criterio", "AC-AUD-09")]
    public void ElObjetoSoloSeAnonimizaCuandoElCatalogoDiceQueIdentificaAUnaPersona(bool objetoPersonal)
    {
        var accion = objetoPersonal ? AjusteDeUsuario : AjusteDeStock;
        var registro = Registro(accion, Actor.Usuario(Admin, "administrador-central"), Ahora.AddYears(-3), objetoId: "obj-9", detalle: []);

        var resultado = PoliticaDeAnonimizacion.Depurar(registro, Ahora, Cuentas())!;

        Assert.Equal(objetoPersonal ? null : "obj-9", resultado.ObjetoId);
    }

    [Fact]
    public void LosActoresDelSistemaDeUnaIntegracionYAnonimos_NoTienenNadaPersonal()
    {
        foreach (var actor in new[] { Actor.Sistema(), Actor.Integracion(), Actor.Anonimo() })
        {
            var registro = Registro(AjusteDeStock, actor, Ahora.AddYears(-3), objetoId: "ed-1", detalle: [("cantidad", "3")]);

            var resultado = PoliticaDeAnonimizacion.Depurar(registro, Ahora, Cuentas());

            Assert.Null(resultado);
        }
    }

    // ---------- Cuenta cancelada aún no anonimizada ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public void UnRegistroDeUnaCuentaCanceladaAyer_SeAnonimiza_AunqueSeaReciente()
    {
        var registro = Registro(IngresoFallido, Actor.Usuario(Ana, "cliente"), Ahora.AddDays(-30), origen: "200.37.12.4", detalle: [("nombre", "Ana Pérez")]);

        var anonimizado = PoliticaDeAnonimizacion.Depurar(registro, Ahora, Cuentas(Ana.ToString()))!;

        Assert.Null(anonimizado.Actor.Id);
        Assert.Null(anonimizado.Actor.Rol);
        Assert.Null(anonimizado.Origen);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public void LaCoincidenciaDeLaCuentaNoDistingueMayusculasEnElIdentificador()
    {
        var registro = Registro(AjusteDeUsuario, Actor.Usuario(Ana, "cliente"), Ahora.AddDays(-1), detalle: []);

        var resultado = PoliticaDeAnonimizacion.Depurar(registro, Ahora, Cuentas(Ana.ToString().ToUpperInvariant()));

        Assert.NotNull(resultado);
        Assert.Null(resultado.Actor.Id);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public void ElRegistroDeUnAdministradorSobreUnaCuentaCancelada_ConservaAlAdministradorComoActor()
    {
        var registro = Registro(AjusteDeUsuario, Actor.Usuario(Admin, "administrador-central"), Ahora.AddDays(-10), objetoId: Ana.ToString(), detalle: [("nombre", "Ana Pérez"), ("nota", "corrección")]);

        var resultado = PoliticaDeAnonimizacion.Depurar(registro, Ahora, Cuentas(Ana.ToString()))!;

        Assert.Equal(Admin, resultado.Actor.Id);
        Assert.Equal("administrador-central", resultado.Actor.Rol);
        Assert.Null(resultado.ObjetoId);
        // El nombre del detalle es personal pero su valor no es el identificador de la cuenta: se conserva (hasta que venza el plazo).
        Assert.Equal("Ana Pérez", resultado.Detalle.Valores["nombre"]);
        Assert.Equal("corrección", resultado.Detalle.Valores["nota"]);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public void ElObjetoQueCoincideConUnaCuentaCancelada_NoSeAnonimizaSiElCatalogoNoLoMarcaPersonal()
    {
        var registro = Registro(AjusteDeStock, Actor.Usuario(Admin, "administrador-central"), Ahora.AddDays(-10), objetoId: Ana.ToString(), detalle: [("cantidad", "3")]);

        Assert.Null(PoliticaDeAnonimizacion.Depurar(registro, Ahora, Cuentas(Ana.ToString())));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public void SoloLasClavesPersonalesDelDetalleCuyoValorCoincideSeAnonimizan()
    {
        var registro = Registro(IngresoFallido, Actor.Usuario(Luis, "cliente"), Ahora.AddDays(-10), objetoId: "otro", origen: "huella-9", detalle: [("nombre", "Luis"), ("cuenta", Ana.ToString()), ("intentos", Ana.ToString())]);

        var resultado = PoliticaDeAnonimizacion.Depurar(registro, Ahora, Cuentas(Ana.ToString()))!;

        Assert.Equal(PoliticaDeAnonimizacion.Constante, resultado.Detalle.Valores["cuenta"]);
        Assert.Equal("Luis", resultado.Detalle.Valores["nombre"]);
        Assert.Equal(Ana.ToString(), resultado.Detalle.Valores["intentos"]);
        Assert.Equal(Luis, resultado.Actor.Id);
        Assert.Equal("huella-9", resultado.Origen);
    }

    [Fact]
    public void UnaCuentaQueNoEsDeNadieDelRegistro_NoLoCambia()
    {
        var registro = Registro(IngresoFallido, Actor.Usuario(Luis, "cliente"), Ahora.AddDays(-5), origen: "huella-9", detalle: [("nombre", "Luis")]);

        Assert.Null(PoliticaDeAnonimizacion.Depurar(registro, Ahora, Cuentas(Ana.ToString(), Guid.NewGuid().ToString())));
    }

    // ---------- Constantes o NULL ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public void LaAnonimizacionUsaUnaConstanteONulo_NuncaUnHashEstable()
    {
        var uno = Registro(IngresoFallido, Actor.Usuario(Ana, "cliente"), Ahora.AddYears(-3), objetoId: "x", origen: "200.37.12.4", detalle: [("nombre", "Ana Pérez")]);
        var otro = Registro(IngresoFallido, Actor.Usuario(Luis, "cliente"), Ahora.AddYears(-3), objetoId: "y", origen: "200.37.99.9", detalle: [("nombre", "Luis Gómez")]);

        var a = PoliticaDeAnonimizacion.Depurar(uno, Ahora, Cuentas())!;
        var b = PoliticaDeAnonimizacion.Depurar(otro, Ahora, Cuentas())!;

        // Dos personas distintas quedan igual de anónimas: nada permite distinguirlas ni enlazarlas.
        Assert.Equal(a.Detalle.Valores["nombre"], b.Detalle.Valores["nombre"]);
        Assert.Equal(a.Detalle.Texto, b.Detalle.Texto);
        Assert.Equal(PoliticaDeAnonimizacion.Constante, a.Detalle.Valores["nombre"]);
        Assert.Null(a.Actor.Id);
        Assert.Null(b.Actor.Id);
        Assert.Null(a.Origen);
        Assert.Null(a.ObjetoId);
    }

    [Fact]
    public void ElDetalleAnonimizadoSigueMarcandoComoPersonalesSusClaves_YNuncaSuperaElMaximo()
    {
        var largo = Registro(IngresoFallido, Actor.Usuario(Ana, "cliente"), Ahora.AddYears(-3), detalle: [("nombre", "A"), ("cuenta", "B"), ("intentos", new string('x', 900))]);

        var resultado = PoliticaDeAnonimizacion.Depurar(largo, Ahora, Cuentas())!;

        Assert.Equal(["cuenta", "nombre"], resultado.Detalle.ClavesPersonales.Order(StringComparer.Ordinal));
        Assert.True(resultado.Detalle.Texto.Length <= DetalleSeguro.MaximoDeCaracteres);
    }

    [Fact]
    public void SiLaConstanteHicieraSuperarElMaximo_LosValoresAnonimizadosQuedanVacios_YElDetalleNoDesborda()
    {
        // Un detalle de 995 caracteres con valores de un solo carácter: la constante de 13 caracteres no cabría.
        var vacio = Registro(IngresoFallido, Actor.Usuario(Ana, "cliente"), Ahora.AddYears(-3), detalle: [("nombre", "A"), ("cuenta", "B"), ("intentos", string.Empty)]);
        var largo = 995 - vacio.Detalle.Texto.Length;
        var registro = Registro(IngresoFallido, Actor.Usuario(Ana, "cliente"), Ahora.AddYears(-3), detalle: [("nombre", "A"), ("cuenta", "B"), ("intentos", new string('x', largo))]);
        Assert.Equal(995, registro.Detalle.Texto.Length);

        var resultado = PoliticaDeAnonimizacion.Depurar(registro, Ahora, Cuentas())!;

        Assert.True(resultado.Detalle.Texto.Length <= DetalleSeguro.MaximoDeCaracteres);
        Assert.Equal(string.Empty, resultado.Detalle.Valores["nombre"]);
        Assert.Equal(string.Empty, resultado.Detalle.Valores["cuenta"]);
        Assert.Equal(["cuenta", "nombre"], resultado.Detalle.ClavesPersonales.Order(StringComparer.Ordinal));
    }

    // ---------- Idempotencia y residuo ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public void AlRepetirLaDepuracion_NoCambiaNada()
    {
        var registro = Registro(IngresoFallido, Actor.Usuario(Ana, "cliente"), Ahora.AddYears(-3), origen: "200.37.12.4", detalle: [("nombre", "Ana Pérez")]);
        var primera = PoliticaDeAnonimizacion.Depurar(registro, Ahora, Cuentas())!;

        var segunda = PoliticaDeAnonimizacion.Depurar(primera, Ahora.AddDays(1), Cuentas(Ana.ToString()));

        Assert.Null(segunda);
        Assert.False(PoliticaDeAnonimizacion.Evaluar(primera, Ahora.AddDays(1), Cuentas()).HayCambios);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public void ElResiduoAnonimizadoNoTienePlazo_NuncaSePurga_PorViejoQueSea()
    {
        var registro = Registro(IngresoFallido, Actor.Usuario(Ana, "cliente"), Ahora.AddYears(-3), origen: "200.37.12.4", detalle: [("nombre", "Ana Pérez")]);
        var residuo = PoliticaDeAnonimizacion.Depurar(registro, Ahora, Cuentas())!;

        Assert.Null(PoliticaDeAnonimizacion.PlazoDelResiduo);
        Assert.False(PoliticaDeAnonimizacion.Evaluar(residuo, Ahora.AddYears(50), Cuentas()).HayCambios);
        Assert.Equal("IngresoFallido", residuo.Accion);
    }

    [Fact]
    public void UnaCuentaCanceladaDespuesDeUnaAnonimizacionParcial_AnonimizaLoQueQuedaPersonal_SinCambiarLaFechaOriginal()
    {
        var registro = Registro(AjusteDeUsuario, Actor.Usuario(Admin, "administrador-central"), Ahora.AddDays(-10), objetoId: Ana.ToString(), detalle: [("nombre", Luis.ToString())]);
        var primera = PoliticaDeAnonimizacion.Depurar(registro, Ahora, Cuentas(Ana.ToString()))!;

        var segunda = PoliticaDeAnonimizacion.Depurar(primera, Ahora.AddDays(2), Cuentas(Luis.ToString()))!;

        Assert.Null(segunda.ObjetoId);
        Assert.Equal(PoliticaDeAnonimizacion.Constante, segunda.Detalle.Valores["nombre"]);
        Assert.Equal(Ahora, segunda.AnonimizadoEn);
    }

    [Fact]
    public void LaDepuracionNoModificaElRegistroOriginal()
    {
        var registro = Registro(IngresoFallido, Actor.Usuario(Ana, "cliente"), Ahora.AddYears(-3), origen: "200.37.12.4", detalle: [("nombre", "Ana Pérez")]);

        _ = PoliticaDeAnonimizacion.Depurar(registro, Ahora, Cuentas());

        Assert.Equal(Ana, registro.Actor.Id);
        Assert.Equal("200.37.12.4", registro.Origen);
        Assert.Equal("Ana Pérez", registro.Detalle.Valores["nombre"]);
        Assert.Null(registro.AnonimizadoEn);
    }

    [Fact]
    public void ElConjuntoDeCuentasCanceladasNoPuedeSerNulo() =>
        Assert.Throws<ArgumentNullException>(() => PoliticaDeAnonimizacion.Evaluar(Registro(AjusteDeStock, Actor.Sistema(), Ahora, detalle: []), Ahora, null!));

    [Fact]
    public void ElPlanDescribeExactamenteQueSeAnonimiza()
    {
        var registro = Registro(IngresoFallido, Actor.Usuario(Ana, "cliente"), Ahora.AddDays(-5), origen: "huella", detalle: [("nombre", "Ana"), ("cuenta", Ana.ToString())]);

        var plan = PoliticaDeAnonimizacion.Evaluar(registro, Ahora, Cuentas(Ana.ToString()));

        Assert.True(plan.ActorId);
        Assert.True(plan.ActorRol);
        Assert.True(plan.Origen);
        Assert.False(plan.Objeto);
        Assert.Equal(["cuenta"], plan.ClavesDelDetalle);
        Assert.True(plan.HayCambios);
    }
}
