using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Carrito;
using BabelHub.IntegrationTests.Pagos;
using BabelHub.IntegrationTests.Pedidos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RolesDeUsuario = BabelHub.Shared.Contratos.RolesDeUsuario;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>
/// CAT-12 con el Catálogo y el Carrito reales (AC-CAT-09): cuando el Inventario no responde, el catálogo no inventa stock («no se puede confirmar», sin un dato reciente) y <b>agregar al carrito sigue permitido</b>: la disponibilidad real se valida al iniciar el
/// checkout (AC-CAR-05). Sobre PostgreSQL con todos los esquemas migrados; el Inventario del Carrito simulado está caído y el cliente del Inventario del Catálogo apunta a un servicio que no responde.
/// </summary>
[Collection(ColeccionDelEcommerceCompleto.Nombre)]
public sealed class AgregarAlCarritoConElInventarioCaidoTests(PostgresDelEcommerceCompletoFixture bd)
{
    private async Task<Guid> SembrarLaEdicionImpresa()
    {
        var (obra, edicion) = (Guid.NewGuid(), Guid.NewGuid());
        var doce = "97863" + Random.Shared.NextInt64(1_000_000, 9_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var isbn = doce + ((10 - (doce.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum() % 10)) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await bd.Ejecutar(
            $"""
            BEGIN;
            INSERT INTO catalogo.obras (id, titulo, autor, editorial, categoria, portada_clave, portada_huella, portada_tipo, estado, version, creada_en)
              VALUES ('{obra}', 'Pedro Páramo (prueba de inventario caído)', 'Autor de prueba', 'Editorial de prueba', 'Narrativa', '{obra:N}', decode(repeat('c2', 32), 'hex'), 'jpeg', 'activa', 1, now());
            INSERT INTO catalogo.ediciones (id, obra_id, tipo, isbn13, isbn_ingresado, precio, estado)
              VALUES ('{edicion}', '{obra}', 'impresa', '{isbn}', '{isbn}', 38.00, 'activa');
            COMMIT;
            """);
        return edicion;
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

    [Fact]
    [Trait("Criterio", "AC-CAT-09")]
    public async Task Con_el_Inventario_caido_el_catalogo_dice_que_no_puede_confirmar_y_agregar_al_carrito_sigue_permitido()
    {
        var edicion = await SembrarLaEdicionImpresa();
        var cuenta = await SembrarCuenta();
        var inventario = new InventarioSimuladoDelCarrito(new RelojManualDePagos()) { Caido = true };
        await using var anfitrion = bd.Anfitrion(servicios =>
        {
            servicios.RemoveAll<IInventarioDelCarrito>();
            servicios.AddSingleton<IInventarioDelCarrito>(inventario);
        });

        // El catálogo no inventa stock: sin respuesta del Inventario y sin un dato reciente, «no se puede confirmar».
        var disponibilidad = await anfitrion.Como(ActorActual.Anonimo(), sp => sp.GetRequiredService<IDisponibilidadDeEdiciones>().Consultar([edicion]));
        Assert.True(disponibilidad.EsExito, disponibilidad.Error?.Clave);
        Assert.Equal(EstadoDeDisponibilidad.NoSePuedeConfirmar, Assert.Single(disponibilidad.Valor).Estado);

        // Agregar al carrito sigue permitido: la línea queda en el carrito del cliente.
        var agregado = await anfitrion.Como(ActorActual.DeUsuario(cuenta, RolesDeUsuario.Cliente, null), sp => sp.GetRequiredService<ICarritoDelCliente>().AgregarLinea(new AgregarLineaEntrada(edicion, 1)));
        Assert.True(agregado.EsExito, agregado.Error?.Clave);
        Assert.Contains(agregado.Valor.Carrito.Lineas, l => l.EdicionId == edicion);
    }
}
