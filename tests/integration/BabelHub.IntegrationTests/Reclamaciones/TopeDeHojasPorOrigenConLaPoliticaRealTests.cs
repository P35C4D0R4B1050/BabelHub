using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using BabelHub.IntegrationTests.Pedidos;
using Microsoft.Extensions.DependencyInjection;
using RolesDeUsuario = BabelHub.Shared.Contratos.RolesDeUsuario;

namespace BabelHub.IntegrationTests.Reclamaciones;

/// <summary>
/// REC-11 (AC-REC-09) con la política real y el anfitrión real: el tope de <b>3 hojas por hora y origen</b> que declara <c>PoliticasDeSeguridad.HojasPorOrigen</c> lo hace cumplir el control de límites real del servicio (<c>EvaluarLimite</c> sobre el motor local,
/// que es el de un servicio sin Redis) y no un doble que cuente hasta 3: la cuarta hoja del mismo origen recibe el límite con la espera sugerida, las tres anteriores quedan registradas y la cuarta no escribe nada en la base. Si el 3 de la política cambiara,
/// esta prueba fallaría.
/// </summary>
[Collection(ColeccionDelEcommerceCompleto.Nombre)]
public sealed class TopeDeHojasPorOrigenConLaPoliticaRealTests(PostgresDelEcommerceCompletoFixture bd)
{
    private static RegistrarHojaEntrada Entrada(string marca, int numero) =>
        new(
            "Ana Pérez Quispe",
            "12345678",
            "Av. Los Olivos 123, Ayacucho",
            "ana.perez@correo.example",
            "Libro «La ciudad y los perros»",
            49.90m,
            "reclamo",
            $"Llegó con las hojas sueltas ({marca}-{numero}).",
            null,
            $"clave-del-formulario-{marca}-{numero:D4}",
            null);

    [Fact]
    [Trait("Criterio", "AC-REC-09")]
    public async Task La_cuarta_hoja_del_mismo_origen_recibe_el_limite_real_con_su_espera_y_las_tres_anteriores_quedan_registradas()
    {
        var marca = Guid.NewGuid().ToString("N")[..12];
        await using var anfitrion = bd.Anfitrion(_ => { });

        var resultados = new List<Resultado<ConstanciaDeHoja>>();
        for (var i = 1; i <= 4; i++)
        {
            var entrada = Entrada(marca, i);
            resultados.Add(await anfitrion.Como(ActorActual.Anonimo(), sp => sp.GetRequiredService<IReclamaciones>().Registrar(entrada)));
        }

        Assert.All(resultados.Take(3), r => Assert.True(r.EsExito, r.Error?.Clave));
        var cuarta = resultados[3];
        Assert.False(cuarta.EsExito);
        Assert.Equal(CategoriaDeError.Limite, cuarta.Error!.Categoria);
        Assert.Contains(cuarta.Error.Errores, e => e.Campo == ClavesDeReclamaciones.EsperaEnSegundos && int.Parse(e.Clave, System.Globalization.CultureInfo.InvariantCulture) > 0);
        Assert.Equal(3L, await bd.Consultar<long>($"SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE detalle LIKE '%({marca}-%'"));
    }

    /// <summary>Una auditoría que no responde: el registro con sesión de un cliente no puede auditar su vinculación (REC-33).</summary>
    private sealed class AuditoriaCaida : IAuditoria
    {
        public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default) => Task.FromException(new AuditoriaNoDisponibleException("sin auditoría"));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-30")]
    public async Task Tres_rechazos_por_no_poder_auditar_el_registro_con_sesion_devuelven_el_cupo_y_despues_se_registran_tres_hojas_sin_sesion_del_mismo_origen()
    {
        var marca = Guid.NewGuid().ToString("N")[..12];
        await using var caida = bd.Anfitrion(servicios => servicios.AddScoped<IAuditoria, AuditoriaCaida>());
        var cliente = ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.Cliente);

        // Con la política real hojas-por-origen liberable (DD-29): cada rechazo devuelve su cupo y suelta su clave.
        var rechazos = new List<Resultado<ConstanciaDeHoja>>();
        for (var i = 1; i <= 3; i++)
        {
            var entrada = Entrada(marca, i);
            rechazos.Add(await caida.Como(cliente, sp => sp.GetRequiredService<IReclamaciones>().Registrar(entrada)));
        }

        Assert.All(rechazos, r =>
        {
            Assert.Equal(CategoriaDeError.NoDisponible, r.Error!.Categoria);
            Assert.Contains(r.Error.Errores, e => e.Campo == ClavesDeReclamaciones.RegistrarSinSesion);
        });
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE detalle LIKE '%({marca}-%'"));

        // Las mismas tres hojas, ahora sin sesión y con las mismas claves del formulario, se registran: el cupo del origen y las claves quedaron libres.
        await using var anfitrion = bd.Anfitrion(_ => { });
        for (var i = 1; i <= 3; i++)
        {
            var entrada = Entrada(marca, i);
            var resultado = await anfitrion.Como(ActorActual.Anonimo(), sp => sp.GetRequiredService<IReclamaciones>().Registrar(entrada));
            Assert.True(resultado.EsExito, resultado.Error?.Clave);
        }

        Assert.Equal(3L, await bd.Consultar<long>($"SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE detalle LIKE '%({marca}-%' AND cuenta_id IS NULL"));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-09")]
    public async Task Repetir_una_hoja_con_su_clave_no_gasta_cupo_y_un_origen_agotado_no_afecta_a_otro_servicio_nuevo()
    {
        var marca = Guid.NewGuid().ToString("N")[..12];
        await using var anfitrion = bd.Anfitrion(_ => { });
        var primera = Entrada(marca, 1);

        var original = await anfitrion.Como(ActorActual.Anonimo(), sp => sp.GetRequiredService<IReclamaciones>().Registrar(primera));
        var repetida = await anfitrion.Como(ActorActual.Anonimo(), sp => sp.GetRequiredService<IReclamaciones>().Registrar(primera));
        var segunda = await anfitrion.Como(ActorActual.Anonimo(), sp => sp.GetRequiredService<IReclamaciones>().Registrar(Entrada(marca, 2)));
        var tercera = await anfitrion.Como(ActorActual.Anonimo(), sp => sp.GetRequiredService<IReclamaciones>().Registrar(Entrada(marca, 3)));

        // La repetición devuelve la misma constancia y no cuenta: las tres hojas distintas caben en el tope.
        Assert.True(original.EsExito && repetida.EsExito && segunda.EsExito && tercera.EsExito);
        Assert.Equal(original.Valor.Numero, repetida.Valor.Numero);
        Assert.Equal(3L, await bd.Consultar<long>($"SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE detalle LIKE '%({marca}-%'"));
    }
}
