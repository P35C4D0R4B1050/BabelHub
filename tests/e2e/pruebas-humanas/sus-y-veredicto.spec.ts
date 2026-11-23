import { test, expect } from '@playwright/test';
import { abrir, archivo, diezPersonas, importarJson, ir, puntaje } from './ayudas';

// El cálculo del SUS (fórmula estándar) y el resumen contra el criterio de AC-WEB-17. Los datos de ejemplo son solo de estas pruebas.

test.describe('Puntaje SUS', () => {
  test('sigue la fórmula estándar: impares (respuesta − 1), pares (5 − respuesta), suma × 2,5', async ({ page }) => {
    await abrir(page);
    expect(await puntaje(page, [5, 1, 5, 1, 5, 1, 5, 1, 5, 1]), 'lo mejor posible').toBe(100);
    expect(await puntaje(page, [1, 5, 1, 5, 1, 5, 1, 5, 1, 5]), 'lo peor posible').toBe(0);
    expect(await puntaje(page, [3, 3, 3, 3, 3, 3, 3, 3, 3, 3]), 'todo neutro').toBe(50);
    expect(await puntaje(page, [4, 2, 4, 2, 4, 2, 4, 2, 4, 2]), 'ejemplo: (3×5 + 3×5) × 2,5').toBe(75);
    expect(await puntaje(page, [5, 3, 4, 2, 5, 1, 4, 2, 5, 3]), 'ejemplo mixto: (4+2+3+3+4+4+3+3+4+2)=32 × 2,5').toBe(80);
    expect(await puntaje(page, [5, 3, 4, 2, 5, 1, 4, 2, 5, null]), 'incompleto no da puntaje').toBeNull();
    expect(await puntaje(page, [5, 3, 4]), 'con menos de 10 no da puntaje').toBeNull();
    expect(await puntaje(page, [6, 1, 5, 1, 5, 1, 5, 1, 5, 1]), 'fuera de rango no da puntaje').toBeNull();
  });

  test('se calcula en pantalla al responder las 10 frases (AC-WEB-17)', async ({ page }) => {
    await abrir(page);
    await ir(page, 'Usabilidad');
    await page.getByRole('button', { name: /Agregar persona a la ronda 1/ }).click();
    await page.getByRole('button', { name: 'SUS', exact: true }).click();
    await expect(page.getByRole('heading', { name: '4. Cuestionario SUS' })).toBeVisible();
    await expect(page.getByText('0 de 10 respondidas')).toBeVisible();
    const respuestas = [5, 3, 4, 2, 5, 1, 4, 2, 5, 3];
    for (const [i, v] of respuestas.entries()) {
      await page.getByRole('group', { name: new RegExp(`^${i + 1}\\. `) }).getByRole('radio', { name: String(v), exact: true }).click();
    }
    await expect(page.getByText(/Puntaje SUS: 80 de 100/)).toBeVisible();
    // Un cambio de respuesta recalcula: la 10 pasa de 3 a 1 → 5−1=4 en lugar de 2 → 34 × 2,5 = 85.
    await page.getByRole('group', { name: /^10\. / }).getByRole('radio', { name: '1', exact: true }).click();
    await expect(page.getByText(/Puntaje SUS: 85 de 100/)).toBeVisible();
  });
});

test.describe('Resumen por ronda y acumulado contra el criterio', () => {
  test('cumple: 9 de 10 sin ayuda, SUS 75 y 0 errores por tarea', async ({ page }) => {
    await abrir(page);
    await importarJson(page, archivo({ dispositivoId: 'disp-ejemplo1', evaluador: 'EV1', personas: diezPersonas({ sinAyuda: 9 }) }));
    await ir(page, 'Usabilidad');
    const tabla = page.getByRole('table', { name: /Acumulado de las dos rondas/ });
    await expect(tabla).toContainText('9 de 10');
    await expect(tabla).toContainText('75');
    await expect(tabla).toContainText('0');
    await expect(tabla).toContainText('Cumple');
    await expect(tabla).not.toContainText('No cumple');
    const ex = await page.evaluate(() => (window as any).__babelhubPruebasHumanas.calcular().c);
    expect(ex.veredicto).toBe('cumple');
    expect(ex.acumulado.sinAyuda).toBe(9);
    expect(ex.acumulado.sus).toBe(75);
  });

  test('no cumple: 8 de 10 sin ayuda aunque el SUS y los errores cumplan', async ({ page }) => {
    await abrir(page);
    await importarJson(page, archivo({ dispositivoId: 'disp-ejemplo1', evaluador: 'EV1', personas: diezPersonas({ sinAyuda: 8 }) }));
    const c = await page.evaluate(() => (window as any).__babelhubPruebasHumanas.calcular().c);
    expect(c.veredicto).toBe('no-cumple');
    expect(c.acumulado.cumpleExito).toBe(false);
    expect(c.acumulado.cumpleSUS).toBe(true);
  });

  test('no cumple: SUS promedio de 62,5 aunque 10 de 10 completen sin ayuda', async ({ page }) => {
    await abrir(page);
    // [4,3,3,2,4,3,3,2,4,3]: impares 3+2+3+2+3=13, pares 2+3+2+3+2=12 → 25 × 2,5 = 62,5
    await importarJson(page, archivo({ dispositivoId: 'disp-ejemplo1', evaluador: 'EV1', personas: diezPersonas({ sinAyuda: 10, sus: [4, 3, 3, 2, 4, 3, 3, 2, 4, 3] }) }));
    const c = await page.evaluate(() => (window as any).__babelhubPruebasHumanas.calcular().c);
    expect(c.acumulado.sus).toBe(62.5);
    expect(c.acumulado.cumpleSUS).toBe(false);
    expect(c.veredicto).toBe('no-cumple');
  });

  test('no cumple: 1 error por tarea en promedio (la meta es menos de 1)', async ({ page }) => {
    await abrir(page);
    await importarJson(page, archivo({ dispositivoId: 'disp-ejemplo1', evaluador: 'EV1', personas: diezPersonas({ sinAyuda: 10, erroresPorPersona: 1 }) }));
    const c = await page.evaluate(() => (window as any).__babelhubPruebasHumanas.calcular().c);
    expect(c.acumulado.erroresMedia).toBe(1);
    expect(c.acumulado.cumpleErrores).toBe(false);
    expect(c.veredicto).toBe('no-cumple');
  });

  test('incompleto con menos de 10 personas, y una persona excluida no cuenta', async ({ page }) => {
    await abrir(page);
    const personas = diezPersonas({ sinAyuda: 10 }).slice(0, 8);
    personas.push({ id: 'per-ejemplo99', ronda: 2, codigo: 'P99', resultado: 'no-completa', excluida: true });
    await importarJson(page, archivo({ dispositivoId: 'disp-ejemplo1', evaluador: 'EV1', personas }));
    const c = await page.evaluate(() => (window as any).__babelhubPruebasHumanas.calcular().c);
    expect(c.acumulado.personas, 'la excluida no cuenta').toBe(8);
    expect(c.veredicto).toBe('incompleto');
  });

  test('con 8 personas y dos con ayuda ya no alcanza 9 de 10', async ({ page }) => {
    await abrir(page);
    const personas = diezPersonas({ sinAyuda: 6 }).slice(0, 8);
    await importarJson(page, archivo({ dispositivoId: 'disp-ejemplo1', evaluador: 'EV1', personas }));
    const c = await page.evaluate(() => (window as any).__babelhubPruebasHumanas.calcular().c);
    expect(c.acumulado.imposibleExito).toBe(true);
    expect(c.veredicto).toBe('no-cumple');
  });

  test('una ayuda registrada con «sin ayuda» marcado cuenta como con ayuda', async ({ page }) => {
    await abrir(page);
    const personas = diezPersonas({ sinAyuda: 10 });
    const archivoConAyuda = archivo({ dispositivoId: 'disp-ejemplo1', evaluador: 'EV1', personas: personas.map((p, i) => (i < 2 ? { ...p, ayudas: 1 } : p)) });
    await importarJson(page, archivoConAyuda);
    const c = await page.evaluate(() => (window as any).__babelhubPruebasHumanas.calcular().c);
    expect(c.acumulado.sinAyuda).toBe(8);
    expect(c.veredicto).toBe('no-cumple');
  });
});
