using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.IntegrationTests.Pedidos;
using Microsoft.Extensions.DependencyInjection;
using RolesDeUsuario = BabelHub.Shared.Contratos.RolesDeUsuario;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// La cuenta del pagador sobre la <b>Identidad real</b> y PostgreSQL real (ecommerce-pagos PAG-21, PAG-25, PAG-31 y PAG-32; AC-PAG-18, AC-PAG-21 y AC-PAG-28): el adaptador <c>CuentaDelPagadorSobreIdentidad</c> que compone el anfitrión real decide con las filas
/// de Identidad si una cuenta puede pagar y, si no, dice el primer motivo: cuenta inactiva o cancelada, correo sin verificar, textos pendientes y cancelación diferida; una cuenta que no existe es <see langword="null"/> (falla cerrada); la marca de cancelación
/// en ejecución se lee con bloqueo (PAG-32) y el correo del titular solo existe si está verificado.
/// </summary>
[Collection(ColeccionDelEcommerceCompleto.Nombre)]
public sealed class CuentaDelPagadorConIdentidadRealTests(PostgresDelEcommerceCompletoFixture bd)
{
    private AnfitrionRealDelEcommerce Anfitrion() => bd.Anfitrion(_ => { });

    /// <summary>Una cuenta de cliente real (activa, con el correo verificado y los tres textos vigentes aceptados) y su correo.</summary>
    private async Task<(Guid Id, string Correo)> SembrarCuenta()
    {
        var id = Guid.NewGuid();
        var correo = $"p{id:N}@example.test";
        await bd.Ejecutar(
            $"""
            INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso) VALUES (1, 'v1', 'v1', 'v1')
              ON CONFLICT (id) DO UPDATE SET terminos = 'v1', privacidad = 'v1', reembolso = 'v1';
            INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, creada_en, acepto_terminos_version, acepto_terminos_en, acepto_privacidad_version, acepto_privacidad_en, acepto_reembolso_version, acepto_reembolso_en)
              VALUES ('{id}', 'Ana Pérez', '{correo}', true, 'cliente', true, now(), 'v1', now(), 'v1', now(), 'v1', now());
            """);
        return (id, correo);
    }

    private static ActorActual DelTitular(Guid cuenta) => ActorActual.DeUsuario(cuenta, RolesDeUsuario.Cliente, null);

    private async Task<EstadoDeLaCuentaParaPagar?> Consultar(Guid cuenta)
    {
        await using var anfitrion = Anfitrion();
        return await anfitrion.Como(DelTitular(cuenta), sp => sp.GetRequiredService<ICuentaDelPagador>().Consultar(cuenta));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-18")]
    public async Task Una_cuenta_activa_verificada_y_al_dia_con_los_textos_puede_pagar()
    {
        var (id, _) = await SembrarCuenta();

        var estado = await Consultar(id);

        Assert.NotNull(estado);
        Assert.True(estado.PuedePagar);
        Assert.Null(estado.Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-18")]
    public async Task Con_el_correo_sin_verificar_no_puede_pagar_y_el_motivo_es_el_correo()
    {
        var (id, _) = await SembrarCuenta();
        await bd.Ejecutar($"UPDATE identidad.usuarios SET correo_verificado = false WHERE id = '{id}'");

        var estado = await Consultar(id);

        Assert.NotNull(estado);
        Assert.False(estado.PuedePagar);
        Assert.Equal(MotivoDeCuentaQueNoPuedePagar.CorreoSinVerificar, estado.Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Con_los_textos_pendientes_no_puede_pagar_y_el_motivo_son_los_textos()
    {
        var (id, _) = await SembrarCuenta();
        await bd.Ejecutar($"UPDATE identidad.usuarios SET acepto_terminos_version = 'vieja' WHERE id = '{id}'");

        var estado = await Consultar(id);

        Assert.NotNull(estado);
        Assert.False(estado.PuedePagar);
        Assert.Equal(MotivoDeCuentaQueNoPuedePagar.TextosPendientes, estado.Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Con_una_cancelacion_diferida_no_puede_iniciar_un_cobro_nuevo_y_el_motivo_es_la_cancelacion()
    {
        var (id, _) = await SembrarCuenta();
        await bd.Ejecutar($"INSERT INTO identidad.solicitudes_de_cancelacion (id, usuario_id, estado, solicitada_en) VALUES (gen_random_uuid(), '{id}', 'diferida', now())");

        var estado = await Consultar(id);

        Assert.NotNull(estado);
        Assert.False(estado.PuedePagar);
        Assert.Equal(MotivoDeCuentaQueNoPuedePagar.CancelacionDiferida, estado.Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Una_cuenta_desactivada_o_cancelada_es_inactiva_y_una_que_no_existe_falla_cerrada()
    {
        var (desactivada, _) = await SembrarCuenta();
        var (cancelada, _) = await SembrarCuenta();
        await bd.Ejecutar($"UPDATE identidad.usuarios SET activo = false WHERE id = '{desactivada}'");
        await bd.Ejecutar($"UPDATE identidad.usuarios SET cancelada_en = now() WHERE id = '{cancelada}'");

        Assert.Equal(MotivoDeCuentaQueNoPuedePagar.CuentaInactiva, (await Consultar(desactivada))!.Motivo);
        Assert.Equal(MotivoDeCuentaQueNoPuedePagar.CuentaInactiva, (await Consultar(cancelada))!.Motivo);
        Assert.Null(await Consultar(Guid.NewGuid()));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-28")]
    public async Task La_marca_de_cancelacion_en_ejecucion_se_lee_con_bloqueo_y_sin_marca_es_falsa()
    {
        var (conMarca, _) = await SembrarCuenta();
        var (sinMarca, _) = await SembrarCuenta();
        await bd.Ejecutar($"INSERT INTO identidad.solicitudes_de_cancelacion (id, usuario_id, estado, solicitada_en, ejecucion_en_curso) VALUES (gen_random_uuid(), '{conMarca}', 'diferida', now(), true)");
        await using var anfitrion = Anfitrion();

        var (enEjecucion, ninguna, sinBloqueo) = await anfitrion.Como(DelTitular(conMarca), async sp =>
        {
            var unidad = sp.GetRequiredService<IUnidadDeTrabajo>();
            var cuenta = sp.GetRequiredService<ICuentaDelPagador>();
            var a = await unidad.Ejecutar(token => cuenta.CancelacionEnEjecucion(conMarca, bloquear: true, token));
            var b = await unidad.Ejecutar(token => cuenta.CancelacionEnEjecucion(sinMarca, bloquear: true, token));
            // Sin bloqueo no se ofrece: la lectura sin bloqueo sería tomarlo sin que quien llama lo espere.
            var c = await Assert.ThrowsAsync<ArgumentException>(() => cuenta.CancelacionEnEjecucion(conMarca, bloquear: false));
            return (a, b, c);
        });

        Assert.True(enEjecucion);
        Assert.False(ninguna);
        Assert.NotNull(sinBloqueo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-28")]
    public async Task El_correo_del_titular_solo_existe_si_esta_verificado_y_no_se_guarda_en_ninguna_parte_de_Pagos()
    {
        var (verificada, correo) = await SembrarCuenta();
        var (sinVerificar, _) = await SembrarCuenta();
        await bd.Ejecutar($"UPDATE identidad.usuarios SET correo_verificado = false WHERE id = '{sinVerificar}'");
        await using var anfitrion = Anfitrion();

        var (conCorreo, sinCorreo) = await anfitrion.Como(ActorActual.DelSistema(), async sp =>
        {
            var cuenta = sp.GetRequiredService<ICuentaDelPagador>();
            return (await cuenta.CorreoDelTitular(verificada), await cuenta.CorreoDelTitular(sinVerificar));
        });

        Assert.Equal(correo, conCorreo);
        Assert.Null(sinCorreo);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM pagos.intenciones WHERE row_to_json(intenciones)::text ILIKE '%' || @c || '%'", ("c", correo)));
    }
}
