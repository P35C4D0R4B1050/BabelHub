using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Biblioteca;

/// <summary>Las depuraciones diarias y la purga de claves (T11; BIB-26, BIB-29, AC-BIB-23, AC-BIB-24).</summary>
public sealed class DepuracionesDeBibliotecaTests
{
    // Hoy en Lima: 2026-11-20 (15:00 UTC = 10:00 en Lima).
    private readonly EntornoDeBiblioteca _entorno = new(ActorDePrueba.DelSistema());

    private DepurarRegistrosDeDescargas Registros => new(_entorno.Actor, _entorno.Descargas, _entorno.Auditoria, _entorno.Unidad, _entorno.Reloj);

    private DepurarAyudas Ayudas => new(_entorno.Actor, _entorno.Descargas, _entorno.Auditoria, _entorno.Unidad, _entorno.Reloj);

    private RegistroDeDescarga Registro(Guid acceso, Guid cuenta, DateOnly dia) => new(Guid.NewGuid(), acceso, cuenta, Guid.NewGuid(), new DateTimeOffset(dia.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(-5)), dia);

    [Fact]
    [Trait("Criterio", "AC-BIB-23")]
    public async Task Un_registro_de_dos_anios_y_un_dia_queda_sin_acceso_ni_usuario_y_el_de_dos_anios_menos_un_dia_no_cambia()
    {
        var cuenta = Guid.NewGuid();
        var acceso = _entorno.SembrarLibro("Libro", cuenta: cuenta, descargas: 1);
        var viejo = Registro(acceso.Id, cuenta, new DateOnly(2024, 11, 19));
        var reciente = Registro(acceso.Id, cuenta, new DateOnly(2024, 11, 21));
        _entorno.Descargas.SembrarRegistro(viejo);
        _entorno.Descargas.SembrarRegistro(reciente);

        var resultado = await Registros.Ejecutar();

        Assert.Equal(1, resultado.Valor);
        var depurado = _entorno.Descargas.Registros.Single(r => r.Id == viejo.Id);
        Assert.True(depurado.EstaDepurado);
        Assert.Equal(viejo.EdicionId, depurado.EdicionId);
        Assert.Equal(viejo.Dia, depurado.Dia);
        Assert.Equal(reciente, _entorno.Descargas.Registros.Single(r => r.Id == reciente.Id));
        var entrada = Assert.Single(_entorno.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.DescargasDeBibliotecaDepuradas, entrada.Accion);
        Assert.Equal("1", entrada.Detalle["cantidad"]);
        Assert.Single(entrada.Detalle);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-23")]
    public async Task El_acceso_del_cliente_sigue_existiendo_y_utilizable_tras_depurar_su_registro()
    {
        var cuenta = Guid.NewGuid();
        var acceso = _entorno.SembrarLibro("Libro", cuenta: cuenta);
        _entorno.Descargas.SembrarRegistro(Registro(acceso.Id, cuenta, new DateOnly(2024, 1, 1)));

        await Registros.Ejecutar();

        var vivo = _entorno.Accesos.Buscar(acceso.Id)!;
        Assert.Equal(EstadoDeAcceso.Vigente, vivo.Estado);
        Assert.Equal(ResultadoDeConsumo.Consumida, vivo.ConsumirDescarga());
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-23")]
    public async Task Repetir_la_depuracion_no_cambia_nada_y_no_audita_otra_vez()
    {
        _entorno.Descargas.SembrarRegistro(Registro(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2024, 1, 1)));

        await Registros.Ejecutar();
        var segunda = await Registros.Ejecutar();

        Assert.Equal(0, segunda.Valor);
        Assert.Single(_entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-23")]
    public async Task Sin_poder_auditar_la_depuracion_no_hace_nada_ese_dia()
    {
        var registro = Registro(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2024, 1, 1));
        _entorno.Descargas.SembrarRegistro(registro);
        _entorno.Auditoria.Caida = true;

        var resultado = await Registros.Ejecutar();

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(registro, Assert.Single(_entorno.Descargas.Registros));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-24")]
    public async Task Una_ayuda_abierta_de_dos_anios_y_un_dia_se_elimina_y_una_cerrada_de_dos_anios_menos_un_dia_no_cambia()
    {
        var vieja = SolicitudDeAyuda.Reconstruir(Guid.NewGuid(), Guid.NewGuid(), MotivoDeAyuda.Otro, EstadoDeAyuda.Abierta, new DateTimeOffset(2024, 11, 19, 15, 0, 0, TimeSpan.Zero), null);
        var cerradaReciente = SolicitudDeAyuda.Reconstruir(Guid.NewGuid(), Guid.NewGuid(), MotivoDeAyuda.Otro, EstadoDeAyuda.Cerrada, new DateTimeOffset(2024, 11, 21, 15, 0, 0, TimeSpan.Zero), new DateTimeOffset(2024, 11, 22, 15, 0, 0, TimeSpan.Zero));
        var cerradaVieja = SolicitudDeAyuda.Reconstruir(Guid.NewGuid(), Guid.NewGuid(), MotivoDeAyuda.Otro, EstadoDeAyuda.Cerrada, new DateTimeOffset(2023, 1, 1, 15, 0, 0, TimeSpan.Zero), new DateTimeOffset(2023, 1, 2, 15, 0, 0, TimeSpan.Zero));
        foreach (var ayuda in new[] { vieja, cerradaReciente, cerradaVieja })
        {
            _entorno.Descargas.SembrarAyuda(ayuda);
        }

        var resultado = await Ayudas.Ejecutar();

        Assert.Equal(2, resultado.Valor);
        Assert.Equal(cerradaReciente.Id, Assert.Single(_entorno.Descargas.Ayudas).Id);
        var entrada = Assert.Single(_entorno.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.AyudasDeBibliotecaDepuradas, entrada.Accion);
        Assert.Equal("2", entrada.Detalle["cantidad"]);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-24")]
    public async Task Sin_poder_auditar_las_ayudas_no_se_eliminan_y_repetir_no_cambia_nada()
    {
        var vieja = SolicitudDeAyuda.Reconstruir(Guid.NewGuid(), Guid.NewGuid(), MotivoDeAyuda.Otro, EstadoDeAyuda.Abierta, new DateTimeOffset(2024, 1, 1, 15, 0, 0, TimeSpan.Zero), null);
        _entorno.Descargas.SembrarAyuda(vieja);
        _entorno.Auditoria.Caida = true;

        Assert.Equal(CategoriaDeError.NoDisponible, (await Ayudas.Ejecutar()).Error!.Categoria);
        Assert.Single(_entorno.Descargas.Ayudas);

        _entorno.Auditoria.Caida = false;
        await Ayudas.Ejecutar();
        Assert.Equal(0, (await Ayudas.Ejecutar()).Valor);
        Assert.Empty(_entorno.Descargas.Ayudas);
        Assert.Single(_entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-23")]
    public async Task Solo_el_sistema_depura_y_purga()
    {
        var cliente = new EntornoDeBiblioteca(ActorDePrueba.DeAdministradorCentral());

        Assert.Equal(CategoriaDeError.Prohibido, (await new DepurarRegistrosDeDescargas(cliente.Actor, cliente.Descargas, cliente.Auditoria, cliente.Unidad, cliente.Reloj).Ejecutar()).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, (await new DepurarAyudas(cliente.Actor, cliente.Descargas, cliente.Auditoria, cliente.Unidad, cliente.Reloj).Ejecutar()).Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, (await new PurgarClavesDeDescarga(cliente.Actor, cliente.Descargas, cliente.Reloj).Ejecutar()).Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-04")]
    public async Task La_purga_quita_las_claves_vencidas_hace_mas_de_una_hora_y_deja_las_demas()
    {
        var acceso = _entorno.SembrarLibro("Libro", cuenta: Guid.NewGuid());
        var ahora = _entorno.Reloj.Ahora;
        var clave = ClaveDeDescarga.Crear("0123456789abcdef0123456789abcdef")!;
        await _entorno.Descargas.RegistrarClave(new ClaveRegistrada(acceso.Id, clave, ahora.AddHours(-3), ahora.AddHours(-3).AddSeconds(300), Guid.NewGuid()), ahora.AddHours(-3));
        await _entorno.Descargas.RegistrarClave(new ClaveRegistrada(acceso.Id, ClaveDeDescarga.Crear("fedcba9876543210fedcba9876543210")!, ahora.AddMinutes(-10), ahora.AddMinutes(-5), Guid.NewGuid()), ahora.AddMinutes(-10));

        var resultado = await new PurgarClavesDeDescarga(_entorno.Actor, _entorno.Descargas, _entorno.Reloj).Ejecutar();

        Assert.Equal(1, resultado.Valor);
        Assert.Single(_entorno.Descargas.Claves);
        Assert.Empty(_entorno.Auditoria.Solicitudes);
    }

    [Theory]
    [InlineData("2026-11-20", "2024-11-20")]
    [InlineData("2026-02-28", "2024-02-29")] // el 29 de febrero de 2024 + 2 años = 28 de febrero de 2026
    [InlineData("2026-03-01", "2024-03-01")]
    [Trait("Criterio", "AC-BIB-23")]
    public void El_ultimo_dia_vencido_es_coherente_con_la_regla_de_vencimiento(string hoy, string esperado)
    {
        var limite = PoliticaDeRetencion.UltimoDiaVencido(DateOnly.Parse(hoy));

        Assert.Equal(DateOnly.Parse(esperado), limite);
        Assert.True(PoliticaDeRetencion.EstaVencido(limite, DateOnly.Parse(hoy)));
        Assert.False(PoliticaDeRetencion.EstaVencido(limite.AddDays(1), DateOnly.Parse(hoy)));
    }
}
