using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Domain.Biblioteca;

namespace BabelHub.Ecommerce.UnitTests.Biblioteca;

/// <summary>La parte de Biblioteca de la cancelación de la cuenta (T10; BIB-25, BIB-27, BIB-28, AC-BIB-22, AC-BIB-25).</summary>
public sealed class CancelacionDeLaCuentaTests
{
    private readonly EntornoDeBiblioteca _entorno = new();
    private readonly Guid _cuenta = Guid.NewGuid();

    private IAplicadorDeCancelacion Aplicador => new EliminarBibliotecaDeUnaCuenta(_entorno.Accesos);

    private (Acceso Vigente, Acceso Revocado) SembrarCuentaConHistorial(Guid cuenta)
    {
        var vigente = _entorno.SembrarLibro("Vigente", cuenta: cuenta, descargas: 2);
        var revocado = _entorno.SembrarLibro("Revocado", cuenta: cuenta, descargas: 1);
        revocado.Revocar(revocado.PedidoId, CausaDeRevocacion.Reembolso, _entorno.Reloj.Ahora);
        _entorno.Descargas.SembrarRegistro(new RegistroDeDescarga(Guid.NewGuid(), vigente.Id, cuenta, vigente.EdicionId, _entorno.Reloj.Ahora, new DateOnly(2026, 11, 20)));
        _entorno.Descargas.SembrarAyuda(SolicitudDeAyuda.Abrir(vigente.Id, MotivoDeAyuda.DescargasAgotadas, _entorno.Reloj.Ahora));
        return (vigente, revocado);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-22")]
    public async Task Cancelar_elimina_los_accesos_incluido_el_revocado_el_registro_y_las_ayudas_de_esa_cuenta_y_nada_mas()
    {
        var otra = Guid.NewGuid();
        SembrarCuentaConHistorial(_cuenta);
        var deLaOtra = _entorno.SembrarLibro("De otra", cuenta: otra);
        _entorno.Descargas.SembrarRegistro(new RegistroDeDescarga(Guid.NewGuid(), deLaOtra.Id, otra, deLaOtra.EdicionId, _entorno.Reloj.Ahora, new DateOnly(2026, 11, 20)));

        await _entorno.Unidad.Ejecutar(async ct =>
        {
            await Aplicador.Aplicar(_cuenta, Guid.NewGuid(), ct);
            return 0;
        });

        Assert.Equal(deLaOtra.Id, Assert.Single(_entorno.Accesos.Todos).Id);
        Assert.Equal(deLaOtra.Id, Assert.Single(_entorno.Descargas.Registros).AccesoId);
        Assert.Empty(_entorno.Descargas.Ayudas);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-22")]
    public async Task Cancelar_una_cuenta_sin_accesos_termina_sin_error_y_repetir_no_cambia_nada()
    {
        await Aplicador.Aplicar(_cuenta, Guid.NewGuid());
        await Aplicador.Aplicar(_cuenta, Guid.NewGuid());

        Assert.Empty(_entorno.Accesos.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-22")]
    public async Task Quien_se_registra_luego_con_el_mismo_correo_es_otra_cuenta_con_la_biblioteca_vacia()
    {
        SembrarCuentaConHistorial(_cuenta);
        await Aplicador.Aplicar(_cuenta, Guid.NewGuid());
        var nueva = new EntornoDeBiblioteca(BabelHub.Ecommerce.UnitTests.Soporte.ActorDePrueba.DeCliente());

        var lista = await nueva.Listar.Ejecutar(new ListarMiBibliotecaEntrada(1, 20));

        Assert.Equal(0, lista.Valor.Total);
        Assert.Empty(nueva.Descargas.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-25")]
    public async Task Si_no_puede_eliminar_el_registro_de_descargas_la_excepcion_revierte_todo_y_se_puede_reintentar()
    {
        var (vigente, revocado) = SembrarCuentaConHistorial(_cuenta);
        _entorno.Descargas.FallaAlEliminar = true;

        await Assert.ThrowsAsync<BibliotecaNoDisponibleException>(() => _entorno.Unidad.Ejecutar(async ct =>
        {
            await Aplicador.Aplicar(_cuenta, Guid.NewGuid(), ct);
            return 0;
        }));

        Assert.Equal(2, _entorno.Accesos.Todos.Count);
        Assert.NotNull(_entorno.Accesos.Buscar(vigente.Id));
        Assert.NotNull(_entorno.Accesos.Buscar(revocado.Id));
        Assert.Single(_entorno.Descargas.Registros);
        Assert.Single(_entorno.Descargas.Ayudas);

        _entorno.Descargas.FallaAlEliminar = false;
        await _entorno.Unidad.Ejecutar(async ct =>
        {
            await Aplicador.Aplicar(_cuenta, Guid.NewGuid(), ct);
            return 0;
        });

        Assert.Empty(_entorno.Accesos.Todos);
        Assert.Empty(_entorno.Descargas.Registros);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-26")]
    public async Task Tras_cancelar_una_solicitud_de_enlace_no_encuentra_el_acceso_y_no_escribe_ningun_registro()
    {
        var entorno = new EntornoDeBiblioteca(BabelHub.Ecommerce.UnitTests.Soporte.ActorDePrueba.DeCliente(_cuenta));
        var propio = entorno.SembrarLibro("Propio");
        entorno.Accesos.DespuesDeLeerUnAcceso = () =>
        {
            entorno.Accesos.EliminarDeLaCuenta(_cuenta).GetAwaiter().GetResult();
            entorno.Accesos.DespuesDeLeerUnAcceso = null;
        };

        var resultado = await entorno.Descarga.SolicitarEnlace(propio.Id, "0123456789abcdef0123456789abcdef");

        Assert.Equal(ClavesDeBiblioteca.NoEncontrado, resultado.Error!.Clave);
        Assert.Empty(entorno.Descargas.Registros);
        Assert.Empty(entorno.Descargas.Claves);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-25")]
    public async Task El_aplicador_se_llama_biblioteca_y_rechaza_una_cuenta_vacia()
    {
        Assert.Equal("biblioteca", Aplicador.Modulo);
        await Assert.ThrowsAsync<ArgumentException>(() => Aplicador.Aplicar(Guid.Empty, Guid.NewGuid()));
    }
}
