import { test, expect } from '@playwright/test';
import { abrir, exportacion } from './ayudas';

// La herramienta detecta el navegador, el sistema y el tipo de aparato de cada persona (cada una prueba con SU navegador).
// `clasificar` es una función pura: aquí se prueba con entradas simuladas de los navegadores más comunes, incluidos los derivados de Chromium.

const UA = {
  chromeWin: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36',
  edgeWin: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0',
  operaWin: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 OPR/115.0.0.0',
  firefoxWin: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0',
  safariMac: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15',
  safariIphone: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1',
  chromeIos: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/131.0.6778.73 Mobile/15E148 Safari/604.1',
  edgeIos: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 EdgiOS/131.0.2903.92 Mobile/15E148 Safari/605.1.15',
  firefoxIos: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) FxiOS/133.0 Mobile/15E148 Safari/605.1.15',
  chromeAndroid: 'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36',
  samsung: 'Mozilla/5.0 (Linux; Android 14; SAMSUNG SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/26.0 Chrome/122.0.0.0 Mobile Safari/537.36',
  firefoxAndroid: 'Mozilla/5.0 (Android 14; Mobile; rv:133.0) Gecko/133.0 Firefox/133.0',
  tabletAndroid: 'Mozilla/5.0 (Linux; Android 14; SM-X710) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36',
};
const marcas = (...b: Array<[string, string]>) => [...b.map(([brand, version]) => ({ brand, version })), { brand: 'Not_A Brand', version: '24' }];

type Caso = { nombre: string; entrada: Record<string, unknown>; navegador: string; version: RegExp | string; sistema: string; sistemaVersion?: string; dispositivo: string; combo: string };
const CASOS: Caso[] = [
  { nombre: 'Chrome en Windows 11 (marcas de Chromium)', entrada: { ua: UA.chromeWin, brands: marcas(['Chromium', '131'], ['Google Chrome', '131']), plataforma: 'Windows', plataformaVersion: '15.0.0', movil: false }, navegador: 'Chrome', version: '131', sistema: 'Windows', sistemaVersion: '11', dispositivo: 'computadora', combo: 'chrome-escritorio' },
  { nombre: 'Chrome en Windows 10', entrada: { ua: UA.chromeWin, brands: marcas(['Chromium', '131'], ['Google Chrome', '131']), plataforma: 'Windows', plataformaVersion: '10.0.0', movil: false }, navegador: 'Chrome', version: '131', sistema: 'Windows', sistemaVersion: '10', dispositivo: 'computadora', combo: 'chrome-escritorio' },
  { nombre: 'Edge en Windows 11: no se confunde con Chrome', entrada: { ua: UA.edgeWin, brands: marcas(['Chromium', '131'], ['Microsoft Edge', '131']), plataforma: 'Windows', plataformaVersion: '15.0.0', movil: false }, navegador: 'Edge', version: '131', sistema: 'Windows', sistemaVersion: '11', dispositivo: 'computadora', combo: 'edge-escritorio' },
  { nombre: 'Edge versión completa desde fullVersionList', entrada: { ua: UA.edgeWin, brands: marcas(['Chromium', '131'], ['Microsoft Edge', '131']), versiones: marcas(['Chromium', '131.0.6778.86'], ['Microsoft Edge', '131.0.2903.70']), plataforma: 'Windows', plataformaVersion: '15.0.0', movil: false }, navegador: 'Edge', version: '131.0.2903.70', sistema: 'Windows', dispositivo: 'computadora', combo: 'edge-escritorio' },
  { nombre: 'Brave (navigator.brave.isBrave)', entrada: { ua: UA.chromeWin, brands: marcas(['Chromium', '131']), brave: true, plataforma: 'Windows', plataformaVersion: '15.0.0', movil: false }, navegador: 'Brave', version: '131', sistema: 'Windows', dispositivo: 'computadora', combo: 'otro' },
  { nombre: 'Brave sin marcas (solo UA y la señal de Brave)', entrada: { ua: UA.chromeWin, brave: true }, navegador: 'Brave', version: '131.0.0.0', sistema: 'Windows', dispositivo: 'computadora', combo: 'otro' },
  { nombre: 'Chromium sin otra marca, sin Brave', entrada: { ua: UA.chromeWin, brands: marcas(['Chromium', '131']), plataforma: 'Linux', movil: false }, navegador: 'Chromium', version: '131', sistema: 'Linux', dispositivo: 'computadora', combo: 'otro' },
  { nombre: 'Opera', entrada: { ua: UA.operaWin, brands: marcas(['Chromium', '131'], ['Opera', '115']), plataforma: 'Windows', plataformaVersion: '15.0.0', movil: false }, navegador: 'Opera', version: '115', sistema: 'Windows', dispositivo: 'computadora', combo: 'otro' },
  { nombre: 'Edge viejo reconocido solo por el UA (sin marcas)', entrada: { ua: UA.edgeWin }, navegador: 'Edge', version: '131.0.0.0', sistema: 'Windows', dispositivo: 'computadora', combo: 'edge-escritorio' },
  { nombre: 'Opera solo por el UA', entrada: { ua: UA.operaWin }, navegador: 'Opera', version: '115.0.0.0', sistema: 'Windows', dispositivo: 'computadora', combo: 'otro' },
  { nombre: 'Firefox en Windows (solo UA)', entrada: { ua: UA.firefoxWin }, navegador: 'Firefox', version: '133.0', sistema: 'Windows', dispositivo: 'computadora', combo: 'firefox-escritorio' },
  { nombre: 'Safari en Mac', entrada: { ua: UA.safariMac }, navegador: 'Safari', version: '17.5', sistema: 'macOS', dispositivo: 'computadora', combo: 'safari-macos' },
  { nombre: 'Safari en iPhone', entrada: { ua: UA.safariIphone, toques: 5 }, navegador: 'Safari', version: '17.5', sistema: 'iOS', sistemaVersion: '17.5', dispositivo: 'celular', combo: 'safari-iphone' },
  { nombre: 'Safari en iPhone aunque el motor reporte marcas de Chromium (UA simulado)', entrada: { ua: UA.safariIphone, brands: marcas(['Chromium', '131']) }, navegador: 'Safari', version: '17.5', sistema: 'iOS', dispositivo: 'celular', combo: 'safari-iphone' },
  { nombre: 'Chrome en iPhone (CriOS) no cuenta como Safari ni como Chrome de Android', entrada: { ua: UA.chromeIos }, navegador: 'Chrome', version: '131.0.6778.73', sistema: 'iOS', dispositivo: 'celular', combo: 'otro' },
  { nombre: 'Edge en iPhone (EdgiOS)', entrada: { ua: UA.edgeIos }, navegador: 'Edge', version: '131.0.2903.92', sistema: 'iOS', dispositivo: 'celular', combo: 'otro' },
  { nombre: 'Firefox en iPhone (FxiOS)', entrada: { ua: UA.firefoxIos }, navegador: 'Firefox', version: '133.0', sistema: 'iOS', dispositivo: 'celular', combo: 'otro' },
  { nombre: 'Safari en iPad con el UA de escritorio (se reconoce por el tacto)', entrada: { ua: UA.safariMac, toques: 5 }, navegador: 'Safari', version: '17.5', sistema: 'iPadOS', dispositivo: 'tableta', combo: 'otro' },
  { nombre: 'Chrome en Android (celular)', entrada: { ua: UA.chromeAndroid, brands: marcas(['Chromium', '131'], ['Google Chrome', '131']), plataforma: 'Android', movil: true }, navegador: 'Chrome', version: '131', sistema: 'Android', sistemaVersion: '14', dispositivo: 'celular', combo: 'chrome-android' },
  { nombre: 'Chrome en una tableta Android', entrada: { ua: UA.tabletAndroid, brands: marcas(['Chromium', '131'], ['Google Chrome', '131']), plataforma: 'Android', movil: false }, navegador: 'Chrome', version: '131', sistema: 'Android', dispositivo: 'tableta', combo: 'chrome-android' },
  { nombre: 'Samsung Internet', entrada: { ua: UA.samsung }, navegador: 'Samsung Internet', version: '26.0', sistema: 'Android', dispositivo: 'celular', combo: 'otro' },
  { nombre: 'Firefox en Android', entrada: { ua: UA.firefoxAndroid }, navegador: 'Firefox', version: '133.0', sistema: 'Android', dispositivo: 'celular', combo: 'otro' },
  { nombre: 'un UA que no se reconoce no inventa nada', entrada: { ua: 'curl/8.0' }, navegador: '', version: '', sistema: '', dispositivo: 'computadora', combo: 'otro' },
];

test.describe('Detección con entradas simuladas', () => {
  for (const c of CASOS) {
    test(c.nombre, async ({ page }) => {
      await abrir(page);
      const r = await page.evaluate((entrada) => { const h = (window as any).__babelhubPruebasHumanas; const d = h.clasificar(entrada); return { ...d, combo: h.comboDe(d) }; }, c.entrada);
      expect(r.navegador).toBe(c.navegador);
      expect(r.version).toBe(c.version);
      expect(r.sistema).toBe(c.sistema);
      if (c.sistemaVersion) expect(r.sistemaVersion).toBe(c.sistemaVersion);
      expect(r.dispositivo).toBe(c.dispositivo);
      expect(r.combo).toBe(c.combo);
    });
  }
});

test.describe('En pantalla: «Detectamos Edge 131 en Windows, ¿es correcto?»', () => {
  test('con las marcas de Edge se anuncia Edge 131 en Windows 11 y se confirma con un clic', async ({ page, isMobile }) => {
    test.skip(isMobile, 'Las marcas de Windows se simulan solo en escritorio (el UA del teléfono dice Android).');
    await page.addInitScript(() => {
      Object.defineProperty(navigator, 'userAgentData', { configurable: true, value: { brands: [{ brand: 'Chromium', version: '131' }, { brand: 'Microsoft Edge', version: '131' }, { brand: 'Not_A Brand', version: '24' }], mobile: false, platform: 'Windows', getHighEntropyValues: async () => ({ fullVersionList: [{ brand: 'Microsoft Edge', version: '131.0.2903.70' }], platformVersion: '15.0.0' }) } });
    });
    await abrir(page);
    await expect(page.getByText(/Detectamos Edge 131 en Windows 11, en una computadora\. ¿Es correcto\?/)).toBeVisible();
    await page.getByRole('button', { name: 'Sí, es correcto' }).click();
    await expect(page.locator('.chip', { hasText: 'Confirmado' })).toBeVisible();
    await page.getByRole('button', { name: 'Empezar' }).click();
    const ex = await exportacion(page);
    expect(ex.participantes[0].combo).toBe('edge-escritorio');
    expect(ex.participantes[0].entorno).toMatchObject({ navegador: 'Edge', version: '131.0.2903.70', sistema: 'Windows', dispositivo: 'computadora', confirmado: true, corregido: false });
  });

  test('Brave se detecta con navigator.brave y no se anuncia como Chrome', async ({ page, isMobile }) => {
    test.skip(isMobile, 'Las marcas de Windows se simulan solo en escritorio (el UA del teléfono dice Android).');
    await page.addInitScript(() => {
      Object.defineProperty(navigator, 'brave', { configurable: true, value: { isBrave: async () => true } });
      Object.defineProperty(navigator, 'userAgentData', { configurable: true, value: { brands: [{ brand: 'Chromium', version: '131' }, { brand: 'Not_A Brand', version: '24' }], mobile: false, platform: 'Windows', getHighEntropyValues: async () => ({ platformVersion: '10.0.0' }) } });
    });
    await abrir(page);
    await expect(page.getByText(/Detectamos Brave 131 en Windows 10/)).toBeVisible();
  });

  test('«No, corregir» deja elegir navegador, versión, sistema y tipo de aparato, y lo guarda como corregido', async ({ page }) => {
    await abrir(page);
    await page.getByRole('button', { name: 'No, corregir' }).click();
    await page.getByRole('combobox', { name: 'Navegador', exact: true }).selectOption('Firefox');
    await page.getByLabel(/Versión/).fill('130');
    await page.getByRole('combobox', { name: /Sistema/ }).selectOption('Windows');
    await page.getByRole('combobox', { name: 'Tipo de aparato' }).selectOption('computadora');
    await page.getByRole('button', { name: 'Guardar' }).click();
    await expect(page.locator('.chip', { hasText: 'Confirmado' })).toBeVisible();
    await expect(page.getByText('Firefox 130', { exact: false }).first()).toBeVisible();
    await page.getByRole('button', { name: 'Empezar' }).click();
    const ex = await exportacion(page);
    expect(ex.participantes[0].combo).toBe('firefox-escritorio');
    expect(ex.participantes[0].entorno).toMatchObject({ navegador: 'Firefox', version: '130', corregido: true, confirmado: true });
    expect(ex.participantes[0].entorno.detectado.navegador).not.toBe('');
  });

  test('sin confirmar el navegador no se puede empezar', async ({ page }) => {
    await abrir(page);
    await page.getByRole('button', { name: 'Empezar' }).click();
    await expect(page.getByRole('alert')).toContainText('confirma tu navegador');
    await expect(page.getByRole('heading', { level: 1, name: 'Ayúdanos a probar BabelHub' })).toBeVisible();
  });
});

test.describe('Con el UA de Firefox y sin userAgentData', () => {
  test.use({ userAgent: UA.firefoxWin });
  test('se anuncia Firefox 133 en Windows', async ({ page }) => {
    await page.addInitScript(() => { Object.defineProperty(navigator, 'userAgentData', { configurable: true, value: undefined }); });
    await abrir(page);
    await expect(page.getByText(/Detectamos Firefox 133 en Windows, en una computadora/)).toBeVisible();
  });
});

test.describe('Con el UA de Safari en iPhone', () => {
  test.use({ userAgent: UA.safariIphone });
  test('se anuncia Safari 17 en iOS 17.5 y cuenta para «Safari en iPhone»', async ({ page }) => {
    await page.addInitScript(() => { Object.defineProperty(navigator, 'userAgentData', { configurable: true, value: undefined }); });
    await abrir(page);
    await expect(page.getByText(/Detectamos Safari 17 en iOS 17\.5, en un celular/)).toBeVisible();
    await page.getByRole('button', { name: 'Sí, es correcto' }).click();
    await page.getByRole('button', { name: 'Empezar' }).click();
    expect((await exportacion(page)).participantes[0].combo).toBe('safari-iphone');
  });
});
