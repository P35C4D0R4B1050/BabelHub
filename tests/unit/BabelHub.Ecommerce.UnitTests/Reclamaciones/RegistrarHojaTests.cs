using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Reclamaciones;

/// <summary>Registrar una hoja: sin sesión, validación, verificación de persona, cupo del origen, número, constancia e idempotencia (T04; REC-02 a REC-05, REC-11, REC-18).</summary>
public sealed class RegistrarHojaTests
{
    private static EntornoDeReclamaciones Entorno() => new(ActorDePrueba.Anonimo());

    [Fact]
    [Trait("Criterio", "AC-REC-02")]
    public async Task Un_visitante_sin_sesion_registra_una_hoja_y_queda_registrada()
    {
        var e = new EntornoDeReclamaciones(ActorDePrueba.SinContexto());

        var resultado = await e.Reclamaciones.Registrar(e.Entrada());

        Assert.True(resultado.EsExito);
        var hoja = Assert.Single(e.Hojas.Todas);
        Assert.Equal("2026-000001", hoja.Numero.ToString());
        Assert.Equal("Ana Pérez Quispe", hoja.Datos.Nombre);
        Assert.Equal("ana.perez@correo.com", hoja.Datos.CorreoNormalizado);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-04")]
    public async Task La_constancia_trae_el_numero_la_fecha_y_el_plazo_de_15_dias_calendario()
    {
        var e = Entorno();

        var constancia = (await e.Reclamaciones.Registrar(e.Entrada())).Valor;

        Assert.Equal("2026-000001", constancia.Numero);
        Assert.Equal(e.Reloj.Ahora, constancia.FechaDeRegistro);
        Assert.Equal(15, constancia.PlazoDeRespuestaEnDias);
        Assert.Equal(new DateOnly(2026, 12, 3), constancia.FechaLimiteDeRespuesta);
        Assert.Equal("reclamo", constancia.Tipo);
        Assert.Equal(49.90m, constancia.MontoReclamado);
        Assert.Equal("Llegó con las hojas sueltas.", constancia.Detalle);
        Assert.Equal("Ana Pérez Quispe", constancia.Nombre);
        Assert.Equal("12345678", constancia.Documento);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-04")]
    public async Task El_numero_es_secuencial_por_anio_de_lima_y_reinicia_cada_anio()
    {
        var e = Entorno();
        e.Limites.Maximo = 10;

        var primero = (await e.Reclamaciones.Registrar(e.Entrada())).Valor.Numero;
        var segundo = (await e.Reclamaciones.Registrar(e.Entrada())).Valor.Numero;
        e.Reloj.Ahora = new DateTimeOffset(2027, 1, 1, 4, 30, 0, TimeSpan.Zero); // 31-dic-2026 23:30 en Lima
        var findeAnio = (await e.Reclamaciones.Registrar(e.Entrada())).Valor.Numero;
        e.Reloj.Ahora = new DateTimeOffset(2027, 1, 1, 5, 0, 0, TimeSpan.Zero); // 1-ene-2027 00:00 en Lima
        var nuevoAnio = (await e.Reclamaciones.Registrar(e.Entrada())).Valor.Numero;

        Assert.Equal(["2026-000001", "2026-000002", "2026-000003", "2027-000001"], [primero, segundo, findeAnio, nuevoAnio]);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-03")]
    public async Task Una_hoja_sin_detalle_documento_o_domicilio_se_rechaza_indicando_cada_campo_y_no_gasta_cupo()
    {
        var e = Entorno();

        var resultado = await e.Reclamaciones.Registrar(e.Entrada() with { Detalle = null, Documento = " ", Domicilio = null });

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal(
            new[] { "documento:falta", "domicilio:falta", "detalle:falta" },
            resultado.Error.Errores.Select(x => $"{x.Campo}:{x.Clave}").ToArray());
        Assert.Empty(e.Hojas.Todas);
        Assert.Empty(e.Limites.Reservadas);
        Assert.Equal(0, e.Limites.Usadas);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-03")]
    public async Task Los_campos_invalidos_se_listan_sin_repetir_lo_que_se_escribio()
    {
        var e = Entorno();

        var resultado = await e.Reclamaciones.Registrar(e.Entrada() with { Correo = "secreto@@x", Nombre = new string('N', 121), MontoReclamado = -5m, Tipo = "denuncia" });

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Equal(
            new[] { "nombre:invalido", "correo:invalido", "montoReclamado:invalido", "tipo:invalido" },
            resultado.Error.Errores.Select(x => $"{x.Campo}:{x.Clave}").ToArray());
        var texto = string.Join(' ', resultado.Error.Errores.Select(x => x.Campo + x.Clave));
        Assert.DoesNotContain("secreto", texto);
        Assert.DoesNotContain("NNNN", texto);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-03")]
    [InlineData("ana@correo.com, otra@correo.com")]
    [InlineData("ana@correo.com\r\nBcc: otra@correo.com")]
    [InlineData("Ana <ana@correo.com>")]
    public async Task Un_correo_con_varios_destinatarios_o_saltos_de_linea_se_rechaza(string correo)
    {
        var e = Entorno();

        var resultado = await e.Reclamaciones.Registrar(e.Entrada() with { Correo = correo });

        Assert.Equal(["correo:invalido"], resultado.Error!.Errores.Select(x => $"{x.Campo}:{x.Clave}").ToArray());
        Assert.Empty(e.Hojas.Todas);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-03")]
    public async Task Sin_la_clave_del_formulario_la_hoja_se_rechaza()
    {
        var e = Entorno();

        var resultado = await e.Reclamaciones.Registrar(e.Entrada() with { ClaveDelFormulario = "  " });

        Assert.Equal(["claveDelFormulario:falta"], resultado.Error!.Errores.Select(x => $"{x.Campo}:{x.Clave}").ToArray());
    }

    [Theory]
    [Trait("Criterio", "AC-REC-09")]
    [InlineData(ResultadoDeExigencia.Requerida)]
    [InlineData(ResultadoDeExigencia.Fallida)]
    public async Task Un_origen_automatizado_sin_verificacion_valida_recibe_403_y_no_se_valida_ni_se_gasta_cupo(ResultadoDeExigencia exigencia)
    {
        var e = Entorno();
        e.Verificacion.Resultado = exigencia;

        var resultado = await e.Reclamaciones.Registrar(e.Entrada() with { Detalle = null });

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal("verificacion-requerida", resultado.Error.Clave);
        Assert.Empty(e.Limites.Reservadas);
        Assert.Empty(e.Hojas.Todas);
        Assert.Equal((AccionProtegida.HojaDeReclamacion, (string?)null), Assert.Single(e.Verificacion.Llamadas));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-09")]
    public async Task Un_origen_automatizado_con_token_valido_registra_pero_la_verificacion_no_lo_exime_del_limite()
    {
        var e = Entorno();
        e.Verificacion.Resultado = ResultadoDeExigencia.Verificada;
        e.Limites.Usadas = 3;

        var excedido = await e.Reclamaciones.Registrar(e.Entrada() with { TokenDeVerificacion = "token-valido" });
        e.Limites.Usadas = 0;
        var permitido = await e.Reclamaciones.Registrar(e.Entrada() with { TokenDeVerificacion = "token-valido" });

        Assert.Equal(CategoriaDeError.Limite, excedido.Error!.Categoria);
        Assert.True(permitido.EsExito);
        Assert.All(e.Verificacion.Llamadas, l => Assert.Equal("token-valido", l.Token));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-09")]
    public async Task La_verificacion_de_persona_no_disponible_responde_servicio_no_disponible()
    {
        var e = Entorno();
        e.Verificacion.Resultado = ResultadoDeExigencia.NoDisponible;

        var resultado = await e.Reclamaciones.Registrar(e.Entrada());

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-09")]
    public async Task La_cuarta_hoja_del_origen_se_rechaza_con_su_espera_y_las_tres_anteriores_siguen()
    {
        var e = Entorno();
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await e.Reclamaciones.Registrar(e.Entrada())).EsExito);
        }

        var cuarta = await e.Reclamaciones.Registrar(e.Entrada());

        Assert.Equal(CategoriaDeError.Limite, cuarta.Error!.Categoria);
        Assert.Equal("limite-excedido", cuarta.Error.Clave);
        Assert.Equal("espera-en-segundos:1800", $"{cuarta.Error.Errores.Single().Campo}:{cuarta.Error.Errores.Single().Clave}");
        Assert.Equal(3, e.Hojas.Todas.Count);
        Assert.Equal(new[] { "2026-000001", "2026-000002", "2026-000003" }, e.Hojas.Todas.Select(h => h.Numero.ToString()).Order());
    }

    [Fact]
    [Trait("Criterio", "AC-REC-09")]
    public async Task El_cupo_se_reserva_con_la_clave_del_formulario_y_se_completa_al_registrar()
    {
        var e = Entorno();

        await e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario"));

        var clave = Assert.Single(e.Limites.Reservadas);
        Assert.NotEqual("clave-uno-del-formulario", clave.ClaveDelCliente);
        Assert.DoesNotContain("clave-uno", clave.ClaveDelCliente);
        Assert.Equal("/reclamaciones/nueva", clave.Ruta);
        Assert.Single(e.Limites.Completadas);
        Assert.Empty(e.Limites.Falladas);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-02")]
    public async Task Reenviar_con_la_misma_clave_devuelve_la_misma_constancia_sin_crear_otra_hoja_ni_contar_otra_vez()
    {
        var e = Entorno();
        var primera = (await e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario"))).Valor;

        var repetida = await e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario"));

        Assert.True(repetida.EsExito);
        Assert.Equal(primera, repetida.Valor);
        Assert.Single(e.Hojas.Todas);
        Assert.Equal(1, e.Limites.Usadas);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-02")]
    public async Task La_misma_clave_con_otro_contenido_es_un_conflicto_y_no_devuelve_datos_de_la_hoja_original()
    {
        var e = Entorno();
        await e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario"));

        var otra = await e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario", detalle: "Otro detalle distinto."));

        Assert.Equal(CategoriaDeError.Conflicto, otra.Error!.Categoria);
        Assert.Single(e.Hojas.Todas);
        Assert.Empty(otra.Error.Errores);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-02")]
    [InlineData(EstadoDeLimite.RepeticionEnCurso)]
    [InlineData(EstadoDeLimite.ClaveEnConflicto)]
    public async Task Un_primer_envio_en_curso_o_una_clave_en_conflicto_responden_conflicto_sin_crear_ni_contar(EstadoDeLimite estado)
    {
        var e = Entorno();
        e.Limites.Forzado = estado == EstadoDeLimite.RepeticionEnCurso ? ResultadoDeLimite.RepeticionEnCurso() : ResultadoDeLimite.ClaveEnConflicto();

        var repetida = await e.Reclamaciones.Registrar(e.Entrada());

        Assert.Equal(CategoriaDeError.Conflicto, repetida.Error!.Categoria);
        Assert.Equal("rec-conflicto", repetida.Error.Clave);
        Assert.Empty(e.Hojas.Todas);
        Assert.Equal(0, e.Hojas.Operaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-02")]
    public async Task La_misma_clave_con_un_contenido_que_solo_cambia_en_mayusculas_es_un_conflicto_porque_las_huellas_son_exactas()
    {
        var e = Entorno();
        await e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario", detalle: "Llegó con las hojas sueltas."));

        var otra = await e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario", detalle: "LLEGÓ CON LAS HOJAS SUELTAS."));

        Assert.Equal(CategoriaDeError.Conflicto, otra.Error!.Categoria);
        Assert.Single(e.Hojas.Todas);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-02")]
    public async Task Dos_claves_que_solo_difieren_en_mayusculas_son_hojas_distintas()
    {
        var e = Entorno();

        var uno = await e.Reclamaciones.Registrar(e.Entrada("Clave-del-formulario-0000001"));
        var dos = await e.Reclamaciones.Registrar(e.Entrada("clave-del-formulario-0000001"));

        Assert.NotEqual(uno.Valor.Numero, dos.Valor.Numero);
        Assert.Equal(2, e.Hojas.Todas.Count);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-03")]
    [InlineData("corta")]
    [InlineData("clave con espacios del formulario")]
    [InlineData("clave-con-ñ-del-formulario-uno")]
    public async Task La_clave_del_formulario_debe_ser_de_al_menos_22_caracteres_base64url(string clave)
    {
        var e = Entorno();

        var resultado = await e.Reclamaciones.Registrar(e.Entrada(clave));

        Assert.Equal(["claveDelFormulario:invalido"], resultado.Error!.Errores.Select(x => $"{x.Campo}:{x.Clave}").ToArray());
        Assert.Empty(e.Limites.Reservadas);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-03")]
    [InlineData("nombre", "Ana\0Pérez")]
    [InlineData("nombre", "Ana\u001fPérez")]
    [InlineData("nombre", "Ana\r\nPérez")]
    [InlineData("nombre", "Ana\u202EPérez")]
    [InlineData("nombre", "Ana[SUSTITUTO]Pérez")]
    [InlineData("domicilio", "Calle 1\u2028Lima")]
    [InlineData("bienContratado", "Libro\u0085")]
    [InlineData("pedidoReferencia", "PED\t1")]
    [InlineData("detalle", "Detalle con NUL \0 oculto")]
    [InlineData("detalle", "Detalle con bidi \u2067 oculto")]
    public async Task El_texto_con_controles_sustitutos_sin_par_o_bidireccionales_se_rechaza_como_invalido(string campo, string valor)
    {
        var e = Entorno();
        valor = valor.Replace("[SUSTITUTO]", ((char)0xD800).ToString(), StringComparison.Ordinal);
        var entrada = campo switch
        {
            "nombre" => e.Entrada() with { Nombre = valor },
            "domicilio" => e.Entrada() with { Domicilio = valor },
            "bienContratado" => e.Entrada() with { BienContratado = valor },
            "pedidoReferencia" => e.Entrada() with { PedidoReferencia = valor },
            _ => e.Entrada() with { Detalle = valor },
        };

        var resultado = await e.Reclamaciones.Registrar(entrada);

        Assert.Equal([$"{campo}:invalido"], resultado.Error!.Errores.Select(x => $"{x.Campo}:{x.Clave}").ToArray());
        Assert.Empty(e.Hojas.Todas);
        Assert.Empty(e.Limites.Reservadas);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-03")]
    public async Task El_detalle_admite_saltos_de_linea_y_un_script_es_solo_texto_que_se_guarda_tal_cual()
    {
        var e = Entorno();

        var resultado = await e.Reclamaciones.Registrar(e.Entrada(detalle: "Línea uno.\r\nLínea dos.\t<script>alert(1)</script>"));

        Assert.True(resultado.EsExito);
        Assert.Equal("Línea uno.\r\nLínea dos.\t<script>alert(1)</script>", e.Hojas.Todas.Single().Detalle);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-09")]
    public async Task Un_fallo_de_la_base_pierde_el_cupo_ya_contado_porque_solo_la_rama_de_la_auditoria_lo_devuelve_pero_suelta_la_clave()
    {
        var e = Entorno();
        e.Hojas.FallaAlAgregar = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario")));

        Assert.Equal(1, e.Limites.Usadas);
        Assert.Single(e.Limites.Falladas);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-02")]
    public async Task Si_la_lectura_de_la_constancia_ya_creada_no_responde_se_dice_servicio_no_disponible()
    {
        var e = Entorno();
        await e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario"));
        e.Hojas.BuscarNoDisponible = true;

        var repetida = await e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario"));

        Assert.Equal(CategoriaDeError.NoDisponible, repetida.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-02")]
    public async Task La_entrada_y_la_constancia_no_se_serializan_con_datos_personales()
    {
        var e = Entorno();
        var entrada = e.Entrada("clave-uno-del-formulario") with { TokenDeVerificacion = "token-de-verificacion" };
        var constancia = (await e.Reclamaciones.Registrar(entrada)).Valor;

        var json = System.Text.Json.JsonSerializer.Serialize(entrada) + System.Text.Json.JsonSerializer.Serialize(constancia);

        foreach (var secreto in new[] { "Ana", "12345678", "Olivos", "correo.com", "hojas sueltas", "token-de-verificacion", "clave-uno" })
        {
            Assert.DoesNotContain(secreto, json, StringComparison.OrdinalIgnoreCase);
        }
    }


    [Fact]
    [Trait("Criterio", "AC-REC-02")]
    public async Task Dos_envios_identicos_con_claves_distintas_generan_hojas_separadas()
    {
        var e = Entorno();

        var uno = await e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario"));
        var dos = await e.Reclamaciones.Registrar(e.Entrada("clave-dos-del-formulario"));

        Assert.NotEqual(uno.Valor.Numero, dos.Valor.Numero);
        Assert.Equal(2, e.Hojas.Todas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-02")]
    public async Task Si_el_almacen_de_contadores_pierde_la_clave_el_indice_unico_devuelve_la_constancia_ya_creada_sin_hueco_en_la_numeracion()
    {
        var e = Entorno();
        var primera = (await e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario"))).Valor;
        e.Limites.IgnoraLasClaves = true;
        e.Limites.Usadas = 0;

        var carrera = await e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario"));
        var siguiente = await e.Reclamaciones.Registrar(e.Entrada("clave-dos-del-formulario"));

        Assert.True(carrera.EsExito);
        Assert.Equal(primera.Numero, carrera.Valor.Numero);
        Assert.Equal("2026-000002", siguiente.Valor.Numero);
        Assert.Equal(2, e.Hojas.Todas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-02")]
    public async Task La_carrera_de_la_misma_clave_con_otro_contenido_es_un_conflicto_y_devuelve_el_cupo()
    {
        var e = Entorno();
        await e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario"));
        e.Limites.IgnoraLasClaves = true;

        var carrera = await e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario", detalle: "Otro detalle distinto."));

        Assert.Equal(CategoriaDeError.Conflicto, carrera.Error!.Categoria);
        Assert.Single(e.Hojas.Todas);
        Assert.Single(e.Limites.Falladas);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-04")]
    public async Task Un_fallo_de_la_base_revierte_el_numero_devuelve_el_cupo_y_la_clave_puede_reintentarse()
    {
        var e = Entorno();
        e.Hojas.FallaAlAgregar = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario")));
        e.Hojas.FallaAlAgregar = false;
        var reintento = await e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario"));

        Assert.Single(e.Limites.Falladas);
        Assert.Equal("2026-000001", reintento.Valor.Numero);
        Assert.Single(e.Hojas.Todas);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-04")]
    public async Task Un_lock_timeout_de_la_secuencia_responde_servicio_no_disponible_sin_dejar_la_clave_en_curso()
    {
        var e = Entorno();
        e.Hojas.SecuenciaNoDisponible = true;

        var resultado = await e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario"));

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Single(e.Limites.Falladas);
        e.Hojas.SecuenciaNoDisponible = false;
        Assert.True((await e.Reclamaciones.Registrar(e.Entrada("clave-uno-del-formulario"))).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-09")]
    public async Task Con_el_almacen_de_contadores_caido_responde_servicio_no_disponible()
    {
        var e = Entorno();
        e.Limites.Caido = true;

        var resultado = await e.Reclamaciones.Registrar(e.Entrada());

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Empty(e.Hojas.Todas);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-04")]
    public async Task El_registro_no_se_audita_y_no_llama_a_identidad_ni_al_correo()
    {
        var e = Entorno();

        await e.Reclamaciones.Registrar(e.Entrada());

        Assert.Empty(e.Auditoria.Solicitudes);
        Assert.Empty(e.Correo.Correos);
        Assert.Empty(e.Denegaciones.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-04")]
    public async Task La_hoja_guarda_resumenes_de_la_clave_y_del_contenido_nunca_el_texto_de_la_clave()
    {
        var e = Entorno();

        await e.Reclamaciones.Registrar(e.Entrada("clave-secreta-del-formulario"));

        var hoja = Assert.Single(e.Hojas.Todas);
        Assert.NotEqual("clave-secreta-del-formulario", hoja.ClaveHash);
        Assert.DoesNotContain("secreta", hoja.ClaveHash);
        Assert.False(string.IsNullOrWhiteSpace(hoja.ContenidoHash));
        Assert.DoesNotContain("12345678", hoja.ContenidoHash);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-02")]
    public async Task El_texto_de_la_entrada_y_de_la_constancia_no_muestra_datos_personales()
    {
        var e = Entorno();
        var entrada = e.Entrada();
        var constancia = (await e.Reclamaciones.Registrar(entrada)).Valor;

        var texto = entrada.ToString() + " " + constancia;

        Assert.DoesNotContain("Ana", texto);
        Assert.DoesNotContain("12345678", texto);
        Assert.DoesNotContain("Olivos", texto);
        Assert.DoesNotContain("correo.com", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hojas sueltas", texto);
    }
}
