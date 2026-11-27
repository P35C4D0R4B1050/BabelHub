using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Carrito;
using BabelHub.IntegrationTests.Pagos;
using BabelHub.IntegrationTests.Pedidos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RolesDeUsuario = BabelHub.Shared.Contratos.RolesDeUsuario;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>
/// La baja real de una edición con el retiro real de Carrito y el pedido real de Pedidos (ecommerce-catalogo CAT-18 y CAT-19; AC-CAT-15) sobre PostgreSQL con todos los esquemas migrados y la auditoría persistida: una edición impresa en el carrito de dos clientes y
/// en un pedido en curso; cuando la administración la da de baja <b>se quita de los dos carritos con un aviso a cada cliente</b>, <b>el pedido en curso sigue mostrándola</b> y queda <b>la auditoría de la baja</b>. Solo el Inventario del Carrito (reservas y stock) es un doble.
/// </summary>
[Collection(ColeccionDelEcommerceCompleto.Nombre)]
public sealed class BajaDeEdicionConElCarritoRealTests(PostgresDelEcommerceCompletoFixture bd)
{
    private static readonly Guid Administrador = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private async Task<(Guid Obra, Guid Edicion)> SembrarLaEdicion()
    {
        var (obra, edicion, digital) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        // Un ISBN-13 con su dígito de control válido (el catálogo lo comprueba al leer la edición).
        var doce = "97861" + Random.Shared.NextInt64(1_000_000, 9_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var suma = doce.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        var isbn = doce + ((10 - (suma % 10)) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
        // Una obra conserva al menos una edición activa (y una por tipo): la impresa se da de baja y queda la digital.
        var otroDoce = "97862" + Random.Shared.NextInt64(1_000_000, 9_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var otroIsbn = otroDoce + ((10 - (otroDoce.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum() % 10)) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await bd.Ejecutar(
            $"""
            BEGIN;
            INSERT INTO catalogo.obras (id, titulo, autor, editorial, categoria, portada_clave, portada_huella, portada_tipo, estado, version, creada_en)
              VALUES ('{obra}', 'El túnel (prueba de baja)', 'Autor de prueba', 'Editorial de prueba', 'Narrativa', '{obra:N}', decode(repeat('b1', 32), 'hex'), 'jpeg', 'activa', 1, now());
            INSERT INTO catalogo.ediciones (id, obra_id, tipo, isbn13, isbn_ingresado, precio, estado)
              VALUES ('{edicion}', '{obra}', 'impresa', '{isbn}', '{isbn}', 45.00, 'activa'), ('{digital}', '{obra}', 'digital', '{otroIsbn}', '{otroIsbn}', 20.00, 'activa');
            INSERT INTO catalogo.licencias (edicion_id, tipo, titular, territorios, vigente_hasta, formato) VALUES ('{digital}', 'dominio_publico', 'Titular de prueba', ARRAY['PE'], NULL, 'epub');
            INSERT INTO catalogo.archivos_digitales (edicion_id, clave_en_almacen, tipo_detectado, tamano, huella_sha256, subido_en, subido_por) VALUES ('{digital}', '{digital:N}', 'epub', 1024, decode(repeat('d3', 32), 'hex'), now(), '00000000-0000-0000-0000-000000000000');
            COMMIT;
            """);
        return (obra, edicion);
    }

    private async Task<Guid> SembrarCuenta()
    {
        var id = Guid.NewGuid();
        await bd.Ejecutar(
            $"""
            INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso) VALUES (1, 'v1', 'v1', 'v1')
              ON CONFLICT (id) DO UPDATE SET terminos = 'v1', privacidad = 'v1', reembolso = 'v1';
            INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, creada_en, acepto_terminos_version, acepto_terminos_en, acepto_privacidad_version, acepto_privacidad_en, acepto_reembolso_version, acepto_reembolso_en)
              VALUES ('{id}', 'Ana Pérez', 'p{id:N}@example.test', true, 'cliente', true, now(), 'v1', now(), 'v1', now(), 'v1', now());
            """);
        return id;
    }

    private static ActorActual DelCliente(Guid cuenta) => ActorActual.DeUsuario(cuenta, RolesDeUsuario.Cliente, null);

    private AnfitrionRealDelEcommerce Anfitrion(Guid edicion)
    {
        var inventario = new InventarioSimuladoDelCarrito(new RelojManualDePagos());
        inventario.FijarStock(edicion, 1000);
        return bd.Anfitrion(servicios =>
        {
            servicios.RemoveAll<IInventarioDelCarrito>();
            servicios.AddSingleton<IInventarioDelCarrito>(inventario);
            servicios.RemoveAll<BabelHub.Ecommerce.Application.Web.IPreferenciaDeSucursal>();
            servicios.AddSingleton(BabelHub.IntegrationTests.Carrito.SucursalesDelCarrito.Preferencia());
        });
    }

    private static CompraAprobada CompraConLaEdicion(Guid pedido, Guid cuenta, Guid edicion)
    {
        var ahora = DateTimeOffset.UtcNow;
        var lineas = new List<LineaDeCompraAprobada> { new(Guid.NewGuid(), edicion, false, "El túnel", null, 1, 45m, 45m, "lima", false, null, null) };
        var aceptaciones = new List<AceptacionDeCompra> { new(TextoLegalDeCompra.Terminos, "v1", ahora), new(TextoLegalDeCompra.Privacidad, "v1", ahora) };
        return new CompraAprobada(pedido, cuenta, TipoDeEntregaDeCompra.Domicilio, null, "ayacucho", 12m, 57m, new ContactoDeLaCompra("Ana Pérez", "987654321", "Jr. Lima 123", "ayacucho"), aceptaciones, lineas);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-15")]
    public async Task La_baja_de_una_edicion_la_quita_de_los_dos_carritos_con_aviso_a_cada_cliente_deja_el_pedido_en_curso_con_ella_y_queda_auditada()
    {
        var (obra, edicion) = await SembrarLaEdicion();
        await using var anfitrion = Anfitrion(edicion);
        var (clienteA, clienteB, titular) = (await SembrarCuenta(), await SembrarCuenta(), await SembrarCuenta());
        foreach (var cuenta in new[] { clienteA, clienteB })
        {
            var agregado = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICarritoDelCliente>().AgregarLinea(new AgregarLineaEntrada(edicion, 1, CodigoDeSucursal: "sede-central")));
            Assert.True(agregado.EsExito, agregado.Error?.Clave);
        }

        // Un pedido en curso con la edición (lo creó una compra aprobada): la baja no lo toca.
        var pedido = Guid.NewGuid();
        Assert.Equal(ResultadoDeConfirmarCompra.Creado, await anfitrion.Como(ActorActual.DelSistema(), sp => sp.GetRequiredService<IConfirmacionDeCompra>().Confirmar(CompraConLaEdicion(pedido, titular, edicion))));

        var version = await bd.Consultar<long>($"SELECT version FROM catalogo.obras WHERE id = '{obra}'");
        var baja = await anfitrion.Como(ActorActual.DeUsuario(Administrador, RolesDeUsuario.AdministradorCentral, null), sp => sp.GetRequiredService<IAdministracionDelCatalogo>().DarDeBajaEdicion(new DarDeBajaEdicionEntrada(obra, edicion, version)));

        Assert.True(baja.EsExito, baja.Error?.Clave);
        foreach (var cuenta in new[] { clienteA, clienteB })
        {
            var carrito = await anfitrion.Como(DelCliente(cuenta), sp => sp.GetRequiredService<ICarritoDelCliente>().Obtener(new SolicitudSobreElCarrito()));
            Assert.True(carrito.EsExito, carrito.Error?.Clave);
            Assert.DoesNotContain(carrito.Valor.Carrito.Lineas, l => l.EdicionId == edicion);
            Assert.Contains(carrito.Valor.Carrito.Avisos, a => a.EdicionId == edicion);
        }

        // El pedido en curso sigue mostrándola, intacto.
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM pedidos.lineas_de_pedido WHERE pedido_id = '{pedido}' AND edicion_id = '{edicion}'"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM pedidos.pedidos WHERE id = '{pedido}'"));
        // Queda la auditoría de la baja, con la edición como objeto y sin datos de ningún cliente.
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE objeto_id = '{edicion}' AND accion = 'EdicionDadaDeBaja'"));
    }
}
