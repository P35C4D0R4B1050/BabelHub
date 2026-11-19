using BabelHub.Ecommerce.Domain.Reclamaciones;

namespace BabelHub.Ecommerce.UnitTests.Reclamaciones;

/// <summary>La hoja de reclamación, su número, su plazo y sus transiciones (T01; REC-05, REC-12, REC-16, REC-20).</summary>
public sealed class HojaDeReclamacionTests
{
    // 2026-11-18 10:00 en Lima (UTC−5).
    private static readonly DateTimeOffset Registro = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    internal static HojaDeReclamacion Nueva(DateTimeOffset? registro = null, TipoDeHoja tipo = TipoDeHoja.Reclamo) =>
        HojaDeReclamacion.Registrar(
            Guid.NewGuid(),
            NumeroDeHoja.Crear(2026, 1),
            tipo,
            new DatosDelConsumidor("Ana Pérez Quispe", "12345678", "Av. Los Olivos 123, Ayacucho", "Ana.Perez@Correo.COM"),
            "Libro «La ciudad y los perros»",
            49.90m,
            "Llegó con las hojas sueltas.",
            "PED-2026-0007",
            "contenido-hash-1",
            "clave-hash-1",
            registro ?? Registro);

    [Fact]
    [Trait("Criterio", "AC-REC-04")]
    public void El_numero_es_el_anio_y_una_secuencia_de_seis_digitos()
    {
        Assert.Equal("2026-000001", NumeroDeHoja.Crear(2026, 1).ToString());
        Assert.Equal("2026-123456", NumeroDeHoja.Crear(2026, 123_456).ToString());
        Assert.Equal("2027-999999", NumeroDeHoja.Crear(2027, 999_999).ToString());
    }

    [Theory]
    [Trait("Criterio", "AC-REC-04")]
    [InlineData(1999, 1)]
    [InlineData(2026, 0)]
    [InlineData(2026, 1_000_000)]
    [InlineData(10_000, 1)]
    public void Un_numero_fuera_de_rango_se_rechaza(int anio, int secuencia)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NumeroDeHoja.Crear(anio, secuencia));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-04")]
    public void El_numero_se_lee_de_un_texto_con_su_formato_exacto()
    {
        Assert.True(NumeroDeHoja.TryParse("2026-000042", out var numero));
        Assert.Equal(2026, numero.Anio);
        Assert.Equal(42, numero.Secuencia);
        Assert.Equal("2026-000042", numero.ToString());
    }

    [Theory]
    [Trait("Criterio", "AC-REC-08")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-42")]
    [InlineData("26-000042")]
    [InlineData("2026-0000042")]
    [InlineData("2026_000042")]
    [InlineData("2026-00004A")]
    [InlineData(" 2026-000042")]
    [InlineData("2026-000042\n")]
    [InlineData("2026-000000")]
    public void Un_texto_que_no_es_un_numero_de_hoja_se_rechaza(string? texto)
    {
        Assert.False(NumeroDeHoja.TryParse(texto, out _));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-04")]
    public void El_anio_del_numero_es_el_de_lima_aunque_en_utc_ya_sea_otro()
    {
        // 31 de diciembre 23:30 en Lima ya es 1 de enero en UTC.
        var finDeAnio = new DateTimeOffset(2027, 1, 1, 4, 30, 0, TimeSpan.Zero);
        var inicioDeAnio = new DateTimeOffset(2027, 1, 1, 5, 0, 0, TimeSpan.Zero);

        Assert.Equal(2026, DiaDeLima.Anio(finDeAnio));
        Assert.Equal(2027, DiaDeLima.Anio(inicioDeAnio));
        Assert.Equal(new DateOnly(2026, 12, 31), DiaDeLima.De(finDeAnio));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-04")]
    public void El_plazo_de_respuesta_es_de_15_dias_calendario_desde_el_dia_de_lima_del_registro()
    {
        Assert.Equal(15, PlazoDeRespuesta.Dias);
        Assert.Equal(new DateOnly(2026, 12, 3), PlazoDeRespuesta.FechaLimite(Registro));
        // 22:00 en Lima del 18 ya es el 19 en UTC: el día que cuenta es el de Lima.
        Assert.Equal(new DateOnly(2026, 12, 3), PlazoDeRespuesta.FechaLimite(new DateTimeOffset(2026, 11, 19, 3, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2027, 1, 14), PlazoDeRespuesta.FechaLimite(new DateTimeOffset(2026, 12, 30, 15, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-04")]
    public void Una_hoja_nueva_queda_pendiente_con_su_numero_su_fecha_y_su_plazo()
    {
        var hoja = Nueva();

        Assert.Equal(EstadoDeHoja.Pendiente, hoja.Estado);
        Assert.Equal("2026-000001", hoja.Numero.ToString());
        Assert.Equal(Registro, hoja.FechaRegistro);
        Assert.Equal(new DateOnly(2026, 12, 3), hoja.FechaLimiteDeRespuesta);
        Assert.Equal(TipoDeHoja.Reclamo, hoja.Tipo);
        Assert.Equal(49.90m, hoja.MontoReclamado);
        Assert.Equal("ana.perez@correo.com", hoja.Datos.CorreoNormalizado);
        Assert.Null(hoja.Respuesta);
        Assert.Null(hoja.AvisoDeRespuesta);
        Assert.False(hoja.DeCuentaCancelada);
        Assert.False(hoja.EstaAnonimizada);
        Assert.True(hoja.EstaPendiente);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-04")]
    public void El_texto_de_una_hoja_no_muestra_datos_personales_ni_el_detalle()
    {
        var texto = Nueva().ToString() + " " + Nueva().Datos;

        Assert.DoesNotContain("Ana", texto);
        Assert.DoesNotContain("12345678", texto);
        Assert.DoesNotContain("Olivos", texto);
        Assert.DoesNotContain("hojas sueltas", texto);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-06")]
    public void Responder_una_hoja_pendiente_guarda_la_respuesta_y_deja_el_aviso_pendiente_con_su_arrendamiento()
    {
        var hoja = Nueva();
        var ahora = Registro.AddDays(2);

        Assert.Equal(ResultadoDeTransicion.Hecha, hoja.Responder("Le reponemos el libro.", Admin, ahora));

        Assert.Equal(EstadoDeHoja.Respondido, hoja.Estado);
        Assert.Equal("Le reponemos el libro.", hoja.Respuesta);
        Assert.Equal(ahora, hoja.RespondidaEn);
        Assert.Equal(Admin, hoja.RespondidaPor);
        Assert.Equal(AvisoDeRespuesta.Pendiente, hoja.AvisoDeRespuesta);
        Assert.Equal(ahora.AddSeconds(60), hoja.AvisoEnCursoHasta);
        Assert.Null(hoja.AvisoEnviadoEn);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-14")]
    public void Una_hoja_respondida_o_descartada_no_se_vuelve_a_responder_y_la_respuesta_no_cambia()
    {
        var respondida = Nueva();
        respondida.Responder("Primera.", Admin, Registro.AddDays(1));
        var descartada = Nueva();
        descartada.Descartar(Registro.AddDays(1));

        Assert.Equal(ResultadoDeTransicion.EstadoInvalido, respondida.Responder("Segunda.", Admin, Registro.AddDays(2)));
        Assert.Equal(ResultadoDeTransicion.EstadoInvalido, descartada.Responder("Respuesta.", Admin, Registro.AddDays(2)));

        Assert.Equal("Primera.", respondida.Respuesta);
        Assert.Equal(EstadoDeHoja.Descartado, descartada.Estado);
        Assert.Null(descartada.Respuesta);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-10")]
    public void Descartar_solo_una_pendiente_marca_la_fecha_y_no_la_elimina()
    {
        var hoja = Nueva();

        Assert.Equal(ResultadoDeTransicion.Hecha, hoja.Descartar(Registro.AddDays(1)));

        Assert.Equal(EstadoDeHoja.Descartado, hoja.Estado);
        Assert.Equal(Registro.AddDays(1), hoja.DescartadaEn);
        Assert.Equal("2026-000001", hoja.Numero.ToString());

        var respondida = Nueva();
        respondida.Responder("Hecho.", Admin, Registro.AddDays(1));
        Assert.Equal(ResultadoDeTransicion.EstadoInvalido, respondida.Descartar(Registro.AddDays(2)));
        Assert.Equal(ResultadoDeTransicion.EstadoInvalido, hoja.Descartar(Registro.AddDays(3)));
        Assert.Equal(Registro.AddDays(1), hoja.DescartadaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public void Una_hoja_anonimizada_no_se_responde_ni_se_descarta()
    {
        var hoja = HojaAnonimizada();

        Assert.True(hoja.EstaAnonimizada);
        Assert.Equal(ResultadoDeTransicion.EstadoInvalido, hoja.Responder("Tarde.", Admin, Registro.AddYears(2).AddDays(1)));
        Assert.Equal(ResultadoDeTransicion.EstadoInvalido, hoja.Descartar(Registro.AddYears(2).AddDays(1)));
        Assert.Equal(EstadoDeHoja.Pendiente, hoja.Estado);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-06")]
    [InlineData("")]
    [InlineData("   ")]
    public void Una_respuesta_vacia_se_rechaza_y_la_hoja_sigue_pendiente(string respuesta)
    {
        var hoja = Nueva();

        Assert.Throws<ArgumentException>(() => hoja.Responder(respuesta, Admin, Registro.AddDays(1)));
        Assert.Throws<ArgumentException>(() => hoja.Responder(new string('x', HojaDeReclamacion.LongitudMaximaDeLaRespuesta + 1), Admin, Registro.AddDays(1)));
        Assert.Throws<ArgumentException>(() => hoja.Responder("ok", Guid.Empty, Registro.AddDays(1)));
        Assert.Equal(EstadoDeHoja.Pendiente, hoja.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-11")]
    public void El_reintento_del_aviso_toma_un_arrendamiento_de_60_segundos_y_un_segundo_reintento_no_envia_otro()
    {
        var hoja = Nueva();
        var respondida = Registro.AddDays(1);
        hoja.Responder("Hecho.", Admin, respondida);

        // Justo tras responder, el primer envío tiene el arrendamiento.
        Assert.Equal(ResultadoDeReintento.EnCurso, hoja.TomarArrendamientoDeAviso(respondida.AddSeconds(30)));
        Assert.Equal(ResultadoDeReintento.Tomado, hoja.TomarArrendamientoDeAviso(respondida.AddSeconds(61)));
        Assert.Equal(respondida.AddSeconds(61).AddSeconds(60), hoja.AvisoEnCursoHasta);
        Assert.Equal(ResultadoDeReintento.EnCurso, hoja.TomarArrendamientoDeAviso(respondida.AddSeconds(90)));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-11")]
    public void Marcar_el_aviso_enviado_cierra_el_pendiente_y_un_reintento_posterior_ya_no_aplica()
    {
        var hoja = Nueva();
        var respondida = Registro.AddDays(1);
        hoja.Responder("Hecho.", Admin, respondida);

        hoja.MarcarAvisoEnviado(respondida.AddSeconds(5));

        Assert.Equal(AvisoDeRespuesta.Enviado, hoja.AvisoDeRespuesta);
        Assert.Equal(respondida.AddSeconds(5), hoja.AvisoEnviadoEn);
        Assert.Null(hoja.AvisoEnCursoHasta);
        Assert.Equal(ResultadoDeReintento.YaEnviado, hoja.TomarArrendamientoDeAviso(respondida.AddMinutes(10)));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-11")]
    public void Un_envio_fallido_libera_el_arrendamiento_y_deja_el_aviso_pendiente_para_reintentar()
    {
        var hoja = Nueva();
        var respondida = Registro.AddDays(1);
        hoja.Responder("Hecho.", Admin, respondida);

        hoja.LiberarArrendamientoDeAviso();

        Assert.Equal(AvisoDeRespuesta.Pendiente, hoja.AvisoDeRespuesta);
        Assert.Null(hoja.AvisoEnCursoHasta);
        Assert.Equal(ResultadoDeReintento.Tomado, hoja.TomarArrendamientoDeAviso(respondida.AddSeconds(5)));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-11")]
    public void El_reintento_solo_aplica_a_una_hoja_respondida_y_sin_anonimizar()
    {
        var pendiente = Nueva();
        var descartada = Nueva();
        descartada.Descartar(Registro.AddDays(1));

        Assert.Equal(ResultadoDeReintento.NoRespondida, pendiente.TomarArrendamientoDeAviso(Registro.AddDays(1)));
        Assert.Equal(ResultadoDeReintento.NoRespondida, descartada.TomarArrendamientoDeAviso(Registro.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => pendiente.MarcarAvisoEnviado(Registro.AddDays(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-18")]
    public void Marcar_una_hoja_de_una_cuenta_cancelada_es_idempotente_y_no_altera_nada_mas()
    {
        var hoja = Nueva();

        Assert.True(hoja.MarcarDeCuentaCancelada());
        Assert.False(hoja.MarcarDeCuentaCancelada());

        Assert.True(hoja.DeCuentaCancelada);
        Assert.Equal(EstadoDeHoja.Pendiente, hoja.Estado);
        Assert.Equal("Llegó con las hojas sueltas.", hoja.Detalle);
        Assert.Equal("Ana Pérez Quispe", hoja.Datos.Nombre);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-18")]
    public void Una_hoja_de_una_cuenta_cancelada_sigue_pudiendose_responder_y_descartar()
    {
        var respondible = Nueva();
        respondible.MarcarDeCuentaCancelada();
        var descartable = Nueva();
        descartable.MarcarDeCuentaCancelada();

        Assert.Equal(ResultadoDeTransicion.Hecha, respondible.Responder("Respuesta.", Admin, Registro.AddDays(1)));
        Assert.Equal(ResultadoDeTransicion.Hecha, descartable.Descartar(Registro.AddDays(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-04")]
    public void Una_hoja_necesita_su_identificador_la_clave_y_el_resumen_del_contenido()
    {
        var datos = new DatosDelConsumidor("Ana", "1", "Calle 1", "a@b.co");

        Assert.Throws<ArgumentException>(() => HojaDeReclamacion.Registrar(Guid.Empty, NumeroDeHoja.Crear(2026, 1), TipoDeHoja.Queja, datos, "Bien", 0m, "Detalle", null, "h", "c", Registro));
        Assert.Throws<ArgumentException>(() => HojaDeReclamacion.Registrar(Guid.NewGuid(), NumeroDeHoja.Crear(2026, 1), TipoDeHoja.Queja, datos, "Bien", 0m, "Detalle", null, "", "c", Registro));
        Assert.Throws<ArgumentException>(() => HojaDeReclamacion.Registrar(Guid.NewGuid(), NumeroDeHoja.Crear(2026, 1), TipoDeHoja.Queja, datos, "Bien", 0m, "Detalle", null, "h", " ", Registro));
        Assert.Throws<ArgumentOutOfRangeException>(() => HojaDeReclamacion.Registrar(Guid.NewGuid(), NumeroDeHoja.Crear(2026, 1), TipoDeHoja.Queja, datos, "Bien", -0.01m, "Detalle", null, "h", "c", Registro));
        Assert.Throws<ArgumentOutOfRangeException>(() => HojaDeReclamacion.Registrar(Guid.NewGuid(), NumeroDeHoja.Crear(2026, 1), TipoDeHoja.Queja, datos, "Bien", 1.001m, "Detalle", null, "h", "c", Registro));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-04")]
    public void El_numero_de_una_hoja_debe_ser_del_anio_de_lima_de_su_registro()
    {
        var datos = new DatosDelConsumidor("Ana", "1", "Calle 1", "a@b.co");

        Assert.Throws<ArgumentException>(() => HojaDeReclamacion.Registrar(Guid.NewGuid(), NumeroDeHoja.Crear(2025, 1), TipoDeHoja.Queja, datos, "Bien", 0m, "Detalle", null, "h", "c", Registro));
    }

    private static HojaDeReclamacion HojaAnonimizada() =>
        HojaDeReclamacion.Rehidratar(
            Guid.NewGuid(),
            NumeroDeHoja.Crear(2026, 1),
            TipoDeHoja.Reclamo,
            DatosDelConsumidor.Anonimizados(),
            "Libro",
            10m,
            HojaDeReclamacion.TextoRedactado,
            null,
            null,
            Registro,
            "clave",
            EstadoDeHoja.Pendiente,
            respuesta: null,
            respondidaEn: null,
            respondidaPor: null,
            avisoDeRespuesta: null,
            avisoEnviadoEn: null,
            avisoEnCursoHasta: null,
            descartadaEn: null,
            deCuentaCancelada: false,
            anonimizadaEn: Registro.AddYears(2));
}
