using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Domain.Reclamaciones;
using RolesDeUsuario = BabelHub.Shared.Contratos.RolesDeUsuario;

namespace BabelHub.IntegrationTests.Reclamaciones;

/// <summary>
/// T19 a T21 (parte con PostgreSQL real): los casos de uso reales de las hojas sobre el repositorio y la función de base de datos, con dobles solo para lo externo. Cincuenta registros simultáneos con números únicos y consecutivos, el cupo de tres por origen bajo simultaneidad, la misma clave
/// repetida (también con el almacén de contadores sin memoria: el índice único es la autoridad), veinte pares de responder contra responder y contra descartar, el correo caído con su reintento que envía una sola vez, la privacidad de lo auditado y lo enviado, la anonimización con la auditoría
/// caída que no deja nada a medias y la consulta de la administración que se niega si no se puede auditar.
/// </summary>
[Collection(ColeccionPostgresDeReclamaciones.Nombre)]
public sealed class HojasConPostgresTests(PostgresDeReclamacionesFixture bd) : IAsyncLifetime
{
    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private EntornoDeReclamacionesConPostgres Entorno() => new(bd);


    private async Task<Guid> Registrar(EntornoDeReclamacionesConPostgres e)
    {
        var resultado = await e.Reclamaciones.Registrar(e.Entrada());
        Assert.True(resultado.EsExito);
        return await bd.Unidad.Ejecutar(async token => (await e.Hojas.ObtenerPorNumero(NumeroDeHoja.TryParse(resultado.Valor.Numero, out var n) ? n : throw new InvalidOperationException(), token))!.Id);
    }

    // ---------- Registro ----------

    [Fact]
    [Trait("Criterio", "AC-REC-04")]
    public async Task Cincuenta_registros_simultaneos_reciben_numeros_unicos_y_consecutivos()
    {
        var e = Entorno();
        e.Limites.Maximo = 1_000;

        var resultados = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => e.Reclamaciones.Registrar(e.Entrada())));

        Assert.All(resultados, r => Assert.True(r.EsExito));
        Assert.Equal(Enumerable.Range(1, 50), resultados.Select(r => NumeroDeHoja.TryParse(r.Valor.Numero, out var n) ? n.Secuencia : 0).Order());
        Assert.Equal(50L, await bd.Consultar<long>("SELECT count(*) FROM reclamaciones.hojas_reclamacion"));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-09")]
    public async Task Diez_solicitudes_simultaneas_de_un_origen_solo_registran_tres_y_el_resto_recibe_el_limite()
    {
        var e = Entorno();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => e.Reclamaciones.Registrar(e.Entrada())));

        Assert.Equal(3, resultados.Count(r => r.EsExito));
        Assert.All(resultados.Where(r => !r.EsExito), r => Assert.Equal(CategoriaDeError.Limite, r.Error!.Categoria));
        Assert.Equal(3L, await bd.Consultar<long>("SELECT count(*) FROM reclamaciones.hojas_reclamacion"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Criterio", "AC-REC-11")]
    public async Task La_misma_clave_repetida_devuelve_la_misma_constancia_sin_crear_otra_hoja_aunque_el_almacen_de_contadores_la_olvide(bool almacenSinMemoria)
    {
        var e = Entorno();
        e.Limites.IgnoraLasClaves = almacenSinMemoria;
        var entrada = e.Entrada("clave-fija-del-formulario");

        var primera = await e.Reclamaciones.Registrar(entrada);
        var segunda = await e.Reclamaciones.Registrar(entrada);

        Assert.True(primera.EsExito);
        Assert.True(segunda.EsExito);
        Assert.Equal(primera.Valor.Numero, segunda.Valor.Numero);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reclamaciones.hojas_reclamacion"));
    }

    [Fact]
    public async Task La_misma_clave_con_otro_contenido_es_un_conflicto_y_no_crea_otra_hoja()
    {
        var e = Entorno();
        Assert.True((await e.Reclamaciones.Registrar(e.Entrada("clave-fija-de-un-solo-uso", "Primer detalle"))).EsExito);

        var otra = await e.Reclamaciones.Registrar(e.Entrada("clave-fija-de-un-solo-uso", "Otro detalle distinto"));

        Assert.False(otra.EsExito);
        Assert.Equal(CategoriaDeError.Conflicto, otra.Error!.Categoria);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reclamaciones.hojas_reclamacion"));
    }

    // ---------- Responder y descartar ----------

    [Theory]
    [InlineData("responder")]
    [InlineData("descartar")]
    [Trait("Criterio", "AC-REC-05")]
    [Trait("Criterio", "AC-REC-28")]
    public async Task Veinte_pares_de_responder_contra_responder_o_contra_descartar_dejan_un_solo_ganador_por_hoja(string contra)
    {
        var e = Entorno();
        e.Limites.Maximo = 1_000;
        var ids = new List<Guid>();
        for (var i = 0; i < 20; i++)
        {
            ids.Add(await Registrar(e));
        }

        var resultados = await Task.WhenAll(ids.SelectMany(id => new[]
        {
            e.Administracion.Responder(new ResponderHojaEntrada(id, "Respuesta de la administración.")),
            contra == "responder" ? e.Administracion.Responder(new ResponderHojaEntrada(id, "Otra respuesta distinta.")) : e.Administracion.Descartar(id),
        }));

        Assert.Equal(20, resultados.Count(r => r.EsExito));
        Assert.All(resultados.Where(r => !r.EsExito), r => Assert.Equal("rec-estado-invalido", r.Error!.Clave));
        Assert.Equal(20L, await bd.Consultar<long>("SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE estado <> 'pendiente'"));
        Assert.Equal(20L, await bd.Consultar<long>("SELECT count(DISTINCT id) FROM reclamaciones.hojas_reclamacion WHERE estado <> 'pendiente'"));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-14")]
    public async Task El_correo_caido_deja_la_respuesta_guardada_y_el_aviso_pendiente_y_dos_reintentos_simultaneos_envian_una_sola_vez()
    {
        var e = Entorno();
        var id = await Registrar(e);
        e.Correo.Falla = true;

        var respondida = await e.Administracion.Responder(new ResponderHojaEntrada(id, "Le reponemos el libro."));

        Assert.True(respondida.EsExito);
        Assert.Equal(AvisoDeLaRespuesta.Pendiente, respondida.Valor.Aviso);
        Assert.Empty(e.Correo.Correos);

        e.Correo.Falla = false;
        var reintentos = await Task.WhenAll(e.Administracion.ReintentarAviso(id), e.Administracion.ReintentarAviso(id));

        Assert.Single(e.Correo.Correos);
        Assert.Equal(1, reintentos.Count(r => r.EsExito && r.Valor.Aviso == AvisoDeLaRespuesta.Enviado));
        Assert.Equal("enviado", await bd.Consultar<string>($"SELECT aviso_estado FROM reclamaciones.hojas_reclamacion WHERE id = '{id}'"));
    }

    // ---------- Privacidad ----------

    [Fact]
    [Trait("Criterio", "AC-REC-13")]
    public async Task Lo_auditado_y_lo_enviado_no_llevan_documento_domicilio_ni_correo()
    {
        var e = Entorno();
        var id = await Registrar(e);
        Assert.True((await e.Administracion.Obtener(id)).EsExito);
        Assert.True((await e.Administracion.Responder(new ResponderHojaEntrada(id, "Le reponemos el libro."))).EsExito);

        var auditado = string.Join('|', e.Auditoria.Solicitudes.SelectMany(s => new[] { s.ObjetoId }.Concat(s.Detalle?.Values ?? [])));
        var enviado = string.Join('|', e.Correo.Correos.SelectMany(c => (c.Datos ?? new Dictionary<string, string>()).Values));

        Assert.NotEmpty(e.Auditoria.Solicitudes);
        foreach (var dato in new[] { "12345678", "Olivos", "ana.perez@correo.com", "Ana Pérez" })
        {
            Assert.DoesNotContain(dato, auditado, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(dato, enviado, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-REC-13")]
    public async Task La_consulta_de_la_administracion_se_niega_si_no_se_puede_auditar_y_otros_roles_no_la_ven()
    {
        var e = Entorno();
        var id = await Registrar(e);

        e.Auditoria.Caida = true;
        var sinAuditoria = await e.Administracion.Obtener(id);
        e.Auditoria.Caida = false;
        e.Actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, Guid.NewGuid());
        var encargado = await e.Administracion.Obtener(id);
        e.Actor.Actual = ActorActual.Anonimo();
        var visitante = await e.Administracion.Obtener(id);

        Assert.Equal(CategoriaDeError.NoDisponible, sinAuditoria.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, encargado.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoAutenticado, visitante.Error!.Categoria);
        // La administración registra un acceso denegado por cada intento de una sesión (el encargado) y también por la falta de sesión del visitante.
        Assert.Equal(2, e.Denegaciones.Eventos.Count);
    }

    // ---------- Anonimización ----------

    private async Task SembrarVieja(string clave)
    {
        await using var comando = bd.Administracion.CreateCommand(
            """
            INSERT INTO reclamaciones.hojas_reclamacion (id, numero, anio, tipo, nombre, documento, domicilio, correo, correo_normalizado, bien_contratado, monto_reclamado, detalle, contenido_hash, fecha_registro, clave_hash, estado)
            VALUES (gen_random_uuid(), '2023-000001', 2023, 'queja', 'Ana Pérez', '12345678', 'Jr. Lima 123', 'ana@example.com', 'ana@example.com', 'Libro', 10, 'Detalle', 'h', now() - interval '3 years', @clave, 'pendiente')
            """);
        comando.Parameters.AddWithValue("clave", clave);
        await comando.ExecuteNonQueryAsync();
    }

    [Fact]
    [Trait("Criterio", "AC-REC-15")]
    public async Task Con_la_auditoria_caida_la_anonimizacion_no_deja_nada_a_medias_y_al_volver_anonimiza_y_audita_solo_la_cantidad()
    {
        var e = Entorno();
        await SembrarVieja("clave-vieja");
        e.Actor.ComoSistema();

        e.Auditoria.Caida = true;
        var caida = await e.Anonimizacion.Ejecutar();
        var sinAnonimizar = await bd.Consultar<bool>("SELECT anonimizada_en IS NULL FROM reclamaciones.hojas_reclamacion");
        e.Auditoria.Caida = false;
        var exito = await e.Anonimizacion.Ejecutar();

        Assert.Equal(CategoriaDeError.NoDisponible, caida.Error!.Categoria);
        Assert.True(sinAnonimizar);
        Assert.Equal(1, exito.Valor);
        var solicitud = Assert.Single(e.Auditoria.Solicitudes);
        Assert.Equal("1", solicitud.Detalle!["cantidad"]);
        Assert.DoesNotContain("12345678", string.Join('|', solicitud.Detalle.Values), StringComparison.Ordinal);
        Assert.Equal("[anonimizado]", await bd.Consultar<string>("SELECT nombre FROM reclamaciones.hojas_reclamacion"));
    }

    [Fact]
    public async Task Solo_el_sistema_puede_anonimizar_y_dos_instancias_a_la_vez_anonimizan_cada_hoja_una_sola_vez()
    {
        var e = Entorno();
        await SembrarVieja("clave-vieja");

        e.Actor.ComoAdministracion();
        var comoAdministracion = await e.Anonimizacion.Ejecutar();
        e.Actor.ComoSistema();
        var resultados = await Task.WhenAll(e.Anonimizacion.Ejecutar(), e.Anonimizacion.Ejecutar());

        Assert.False(comoAdministracion.EsExito);
        Assert.Equal(1, resultados.Sum(r => r.Valor));
    }
}
