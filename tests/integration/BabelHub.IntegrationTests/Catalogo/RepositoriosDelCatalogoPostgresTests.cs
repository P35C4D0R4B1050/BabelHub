using System.Security.Cryptography;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Domain.Catalogo;
using Npgsql;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>Los repositorios y el esquema <c>catalogo</c> contra PostgreSQL real (T14; CAT-02, CAT-03, CAT-17, AC-CAT-02, AC-CAT-14): índices únicos, concurrencia optimista, disparador diferido y permisos del rol.</summary>
[Collection(ColeccionPostgresDelCatalogo.Nombre)]
public sealed class RepositoriosDelCatalogoPostgresTests(PostgresDelCatalogoFixture bd) : IAsyncLifetime
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(-5));
    private static readonly Guid Ana = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static int _siguiente = 5000;

    internal static string IsbnNumero()
    {
        var doce = "978" + Interlocked.Increment(ref _siguiente).ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        var suma = doce.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return doce + ((10 - (suma % 10)) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static HuellaDeArchivo Huella() => HuellaDeArchivo.Desde(SHA256.HashData(Guid.NewGuid().ToByteArray()));

    internal static Edicion Impresa(string? isbn = null, decimal precio = 129m) => Edicion.Impresa(Guid.NewGuid(), Isbn.Crear(isbn ?? IsbnNumero()), Precio.Crear(precio));

    internal static Edicion Digital(string? isbn = null, string[]? territorios = null) =>
        Edicion.Digital(
            Guid.NewGuid(),
            Isbn.Crear(isbn ?? IsbnNumero()),
            Precio.Crear(49m),
            LicenciaDigital.Rehidratar(TipoDeLicencia.Comercial, "Editorial Andina", territorios ?? ["PE", "CL"], new DateOnly(2027, 5, 1), FormatoDigital.Epub),
            new ArchivoDeEdicion(ClaveEnAlmacen.Nueva(), TipoDeArchivo.Epub, 12_345, Huella(), Ahora, Ana));

    internal static Obra UnaObra(string titulo = "Estructuras de datos", params Edicion[] ediciones) =>
        Obra.Registrar(
            Guid.NewGuid(),
            new DatosDeObra(titulo, "Ana Quispe", "Editorial Andina", "Técnico"),
            new PortadaDeObra(ClaveEnAlmacen.Nueva(), TipoDeArchivo.Jpeg, Huella()),
            ediciones.Length == 0 ? [Impresa()] : ediciones,
            Ahora).Valor;

    // ---------- Obras ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public async Task Una_obra_con_una_impresa_y_una_digital_se_guarda_y_se_lee_igual_con_licencia_y_archivo()
    {
        var obra = UnaObra("Con ambas", Impresa("978-0-00-000001-9", 129.50m), Digital());

        Assert.Equal(ResultadoDeEscrituraDeObra.Escrita, await bd.Obras.Agregar(obra));
        var leida = (await bd.Obras.Obtener(obra.Id, false))!;

        Assert.Equal("Con ambas", leida.Titulo);
        Assert.Equal(obra.Version, leida.Version);
        Assert.Equal(obra.Portada.ClaveDelAlmacen, leida.Portada.ClaveDelAlmacen);
        Assert.Equal(obra.Portada.Huella, leida.Portada.Huella);
        Assert.Equal(2, leida.Ediciones.Count);
        var impresa = leida.Ediciones.Single(e => !e.EsDigital);
        Assert.Equal("9780000000019", impresa.Isbn.Isbn13);
        Assert.Equal("978-0-00-000001-9", impresa.Isbn.TextoIngresado);
        Assert.Equal(129.50m, impresa.Precio.Valor);
        var digital = leida.Ediciones.Single(e => e.EsDigital);
        var original = obra.Ediciones.Single(e => e.EsDigital);
        Assert.Equal(original.Licencia, digital.Licencia);
        Assert.Equal(["CL", "PE"], digital.Licencia!.Territorios);
        Assert.Equal(original.Archivo!.ClaveDelAlmacen, digital.Archivo!.ClaveDelAlmacen);
        Assert.Equal(original.Archivo.Huella, digital.Archivo.Huella);
        Assert.Equal(original.Archivo.TamanoEnBytes, digital.Archivo.TamanoEnBytes);
        Assert.Equal(obra.Id, (await bd.Obras.ObtenerPorEdicion(digital.Id, false))!.Id);
        Assert.Null(await bd.Obras.Obtener(Guid.NewGuid(), false));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-02")]
    public async Task Un_isbn_repetido_no_inserta_nada_no_aborta_la_transaccion_y_el_titulo_se_lee_despues()
    {
        var primera = UnaObra("Dueña del ISBN", Impresa("978-0-00-000001-9"));
        await bd.Obras.Agregar(primera);
        var segunda = UnaObra("Otra obra", Impresa("978-0-00-000001-9"));

        string? titulo = null;
        var resultado = ResultadoDeEscrituraDeObra.Escrita;
        await Assert.ThrowsAsync<InvalidOperationException>(() => bd.Unidad.Ejecutar<int>(
            async token =>
            {
                resultado = await bd.Obras.Agregar(segunda, token);
                titulo = await bd.Obras.TituloDeLaEdicionConIsbn("9780000000019", token);
                throw new InvalidOperationException("Se revierte como lo hace TransaccionPorCasoDeUso.");
            },
            default));

        Assert.Equal(ResultadoDeEscrituraDeObra.IsbnDuplicado, resultado);
        Assert.Equal("Dueña del ISBN", titulo);
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.obras"));
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.ediciones"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-14")]
    public async Task Un_isbn_10_y_su_isbn_13_son_la_misma_edicion_para_el_indice_unico()
    {
        await bd.Obras.Agregar(UnaObra("Con ISBN-10", Impresa("0-306-40615-2")));

        var repetido = await bd.Unidad.Ejecutar(async token => await bd.Obras.Agregar(UnaObra("Con ISBN-13", Impresa("978-0-306-40615-7")), token), default);

        Assert.Equal(ResultadoDeEscrituraDeObra.IsbnDuplicado, repetido);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-14")]
    public async Task Dos_altas_simultaneas_con_el_mismo_isbn_aceptan_solo_una()
    {
        var isbn = IsbnNumero();
        var a = UnaObra("Simultánea A", Impresa(isbn));
        var b = UnaObra("Simultánea B", Impresa(isbn));

        var resultados = await Task.WhenAll(Intentar(a), Intentar(b));

        Assert.Single(resultados, ResultadoDeEscrituraDeObra.Escrita);
        Assert.Single(resultados, ResultadoDeEscrituraDeObra.IsbnDuplicado);
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.ediciones"));

async Task<ResultadoDeEscrituraDeObra> Intentar(Obra obra)        {            try            {                return await bd.Unidad.Ejecutar(async token => await Registrar(obra, token), default);            }            catch (DuplicadoRevertido)            {                return ResultadoDeEscrituraDeObra.IsbnDuplicado;            }        }
        async Task<ResultadoDeEscrituraDeObra> Registrar(Obra obra, CancellationToken token)
        {
            var escritura = await bd.Obras.Agregar(obra, token);
            if (escritura != ResultadoDeEscrituraDeObra.Escrita)
            {
                // La obra ya se insertó en esta transacción: sin revertir quedaría una obra sin ediciones (el caso de uso lo revierte).
                throw new DuplicadoRevertido();
            }

            return escritura;
        }
    }

    private sealed class DuplicadoRevertido : Exception;

    [Fact]
    [Trait("Criterio", "AC-CAT-08")]
    public async Task Guardar_con_la_version_leida_aplica_los_cambios_y_con_una_vieja_da_conflicto_sin_escribir()
    {
        var obra = UnaObra("Versionada", Impresa(precio: 129m), Digital());
        await bd.Obras.Agregar(obra);
        var cargada = (await bd.Obras.Obtener(obra.Id, true))!;
        var digital = cargada.Ediciones.Single(e => e.EsDigital);
        var impresa = cargada.Ediciones.Single(e => !e.EsDigital);
        var versionLeida = cargada.Version;
        cargada.EditarEdicion(impresa.Id, new CambioDeEdicion(Precio: Precio.Crear(139m)));
        cargada.RetirarEdicionDigital(digital.Id, "Fin del contrato", Ahora.AddDays(1));

        Assert.Equal(ResultadoDeEscrituraDeObra.Escrita, await bd.Obras.Guardar(cargada, versionLeida));
        var guardada = (await bd.Obras.Obtener(obra.Id, false))!;
        Assert.Equal(versionLeida + 2, guardada.Version);
        Assert.Equal(139m, guardada.Ediciones.Single(e => !e.EsDigital).Precio.Valor);
        var retirada = guardada.Ediciones.Single(e => e.EsDigital);
        Assert.Equal(EstadoDeEdicion.RetiradaDeVenta, retirada.Estado);
        Assert.Equal("Fin del contrato", retirada.MotivoDeRetiro);
        Assert.NotNull(retirada.Archivo);

        var otra = (await bd.Obras.Obtener(obra.Id, false))!;
        otra.EditarDatos(new DatosDeObra("Otro título", "A", "E", "C"));
        Assert.Equal(ResultadoDeEscrituraDeObra.ConflictoDeVersion, await bd.Obras.Guardar(otra, versionLeida));
        Assert.Equal("Versionada", (await bd.Obras.Obtener(obra.Id, false))!.Titulo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-08")]
    public async Task Guardar_agrega_una_edicion_cambia_la_licencia_reemplaza_el_archivo_y_da_de_baja()
    {
        var obra = UnaObra("Cambios", Digital());
        await bd.Obras.Agregar(obra);
        var cargada = (await bd.Obras.Obtener(obra.Id, false))!;
        var versionLeida = cargada.Version;
        var digital = cargada.Ediciones[0];
        var nuevoArchivo = new ArchivoDeEdicion(ClaveEnAlmacen.Nueva(), TipoDeArchivo.Epub, 999, Huella(), Ahora.AddDays(1), Ana);
        var nuevaLicencia = LicenciaDigital.Rehidratar(TipoDeLicencia.DominioPublico, "Dominio público", ["PE"], null, FormatoDigital.Epub);
        cargada.EditarEdicion(digital.Id, new CambioDeEdicion(Licencia: nuevaLicencia, Archivo: nuevoArchivo));
        cargada.AgregarEdicion(Impresa());

        Assert.Equal(ResultadoDeEscrituraDeObra.Escrita, await bd.Obras.Guardar(cargada, versionLeida));
        var guardada = (await bd.Obras.Obtener(obra.Id, false))!;
        Assert.Equal(2, guardada.Ediciones.Count);
        var editada = guardada.Ediciones.Single(e => e.EsDigital);
        Assert.Equal(nuevaLicencia, editada.Licencia);
        Assert.Equal(nuevoArchivo.ClaveDelAlmacen, editada.Archivo!.ClaveDelAlmacen);

        var porBaja = (await bd.Obras.Obtener(obra.Id, false))!;
        var v = porBaja.Version;
        porBaja.DarDeBaja(Ahora.AddDays(2));
        Assert.Equal(ResultadoDeEscrituraDeObra.Escrita, await bd.Obras.Guardar(porBaja, v));
        var baja = (await bd.Obras.Obtener(obra.Id, false))!;
        Assert.Equal(EstadoDeObra.Baja, baja.Estado);
        Assert.All(baja.Ediciones, e => Assert.Equal(EstadoDeEdicion.Baja, e.Estado));
        Assert.Equal("Cambios", baja.Titulo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-02")]
    public async Task Guardar_una_edicion_nueva_con_un_isbn_de_otra_obra_da_isbn_duplicado()
    {
        await bd.Obras.Agregar(UnaObra("Dueña", Impresa("978-0-00-000001-9")));
        var otra = UnaObra("Otra", Impresa());
        await bd.Obras.Agregar(otra);
        var cargada = (await bd.Obras.Obtener(otra.Id, false))!;
        var v = cargada.Version;
        cargada.AgregarEdicion(Digital("978-0-00-000001-9"));

        var resultado = await bd.Unidad.Ejecutar(async token => await bd.Obras.Guardar(cargada, v, token), default);

        Assert.Equal(ResultadoDeEscrituraDeObra.IsbnDuplicado, resultado);
    }

    // ---------- Índices, disparador y permisos ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-01")]
    public async Task Una_obra_no_puede_tener_dos_ediciones_activas_del_mismo_tipo_ni_ediciones_repetidas_por_el_indice_parcial()
    {
        var obra = UnaObra("Única por tipo", Impresa());
        await bd.Obras.Agregar(obra);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar(
            $"""
            INSERT INTO catalogo.ediciones (id, obra_id, tipo, isbn13, isbn_ingresado, precio, estado)
            VALUES (gen_random_uuid(), '{obra.Id}', 'impresa', '{IsbnNumero()}', '{IsbnNumero()}', 10, 'activa')
            """));

        Assert.Equal("23505", ex.SqlState);
        Assert.Contains("ediciones_un_tipo_por_obra", ex.ConstraintName, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-03")]
    public async Task El_disparador_diferido_rechaza_al_confirmar_una_digital_sin_licencia_o_sin_archivo_y_una_impresa_con_licencia()
    {
        var obra = UnaObra("Trigger", Impresa());
        await bd.Obras.Agregar(obra);
        var digitalId = Guid.NewGuid();

        var sinLicencia = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar(
            $"""
            BEGIN;
            INSERT INTO catalogo.ediciones (id, obra_id, tipo, isbn13, isbn_ingresado, precio, estado) VALUES ('{digitalId}', '{obra.Id}', 'digital', '{IsbnNumero()}', '0123456789', 10, 'activa');
            COMMIT;
            """));
        var impresaConLicencia = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar(
            $"""
            BEGIN;
            INSERT INTO catalogo.licencias (edicion_id, tipo, titular, territorios, vigente_hasta, formato) VALUES ('{obra.Ediciones[0].Id}', 'comercial', 'X', ARRAY['PE'], '2030-01-01', 'epub');
            COMMIT;
            """));

        Assert.Equal("23514", sinLicencia.SqlState);
        Assert.Equal("23514", impresaConLicencia.SqlState);
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.ediciones"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-03")]
    public async Task La_funcion_del_disparador_es_security_definer_de_dueno_propio_con_el_search_path_fijo()
    {
        var definer = await bd.Consultar<bool>("SELECT prosecdef FROM pg_proc WHERE proname = 'verificar_edicion'");
        var dueno = await bd.Consultar<string>("SELECT pg_get_userbyid(proowner) FROM pg_proc WHERE proname = 'verificar_edicion'");
        var configuracion = await bd.Consultar<string>("SELECT array_to_string(proconfig, ',') FROM pg_proc WHERE proname = 'verificar_edicion'");

        Assert.True(definer);
        Assert.Equal("propietario", dueno);
        Assert.Contains("search_path=pg_catalog, catalogo", configuracion, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public async Task Las_extensiones_de_busqueda_existen_y_normalizar_quita_tildes_y_mayusculas()
    {
        Assert.Equal(2, await bd.Consultar<long>("SELECT count(*) FROM pg_extension WHERE extname IN ('unaccent', 'pg_trgm')"));
        Assert.Equal("algebra lineal", await bd.Consultar<string>("SELECT catalogo.normalizar('Álgebra LINEAL')"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-11")]
    public async Task El_rol_de_acceso_por_si_solo_no_lee_el_esquema_y_con_el_rol_del_modulo_no_cambia_el_isbn_ni_borra_obras()
    {
        var obra = UnaObra("Permisos", Impresa());
        await bd.Obras.Agregar(obra);

        await using (var sinRol = await bd.Acceso.OpenConnectionAsync())
        {
            var lectura = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await using var comando = new NpgsqlCommand("SELECT count(*) FROM catalogo.obras", sinRol);
                await comando.ExecuteScalarAsync();
            });
            Assert.Equal("42501", lectura.SqlState);
        }

        var isbn = await Assert.ThrowsAsync<PostgresException>(() => bd.Unidad.Ejecutar(
            async token =>
            {
                await bd.AccesoAlEsquema.Ejecutar(
                    async (conexion, transaccion, interno) =>
                    {
                        await using var comando = new NpgsqlCommand("UPDATE catalogo.ediciones SET isbn13 = '9780000099999'", conexion, transaccion);
                        return await comando.ExecuteNonQueryAsync(interno);
                    },
                    token);
                return 0;
            },
            default));
        var borrado = await Assert.ThrowsAsync<PostgresException>(() => bd.Unidad.Ejecutar(
            async token =>
            {
                await bd.AccesoAlEsquema.Ejecutar(
                    async (conexion, transaccion, interno) =>
                    {
                        await using var comando = new NpgsqlCommand("DELETE FROM catalogo.obras", conexion, transaccion);
                        return await comando.ExecuteNonQueryAsync(interno);
                    },
                    token);
                return 0;
            },
            default));

        Assert.Equal("42501", isbn.SqlState);
        Assert.Equal("42501", borrado.SqlState);
    }

    // ---------- Archivos pendientes ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Una_referencia_nace_subiendo_pasa_a_lista_se_consume_y_las_claves_del_almacen_son_unicas()
    {
        var referencia = ReferenciaDeArchivo.Iniciar(Guid.NewGuid(), Ana, FinalidadDeArchivo.Libro, Ahora);
        await bd.Referencias.Agregar(referencia);

        var subiendo = (await bd.Referencias.Obtener(referencia.Id, true))!;
        Assert.Equal(EstadoDeReferencia.Subiendo, subiendo.Estado);
        Assert.Null(subiendo.TipoDetectado);
        subiendo.MarcarLista(TipoDeArchivo.Epub, 5_000, Huella());
        await bd.Referencias.Guardar(subiendo);
        var lista = (await bd.Referencias.Obtener(referencia.Id, true))!;
        Assert.Equal(EstadoDeReferencia.Lista, lista.Estado);
        Assert.Equal(TipoDeArchivo.Epub, lista.TipoDetectado);
        Assert.Equal(Ahora.AddHours(24), lista.VenceEn);

        Assert.Equal(ResultadoDeConsumo.Consumida, lista.Consumir(Ana, FinalidadDeArchivo.Libro, FormatoDigital.Epub, Ahora.AddHours(1)));
        await bd.Referencias.Guardar(lista);
        var consumida = (await bd.Referencias.Obtener(referencia.Id, false))!;
        Assert.Equal(EstadoDeReferencia.Consumida, consumida.Estado);
        Assert.Equal(Ahora.AddHours(1), consumida.ConsumidoEn);

        var duplicada = ReferenciaDeArchivo.Rehidratar(Guid.NewGuid(), Ana, FinalidadDeArchivo.Libro, referencia.ClaveDelAlmacen, EstadoDeReferencia.Subiendo, null, null, null, Ahora, Ahora.AddHours(24), null);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => bd.Referencias.Agregar(duplicada));
        Assert.Equal("23505", ex.SqlState);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task El_descarte_es_un_upsert_por_clave_actualiza_la_consumida_y_si_ya_se_purgo_inserta_una_fila_de_descarte()
    {
        var consumida = ReferenciaDeArchivo.Iniciar(Guid.NewGuid(), Ana, FinalidadDeArchivo.Libro, Ahora);
        consumida.MarcarLista(TipoDeArchivo.Epub, 10, Huella());
        consumida.Consumir(Ana, FinalidadDeArchivo.Libro, FormatoDigital.Epub, Ahora.AddMinutes(1));
        await bd.Referencias.Agregar(consumida);
        var purgada = ClaveEnAlmacen.Nueva();

        await bd.Referencias.EncolarDescarte(consumida.ClaveDelAlmacen, FinalidadDeArchivo.Libro, Guid.NewGuid(), Ahora.AddDays(30));
        await bd.Referencias.EncolarDescarte(purgada, FinalidadDeArchivo.Portada, Ana, Ahora.AddDays(30));

        Assert.Equal(EstadoDeReferencia.Descarte, (await bd.Referencias.Obtener(consumida.Id, false))!.Estado);
        var insertada = await bd.Consultar<string>($"SELECT estado || ',' || finalidad || ',' || usuario_id FROM catalogo.archivos_pendientes WHERE clave_en_almacen = '{purgada}'");
        Assert.Equal($"descarte,portada,{Ana}", insertada);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task La_depuracion_lista_las_subiendo_viejas_las_listas_vencidas_los_descartes_y_las_consumidas_de_mas_de_7_dias_y_nada_mas()
    {
        async Task<Guid> Sembrar(EstadoDeReferencia estado, TimeSpan edad)
        {
            var creada = Ahora - edad;
            var id = Guid.NewGuid();
            var r = ReferenciaDeArchivo.Rehidratar(
                id,
                Ana,
                FinalidadDeArchivo.Libro,
                ClaveEnAlmacen.Nueva(),
                estado,
                estado is EstadoDeReferencia.Subiendo ? null : TipoDeArchivo.Epub,
                estado is EstadoDeReferencia.Subiendo ? null : 10,
                estado is EstadoDeReferencia.Subiendo ? null : Huella(),
                creada,
                creada.AddHours(24),
                estado == EstadoDeReferencia.Consumida ? creada : null);
            await bd.Referencias.Agregar(r);
            return id;
        }

        var viejaSubiendo = await Sembrar(EstadoDeReferencia.Subiendo, TimeSpan.FromHours(2));
        await Sembrar(EstadoDeReferencia.Subiendo, TimeSpan.FromMinutes(10));
        var vencida = await Sembrar(EstadoDeReferencia.Lista, TimeSpan.FromHours(25));
        await Sembrar(EstadoDeReferencia.Lista, TimeSpan.FromHours(3));
        var descarte = await Sembrar(EstadoDeReferencia.Descarte, TimeSpan.FromMinutes(1));
        var consumidaVieja = await Sembrar(EstadoDeReferencia.Consumida, TimeSpan.FromDays(8));
        await Sembrar(EstadoDeReferencia.Consumida, TimeSpan.FromDays(6));

        var candidatas = await bd.Referencias.Depurables(Ahora, TimeSpan.FromHours(1), 100);

        Assert.Equal(new[] { viejaSubiendo, vencida, descarte, consumidaVieja }.Order(), candidatas.Select(c => c.Id).Order());

        await bd.Referencias.Eliminar(descarte);
        Assert.Null(await bd.Referencias.Obtener(descarte, false));
    }

    // ---------- Ingresos solicitados ----------

    private static IngresoSolicitado Ingreso(Guid edicion, int cantidad = 30, Guid? clave = null) =>
        IngresoSolicitado.Solicitar(Guid.NewGuid(), edicion, clave ?? Guid.NewGuid(), Ana, cantidad, "GR-001", "nota", Ahora);

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Un_solo_pendiente_por_edicion_y_una_clave_de_idempotencia_unica_y_las_transiciones_son_condicionales()
    {
        var obra = UnaObra("Con ingresos", Impresa());
        await bd.Obras.Agregar(obra);
        var edicion = obra.Ediciones[0].Id;
        var primero = Ingreso(edicion);

        Assert.True(await bd.Ingresos.Agregar(primero));
        Assert.False(await bd.Ingresos.Agregar(Ingreso(edicion)));
        Assert.False(await bd.Ingresos.Agregar(Ingreso(edicion, 5, primero.ClaveDeIdempotencia)));
        Assert.Equal(primero.Id, (await bd.Ingresos.PendienteDeLaEdicion(edicion))!.Id);
        Assert.Equal(primero.Id, (await bd.Ingresos.PorClave(primero.ClaveDeIdempotencia))!.Id);

        var enCurso = (await bd.Ingresos.Obtener(primero.Id, true))!;
        var ingresoId = Guid.NewGuid();
        enCurso.MarcarRegistrado(ingresoId, Ahora.AddSeconds(5));
        Assert.True(await bd.Ingresos.GuardarTransicion(enCurso));
        Assert.False(await bd.Ingresos.GuardarTransicion(enCurso));
        var registrado = (await bd.Ingresos.Obtener(primero.Id, false))!;
        Assert.Equal(EstadoDeIngreso.Registrado, registrado.Estado);
        Assert.Equal(ingresoId, registrado.IngresoId);
        Assert.Equal("GR-001", registrado.Referencia);

        Assert.True(await bd.Ingresos.Agregar(Ingreso(edicion, 7)));
        Assert.Equal(2, (await bd.Ingresos.DeLaEdicion(edicion, 50)).Count);
        Assert.Equal([ingresoId], (await bd.Ingresos.IngresosYaConstatados([ingresoId, Guid.NewGuid()])).ToList());
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task El_reintento_actualiza_los_intentos_y_el_ultimo_intento_de_un_pendiente_y_un_ingreso_registrado_exige_su_identificador()
    {
        var obra = UnaObra("Reintentos", Impresa());
        await bd.Obras.Agregar(obra);
        var fila = Ingreso(obra.Ediciones[0].Id);
        await bd.Ingresos.Agregar(fila);

        var cargada = (await bd.Ingresos.Obtener(fila.Id, true))!;
        cargada.RegistrarReintento(Ahora.AddMinutes(10));
        Assert.True(await bd.Ingresos.GuardarTransicion(cargada));
        var tras = (await bd.Ingresos.Obtener(fila.Id, false))!;

        Assert.Equal(2, tras.Intentos);
        Assert.Equal(Ahora.AddMinutes(10), tras.UltimoIntentoEn);
        Assert.Equal(Ahora, tras.SolicitadoEn);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE catalogo.ingresos_solicitados SET estado = 'registrado', resultado_en = now() WHERE id = '{fila.Id}'"));
        Assert.Equal("23514", ex.SqlState);
    }
}
