using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>El limitador de subidas (T17; CAT-16, SEG-07, SEG-08): 4 simultáneas, 2 por usuario, duración máxima y liberación del cupo en cualquier desenlace.</summary>
public sealed class LimitadorDeSubidasTests
{
    private static readonly Guid Ana = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Beto = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static byte[] Epub()
    {
        var datos = new byte[200];
        new byte[] { 0x50, 0x4B, 0x03, 0x04 }.CopyTo(datos, 0);
        return datos;
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Un_usuario_no_pasa_de_dos_subidas_y_el_proceso_no_pasa_de_cuatro()
    {
        var limitador = new LimitadorDeSubidas();

        using var a1 = limitador.Intentar(Ana);
        using var a2 = limitador.Intentar(Ana);
        var a3 = limitador.Intentar(Ana);
        using var b1 = limitador.Intentar(Beto);
        using var b2 = limitador.Intentar(Beto);
        var quinta = limitador.Intentar(Guid.NewGuid());

        Assert.NotNull(a1);
        Assert.NotNull(a2);
        Assert.Null(a3);
        Assert.NotNull(b1);
        Assert.NotNull(b2);
        Assert.Null(quinta);
        Assert.Equal(4, limitador.EnCurso);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Liberar_un_permiso_devuelve_el_cupo_una_sola_vez_aunque_se_libere_dos_veces()
    {
        var limitador = new LimitadorDeSubidas(maximoGlobal: 2, maximoPorUsuario: 1);
        var a = limitador.Intentar(Ana)!;
        using var b = limitador.Intentar(Beto);

        Assert.Null(limitador.Intentar(Ana));
        a.Dispose();
        a.Dispose();

        Assert.Equal(1, limitador.EnCurso);
        using var otra = limitador.Intentar(Ana);
        Assert.NotNull(otra);
        Assert.Equal(2, limitador.EnCurso);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task El_permiso_se_cancela_al_cumplirse_la_duracion_maxima()
    {
        var limitador = new LimitadorDeSubidas(duracionMaxima: TimeSpan.FromMilliseconds(30));
        using var permiso = limitador.Intentar(Ana)!;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(Timeout.Infinite, permiso.Cancelacion));

        Assert.True(permiso.Cancelacion.IsCancellationRequested);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Los_topes_deben_ser_positivos()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LimitadorDeSubidas(maximoGlobal: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LimitadorDeSubidas(maximoPorUsuario: 0));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Sin_cupo_la_subida_se_rechaza_como_limite_antes_de_tocar_la_base_o_el_almacen()
    {
        var e = new EntornoDelCatalogo();
        var usuario = e.Actor.Actual!.UsuarioId!.Value;
        using var uno = e.Limitador.Intentar(usuario);
        using var dos = e.Limitador.Intentar(usuario);

        var resultado = await e.Archivos().SubirArchivo(new SubirArchivoEntrada(FinalidadDeLaSubida.Libro, new MemoryStream(Epub())));

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.Limite, resultado.Error!.Categoria);
        Assert.Equal(ClavesDelCatalogo.LimiteDeSubidas, resultado.Error!.Clave);
        Assert.Equal(0, e.Almacen.Escrituras);
        Assert.Empty(e.Referencias.Todas);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Quien_no_es_de_la_administracion_central_se_rechaza_sin_consumir_cupo()
    {
        var e = new EntornoDelCatalogo(ActorDePrueba.DeCliente());

        var resultado = await e.Archivos().SubirArchivo(new SubirArchivoEntrada(FinalidadDeLaSubida.Libro, new MemoryStream(Epub())));

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
        Assert.Equal(0, e.Limitador.EnCurso);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task El_cupo_se_devuelve_al_terminar_la_subida_con_exito_o_con_rechazo()
    {
        var e = new EntornoDelCatalogo();

        var buena = await e.Archivos().SubirArchivo(new SubirArchivoEntrada(FinalidadDeLaSubida.Libro, new MemoryStream(Epub())));
        var mala = await e.Archivos().SubirArchivo(new SubirArchivoEntrada(FinalidadDeLaSubida.Libro, new MemoryStream("MZ ejecutable"u8.ToArray())));
        e.Almacen.NoResponde = true;
        var caida = await e.Archivos().SubirArchivo(new SubirArchivoEntrada(FinalidadDeLaSubida.Libro, new MemoryStream(Epub())));

        Assert.True(buena.EsExito);
        Assert.False(mala.EsExito);
        Assert.Equal(CategoriaDeError.NoDisponible, caida.Error!.Categoria);
        Assert.Equal(0, e.Limitador.EnCurso);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Una_subida_que_supera_la_duracion_maxima_se_corta_se_descarta_y_es_un_limite()
    {
        var e = new EntornoDelCatalogo { Limitador = new LimitadorDeSubidas(duracionMaxima: TimeSpan.FromMilliseconds(30)) };
        var almacen = new AlmacenQueNuncaTermina();
        var subir = new SubirArchivo(e.Autorizador, new ValidadorDeSubirArchivo(), e.Referencias, almacen, e.Unidad, e.Reloj, e.Limitador);

        var resultado = await subir.Ejecutar(new SubirArchivoEntrada(FinalidadDeLaSubida.Libro, new MemoryStream(Epub())));

        Assert.False(resultado.EsExito);
        Assert.Equal(CategoriaDeError.Limite, resultado.Error!.Categoria);
        Assert.Equal(ClavesDelCatalogo.LimiteDeSubidas, resultado.Error!.Clave);
        Assert.Single(almacen.Borrados);
        Assert.Empty(e.Referencias.Todas);
        Assert.Equal(0, e.Limitador.EnCurso);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Si_cancela_el_llamador_la_cancelacion_se_propaga_y_el_cupo_se_devuelve()
    {
        var e = new EntornoDelCatalogo();
        var almacen = new AlmacenQueNuncaTermina();
        var subir = new SubirArchivo(e.Autorizador, new ValidadorDeSubirArchivo(), e.Referencias, almacen, e.Unidad, e.Reloj, e.Limitador);
        using var cancelacion = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => subir.Ejecutar(new SubirArchivoEntrada(FinalidadDeLaSubida.Libro, new MemoryStream(Epub())), cancelacion.Token));

        Assert.Equal(0, e.Limitador.EnCurso);
    }

    private sealed class AlmacenQueNuncaTermina : IAlmacenDeArchivos
    {
        public List<string> Borrados { get; } = [];

        public async Task<ResultadoDeEscrituraEnAlmacen> Escribir(string claveEnAlmacen, FinalidadDeArchivo finalidad, Stream contenido, CancellationToken cancelacion = default)
        {
            await Task.Delay(Timeout.Infinite, cancelacion);
            throw new InvalidOperationException("No se alcanza.");
        }

        public Task<Stream?> AbrirPortada(string claveEnAlmacen, CancellationToken cancelacion = default) => Task.FromResult<Stream?>(null);

        public Task Borrar(string claveEnAlmacen, CancellationToken cancelacion = default)
        {
            Borrados.Add(claveEnAlmacen);
            return Task.CompletedTask;
        }
    }
}
