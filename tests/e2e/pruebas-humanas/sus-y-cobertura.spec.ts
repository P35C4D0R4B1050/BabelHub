import { test, expect } from '@playwright/test';
import { abrir, archivo, calculado, importarJson, part, puntaje, seisCombinaciones, SUS_75, type PartDeEjemplo } from './ayudas';

// El cálculo del SUS, la cobertura por navegador (AC-WEB-18) y los criterios de AC-WEB-15 y AC-WEB-17 que la app calcula como ayuda
// (el veredicto definitivo lo emite quien lee los JSON). Los datos de ejemplo viven solo aquí: la app nunca trae nada precargado.

test.describe('Puntaje SUS', () => {
  test('sigue la fórmula estándar: impares (respuesta − 1), pares (5 − respuesta), suma × 2,5', async ({ page }) => {
    await abrir(page);
    expect(await puntaje(page, SUS_75)).toBe(75); // ejemplo del documento 09 §15
    expect(await puntaje(page, [5, 1, 5, 1, 5, 1, 5, 1, 5, 1])).toBe(100);
    expect(await puntaje(page, [1, 5, 1, 5, 1, 5, 1, 5, 1, 5])).toBe(0);
    expect(await puntaje(page, Array(10).fill(3))).toBe(50);
    expect(await puntaje(page, [4, 3, 4, 3, 4, 3, 4, 3, 4, 3])).toBe(62.5);
    expect(await puntaje(page, [5, 5, 5, 5, 5, 5, 5, 5, 5, 5])).toBe(50);
    expect(await puntaje(page, [2, 4, 3, 5, 1, 2, 4, 3, 5, 1])).toBe(50); // impares 1+2+0+3+4 = 10; pares 1+0+3+2+4 = 10; 20 × 2,5
  });

  test('solo se calcula con las 10 respuestas válidas', async ({ page }) => {
    await abrir(page);
    expect(await puntaje(page, [4, 2, 4, 2, 4, 2, 4, 2, 4, null])).toBeNull();
    expect(await puntaje(page, [4, 2, 4, 2, 4, 2, 4, 2, 4])).toBeNull();
    expect(await puntaje(page, [4, 2, 4, 2, 4, 2, 4, 2, 4, 6])).toBeNull();
    expect(await puntaje(page, [4, 2, 4, 2, 4, 2, 4, 2, 4, 0])).toBeNull();
  });
});

const ESCRITORIO = ['Chrome en computadora', 'Edge en computadora', 'Firefox en computadora', 'Safari en Mac'];

test.describe('Cobertura de navegadores (AC-WEB-18)', () => {
  test('Cumple: las seis combinaciones con la compra completa y dos versiones en las de escritorio', async ({ page }) => {
    await abrir(page);
    await importarJson(page, archivo(seisCombinaciones()));
    const c = await calculado(page);
    expect(c.a.veredicto).toBe('cumple');
    expect(c.a.faltan).toEqual([]);
    expect(c.a.combos.map((x: any) => x.estado)).toEqual(Array(6).fill('cumple'));
    await expect(page.getByRole('table', { name: /Cobertura de las seis combinaciones/ })).toBeVisible();
  });

  test('Cumple con reserva: falta la segunda versión de una combinación de escritorio, y se dice cuál', async ({ page }) => {
    await abrir(page);
    await importarJson(page, archivo(seisCombinaciones({ sinSegundaVersion: 'firefox-escritorio-2' })));
    const c = await calculado(page);
    expect(c.a.veredicto).toBe('cumple-reserva');
    expect(c.a.faltaVersion).toEqual(['Firefox en computadora']);
    await expect(page.getByText(/Falta una segunda versión estable de: Firefox en computadora/)).toBeVisible();
  });

  test('Incompleto: el resumen indica qué combinaciones faltan', async ({ page }) => {
    await abrir(page);
    await importarJson(page, archivo(seisCombinaciones({ sin: ['safari-macos', 'safari-iphone'] })));
    const c = await calculado(page);
    expect(c.a.veredicto).toBe('incompleto');
    expect(c.a.faltan).toEqual(['Safari en Mac', 'Safari en iPhone']);
    await expect(page.getByText(/Faltan: Safari en Mac; Safari en iPhone/)).toBeVisible();
  });

  test('No cumple: la última persona de una combinación no completó la compra', async ({ page }) => {
    await abrir(page);
    const todas = seisCombinaciones();
    todas.push(part({ id: 'per-edgefalla001', nav: 'Edge', ver: '131.0.0.0', a: 'no', act: '2026-11-24T10:00:00.000Z' }));
    await importarJson(page, archivo(todas));
    const c = await calculado(page);
    expect(c.a.veredicto).toBe('no-cumple');
    expect(c.a.combos.find((x: any) => x.combo === 'edge-escritorio').estado).toBe('falla');
  });

  test('Brave, Opera y Chrome en iPhone no cuentan para las seis, pero se listan', async ({ page }) => {
    await abrir(page);
    await importarJson(page, archivo([part({ id: 'per-brave000001', nav: 'Brave', ver: '131', a: 'si' }), part({ id: 'per-opera000001', nav: 'Opera', ver: '115', a: 'si' }), part({ id: 'per-crios000001', nav: 'Chrome', ver: '131', sis: 'iOS', disp: 'celular', a: 'si' })]));
    const c = await calculado(page);
    expect(c.a.combos.every((x: any) => x.personas === 0)).toBe(true);
    expect(c.a.otros).toEqual(['Brave 131 en Windows', 'Opera 115 en Windows', 'Chrome 131 en iOS']);
    expect(c.a.veredicto).toBe('incompleto');
  });

  test('el mismo navegador en dos personas con la misma versión no cuenta como dos versiones', async ({ page }) => {
    await abrir(page);
    await importarJson(page, archivo(seisCombinaciones({ sinSegundaVersion: 'chrome-escritorio-2' }).concat(part({ id: 'per-chrome0003', nav: 'Chrome', ver: '130.0.6723.1', a: 'si' }))));
    expect((await calculado(page)).a.combos[0].versiones).toEqual(['130']);
    expect((await calculado(page)).a.veredicto).toBe('cumple-reserva');
    expect(ESCRITORIO).toHaveLength(4);
  });
});

const conB = (b1: number, lectores: Array<'nvda' | 'voiceover'>, criticoEn?: 'b1' | 'b2'): PartDeEjemplo[] => [
  ...Array.from({ length: b1 }, (_, i) => ({ id: `per-teclado${String(i).padStart(4, '0')}`, b1: (criticoEn === 'b1' && i === 0 ? 'si' : 'no') as 'si' | 'no' })),
  ...lectores.map((l, i) => ({ id: `per-lector${String(i).padStart(5, '0')}`, b2: { lector: l, bloqueo: (criticoEn === 'b2' && i === 0 ? 'si' : 'no') as 'si' | 'no' } })),
];

test.describe('Teclado y lector de pantalla (AC-WEB-15): 0 críticos', () => {
  const casos: Array<[string, PartDeEjemplo[], string, number]> = [
    ['Cumple: 3 personas con teclado y NVDA y VoiceOver, sin bloqueos', conB(3, ['nvda', 'voiceover']), 'cumple', 0],
    ['Cumple con reserva: falta uno de los dos lectores', conB(3, ['nvda']), 'cumple-reserva', 0],
    ['Incompleto: sin lector de pantalla', conB(4, []), 'incompleto', 0],
    ['Incompleto: pocas personas con el teclado', conB(2, ['nvda', 'voiceover']), 'incompleto', 0],
    ['No cumple: una persona se bloqueó con el teclado', conB(3, ['nvda', 'voiceover'], 'b1'), 'no-cumple', 1],
    ['No cumple: una persona se bloqueó con el lector', conB(3, ['nvda', 'voiceover'], 'b2'), 'no-cumple', 1],
  ];
  for (const [nombre, personas, veredicto, criticos] of casos) {
    test(nombre, async ({ page }) => {
      await abrir(page);
      await importarJson(page, archivo(personas.map(part)));
      const c = await calculado(page);
      expect(c.b.veredicto).toBe(veredicto);
      expect(c.b.criticos).toHaveLength(criticos);
    });
  }

  test('las respuestas de 1 o 2 puntos son no críticas y no impiden cumplir', async ({ page }) => {
    await abrir(page);
    const base = conB(3, ['nvda', 'voiceover']).map(part);
    (base[0].b1 as any).items.T1.v = 1;
    (base[0].b1 as any).items.T2.v = 2;
    await importarJson(page, archivo(base));
    const c = await calculado(page);
    expect(c.b).toMatchObject({ veredicto: 'cumple', noCriticos: 2 });
    expect(c.b.criticos).toEqual([]);
  });
});

const diez = (o: { sinAyuda?: number; sus?: number[]; errores?: string; rondaUna?: number } = {}): PartDeEjemplo[] =>
  Array.from({ length: 10 }, (_, i) => ({
    id: `per-usab${String(i).padStart(7, '0')}`, codigo: `P${i + 1}`,
    c: { ronda: (i < (o.rondaUna ?? 5) ? '1' : '2') as '1' | '2', ayuda: (i < (o.sinAyuda ?? 10) ? 'no' : 'si') as 'si' | 'no', sus: o.sus ?? SUS_75, errores: o.errores ?? '0', apoyo: (i === 0 || i === 5 ? 'si' : 'no') as 'si' | 'no' },
  }));

test.describe('Usabilidad sin moderador (AC-WEB-17)', () => {
  test('Cumple: 10 personas (5 + 5), 9 sin ayuda, SUS 75 y menos de 1 error por tarea', async ({ page }) => {
    await abrir(page);
    await importarJson(page, archivo(diez({ sinAyuda: 9 }).map(part)));
    const c = await calculado(page);
    expect(c.c).toMatchObject({ validas: 10, sinAyuda: 9, sus: 75, erroresMedia: 0, veredicto: 'cumple' });
    expect(c.c.rondas).toMatchObject({ 1: 5, 2: 5, sinRonda: 0 });
    await expect(page.getByText(/sin ayuda 9 de 10/)).toBeVisible();
  });

  test('No cumple: 8 de 10 sin ayuda aunque el SUS y los errores cumplan', async ({ page }) => {
    await abrir(page);
    await importarJson(page, archivo(diez({ sinAyuda: 8 }).map(part)));
    expect((await calculado(page)).c).toMatchObject({ sinAyuda: 8, cumpleExito: false, veredicto: 'no-cumple' });
  });

  test('No cumple: SUS promedio de 62,5 aunque 10 de 10 compren sin ayuda', async ({ page }) => {
    await abrir(page);
    await importarJson(page, archivo(diez({ sus: [4, 3, 4, 3, 4, 3, 4, 3, 4, 3] }).map(part)));
    expect((await calculado(page)).c).toMatchObject({ sus: 62.5, cumpleSUS: false, veredicto: 'no-cumple' });
  });

  test('No cumple: 1 error por tarea en promedio (la meta es menos de 1); 0 con «3 o más» cuenta como 3', async ({ page }) => {
    await abrir(page);
    await importarJson(page, archivo(diez({ errores: '1' }).map(part)));
    expect((await calculado(page)).c).toMatchObject({ erroresMedia: 1, cumpleErrores: false, veredicto: 'no-cumple' });
    await importarJson(page, archivo(diez({ errores: '3+' }).map((p) => ({ ...part(p), id: p.id.replace('usab', 'usbb') }))));
    expect((await calculado(page)).c.erroresMedia).toBeGreaterThan(1);
  });

  test('Incompleto: con menos de 10 personas; se pide asignar la ronda si falta', async ({ page }) => {
    await abrir(page);
    await importarJson(page, archivo(diez().slice(0, 7).map(part)));
    expect((await calculado(page)).c).toMatchObject({ validas: 7, veredicto: 'incompleto' });
    const sinRonda = diez().map(part);
    sinRonda.forEach((p) => { (p.c as any).ronda = ''; });
    await importarJson(page, archivo(sinRonda.map((p) => ({ ...p, id: p.id.replace('usab', 'sinr') }))));
    const c = (await calculado(page)).c;
    expect(c.rondas.sinRonda).toBe(10);
    expect(c.veredicto).toBe('incompleto');
  });

  test('con 8 personas y 6 sin ayuda ya no alcanza 9 de 10: No cumple sin esperar', async ({ page }) => {
    await abrir(page);
    await importarJson(page, archivo(diez({ sinAyuda: 6 }).slice(0, 8).map(part)));
    expect((await calculado(page)).c.veredicto).toBe('no-cumple');
  });

  test('no cuentan: quien ya conocía la tienda, quien hizo antes la compra guiada y quien no dio su consentimiento', async ({ page }) => {
    await abrir(page);
    const gente = diez().map(part);
    (gente[0].c as any).perfil.conocia = 'si';
    (gente[1].c as any).despuesDeA = true;
    (gente[2].c as any).consentimiento.acepta = false;
    await importarJson(page, archivo(gente));
    const c = await calculado(page);
    expect(c.c.validas).toBe(7);
    expect(c.c.descartadas).toEqual(['P1', 'P2', 'P3']);
    expect(c.avisos.join('\n')).toMatch(/P1: su tarea de usabilidad no cuenta.*ya conocía la tienda/);
    expect(c.avisos.join('\n')).toMatch(/P2: .*hizo antes la compra guiada/);
    expect(c.avisos.join('\n')).toMatch(/P3: .*sin consentimiento completo/);
    expect(c.c.veredicto).toBe('incompleto');
  });

  test('una persona sin las 10 respuestas del SUS no entra en el promedio', async ({ page }) => {
    await abrir(page);
    const gente = diez().map(part);
    (gente[0].c as any).sus = [4, 2, 4, 2, 4, 2, 4, 2, 4, null];
    await importarJson(page, archivo(gente));
    const c = (await calculado(page)).c;
    expect(c.susN).toBe(9);
    expect(c.veredicto).toBe('incompleto'); // faltan cuestionarios completos
  });

  test('en el resumen, quien reúne puede asignar la ronda de cada persona', async ({ page }) => {
    await abrir(page);
    const gente = diez().slice(0, 2).map(part);
    gente.forEach((p) => { (p.c as any).ronda = ''; });
    await importarJson(page, archivo(gente));
    await page.getByRole('combobox', { name: 'Ronda de P1' }).selectOption('2');
    expect((await calculado(page)).c.rondas[2]).toBe(1);
  });
});
