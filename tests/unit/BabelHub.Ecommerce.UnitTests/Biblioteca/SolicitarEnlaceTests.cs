using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Biblioteca;

/// <summary>El orquestador de la descarga y su transacción corta (T06, T07; BIB-03 a BIB-07, BIB-10, BIB-13, BIB-15, BIB-17, BIB-23, BIB-28).</summary>
public sealed class SolicitarEnlaceTests
{
    private const string Clave = "0123456789abcdef0123456789abcdef";
    private const string OtraClave = "fedcba9876543210fedcba9876543210";

    private static string Codigo(Resultado<EnlaceDeDescarga> r) => r.Error!.Clave;

    [Fact]
    [Trait("Criterio", "AC-BIB-04")]
    public async Task El_titular_recibe_un_enlace_de_cinco_minutos_la_descarga_se_cuenta_y_queda_un_registro()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Cien años de soledad", descargas: 2);

        var resultado = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        Assert.True(resultado.EsExito);
        Assert.Equal(entorno.Reloj.Ahora.AddSeconds(300), resultado.Valor.VenceEn);
        var enlace = Assert.Single(entorno.Entrega.Enlaces);
        Assert.Equal(300, enlace.Vigencia);
        Assert.Equal("Cien años de soledad.epub", enlace.Nombre);
        Assert.Equal(3, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
        var registro = Assert.Single(entorno.Descargas.Registros);
        Assert.Equal(acceso.Id, registro.AccesoId);
        Assert.Equal(acceso.CuentaId, registro.UsuarioId);
        Assert.Equal(acceso.EdicionId, registro.EdicionId);
        Assert.Equal(entorno.Reloj.Ahora, registro.EntregadaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-04")]
    public async Task El_conteo_ocurre_al_entregar_aunque_el_enlace_no_se_use_y_la_url_no_se_guarda_en_ninguna_parte()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro");

        var resultado = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        var url = resultado.Valor.Url.AbsoluteUri;
        Assert.Equal(1, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
        var guardado = string.Join('|', entorno.Descargas.Claves.Select(c => $"{c.FirmadoEn:O}{c.VenceEn:O}{c.Clave.Hexadecimal}{c.DescargaId}"))
            + string.Join('|', entorno.Descargas.Registros.Select(r => r.ToString()))
            + string.Join('|', entorno.Auditoria.Solicitudes.Select(s => s.ToString()));
        Assert.DoesNotContain("X-Amz-Signature", guardado, StringComparison.Ordinal);
        Assert.DoesNotContain(url, guardado, StringComparison.Ordinal);
        Assert.DoesNotContain(Clave, guardado, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-05")]
    public async Task Con_cinco_descargas_usadas_se_rechaza_explicando_el_limite()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro", descargas: 5);

        var resultado = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        Assert.Equal(ClavesDeBiblioteca.LimiteDeDescargas, Codigo(resultado));
        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
        Assert.Equal(0, entorno.Entrega.Comprobaciones);
        Assert.Empty(entorno.Descargas.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-04")]
    public async Task La_misma_clave_dentro_de_la_vigencia_devuelve_el_mismo_enlace_sin_contar_otra_descarga()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro");

        var primero = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);
        entorno.Reloj.Ahora = entorno.Reloj.Ahora.AddSeconds(120);
        var segundo = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        Assert.Equal(primero.Valor.Url, segundo.Valor.Url);
        Assert.Equal(primero.Valor.VenceEn, segundo.Valor.VenceEn);
        Assert.Equal(1, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
        Assert.Single(entorno.Descargas.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-05")]
    public async Task La_quinta_descarga_interrumpida_se_reintenta_con_el_mismo_enlace_aunque_el_cupo_este_agotado()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro", descargas: 4);
        var quinta = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);
        Assert.Equal(5, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);

        entorno.Reloj.Ahora = entorno.Reloj.Ahora.AddSeconds(60);
        var reintento = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);
        var otraClave = await entorno.Descarga.SolicitarEnlace(acceso.Id, OtraClave);

        Assert.Equal(quinta.Valor.Url, reintento.Valor.Url);
        Assert.Equal(5, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
        Assert.Equal(ClavesDeBiblioteca.LimiteDeDescargas, Codigo(otraClave));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-04")]
    public async Task Repetida_a_los_seis_minutos_cuenta_una_descarga_nueva_con_un_enlace_nuevo_y_cien_repeticiones_no_superan_el_primer_vencimiento()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro");
        var primero = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        for (var i = 1; i <= 100; i++)
        {
            entorno.Reloj.Ahora = entorno.Reloj.Ahora.AddSeconds(2);
            var repeticion = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);
            if (repeticion.EsExito && repeticion.Valor.VenceEn == primero.Valor.VenceEn)
            {
                continue;
            }

            Assert.Fail($"La repetición {i} no debía superar el primer vencimiento.");
        }

        Assert.Equal(1, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
        entorno.Reloj.Ahora = new DateTimeOffset(2026, 11, 20, 15, 6, 0, TimeSpan.Zero);
        var nuevo = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        Assert.True(nuevo.EsExito);
        Assert.NotEqual(primero.Valor.Url, nuevo.Valor.Url);
        Assert.Equal(2, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
        Assert.Equal(2, entorno.Descargas.Registros.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-15")]
    public async Task Una_clave_repetida_que_llega_a_la_transaccion_revierte_el_conteo_y_reconstruye_el_mismo_enlace()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro");
        var primero = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        // La segunda solicitud simultánea leyó el acceso antes de que la primera confirmara: llega a la transacción con la clave ya registrada.
        var salida = await entorno.Consumir.Ejecutar(new EntradaDeConsumo(
            entorno.Accesos.Buscar(acceso.Id)!,
            ClaveDeDescarga.Crear(Clave)!,
            "libros/x.epub",
            "Libro.epub",
            entorno.Reloj.Ahora.AddSeconds(1),
            new DateOnly(2026, 11, 20)));

        Assert.IsType<SalidaDeConsumo.RepeticionVigente>(salida);
        Assert.Equal(1, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
        Assert.Single(entorno.Descargas.Registros);
        Assert.True(primero.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-15")]
    public async Task Con_una_sola_descarga_restante_dos_solicitudes_distintas_dan_un_solo_enlace_y_el_contador_queda_en_el_maximo()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro", descargas: 4);

        var a = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);
        var b = await entorno.Descarga.SolicitarEnlace(acceso.Id, OtraClave);

        Assert.True(a.EsExito);
        Assert.Equal(ClavesDeBiblioteca.LimiteDeDescargas, Codigo(b));
        Assert.Equal(5, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
        Assert.Single(entorno.Descargas.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-15")]
    public async Task Si_otro_proceso_agota_el_cupo_tras_la_lectura_se_relee_y_se_responde_el_limite_sin_contar()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro", descargas: 4);
        entorno.Accesos.DespuesDeLeerUnAcceso = () =>
        {
            entorno.Accesos.Buscar(acceso.Id)!.ConsumirDescarga();
            entorno.Accesos.DespuesDeLeerUnAcceso = null;
        };

        var resultado = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        Assert.Equal(ClavesDeBiblioteca.LimiteDeDescargas, Codigo(resultado));
        Assert.Equal(5, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
        Assert.Empty(entorno.Descargas.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-20")]
    public async Task Si_un_reembolso_revoca_el_acceso_tras_la_lectura_no_se_entrega_ningun_enlace_ni_se_cuenta()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro");
        entorno.Accesos.DespuesDeLeerUnAcceso = () =>
        {
            var vivo = entorno.Accesos.Buscar(acceso.Id)!;
            vivo.Revocar(vivo.PedidoId, CausaDeRevocacion.Reembolso, entorno.Reloj.Ahora);
            entorno.Accesos.DespuesDeLeerUnAcceso = null;
        };

        var resultado = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        Assert.Equal(ClavesDeBiblioteca.AccesoNoDisponible, Codigo(resultado));
        Assert.Equal(0, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
        Assert.Empty(entorno.Descargas.Registros);
        Assert.Empty(entorno.Descargas.Claves);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-26")]
    public async Task Si_la_cuenta_se_cancela_tras_la_lectura_se_rechaza_como_no_encontrado_sin_ningun_registro()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro");
        entorno.Accesos.DespuesDeLeerUnAcceso = () =>
        {
            entorno.Accesos.Eliminar(acceso.Id);
            entorno.Accesos.DespuesDeLeerUnAcceso = null;
        };

        var resultado = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        Assert.Equal(ClavesDeBiblioteca.NoEncontrado, Codigo(resultado));
        Assert.Empty(entorno.Descargas.Registros);
        Assert.Empty(entorno.Descargas.Claves);
        Assert.Single(entorno.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-26")]
    public async Task Si_la_clave_falla_por_la_clave_foranea_se_revierte_todo_y_se_responde_no_encontrado_sin_ningun_registro()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro");
        entorno.Descargas.ForzarAccesoInexistente = true;

        var resultado = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        Assert.Equal(ClavesDeBiblioteca.NoEncontrado, Codigo(resultado));
        Assert.Equal(0, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
        Assert.Empty(entorno.Descargas.Registros);
        Assert.Empty(entorno.Descargas.Claves);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-03")]
    public async Task Un_cliente_ajeno_o_un_acceso_inexistente_reciben_lo_mismo_y_se_registra_cada_intento()
    {
        var entorno = new EntornoDeBiblioteca();
        var ajeno = entorno.SembrarLibro("De otra", cuenta: Guid.NewGuid());

        var deAjeno = await entorno.Descarga.SolicitarEnlace(ajeno.Id, Clave);
        var inexistente = await entorno.Descarga.SolicitarEnlace(Guid.NewGuid(), OtraClave);

        Assert.Equal(ClavesDeBiblioteca.NoEncontrado, Codigo(deAjeno));
        Assert.Equal(ClavesDeBiblioteca.NoEncontrado, Codigo(inexistente));
        Assert.Equal(2, entorno.Denegaciones.Eventos.Count);
        Assert.Equal(0, entorno.Accesos.Buscar(ajeno.Id)!.DescargasUsadas);
        Assert.Equal(0, entorno.Entrega.Comprobaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-06")]
    public async Task Otro_rol_o_sin_sesion_se_rechazan_antes_de_reservar_el_limite()
    {
        foreach (var actor in new[] { ActorDePrueba.DeAdministradorCentral(), ActorDePrueba.DePersonalDeLogistica(), ActorDePrueba.Anonimo(), ActorDePrueba.SinContexto() })
        {
            var entorno = new EntornoDeBiblioteca(actor);

            var resultado = await entorno.Descarga.SolicitarEnlace(Guid.NewGuid(), Clave);

            Assert.False(resultado.EsExito);
            Assert.Empty(entorno.Limites.Reservas);
            Assert.Single(entorno.Denegaciones.Eventos);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("corta")]
    [InlineData("con espacios y mas de ocho")]
    [Trait("Criterio", "AC-BIB-06")]
    public async Task Una_clave_mal_formada_se_rechaza_sin_reservar_el_limite_ni_leer_nada(string clave)
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro");

        var resultado = await entorno.Descarga.SolicitarEnlace(acceso.Id, clave);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Empty(entorno.Limites.Reservas);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-16")]
    public async Task Un_acceso_revocado_o_suspendido_no_entrega_enlace()
    {
        var entorno = new EntornoDeBiblioteca();
        var revocado = entorno.SembrarLibro("Revocado");
        revocado.Revocar(revocado.PedidoId, CausaDeRevocacion.Reembolso, entorno.Reloj.Ahora);
        var suspendido = entorno.SembrarLibro("Suspendido");
        suspendido.Suspender(entorno.Reloj.Ahora);

        foreach (var acceso in new[] { revocado, suspendido })
        {
            var resultado = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

            Assert.Equal(ClavesDeBiblioteca.AccesoNoDisponible, Codigo(resultado));
            Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
        }

        Assert.Empty(entorno.Descargas.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-11")]
    public async Task Si_el_archivo_falta_en_el_almacen_no_se_consume_se_alerta_y_se_ofrece_reportarlo()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro", descargas: 1);
        entorno.Entrega.Poner($"libros/{acceso.EdicionId:N}.epub", ExistenciaDelArchivo.NoExiste);

        var resultado = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        Assert.Equal(ClavesDeBiblioteca.ArchivoNoDisponible, Codigo(resultado));
        Assert.Equal(1, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
        Assert.Empty(entorno.Descargas.Registros);
        Assert.Equal(ClavesDeBiblioteca.AlertaArchivoFaltante, Assert.Single(entorno.Alertas.Alertas).Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-11")]
    public async Task Un_plazo_agotado_del_almacen_es_servicio_no_disponible_sin_consumir_ni_alertar()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro");
        entorno.Entrega.PorOmision = ExistenciaDelArchivo.NoDisponible;

        var resultado = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(ClavesDeBiblioteca.ServicioNoDisponible, Codigo(resultado));
        Assert.Equal(0, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
        Assert.Empty(entorno.Alertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-11")]
    public async Task Una_edicion_sin_referencia_de_archivo_es_archivo_no_disponible_y_si_el_catalogo_cae_es_servicio_no_disponible()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.Accesos.Sembrar(entorno.Actor.Actual!.UsuarioId!.Value, Guid.NewGuid());

        Assert.Equal(ClavesDeBiblioteca.ArchivoNoDisponible, Codigo(await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave)));

        entorno.Referencias.Caida = true;
        Assert.Equal(ClavesDeBiblioteca.ServicioNoDisponible, Codigo(await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave)));
        Assert.Empty(entorno.Descargas.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-11")]
    public async Task Una_alerta_que_no_se_puede_publicar_no_cambia_la_respuesta()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro");
        entorno.Entrega.PorOmision = ExistenciaDelArchivo.NoExiste;
        entorno.Alertas.Falla = new InvalidOperationException("sin alertas");

        var resultado = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        Assert.Equal(ClavesDeBiblioteca.ArchivoNoDisponible, Codigo(resultado));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-04")]
    public async Task Ninguna_llamada_de_red_ocurre_dentro_de_una_transaccion_y_la_firma_es_local()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro");

        await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        Assert.Equal(1, entorno.Entrega.Comprobaciones);
        Assert.Equal(1, entorno.Entrega.Firmas);
        Assert.Equal(0, entorno.Entrega.LlamadasDentroDeLaTransaccion);
        Assert.Equal(1, entorno.Unidad.Ejecuciones);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-04")]
    public async Task Si_la_firma_falla_se_revierte_el_conteo_y_el_registro()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro");
        entorno.Entrega.FallaLaFirma = true;

        var resultado = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(0, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
        Assert.Empty(entorno.Descargas.Registros);
        Assert.Empty(entorno.Descargas.Claves);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-13")]
    public async Task El_limite_se_reserva_con_la_cuenta_y_una_clave_que_une_cuenta_acceso_y_clave()
    {
        var entorno = new EntornoDeBiblioteca();
        var a = entorno.SembrarLibro("Uno");
        var b = entorno.SembrarLibro("Dos");

        await entorno.Descarga.SolicitarEnlace(a.Id, Clave);
        await entorno.Descarga.SolicitarEnlace(b.Id, Clave);

        Assert.Equal(2, entorno.Limites.Reservas.Count);
        Assert.All(entorno.Limites.Reservas, r => Assert.Equal("enlaces-de-descarga-por-cuenta", r.Politica));
        Assert.All(entorno.Limites.Reservas, r => Assert.Equal(a.CuentaId.ToString("N"), r.Sujeto));
        Assert.NotEqual(entorno.Limites.Reservas[0].ClaveDelLlamador, entorno.Limites.Reservas[1].ClaveDelLlamador);
        Assert.DoesNotContain(Clave, entorno.Limites.Reservas[0].ClaveDelLlamador!, StringComparison.Ordinal);
        Assert.Equal(2, entorno.Limites.Usadas("enlaces-de-descarga-por-cuenta"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-13")]
    public async Task La_repeticion_de_la_misma_combinacion_no_cuenta_otra_vez_para_el_limite_pero_la_misma_clave_contra_otro_acceso_si()
    {
        var entorno = new EntornoDeBiblioteca();
        var a = entorno.SembrarLibro("Uno");
        var b = entorno.SembrarLibro("Dos");

        await entorno.Descarga.SolicitarEnlace(a.Id, Clave);
        await entorno.Descarga.SolicitarEnlace(a.Id, Clave);
        Assert.Equal(1, entorno.Limites.Usadas("enlaces-de-descarga-por-cuenta"));

        await entorno.Descarga.SolicitarEnlace(b.Id, Clave);
        Assert.Equal(2, entorno.Limites.Usadas("enlaces-de-descarga-por-cuenta"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-13")]
    public async Task El_enlace_once_en_una_hora_se_rechaza_con_la_respuesta_de_limite_sin_consumir_ni_leer()
    {
        var entorno = new EntornoDeBiblioteca();
        entorno.Limites.Maximos["enlaces-de-descarga-por-cuenta"] = 10;
        var acceso = entorno.SembrarLibro("Libro");
        for (var i = 0; i < 10; i++)
        {
            entorno.Limites.Reservas.Clear();
            var clave = $"clave-numero-{i:D4}-0000000000";
            await entorno.Descarga.SolicitarEnlace(acceso.Id, clave);
        }

        var usadas = entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas;
        var undecimo = await entorno.Descarga.SolicitarEnlace(acceso.Id, "clave-numero-0011-0000000000");

        Assert.Equal(CategoriaDeError.Limite, undecimo.Error!.Categoria);
        Assert.Equal(ClavesDeBiblioteca.LimiteExcedido, undecimo.Error.Clave);
        Assert.Contains(undecimo.Error.Errores, e => e.Campo == ClavesDeBiblioteca.EsperaEnSegundos);
        Assert.Contains(undecimo.Error.Errores, e => e.Campo == ClavesDeBiblioteca.Motivo);
        Assert.Contains(undecimo.Error.Errores, e => e.Campo == ClavesDeBiblioteca.DisponibleDesde);
        Assert.Equal(usadas, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-13")]
    public async Task Con_el_almacen_de_contadores_caido_la_descarga_falla_cerrada_y_una_reserva_en_curso_es_conflicto()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro");
        entorno.Limites.Caido = true;

        var caido = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        Assert.Equal(CategoriaDeError.NoDisponible, caido.Error!.Categoria);

        entorno.Limites.Caido = false;
        entorno.Limites.EnCurso = true;
        var enCurso = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);
        Assert.Equal(CategoriaDeError.Conflicto, enCurso.Error!.Categoria);
        Assert.Equal(ClavesDeBiblioteca.Conflicto, enCurso.Error.Clave);
        Assert.Equal(0, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
        Assert.Empty(entorno.Descargas.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-03")]
    public async Task Con_textos_legales_pendientes_la_descarga_se_rechaza_en_la_aplicacion()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro");
        entorno.Cuentas.TextosPendientes = true;

        var resultado = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        Assert.Equal(ClavesDeBiblioteca.TextosPendientes, Codigo(resultado));
        Assert.Equal(0, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-08")]
    public async Task Una_edicion_con_la_licencia_vencida_o_retirada_se_descarga_igual()
    {
        var entorno = new EntornoDeBiblioteca();
        var acceso = entorno.SembrarLibro("Libro vencido");
        entorno.Catalogo.Poner(acceso.EdicionId, FormatoDeEdicion.Digital, 25m, "Libro vencido", EstadoDeEdicionConsultado.RetiradaDeVenta, MotivoDeNoDisponibleDeEdicion.LicenciaVencida);

        var resultado = await entorno.Descarga.SolicitarEnlace(acceso.Id, Clave);

        Assert.True(resultado.EsExito);
        Assert.Equal(1, entorno.Accesos.Buscar(acceso.Id)!.DescargasUsadas);
    }

    [Theory]
    [InlineData("Cien años de soledad", FormatoDeArchivoDigital.Epub, "Cien años de soledad.epub")]
    [InlineData("Título \"con\" comillas/barra\\ y\r\ncontrol", FormatoDeArchivoDigital.Pdf, "Título con comillasbarra y control.pdf")]
    [InlineData("...", FormatoDeArchivoDigital.Epub, "libro.epub")]
    [InlineData(null, FormatoDeArchivoDigital.Pdf, "libro.pdf")]
    [Trait("Criterio", "AC-BIB-12")]
    public void El_nombre_de_archivo_se_sanea(string? titulo, FormatoDeArchivoDigital formato, string esperado)
    {
        Assert.Equal(esperado, NombreDeArchivoDeDescarga.Para(titulo, formato));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-12")]
    public void Un_titulo_larguisimo_se_acota()
    {
        var nombre = NombreDeArchivoDeDescarga.Para(new string('a', 500), FormatoDeArchivoDigital.Epub);

        Assert.Equal(80 + ".epub".Length, nombre.Length);
    }
}
