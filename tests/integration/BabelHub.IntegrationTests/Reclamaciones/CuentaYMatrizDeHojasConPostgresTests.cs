using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using RolesDeUsuario = BabelHub.Shared.Contratos.RolesDeUsuario;

namespace BabelHub.IntegrationTests.Reclamaciones;

/// <summary>
/// T20 y T21 de <c>ecommerce-libro-reclamaciones</c> con los casos de uso reales sobre PostgreSQL real (AC-REC-05, AC-REC-08, AC-REC-12, AC-REC-13, AC-REC-15, AC-REC-16, AC-REC-17, AC-REC-18; REC-19, REC-20): el recuento de pendientes de tres
/// consumidores (con correo verificado, con otro correo y sin verificar), la cancelación de la cuenta que marca las hojas de su correo verificado sin tocar ningún dato, la administración que sigue viendo la hoja, el mismo correo en una cuenta
/// nueva que no la ve, la anonimización posterior, la falla forzada que no deja nada a medias y la matriz de cada operación de la administración por rol (rechazo uniforme con un evento de denegación por cada rechazo de una sesión y
/// <c>NoAutenticado</c> sin evento para el visitante). Solo el correo verificado de la cuenta (Identidad), el actor, los límites, el correo y la auditoría son dobles.
/// </summary>
[Collection(ColeccionPostgresDeReclamaciones.Nombre)]
public sealed class CuentaYMatrizDeHojasConPostgresTests(PostgresDeReclamacionesFixture bd) : IAsyncLifetime
{
    private static readonly Guid CuentaDeAna = Guid.Parse("c0000000-0000-0000-0000-00000000a001");

    private static readonly Guid CuentaDeLuis = Guid.Parse("c0000000-0000-0000-0000-00000000a002");

    private static readonly Guid CuentaSinVerificar = Guid.Parse("c0000000-0000-0000-0000-00000000a003");

    private static readonly Guid CuentaNuevaConElCorreoDeAna = Guid.Parse("c0000000-0000-0000-0000-00000000a004");

    private const string CorreoDeAna = "ana@example.com";

    private const string CorreoDeLuis = "luis@example.com";

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private EntornoDeReclamacionesConPostgres Entorno() => new(bd);

    private static CorreosDeLasCuentas Correos() => new()
    {
        [CuentaDeAna] = CorreoDeAna,
        [CuentaDeLuis] = CorreoDeLuis,
        [CuentaSinVerificar] = null,
        [CuentaNuevaConElCorreoDeAna] = CorreoDeAna,
    };

    /// <summary>Registra una hoja para un correo y devuelve su identificador.</summary>
    private async Task<Guid> Registrar(EntornoDeReclamacionesConPostgres e, string correo)
    {
        e.Limites.Maximo = 1_000;
        var resultado = await e.Reclamaciones.Registrar(e.Entrada() with { Correo = correo });
        Assert.True(resultado.EsExito);
        return await bd.Consultar<Guid>($"SELECT id FROM reclamaciones.hojas_reclamacion WHERE numero = '{resultado.Valor.Numero}'");
    }

    /// <summary>Las pendientes de un correo de contacto que no son de una cuenta cancelada, leídas como administrador de la prueba (la base, sin pasar por el módulo).</summary>
    private Task<long> PendientesDelCorreo(string correo) =>
        bd.Consultar<long>($"SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE correo_normalizado = '{correo}' AND estado = 'pendiente' AND NOT de_cuenta_cancelada");

    private static ActorActual Usuario(string rol) => ActorActual.DeUsuario(Guid.NewGuid(), rol);

    // ---------- Pendientes y cancelación de la cuenta (T20) ----------

    [Fact]
    [Trait("Criterio", "AC-REC-17")]
    public async Task El_recuento_de_pendientes_cuenta_solo_las_pendientes_del_correo_verificado_de_cada_consumidor()
    {
        var e = Entorno();
        var pendienteA1 = await Registrar(e, CorreoDeAna);
        var pendienteA2 = await Registrar(e, CorreoDeAna);
        var respondida = await Registrar(e, CorreoDeAna);
        var descartada = await Registrar(e, CorreoDeAna);
        await Registrar(e, CorreoDeLuis);
        e.Actor.ComoAdministracion();
        Assert.True((await e.Administracion.Responder(new ResponderHojaEntrada(respondida, "Gracias por escribirnos."))).EsExito);
        Assert.True((await e.Administracion.Descartar(descartada)).EsExito);
        var contar = new ContarHojasPendientesDeUnCorreo(Correos(), e.Vinculador, e.Vinculos);

        // Una cuenta sin correo verificado no tiene hojas que contar: un correo sin verificar podría ser de otra persona.
        var (ana, luis, sinVerificar) = await bd.Unidad.Ejecutar(async token => (await contar.Contar(CuentaDeAna, token), await contar.Contar(CuentaDeLuis, token), await contar.Contar(CuentaSinVerificar, token)));

        Assert.Equal(2, ana);
        Assert.Equal(1, luis);
        Assert.Equal(0, sinVerificar);
        Assert.NotEqual(pendienteA1, pendienteA2);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-17")]
    public async Task Si_no_se_puede_leer_el_correo_el_recuento_y_la_cancelacion_fallan_cerrados_y_no_asumen_cero()
    {
        var e = Entorno();
        await Registrar(e, CorreoDeAna);
        var caido = new CorreosDeLasCuentas { Falla = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => bd.Unidad.Ejecutar(token => new ContarHojasPendientesDeUnCorreo(caido, e.Vinculador, e.Vinculos).Contar(CuentaDeAna, token)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bd.Unidad.Ejecutar<int>(async token =>
        {
            await new MarcarHojasDeUnaCuentaCancelada(caido, e.Vinculos).Aplicar(CuentaDeAna, Guid.NewGuid(), token);
            return 0;
        }));

        Assert.Equal(1L, await PendientesDelCorreo(CorreoDeAna));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-05")]
    [Trait("Criterio", "AC-REC-18")]
    public async Task Cancelar_la_cuenta_marca_las_hojas_de_su_correo_sin_tocar_datos_la_administracion_las_sigue_viendo_y_una_cuenta_nueva_con_ese_correo_no_las_ve()
    {
        var e = Entorno();
        var hojaDeAna = await Registrar(e, CorreoDeAna);
        var hojaDeLuis = await Registrar(e, CorreoDeLuis);
        var marcar = new MarcarHojasDeUnaCuentaCancelada(Correos(), e.Vinculos);

        await bd.Unidad.Ejecutar<int>(async token =>
        {
            await marcar.Aplicar(CuentaDeAna, Guid.NewGuid(), token);
            return 0;
        });

        // No se borra ni altera nada: los datos originales siguen en la base.
        Assert.Equal("Ana Pérez Quispe", await bd.Consultar<string>($"SELECT nombre FROM reclamaciones.hojas_reclamacion WHERE id = '{hojaDeAna}'"));
        // La administración sigue viendo la hoja, marcada como de una cuenta cancelada.
        e.Actor.ComoAdministracion();
        var detalle = await e.Administracion.Obtener(hojaDeAna);
        Assert.True(detalle.EsExito);
        Assert.True(detalle.Valor.DeCuentaCancelada);
        Assert.Equal("Ana Pérez Quispe", detalle.Valor.Nombre);
        Assert.Contains((await e.Administracion.Listar(new ListarHojasEntrada())).Valor.Hojas, h => h.Id == hojaDeAna);
        // Otra persona que se registre después con el mismo correo no ve la hoja; la del otro consumidor sigue visible para su dueño.
        Assert.Equal(0, await bd.Unidad.Ejecutar(async token => (await e.Vinculador.Reclamar(CuentaNuevaConElCorreoDeAna, CorreoDeAna, "consulta", token), (await e.Vinculos.ListarDeLaCuenta(CuentaNuevaConElCorreoDeAna, 1, 10, token)).Total).Total));
        Assert.Equal(1, await bd.Unidad.Ejecutar(async token => (await e.Vinculador.Reclamar(CuentaDeLuis, CorreoDeLuis, "consulta", token), (await e.Vinculos.ListarDeLaCuenta(CuentaDeLuis, 1, 10, token)).Total).Total));
        Assert.Equal(0, await bd.Unidad.Ejecutar(token => new ContarHojasPendientesDeUnCorreo(Correos(), e.Vinculador, e.Vinculos).Contar(CuentaNuevaConElCorreoDeAna, token)));
        Assert.NotEqual(hojaDeAna, hojaDeLuis);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-18")]
    public async Task Una_falla_forzada_en_la_cancelacion_no_deja_hojas_a_medias_y_repetirla_es_idempotente()
    {
        var e = Entorno();
        await Registrar(e, CorreoDeAna);
        await Registrar(e, CorreoDeAna);
        var marcar = new MarcarHojasDeUnaCuentaCancelada(Correos(), e.Vinculos);

        // Identidad falla después de que Reclamaciones marcó: todo se revierte.
        await Assert.ThrowsAsync<InvalidOperationException>(() => bd.Unidad.Ejecutar<int>(async token =>
        {
            await marcar.Aplicar(CuentaDeAna, Guid.NewGuid(), token);
            throw new InvalidOperationException("Identidad falla");
        }));
        Assert.Equal(2L, await PendientesDelCorreo(CorreoDeAna));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE de_cuenta_cancelada"));

        // El reintento (y repetirlo) deja todas marcadas una sola vez.
        for (var i = 0; i < 2; i++)
        {
            await bd.Unidad.Ejecutar<int>(async token =>
            {
                await marcar.Aplicar(CuentaDeAna, Guid.NewGuid(), token);
                return 0;
            });
        }

        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE de_cuenta_cancelada"));
        Assert.Equal(0L, await PendientesDelCorreo(CorreoDeAna));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public async Task La_hoja_de_una_cuenta_cancelada_se_anonimiza_a_los_dos_anios_y_la_administracion_la_ve_anonimizada()
    {
        var e = Entorno();
        // La hoja se siembra ya vieja como administrador de la prueba (la base prohíbe alterar la fecha de registro de una hoja).
        await bd.Ejecutar(
            """
            INSERT INTO reclamaciones.hojas_reclamacion (id, numero, anio, tipo, nombre, documento, domicilio, correo, correo_normalizado, bien_contratado, monto_reclamado, detalle, contenido_hash, fecha_registro, clave_hash, estado)
            VALUES (gen_random_uuid(), '2023-000001', 2023, 'queja', 'Ana Pérez', '12345678', 'Jr. Lima 123', 'ana@example.com', 'ana@example.com', 'Libro', 10, 'Detalle', 'h', now() - interval '2 years 1 day', 'clave-de-la-hoja-vieja', 'pendiente')
            """);
        var hoja = await bd.Consultar<Guid>("SELECT id FROM reclamaciones.hojas_reclamacion WHERE numero = '2023-000001'");
        await bd.Unidad.Ejecutar<int>(async token =>
        {
            await new MarcarHojasDeUnaCuentaCancelada(Correos(), e.Vinculos).Aplicar(CuentaDeAna, Guid.NewGuid(), token);
            return 0;
        });
        e.Actor.ComoSistema();

        var anonimizadas = await e.Anonimizacion.Ejecutar();

        Assert.Equal(1, anonimizadas.Valor);
        e.Actor.ComoAdministracion();
        var detalle = await e.Administracion.Obtener(hoja);
        Assert.True(detalle.Valor.Anonimizada);
        Assert.True(detalle.Valor.DeCuentaCancelada);
        Assert.Equal("[anonimizado]", detalle.Valor.Nombre);
    }

    // ---------- Matriz de operaciones por rol (T21) ----------

    private static IReadOnlyList<(string Nombre, Func<IAdministracionDeReclamaciones, Guid, Task<(bool Exito, CategoriaDeError? Categoria)>> Operacion)> Operaciones() =>
    [
        ("listar", async (a, _) => { var r = await a.Listar(new ListarHojasEntrada()); return (r.EsExito, r.Error?.Categoria); }),
        ("obtener", async (a, id) => { var r = await a.Obtener(id); return (r.EsExito, r.Error?.Categoria); }),
        ("responder", async (a, id) => { var r = await a.Responder(new ResponderHojaEntrada(id, "Respuesta de prueba.")); return (r.EsExito, r.Error?.Categoria); }),
        ("descartar", async (a, id) => { var r = await a.Descartar(id); return (r.EsExito, r.Error?.Categoria); }),
        ("reintentar-aviso", async (a, id) => { var r = await a.ReintentarAviso(id); return (r.EsExito, r.Error?.Categoria); }),
    ];

    [Theory]
    [InlineData(RolesDeUsuario.EncargadoDeSucursal)]
    [InlineData(RolesDeUsuario.PersonalDeLogistica)]
    [InlineData(RolesDeUsuario.Cliente)]
    [Trait("Criterio", "AC-REC-08")]
    [Trait("Criterio", "AC-REC-16")]
    public async Task Cada_operacion_de_la_administracion_rechaza_de_forma_uniforme_a_quien_no_es_de_la_administracion_con_un_evento_por_rechazo_y_sin_tocar_la_hoja(string rol)
    {
        var e = Entorno();
        var hoja = await Registrar(e, CorreoDeAna);
        var ajena = Guid.NewGuid();
        e.Actor.Actual = Usuario(rol);

        foreach (var (nombre, operacion) in Operaciones())
        {
            var eventosAntes = e.Denegaciones.Eventos.Count;
            var sobreLaExistente = await operacion(e.Administracion, hoja);
            var sobreLaInexistente = await operacion(e.Administracion, ajena);

            Assert.False(sobreLaExistente.Exito, nombre);
            Assert.Equal(CategoriaDeError.Prohibido, sobreLaExistente.Categoria);
            // La misma respuesta exista o no la hoja, y un evento por cada rechazo de la sesión.
            Assert.Equal(sobreLaExistente, sobreLaInexistente);
            Assert.Equal(eventosAntes + 2, e.Denegaciones.Eventos.Count);
        }

        Assert.Equal("pendiente", await bd.Consultar<string>($"SELECT estado FROM reclamaciones.hojas_reclamacion WHERE id = '{hoja}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-08")]
    public async Task El_visitante_recibe_no_autenticado_en_cada_operacion_con_un_evento_sin_sesion_y_ninguno_por_el_numero_de_la_hoja()
    {
        var e = Entorno();
        var hoja = await Registrar(e, CorreoDeAna);
        e.Actor.Actual = ActorActual.Anonimo();

        foreach (var (nombre, operacion) in Operaciones())
        {
            var resultado = await operacion(e.Administracion, hoja);
            Assert.False(resultado.Exito, nombre);
            Assert.Equal(CategoriaDeError.NoAutenticado, resultado.Categoria);
        }

        // Un evento genérico «sin sesión» por intento (ECO-NO-AUTENTICADO): ninguno nombra la hoja ni su número, que el visitante no debe poder sondear.
        Assert.Equal(Operaciones().Count, e.Denegaciones.Eventos.Count);
        Assert.All(e.Denegaciones.Eventos, ev =>
        {
            Assert.Equal("sin-sesion", ev.Motivo);
            Assert.Equal("ECO-NO-AUTENTICADO", ev.CodigoDeError);
            Assert.DoesNotContain(hoja.ToString(), ev.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("-0000", ev.ToString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Criterio", "AC-REC-12")]
    public async Task La_administracion_central_puede_cada_operacion_y_una_hoja_inexistente_es_no_encontrada_sin_evento_de_denegacion()
    {
        var e = Entorno();
        var hoja = await Registrar(e, CorreoDeAna);
        e.Actor.ComoAdministracion();

        Assert.True((await Operaciones()[0].Operacion(e.Administracion, hoja)).Exito);
        Assert.True((await Operaciones()[1].Operacion(e.Administracion, hoja)).Exito);
        var inexistente = await Operaciones()[1].Operacion(e.Administracion, Guid.NewGuid());
        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Categoria);
        Assert.Empty(e.Denegaciones.Eventos);
    }
}

/// <summary>El correo verificado de cada cuenta tal como lo daría Identidad: <see langword="null"/> si no está verificado, y puede caerse (el recuento y la cancelación fallan cerrados).</summary>
internal sealed class CorreosDeLasCuentas : Dictionary<Guid, string?>, ICorreoVerificadoDeLaCuenta
{
    public bool Falla { get; set; }

    public Task<string?> De(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            throw new InvalidOperationException("Identidad no responde.");
        }

        return Task.FromResult(TryGetValue(usuarioId, out var correo) ? correo : null);
    }
}
