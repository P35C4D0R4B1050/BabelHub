using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Domain.Reclamaciones;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Reclamaciones;

/// <summary>Responder, reintentar el aviso, descartar y listar las hojas, solo la administración central (T05, T06; REC-07 a REC-09, REC-12, REC-13, REC-16).</summary>
public sealed class AdministracionDeHojasTests
{
    private static readonly Guid Admin = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static EntornoDeReclamaciones Entorno() => new(ActorDePrueba.DeAdministradorCentral(Admin));

    [Fact]
    [Trait("Criterio", "AC-REC-06")]
    public async Task Responder_una_hoja_pendiente_la_pasa_a_respondido_avisa_por_correo_y_deja_la_auditoria()
    {
        var e = Entorno();
        var id = await e.RegistrarUna();

        var resultado = await e.Administracion.Responder(new ResponderHojaEntrada(id, "Le reponemos el libro."));

        Assert.True(resultado.EsExito);
        Assert.Equal(new ResultadoDeLaHoja("2026-000001", EstadoDeLaHoja.Respondido, AvisoDeLaRespuesta.Enviado), resultado.Valor);
        var hoja = Assert.Single(e.Hojas.Todas);
        Assert.Equal(EstadoDeHoja.Respondido, hoja.Estado);
        Assert.Equal("Le reponemos el libro.", hoja.Respuesta);
        Assert.Equal(Admin, hoja.RespondidaPor);
        Assert.Equal(AvisoDeRespuesta.Enviado, hoja.AvisoDeRespuesta);
        var auditoria = Assert.Single(e.Auditoria.Solicitudes);
        Assert.Equal("HojaDeReclamacionRespondida", auditoria.Accion.Identificador);
        Assert.Equal("2026-000001", auditoria.ObjetoId);
        Assert.Equal("respondido", auditoria.Detalle["estadoNuevo"]);
        Assert.True(e.Hojas.LecturasConBloqueo >= 2); // la respuesta y la marca del aviso leen con bloqueo
    }

    [Fact]
    [Trait("Criterio", "AC-REC-14")]
    public async Task El_correo_de_la_respuesta_lleva_el_numero_la_fecha_y_la_respuesta_sin_el_documento_ni_el_domicilio()
    {
        var e = Entorno();
        var id = await e.RegistrarUna();

        await e.Administracion.Responder(new ResponderHojaEntrada(id, "Le reponemos el libro."));

        var correo = Assert.Single(e.Correo.Correos);
        Assert.Equal(CategoriaDeCorreo.RespuestaDeReclamo, correo.Categoria);
        Assert.Equal("Ana.Perez@Correo.COM", correo.Destinatario);
        Assert.Equal("2026-000001", correo.Datos["numero"]);
        Assert.Equal("2026-11-18", correo.Datos["fecha"]);
        Assert.Equal("Le reponemos el libro.", correo.Datos["respuesta"]);
        var todo = correo.Plantilla + correo + string.Join('|', correo.Datos.Select(p => p.Key + p.Value));
        Assert.DoesNotContain("12345678", todo);
        Assert.DoesNotContain("Olivos", todo);
        Assert.DoesNotContain("Ana", todo);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-11")]
    public async Task El_correo_se_envia_despues_de_confirmar_y_nunca_dentro_de_una_transaccion()
    {
        var e = Entorno();
        var id = await e.RegistrarUna();

        await e.Administracion.Responder(new ResponderHojaEntrada(id, "Listo."));

        Assert.Equal(0, e.Correo.LlamadasDentroDeUnaTransaccion);
        Assert.Single(e.Correo.Correos);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-11")]
    public async Task Si_el_correo_falla_la_hoja_queda_respondida_con_su_respuesta_y_el_aviso_pendiente()
    {
        var e = Entorno();
        var id = await e.RegistrarUna();
        e.Correo.Falla = true;

        var resultado = await e.Administracion.Responder(new ResponderHojaEntrada(id, "Listo."));

        Assert.True(resultado.EsExito);
        Assert.Equal(AvisoDeLaRespuesta.Pendiente, resultado.Valor.Aviso);
        var hoja = Assert.Single(e.Hojas.Todas);
        Assert.Equal(EstadoDeHoja.Respondido, hoja.Estado);
        Assert.Equal("Listo.", hoja.Respuesta);
        Assert.Equal(AvisoDeRespuesta.Pendiente, hoja.AvisoDeRespuesta);
        Assert.Null(hoja.AvisoEnCursoHasta);
        Assert.Empty(e.Correo.Correos);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-11")]
    public async Task Un_resultado_suprimido_cuenta_como_fallo_y_uno_encolado_como_enviado()
    {
        var suprimido = Entorno();
        var idUno = await suprimido.RegistrarUna();
        suprimido.Correo.Resultado = ResultadoDeEnvio.Suprimido;
        var encolado = Entorno();
        var idDos = await encolado.RegistrarUna();
        encolado.Correo.Resultado = ResultadoDeEnvio.Encolado;

        var a = await suprimido.Administracion.Responder(new ResponderHojaEntrada(idUno, "Listo."));
        var b = await encolado.Administracion.Responder(new ResponderHojaEntrada(idDos, "Listo."));

        Assert.Equal(AvisoDeLaRespuesta.Pendiente, a.Valor.Aviso);
        Assert.Equal(AvisoDeLaRespuesta.Enviado, b.Valor.Aviso);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-11")]
    public async Task Al_reintentar_con_el_envio_disponible_el_consumidor_recibe_el_correo_una_sola_vez()
    {
        var e = Entorno();
        var id = await e.RegistrarUna();
        e.Correo.Falla = true;
        await e.Administracion.Responder(new ResponderHojaEntrada(id, "Listo."));
        e.Correo.Falla = false;

        var reintento = await e.Administracion.ReintentarAviso(id);
        var segundo = await e.Administracion.ReintentarAviso(id);

        Assert.True(reintento.EsExito);
        Assert.Equal(AvisoDeLaRespuesta.Enviado, reintento.Valor.Aviso);
        Assert.Single(e.Correo.Correos);
        Assert.Equal(CategoriaDeError.Conflicto, segundo.Error!.Categoria);
        Assert.Equal("rec-estado-invalido", segundo.Error.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-11")]
    public async Task Un_reintento_con_el_arrendamiento_vigente_es_un_conflicto_y_no_envia_otro_correo()
    {
        var e = Entorno();
        var id = await e.RegistrarUna();
        // Una respuesta con su primer envío todavía en curso: el aviso está pendiente y el arrendamiento vigente.
        var hoja = (await e.Hojas.Obtener(id, false))!;
        hoja.Responder("Listo.", Admin, e.Reloj.Ahora);
        e.Hojas.Sembrar(hoja);

        var reintento = await e.Administracion.ReintentarAviso(id);

        Assert.Equal(CategoriaDeError.Conflicto, reintento.Error!.Categoria);
        Assert.Equal("rec-conflicto", reintento.Error.Clave);
        Assert.Empty(e.Correo.Correos);

        e.Reloj.Ahora = e.Reloj.Ahora.AddSeconds(61);
        var despues = await e.Administracion.ReintentarAviso(id);
        Assert.Equal(AvisoDeLaRespuesta.Enviado, despues.Valor.Aviso);
        Assert.Single(e.Correo.Correos);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-11")]
    public async Task Reintentar_una_hoja_no_respondida_o_ya_anonimizada_es_un_estado_invalido()
    {
        var e = Entorno();
        var pendiente = await e.RegistrarUna();
        var anonimizada = HojaDeReclamacion.Rehidratar(
            Guid.NewGuid(), NumeroDeHoja.Crear(2026, 50), TipoDeHoja.Reclamo, DatosDelConsumidor.Anonimizados(), "Libro", 10m, HojaDeReclamacion.TextoRedactado, null, null,
            e.Reloj.Ahora.AddYears(-2), "clave-vieja", EstadoDeHoja.Respondido, HojaDeReclamacion.TextoRedactado, e.Reloj.Ahora.AddYears(-2), Admin, AvisoDeRespuesta.Pendiente, null, null, null, false, e.Reloj.Ahora);
        e.Hojas.Sembrar(anonimizada);

        Assert.Equal("rec-estado-invalido", (await e.Administracion.ReintentarAviso(pendiente)).Error!.Clave);
        Assert.Equal("rec-estado-invalido", (await e.Administracion.ReintentarAviso(anonimizada.Id)).Error!.Clave);
        Assert.Empty(e.Correo.Correos);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-14")]
    [Trait("Criterio", "AC-REC-28")]
    public async Task Una_hoja_respondida_o_descartada_no_se_vuelve_a_responder_y_no_hay_otro_correo_ni_otra_auditoria()
    {
        var e = Entorno();
        var respondida = await e.RegistrarUna();
        var descartada = await e.RegistrarUna();
        await e.Administracion.Responder(new ResponderHojaEntrada(respondida, "Primera."));
        await e.Administracion.Descartar(descartada);
        var auditoriasAntes = e.Auditoria.Solicitudes.Count;

        var segunda = await e.Administracion.Responder(new ResponderHojaEntrada(respondida, "Segunda."));
        var sobreDescartada = await e.Administracion.Responder(new ResponderHojaEntrada(descartada, "Respuesta."));

        Assert.Equal("rec-estado-invalido", segunda.Error!.Clave);
        Assert.Equal("rec-estado-invalido", sobreDescartada.Error!.Clave);
        Assert.Equal(CategoriaDeError.Conflicto, segunda.Error.Categoria);
        Assert.Equal("Primera.", e.Hojas.Todas.Single(h => h.Id == respondida).Respuesta);
        Assert.Single(e.Correo.Correos);
        Assert.Equal(auditoriasAntes, e.Auditoria.Solicitudes.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-14")]
    public async Task Una_hoja_anonimizada_no_se_responde_porque_no_hay_a_quien_enviar_el_correo()
    {
        var e = Entorno();
        var anonimizada = HojaDeReclamacion.Rehidratar(
            Guid.NewGuid(), NumeroDeHoja.Crear(2024, 9), TipoDeHoja.Queja, DatosDelConsumidor.Anonimizados(), "Libro", 10m, HojaDeReclamacion.TextoRedactado, null, null,
            e.Reloj.Ahora.AddYears(-2).AddDays(-1), "clave-vieja", EstadoDeHoja.Pendiente, null, null, null, null, null, null, null, false, e.Reloj.Ahora);
        e.Hojas.Sembrar(anonimizada);

        var resultado = await e.Administracion.Responder(new ResponderHojaEntrada(anonimizada.Id, "Tarde."));

        Assert.Equal("rec-estado-invalido", resultado.Error!.Clave);
        Assert.Empty(e.Correo.Correos);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-14")]
    public async Task Sin_poder_auditar_la_respuesta_no_se_aplica_y_la_hoja_sigue_pendiente_sin_correo()
    {
        var e = Entorno();
        var id = await e.RegistrarUna();
        e.Auditoria.Caida = true;

        var resultado = await e.Administracion.Responder(new ResponderHojaEntrada(id, "Listo."));

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        var hoja = Assert.Single(e.Hojas.Todas);
        Assert.Equal(EstadoDeHoja.Pendiente, hoja.Estado);
        Assert.Null(hoja.Respuesta);
        Assert.Empty(e.Correo.Correos);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-06")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Una_respuesta_vacia_se_rechaza_con_su_campo(string? respuesta)
    {
        var e = Entorno();
        var id = await e.RegistrarUna();

        var resultado = await e.Administracion.Responder(new ResponderHojaEntrada(id, respuesta));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal("respuesta:falta", $"{resultado.Error.Errores.Single().Campo}:{resultado.Error.Errores.Single().Clave}");
        Assert.Equal(EstadoDeHoja.Pendiente, e.Hojas.Todas.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-06")]
    public async Task Una_respuesta_de_mas_de_3000_caracteres_se_rechaza()
    {
        var e = Entorno();
        var id = await e.RegistrarUna();

        var resultado = await e.Administracion.Responder(new ResponderHojaEntrada(id, new string('x', 3001)));
        var exacta = await e.Administracion.Responder(new ResponderHojaEntrada(id, new string('x', 3000)));

        Assert.Equal("respuesta:invalido", $"{resultado.Error!.Errores.Single().Campo}:{resultado.Error.Errores.Single().Clave}");
        Assert.True(exacta.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-06")]
    public async Task Una_hoja_inexistente_responde_no_encontrado()
    {
        var e = Entorno();

        var resultado = await e.Administracion.Responder(new ResponderHojaEntrada(Guid.NewGuid(), "Listo."));

        Assert.Equal(CategoriaDeError.NoEncontrado, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-18")]
    public async Task Responder_y_descartar_siguen_la_spec_para_la_hoja_de_una_cuenta_cancelada()
    {
        var e = Entorno();
        var paraResponder = await e.RegistrarUna();
        var paraDescartar = await e.RegistrarUna();
        foreach (var id in new[] { paraResponder, paraDescartar })
        {
            var hoja = (await e.Hojas.Obtener(id, false))!;
            hoja.MarcarDeCuentaCancelada();
            e.Hojas.Sembrar(hoja);
        }

        var respondida = await e.Administracion.Responder(new ResponderHojaEntrada(paraResponder, "Listo."));
        var descartada = await e.Administracion.Descartar(paraDescartar);

        Assert.True(respondida.EsExito);
        Assert.True(descartada.EsExito);
        Assert.Equal("Ana.Perez@Correo.COM", Assert.Single(e.Correo.Correos).Destinatario);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-10")]
    public async Task Descartar_una_hoja_pendiente_la_marca_audita_y_no_la_elimina()
    {
        var e = Entorno();
        var id = await e.RegistrarUna();

        var resultado = await e.Administracion.Descartar(id);

        Assert.Equal(new ResultadoDeLaHoja("2026-000001", EstadoDeLaHoja.Descartado, null), resultado.Valor);
        var hoja = Assert.Single(e.Hojas.Todas);
        Assert.Equal(EstadoDeHoja.Descartado, hoja.Estado);
        Assert.NotNull(hoja.DescartadaEn);
        var auditoria = Assert.Single(e.Auditoria.Solicitudes);
        Assert.Equal("HojaDeReclamacionDescartada", auditoria.Accion.Identificador);
        Assert.Equal("2026-000001", auditoria.ObjetoId);
        Assert.Equal("spam", auditoria.Detalle["causa"]);
        Assert.Empty(e.Correo.Correos);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-10")]
    public async Task Solo_se_descarta_una_hoja_pendiente_y_una_descartada_no_se_descarta_otra_vez()
    {
        var e = Entorno();
        var respondida = await e.RegistrarUna();
        var descartada = await e.RegistrarUna();
        await e.Administracion.Responder(new ResponderHojaEntrada(respondida, "Listo."));
        await e.Administracion.Descartar(descartada);

        var a = await e.Administracion.Descartar(respondida);
        var b = await e.Administracion.Descartar(descartada);

        Assert.Equal("rec-estado-invalido", a.Error!.Clave);
        Assert.Equal("rec-estado-invalido", b.Error!.Clave);
        Assert.Equal(2, e.Auditoria.Solicitudes.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-10")]
    public async Task Sin_poder_auditar_el_descarte_no_se_aplica()
    {
        var e = Entorno();
        var id = await e.RegistrarUna();
        e.Auditoria.Caida = true;

        var resultado = await e.Administracion.Descartar(id);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(EstadoDeHoja.Pendiente, e.Hojas.Todas.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-07")]
    public async Task El_listado_por_defecto_muestra_pendientes_y_respondidas_y_oculta_las_descartadas()
    {
        var e = Entorno();
        var pendiente = await e.RegistrarUna();
        var respondida = await e.RegistrarUna();
        var descartada = await e.RegistrarUna();
        await e.Administracion.Responder(new ResponderHojaEntrada(respondida, "Listo."));
        await e.Administracion.Descartar(descartada);

        var lista = (await e.Administracion.Listar(new ListarHojasEntrada())).Valor;

        Assert.Equal(2, lista.Total);
        Assert.Equal(new[] { pendiente, respondida }.Order(), lista.Hojas.Select(h => h.Id).Order());
    }

    [Fact]
    [Trait("Criterio", "AC-REC-07")]
    public async Task Filtrar_por_pendientes_muestra_solo_esas_y_por_descartadas_solo_las_descartadas()
    {
        var e = Entorno();
        var pendiente = await e.RegistrarUna();
        var respondida = await e.RegistrarUna();
        var descartada = await e.RegistrarUna();
        await e.Administracion.Responder(new ResponderHojaEntrada(respondida, "Listo."));
        await e.Administracion.Descartar(descartada);

        var pendientes = (await e.Administracion.Listar(new ListarHojasEntrada(EstadoDeLaHoja.Pendiente))).Valor;
        var descartadas = (await e.Administracion.Listar(new ListarHojasEntrada(EstadoDeLaHoja.Descartado))).Valor;

        Assert.Equal(pendiente, Assert.Single(pendientes.Hojas).Id);
        var fila = Assert.Single(descartadas.Hojas);
        Assert.Equal(descartada, fila.Id);
        Assert.Equal(EstadoDeLaHoja.Descartado, fila.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-10")]
    public async Task El_listado_no_trae_datos_personales_y_muestra_numero_fecha_tipo_estado_y_aviso()
    {
        var e = Entorno();
        var id = await e.RegistrarUna();
        await e.Administracion.Responder(new ResponderHojaEntrada(id, "Listo."));

        var fila = Assert.Single((await e.Administracion.Listar(new ListarHojasEntrada())).Valor.Hojas);

        Assert.Equal("2026-000001", fila.Numero);
        Assert.Equal("reclamo", fila.Tipo);
        Assert.Equal(AvisoDeLaRespuesta.Enviado, fila.Aviso);
        var propiedades = typeof(ResumenDeHoja).GetProperties().Select(p => p.Name).ToArray();
        Assert.Equal(["Id", "Numero", "FechaDeRegistro", "Tipo", "Estado", "Aviso"], propiedades);
        Assert.DoesNotContain("Ana", fila.ToString());
        Assert.DoesNotContain(e.Auditoria.Solicitudes, s => s.Accion.Identificador == "HojaDeReclamacionConsultada");
    }

    [Fact]
    [Trait("Criterio", "AC-REC-07")]
    public async Task El_listado_pagina_con_tope_de_50_y_rechaza_paginas_y_tamanos_invalidos()
    {
        var e = Entorno();
        for (var i = 0; i < 3; i++)
        {
            await e.RegistrarUna();
        }

        var primera = (await e.Administracion.Listar(new ListarHojasEntrada(null, 1, 2))).Valor;
        var segunda = (await e.Administracion.Listar(new ListarHojasEntrada(null, 2, 2))).Valor;
        var demasiado = await e.Administracion.Listar(new ListarHojasEntrada(null, 1, 51));
        var cero = await e.Administracion.Listar(new ListarHojasEntrada(null, 0, 10));
        var tope = await e.Administracion.Listar(new ListarHojasEntrada(null, 1, 50));

        Assert.Equal(2, primera.Hojas.Count);
        Assert.Single(segunda.Hojas);
        Assert.Equal(3, segunda.Total);
        Assert.Equal(["tamano:invalido"], demasiado.Error!.Errores.Select(x => $"{x.Campo}:{x.Clave}").ToArray());
        Assert.Equal(["pagina:invalido"], cero.Error!.Errores.Select(x => $"{x.Campo}:{x.Clave}").ToArray());
        Assert.True(tope.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-16")]
    public async Task Solo_la_administracion_central_opera_y_cada_intento_rechazado_deja_un_acceso_denegado_sin_tocar_nada()
    {
        var admin = Entorno();
        var id = await admin.RegistrarUna();

        var casos = new (ActorDePrueba Actor, CategoriaDeError Esperado)[]
        {
            (ActorDePrueba.DeCliente(), CategoriaDeError.Prohibido),
            (ActorDePrueba.DeEncargadoDe(Guid.NewGuid()), CategoriaDeError.Prohibido),
            (ActorDePrueba.DePersonalDeLogistica(), CategoriaDeError.Prohibido),
            (ActorDePrueba.Anonimo(), CategoriaDeError.NoAutenticado),
            (ActorDePrueba.SinContexto(), CategoriaDeError.NoAutenticado),
        };

        foreach (var (actor, esperado) in casos)
        {
            admin.Actor.Establecer(actor.Actual);
            var antes = admin.Denegaciones.Eventos.Count;

            var resultados = new[]
            {
                (await admin.Administracion.Listar(new ListarHojasEntrada())).Error,
                (await admin.Administracion.Responder(new ResponderHojaEntrada(id, "Hola."))).Error,
                (await admin.Administracion.Descartar(id)).Error,
                (await admin.Administracion.ReintentarAviso(id)).Error,
            };

            Assert.All(resultados, r => Assert.Equal(esperado, r!.Categoria));
            Assert.Equal(antes + 4, admin.Denegaciones.Eventos.Count);
        }

        Assert.Equal(EstadoDeHoja.Pendiente, admin.Hojas.Todas.Single().Estado);
        Assert.Empty(admin.Correo.Correos);
        Assert.Empty(admin.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-14")]
    public async Task Responder_descartar_y_reintentar_leen_la_hoja_con_bloqueo()
    {
        var e = Entorno();
        var respondida = await e.RegistrarUna();
        var descartada = await e.RegistrarUna();
        e.Correo.Falla = true;
        await e.Administracion.Responder(new ResponderHojaEntrada(respondida, "Listo."));
        var antes = e.Hojas.LecturasConBloqueo;

        await e.Administracion.Descartar(descartada);
        Assert.Equal(antes + 1, e.Hojas.LecturasConBloqueo);
        e.Correo.Falla = false;
        await e.Administracion.ReintentarAviso(respondida);
        Assert.True(e.Hojas.LecturasConBloqueo >= antes + 3);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-14")]
    public async Task Un_rechazo_por_rol_no_toca_el_repositorio_ni_audita_ni_envia()
    {
        var e = Entorno();
        var id = await e.RegistrarUna();
        e.Actor.Establecer(ActorDePrueba.DeCliente().Actual);
        var operaciones = e.Hojas.Operaciones;

        await e.Administracion.Listar(new ListarHojasEntrada());
        await e.Administracion.Responder(new ResponderHojaEntrada(id, "Hola."));
        await e.Administracion.Descartar(id);
        await e.Administracion.ReintentarAviso(id);

        Assert.Equal(operaciones, e.Hojas.Operaciones);
        Assert.Empty(e.Auditoria.Solicitudes);
        Assert.Empty(e.Correo.Correos);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-14")]
    public async Task Un_lock_timeout_al_bloquear_la_hoja_es_un_conflicto_y_no_cambia_nada()
    {
        var e = Entorno();
        var id = await e.RegistrarUna();
        e.Hojas.BloqueoNoDisponible = true;

        var responder = await e.Administracion.Responder(new ResponderHojaEntrada(id, "Listo."));
        var descartar = await e.Administracion.Descartar(id);
        var reintentar = await e.Administracion.ReintentarAviso(id);

        Assert.All(new[] { responder.Error, descartar.Error, reintentar.Error }, error => Assert.Equal(CategoriaDeError.Conflicto, error!.Categoria));
        Assert.Equal(EstadoDeHoja.Pendiente, e.Hojas.Todas.Single().Estado);
        Assert.Empty(e.Correo.Correos);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-06")]
    [InlineData("Respuesta con NUL \0 oculto")]
    [InlineData("Respuesta con bidi \u202E oculto")]
    public async Task Una_respuesta_con_controles_o_bidireccionales_se_rechaza(string respuesta)
    {
        var e = Entorno();
        var id = await e.RegistrarUna();

        var resultado = await e.Administracion.Responder(new ResponderHojaEntrada(id, respuesta));

        Assert.Equal("respuesta:invalido", $"{resultado.Error!.Errores.Single().Campo}:{resultado.Error.Errores.Single().Clave}");
        Assert.Equal(EstadoDeHoja.Pendiente, e.Hojas.Todas.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-16")]
    public void El_rol_de_administracion_central_coincide_con_el_de_los_roles_de_usuario()
    {
        Assert.Equal(BabelHub.Shared.Contratos.RolesDeUsuario.AdministradorCentral, BabelHub.Ecommerce.Application.Reclamaciones.Interno.RolesDeReclamaciones.AdministradorCentral);
    }
}
