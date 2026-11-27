using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Infrastructure.Pagos;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>T55 (PAG-26, PAG-53; AC-PAG-44): la columna del envío por sucursal sobre PostgreSQL real con el rol <c>rol_pagos</c>: ida y vuelta, reemplazo, desvinculación y una base anterior actualizada dos veces.</summary>
[Collection(ColeccionPostgresDePagos.Nombre)]
public sealed class EnvioPorSucursalDePagosPostgresTests(PostgresDePagosFixture bd) : IAsyncLifetime
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly EnvioDeSucursalDeIntencion[] DosSucursales = [new("ayacucho", Importe.De(30m)), new("cusco", Importe.De(18.50m))];

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static InstantaneaDeIntencion Instantanea(string huella, bool aDomicilio)
    {
        var linea = new LineaDeIntencion(Guid.NewGuid(), Guid.NewGuid(), false, "Ficciones", null, 2, Importe.De(50m), Importe.De(100m));
        var texto = new TextoAceptado("2026-11", Inicio.AddMinutes(-5));
        return aDomicilio
            ? InstantaneaDeIntencion.Crear(Cuenta, TipoDeEntrega.Domicilio, null, "AYA", [linea], Importe.De(48.50m), Importe.De(148.50m), huella, texto, texto, null, new ContactoDeEntrega("Ana", "987654321", "Jr. Lima 123"), DosSucursales)
            : InstantaneaDeIntencion.Crear(Cuenta, TipoDeEntrega.Recojo, null, null, [linea], Importe.Cero, Importe.De(100m), huella, texto, texto, null, null);
    }

    private static IntencionDePago Abierta(bool aDomicilio = true) => IntencionDePago.Crear(Guid.NewGuid(), Instantanea("huella-a", aDomicilio), Inicio + PlazosDelPago.VigenciaConImpresos, Inicio);

    private Task<T> En<T>(Func<CancellationToken, Task<T>> trabajo) => bd.Unidad.Ejecutar(trabajo);

    private async Task Agregar(IntencionDePago intencion) => await En(async token =>
    {
        await bd.Pagos.AgregarIntencion(intencion, token);
        return true;
    });

    private async Task Guardar(IntencionDePago intencion) => await En(async token =>
    {
        await bd.Pagos.GuardarIntencion(intencion, token);
        return true;
    });

    private async Task<IntencionDePago> Leer(Guid id) => (await En(token => bd.Pagos.ObtenerIntencion(id, paraActualizar: true, token)))!;

    [Fact]
    [Trait("Criterio", "AC-PAG-44")]
    public async Task El_detalle_se_guarda_y_se_lee_a_domicilio_y_queda_nulo_en_un_recojo()
    {
        var domicilio = Abierta();
        var recojo = Abierta(aDomicilio: false);

        await Agregar(domicilio);
        await Agregar(recojo);

        Assert.Equal(DosSucursales, (await Leer(domicilio.Id)).EnviosPorSucursal);
        Assert.Null((await Leer(recojo.Id)).EnviosPorSucursal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-44")]
    public async Task Reemplazar_con_y_sin_detalle_sobrescribe_la_columna_con_el_rol_de_pagos()
    {
        var intencion = Abierta();
        await Agregar(intencion);

        intencion.Reabrir();
        Assert.Equal(ResultadoDeCrear.Reemplazada, intencion.Reemplazar(Instantanea("huella-b", aDomicilio: false), Inicio + PlazosDelPago.VigenciaConImpresos, Inicio.AddMinutes(1)));
        await Guardar(intencion);
        Assert.Null((await Leer(intencion.Id)).EnviosPorSucursal);

        intencion.Reabrir();
        Assert.Equal(ResultadoDeCrear.Reemplazada, intencion.Reemplazar(Instantanea("huella-c", aDomicilio: true), Inicio + PlazosDelPago.VigenciaConImpresos, Inicio.AddMinutes(2)));
        await Guardar(intencion);
        Assert.Equal(DosSucursales, (await Leer(intencion.Id)).EnviosPorSucursal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-44")]
    public async Task Desvincular_borra_el_detalle_guardado()
    {
        var intencion = Abierta();
        await Agregar(intencion);

        intencion.Desvincular(Inicio.AddMinutes(1));
        await Guardar(intencion);

        Assert.Equal(1, await Contar("SELECT count(*) FROM pagos.intenciones WHERE envios_por_sucursal IS NULL AND desvinculada_en IS NOT NULL"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-44")]
    public async Task Una_base_anterior_gana_la_columna_al_migrar_dos_veces_sin_rellenar_sus_filas()
    {
        await Ejecutar("ALTER TABLE pagos.intenciones DROP COLUMN envios_por_sucursal");
        var anterior = Guid.NewGuid();
        await Ejecutar(
            $"""
            INSERT INTO pagos.intenciones (id, cuenta_id, estado, tiene_impresos, tipo_de_entrega, region_de_entrega, total, envio, iniciada_en, vence_en, limite_pendiente_en, creada_en)
            VALUES ('{anterior}', '{Cuenta}', 'abierta', true, 'domicilio', 'AYA', 112, 12, now(), now(), now(), now())
            """);

        for (var vez = 0; vez < 2; vez++)
        {
            await Ejecutar(DefinicionDePagos.Sql);
            await Ejecutar(DefinicionDePagos.Permisos);
        }

        var leida = await Leer(anterior);
        Assert.Null(leida.EnviosPorSucursal);
        Assert.Equal(Importe.De(12m), leida.Envio);
        var nueva = Abierta();
        await Agregar(nueva);
        await Guardar(nueva);
        Assert.Equal(DosSucursales, (await Leer(nueva.Id)).EnviosPorSucursal);
    }

    private async Task Ejecutar(string sql)
    {
        await using var comando = bd.Administracion.CreateCommand(sql);
        await comando.ExecuteNonQueryAsync();
    }

    private async Task<long> Contar(string sql)
    {
        await using var comando = bd.Administracion.CreateCommand(sql);
        return (long)(await comando.ExecuteScalarAsync())!;
    }
}
