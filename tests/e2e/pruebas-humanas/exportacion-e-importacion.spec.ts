import { test, expect, type Page } from '@playwright/test';
import { abrir, archivo, corridaCompleta, importarJson, ir, leerDescarga, sesionCompleta, CLAVE_LS } from './ayudas';

// Exportar a JSON y a Markdown, importar y combinar archivos de varios dispositivos, y borrar los datos locales (T32b a T34b).
// Los datos son de ejemplo y solo viven en estas pruebas.

async function descargar(page: Page, boton: string) {
  const [d] = await Promise.all([page.waitForEvent('download'), page.getByRole('button', { name: boton }).click()]);
  return { nombre: d.suggestedFilename(), texto: await leerDescarga(d) };
}

// Crea por la interfaz una corrida con un paso fallido, una sesión de NVDA con un crítico y una persona con SUS, tal como lo haría la persona evaluadora.
async function cargarDatosDeEjemplo(page: Page) {
  await abrir(page);
  await page.getByLabel('Código de quien evalúa').fill('EV1');
  await page.getByLabel('Dispositivo', { exact: true }).fill('Equipo de ejemplo');
  await page.getByLabel('Versión probada de la tienda').fill('commit de ejemplo');

  await ir(page, 'Navegadores');
  await page.getByRole('button', { name: 'Nueva corrida en Chrome · Escritorio' }).click();
  await page.getByLabel('Versión del navegador').fill('126.0');
  for (const paso of ['1. Inicio', '2. Catálogo', '4. Agregar al carrito']) await page.getByRole('group', { name: paso }).getByRole('radio', { name: 'Cumple', exact: true }).click();
  await page.getByRole('group', { name: '3. Ficha del libro' }).getByRole('radio', { name: 'Falla', exact: true }).click();
  await page.getByRole('group', { name: '¿Se completó la compra?' }).getByRole('radio', { name: 'Sí', exact: true }).click();
  await page.getByRole('button', { name: 'Agregar defecto' }).click();
  await page.getByLabel('Qué pasó (pasos para repetirlo)').fill('La disponibilidad no muestra icono en la ficha.');
  await page.getByLabel('Nombre del archivo de la captura').fill('ficha-chrome-126.png');

  await ir(page, 'Lector');
  await page.getByRole('button', { name: 'Nueva sesión: NVDA · claro' }).click();
  await page.getByText('Checkout', { exact: true }).click();
  await page.getByRole('group', { name: 'K2 en Checkout' }).getByRole('radio', { name: 'Crítico AA', exact: true }).click();
  await page.getByLabel('Qué pasó (obligatorio al fallar)').fill('El foco desaparece al abrir el selector de sucursal.');

  await ir(page, 'Usabilidad');
  await page.getByRole('button', { name: /Agregar persona a la ronda 1/ }).click();
  await page.getByLabel('Quién modera (código)').fill('EV1');
  await page.getByLabel('Leí el texto en voz alta').check();
  await page.getByLabel('Dijo que entiende que puede parar').check();
  await page.getByLabel('Acepta participar').check();
  await page.getByRole('button', { name: 'Resultado', exact: true }).click();
  await page.getByRole('group', { name: 'Resultado de la tarea' }).getByRole('radio', { name: 'Sin ayuda', exact: true }).click();
  await page.getByLabel('Tiempo total, en segundos').fill('250');
  await page.getByRole('button', { name: 'SUS', exact: true }).click();
  for (const [i, v] of [5, 3, 4, 2, 5, 1, 4, 2, 5, 3].entries()) await page.getByRole('group', { name: new RegExp(`^${i + 1}\\. `) }).getByRole('radio', { name: String(v), exact: true }).click();
  await page.getByRole('button', { name: 'Cierre', exact: true }).click();
  await page.getByLabel('Problema principal').fill('No encontró el botón de recojo en sucursal.');
}

test.describe('Exportar', () => {
  test('el JSON trae el esquema, los datos crudos y lo calculado; el Markdown trae las tablas del protocolo', async ({ page }) => {
    await cargarDatosDeEjemplo(page);
    await ir(page, 'Datos');
    const json = await descargar(page, 'Descargar JSON');
    expect(json.nombre).toMatch(/^babelhub-pruebas-humanas_ev1_\d{8}-\d{4}\.json$/);
    const ex = JSON.parse(json.texto);
    expect(ex.esquema).toBe('babelhub-pruebas-humanas');
    expect(ex.version).toBe(1);
    expect(ex.dispositivo).toMatchObject({ evaluador: 'EV1', etiqueta: 'Equipo de ejemplo' });
    expect(ex.versionProbada).toBe('commit de ejemplo');
    expect(ex.datos.a.corridas).toHaveLength(1);
    expect(ex.datos.a.corridas[0]).toMatchObject({ combo: 'chrome-escritorio', version: '126.0', completo: 'si' });
    expect(ex.datos.a.corridas[0].pasos.ficha.r).toBe('falla');
    expect(ex.datos.a.corridas[0].defectos[0]).toMatchObject({ captura: 'ficha-chrome-126.png', gravedad: 'mayor' });
    expect(ex.datos.b.sesiones[0].pantallas.checkout.items.K2.e).toBe('critico');
    expect(ex.calculado.b.criticos).toBe(1);
    expect(ex.calculado.b.veredicto).toBe('no-cumple');
    const persona = ex.datos.c.personas[0];
    expect(persona).toMatchObject({ codigo: 'P1', ronda: 1, susPuntaje: 80, sinAyudaCalculado: true, erroresN: 0 });
    expect(persona.problema).toContain('botón de recojo');
    expect(ex.calculado.veredictoApp).toBe('no-cumple');
    expect(ex.calculado.nota).toContain('veredicto definitivo');
    // El estado interno del cronómetro no sale en el archivo.
    expect(persona.tarea.cron).toBeUndefined();
    expect(Date.parse(ex.exportadoEn)).not.toBeNaN();

    const md = await descargar(page, 'Descargar resumen');
    expect(md.nombre).toMatch(/\.md$/);
    expect(md.texto).toContain('# Resultados de las pruebas humanas de BabelHub');
    expect(md.texto).toContain('| Fecha | Persona | Navegador y versión | Sistema o dispositivo | Completó la compra (sí/no) | Defectos |');
    expect(md.texto).toContain('ficha-chrome-126.png');
    expect(md.texto).toContain('| Fecha | Persona | Lector y navegador | Pantalla | Tema | Resultado (cumple/falla) | Observación |');
    expect(md.texto).toContain('falla (crítica)');
    expect(md.texto).toContain('| Ronda | Persona (código) | Éxito sin ayuda | Errores | Tiempo (min) | SUS (0–100) | Problema principal |');
    expect(md.texto).toContain('| 1 | P1 | sí | 0 | 4,2 | 80 |');
    expect(md.texto).toContain('El veredicto definitivo lo emite quien lee el JSON');
  });

  test('avisa de posibles datos personales (correo, teléfono) antes de devolver el archivo', async ({ page }) => {
    await cargarDatosDeEjemplo(page);
    await page.getByLabel('Problema principal').fill('Escribió su correo ana@example.com y su teléfono 987654321.');
    await ir(page, 'Datos');
    await page.getByRole('button', { name: 'Copiar JSON' }).click();
    await expect(page.getByText('Posibles datos personales')).toBeVisible();
    await expect(page.getByText('parece un correo electrónico')).toBeVisible();
  });

  test('«Ver el texto» muestra el JSON para copiarlo a mano (iPhone sin portapapeles)', async ({ page }) => {
    await cargarDatosDeEjemplo(page);
    await ir(page, 'Datos');
    await page.getByRole('button', { name: 'Ver el texto' }).first().click();
    await expect(page.getByLabel('Texto exportado (selecciona y copia)')).toHaveValue(/"esquema": "babelhub-pruebas-humanas"/);
  });
});

test.describe('Importar, combinar y borrar', () => {
  test('exportar, borrar los datos locales e importar devuelve lo mismo; importarlo otra vez no duplica nada', async ({ page }) => {
    await cargarDatosDeEjemplo(page);
    await ir(page, 'Datos');
    const json = await descargar(page, 'Descargar JSON');
    const original = JSON.parse(json.texto);

    await page.getByRole('button', { name: 'Borrar los datos locales…' }).click();
    await expect(page.getByRole('dialog')).toContainText('Se borrará todo');
    await page.getByRole('button', { name: 'Sí, borrar todo' }).click();
    await expect(page.getByText('0 corridas de navegadores, 0 sesiones de lector y 0 personas')).toBeVisible();
    expect(await page.evaluate((k) => JSON.parse(localStorage.getItem(k) ?? '{}').a.corridas.length, CLAVE_LS)).toBe(0);

    await importarJson(page, json.texto, 'exportado.json');
    await expect(page.getByText(/3 registros agregados · 0 actualizados · 0 conservados/)).toBeVisible();
    await ir(page, 'Datos');
    const otra = await descargar(page, 'Descargar JSON');
    const vuelto = JSON.parse(otra.texto);
    expect(vuelto.datos.a.corridas).toEqual(original.datos.a.corridas);
    expect(vuelto.datos.b.sesiones).toEqual(original.datos.b.sesiones);
    expect(vuelto.datos.c.personas).toEqual(original.datos.c.personas);

    await importarJson(page, json.texto, 'otra-vez.json');
    await expect(page.getByText(/0 registros agregados · 0 actualizados · 3 conservados/)).toBeVisible();
  });

  test('combina archivos de varios dispositivos: suma lo distinto y deja el registro más reciente cuando coincide', async ({ page }) => {
    await abrir(page);
    const a = archivo({ dispositivoId: 'disp-iphone01', evaluador: 'EV2', corridas: [corridaCompleta({ id: 'run-iphone01', combo: 'safari-iphone', navegador: 'Safari', version: '17.5' })], sesiones: [sesionCompleta({ id: 'ses-iphone01', lector: 'voiceover', tema: 'babelhub' })] });
    const b = archivo({ dispositivoId: 'disp-android1', evaluador: 'EV3', corridas: [corridaCompleta({ id: 'run-android1', combo: 'chrome-android', navegador: 'Chrome', version: '126' })] });
    await importarJson(page, a, 'iphone.json');
    await expect(page.getByText(/2 registros agregados/)).toBeVisible();
    await importarJson(page, b, 'android.json');
    await expect(page.getByText(/1 registros agregados/)).toBeVisible();
    await expect(page.getByText(/Procedencia de lo importado: EV2 \(disp-iphone01\), EV3 \(disp-android1\)/)).toBeVisible();

    // La misma corrida del iPhone, corregida más tarde (act mayor): gana la nueva. Con act menor: se conserva la local.
    const mas = corridaCompleta({ id: 'run-iphone01', combo: 'safari-iphone', navegador: 'Safari', version: '17.6', act: '2026-11-23T15:00:00.000Z' });
    await importarJson(page, archivo({ dispositivoId: 'disp-iphone01', evaluador: 'EV2', corridas: [mas], exportadoEn: '2026-11-23T15:00:00.000Z' }), 'iphone-nuevo.json');
    await expect(page.getByText(/0 registros agregados · 1 actualizados · 0 conservados/)).toBeVisible();
    const menos = corridaCompleta({ id: 'run-iphone01', combo: 'safari-iphone', navegador: 'Safari', version: '17.0', act: '2026-11-23T08:00:00.000Z' });
    await importarJson(page, archivo({ dispositivoId: 'disp-iphone01', evaluador: 'EV2', corridas: [menos], exportadoEn: '2026-11-23T16:00:00.000Z' }), 'iphone-viejo.json');
    await expect(page.getByText(/0 registros agregados · 0 actualizados · 1 conservados/)).toBeVisible();
    const guardado = await page.evaluate((k) => JSON.parse(localStorage.getItem(k)!).a.corridas.map((r: any) => `${r.id}:${r.version}`).sort(), CLAVE_LS);
    expect(guardado).toEqual(['run-android1:126', 'run-iphone01:17.6']);
  });

  test('rechaza lo que no es de la herramienta y no cambia los datos', async ({ page }) => {
    await abrir(page);
    await importarJson(page, 'esto no es json', 'roto.json');
    await expect(page.getByText('roto.json: no es un JSON válido.')).toBeVisible();
    await importarJson(page, JSON.stringify({ esquema: 'otra-cosa', version: 1 }), 'ajeno.json');
    await expect(page.getByText(/ajeno\.json: no es un archivo de esta herramienta/)).toBeVisible();
    await importarJson(page, JSON.stringify({ ...archivo({ dispositivoId: 'disp-futuro1', evaluador: 'EV9' }), version: 2 }), 'futuro.json');
    await expect(page.getByText(/futuro\.json: es de una versión más nueva/)).toBeVisible();
    await expect(page.getByText(/0 registros agregados · 0 actualizados · 0 conservados/)).toBeVisible();
  });

  test('un archivo hostil se muestra como texto y no ejecuta nada (los datos importados no son de confianza)', async ({ page }) => {
    await abrir(page);
    const malo = archivo({ dispositivoId: 'disp-malo001', evaluador: '<b>EV</b>', personas: [{ id: 'per-malo0001', ronda: 1, codigo: '"><script>window.__xss=2</script>', resultado: 'sin-ayuda', problema: '<img src=x onerror="window.__xss=1">Texto', gravedad: 3 }] });
    await importarJson(page, malo, 'hostil.json');
    await ir(page, 'Usabilidad');
    await expect(page.getByRole('cell', { name: /<img src=x onerror=/ })).toBeVisible();
    await expect(page.locator('img[src="x"]')).toHaveCount(0);
    expect(await page.evaluate(() => (window as any).__xss)).toBeUndefined();
  });

  test('códigos como «__proto__» o «constructor» no rompen la vista de datos ni la exportación', async ({ page }) => {
    await abrir(page);
    const personas = ['__proto__', 'constructor', 'toString', 'constructor'].map((codigo, i) => ({ id: `per-proto000${i}`, ronda: 1 as const, codigo, resultado: 'sin-ayuda' as const }));
    await importarJson(page, archivo({ dispositivoId: 'disp-proto001', evaluador: 'EV1', personas }));
    await expect(page.getByText(/4 registros agregados/)).toBeVisible();
    await expect(page.getByText(/el código constructor se repite en 2 registros/)).toBeVisible();
    await page.getByRole('button', { name: 'Ver el texto' }).first().click();
    await expect(page.getByLabel('Texto exportado (selecciona y copia)')).toHaveValue(/"codigo": "__proto__"/);
  });

  test('ignora campos desconocidos, recorta lo largo y descarta ids inválidos', async ({ page }) => {
    await abrir(page);
    const extra: any = archivo({ dispositivoId: 'disp-extra001', evaluador: 'EV1', personas: [{ id: 'per-extra0001', ronda: 1, codigo: 'P1', resultado: 'sin-ayuda', problema: 'x'.repeat(5000) }, { id: 'id con espacios', ronda: 1, codigo: 'P2', resultado: 'sin-ayuda' }] });
    extra.datos.c.personas[0].campoInventado = 'no debe guardarse';
    await importarJson(page, extra, 'extra.json');
    await expect(page.getByText(/1 registros agregados/)).toBeVisible();
    const guardado = await page.evaluate((k) => JSON.parse(localStorage.getItem(k)!).c.personas, CLAVE_LS);
    expect(guardado).toHaveLength(1);
    expect(guardado[0].campoInventado).toBeUndefined();
    expect(guardado[0].problema).toHaveLength(800);
  });

  test('los datos sobreviven a recargar la página y «borrar» los quita también del almacenamiento', async ({ page }) => {
    await abrir(page);
    await page.getByLabel('Código de quien evalúa').fill('EV7');
    await ir(page, 'Navegadores');
    await page.getByRole('button', { name: 'Nueva corrida en Safari · iPhone real' }).click();
    await page.getByLabel('Versión del navegador').fill('17.5');
    await page.reload();
    await expect(page.getByLabel('Versión del navegador')).toHaveValue('17.5');
    await ir(page, 'Inicio');
    await expect(page.getByLabel('Código de quien evalúa')).toHaveValue('EV7');
    await ir(page, 'Datos');
    await page.getByRole('button', { name: 'Borrar los datos locales…' }).click();
    await page.getByRole('button', { name: 'Cancelar' }).click();
    expect(await page.evaluate((k) => localStorage.getItem(k) !== null, CLAVE_LS)).toBe(true);
    await page.getByRole('button', { name: 'Borrar los datos locales…' }).click();
    await page.getByRole('button', { name: 'Sí, borrar todo' }).click();
    await page.reload();
    await ir(page, 'Inicio');
    await expect(page.getByLabel('Código de quien evalúa')).toHaveValue('');
    await ir(page, 'Navegadores');
    await expect(page.getByRole('link', { name: /Safari/ })).toHaveCount(0);
  });
});
