import { test, expect, type Page } from '@playwright/test';
import { abrir, archivo, corridaCompleta, importarJson, ir, sesionCompleta } from './ayudas';

// Los tres bloques paso a paso, como los usaría la persona: A (navegadores), B (teclado y lector) y C (moderador de usabilidad).
// Los datos son de ejemplo y solo viven en estas pruebas; no se inventa ningún resultado de personas.

const calcular = (page: Page) => page.evaluate(() => (window as any).__babelhubPruebasHumanas.calcular());

async function marcarTodo(page: Page, valor: 'ok') {
  await expect(page.locator(`input[type=radio][value=${valor}]`).first()).toBeAttached();
  const n = await page.locator(`input[type=radio][value=${valor}]`).count();
  for (let i = 0; i < n; i++) await page.locator(`input[type=radio][value=${valor}]`).nth(i).click();
}

test.describe('A · Navegadores y móviles', () => {
  test('una corrida completa por la interfaz: cumple con reserva con una versión y cumple con dos', async ({ page }) => {
    await abrir(page);
    await ir(page, 'Navegadores');
    for (const version of ['125.0', '126.0']) {
      await page.getByRole('button', { name: 'Nueva corrida en Chrome · Escritorio' }).click();
      await page.getByLabel('Versión del navegador').fill(version);
      await marcarTodo(page, 'ok');
      await page.getByRole('group', { name: '¿Se completó la compra?' }).getByRole('radio', { name: 'Sí', exact: true }).click();
      await expect(page.getByText('Cumple', { exact: true }).first()).toBeVisible();
      await ir(page, 'Navegadores');
      if (version === '125.0') {
        const tarjeta = page.getByRole('group', { name: 'Chrome · Escritorio' });
        await expect(tarjeta).toContainText('Cumple con reserva');
        await expect(tarjeta).toContainText('Versiones con la compra completada: 1 de 2');
      }
    }
    const tarjeta = page.getByRole('group', { name: 'Chrome · Escritorio' });
    await expect(tarjeta).toContainText('Versiones con la compra completada: 2 de 2');
    const a = (await calcular(page)).a;
    expect(a.combos.find((c: any) => c.combo === 'chrome-escritorio').estado).toBe('cumple');
    expect(a.veredicto).toBe('incompleto');
  });

  test('un paso en «Falla» sin defecto descrito se avisa, y no completar la compra es «No cumple»', async ({ page }) => {
    await abrir(page);
    await ir(page, 'Navegadores');
    await page.getByRole('button', { name: 'Nueva corrida en Safari · iPhone real' }).click();
    await marcarTodo(page, 'ok');
    await page.getByRole('radio', { name: 'Falla', exact: true }).first().click();
    await page.getByRole('group', { name: '¿Se completó la compra?' }).getByRole('radio', { name: 'No', exact: true }).click();
    await expect(page.getByText('No completó la compra')).toBeVisible();
    const c = await calcular(page);
    expect(c.a.veredicto).toBe('no-cumple');
    expect(c.b.veredicto).toBe('incompleto');
    await ir(page, 'Datos');
    await expect(page.getByText(/Avisos de la app/)).toBeVisible();
  });

  test('las seis combinaciones completas dan «Cumple con reserva» si falta la versión anterior de escritorio, y «Cumple» con ambas', async ({ page }) => {
    await abrir(page);
    const corridas = [
      corridaCompleta({ id: 'run-chrome126', combo: 'chrome-escritorio', navegador: 'Chrome', version: '126' }),
      corridaCompleta({ id: 'run-edge126a', combo: 'edge-escritorio', navegador: 'Edge', version: '126' }),
      corridaCompleta({ id: 'run-ffox127a', combo: 'firefox-escritorio', navegador: 'Firefox', version: '127' }),
      corridaCompleta({ id: 'run-safari17', combo: 'safari-macos', navegador: 'Safari', version: '17.5' }),
      corridaCompleta({ id: 'run-android1', combo: 'chrome-android', navegador: 'Chrome', version: '126' }),
      corridaCompleta({ id: 'run-iphone01', combo: 'safari-iphone', navegador: 'Safari', version: '17.5' }),
    ];
    await importarJson(page, archivo({ dispositivoId: 'disp-ejemplo1', evaluador: 'EV1', corridas }));
    expect((await calcular(page)).a.veredicto).toBe('cumple-reserva');
    const anteriores = [
      corridaCompleta({ id: 'run-chrome125', combo: 'chrome-escritorio', navegador: 'Chrome', version: '125' }),
      corridaCompleta({ id: 'run-edge125a', combo: 'edge-escritorio', navegador: 'Edge', version: '125' }),
      corridaCompleta({ id: 'run-ffox126a', combo: 'firefox-escritorio', navegador: 'Firefox', version: '126' }),
      corridaCompleta({ id: 'run-safari16', combo: 'safari-macos', navegador: 'Safari', version: '16.6' }),
    ];
    await importarJson(page, archivo({ dispositivoId: 'disp-ejemplo1', evaluador: 'EV1', corridas: anteriores, exportadoEn: '2026-11-23T13:00:00.000Z' }));
    expect((await calcular(page)).a.veredicto).toBe('cumple');
    // Una corrida posterior en la que la compra no se completó vuelve el veredicto en «No cumple».
    await importarJson(page, archivo({ dispositivoId: 'disp-ejemplo1', evaluador: 'EV1', corridas: [corridaCompleta({ id: 'run-android2', combo: 'chrome-android', navegador: 'Chrome', version: '126', completo: 'no', act: '2026-11-23T18:00:00.000Z' })], exportadoEn: '2026-11-23T18:00:00.000Z' }));
    expect((await calcular(page)).a.veredicto).toBe('no-cumple');
  });
});

test.describe('B · Teclado y lector de pantalla', () => {
  test('NVDA incluye el salto «Saltar al contenido» (K1) y VoiceOver usa los gestos; K1 no aplica a VoiceOver', async ({ page }) => {
    await abrir(page);
    await ir(page, 'Lector');
    await page.getByRole('button', { name: 'Nueva sesión: NVDA · claro' }).click();
    await page.getByText('Inicio', { exact: true }).first().click();
    await expect(page.getByText('Con Tab desde el principio, el primer destino').first()).toBeVisible();
    await expect(page.getByRole('group', { name: /^K1 en Inicio/ })).toBeVisible();
    await ir(page, 'Lector');
    await page.getByRole('button', { name: 'Nueva sesión: VoiceOver · oscuro' }).click();
    await page.getByText('Inicio', { exact: true }).first().click();
    await expect(page.getByText('Deslizar a la derecha recorre la pantalla').first()).toBeVisible();
    await expect(page.getByRole('group', { name: /^K1 en Inicio/ })).toHaveCount(0);
  });

  test('marcar lo pendiente como «Cumple» agiliza la pantalla; un crítico sin descripción se avisa y da «No cumple»', async ({ page }) => {
    await abrir(page);
    await ir(page, 'Lector');
    await page.getByRole('button', { name: 'Nueva sesión: NVDA · oscuro' }).click();
    for (const pantalla of ['Inicio', 'Catálogo']) {
      const d = page.locator('details', { has: page.locator('summary', { hasText: pantalla }) }).first();
      await d.locator('summary').click();
      await d.getByRole('button', { name: /Marcar lo pendiente como «Cumple»/ }).click();
    }
    await expect(page.locator('summary', { hasText: 'Inicio' }).first()).toContainText('Completa');
    await expect(page.getByText('2 de 13 pantallas completas')).toBeVisible();
    // Un crítico en el catálogo
    await page.getByRole('group', { name: 'L8 en Catálogo' }).getByRole('radio', { name: 'Crítico AA' }).click();
    await expect(page.getByLabel('Qué pasó (obligatorio al fallar)')).toBeVisible();
    await expect(page.locator('summary', { hasText: 'Catálogo' }).first()).toContainText('1 crítico');
    const b = (await calcular(page)).b;
    expect(b.criticos).toBe(1);
    expect(b.veredicto).toBe('no-cumple');
    await ir(page, 'Datos');
    await expect(page.getByText(/falla sin descripción/)).toBeVisible();
  });

  test('«Cumple» solo con las cuatro sesiones (lector × tema) completas y sin críticos', async ({ page }) => {
    await abrir(page);
    const s = (id: string, lector: 'nvda' | 'voiceover', tema: 'babelhub' | 'babelhub-dark') => sesionCompleta({ id, lector, tema });
    await importarJson(page, archivo({ dispositivoId: 'disp-ejemplo1', evaluador: 'EV1', sesiones: [s('ses-nvda-cla1', 'nvda', 'babelhub'), s('ses-nvda-osc1', 'nvda', 'babelhub-dark'), s('ses-vo-claro1', 'voiceover', 'babelhub')] }));
    expect((await calcular(page)).b.veredicto, 'faltan sesiones').toBe('incompleto');
    await importarJson(page, archivo({ dispositivoId: 'disp-ejemplo2', evaluador: 'EV2', sesiones: [s('ses-vo-osc01', 'voiceover', 'babelhub-dark')], exportadoEn: '2026-11-23T13:00:00.000Z' }));
    expect((await calcular(page)).b.veredicto).toBe('cumple');
    // Un «no crítico» no impide cumplir; un crítico, sí.
    await importarJson(page, archivo({ dispositivoId: 'disp-ejemplo2', evaluador: 'EV2', sesiones: [sesionCompleta({ id: 'ses-vo-osc01', lector: 'voiceover', tema: 'babelhub-dark', fallas: [{ pantalla: 'pago', item: 'L7', e: 'menor', n: 'La portada tiene un texto alternativo pobre.' }], act: '2026-11-23T14:00:00.000Z' })], exportadoEn: '2026-11-23T14:00:00.000Z' }));
    let b = (await calcular(page)).b;
    expect(b.noCriticos).toBe(1);
    expect(b.veredicto).toBe('cumple');
    await importarJson(page, archivo({ dispositivoId: 'disp-ejemplo2', evaluador: 'EV2', sesiones: [sesionCompleta({ id: 'ses-vo-osc01', lector: 'voiceover', tema: 'babelhub-dark', fallas: [{ pantalla: 'pago', item: 'L3', e: 'critico', n: 'El error del pago no se anuncia.' }], act: '2026-11-23T15:00:00.000Z' })], exportadoEn: '2026-11-23T15:00:00.000Z' }));
    b = (await calcular(page)).b;
    expect(b.criticos).toBe(1);
    expect(b.veredicto).toBe('no-cumple');
  });

  test('el criterio de «crítico AA» está a la vista antes de empezar', async ({ page }) => {
    await abrir(page);
    await ir(page, 'Lector');
    await expect(page.getByRole('heading', { name: 'Qué cuenta como «crítico AA»' })).toBeVisible();
    await expect(page.getByText('La persona no puede continuar o terminar la tarea de la pantalla')).toBeVisible();
    await expect(page.getByText('Un control (botón, enlace, campo) sin nombre')).toBeVisible();
    await expect(page.getByText('Un error o un cambio de estado importante que no se anuncia')).toBeVisible();
  });
});

test.describe('C · Moderador de usabilidad', () => {
  test('consentimiento, cronómetro con bloqueo de 2 minutos, errores, ayuda, resultado y SUS en una persona', async ({ page }) => {
    await page.clock.install({ time: new Date('2026-11-23T15:00:00Z') });
    await page.clock.pauseAt(new Date('2026-11-23T15:00:01Z'));
    await abrir(page);
    await ir(page, 'Usabilidad');
    await page.getByRole('button', { name: /Agregar persona a la ronda 1/ }).click();
    await expect(page.getByRole('heading', { name: /Persona P1 · ronda 1/ })).toBeVisible();

    // Consentimiento: el aviso pide marcar las tres casillas antes de la tarea.
    await expect(page.getByText('No empieces la tarea hasta marcar las tres primeras casillas.')).toBeVisible();
    await page.getByLabel('Leí el texto en voz alta').check();
    await page.getByLabel('Dijo que entiende que puede parar').check();
    await page.getByLabel('Acepta participar').check();
    await expect(page.getByText('Consentimiento registrado. Puedes seguir a la tarea.')).toBeVisible();
    await expect(page.getByText('Ley N.º 29733')).toBeVisible();

    // Tarea
    await page.getByRole('button', { name: 'Tarea', exact: true }).click();
    await expect(page.getByText('«El zorro y el cóndor del Ande»').first()).toBeVisible();
    await page.getByRole('button', { name: 'Iniciar' }).click();
    await page.clock.runFor(65_000);
    await expect(page.locator('#crono')).toHaveText('01:05');
    await page.getByRole('button', { name: '+ Clic equivocado' }).click();
    await page.getByRole('button', { name: '+ Mensaje no entendido' }).click();
    await expect(page.locator('#n-errores')).toHaveText('2');
    await page.getByRole('button', { name: 'Deshacer el último' }).click();
    await expect(page.locator('#n-errores')).toHaveText('1');
    await page.getByRole('button', { name: 'Se bloqueó' }).click();
    await page.clock.runFor(119_000);
    await expect(page.locator('#bloqueo')).toHaveText('01:59');
    await expect(page.locator('#bloqueo-msg')).toBeHidden();
    await page.clock.runFor(2_000);
    await expect(page.locator('#bloqueo-msg')).toBeVisible();
    await expect(page.locator('#bloqueo-msg')).toContainText('Ya pasaron 2 minutos');
    await page.getByRole('button', { name: /Registrar una ayuda \(0\)/ }).click();
    await expect(page.getByRole('button', { name: /Registrar una ayuda \(1\)/ })).toBeVisible();
    await page.getByRole('button', { name: 'Detener (terminó)' }).click();
    await expect(page.locator('main p', { hasText: 'Tiempo registrado:' })).toContainText('03:06');

    // Resultado: «sin ayuda» con una ayuda registrada se avisa y cuenta como con ayuda
    await page.getByRole('button', { name: 'Resultado', exact: true }).click();
    await page.getByRole('group', { name: 'Resultado de la tarea' }).getByRole('radio', { name: 'Sin ayuda' }).click();
    await expect(page.getByText(/se cuenta como con ayuda/)).toBeVisible();
    await page.getByRole('group', { name: 'Resultado de la tarea' }).getByRole('radio', { name: 'Con ayuda' }).click();
    await expect(page.getByText(/se cuenta como con ayuda/)).toHaveCount(0);
    await expect(page.getByLabel('Tiempo total, en segundos')).toHaveValue('186');
    await page.getByRole('radio', { name: '6', exact: true }).click();

    // SUS completo → persona completa
    await page.getByRole('button', { name: 'SUS', exact: true }).click();
    for (let i = 1; i <= 10; i++) await page.getByRole('group', { name: new RegExp(`^${i}\\. `) }).getByRole('radio', { name: i % 2 ? '4' : '2', exact: true }).click();
    await expect(page.getByText(/Puntaje SUS: 75 de 100/)).toBeVisible();
    await page.getByRole('button', { name: 'Cierre', exact: true }).click();
    await page.getByLabel('Problema principal').fill('No encontró dónde elegir la sucursal de recojo.');
    await page.getByLabel('Gravedad (0 cosmético a 4 catastrófico)').selectOption('3');
    await expect(page.getByText('Esta persona tiene todo lo necesario')).toBeVisible();
    await page.getByRole('link', { name: 'Terminar y volver' }).click();

    // El resumen de la ronda usa lo anotado.
    const ronda = page.getByRole('table', { name: /Resumen de la ronda 1/ });
    await expect(ronda).toContainText('Personas válidas con resultado');
    await expect(ronda.getByRole('row', { name: /Éxito sin ayuda/ })).toContainText('0 de 1');
    await expect(ronda.getByRole('row', { name: /Errores por tarea/ })).toContainText('1');
    await expect(ronda.getByRole('row', { name: /SUS promedio/ })).toContainText('75');
    await expect(ronda.getByRole('row', { name: /Problemas de gravedad 3 o 4/ })).toContainText('1');
    const p = (await calcular(page)).c.rondas[1];
    expect(p).toMatchObject({ validas: 1, sinAyuda: 0, erroresMedia: 1, sus: 75, tiempoMediana: 186, graves: 1 });
  });

  test('el cronómetro sigue contando si se recarga la página', async ({ page }) => {
    await page.clock.install({ time: new Date('2026-11-23T15:00:00Z') });
    await page.clock.pauseAt(new Date('2026-11-23T15:00:01Z'));
    await abrir(page);
    await ir(page, 'Usabilidad');
    await page.getByRole('button', { name: /Agregar persona a la ronda 1/ }).click();
    await page.getByRole('button', { name: 'Tarea', exact: true }).click();
    await page.getByRole('button', { name: 'Iniciar' }).click();
    await page.clock.runFor(30_000);
    await page.getByRole('button', { name: 'Detener (terminó)' }).click();
    await page.reload();
    await page.getByRole('button', { name: 'Tarea', exact: true }).click();
    await expect(page.locator('main p', { hasText: 'Tiempo registrado:' })).toContainText('00:30');
    await expect(page.getByRole('button', { name: 'Reanudar' })).toBeVisible();
  });

  test('los códigos siguen la numeración (P1…P10), se puede excluir a una persona y deja de contar', async ({ page }) => {
    await abrir(page);
    await ir(page, 'Usabilidad');
    await page.getByRole('button', { name: /Agregar persona a la ronda 1/ }).click();
    await page.getByRole('link', { name: 'Volver a usabilidad' }).click();
    await page.getByRole('button', { name: /Agregar persona a la ronda 1/ }).click();
    await expect(page.getByLabel('Código de la persona')).toHaveValue('P2');
    await page.getByLabel('Excluir a esta persona de los resultados').check();
    await page.getByLabel('Motivo (sin datos personales)').fill('Conocía el sistema.');
    await expect(page.getByText(/Esta persona está excluida de los resultados/)).toBeVisible();
    await page.getByRole('link', { name: 'Volver a usabilidad' }).click();
    await expect(page.getByRole('link', { name: 'P2' }).locator('xpath=..')).toContainText('Excluida');
    await page.getByRole('radio', { name: 'Ronda 2' }).click();
    await page.getByRole('button', { name: /Agregar persona a la ronda 2/ }).click();
    await expect(page.getByLabel('Código de la persona')).toHaveValue('P3');
  });

  test('avisa si una ronda no tiene a nadie con tecnología de apoyo o tiene menos de 5 personas', async ({ page }) => {
    await abrir(page);
    await importarJson(page, archivo({ dispositivoId: 'disp-ejemplo1', evaluador: 'EV1', personas: [{ id: 'per-ejemplo01', ronda: 1, codigo: 'P1', resultado: 'sin-ayuda' }] }));
    await ir(page, 'Datos');
    await expect(page.getByText(/la ronda 1 tiene 1 de 5 personas válidas/)).toBeVisible();
    await expect(page.getByText(/la ronda 1 no tiene a nadie con tecnología de apoyo/)).toBeVisible();
  });

  test('un código repetido entre dos dispositivos se avisa', async ({ page }) => {
    await abrir(page);
    await importarJson(page, archivo({ dispositivoId: 'disp-ejemplo1', evaluador: 'EV1', personas: [{ id: 'per-ejemplo01', ronda: 1, codigo: 'P1', resultado: 'sin-ayuda' }] }));
    await importarJson(page, archivo({ dispositivoId: 'disp-ejemplo2', evaluador: 'EV2', personas: [{ id: 'per-ejemplo02', ronda: 2, codigo: 'P1', resultado: 'sin-ayuda' }], exportadoEn: '2026-11-23T13:00:00.000Z' }));
    await ir(page, 'Datos');
    await expect(page.getByText(/el código P1 se repite en 2 registros/)).toBeVisible();
  });

  test('el resumen de la ronda 1 pide registrar los cambios hechos entre rondas y los guarda', async ({ page }) => {
    await abrir(page);
    await ir(page, 'Usabilidad');
    await page.getByLabel('Cambios hechos entre las rondas').fill('Se movió el selector de sucursal al principio del checkout.');
    await page.getByLabel('Resumen y decisiones de la ronda 1').fill('Dos personas no vieron el recojo.');
    await page.reload();
    await expect(page.getByLabel('Cambios hechos entre las rondas')).toHaveValue(/selector de sucursal/);
    await page.getByRole('radio', { name: 'Ronda 2' }).click();
    await expect(page.getByLabel('Cambios hechos entre las rondas')).toHaveCount(0);
    await expect(page.getByLabel('Resumen y decisiones de la ronda 2')).toHaveValue('');
  });
});
