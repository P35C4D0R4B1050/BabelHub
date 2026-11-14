using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>Asegurar la credencial de la sesión: renovación bajo bloqueo, rechazo, fallo transitorio y verificación (T07, WEB-32, D04, D05).</summary>
public sealed class AsegurarCredencialTests
{
    private static Task<ResultadoDeCredencial> Asegurar(DoblesDeSesion d, string cookie, HostWeb host = HostWeb.Tienda) =>
        d.Asegurar.Ejecutar(cookie, host, default);

    private static ResultadoDeRenovacion.Renovada Renovada(DoblesDeSesion d, string credencial = "jwt-ok-2", string renovacion = "renov-2", int minutos = 15) =>
        new(credencial, d.Reloj.Ahora.AddMinutes(minutos), renovacion);

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Con_la_credencial_vigente_no_se_renueva_y_se_entrega_el_actor_verificado_y_el_token_en_memoria()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();

        var resultado = await Asegurar(d, cookie);

        Assert.Equal(EstadoDeCredencial.Asegurada, resultado.Estado);
        Assert.Equal(DoblesDeSesion.ActorCliente, resultado.Actor);
        Assert.Equal("jwt-ok-1", resultado.Credencial!.Valor);
        Assert.DoesNotContain("jwt-ok", $"{resultado.Credencial}", StringComparison.Ordinal);
        Assert.Equal(0, d.Identidad.LlamadasARenovar);
        Assert.Equal(0, d.Almacen.CargasParaRenovar);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Con_menos_de_60_segundos_se_renueva_bajo_el_bloqueo_de_la_fila_y_se_guardan_las_nuevas_credenciales_cifradas()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(vence: 15);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(14).AddSeconds(30);
        d.Identidad.Renovaciones.Enqueue(Renovada(d));

        var resultado = await Asegurar(d, cookie);

        Assert.Equal(EstadoDeCredencial.Asegurada, resultado.Estado);
        Assert.Equal("jwt-ok-2", resultado.Credencial!.Valor);
        Assert.Equal(1, d.Identidad.LlamadasARenovar);
        Assert.Equal(1, d.Almacen.CargasParaRenovar);
        var fila = d.Fila(cookie);
        Assert.Equal(d.Reloj.Ahora.AddMinutes(15), fila.CredencialVenceEn);
        Assert.False(fila.RenovacionIncierta);
        Assert.Equal("renov-2", d.Cifrador.Descifrar(fila.RenovacionCifrada!, fila.VersionDeClave, PropositoDeSecreto.Renovacion, ServiciosDeSesion.DatosAsociados(fila.Sesion.Id, PropositoDeSecreto.Renovacion, DoblesDeSesion.Usuario)));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Solicitudes_seguidas_sobre_una_credencial_por_vencer_renuevan_una_sola_vez_porque_relee_la_fila_bajo_el_bloqueo()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(vence: 15);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(14).AddSeconds(30);
        d.Identidad.Renovaciones.Enqueue(Renovada(d));

        var resultados = new List<ResultadoDeCredencial>();
        for (var i = 0; i < 20; i++)
        {
            resultados.Add(await Asegurar(d, cookie));
        }

        Assert.All(resultados, r => Assert.Equal(EstadoDeCredencial.Asegurada, r.Estado));
        Assert.Equal(1, d.Identidad.LlamadasARenovar);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Otra_solicitud_que_renueva_mientras_esta_espera_el_bloqueo_evita_la_segunda_llamada_a_Identidad()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(vence: 15);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(14).AddSeconds(30);

        // La fila se renueva «por otra solicitud» entre la primera lectura y la toma del bloqueo.
        var hash = ValorDeCookieDeSesion.Hash(cookie);
        var cargas = 0;
        var envuelto = new AlmacenQueRenuevaAlBloquear(d, hash, () => cargas++);
        var asegurar = new AsegurarCredencialDeSesion(new ServiciosDeSesion(envuelto, d.Cifrador, d.Identidad, d.Seguridad, d.Reloj), d.Unidad, d.Verificador, d.Alertas);

        var resultado = await asegurar.Ejecutar(cookie, HostWeb.Tienda, default);

        Assert.Equal(EstadoDeCredencial.Asegurada, resultado.Estado);
        Assert.Equal(0, d.Identidad.LlamadasARenovar);
        Assert.Equal("jwt-ok-otra", resultado.Credencial!.Valor);
        Assert.Equal(1, cargas);
    }

    private sealed class AlmacenQueRenuevaAlBloquear(DoblesDeSesion d, HashDeSesion id, Action alBloquear) : IAlmacenDeSesionesWeb
    {
        public Task Insertar(SesionGuardada sesion, CancellationToken cancelacion = default) => d.Almacen.Insertar(sesion, cancelacion);

        public Task<SesionGuardada?> Cargar(HashDeSesion id, CancellationToken cancelacion = default) => d.Almacen.Cargar(id, cancelacion);

        public Task Guardar(SesionGuardada sesion, CancellationToken cancelacion = default) => d.Almacen.Guardar(sesion, cancelacion);

        public Task<IReadOnlyList<SesionGuardada>> CargarAbiertasDeUnUsuario(Guid usuarioId, CancellationToken cancelacion = default) => d.Almacen.CargarAbiertasDeUnUsuario(usuarioId, cancelacion);

        public async Task<SesionGuardada?> CargarParaRenovar(HashDeSesion idCargado, CancellationToken cancelacion = default)
        {
            alBloquear();
            var fila = await d.Almacen.CargarParaRenovar(idCargado, cancelacion);
            var cifrada = d.Cifrador.Cifrar("jwt-ok-otra", PropositoDeSecreto.Credencial, ServiciosDeSesion.DatosAsociados(id, PropositoDeSecreto.Credencial, DoblesDeSesion.Usuario));
            fila!.CredencialCifrada = cifrada.Bytes;
            fila.CredencialVenceEn = d.Reloj.Ahora.AddMinutes(15);
            return fila;
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Un_rechazo_de_Identidad_cierra_la_sesion_conserva_el_borrador_borra_la_credencial_y_revoca_la_renovacion()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(vence: 15, renovacion: "renov-1");
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(14).AddSeconds(30);
        d.Identidad.Renovaciones.Enqueue(new ResultadoDeRenovacion.Rechazada());

        var resultado = await Asegurar(d, cookie);

        Assert.Equal(EstadoDeCredencial.SesionCerrada, resultado.Estado);
        Assert.Null(resultado.Credencial);
        var fila = d.Fila(cookie);
        Assert.Equal(MotivoDeCierre.RenovacionRechazada, fila.Sesion.MotivoDeCierre);
        Assert.True(fila.Sesion.MotivoDeCierre!.Value.ConservaElBorrador());
        Assert.Null(fila.CredencialCifrada);
        Assert.Null(fila.RenovacionCifrada);
        Assert.Equal(["renov-1"], d.Identidad.Revocadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Un_fallo_transitorio_responde_servicio_no_disponible_sin_cerrar_ni_usar_la_credencial_vencida_y_la_solicitud_siguiente_reintenta()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(vence: 15);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(16);
        d.Identidad.Renovaciones.Enqueue(new ResultadoDeRenovacion.Transitoria());

        var primera = await Asegurar(d, cookie);

        Assert.Equal(EstadoDeCredencial.ServicioNoDisponible, primera.Estado);
        Assert.Null(primera.Credencial);
        Assert.Equal(0, d.Verificador.Llamadas);
        var fila = d.Fila(cookie);
        Assert.Equal(EstadoDeSesion.Abierta, fila.Sesion.Estado);
        Assert.True(fila.RenovacionIncierta);

        d.Identidad.Renovaciones.Enqueue(Renovada(d));
        var segunda = await Asegurar(d, cookie);
        Assert.Equal(EstadoDeCredencial.Asegurada, segunda.Estado);
        Assert.False(d.Fila(cookie).RenovacionIncierta);
        Assert.Equal(2, d.Identidad.LlamadasARenovar);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Una_excepcion_al_llamar_a_Identidad_es_un_fallo_transitorio()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(vence: 15);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(16);
        d.Identidad.AntesDeRenovar = () => throw new HttpRequestException("caído");

        var resultado = await Asegurar(d, cookie);

        Assert.Equal(EstadoDeCredencial.ServicioNoDisponible, resultado.Estado);
        Assert.Equal(EstadoDeSesion.Abierta, d.Fila(cookie).Sesion.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Despues_de_un_fallo_incierto_si_Identidad_ve_la_renovacion_como_reutilizada_el_rechazo_cierra_la_sesion()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(vence: 15);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(16);
        d.Identidad.Renovaciones.Enqueue(new ResultadoDeRenovacion.Transitoria());
        await Asegurar(d, cookie);
        d.Identidad.Renovaciones.Enqueue(new ResultadoDeRenovacion.Rechazada());

        var resultado = await Asegurar(d, cookie);

        Assert.Equal(EstadoDeCredencial.SesionCerrada, resultado.Estado);
        Assert.Equal(MotivoDeCierre.RenovacionRechazada, d.Fila(cookie).Sesion.MotivoDeCierre);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Una_credencial_invalida_intenta_una_renovacion_y_si_se_rechaza_cierra_la_sesion()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(credencial: "jwt-con-kid-revocado");
        d.Identidad.Renovaciones.Enqueue(new ResultadoDeRenovacion.Rechazada());

        var resultado = await Asegurar(d, cookie);

        Assert.Equal(EstadoDeCredencial.SesionCerrada, resultado.Estado);
        Assert.Equal(1, d.Identidad.LlamadasARenovar);
        Assert.Equal(MotivoDeCierre.RenovacionRechazada, d.Fila(cookie).Sesion.MotivoDeCierre);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Una_credencial_invalida_que_Identidad_renueva_con_exito_sigue_adelante()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(credencial: "jwt-con-kid-revocado");
        d.Identidad.Renovaciones.Enqueue(Renovada(d));

        var resultado = await Asegurar(d, cookie);

        Assert.Equal(EstadoDeCredencial.Asegurada, resultado.Estado);
        Assert.Equal("jwt-ok-2", resultado.Credencial!.Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Si_la_credencial_renovada_tambien_es_invalida_se_cierra_y_no_se_renueva_en_bucle()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(credencial: "jwt-mala");
        d.Identidad.Renovaciones.Enqueue(Renovada(d, credencial: "jwt-tambien-mala"));
        d.Identidad.Renovaciones.Enqueue(Renovada(d, credencial: "jwt-otra-mala"));

        var resultado = await Asegurar(d, cookie);

        Assert.Equal(EstadoDeCredencial.SesionCerrada, resultado.Estado);
        Assert.Equal(1, d.Identidad.LlamadasARenovar);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task Con_el_verificador_sin_claves_vigentes_no_se_cierra_ninguna_sesion_y_se_alerta()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();
        d.Verificador.Regla = (_, _) => new VerificacionDeCredencial(EstadoDeVerificacion.SinClavesVigentes, null);

        var resultado = await Asegurar(d, cookie);

        Assert.Equal(EstadoDeCredencial.ServicioNoDisponible, resultado.Estado);
        Assert.Equal(EstadoDeSesion.Abierta, d.Fila(cookie).Sesion.Estado);
        Assert.Equal(0, d.Identidad.LlamadasARenovar);
        Assert.Single(d.Alertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task Un_secreto_que_no_se_puede_descifrar_cierra_la_sesion_con_un_evento_y_nunca_falla_con_una_excepcion()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();
        d.Cifrador.Falla = true;

        var resultado = await Asegurar(d, cookie);

        Assert.Equal(EstadoDeCredencial.SesionCerrada, resultado.Estado);
        Assert.Equal(EstadoDeSesion.Cerrada, d.Fila(cookie).Sesion.Estado);
        Assert.Contains(d.Seguridad.Eventos, e => e.Motivo == MotivoDeDenegacionWeb.SecretoIlegible);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task Una_cookie_de_otro_host_cerrada_o_inexistente_no_tiene_sesion()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir();
        var cerrada = await d.Abrir();
        d.Fila(cerrada).Sesion.Cerrar(MotivoDeCierre.Voluntario, DoblesDeSesion.Inicio);

        Assert.Equal(EstadoDeCredencial.SinSesion, (await Asegurar(d, cookie, HostWeb.Gestion)).Estado);
        Assert.Equal(EstadoDeCredencial.SinSesion, (await Asegurar(d, cerrada)).Estado);
        Assert.Equal(EstadoDeCredencial.SinSesion, (await Asegurar(d, "mala")).Estado);
        Assert.Equal(EstadoDeCredencial.SinSesion, (await Asegurar(d, new string('Q', 43))).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task El_orden_de_bloqueo_es_primero_las_filas_web_y_despues_Identidad()
    {
        var d = new DoblesDeSesion();
        var cookie = await d.Abrir(vence: 15);
        d.Reloj.Ahora = DoblesDeSesion.Inicio.AddMinutes(16);
        var orden = new List<string>();
        d.Identidad.AntesDeRenovar = () =>
        {
            orden.Add($"identidad (filas bloqueadas: {d.Almacen.CargasParaRenovar})");
            return Task.CompletedTask;
        };
        d.Identidad.Renovaciones.Enqueue(Renovada(d));

        await Asegurar(d, cookie);

        Assert.Equal(["identidad (filas bloqueadas: 1)"], orden);
    }
}
