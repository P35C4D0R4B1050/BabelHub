import { test, expect, type Download, type Page } from '@playwright/test';
import { CLAVE_LS, abrir, archivo, empezar, exportacion, hacerA, hacerB1, hacerC, importarJson, leerDescarga, part } from './ayudas';

// Cierre: un solo botón «Descargar mi resultado» (JSON + resumen Markdown), instrucciones simples, importación y combinación para quien reúne,
// privacidad (solo códigos; aviso si el texto libre parece un correo o un teléfono) y borrado.

test.setTimeout(180_000);

async function terminarTodo(page: Page, dificil = 'Encontrar el botón de pagar') {
  await empezar(page);
  await hacerC(page, { dificil });
  await hacerA(page);
  await hacerB1(page);
  await expect(page.getByRole('heading', { level: 1, name: '¡Muchas gracias!' })).toBeVisible();
}

async function descargarDos(page: Page, boton: () => Promise<void>): Promise<Download[]> {
  const descargas: Download[] = [];
  page.on('download', (d) => descargas.push(d));
  await boton();
  await expect.poll(() => descargas.length, { timeout: 10_000 }).toBe(2);
  return descargas;
}

test.describe('Gracias y «Descargar mi resultado»', () => {
  test('un solo botón baja el JSON y el resumen en Markdown, con instrucciones para enviarlos', async ({ page }) => {
    await terminarTodo(page);
    await expect(page.getByRole('button', { name: 'Descargar mi resultado' })).toHaveCount(1);
    await expect(page.getByRole('heading', { level: 2, name: 'Cómo enviarlo' })).toBeVisible();
    await expect(page.getByText(/WhatsApp/).first()).toBeVisible();
    await expect(page.getByText(/Descargas/).first()).toBeVisible();
    const [json, md] = await descargarDos(page, () => page.getByRole('button', { name: 'Descargar mi resultado' }).click());
    expect(json.suggestedFilename()).toMatch(/^babelhub-resultado_ev-[a-z0-9]{4}_\d{8}-\d{4}\.json$/);
    expect(md.suggestedFilename()).toMatch(/^babelhub-resultado_ev-[a-z0-9]{4}_\d{8}-\d{4}\.md$/);
    const ex = JSON.parse(await leerDescarga(json));
    expect(ex).toMatchObject({ esquema: 'babelhub-pruebas-humanas', version: 2 });
    expect(ex.participantes).toHaveLength(1);
    const p = ex.participantes[0];
    expect(p).toMatchObject({ bienvenida: true, combo: expect.any(String) });
    expect([p.a.estado, p.b1.estado, p.c.estado]).toEqual(['hecha', 'hecha', 'hecha']);
    expect(p.c).toMatchObject({ susPuntaje: 75, resultado: 'sin-ayuda', valida: true });
    expect(p.entorno).toMatchObject({ confirmado: true, pantalla: { ancho: expect.any(Number), alto: expect.any(Number) } });
    expect(p.entorno.ua).toContain('Mozilla');
    expect(ex.calculado.nota).toContain('veredicto definitivo');
    const texto = await leerDescarga(md);
    expect(texto).toBe(ex.resumenMarkdown);
    expect(texto).toContain('# Resultado de la prueba de BabelHub');
    expect(texto).toContain('autodeclarados');
    expect(texto).toContain('| Paso | ¿Pudo? | Nota | Captura (nombre) |');
    expect(texto).toContain('Pude llegar a todos los botones y enlaces solo con la tecla Tab.');
    expect(texto).toContain('SUS (0–100)');
    await expect(page.getByText(/se descargaron dos archivos/)).toBeVisible();
  });

  test('el archivo no trae nombres ni claves: solo código, entorno, respuestas y la dirección de la tienda sin datos de la persona', async ({ page }) => {
    await terminarTodo(page);
    const ex = await exportacion(page);
    const texto = JSON.stringify(ex);
    expect(texto).not.toMatch(/@[\w-]+\.\w/); // ningún correo (el de prueba de la tarjeta no se guarda)
    expect(Object.keys(ex.participantes[0]).sort()).toEqual(['a', 'act', 'apodo', 'b1', 'b2', 'bienvenida', 'c', 'codigo', 'combo', 'creada', 'entorno', 'id', 'tienda'].sort());
    expect(ex.participantes[0].tienda).toEqual({ url: 'https://tienda.ejemplo.test/', version: '2026-11-23' });
    const guardado = JSON.parse(await page.evaluate((k) => localStorage.getItem(k)!, CLAVE_LS));
    expect(Object.keys(guardado.yo.c.tarea)).toContain('inicio'); // lo crudo vive solo en el dispositivo
    expect(Object.keys(ex.participantes[0].c.tarea)).not.toContain('inicio');
  });

  test('avisa en el momento y antes de enviar si el texto libre parece un correo o un teléfono', async ({ page }) => {
    await empezar(page);
    await hacerC(page, { dificil: 'escríbeme a maria@correo.com o al 987 654 321' });
    await hacerA(page);
    await hacerB1(page);
    await expect(page.getByText(/Revisa antes de enviar/)).toBeVisible();
    await expect(page.getByText(/parece un correo electrónico/)).toBeVisible();
    // En el momento de escribir:
    await page.getByRole('link', { name: /Volver al inicio/ }).click();
    await page.getByRole('link', { name: /Revisar.*tarea Compra un libro/ }).click();
    for (let i = 0; i < 5; i++) await page.getByRole('button', { name: /^Siguiente/ }).click().catch(() => {});
  });

  test('mientras se escribe, el aviso sobre correos y teléfonos aparece solo y se va al quitarlo', async ({ page }) => {
    await empezar(page);
    await page.goto('/#/t/a');
    await page.getByRole('button', { name: /^Siguiente/ }).click();
    await page.getByRole('radio', { name: 'No', exact: true }).check();
    const campo = page.getByLabel('Cuéntanos qué pasó');
    await campo.fill('me llamo al 999 888 777');
    await expect(page.getByText(/Cuidado: esto tiene una secuencia larga de números/)).toBeVisible();
    await campo.fill('salió un error');
    await expect(page.getByText(/Cuidado: esto/)).toHaveCount(0);
    await campo.fill('mi correo es a@b.co');
    await expect(page.getByText(/Cuidado: esto parece un correo/)).toBeVisible();
  });

  test('si faltan tareas, se avisa pero se puede enviar lo que hay', async ({ page }) => {
    await empezar(page);
    await hacerB1(page);
    await page.goto('/#/gracias');
    await expect(page.getByRole('heading', { level: 1, name: 'Antes de enviar tu resultado' })).toBeVisible();
    await expect(page.getByText(/Todavía te faltan 2 tareas/)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Descargar mi resultado' })).toBeEnabled();
  });

  test('«Ver lo que se enviará» muestra el resumen para leerlo antes de enviarlo', async ({ page }) => {
    await terminarTodo(page);
    await page.getByText('Ver lo que se enviará').click();
    await expect(page.getByRole('textbox', { name: 'Resumen de tu resultado' })).toHaveValue(/# Resultado de la prueba de BabelHub/);
  });

  test('«Borrar mis datos» vacía el dispositivo y vuelve a la bienvenida', async ({ page }) => {
    await terminarTodo(page);
    await page.getByRole('button', { name: 'Borrar mis datos' }).click();
    await expect(page.getByRole('dialog')).toContainText('Todavía no descargaste tu resultado');
    await page.getByRole('button', { name: 'Sí, borrar' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Ayúdanos a probar BabelHub' })).toBeVisible();
    const guardado = JSON.parse(await page.evaluate((k) => localStorage.getItem(k)!, CLAVE_LS));
    expect(guardado.yo.bienvenida).toBe(false);
    expect(guardado.yo.c.estado).toBe('pendiente');
    expect((await exportacion(page)).participantes).toEqual([]);
  });

  test('tras descargar, ya no figura «sin descargar» en el aviso de borrado', async ({ page }) => {
    await terminarTodo(page);
    await descargarDos(page, () => page.getByRole('button', { name: 'Descargar mi resultado' }).click());
    await page.getByRole('button', { name: 'Borrar mis datos' }).click();
    await expect(page.getByRole('dialog')).not.toContainText('Todavía no descargaste');
  });
});

test.describe('Importar y combinar (para quien reúne los resultados)', () => {
  test('combina archivos de varias personas: suma lo distinto, gana el más reciente y no duplica al repetir', async ({ page }) => {
    await abrir(page);
    const a = part({ id: 'per-persona00001', codigo: 'EV-AAAA', a: 'si' });
    const b = part({ id: 'per-persona00002', codigo: 'EV-BBBB', nav: 'Firefox', ver: '133.0', a: 'si' });
    await importarJson(page, archivo([a, b]));
    await expect(page.getByRole('main').getByText(/1 archivo leído · 2 personas agregadas · 0 actualizadas · 0 conservadas/)).toBeVisible();
    // El mismo archivo otra vez: nada se duplica.
    await page.locator('#archivo-json').setInputFiles({ name: 'otra-vez.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(archivo([a, b]))) });
    await expect(page.getByRole('main').getByText(/0 personas agregadas · 0 actualizadas · 2 conservadas/)).toBeVisible();
    // Una versión más reciente de la persona A gana; una más vieja se ignora.
    const nuevaA = { ...a, act: '2026-11-25T10:00:00.000Z', apodo: 'nueva' };
    const viejaB = { ...b, act: '2026-11-01T10:00:00.000Z', apodo: 'vieja' };
    await page.locator('#archivo-json').setInputFiles({ name: 'cambios.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(archivo([nuevaA, viejaB, part({ id: 'per-persona00003', codigo: 'EV-CCCC', a: 'si' })]))) });
    await expect(page.getByRole('main').getByText(/1 personas agregadas · 1 actualizadas · 1 conservadas/)).toBeVisible();
    const guardado = JSON.parse(await page.evaluate((k) => localStorage.getItem(k)!, CLAVE_LS));
    expect(guardado.otros.map((p: any) => [p.codigo, p.apodo])).toEqual([['EV-AAAA', 'nueva'], ['EV-BBBB', ''], ['EV-CCCC', '']]);
    await expect(page.getByRole('table', { name: 'Personas reunidas' }).getByRole('row')).toHaveCount(4);
  });

  test('«Descargar todo» incluye lo reunido y lo de esta persona', async ({ page }) => {
    await empezar(page);
    await hacerB1(page);
    await importarJson(page, archivo([part({ id: 'per-persona00001', codigo: 'EV-AAAA', a: 'si' })]));
    const [json] = await descargarDos(page, () => page.getByRole('button', { name: /Descargar todo/ }).click());
    const ex = JSON.parse(await leerDescarga(json));
    expect(ex.participantes).toHaveLength(2);
    expect(json.suggestedFilename()).toContain('reunido');
    // El archivo reunido se puede volver a importar sin duplicar a nadie (una vacía lo recibe).
    await page.evaluate(() => localStorage.clear());
    await abrir(page);
    await importarJson(page, ex);
    await expect(page.getByRole('main').getByText(/2 personas agregadas/)).toBeVisible();
  });

  test('se puede pegar el JSON en vez de elegir un archivo', async ({ page }) => {
    await abrir(page);
    await page.goto('/#/avanzado');
    await page.getByRole('button', { name: /Pegar el JSON/ }).click();
    await page.getByLabel('JSON pegado').fill(JSON.stringify(archivo([part({ id: 'per-pegado000001', codigo: 'EV-PPPP', a: 'si' })])));
    await page.getByRole('button', { name: 'Importar el texto pegado' }).click();
    await expect(page.getByRole('main').getByText(/1 personas agregadas/)).toBeVisible();
  });

  test('un archivo de la versión 1 de la herramienta se rechaza con un mensaje claro', async ({ page }) => {
    await abrir(page);
    await importarJson(page, { esquema: 'babelhub-pruebas-humanas', version: 1, datos: { a: { corridas: [] }, b: { sesiones: [] }, c: { personas: [] } } }, 'viejo.json');
    await expect(page.getByRole('main').getByText(/viejo\.json: es de la versión 1 de la herramienta/)).toBeVisible();
    await importarJson(page, { esquema: 'babelhub-pruebas-humanas', version: 9, participantes: [] }, 'nuevo.json');
    await expect(page.getByRole('main').getByText(/nuevo\.json: es de una versión más nueva/)).toBeVisible();
  });

  test('el resumen en Markdown no deja pasar imágenes, enlaces ni HTML de textos importados', async ({ page }) => {
    await abrir(page);
    const p: any = part({ id: 'per-markdown00001', codigo: 'EV-MMMM', a: 'si', b1: 'si' });
    p.a.pasos.inicio = { r: 'falla', n: '![x](https://malo.example/p.png) [clic](https://malo.example) <img src=x onerror=1>', captura: '' };
    p.b1.nota = '# titulo falso\n- punto';
    await importarJson(page, archivo([p]));
    const md: string = await page.evaluate(() => (window as any).__babelhubPruebasHumanas.construirExport().resumenMarkdown);
    expect(md).not.toMatch(/(?<!\\)!\[/);
    expect(md).not.toMatch(/(?<!\\)\[clic\]/);
    expect(md).not.toMatch(/(?<!\\)<img/);
    expect(md.split('\n').filter((l) => l.startsWith('# titulo'))).toEqual([]);
  });

  test('hay un tope de personas reunidas y se avisa al alcanzarlo', async ({ page }) => {
    await abrir(page);
    const muchas = Array.from({ length: 300 }, (_, i) => part({ id: `per-tope${String(i).padStart(6, '0')}`, a: 'si' }));
    await importarJson(page, archivo(muchas));
    await importarJson(page, archivo([part({ id: 'per-tope-extra001', a: 'si' })]));
    await expect(page.getByRole('main').getByText(/Se alcanzó el tope de 300 personas/)).toBeVisible();
  });

  test('rechaza lo que no es de la herramienta y no cambia los datos', async ({ page }) => {
    await abrir(page);
    await importarJson(page, { hola: 'mundo' }, 'ajeno.json');
    await expect(page.getByRole('main').getByText(/ajeno\.json: no es un archivo de esta herramienta/)).toBeVisible();
    await importarJson(page, 'esto no es json', 'roto.json');
    await expect(page.getByRole('main').getByText(/roto\.json: no es un JSON válido/)).toBeVisible();
    await importarJson(page, archivo([]), 'vacio.json');
    await expect(page.getByRole('main').getByText(/vacio\.json: no trae ninguna persona/)).toBeVisible();
    await expect(page.getByRole('main').getByText(/Todavía no hay resultados/)).toBeVisible();
  });

  test('un archivo hostil se muestra como texto y no ejecuta nada', async ({ page }) => {
    await abrir(page);
    const malo = part({ id: 'per-hostil000001', codigo: '<img src=x onerror="window.__x=1">', a: 'si' });
    (malo.entorno as any).navegador = '<script>window.__x=2</script>';
    await importarJson(page, archivo([malo]));
    await expect(page.getByRole('table', { name: 'Personas reunidas' })).toContainText('<img src=x onerr');
    expect(await page.evaluate(() => (window as any).__x)).toBeUndefined();
    await expect(page.locator('main img, main script')).toHaveCount(0);
  });

  test('códigos como «__proto__» o «constructor» no rompen la vista ni la exportación', async ({ page }) => {
    await abrir(page);
    await importarJson(page, archivo([part({ id: 'per-proto0000001', codigo: '__proto__', a: 'si' }), part({ id: 'per-proto0000002', codigo: '__proto__', a: 'si' }), part({ id: 'per-proto0000003', codigo: 'constructor', a: 'si' })]));
    await expect(page.getByRole('table', { name: 'Personas reunidas' })).toBeVisible();
    const c = await page.evaluate(() => (window as any).__babelhubPruebasHumanas.calcular());
    expect(c.avisos.join(' ')).toContain('El código __proto__ se repite en 2 personas');
    expect(Object.keys(Object.prototype)).toEqual([]);
  });

  test('ignora campos desconocidos, recorta lo largo, descarta ids inválidos y valores fuera de lo permitido', async ({ page }) => {
    await abrir(page);
    const p: any = part({ id: 'per-recorte000001', codigo: 'EV-RRRR', a: 'si' });
    p.campoRaro = 'x'; p.entorno.sistema = 'S'.repeat(500); p.a.completo = 'quizás'; p.c = { ...p.c, ronda: '7' };
    await importarJson(page, archivo([p, part({ id: 'no valido!', a: 'si' }), { sin: 'id' }]));
    await expect(page.getByRole('main').getByText(/1 personas agregadas/)).toBeVisible();
    const guardado = JSON.parse(await page.evaluate((k) => localStorage.getItem(k)!, CLAVE_LS));
    expect(guardado.otros).toHaveLength(1);
    expect(guardado.otros[0].campoRaro).toBeUndefined();
    expect(guardado.otros[0].entorno.sistema.length).toBeLessThanOrEqual(30);
    expect(guardado.otros[0].a.completo).toBe('');
    expect(guardado.otros[0].c.ronda).toBe('');
  });

  test('lo importado y lo propio sobreviven a recargar; «Borrar los datos locales» lo quita todo', async ({ page }) => {
    await abrir(page);
    await importarJson(page, archivo([part({ id: 'per-persona00001', codigo: 'EV-AAAA', a: 'si' })]));
    await page.reload();
    await expect(page.getByRole('table', { name: 'Personas reunidas' })).toContainText('EV-AAAA');
    await page.getByRole('button', { name: 'Borrar los datos locales…' }).click();
    await page.getByRole('button', { name: 'Sí, borrar todo' }).click();
    expect(await page.evaluate((k) => JSON.parse(localStorage.getItem(k)!).otros.length, CLAVE_LS)).toBe(0);
  });
});
