import { test, expect } from '@playwright/test';
import { ENLACE, abrir, empezar, exportacion, hacerA, hacerB1, hacerB2, hacerC, irTarea, siguiente, elegir } from './ayudas';

// El recorrido de una persona común, sin moderador: bienvenida → tareas de a una → gracias.
// Cubre el progreso («X de 3 tareas»), el guardado automático, retomar otro día y los datos que se piden (los mínimos, una sola vez).

test.setTimeout(180_000);

test.describe('Bienvenida', () => {
  test('arranca vacía, pide lo mínimo (confirmar el navegador) y trae la tienda del enlace', async ({ page }) => {
    await abrir(page);
    await expect(page.getByText(/^Tu código es/)).toContainText(/EV-[A-HJ-NP-Z2-9]{4}/);
    await expect(page.getByText('Tienda de prueba: tienda.ejemplo.test')).toBeVisible();
    await expect(page.getByText(/versión 2026-11-23/)).toBeVisible();
    await expect(page.getByRole('textbox', { name: /dirección de la tienda/i })).toHaveCount(0); // ya viene en el enlace: no se vuelve a preguntar
    const ex = await exportacion(page);
    expect(ex.participantes).toEqual([]); // nada precargado: sin «bienvenida» no hay persona que exportar
    await page.getByRole('button', { name: 'Sí, es correcto' }).click();
    await page.getByRole('button', { name: 'Empezar' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Gracias por ayudarnos' })).toBeVisible();
    await expect(page.getByText(/Tu progreso: 0 de 3 tareas/)).toBeVisible();
  });

  test('sin dirección en el enlace pregunta la tienda UNA vez y la valida (solo http o https)', async ({ page }) => {
    await abrir(page, '/');
    await page.getByRole('button', { name: 'Sí, es correcto' }).click();
    await page.getByRole('button', { name: 'Empezar' }).click();
    await expect(page.getByRole('alert')).toContainText('dirección de la tienda');
    const campo = page.getByRole('textbox', { name: /dirección de la tienda/i });
    await campo.fill('javascript:alert(1)');
    await page.getByRole('button', { name: 'Empezar' }).click();
    await expect(page.getByRole('alert')).toContainText('dirección de la tienda');
    await campo.fill('ftp://tienda.ejemplo.test');
    await page.getByRole('button', { name: 'Empezar' }).click();
    await expect(page.getByRole('alert')).toBeVisible();
    await campo.fill('https://usuario:clave@tienda.ejemplo.test/');
    await page.getByRole('button', { name: 'Empezar' }).click();
    await expect(page.getByRole('alert')).toBeVisible();
    await campo.fill('tienda.ejemplo.test');
    await page.getByRole('button', { name: 'Empezar' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Gracias por ayudarnos' })).toBeVisible();
    const ex = await exportacion(page);
    expect(ex.participantes[0].tienda.url).toBe('https://tienda.ejemplo.test/');
  });

  test('parámetros del enlace: una dirección que no es http(s) se ignora; la versión y la ronda se leen', async ({ page }) => {
    await abrir(page, '/?tienda=javascript:alert(1)&v=abc&ronda=2');
    await expect(page.getByRole('textbox', { name: /dirección de la tienda/i })).toBeVisible();
    await page.getByRole('button', { name: 'Sí, es correcto' }).click();
    await page.getByRole('textbox', { name: /dirección de la tienda/i }).fill('https://tienda.ejemplo.test');
    await page.getByRole('button', { name: 'Empezar' }).click();
    const p = (await exportacion(page)).participantes[0];
    expect(p.tienda).toMatchObject({ url: 'https://tienda.ejemplo.test/', version: 'abc' });
    expect(p.c.ronda).toBe('2');
  });

  test('antes de empezar dice todo lo que se guarda, quién es responsable, por cuánto tiempo y cómo ejercer los derechos', async ({ page }) => {
    await abrir(page);
    await expect(page.getByRole('heading', { level: 2, name: 'Qué guardamos de ti' })).toBeVisible();
    for (const t of [/solo en tu dispositivo/, /tamaño de tu pantalla/, /nombre.*de las capturas/, /pueden revelar una discapacidad/, /Responsable/, /Ley N.º 29733/, /hasta cerrar la verificación/, /acceder, corregir, borrar, oponerte/, /Al pulsar «Empezar» aceptas esto/]) await expect(page.getByRole('main').getByText(t).first()).toBeVisible();
  });

  test('el apodo es opcional y no se pide el nombre', async ({ page }) => {
    await abrir(page);
    await expect(page.getByText(/No te pedimos tu nombre/)).toBeVisible();
    await expect(page.getByLabel(/Nombre/)).toHaveCount(0);
    await page.getByLabel(/Un apodo/).fill('Lectora');
    await page.getByRole('button', { name: 'Sí, es correcto' }).click();
    await page.getByRole('button', { name: 'Empezar' }).click();
    expect((await exportacion(page)).participantes[0].apodo).toBe('Lectora');
  });
});

test.describe('Inicio y progreso', () => {
  test('muestra una tarjeta por tarea, sin formularios desplegados, y «Te toca» en la primera pendiente', async ({ page }) => {
    await empezar(page);
    await expect(page.getByRole('article')).toHaveCount(4); // 3 tareas + la opcional del lector
    await expect(page.locator('main input, main textarea, main select')).toHaveCount(0);
    await expect(page.getByRole('progressbar', { name: 'Tu progreso' })).toHaveAttribute('aria-valuenow', '0');
    await expect(page.locator('.tarea.toca')).toContainText('Compra un libro');
    await expect(page.getByRole('link', { name: /Abrir la tienda de prueba/ })).toHaveAttribute('href', 'https://tienda.ejemplo.test/');
  });

  test('cada tarea hecha sube el progreso y la siguiente pasa a «Te toca»', async ({ page }) => {
    await empezar(page);
    await hacerC(page);
    await expect(page.getByText(/Tu progreso: 1 de 3 tareas/)).toBeVisible();
    await expect(page.getByText(/Tarea «Compra un libro» lista/)).toBeVisible();
    await expect(page.locator('.tarea.toca')).toContainText('Recorre la compra paso a paso');
    await hacerA(page);
    await expect(page.getByText(/Tu progreso: 2 de 3 tareas/)).toBeVisible();
    await expect(page.locator('.tarea.toca')).toContainText('solo con el teclado');
    await hacerB1(page);
    await expect(page.getByRole('heading', { level: 1, name: '¡Muchas gracias!' })).toBeVisible();
  });
});

test.describe('Tarea: compra un libro (usabilidad sin moderador)', () => {
  test('consentimiento breve, perfil en rangos, cronómetro automático, preguntas y SUS automático', async ({ page }) => {
    await empezar(page);
    await irTarea(page, /Empezar.*tarea Compra un libro/);
    await expect(page.getByRole('heading', { level: 1, name: 'Antes de empezar' })).toBeVisible();
    await expect(page.getByText(/Probamos la tienda, no a ti/)).toBeVisible();
    // Sin marcar el consentimiento no se avanza y se explica por qué.
    await siguiente(page);
    await expect(page.getByRole('alert')).toContainText('Marca las tres casillas');
    for (const t of [/Leí y entendí/, /Sé que puedo parar/, /Acepto participar/]) await page.getByRole('checkbox', { name: t }).click();
    await siguiente(page);
    await expect(page.getByRole('heading', { level: 1, name: 'Cuéntanos un poco de ti' })).toBeVisible();
    await siguiente(page);
    await expect(page.getByRole('alert')).toContainText('Responde las cuatro preguntas');
    await page.getByRole('radio', { name: '35 a 44 años' }).click();
    await page.getByRole('radio', { name: 'Una o varias veces al mes' }).click();
    await elegir(page, /ayuda para ver/, 'Sí');
    await elegir(page, /Ya habías visto/, 'No');
    await siguiente(page);
    // Tarea con cronómetro: «Siguiente» queda inactivo hasta pulsar «Terminé».
    await expect(page.getByText(/El zorro y el cóndor del Ande/)).toBeVisible();
    await expect(page.getByRole('button', { name: /^Siguiente/ })).toBeDisabled();
    await page.getByRole('button', { name: 'Empezar tarea' }).click();
    await expect(page.getByRole('timer')).toBeVisible();
    await expect.poll(async () => (await page.getByRole('timer').textContent())?.trim(), { timeout: 8000 }).not.toBe('00:00');
    await expect(page.getByRole('link', { name: /Abrir la tienda de prueba/ })).toHaveAttribute('target', '_blank');
    await page.getByRole('button', { name: 'Terminé' }).click();
    await expect(page.getByText(/Tardaste 00:0\d/)).toBeVisible();
    await siguiente(page);
    await siguiente(page);
    await expect(page.getByRole('alert')).toContainText('Responde las tres preguntas');
    await elegir(page, /Lograste comprar/, 'Sí');
    await elegir(page, /Pediste ayuda/, /No, lo hice solo o sola/);
    await elegir(page, /Cuántas veces/, '1 vez');
    await siguiente(page);
    // SUS: sin responder las 10 no se avanza.
    await expect(page.getByRole('heading', { level: 1, name: 'Tu opinión sobre la tienda' })).toBeVisible();
    await expect(page.locator('fieldset.card')).toHaveCount(10);
    await siguiente(page);
    await expect(page.getByRole('alert')).toContainText('Te faltan 10 frase(s)');
    for (let i = 1; i <= 10; i++) await page.getByRole('group', { name: new RegExp(`^${i}\\. `) }).getByRole('radio', { name: new RegExp(`^${i % 2 ? 4 : 2}:`) }).click();
    await siguiente(page);
    await page.getByLabel(/lo más difícil/).fill('Encontrar el botón de pagar');
    await siguiente(page);
    await expect(page.getByText(/Tu progreso: 1 de 3 tareas/)).toBeVisible();
    const c = (await exportacion(page)).participantes[0].c;
    expect(c).toMatchObject({ estado: 'hecha', resultado: 'sin-ayuda', susPuntaje: 75, erroresN: 1, valida: true, despuesDeA: false });
    expect(c.perfil).toMatchObject({ edad: '35-44', experiencia: 'mensual', apoyo: 'si', conocia: 'no' });
    expect(c.consentimiento).toMatchObject({ leido: true, retiro: true, acepta: true });
    expect(c.tarea.tiempoSeg).toBeGreaterThanOrEqual(1);
    expect(c.tarea.inicio).toBeUndefined(); // las marcas de tiempo crudas no salen en el archivo
  });

  test('«No pude terminarla» cuenta como que no completó; pedir ayuda cuenta como con ayuda', async ({ page }) => {
    await empezar(page);
    await hacerC(page, { lograste: 'No', ayuda: true, errores: '3 o más' });
    const c = (await exportacion(page)).participantes[0].c;
    expect(c).toMatchObject({ resultado: 'no-completa', erroresN: 3 });
    // Otra persona: logró pero pidió ayuda.
    await page.evaluate(() => localStorage.clear());
    await empezar(page);
    await hacerC(page, { ayuda: true });
    expect((await exportacion(page)).participantes[0].c.resultado).toBe('con-ayuda');
  });

  test('el cronómetro sigue contando si se recarga la página', async ({ page }) => {
    await empezar(page);
    await irTarea(page, /Empezar.*tarea Compra un libro/);
    for (const t of [/Leí y entendí/, /Sé que puedo parar/, /Acepto participar/]) await page.getByRole('checkbox', { name: t }).click();
    await siguiente(page);
    await page.getByRole('radio', { name: '25 a 34 años' }).click();
    await page.getByRole('radio', { name: 'Pocas veces al año' }).click();
    await elegir(page, /ayuda para ver/, 'No');
    await elegir(page, /Ya habías visto/, 'No');
    await siguiente(page);
    await page.getByRole('button', { name: 'Empezar tarea' }).click();
    await page.waitForTimeout(2200);
    await page.reload();
    await expect(page.getByRole('timer')).toBeVisible();
    await expect.poll(async () => (await page.getByRole('timer').textContent())?.trim(), { timeout: 5000 }).not.toBe('00:00');
    await expect(page.getByRole('timer')).not.toHaveText('00:00');
  });

  test('si ya conocía la tienda, o hizo antes la compra guiada, no cuenta como primera vez', async ({ page }) => {
    await empezar(page);
    await hacerC(page, { conocia: 'Sí' });
    const c = (await exportacion(page)).participantes[0].c;
    expect(c.valida).toBe(false);
    await page.evaluate(() => localStorage.clear());
    await empezar(page);
    await irTarea(page, /Empezar.*tarea Recorre la compra/); // solo entrar a la compra guiada ya cuenta como haberla empezado
    await page.getByRole('link', { name: /Volver al inicio/ }).click();
    await hacerC(page);
    const c2 = (await exportacion(page)).participantes[0].c;
    expect(c2).toMatchObject({ despuesDeA: true, valida: false });
  });

  test('avisa al empezar la compra guiada antes que esta tarea', async ({ page }) => {
    await empezar(page);
    await irTarea(page, /Empezar.*tarea Recorre la compra/);
    await expect(page.getByText(/hazla primero/)).toBeVisible();
  });
});

test.describe('Tarea: compra paso a paso (navegador)', () => {
  test('ocho pasos de uno en uno con «¿Pudiste hacerlo?»; «Con dificultad» pide contar qué pasó', async ({ page }) => {
    await empezar(page);
    await irTarea(page, /Empezar.*tarea Recorre la compra/);
    await expect(page.getByRole('heading', { level: 1, name: 'Cómo funciona' })).toBeVisible();
    await siguiente(page);
    await expect(page.getByRole('heading', { level: 1, name: 'Paso 1 de 8: Abre la tienda' })).toBeVisible();
    await expect(page.locator('.paso-n')).toContainText('Paso 2 de 10');
    await siguiente(page);
    await expect(page.getByRole('alert')).toContainText('Elige una opción');
    await page.getByRole('radio', { name: 'Con dificultad' }).click();
    await siguiente(page);
    await expect(page.getByRole('alert')).toContainText('Cuéntanos en una frase');
    await page.getByLabel('Cuéntanos qué pasó').fill('La página tardó mucho en cargar');
    await siguiente(page);
    await expect(page.getByRole('heading', { level: 1, name: 'Paso 2 de 8: Entra al catálogo' })).toBeVisible();
  });

  test('la captura es opcional y solo se anota su nombre (no se sube nada)', async ({ page }) => {
    await empezar(page);
    await irTarea(page, /Empezar.*tarea Recorre la compra/);
    await siguiente(page);
    await page.getByRole('radio', { name: 'No', exact: true }).click();
    await page.getByLabel('Cuéntanos qué pasó').fill('Salió un error');
    await page.locator('input[type=file]').setInputFiles({ name: 'error-pago.png', mimeType: 'image/png', buffer: Buffer.from('no-es-una-imagen-real') });
    await expect(page.getByText('error-pago.png')).toBeVisible();
    const p = (await exportacion(page)).participantes[0];
    expect(p.a.pasos.inicio).toEqual({ r: 'falla', n: 'Salió un error', captura: 'error-pago.png' });
    expect(JSON.stringify(p)).not.toContain('no-es-una-imagen-real');
    await page.getByRole('button', { name: 'Quitar' }).click();
    await expect(page.getByText('error-pago.png')).toHaveCount(0);
  });

  test('al final pregunta si completó la compra y si vio algo raro; el resultado queda en el JSON', async ({ page }) => {
    await empezar(page);
    await hacerA(page, { respuesta: 'Sí', completo: 'No' });
    const a = (await exportacion(page)).participantes[0].a;
    expect(a).toMatchObject({ estado: 'hecha', completo: 'no' });
    expect(a.defectos).toEqual([]);
    await page.evaluate(() => localStorage.clear());
    await empezar(page);
    await hacerA(page, { respuesta: 'No', completo: 'No' });
    const a2 = (await exportacion(page)).participantes[0].a;
    expect(a2.defectos).toHaveLength(8);
    expect(a2.defectos[0]).toMatchObject({ gravedad: 'bloqueante', desc: 'No encontré el botón' });
  });

  test('«¿Viste algo raro?» ofrece opciones en lenguaje llano', async ({ page }) => {
    await empezar(page);
    await irTarea(page, /Empezar.*tarea Recorre la compra/);
    await siguiente(page);
    for (let i = 1; i <= 8; i++) { await page.getByRole('radio', { name: 'Sí', exact: true }).click(); await siguiente(page); }
    await expect(page.getByRole('heading', { level: 1, name: 'Para terminar' })).toBeVisible();
    await page.getByRole('checkbox', { name: 'Un mensaje de error' }).click();
    await page.getByRole('checkbox', { name: 'La página se movía hacia los lados' }).click();
    await elegir(page, /completar la compra/, 'Sí');
    await page.getByLabel('Cuéntanos más (opcional)').fill('Se movía en el carrito');
    await siguiente(page);
    const a = (await exportacion(page)).participantes[0].a;
    expect(a.raro.tipos.sort()).toEqual(['error', 'lado']);
  });
});

test.describe('Tarea: solo con el teclado', () => {
  test('con teclado: teclas ilustradas, recorrido, 6 preguntas de 5 puntos y la pregunta de bloqueo', async ({ page }) => {
    await empezar(page);
    await irTarea(page, /Empezar.*tarea Usa la tienda solo con el teclado/);
    await elegir(page, /teclado a mano/, /Sí, tengo teclado/);
    await siguiente(page);
    await expect(page.getByRole('heading', { level: 1, name: 'Las teclas que vas a usar' })).toBeVisible();
    for (const t of ['Tecla Tab', 'Tecla Enter', 'Tecla Espacio', 'Tecla Escape']) await expect(page.getByRole('img', { name: t })).toBeVisible();
    await expect(page.getByRole('img', { name: /Teclas de flechas/ })).toBeVisible();
    await siguiente(page);
    await expect(page.getByRole('heading', { level: 1, name: 'Haz este recorrido' })).toBeVisible();
    await siguiente(page);
    await expect(page.getByRole('alert')).toContainText('Elige un tema');
    await elegir(page, /Con qué tema/, 'Oscuro');
    await siguiente(page);
    await expect(page.locator('fieldset.card')).toHaveCount(6);
    await expect(page.getByText('Pude llegar a todos los botones y enlaces solo con la tecla Tab.')).toBeVisible();
    await expect(page.getByText('1 · Muy en desacuerdo').first()).toBeVisible();
    await expect(page.getByText('5 · Muy de acuerdo').first()).toBeVisible();
    await siguiente(page);
    await expect(page.getByRole('alert')).toContainText('Te faltan 6 pregunta(s)');
    await page.locator('fieldset.card').nth(0).getByRole('radio', { name: /^5:/ }).click();
    await page.locator('fieldset.card').nth(1).getByRole('radio', { name: /^2:/ }).click();
    await page.locator('fieldset.card').nth(2).getByRole('checkbox', { name: /No me tocó/ }).click();
    for (const i of [3, 4, 5]) await page.locator('fieldset.card').nth(i).getByRole('radio', { name: /^3:/ }).click();
    await siguiente(page);
    await elegir(page, /no pudiste continuar usando solo el teclado/, 'Sí');
    await siguiente(page);
    await expect(page.getByRole('alert')).toContainText('dónde te quedaste');
    await page.getByLabel(/Dónde pasó/).fill('En el pago, el foco desapareció');
    await siguiente(page);
    const b1 = (await exportacion(page)).participantes[0].b1;
    expect(b1).toMatchObject({ estado: 'hecha', teclado: 'si', tema: 'oscuro', bloqueo: 'si', criticos: 1 });
    expect(b1.items.T1).toEqual({ v: 5, na: false });
    expect(b1.items.T3).toEqual({ v: null, na: true });
    expect((await exportacion(page)).calculado.b.criticos).toEqual([{ codigo: expect.any(String), bloque: 'B1 (teclado)', nota: 'En el pago, el foco desapareció' }]);
  });

  test('sin teclado se puede saltar la tarea y cuenta como resuelta (no como hecha)', async ({ page }) => {
    await empezar(page);
    await irTarea(page, /Empezar.*tarea Usa la tienda solo con el teclado/);
    await elegir(page, /teclado a mano/, /No tengo/);
    await expect(page.getByText('esta tarea necesita teclado')).toBeVisible();
    await siguiente(page);
    await expect(page.getByRole('alert')).toContainText('Saltar esta tarea');
    await page.getByRole('button', { name: 'Saltar esta tarea' }).click();
    await expect(page.getByText(/Tu progreso: 1 de 3 tareas/)).toBeVisible();
    await expect(page.locator('.tarea', { hasText: 'solo con el teclado' }).getByText('Saltada')).toBeVisible();
    expect((await exportacion(page)).participantes[0].b1.estado).toBe('omitida');
  });
});

test.describe('Tarea opcional: lector de pantalla', () => {
  test('guía de NVDA y de VoiceOver paso a paso; 10 preguntas; no cuenta en «X de 3»', async ({ page }) => {
    await empezar(page);
    await irTarea(page, /Empezar.*tarea Lector de pantalla/);
    await expect(page.getByText(/Un lector de pantalla dice en voz alta/)).toBeVisible();
    await elegir(page, /Con qué lector/, /NVDA/);
    await siguiente(page);
    await expect(page.getByText(/nvaccess\.org/)).toBeVisible();
    await expect(page.getByText(/Ctrl \+ Alt \+ N/)).toBeVisible();
    await page.getByRole('button', { name: '‹ Atrás' }).click();
    await elegir(page, /Con qué lector/, /VoiceOver/);
    await siguiente(page);
    await expect(page.getByText(/triple clic/)).toBeVisible();
    await expect(page.getByText(/dibuja una «Z»/)).toBeVisible();
    await siguiente(page);
    await elegir(page, /Con qué tema/, 'Claro');
    await siguiente(page);
    await expect(page.locator('fieldset.card')).toHaveCount(10);
    await siguiente(page);
    await expect(page.getByRole('alert')).toContainText('Te faltan 10 pregunta(s)');
  });

  test('completa con VoiceOver y bloqueo: queda como incumplimiento crítico del bloque del lector', async ({ page }) => {
    await empezar(page);
    await hacerB2(page, { lector: /VoiceOver/, bloqueo: true });
    await expect(page.getByText(/Tu progreso: 0 de 3 tareas/)).toBeVisible();
    const ex = await exportacion(page);
    expect(ex.participantes[0].b2).toMatchObject({ estado: 'hecha', lector: 'voiceover', bloqueo: 'si', criticos: 1 });
    expect(ex.calculado.b.criticos[0].bloque).toBe('B2 (lector)');
  });
});

test.describe('Se guarda solo y se puede continuar otro día', () => {
  test('al recargar a mitad de una tarea, «Continuar» vuelve al mismo paso con lo contestado', async ({ page }) => {
    await empezar(page);
    await irTarea(page, /Empezar.*tarea Recorre la compra/);
    await siguiente(page);
    await page.getByRole('radio', { name: 'Con dificultad' }).click();
    await page.getByLabel('Cuéntanos qué pasó').fill('Tardó en cargar');
    await siguiente(page);
    await expect(page.getByRole('heading', { level: 1, name: 'Paso 2 de 8: Entra al catálogo' })).toBeVisible();
    await page.goto(ENLACE); // «otro día»: se vuelve a abrir el enlace
    await expect(page.getByRole('heading', { level: 1, name: 'Gracias por ayudarnos' })).toBeVisible();
    await expect(page.locator('.tarea', { hasText: 'Recorre la compra' }).getByText('En curso')).toBeVisible();
    await irTarea(page, /Continuar.*tarea Recorre la compra/);
    await expect(page.getByRole('heading', { level: 1, name: 'Paso 2 de 8: Entra al catálogo' })).toBeVisible();
    await page.getByRole('button', { name: '‹ Atrás' }).click();
    await expect(page.getByLabel('Cuéntanos qué pasó')).toHaveValue('Tardó en cargar');
  });

  test('el texto escrito se guarda sin pulsar nada y sobrevive a recargar', async ({ page }) => {
    await empezar(page);
    await irTarea(page, /Empezar.*tarea Recorre la compra/);
    await siguiente(page);
    await page.getByRole('radio', { name: 'No', exact: true }).click();
    await page.getByLabel('Cuéntanos qué pasó').pressSequentially('se cerró solo');
    await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('babelhub-pruebas-humanas:datos:v2')!).yo.a.pasos.inicio.n)).toBe('se cerró solo');
    await page.goto(ENLACE);
    await irTarea(page, /Continuar.*tarea Recorre la compra/);
    await expect(page.getByLabel('Cuéntanos qué pasó')).toHaveValue('se cerró solo');
  });

  test('el progreso, el código y el navegador confirmado se conservan al volver a abrir', async ({ page }) => {
    await empezar(page);
    const codigo = (await exportacion(page)).participantes[0].codigo;
    await hacerC(page);
    await page.goto('/?tienda=https://otra.ejemplo.test/'); // otro enlace, mismo dispositivo: no se vuelve a pedir nada
    await expect(page.getByText(/Tu progreso: 1 de 3 tareas/)).toBeVisible();
    const p = (await exportacion(page)).participantes[0];
    expect(p.codigo).toBe(codigo);
    expect(p.entorno.confirmado).toBe(true);
  });
});

test.describe('Sin almacenamiento (modo privado)', () => {
  test('avisa con claridad y permite seguir', async ({ page }) => {
    await page.addInitScript(() => {
      const fallo = () => { throw new DOMException('cuota', 'QuotaExceededError'); };
      Storage.prototype.setItem = fallo;
      Object.defineProperty(Storage.prototype, 'getItem', { configurable: true, value: () => { throw new DOMException('bloqueado', 'SecurityError'); } });
    });
    await abrir(page);
    await expect(page.getByText(/este navegador no deja guardar/i).first()).toBeVisible();
    await expect(page.getByText(/no cierres esta pestaña/i).first()).toBeVisible();
    await page.getByRole('button', { name: 'Sí, es correcto' }).click();
    await page.getByRole('button', { name: 'Empezar' }).click();
    await expect(page.getByRole('status').filter({ hasText: /No cierres esta pestaña/ })).toBeVisible();
    await hacerB1(page);
    await expect(page.getByText(/Tu progreso: 1 de 3 tareas/)).toBeVisible();
  });
});
