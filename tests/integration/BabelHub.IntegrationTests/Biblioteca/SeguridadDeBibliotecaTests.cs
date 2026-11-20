using System.Text;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;
using BabelHub.Ecommerce.Infrastructure.Biblioteca;

namespace BabelHub.IntegrationTests.Biblioteca;

/// <summary>
/// La seguridad de Biblioteca con PostgreSQL real y el almacén real (T24; BIB-03, BIB-07, BIB-12, SEG-09, SEG-10, regla 11): la matriz de la biblioteca y de la administración por rol, la respuesta idéntica a un acceso ajeno y a uno inexistente con un evento por intento, y la siembra de que
/// la URL firmada, la clave de un solo uso, la credencial del almacén y la clave de objeto del archivo no aparecen en la base, la auditoría, las alertas ni los errores.
/// </summary>
public sealed class SeguridadDeBibliotecaTests(PostgresDeBibliotecaFixture bd, AlmacenDeLibrosFixture almacen) : IClassFixture<PostgresDeBibliotecaFixture>, IClassFixture<AlmacenDeLibrosFixture>, IAsyncLifetime
{
    private const string Clave = "centinela-de-la-clave-de-un-solo-uso-9f3a";

    private EntornoIntegradoDeBiblioteca _entorno = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _entorno = new EntornoIntegradoDeBiblioteca(bd, new S3EntregaDeArchivos(almacen.Cliente, almacen.OpcionesDeLibros), DateTimeOffset.UtcNow);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid Cuenta, Guid Acceso, string ClaveDeObjeto)> Comprar(string titulo = "Libro de prueba")
    {
        var claveDeObjeto = await almacen.GuardarSintetico(Encoding.UTF8.GetBytes("sintético"));
        var cuenta = Guid.NewGuid();
        var (acceso, _, _) = await _entorno.Comprar(cuenta, claveDeObjeto, titulo);
        return (cuenta, acceso, claveDeObjeto);
    }

    private Task<T> Como<T>(string rol, Func<Task<T>> trabajo)
    {
        _entorno.Actor.Establecer(rol switch
        {
            "anonimo" => ActorActual.Anonimo(),
            "sistema" => ActorActual.DelSistema(),
            _ => ActorActual.DeUsuario(Guid.NewGuid(), rol, rol == "encargado-de-sucursal" ? Guid.NewGuid() : null),
        });
        return trabajo();
    }

    [Theory]
    [Trait("Criterio", "AC-BIB-10")]
    [InlineData("cliente", true)]
    [InlineData("encargado-de-sucursal", false)]
    [InlineData("personal-de-logistica", false)]
    [InlineData("administrador-central", false)]
    [InlineData("anonimo", false)]
    public async Task Solo_un_cliente_ve_su_biblioteca_y_cada_intento_de_otro_rol_deja_un_evento(string rol, bool permitido)
    {
        var (cuenta, _, _) = await Comprar();
        var antes = _entorno.Denegaciones.Total;

        var resultado = await Task.Run(() => rol == "cliente"
            ? _entorno.ComoCliente(cuenta, () => _entorno.Listar.Ejecutar(new ListarMiBibliotecaEntrada(1, 20)))
            : Como(rol, () => _entorno.Listar.Ejecutar(new ListarMiBibliotecaEntrada(1, 20))));

        Assert.Equal(permitido, resultado.EsExito);
        Assert.Equal(permitido ? 0 : 1, _entorno.Denegaciones.Total - antes);
        if (!permitido)
        {
            Assert.Equal(rol == "anonimo" ? CategoriaDeError.NoAutenticado : CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        }
    }

    [Theory]
    [Trait("Criterio", "AC-BIB-10")]
    [InlineData("cliente", false)]
    [InlineData("encargado-de-sucursal", false)]
    [InlineData("personal-de-logistica", false)]
    [InlineData("administrador-central", true)]
    [InlineData("anonimo", false)]
    public async Task Solo_la_administracion_central_consulta_la_biblioteca_de_un_cliente_restituye_y_lista_ayudas(string rol, bool permitido)
    {
        var (cuenta, acceso, _) = await Comprar();
        await _entorno.Pedir(cuenta, acceso, Clave);
        var antes = _entorno.Denegaciones.Total;

        var consulta = await Task.Run(() => Como(rol, () => _entorno.Consulta.Ejecutar(cuenta)));
        var ayudas = await Task.Run(() => Como(rol, () => _entorno.ListarAyudas.Ejecutar(new ListarAyudasEntrada(null, 1, 20))));
        var restitucion = await Task.Run(() => Como(rol, () => _entorno.Restituir.Ejecutar(new RestituirDescargasEntrada(acceso, 1, "otro"))));

        Assert.Equal(permitido, consulta.EsExito);
        Assert.Equal(permitido, ayudas.EsExito);
        Assert.Equal(permitido, restitucion.EsExito);
        Assert.Equal(permitido ? 0 : 3, _entorno.Denegaciones.Total - antes);
        Assert.Equal(permitido ? 0L : 1L, await bd.Consultar<long>($"SELECT descargas_usadas::bigint FROM biblioteca.accesos WHERE id = '{acceso}'"));
        Assert.Equal(permitido ? 2 : 0, _entorno.Auditoria.Registradas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-03")]
    public async Task Un_acceso_ajeno_y_uno_inexistente_responden_exactamente_lo_mismo_y_dejan_un_evento_cada_uno_sin_tocar_nada()
    {
        var (_, ajeno, _) = await Comprar();
        var otro = Guid.NewGuid();
        var antes = _entorno.Denegaciones.Total;

        var deAjeno = await _entorno.Pedir(otro, ajeno, Clave);
        var inexistente = await _entorno.Pedir(otro, Guid.NewGuid(), Clave);
        var ayudaAjena = await _entorno.ComoCliente(otro, () => _entorno.PedirAyuda.Ejecutar(new PedirAyudaEntrada(ajeno, "otro")));
        var ayudaInexistente = await _entorno.ComoCliente(otro, () => _entorno.PedirAyuda.Ejecutar(new PedirAyudaEntrada(Guid.NewGuid(), "otro")));
        var verAjeno = await _entorno.ComoCliente(otro, () => new ObtenerAcceso(_entorno.Autorizador, bd.Accesos, _entorno.Proyector).Ejecutar(ajeno));
        var verInexistente = await _entorno.ComoCliente(otro, () => new ObtenerAcceso(_entorno.Autorizador, bd.Accesos, _entorno.Proyector).Ejecutar(Guid.NewGuid()));

        foreach (var (a, b) in new[] { (deAjeno.Error, inexistente.Error), (ayudaAjena.Error, ayudaInexistente.Error), (verAjeno.Error, verInexistente.Error) })
        {
            Assert.Equal((b!.Categoria, b.Clave, b.Errores.Count), (a!.Categoria, a.Clave, a.Errores.Count));
            Assert.Equal(ClavesDeBiblioteca.NoEncontrado, a.Clave);
        }

        Assert.Equal(6, _entorno.Denegaciones.Total - antes);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.descargas"));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.solicitudes_de_ayuda"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-12")]
    public async Task La_url_firmada_la_clave_de_un_solo_uso_la_credencial_y_la_clave_de_objeto_no_aparecen_en_la_base_la_auditoria_las_alertas_ni_los_errores()
    {
        var (cuenta, acceso, claveDeObjeto) = await Comprar("Título público");
        _entorno.Reloj.Ahora = DateTimeOffset.UtcNow.AddSeconds(-2);

        var enlace = await _entorno.Pedir(cuenta, acceso, Clave);
        var vencida = await _entorno.Pedir(cuenta, acceso, "otra-clave-de-un-solo-uso-0123456789");
        await _entorno.ComoCliente(cuenta, () => _entorno.PedirAyuda.Ejecutar(new PedirAyudaEntrada(acceso, "descargas-agotadas")));
        await _entorno.ComoAdministracion(() => _entorno.Consulta.Ejecutar(cuenta));
        await _entorno.ComoAdministracion(() => _entorno.Restituir.Ejecutar(new RestituirDescargasEntrada(acceso, 1, "descarga-fallida")));
        var faltante = await _entorno.Pedir(cuenta, (await ComprarSinArchivo(cuenta)), Clave);

        var firma = System.Web.HttpUtility.ParseQueryString(enlace.Valor.Url.Query)["X-Amz-Signature"]!;
        var secretos = new[] { Clave, firma, AlmacenDeLibrosFixture.ClaveSecreta, claveDeObjeto, enlace.Valor.Url.Query, "X-Amz-Signature", "X-Amz-Credential" };

        // (1) La base: todas las tablas del módulo como texto.
        var volcado = new StringBuilder();
        foreach (var tabla in new[] { "accesos", "descargas", "claves_de_descarga", "solicitudes_de_ayuda" })
        {
            volcado.AppendLine(await bd.Consultar<string>($"SELECT coalesce(string_agg(t::text, E'\\n'), '') FROM biblioteca.{tabla} t"));
        }

        // (2) La auditoría, las alertas, los errores y el texto de los resultados.
        var auditoria = string.Join('|', _entorno.Auditoria.Registradas.Select(r => $"{r.Accion.Identificador}{r.ObjetoTipo}{r.ObjetoId}{r.Motivo}{string.Join(',', r.Detalle.Select(d => d.Key + d.Value))}"));
        var alertas = string.Join('|', _entorno.Alertas.Alertas.Select(a => a.ToString()));
        var errores = string.Join('|', new[] { vencida, faltante }.Where(r => !r.EsExito).Select(r => $"{r.Error!.Categoria}{r.Error.Clave}{string.Join(',', r.Error.Errores.Select(e => e.Campo + e.Clave))}"));
        var textoDelEnlace = enlace.Valor.ToString() + enlace.ToString();

        foreach (var (nombre, texto) in new[] { ("base", volcado.ToString()), ("auditoría", auditoria), ("alertas", alertas), ("errores", errores), ("texto del enlace", textoDelEnlace) })
        {
            foreach (var secreto in secretos)
            {
                Assert.DoesNotContain(secreto, texto, StringComparison.Ordinal);
            }

            Assert.False(texto.Contains("https://", StringComparison.OrdinalIgnoreCase) && nombre != "base", $"Un enlace salió en {nombre}.");
        }

        Assert.True(await bd.Consultar<long>("SELECT count(*) FROM biblioteca.claves_de_descarga WHERE octet_length(clave_hash) = 32") >= 1, "La clave se guarda solo como resumen de 32 bytes.");
    }

    private async Task<Guid> ComprarSinArchivo(Guid cuenta)
    {
        var (acceso, _, _) = await _entorno.Comprar(cuenta, new string('e', 32), "Sin archivo");
        return acceso;
    }
}
