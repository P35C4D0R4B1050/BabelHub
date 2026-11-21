import { test, expect } from '@playwright/test';
import { clave, conOrigenPropio, ingresarComoClienteNuevo, ingresarComoPersonal, sinViolacionesDeAccesibilidad, urlDeLaGestion } from '../pedidos/ayudas';

// El menú por rol y alcance (web-base WEB-02; AC-WEB-02): en cada área solo las opciones que el rol puede usar. El visitante ve el catálogo y el carrito; el cliente suma sus pedidos y su biblioteca; la administración central ve todas sus pantallas; un encargado de
// sucursal no ve ninguna opción de Administración y, si abre esa dirección, ve la pantalla de sin permiso. El acceso real lo decide el servidor en cada ruta: el menú solo evita ofrecer lo que respondería «sin permiso».
// Mismo entorno que `acceso/ingreso-del-personal.spec.ts` (hosts de la tienda y de la gestión con datos sembrados).

test.beforeEach(async ({ page }) => conOrigenPropio(page));

const menu = (page: import('@playwright/test').Page) => page.getByRole('banner').getByRole('navigation', { name: 'Principal' });

test.describe('Menú por rol y alcance', () => {
  test.skip(!clave, 'Falta BABELHUB__Sembrador__Clave (entorno E2E local con el sembrador de sesiones)');

  test('un visitante ve el catálogo y el carrito, pero no pedidos ni biblioteca (AC-WEB-02)', async ({ page }) => {
    await page.goto('/catalogo');
    await expect(menu(page).getByRole('link', { name: 'Catálogo' })).toBeVisible();
    await expect(menu(page).getByRole('link', { name: 'Carrito' })).toBeVisible();
    await expect(menu(page).getByRole('link', { name: 'Mis pedidos' })).toHaveCount(0);
    await expect(menu(page).getByRole('link', { name: 'Mi biblioteca' })).toHaveCount(0);
    await sinViolacionesDeAccesibilidad(page);
  });

  test('un cliente con sesión ve también sus pedidos y su biblioteca (AC-WEB-02)', async ({ page }) => {
    await ingresarComoClienteNuevo(page);
    await page.goto('/catalogo');
    for (const nombre of ['Catálogo', 'Carrito', 'Mis pedidos', 'Mi biblioteca']) {
      await expect(menu(page).getByRole('link', { name: nombre })).toBeVisible();
    }
  });

  test('la administración central ve todas sus pantallas de gestión (AC-WEB-02)', async ({ page }) => {
    await ingresarComoPersonal(page, 'administrador');
    await page.goto(`${urlDeLaGestion}/administracion/obras`);
    for (const nombre of ['Catálogo nacional', 'Pedidos', 'Devoluciones', 'Reembolsos', 'Ayudas de la biblioteca', 'Hojas de reclamación', 'Usuarios y roles', 'Solicitudes de datos', 'Auditoría', 'Alertas de seguridad']) {
      await expect(menu(page).getByRole('link', { name: nombre, exact: true })).toBeVisible();
    }
    await sinViolacionesDeAccesibilidad(page);
  });

  test('un encargado de sucursal no ve ninguna opción de Administración y, si abre esa dirección, ve la pantalla de sin permiso (AC-WEB-02)', async ({ page }) => {
    await ingresarComoPersonal(page, 'encargado', 'ayacucho');
    await page.goto(`${urlDeLaGestion}/sucursal/ayacucho/pedidos`);
    await expect(menu(page).getByRole('link', { name: 'Pedidos de recojo' })).toHaveAttribute('href', '/sucursal/ayacucho/pedidos');
    await expect(menu(page).getByRole('link', { name: 'Envíos' })).toBeVisible();
    for (const nombre of ['Catálogo nacional', 'Usuarios y roles', 'Auditoría', 'Alertas de seguridad', 'Pedidos a domicilio']) {
      await expect(menu(page).getByRole('link', { name: nombre, exact: true })).toHaveCount(0);
    }

    const respuesta = await page.goto(`${urlDeLaGestion}/administracion/usuarios`);
    expect(respuesta!.status()).toBe(403);
  });
});
