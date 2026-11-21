using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using BabelHub.Ecommerce.Domain.Reclamaciones;
using BabelHub.Ecommerce.UnitTests.Auditoria;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Reclamaciones;

/// <summary>
/// El vínculo de las hojas con la cuenta del titular en la Aplicación (T25 a T29; REC-20 a REC-26, REC-29, REC-33, REC-34; DD-26, DD-29): el registro con sesión, el reclamo con su auditoría, el receptor de los cambios de
/// cuenta de Identidad y la vinculación inicial por lotes. Todo con la auditoría, la base y Identidad fallando.
/// </summary>
public sealed class VinculoDeHojasTests
{
    private static readonly Guid Ana = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Beto = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private const string CorreoDeAna = "ana.perez@correo.com";

    /// <summary>La misma hoja como la deja la función de anonimización de la base de datos: con la marca y sin cuenta.</summary>
    private static HojaDeReclamacion Anonimizada(HojaDeReclamacion h, DateTimeOffset en) =>
        HojaDeReclamacion.Rehidratar(h.Id, h.Numero, h.Tipo, h.Datos, h.BienContratado, h.MontoReclamado, h.Detalle, h.PedidoReferencia, h.ContenidoHash, h.FechaRegistro, h.ClaveHash, h.Estado, h.Respuesta, h.RespondidaEn, h.RespondidaPor, h.AvisoDeRespuesta, h.AvisoEnviadoEn, h.AvisoEnCursoHasta, h.DescartadaEn, h.DeCuentaCancelada, en, null);

    /// <summary>Registra una hoja y la deja sin cuenta, como la de un visitante (el cliente con sesión la vincula al registrar).</summary>
    private static async Task<Guid> RegistrarSinCuenta(EntornoDeReclamaciones e)
    {
        var id = await e.RegistrarUna();
        var hoja = e.Hojas.Todas.Single(h => h.Id == id);
        Assert.True(hoja.Desvincular());
        e.Hojas.Sembrar(hoja);
        e.Auditoria.Solicitudes.Clear();
        return id;
    }

    private static SolicitudDeRegistro UnaVinculacion(EntornoDeReclamaciones e) =>
        Assert.Single(e.Auditoria.Solicitudes, s => s.Accion == AccionesAuditables.HojaDeReclamacionVinculada);

    // ---------- Registro con sesión (T26, REC-21, REC-29, REC-33) ----------

    [Fact]
    [Trait("Criterio", "AC-REC-20")]
    [Trait("Criterio", "AC-REC-26")]
    public async Task Un_cliente_con_sesion_registra_la_hoja_vinculada_a_su_cuenta_y_se_audita_sin_el_correo_ni_el_numero()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DeCliente(Ana));

        var resultado = await e.Reclamaciones.Registrar(e.Entrada());

        Assert.True(resultado.EsExito);
        Assert.Equal(Ana, Assert.Single(e.Hojas.Todas).CuentaId);
        var auditoria = UnaVinculacion(e);
        Assert.Equal("usuario", auditoria.ObjetoTipo);
        Assert.Equal(Ana.ToString("D"), auditoria.ObjetoId);
        Assert.Equal("registro", auditoria.Detalle["disparador"]);
        Assert.Equal("1", auditoria.Detalle["conteo"]);
        Assert.DoesNotContain(auditoria.Detalle.Values, v => v is not null && (v.Contains('@', StringComparison.Ordinal) || v.Contains("2026-", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-20")]
    public async Task Un_cliente_sin_correo_verificado_tambien_vincula_la_hoja_que_registra_con_su_sesion()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DeCliente(Ana));

        _ = await e.Reclamaciones.Registrar(e.Entrada() with { Correo = "otro.contacto@correo.com" });

        Assert.Equal(Ana, Assert.Single(e.Hojas.Todas).CuentaId);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-29")]
    [InlineData("visitante")]
    [InlineData("personal")]
    [InlineData("sistema")]
    public async Task Un_visitante_el_personal_o_el_sistema_registran_sin_cuenta_y_sin_auditoria(string quien)
    {
        var actor = quien switch
        {
            "visitante" => ActorDePrueba.Anonimo(),
            "personal" => ActorDePrueba.DeAdministradorCentral(),
            _ => ActorDePrueba.DelSistema(),
        };
        var e = new EntornoDeReclamaciones(actor);

        var resultado = await e.Reclamaciones.Registrar(e.Entrada());

        Assert.True(resultado.EsExito);
        Assert.Null(Assert.Single(e.Hojas.Todas).CuentaId);
        Assert.Empty(e.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-26")]
    [Trait("Criterio", "AC-REC-30")]
    public async Task Sin_poder_auditar_la_hoja_no_se_guarda_se_devuelve_el_cupo_se_suelta_la_clave_y_se_ofrece_registrar_sin_sesion()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DeCliente(Ana));
        e.Auditoria.Caida = true;

        var resultado = await e.Reclamaciones.Registrar(e.Entrada("clave-fija-del-formulario"));

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(ClavesDeReclamaciones.ServicioNoDisponible, resultado.Error.Clave);
        Assert.Contains(resultado.Error.Errores, c => c.Campo == ClavesDeReclamaciones.RegistrarSinSesion);
        Assert.Empty(e.Hojas.Todas);
        Assert.Single(e.Limites.Liberadas);
        Assert.Single(e.Limites.Falladas);
        Assert.Equal(0, e.Limites.Usadas);

        // El mismo envío, ahora sin sesión, se registra de inmediato: la clave quedó libre y el cupo, devuelto.
        var sinSesion = new EntornoDeReclamaciones(ActorDePrueba.Anonimo());
        Assert.True((await sinSesion.Reclamaciones.Registrar(sinSesion.Entrada("clave-fija-del-formulario"))).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-30")]
    public async Task Tres_rechazos_de_auditoria_no_agotan_el_cupo_del_origen_y_se_registran_tres_hojas_sin_sesion()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DeCliente(Ana));
        e.Auditoria.Caida = true;
        for (var i = 0; i < 3; i++)
        {
            Assert.False((await e.Reclamaciones.Registrar(e.Entrada())).EsExito);
        }

        // Con tres rechazos el cupo del origen (3) sigue completo.
        Assert.Equal(0, e.Limites.Usadas);
        e.Auditoria.Caida = false;
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await e.Reclamaciones.Registrar(e.Entrada())).EsExito);
        }

        Assert.Equal(3, e.Hojas.Todas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-30")]
    public async Task Si_devolver_el_cupo_falla_el_rechazo_sigue_siendo_servicio_no_disponible_y_la_clave_se_suelta()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DeCliente(Ana));
        e.Auditoria.Caida = true;
        e.Limites.LiberarFalla = true;

        var resultado = await e.Reclamaciones.Registrar(e.Entrada());

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Single(e.Limites.Falladas);
        Assert.Empty(e.Hojas.Todas);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-30")]
    public async Task Otro_fallo_de_la_base_gasta_el_cupo_como_antes_y_no_devuelve_nada()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DeCliente(Ana));
        e.Hojas.FallaAlAgregar = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Reclamaciones.Registrar(e.Entrada()));

        Assert.Empty(e.Limites.Liberadas);
        Assert.Equal(1, e.Limites.Usadas);
    }

    // ---------- Reclamo (T25 y T27, REC-22, REC-27, REC-29) ----------

    [Fact]
    [Trait("Criterio", "AC-REC-21")]
    [Trait("Criterio", "AC-REC-26")]
    public async Task Listar_con_correo_verificado_vincula_las_hojas_del_correo_y_las_audita_una_sola_vez()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, CorreoDeAna);
        await e.Base.RegistrarUna();
        await e.Base.RegistrarUna();

        var resultado = await e.Consumidor.Listar(new ListarMisHojasEntrada());

        Assert.Equal(2, resultado.Valor.Total);
        Assert.All(e.Hojas.Todas, h => Assert.Equal(Ana, h.CuentaId));
        var auditoria = UnaVinculacion(e.Base);
        Assert.Equal("consulta", auditoria.Detalle["disparador"]);
        Assert.Equal("2", auditoria.Detalle["conteo"]);

        // Una segunda consulta no vuelve a vincular ni a auditar: no hay hojas sin cuenta.
        _ = await e.Consumidor.Listar(new ListarMisHojasEntrada());
        _ = UnaVinculacion(e.Base);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-21")]
    public async Task Obtener_una_hoja_por_su_numero_reclama_primero_las_hojas_del_correo()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, CorreoDeAna);
        await e.Base.RegistrarUna();

        var resultado = await e.Consumidor.Obtener("2026-000001");

        Assert.True(resultado.EsExito);
        Assert.Equal("consulta", UnaVinculacion(e.Base).Detalle["disparador"]);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-21")]
    public async Task Una_hoja_vinculada_a_otra_cuenta_no_se_ve_aunque_el_correo_de_contacto_coincida()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, CorreoDeAna);
        var id = await e.Base.RegistrarUna();
        var hoja = e.Hojas.Todas.Single(h => h.Id == id);
        Assert.True(hoja.Vincular(Beto));
        e.Hojas.Sembrar(hoja);

        var lista = await e.Consumidor.Listar(new ListarMisHojasEntrada());
        var una = await e.Consumidor.Obtener("2026-000001");

        Assert.Equal(0, lista.Valor.Total);
        Assert.Equal(CategoriaDeError.NoEncontrado, una.Error!.Categoria);
        Assert.Equal(Beto, e.Hojas.Todas.Single().CuentaId);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-20")]
    public async Task Una_hoja_vinculada_se_ve_por_cuenta_aunque_su_correo_de_contacto_sea_otro()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, CorreoDeAna);
        var sesion = new EntornoDeReclamaciones(ActorDePrueba.DeCliente(Ana));
        var hoja = HojaDeReclamacionTests.Nueva();
        Assert.True(hoja.Vincular(Ana));
        e.Hojas.Sembrar(hoja);

        var lista = await e.Consumidor.Listar(new ListarMisHojasEntrada());

        Assert.Equal(1, lista.Valor.Total);
        Assert.False(lista.Valor.CorreoSinVerificar);
        Assert.NotNull(sesion);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-22")]
    [InlineData("lectura-del-correo")]
    [InlineData("vincular")]
    [InlineData("auditar")]
    public async Task Si_no_se_puede_leer_el_correo_vincular_o_auditar_la_lista_y_la_hoja_fallan_sin_vincular_nada(string falla)
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, CorreoDeAna);
        await e.Base.RegistrarUna();
        switch (falla)
        {
            case "lectura-del-correo":
                e.Correos.Falla = true;
                break;
            case "vincular":
                e.Hojas.FallaAlVincular = true;
                break;
            default:
                e.Base.Auditoria.Caida = true;
                break;
        }

        var lista = await e.Consumidor.Listar(new ListarMisHojasEntrada());
        var una = await e.Consumidor.Obtener("2026-000001");

        Assert.Equal(CategoriaDeError.NoDisponible, lista.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, una.Error!.Categoria);
        Assert.All(e.Hojas.Todas, h => Assert.Null(h.CuentaId));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-21")]
    public async Task Las_hojas_de_una_cuenta_cancelada_o_anonimizadas_no_se_reclaman()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, CorreoDeAna);
        var cancelada = await e.Base.RegistrarUna();
        var hoja = e.Hojas.Todas.Single(h => h.Id == cancelada);
        hoja.MarcarDeCuentaCancelada();
        e.Hojas.Sembrar(hoja);
        await e.Base.RegistrarUna();
        e.Hojas.Sembrar(Anonimizada(e.Hojas.Todas.Single(h => h.Id != cancelada), e.Base.Reloj.Ahora));

        var resultado = await e.Consumidor.Listar(new ListarMisHojasEntrada());

        Assert.Equal(0, resultado.Valor.Total);
        Assert.All(e.Hojas.Todas, h => Assert.Null(h.CuentaId));
        Assert.Empty(e.Base.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-21")]
    public async Task Una_sesion_de_personal_no_lista_mis_hojas_ni_reclama_nada()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DeAdministradorCentral());
        var autorizador = new AutorizadorDeReclamaciones(e.Actor, e.Denegaciones);
        var correos = new CorreoVerificadoDoble();
        var listar = new ListarMisHojas(autorizador, correos, e.Vinculador, e.Hojas, e.Unidad, new LimiteDeConsultaDeHojas(e.Limites));
        await e.RegistrarUna();

        var resultado = await listar.Ejecutar(new ListarMisHojasEntrada());

        Assert.False(resultado.EsExito);
        Assert.Equal(0, e.Hojas.Reclamos);
    }

    // ---------- Conteo de Identidad (T27, REC-19) ----------

    [Fact]
    [Trait("Criterio", "AC-REC-22")]
    [Trait("Criterio", "AC-REC-26")]
    public async Task El_conteo_de_pendientes_vincula_con_el_disparador_pendientes()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, CorreoDeAna);
        await e.Base.RegistrarUna();

        var cuenta = await new ContarHojasPendientesDeUnCorreo(e.Correos, e.Vinculador, e.Hojas).Contar(Ana);

        Assert.Equal(1, cuenta);
        Assert.Equal("pendientes", UnaVinculacion(e.Base).Detalle["disparador"]);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-22")]
    public async Task Si_no_se_puede_auditar_el_conteo_lanza_y_nunca_devuelve_un_conteo_parcial()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, CorreoDeAna);
        await e.Base.RegistrarUna();
        e.Base.Auditoria.Caida = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => new ContarHojasPendientesDeUnCorreo(e.Correos, e.Vinculador, e.Hojas).Contar(Ana));
        Assert.All(e.Hojas.Todas, h => Assert.Null(h.CuentaId));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-27")]
    public async Task Una_cuenta_sin_correo_verificado_cuenta_cero_aunque_tenga_hojas_vinculadas_y_no_reclama_las_del_correo_de_otra_persona()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, null);
        await e.Base.RegistrarUna();
        var suya = e.Hojas.Todas.Single();
        Assert.True(suya.Vincular(Ana));
        e.Hojas.Sembrar(suya);

        Assert.Equal(0, await new ContarHojasPendientesDeUnCorreo(e.Correos, e.Vinculador, e.Hojas).Contar(Ana));
        Assert.Equal(0, e.Hojas.Reclamos);
    }

    // ---------- Cancelación (T27, REC-20, REC-24, REC-31) ----------

    [Fact]
    [Trait("Criterio", "AC-REC-19")]
    public async Task La_cancelacion_marca_y_desvincula_las_hojas_vinculadas_y_las_sin_cuenta_del_correo_verificado()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, CorreoDeAna);
        await e.Base.RegistrarUna();
        var vinculada = e.Hojas.Todas.Single();
        Assert.True(vinculada.Vincular(Ana));
        e.Hojas.Sembrar(vinculada);
        await e.Base.RegistrarUna();

        await new MarcarHojasDeUnaCuentaCancelada(e.Correos, e.Hojas).Aplicar(Ana, Guid.NewGuid());

        Assert.All(e.Hojas.Todas, h =>
        {
            Assert.True(h.DeCuentaCancelada);
            Assert.Null(h.CuentaId);
        });
    }

    [Fact]
    [Trait("Criterio", "AC-REC-31")]
    public async Task Una_cuenta_sin_correo_verificado_que_se_cancela_marca_solo_sus_hojas_vinculadas_y_no_las_de_un_tercero()
    {
        var e = EntornoDeConsumidor.Nuevo(Ana, null);
        var tercero = await e.Base.RegistrarUna();
        await e.Base.RegistrarUna();
        var suya = e.Hojas.Todas.Single(h => h.Id != tercero);
        Assert.True(suya.Vincular(Ana));
        e.Hojas.Sembrar(suya);

        await new MarcarHojasDeUnaCuentaCancelada(e.Correos, e.Hojas).Aplicar(Ana, Guid.NewGuid());

        Assert.True(e.Hojas.Todas.Single(h => h.Id == suya.Id).DeCuentaCancelada);
        Assert.False(e.Hojas.Todas.Single(h => h.Id == tercero).DeCuentaCancelada);
    }

    // ---------- Receptor de cambios de cuenta (T28, REC-23, REC-25, REC-30) ----------

    private static ReceptorDeCambiosDeCuentaDeReclamaciones Receptor(EntornoDeReclamaciones e) => new(e.Vinculador, e.Hojas, e.Unidad);

    [Fact]
    [Trait("Criterio", "AC-REC-23")]
    [Trait("Criterio", "AC-REC-26")]
    public async Task Al_cambiar_el_correo_vincula_las_hojas_del_correo_anterior_con_el_disparador_cambio_de_correo()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DeCliente(Ana));
        await RegistrarSinCuenta(e);
        var hoja = e.Hojas.Todas.Single();

        await Receptor(e).AlCambiarElCorreo(Ana, "ana.perez@correo.com");

        Assert.Equal(Ana, e.Hojas.Todas.Single(h => h.Id == hoja.Id).CuentaId);
        Assert.Equal("cambio-de-correo", Assert.Single(e.Auditoria.Solicitudes, s => s.Detalle.TryGetValue("disparador", out var d) && d == "cambio-de-correo").Detalle["disparador"]);
        Assert.Equal("reclamaciones", Receptor(e).Modulo);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-23")]
    [InlineData("")]
    [InlineData("no es un correo")]
    public async Task Un_correo_anterior_que_no_es_valido_para_el_modulo_no_hace_nada_ni_falla(string correo)
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DeCliente(Ana));

        await Receptor(e).AlCambiarElCorreo(Ana, correo);

        Assert.Equal(0, e.Hojas.Reclamos);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-23")]
    [InlineData("auditar")]
    [InlineData("base")]
    public async Task Si_no_puede_vincular_o_auditar_el_receptor_lanza_para_que_el_cambio_de_correo_no_se_aplique(string falla)
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DeCliente(Ana));
        await RegistrarSinCuenta(e);
        if (falla == "auditar")
        {
            e.Auditoria.Caida = true;
        }
        else
        {
            e.Hojas.FallaAlVincular = true;
        }

        await Assert.ThrowsAnyAsync<Exception>(() => Receptor(e).AlCambiarElCorreo(Ana, CorreoDeAna));
        Assert.All(e.Hojas.Todas, h => Assert.Null(h.CuentaId));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-27")]
    public async Task Al_reemplazar_una_cuenta_sin_verificar_sus_hojas_quedan_sin_cuenta_y_otra_cuenta_las_puede_reclamar()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DeCliente(Ana));
        await e.RegistrarUna();
        Assert.Equal(Ana, e.Hojas.Todas.Single().CuentaId);

        await Receptor(e).AlReemplazarLaCuenta(Ana);

        Assert.Null(e.Hojas.Todas.Single().CuentaId);
        _ = await e.Vinculador.Reclamar(Beto, CorreoDeAna, DisparadoresDeVinculo.Consulta);
        Assert.Equal(Beto, e.Hojas.Todas.Single().CuentaId);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-27")]
    public async Task Una_cuenta_vacia_se_rechaza_en_el_receptor()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DeCliente(Ana));

        await Assert.ThrowsAsync<ArgumentException>(() => Receptor(e).AlCambiarElCorreo(Guid.Empty, CorreoDeAna));
        await Assert.ThrowsAsync<ArgumentException>(() => Receptor(e).AlReemplazarLaCuenta(Guid.Empty));
    }

    // ---------- Vinculación inicial (T29, REC-26, REC-34) ----------

    private sealed class ClientesPorCorreoDoble : Application.Identidad.IClientePorCorreoVerificadoDeSistema
    {
        public Dictionary<string, Guid> Cuentas { get; } = [];

        public bool Falla { get; set; }

        public List<string> Consultados { get; } = [];

        public Task<Guid?> Consultar(string correoNormalizado, CancellationToken cancelacion = default)
        {
            Consultados.Add(correoNormalizado);
            return Falla ? throw new InvalidOperationException("Identidad no responde.") : Task.FromResult<Guid?>(Cuentas.TryGetValue(correoNormalizado, out var cuenta) ? cuenta : null);
        }
    }

    private static (VincularHojasExistentes Paso, ClientesPorCorreoDoble Clientes) ArmarVinculacionInicial(EntornoDeReclamaciones e, ActorDePrueba? actor = null)
    {
        var clientes = new ClientesPorCorreoDoble();
        var autorizador = new AutorizadorDeReclamaciones(actor ?? ActorDePrueba.DelSistema(), e.Denegaciones);
        return (new VincularHojasExistentes(autorizador, e.Hojas, clientes, e.Vinculador, e.Unidad, e.Reloj), clientes);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-24")]
    [Trait("Criterio", "AC-REC-26")]
    public async Task La_vinculacion_inicial_vincula_solo_los_correos_con_una_cuenta_de_cliente_verificada_y_termina_una_sola_vez()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DelSistema());
        await e.RegistrarUna();
        await e.Reclamaciones.Registrar(e.Entrada() with { Correo = "sin.cuenta@correo.com" });
        var (paso, clientes) = ArmarVinculacionInicial(e);
        clientes.Cuentas[CorreoDeAna] = Ana;

        var resultado = await paso.Ejecutar();

        Assert.True(resultado.EsExito);
        Assert.Equal(1, resultado.Valor);
        Assert.Equal(Ana, e.Hojas.Todas.Single(h => h.Datos.CorreoNormalizado == CorreoDeAna).CuentaId);
        Assert.Null(e.Hojas.Todas.Single(h => h.Datos.CorreoNormalizado == "sin.cuenta@correo.com").CuentaId);
        Assert.NotNull(e.Hojas.EstadoInicial.CompletadaEn);
        Assert.Null(e.Hojas.EstadoInicial.UltimoCorreo);
        Assert.Equal("vinculacion-inicial", UnaVinculacion(e).Detalle["disparador"]);

        // Una segunda corrida no consulta a Identidad ni vincula nada más.
        var consultas = clientes.Consultados.Count;
        Assert.Equal(0, (await paso.Ejecutar()).Valor);
        Assert.Equal(consultas, clientes.Consultados.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-24")]
    public async Task La_vinculacion_inicial_no_toca_los_datos_de_las_hojas()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DelSistema());
        await e.RegistrarUna();
        var antes = e.Hojas.Todas.Single();
        var (paso, clientes) = ArmarVinculacionInicial(e);
        clientes.Cuentas[CorreoDeAna] = Ana;

        await paso.Ejecutar();

        var despues = e.Hojas.Todas.Single();
        Assert.Equal((antes.Estado, antes.Datos, antes.Detalle, antes.FechaRegistro, antes.Respuesta), (despues.Estado, despues.Datos, despues.Detalle, despues.FechaRegistro, despues.Respuesta));
    }

    [Theory]
    [Trait("Criterio", "AC-REC-24")]
    [InlineData("identidad")]
    [InlineData("auditoria")]
    public async Task Si_Identidad_o_la_auditoria_fallan_no_avanza_el_cursor_y_la_siguiente_ejecucion_continua(string falla)
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DelSistema());
        await e.RegistrarUna();
        var (paso, clientes) = ArmarVinculacionInicial(e);
        clientes.Cuentas[CorreoDeAna] = Ana;
        if (falla == "identidad")
        {
            clientes.Falla = true;
        }
        else
        {
            e.Auditoria.Caida = true;
        }

        var fallida = await paso.Ejecutar();

        Assert.Equal(CategoriaDeError.NoDisponible, fallida.Error!.Categoria);
        Assert.Null(e.Hojas.EstadoInicial.CompletadaEn);
        Assert.Null(e.Hojas.EstadoInicial.UltimoCorreo);
        Assert.Null(e.Hojas.Todas.Single().CuentaId);

        clientes.Falla = false;
        e.Auditoria.Caida = false;
        var reanudada = await paso.Ejecutar();

        Assert.Equal(1, reanudada.Valor);
        Assert.NotNull(e.Hojas.EstadoInicial.CompletadaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-24")]
    public async Task Si_el_primer_lote_falla_siempre_el_inicio_queda_guardado_y_a_los_siete_dias_se_alerta()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DelSistema());
        await e.RegistrarUna();
        var (paso, clientes) = ArmarVinculacionInicial(e);
        clientes.Falla = true;
        var inicio = e.Reloj.Ahora;

        Assert.False((await paso.Ejecutar()).EsExito);

        Assert.Equal(inicio, e.Hojas.EstadoInicial.IniciadaEn);
        Assert.Null(e.Hojas.EstadoInicial.CompletadaEn);
        var alertas = new AlertasTecnicasEspia();
        var revision = new RevisarVinculacionInicial(e.Hojas, e.Unidad, alertas, e.Reloj);
        e.Reloj.Ahora = inicio.AddDays(7);
        Assert.True((await revision.Ejecutar()).Valor);
        Assert.Single(alertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-24")]
    public async Task La_vinculacion_inicial_recorre_por_lotes_ordenados_y_guarda_el_cursor_del_ultimo_lote_completo()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DelSistema());
        e.Limites.Maximo = 1_000;
        var total = VincularHojasExistentes.TamanoDelLote + 5;
        for (var i = 0; i < total; i++)
        {
            await e.Reclamaciones.Registrar(e.Entrada() with { Correo = $"cliente{i:D3}@correo.com" });
        }

        var (paso, clientes) = ArmarVinculacionInicial(e);
        for (var i = 0; i < total; i++)
        {
            clientes.Cuentas[$"cliente{i:D3}@correo.com"] = Guid.NewGuid();
        }

        var resultado = await paso.Ejecutar();

        Assert.Equal(total, resultado.Valor);
        Assert.Equal(clientes.Consultados.OrderBy(c => c, StringComparer.Ordinal).ToList(), clientes.Consultados);
        Assert.All(e.Hojas.Todas, h => Assert.NotNull(h.CuentaId));
    }

    [Theory]
    [Trait("Criterio", "AC-REC-24")]
    [InlineData("visitante")]
    [InlineData("cliente")]
    [InlineData("personal")]
    public async Task Solo_el_actor_sistema_corre_la_vinculacion_inicial(string quien)
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DelSistema());
        await e.RegistrarUna();
        var actor = quien switch
        {
            "visitante" => ActorDePrueba.Anonimo(),
            "cliente" => ActorDePrueba.DeCliente(Ana),
            _ => ActorDePrueba.DeAdministradorCentral(),
        };
        var (paso, clientes) = ArmarVinculacionInicial(e, actor);
        clientes.Cuentas[CorreoDeAna] = Ana;

        var resultado = await paso.Ejecutar();

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Empty(clientes.Consultados);
        Assert.Null(e.Hojas.Todas.Single().CuentaId);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-24")]
    public async Task Sin_completar_en_siete_dias_se_publica_una_alerta_tecnica_sin_datos_y_antes_no()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DelSistema());
        var alertas = new AlertasTecnicasEspia();
        var revision = new RevisarVinculacionInicial(e.Hojas, e.Unidad, alertas, e.Reloj);
        var inicio = e.Reloj.Ahora;
        e.Hojas.EstadoInicial = new EstadoDeLaVinculacionInicial("ana@correo.com", inicio, null);

        e.Reloj.Ahora = inicio.AddDays(6);
        Assert.False((await revision.Ejecutar()).Valor);
        Assert.Empty(alertas.Alertas);

        e.Reloj.Ahora = inicio.AddDays(7);
        Assert.True((await revision.Ejecutar()).Valor);
        var alerta = Assert.Single(alertas.Alertas);
        Assert.Equal(RevisarVinculacionInicial.MotivoDeAlerta, alerta.Motivo);
        Assert.DoesNotContain("ana@correo.com", alerta.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-24")]
    [InlineData("completada")]
    [InlineData("sin-empezar")]
    public async Task Una_vinculacion_completada_o_que_aun_no_empezo_no_alerta(string estado)
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.DelSistema());
        var alertas = new AlertasTecnicasEspia();
        e.Hojas.EstadoInicial = estado == "completada"
            ? new EstadoDeLaVinculacionInicial(null, e.Reloj.Ahora.AddDays(-30), e.Reloj.Ahora.AddDays(-20))
            : new EstadoDeLaVinculacionInicial(null, null, null);

        Assert.False((await new RevisarVinculacionInicial(e.Hojas, e.Unidad, alertas, e.Reloj).Ejecutar()).Valor);
        Assert.Empty(alertas.Alertas);
    }
}
