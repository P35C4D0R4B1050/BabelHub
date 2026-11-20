using System.Net;
using System.Text;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Biblioteca;

namespace BabelHub.IntegrationTests.Biblioteca;

/// <summary>
/// La descarga de punta a punta con PostgreSQL real y un almacén compatible con S3 real, con el reloj controlado (T22; BIB-04, BIB-05, BIB-06, BIB-08, BIB-11, BIB-13, BIB-14, BIB-15, BIB-18, AC-BIB-04, 05, 06, 08, 11, 12, 13): el enlace entregado descarga el archivo del almacén, vence a los
/// 300 segundos, la misma clave dentro de su vigencia da el mismo enlace sin contar, a los 6 minutos cuenta una descarga nueva, el límite de 10 por hora responde sin consumir y una edición con la licencia vencida se descarga igual.
/// </summary>
public sealed class DescargaConAlmacenRealTests(PostgresDeBibliotecaFixture bd, AlmacenDeLibrosFixture almacen) : IClassFixture<PostgresDeBibliotecaFixture>, IClassFixture<AlmacenDeLibrosFixture>, IAsyncLifetime
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false });

    private EntornoIntegradoDeBiblioteca _entorno = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _entorno = new EntornoIntegradoDeBiblioteca(bd, new S3EntregaDeArchivos(almacen.Cliente, almacen.OpcionesDeLibros), DateTimeOffset.UtcNow);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private const string Clave = "la-clave-de-un-solo-uso-0123456789";

    private async Task<(Guid Cuenta, Guid Acceso, byte[] Contenido)> Comprar(string titulo = "Libro de prueba", EstadoDeEdicionConsultado estado = EstadoDeEdicionConsultado.Activa, MotivoDeNoDisponibleDeEdicion? motivo = null)
    {
        var contenido = Encoding.UTF8.GetBytes("contenido sintético " + Guid.NewGuid());
        var claveEnAlmacen = await almacen.GuardarSintetico(contenido);
        var cuenta = Guid.NewGuid();
        var (acceso, _, _) = await _entorno.Comprar(cuenta, claveEnAlmacen, titulo, estado, motivo);
        return (cuenta, acceso, contenido);
    }

    private async Task<long> Usadas(Guid acceso) => await bd.Consultar<long>($"SELECT descargas_usadas::bigint FROM biblioteca.accesos WHERE id = '{acceso}'");

    [Fact]
    [Trait("Criterio", "AC-BIB-12")]
    public async Task El_enlace_entregado_descarga_el_archivo_directamente_del_almacen_con_attachment_y_vence_a_los_trescientos_segundos()
    {
        var (cuenta, acceso, contenido) = await Comprar("Cien años de soledad");
        _entorno.Reloj.Ahora = DateTimeOffset.UtcNow.AddSeconds(-2);

        var enlace = await _entorno.Pedir(cuenta, acceso, Clave);

        Assert.True(enlace.EsExito, enlace.Error?.Clave);
        Assert.Equal(almacen.Opciones.Endpoint.Authority, enlace.Valor.Url.Authority);
        var firmadoEn = new DateTimeOffset(_entorno.Reloj.Ahora.UtcTicks - (_entorno.Reloj.Ahora.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
        Assert.Equal(300, (enlace.Valor.VenceEn - firmadoEn).TotalSeconds);
        using var respuesta = await Http.GetAsync(enlace.Valor.Url);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(contenido, await respuesta.Content.ReadAsByteArrayAsync());
        Assert.StartsWith("attachment", respuesta.Content.Headers.ContentDisposition?.ToString(), StringComparison.Ordinal);
        Assert.Equal(1L, await Usadas(acceso));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-06")]
    public async Task Un_enlace_emitido_hace_diez_minutos_no_completa_la_descarga_y_la_solicitud_de_otro_cliente_se_rechaza()
    {
        var (cuenta, acceso, _) = await Comprar();
        _entorno.Reloj.Ahora = DateTimeOffset.UtcNow.AddMinutes(-10);
        var vencido = await _entorno.Pedir(cuenta, acceso, Clave);

        using var respuesta = await Http.GetAsync(vencido.Valor.Url);
        var ajeno = await _entorno.Pedir(Guid.NewGuid(), acceso, Clave);

        Assert.True(respuesta.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest);
        Assert.Equal(ClavesDeBiblioteca.NoEncontrado, ajeno.Error!.Clave);
        Assert.Equal(1, _entorno.Denegaciones.Total);
        Assert.Equal(1L, await Usadas(acceso));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-04")]
    public async Task La_misma_clave_da_el_mismo_enlace_sin_contar_a_los_seis_minutos_cuenta_una_nueva_y_cien_repeticiones_no_superan_el_primer_vencimiento()
    {
        var (cuenta, acceso, _) = await Comprar();
        var inicio = new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
        _entorno.Reloj.Ahora = inicio;
        var primero = await _entorno.Pedir(cuenta, acceso, Clave);

        for (var i = 1; i <= 100; i++)
        {
            _entorno.Reloj.Ahora = inicio.AddSeconds(2 * i);
            var repeticion = await _entorno.Pedir(cuenta, acceso, Clave);
            Assert.Equal(primero.Valor.Url, repeticion.Valor.Url);
            Assert.Equal(primero.Valor.VenceEn, repeticion.Valor.VenceEn);
        }

        Assert.Equal(1L, await Usadas(acceso));
        _entorno.Reloj.Ahora = inicio.AddMinutes(6);
        var nuevo = await _entorno.Pedir(cuenta, acceso, Clave);

        Assert.NotEqual(primero.Valor.Url, nuevo.Valor.Url);
        Assert.Equal(2L, await Usadas(acceso));
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.descargas"));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-08")]
    public async Task Una_descarga_real_de_una_edicion_con_la_licencia_vencida_o_retirada_de_la_venta_funciona_igual()
    {
        var (cuenta, acceso, contenido) = await Comprar("Libro retirado", EstadoDeEdicionConsultado.RetiradaDeVenta, MotivoDeNoDisponibleDeEdicion.LicenciaVencida);
        _entorno.Reloj.Ahora = DateTimeOffset.UtcNow.AddSeconds(-2);

        var enlace = await _entorno.Pedir(cuenta, acceso, Clave);
        using var respuesta = await Http.GetAsync(enlace.Valor.Url);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(contenido, await respuesta.Content.ReadAsByteArrayAsync());
        var lista = await _entorno.ComoCliente(cuenta, () => _entorno.Listar.Ejecutar(new ListarMiBibliotecaEntrada(1, 20)));
        Assert.Equal("Libro retirado", Assert.Single(lista.Valor.Accesos).Titulo);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-11")]
    public async Task Si_el_archivo_falta_en_el_almacen_no_se_consume_una_descarga_se_alerta_y_se_ofrece_reportarlo()
    {
        var cuenta = Guid.NewGuid();
        var (acceso, _, _) = await _entorno.Comprar(cuenta, new string('f', 32));

        var resultado = await _entorno.Pedir(cuenta, acceso, Clave);

        Assert.Equal(ClavesDeBiblioteca.ArchivoNoDisponible, resultado.Error!.Clave);
        Assert.Equal(0L, await Usadas(acceso));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM biblioteca.descargas"));
        Assert.Equal(ClavesDeBiblioteca.AlertaArchivoFaltante, Assert.Single(_entorno.Alertas.Alertas).Motivo);
        var ayuda = await _entorno.ComoCliente(cuenta, () => _entorno.PedirAyuda.Ejecutar(new PedirAyudaEntrada(acceso, "archivo-no-disponible")));
        Assert.True(ayuda.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-13")]
    public async Task El_enlace_once_en_una_hora_se_rechaza_con_la_respuesta_de_limite_sin_consumir_y_con_los_contadores_caidos_falla_cerrado()
    {
        var (cuenta, acceso, _) = await Comprar();
        await bd.Ejecutar($"UPDATE biblioteca.accesos SET descargas_usadas = 0 WHERE id = '{acceso}'");
        _entorno.Limites.Maximo = 10;
        for (var i = 0; i < 10; i++)
        {
            var r = await _entorno.Pedir(cuenta, acceso, $"clave-numero-{i:D4}-0000000000");
            if (i < 5)
            {
                Assert.True(r.EsExito, r.Error?.Clave);
            }
        }

        var antes = await Usadas(acceso);
        var undecimo = await _entorno.Pedir(cuenta, acceso, "clave-numero-0011-0000000000");

        Assert.Equal(CategoriaDeError.Limite, undecimo.Error!.Categoria);
        Assert.Equal(antes, await Usadas(acceso));

        _entorno.Limites.Caido = true;
        var sinContadores = await _entorno.Pedir(cuenta, acceso, "clave-numero-0012-0000000000");
        Assert.Equal(CategoriaDeError.NoDisponible, sinContadores.Error!.Categoria);
        Assert.Equal(antes, await Usadas(acceso));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-13")]
    public async Task La_misma_clave_contra_dos_accesos_propios_cuenta_dos_veces_para_el_limite_y_repetirla_contra_el_mismo_no_cuenta()
    {
        var (cuenta, acceso1, _) = await Comprar("Uno");
        var contenido = Encoding.UTF8.GetBytes("otro");
        var (acceso2, _, _) = await _entorno.Comprar(cuenta, await almacen.GuardarSintetico(contenido), "Dos");

        await _entorno.Pedir(cuenta, acceso1, Clave);
        await _entorno.Pedir(cuenta, acceso1, Clave);
        Assert.Equal(1, _entorno.Limites.Usadas("enlaces-de-descarga-por-cuenta"));

        await _entorno.Pedir(cuenta, acceso2, Clave);
        Assert.Equal(2, _entorno.Limites.Usadas("enlaces-de-descarga-por-cuenta"));
    }
}
