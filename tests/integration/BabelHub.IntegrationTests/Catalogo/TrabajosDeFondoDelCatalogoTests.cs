using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using BabelHub.Ecommerce.Api.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;
using BabelHub.IntegrationTests.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using static BabelHub.IntegrationTests.Catalogo.ArchivosDeMuestra;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>
/// Los trabajos de fondo del Catálogo contra PostgreSQL y un almacén S3 reales, con un reloj controlado (T23; AC-CAT-12, AC-CAT-16): la depuración de archivos sin uso parte de las filas (objeto y luego fila, con margen mayor que una subida, <c>descarte</c> y purga de las
/// <c>consumida</c> sin tocar el objeto) y la anonimización a los 2 años (usuario, <c>nota</c>, <c>referencia</c>, <c>subido_por</c> y <c>usuario_id</c>) con su auditoría sin datos personales; ambos registran su corrida y fallan cerrado.
/// </summary>
[Collection(ColeccionDeLaApiDelCatalogo.Nombre)]
public sealed class TrabajosDeFondoDelCatalogoTests(PostgresDelCatalogoYAuditoriaFixture bd, AlmacenDeObjetosFixture almacen) : IAsyncLifetime
{
    private static readonly Guid Ana = Guid.Parse("a0000000-0000-0000-0000-0000000000a1");

    private CatalogoApiHost _host = null!;
    private int _isbn;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _host = await CatalogoApiHost.Crear(bd, almacen.Cliente);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private string NuevoIsbn()
    {
        var cuerpo = "979" + (++_isbn).ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        var suma = cuerpo.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return cuerpo + ((10 - (suma % 10)) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static TrabajoDeFondoDelCatalogo Trabajo(CatalogoApiHost host, string nombre) =>
        host.Servicios.GetRequiredService<IReadOnlyList<TrabajoDeFondoDelCatalogo>>().Single(t => t.Nombre == nombre);

    private async Task<bool> Existe(string contenedor, string clave)
    {
        try
        {
            await almacen.Cliente.GetObjectMetadataAsync(contenedor, clave);
            return true;
        }
        catch (AmazonS3Exception error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>Una fila de <c>archivos_pendientes</c> con su objeto en el almacén (si <paramref name="conObjeto"/>), creada hace <paramref name="antiguedad"/> y con el vencimiento dado.</summary>
    private async Task<string> Fila(string estado, TimeSpan antiguedad, TimeSpan? vence = null, bool conObjeto = true, string finalidad = "libro", TimeSpan? consumidaHace = null, Guid? usuario = null)
    {
        var clave = ClaveEnAlmacen.Nueva();
        if (conObjeto)
        {
            await almacen.Cliente.PutObjectAsync(new PutObjectRequest { BucketName = finalidad == "libro" ? CatalogoApiHost.Libros : CatalogoApiHost.Portadas, Key = clave, ContentBody = "contenido" });
        }

        var completa = estado is "lista" or "consumida";
        await bd.Ejecutar(
            $"""
            INSERT INTO catalogo.archivos_pendientes (id, usuario_id, finalidad, clave_en_almacen, estado, tipo_detectado, tamano, huella_sha256, creado_en, vence_en, consumido_en)
            VALUES ('{Guid.NewGuid()}', '{usuario ?? Ana}', '{finalidad}', '{clave}', '{estado}',
                    {(completa ? (finalidad == "libro" ? "'epub'" : "'png'") : "NULL")}, {(completa ? "100" : "NULL")}, {(completa ? "decode(repeat('ab', 32), 'hex')" : "NULL")},
                    now() - interval '{(long)antiguedad.TotalSeconds} seconds', now() + interval '{(long)(vence ?? TimeSpan.FromHours(24)).TotalSeconds} seconds',
                    {(estado == "consumida" ? $"now() - interval '{(long)(consumidaHace ?? antiguedad).TotalSeconds} seconds'" : "NULL")})
            """);
        return clave;
    }

    private async Task<bool> HayFila(string clave) => await bd.Consultar<long>($"SELECT count(*) FROM catalogo.archivos_pendientes WHERE clave_en_almacen = '{clave}'") == 1;

    // ---------- Depuración de archivos sin uso ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task La_depuracion_borra_el_objeto_y_luego_la_fila_de_lo_que_ya_no_sirve_y_deja_lo_reciente_o_vigente()
    {
        var subiendoVieja = await Fila("subiendo", TimeSpan.FromHours(2));
        var subiendoReciente = await Fila("subiendo", TimeSpan.FromMinutes(10));
        var listaVencida = await Fila("lista", TimeSpan.FromHours(30), vence: TimeSpan.FromHours(-6));
        var listaVigente = await Fila("lista", TimeSpan.FromHours(1), vence: TimeSpan.FromHours(23));
        var descarte = await Fila("descarte", TimeSpan.FromHours(1), finalidad: "portada");
        var sinObjeto = await Fila("descarte", TimeSpan.FromHours(1), conObjeto: false);

        var resultado = await Trabajo(_host, TrabajosDelCatalogo.DepuracionDeArchivos).EjecutarUnaVez();

        Assert.Equal(ResultadoDeTrabajo.Exito, resultado);
        foreach (var (clave, contenedor) in new[] { (subiendoVieja, CatalogoApiHost.Libros), (listaVencida, CatalogoApiHost.Libros), (descarte, CatalogoApiHost.Portadas) })
        {
            Assert.False(await HayFila(clave), "La fila debía quitarse.");
            Assert.False(await Existe(contenedor, clave), "El objeto debía borrarse.");
        }

        Assert.False(await HayFila(sinObjeto));
        foreach (var clave in new[] { subiendoReciente, listaVigente })
        {
            Assert.True(await HayFila(clave));
            Assert.True(await Existe(CatalogoApiHost.Libros, clave));
        }

        Assert.Contains((TrabajosDelCatalogo.DepuracionDeArchivos, ResultadoDeTrabajo.Exito), _host.RegistroDeTrabajos.Corridas);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Purgar_una_fila_consumida_a_los_7_dias_nunca_borra_el_objeto_que_ya_vive_en_la_obra_y_su_portada_se_sigue_sirviendo()
    {
        var portada = await _host.SubirYReferenciar("portada", Png(40, 40, 900), Ana);
        var libro = await _host.SubirYReferenciar("libro", Epub(), Ana);
        using var alta = await _host.Json(HttpMethod.Post, "/administracion/obras", new
        {
            titulo = "Con archivos",
            autor = "A",
            editorial = "E",
            categoria = "C",
            portadaRef = portada,
            ediciones = new object[] { new { formato = "digital", isbn = NuevoIsbn(), precio = 5m, licencia = new { tipo = "dominioPublico", titular = "DP", territorios = new[] { "PE" }, formato = "epub" }, archivoRef = libro } },
        }, usuario: Ana);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var clavePortada = (await bd.Consultar<string>("SELECT portada_clave FROM catalogo.obras"))!;
        var claveLibro = (await bd.Consultar<string>("SELECT clave_en_almacen FROM catalogo.archivos_digitales"))!;
        var huella = Convert.ToHexStringLower((await bd.Consultar<byte[]>("SELECT portada_huella FROM catalogo.obras"))!);

        // Antes de los 7 días las filas consumidas siguen.
        _host.Reloj.Adelantar(TimeSpan.FromDays(6));
        await Trabajo(_host, TrabajosDelCatalogo.DepuracionDeArchivos).EjecutarUnaVez();
        Assert.Equal(2, await bd.Consultar<long>("SELECT count(*) FROM catalogo.archivos_pendientes WHERE estado = 'consumida'"));

        _host.Reloj.Adelantar(TimeSpan.FromDays(2));
        var resultado = await Trabajo(_host, TrabajosDelCatalogo.DepuracionDeArchivos).EjecutarUnaVez();

        Assert.Equal(ResultadoDeTrabajo.Exito, resultado);
        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM catalogo.archivos_pendientes"));
        Assert.True(await Existe(CatalogoApiHost.Portadas, clavePortada));
        Assert.True(await Existe(CatalogoApiHost.Libros, claveLibro));
        using var imagen = await _host.Cliente.GetAsync($"/portadas/{huella}");
        Assert.Equal(HttpStatusCode.OK, imagen.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task El_reemplazo_de_una_portada_a_los_30_dias_deja_el_objeto_viejo_borrado_aunque_su_fila_consumida_ya_se_purgo()
    {
        var obra = await _host.RegistrarImpresa("Reemplazo", NuevoIsbn(), usuario: Ana);
        var obraId = obra.GetProperty("obraId").GetGuid();
        var viejaClave = (await bd.Consultar<string>("SELECT portada_clave FROM catalogo.obras"))!;

        // A los 30 días la fila consumida de la portada ya se purgó (a los 7); el reemplazo inserta una fila de descarte desde la obra.
        _host.Reloj.Adelantar(TimeSpan.FromDays(30));
        await Trabajo(_host, TrabajosDelCatalogo.DepuracionDeArchivos).EjecutarUnaVez();
        Assert.False(await HayFila(viejaClave));
        Assert.True(await Existe(CatalogoApiHost.Portadas, viejaClave));

        var nueva = await _host.SubirYReferenciar("portada", Png(41, 41, 1000), Ana);
        using var reemplazo = await _host.Json(HttpMethod.Put, $"/administracion/obras/{obraId}", new { version = obra.GetProperty("version").GetInt64(), titulo = "Reemplazo", autor = "Ana Quispe", editorial = "Editorial Andina", categoria = "Técnico", portadaRef = nueva }, usuario: Ana);
        Assert.Equal(HttpStatusCode.OK, reemplazo.StatusCode);
        Assert.Equal("descarte", await bd.Consultar<string>($"SELECT estado FROM catalogo.archivos_pendientes WHERE clave_en_almacen = '{viejaClave}'"));

        var resultado = await Trabajo(_host, TrabajosDelCatalogo.DepuracionDeArchivos).EjecutarUnaVez();

        Assert.Equal(ResultadoDeTrabajo.Exito, resultado);
        Assert.False(await Existe(CatalogoApiHost.Portadas, viejaClave));
        Assert.False(await HayFila(viejaClave));
        var claveNueva = (await bd.Consultar<string>("SELECT portada_clave FROM catalogo.obras"))!;
        Assert.NotEqual(viejaClave, claveNueva);
        Assert.True(await Existe(CatalogoApiHost.Portadas, claveNueva));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Si_el_almacen_no_responde_la_depuracion_falla_cerrada_deja_la_fila_para_la_corrida_siguiente_y_registra_el_fallo()
    {
        var descarte = await Fila("descarte", TimeSpan.FromHours(1));
        await using var sinAlmacen = await CatalogoApiHost.Crear(bd, new AmazonS3Client(
            new Amazon.Runtime.BasicAWSCredentials("acceso-de-pruebas-0123456789", "secreta-de-pruebas-0123456789-abcdefghijkl"),
            new AmazonS3Config { ServiceURL = "http://127.0.0.1:1", ForcePathStyle = true, MaxErrorRetry = 0, Timeout = TimeSpan.FromSeconds(5), AuthenticationRegion = "us-east-1" }),
            crearContenedores: false);

        var resultado = await Trabajo(sinAlmacen, TrabajosDelCatalogo.DepuracionDeArchivos).EjecutarUnaVez();

        Assert.Equal(ResultadoDeTrabajo.Fallo, resultado);
        Assert.True(await HayFila(descarte));
        Assert.True(await Existe(CatalogoApiHost.Libros, descarte));
        Assert.Contains((TrabajosDelCatalogo.DepuracionDeArchivos, ResultadoDeTrabajo.Fallo), sinAlmacen.RegistroDeTrabajos.Corridas);

        // Con el almacén de vuelta, la corrida siguiente termina lo pendiente.
        await Trabajo(_host, TrabajosDelCatalogo.DepuracionDeArchivos).EjecutarUnaVez();
        Assert.False(await HayFila(descarte));
        Assert.False(await Existe(CatalogoApiHost.Libros, descarte));
    }

    // ---------- Anonimización de ingresos a los 2 años ----------

    /// <summary>Un ingreso registrado con su usuario, referencia y nota, y su fecha retrasada <paramref name="antiguedad"/>.</summary>
    private async Task<Guid> IngresoAntiguo(TimeSpan antiguedad, Guid? usuario = null)
    {
        var alta = await _host.RegistrarImpresa("Con ingresos", NuevoIsbn(), usuario: usuario ?? Ana);
        var edicion = alta.GetProperty("edicionIds")[0].GetGuid();
        using var formulario = await _host.Json(HttpMethod.Get, $"/administracion/ediciones/{edicion}/ingresos/nuevo", usuario: usuario ?? Ana);
        var token = (await CatalogoApiHost.Leer(formulario)).GetProperty("token").GetString();
        using var ingreso = await _host.Json(HttpMethod.Post, $"/administracion/ediciones/{edicion}/ingresos", new { cantidad = 7, referencia = "GR-9", nota = "Recibido por Juan Pérez", token }, usuario: usuario ?? Ana);
        var id = (await CatalogoApiHost.Leer(ingreso)).GetProperty("ingresoSolicitadoId").GetGuid();
        var segundos = (long)antiguedad.TotalSeconds;
        await bd.Ejecutar($"UPDATE catalogo.ingresos_solicitados SET solicitado_en = now() - interval '{segundos} seconds', ultimo_intento_en = now() - interval '{segundos} seconds', resultado_en = now() - interval '{segundos} seconds' WHERE id = '{id}'");
        return id;
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task A_los_2_anios_se_anonimizan_el_usuario_la_nota_y_la_referencia_y_lo_reciente_no_se_toca_y_queda_una_auditoria_sin_datos_personales()
    {
        var antiguo = await IngresoAntiguo(TimeSpan.FromDays(731));
        var casiDosAnios = await IngresoAntiguo(TimeSpan.FromDays(728));
        var reciente = await IngresoAntiguo(TimeSpan.FromDays(10));
        var antes = await bd.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'IngresosSolicitadosAnonimizados'");

        var resultado = await Trabajo(_host, TrabajosDelCatalogo.AnonimizacionDeIngresos).EjecutarUnaVez();

        Assert.Equal(ResultadoDeTrabajo.Exito, resultado);
        Assert.Equal($"{Guid.Empty}||", await bd.Consultar<string>($"SELECT usuario_id || '|' || coalesce(referencia, '') || '|' || coalesce(nota, '') FROM catalogo.ingresos_solicitados WHERE id = '{antiguo}'"));
        Assert.Equal("registrado", await bd.Consultar<string>($"SELECT estado FROM catalogo.ingresos_solicitados WHERE id = '{antiguo}'"));
        foreach (var intacto in new[] { casiDosAnios, reciente })
        {
            Assert.Equal($"{Ana}|GR-9|Recibido por Juan Pérez", await bd.Consultar<string>($"SELECT usuario_id || '|' || referencia || '|' || nota FROM catalogo.ingresos_solicitados WHERE id = '{intacto}'"));
        }

        Assert.Equal(antes + 1, await bd.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'IngresosSolicitadosAnonimizados'"));
        var detalle = await bd.Consultar<string>("SELECT detalle::text FROM auditoria.registros WHERE accion = 'IngresosSolicitadosAnonimizados' ORDER BY secuencia DESC LIMIT 1");
        Assert.Contains("\"cantidad\"", detalle, StringComparison.Ordinal);
        Assert.DoesNotContain("GR-9", detalle, StringComparison.Ordinal);
        Assert.DoesNotContain("Juan", detalle, StringComparison.Ordinal);
        Assert.DoesNotContain(Ana.ToString(), detalle, StringComparison.Ordinal);
        Assert.Equal("sistema", await bd.Consultar<string>("SELECT actor_tipo FROM auditoria.registros WHERE accion = 'IngresosSolicitadosAnonimizados' ORDER BY secuencia DESC LIMIT 1"));
        Assert.Contains((TrabajosDelCatalogo.AnonimizacionDeIngresos, ResultadoDeTrabajo.Exito), _host.RegistroDeTrabajos.Corridas);

        // Una segunda corrida no encuentra nada que anonimizar y no deja otro registro.
        await Trabajo(_host, TrabajosDelCatalogo.AnonimizacionDeIngresos).EjecutarUnaVez();
        Assert.Equal(antes + 1, await bd.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'IngresosSolicitadosAnonimizados'"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Tambien_se_anonimizan_subido_por_de_los_libros_y_el_usuario_de_las_subidas_con_mas_de_2_anios_con_el_reloj_controlado()
    {
        var obra = await _host.RegistrarImpresa("Para el reloj", NuevoIsbn(), usuario: Ana);
        var portada = await _host.SubirYReferenciar("portada", Png(40, 40, 900), Ana);
        var libro = await _host.SubirYReferenciar("libro", Epub(), Ana);
        using var digital = await _host.Json(HttpMethod.Post, $"/administracion/obras/{obra.GetProperty("obraId").GetGuid()}/ediciones", new { version = obra.GetProperty("version").GetInt64(), edicion = new { formato = "digital", isbn = NuevoIsbn(), precio = 5m, licencia = new { tipo = "dominioPublico", titular = "DP", territorios = new[] { "PE" }, formato = "epub" }, archivoRef = libro } }, usuario: Ana);
        Assert.Equal(HttpStatusCode.Created, digital.StatusCode);
        Assert.NotEqual(Guid.Empty, portada);
        Assert.Equal(Ana, await bd.Consultar<Guid>("SELECT subido_por FROM catalogo.archivos_digitales"));

        // Dos años y un día después, con el reloj controlado.
        _host.Reloj.Adelantar(TimeSpan.FromDays(731));
        var resultado = await Trabajo(_host, TrabajosDelCatalogo.AnonimizacionDeIngresos).EjecutarUnaVez();

        Assert.Equal(ResultadoDeTrabajo.Exito, resultado);
        Assert.Equal(Guid.Empty, await bd.Consultar<Guid>("SELECT subido_por FROM catalogo.archivos_digitales"));
        Assert.Equal(0, await bd.Consultar<long>($"SELECT count(*) FROM catalogo.archivos_pendientes WHERE usuario_id = '{Ana}'"));
        Assert.True(await bd.Consultar<long>("SELECT count(*) FROM catalogo.archivos_pendientes WHERE usuario_id = '" + Guid.Empty + "'") >= 2);
        Assert.Equal("epub", await bd.Consultar<string>("SELECT tipo_detectado FROM catalogo.archivos_digitales"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Con_la_auditoria_caida_la_anonimizacion_falla_cerrada_no_toca_nada_y_registra_el_fallo()
    {
        var antiguo = await IngresoAntiguo(TimeSpan.FromDays(731));
        _host.AuditoriaCaida = true;

        var resultado = await Trabajo(_host, TrabajosDelCatalogo.AnonimizacionDeIngresos).EjecutarUnaVez();

        Assert.Equal(ResultadoDeTrabajo.Fallo, resultado);
        Assert.Equal($"{Ana}|GR-9|Recibido por Juan Pérez", await bd.Consultar<string>($"SELECT usuario_id || '|' || referencia || '|' || nota FROM catalogo.ingresos_solicitados WHERE id = '{antiguo}'"));
        Assert.Contains((TrabajosDelCatalogo.AnonimizacionDeIngresos, ResultadoDeTrabajo.Fallo), _host.RegistroDeTrabajos.Corridas);

        _host.AuditoriaCaida = false;
        Assert.Equal(ResultadoDeTrabajo.Exito, await Trabajo(_host, TrabajosDelCatalogo.AnonimizacionDeIngresos).EjecutarUnaVez());
        Assert.Equal($"{Guid.Empty}||", await bd.Consultar<string>($"SELECT usuario_id || '|' || coalesce(referencia, '') || '|' || coalesce(nota, '') FROM catalogo.ingresos_solicitados WHERE id = '{antiguo}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Un_pendiente_de_mas_de_2_anios_queda_sin_autor_y_solo_se_resuelve_conciliandolo()
    {
        _host.Inventario.IngresosCaidos = true;
        var alta = await _host.RegistrarImpresa("Pendiente viejo", NuevoIsbn(), usuario: Ana);
        var edicion = alta.GetProperty("edicionIds")[0].GetGuid();
        using var formulario = await _host.Json(HttpMethod.Get, $"/administracion/ediciones/{edicion}/ingresos/nuevo", usuario: Ana);
        var token = (await CatalogoApiHost.Leer(formulario)).GetProperty("token").GetString();
        using var ingreso = await _host.Json(HttpMethod.Post, $"/administracion/ediciones/{edicion}/ingresos", new { cantidad = 7, token }, usuario: Ana);
        var id = (await CatalogoApiHost.Leer(ingreso)).GetProperty("ingresoSolicitadoId").GetGuid();
        await bd.Ejecutar($"UPDATE catalogo.ingresos_solicitados SET solicitado_en = now() - interval '731 days', ultimo_intento_en = now() - interval '731 days' WHERE id = '{id}'");

        await Trabajo(_host, TrabajosDelCatalogo.AnonimizacionDeIngresos).EjecutarUnaVez();
        _host.Inventario.IngresosCaidos = false;
        using var reintento = await _host.Json(HttpMethod.Post, $"/administracion/ediciones/{edicion}/ingresos/{id}/reintentar", usuario: Ana);

        Assert.Equal("pendiente", await bd.Consultar<string>($"SELECT estado FROM catalogo.ingresos_solicitados WHERE id = '{id}'"));
        Assert.Equal(Guid.Empty, await bd.Consultar<Guid>($"SELECT usuario_id FROM catalogo.ingresos_solicitados WHERE id = '{id}'"));
        Assert.Equal(HttpStatusCode.Forbidden, reintento.StatusCode);
    }

    // ---------- Los trabajos ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public void Los_trabajos_tienen_nombres_estables_de_la_tabla_de_registro_y_corren_una_vez_al_dia()
    {
        var trabajos = _host.Servicios.GetRequiredService<IReadOnlyList<TrabajoDeFondoDelCatalogo>>();

        Assert.Equal([TrabajosDelCatalogo.DepuracionDeArchivos, TrabajosDelCatalogo.AnonimizacionDeIngresos], trabajos.Select(t => t.Nombre));
        Assert.All(trabajos, t => Assert.Matches("^[a-z][a-z0-9-]{0,99}$", t.Nombre));
        Assert.Equal(TimeSpan.FromDays(1), TrabajosDelCatalogo.Periodo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task El_trabajo_corre_como_sistema_y_no_deja_el_actor_fijado()
    {
        var actores = _host.Servicios.GetRequiredService<IActorActual>();

        await Trabajo(_host, TrabajosDelCatalogo.AnonimizacionDeIngresos).EjecutarUnaVez();

        Assert.Null(actores.Actual);
    }
}
