using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Domain.Identidad;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Infrastructure.Carrito;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using BabelHub.IntegrationTests.Pagos;
using BabelHub.IntegrationTests.Pedidos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MedioDelContrato = BabelHub.Ecommerce.Application.Pagos.MedioDePago;
using Roles = BabelHub.Shared.Contratos.RolesDeUsuario;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// T62 de <c>identidad-seguridad</c> con un <b>pedido real</b> (AC-IDE-70, IDE-92) y D5 (AC-IDE-24, IDE-27) sobre el host real del E-commerce y PostgreSQL: el cliente compra por el flujo real (carrito, checkout con su contacto, cobro aprobado
/// del simulador y creación del pedido por Pedidos). Con el pedido sin entregar, la cancelación queda diferida y le <b>indica cuáles faltan</b> (el pedido, con los pendientes que cuentan los módulos reales); después, al corregir su nombre y
/// cambiar su correo, el pedido conserva el contacto con que se emitió y el correo anterior no queda en la auditoría, en los eventos de seguridad ni en las alertas. Solo lo externo es doble: el Inventario, el reloj y el transporte de correo.
/// </summary>
[Collection(ColeccionDelEcommerceCompleto.Nombre)]
public sealed class CuentaConUnPedidoRealTests(PostgresDelEcommerceCompletoFixture bd) : IAsyncLifetime
{
    private static readonly Guid Obra = Guid.Parse("a0000000-0000-0000-0000-0000000000e7");

    private static readonly Guid EdicionImpresa = Guid.Parse("b0000000-0000-0000-0000-0000000000e7");

    private const string Frase = "una frase larga y segura para el cliente";

    private const string NombreDelContacto = "Rosa Quispe Ficticia";

    private readonly RelojManualDePagos _reloj = new();

    private InventarioSimuladoDelCarrito _inventarioDelCarrito = null!;

    private ReservasSimuladasDelPago _reservasDelPago = null!;

    private readonly CapturadorDeCodigos _correo = new();

    private sealed class CapturadorDeCodigos : IEnviadorDeCorreo
    {
        public List<CorreoDeCodigo> Codigos { get; } = [];

        public Task<ResultadoDeEnvio> Enviar(CorreoSaliente correo, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeEnvio.Enviado);

        public Task<ResultadoDeEnvio> EnviarCodigo(CorreoDeCodigo correo, CancellationToken cancelacion = default)
        {
            lock (Codigos)
            {
                Codigos.Add(correo);
            }

            return Task.FromResult(ResultadoDeEnvio.Enviado);
        }
    }

    public async Task InitializeAsync()
    {
        _inventarioDelCarrito = new InventarioSimuladoDelCarrito(_reloj);
        _inventarioDelCarrito.FijarStock(EdicionImpresa, 1000);
        _reservasDelPago = new ReservasSimuladasDelPago(_reloj);
        await bd.Ejecutar(
            $"""
            BEGIN;
            INSERT INTO catalogo.obras (id, titulo, autor, editorial, categoria, portada_clave, portada_huella, portada_tipo, estado, version, creada_en)
              VALUES ('{Obra}', 'La ciudad y los perros (prueba)', 'Autor de prueba', 'Editorial de prueba', 'Narrativa', 'e2e0000000000000000000000000e7e7', decode(repeat('e7', 32), 'hex'), 'jpeg', 'activa', 1, now())
              ON CONFLICT DO NOTHING;
            INSERT INTO catalogo.ediciones (id, obra_id, tipo, isbn13, isbn_ingresado, precio, estado)
              VALUES ('{EdicionImpresa}', '{Obra}', 'impresa', '9786123450137', '9786123450137', 50.00, 'activa')
              ON CONFLICT DO NOTHING;
            INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso) VALUES (1, 'v1', 'v1', 'v1')
              ON CONFLICT (id) DO UPDATE SET terminos = 'v1', privacidad = 'v1', reembolso = 'v1';
            COMMIT;
            """);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AnfitrionRealDelEcommerce Anfitrion() => bd.Anfitrion(servicios =>
    {
        servicios.RemoveAll<IReloj>();
        servicios.AddSingleton<IReloj>(_reloj);
        servicios.RemoveAll<IEnviadorDeCorreo>();
        servicios.AddSingleton<IEnviadorDeCorreo>(_correo);
        servicios.RemoveAll<IInventarioDelCarrito>();
        servicios.AddSingleton<IInventarioDelCarrito>(_inventarioDelCarrito);
        servicios.RemoveAll<BabelHub.Ecommerce.Application.Web.IPreferenciaDeSucursal>();
        servicios.AddSingleton(BabelHub.IntegrationTests.Carrito.SucursalesDelCarrito.Preferencia());
        servicios.RemoveAll<IReservasDelPago>();
        servicios.AddSingleton<IReservasDelPago>(_reservasDelPago);
        servicios.RemoveAll<IReservasDelPedido>();
        servicios.AddSingleton<IReservasDelPedido>(new ReservasFalsas());
        servicios.RemoveAll<OpcionesDelCobro>();
        servicios.AddSingleton(new OpcionesDelCobro(TimeSpan.FromMilliseconds(300)));
        // El Inventario no está en este host: sus pendientes de la cuenta (reservas) se quitan del compuesto; los de los demás módulos reales siguen contando.
        foreach (var d in servicios.Where(d => d.ServiceType == typeof(IPendientesDeCuenta) && d.ImplementationType == typeof(Ecommerce.Infrastructure.Identidad.InventarioPendientesDeCuenta)).ToList())
        {
            servicios.Remove(d);
        }
    });

    private static ActorActual DelCliente(Guid cuenta) => ActorActual.DeUsuario(cuenta, Roles.Cliente, null);

    private static ValorSecreto S(string valor) => new(valor);

    private async Task<Guid> SembrarCliente(AnfitrionRealDelEcommerce anfitrion, string correo)
    {
        var id = Guid.NewGuid();
        var resumen = await anfitrion.Como(ActorActual.DelSistema(), async sp =>
        {
            var (contrasena, _) = Contrasena.Crear(Frase, _ => false);
            return await sp.GetRequiredService<IHasherDeContrasenas>().Resumir(contrasena!);
        });
        await bd.Ejecutar(
            $"""
            INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, hash_contrasena, creada_en, acepto_terminos_version, acepto_terminos_en, acepto_privacidad_version, acepto_privacidad_en, acepto_reembolso_version, acepto_reembolso_en)
              VALUES ('{id}', 'Cliente Original', '{correo}', true, 'cliente', true, '{resumen}', DATE '2026-10-01', 'v1', now(), 'v1', now(), 'v1', now());
            """);
        return id;
    }

    private static async Task<TextoVistoDeCompra[]> TextosVigentes(AnfitrionRealDelEcommerce anfitrion)
    {
        var terminos = await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<ITextosLegales>().Obtener(TipoDeTextoLegal.Terminos));
        var privacidad = await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<ITextosLegales>().Obtener(TipoDeTextoLegal.Privacidad));
        Assert.True(terminos.EsExito && privacidad.EsExito, "Identidad debe publicar las versiones vigentes de los textos.");
        return [new(TextoLegalDeCompra.Terminos, terminos.Valor.Version), new(TextoLegalDeCompra.Privacidad, privacidad.Valor.Version)];
    }

    /// <summary>El flujo real de una compra a domicilio hasta que Pedidos crea el pedido: carrito, checkout, cobro aprobado del simulador y reanudación de las compras aprobadas.</summary>
    private async Task<Guid> ComprarADomicilio(AnfitrionRealDelEcommerce anfitrion, Guid cuenta)
    {
        var textos = await TextosVigentes(anfitrion);
        Assert.True((await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICarritoDelCliente>().AgregarLinea(new AgregarLineaEntrada(EdicionImpresa, 1, CodigoDeSucursal: "sede-central")))).EsExito);
        Assert.True((await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICheckoutDelCliente>().Iniciar(new IniciarCheckoutEntrada()))).EsExito);
        var entrega = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICheckoutDelCliente>().ElegirEntrega(new ElegirEntregaEntrada(TipoDeEntregaDeCompra.Domicilio, new ContactoDeCompra(NombreDelContacto, "987654321", "Jr. Ficticio 123, Ayacucho", "ayacucho"))));
        Assert.True(entrega.EsExito, entrega.Error?.Clave);
        Assert.True((await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICheckoutDelCliente>().RegistrarAceptaciones(new RegistrarAceptacionesEntrada(textos)))).EsExito);
        var pasar = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICheckoutDelCliente>().PasarAlPago(new PasarAlPagoEntrada(entrega.Valor.Total, textos)));
        Assert.True(pasar.EsExito, pasar.Error?.Clave);
        var pedido = pasar.Valor.PedidoId ?? throw new InvalidOperationException("Pasar al pago no devolvió el pedido.");

        var lineas = new List<Guid>();
        await using (var comando = bd.Administracion.CreateCommand("SELECT linea_id FROM pagos.intencion_lineas WHERE intencion_id = @p"))
        {
            comando.Parameters.AddWithValue("p", pedido);
            await using var lector = await comando.ExecuteReaderAsync();
            while (await lector.ReadAsync())
            {
                lineas.Add(lector.GetGuid(0));
            }
        }

        Assert.NotEmpty(lineas);
        _reservasDelPago.Sembrar(pedido, _reloj.Ahora + PlazosDelPago.VigenciaConImpresos, [.. lineas.Select(l => new LineaConfirmadaDeReserva(l, "AYA-01", false, null, null))]);

        var cobro = await anfitrion.Como(DelCliente(cuenta), async sp =>
        {
            var pagos = sp.GetRequiredService<IPagos>();
            var clave = await pagos.EmitirClaveDeIntento(new EmitirClaveDeIntentoEntrada(pedido));
            Assert.True(clave.EsExito, clave.Error?.Clave);
            var resultado = await pagos.Cobrar(new CobrarEntrada(pedido, clave.Valor.Token, MedioDelContrato.Tarjeta, TokenDelMedioDePago.Crear(EscenariosDelSimulador.Aprobado)));
            Assert.True(resultado.EsExito, resultado.Error?.Clave);
            return resultado.Valor;
        });
        Assert.Equal(EstadoDelCobro.Aprobado, cobro.Estado);
        await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<ReanudarComprasAprobadas>().Ejecutar(50));
        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM pedidos.pedidos WHERE id = @p", ("p", pedido)));
        return pedido;
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    [Trait("Criterio", "AC-IDE-70")]
    public async Task Con_un_pedido_real_sin_entregar_la_cancelacion_dice_cual_falta_y_al_corregir_el_nombre_y_cambiar_el_correo_el_pedido_conserva_su_contacto_sin_dejar_el_correo_anterior_en_ningun_registro()
    {
        await using var anfitrion = Anfitrion();
        var anterior = $"p{Guid.NewGuid():N}@example.test";
        var nuevo = $"nuevo-p{Guid.NewGuid():N}@example.test";
        var cuenta = await SembrarCliente(anfitrion, anterior);
        var actor = DelCliente(cuenta);
        var marca = (await anfitrion.Como(actor, sp => sp.GetRequiredService<IDispositivosDeLaCuenta>().RegistrarElIngresoCompleto(cuenta, null, "PE"))).Valor.MarcaNueva!.Revelar();
        _reloj.Avanzar(TimeSpan.FromHours(25));
        var pedido = await ComprarADomicilio(anfitrion, cuenta);

        // D5 / AC-IDE-24: con el pedido de impresos sin entregar, la cancelación queda diferida y le dice cuál falta (no solo un número).
        var solicitada = await anfitrion.Como(actor, sp => sp.GetRequiredService<ICancelacionDeCuenta>().Solicitar(cuenta, S(Frase), null, S(marca)));
        Assert.True(solicitada.EsExito, solicitada.Error?.Clave);
        Assert.Equal(EstadoDeLaCancelacion.Diferida, solicitada.Valor.Estado);
        Assert.Equal(1, solicitada.Valor.Desglose?.PedidosSinCerrar);
        Assert.Equal(solicitada.Valor.PendientesAbiertos, solicitada.Valor.Desglose?.Total);
        var consultada = await anfitrion.Como(actor, sp => sp.GetRequiredService<ICancelacionDeCuenta>().Consultar(cuenta));
        Assert.Equal(solicitada.Valor.Desglose, consultada.Valor.Desglose);
        Assert.True((await anfitrion.Como(actor, sp => sp.GetRequiredService<ICancelacionDeCuenta>().Desistir(cuenta))).EsExito);

        // AC-IDE-70: corregir el nombre y cambiar el correo no toca el pedido emitido.
        var corregido = await anfitrion.Como(actor, sp => sp.GetRequiredService<ICuentaDelCliente>().CorregirElNombre(cuenta, "Ana Pérez Nueva"));
        Assert.True(corregido.EsExito, corregido.Error?.Clave);
        var cambio = await anfitrion.Como(actor, sp => sp.GetRequiredService<ICambioDeCorreo>().Solicitar(cuenta, nuevo, S(Frase), S(marca), "PE"));
        Assert.True(cambio.EsExito, cambio.Error?.Clave);
        var codigo = Assert.Single(_correo.Codigos, c => c.Destinatario == nuevo).Codigo.Revelar();
        var verificado = await anfitrion.Como(actor, sp => sp.GetRequiredService<ICambioDeCorreo>().VerificarElCorreoNuevo(cuenta, S(codigo)));
        Assert.True(verificado.EsExito, verificado.Error?.Clave);
        Assert.Equal(nuevo, await bd.Consultar<string>($"SELECT correo FROM identidad.usuarios WHERE id = '{cuenta}'"));
        Assert.Equal("Ana Pérez Nueva", await bd.Consultar<string>($"SELECT nombre FROM identidad.usuarios WHERE id = '{cuenta}'"));

        var detalle = await anfitrion.Como(
            ActorActual.DeUsuario(Guid.Parse("66666666-6666-6666-6666-666666666666"), Roles.AdministradorCentral, null),
            sp => sp.GetRequiredService<IAdministracionDePedidos>().Obtener(pedido));
        Assert.True(detalle.EsExito, detalle.Error?.Clave);
        Assert.Equal(NombreDelContacto, detalle.Valor.Contacto?.Nombre);
        Assert.Equal(1, await bd.Consultar<int>("SELECT count(*) FROM pedidos.pedidos WHERE id = @p", ("p", pedido)));

        // El correo anterior no aparece en la auditoría, en los eventos de seguridad ni en las alertas; ni el nombre del contacto ni el nuevo nombre pasan a la auditoría del cambio.
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE detalle::text ILIKE '%{anterior}%' OR objeto_id ILIKE '%{anterior}%' OR motivo ILIKE '%{anterior}%'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM identidad.registros WHERE detalle::text ILIKE '%{anterior}%' OR objeto_id ILIKE '%{anterior}%' OR motivo ILIKE '%{anterior}%'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.alertas a WHERE a::text ILIKE '%{anterior}%'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE objeto_id = '{cuenta:D}' AND detalle::text ILIKE '%Ana P%'"));
    }
}
