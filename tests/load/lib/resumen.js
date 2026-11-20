// Resumen de cada corrida (JSON completo de k6 y una tabla en Markdown con los umbrales) escrito en la carpeta de resultados que monta ejecutar.sh.
const DIRECTORIO = __ENV.DIRECTORIO_DE_RESULTADOS || '/resultados';

function fila(nombre, metrica) {
  const v = metrica.values || {};
  const partes = [];
  if (v['p(95)'] !== undefined) partes.push(`p95 ${v['p(95)'].toFixed(1)} ms`);
  if (v.med !== undefined) partes.push(`mediana ${v.med.toFixed(1)} ms`);
  if (metrica.type === 'rate' && v.rate !== undefined) partes.push(`tasa ${(v.rate * 100).toFixed(2)} %`);
  if (metrica.type === 'counter' && v.count !== undefined) partes.push(`cuenta ${v.count}`);
  const umbrales = Object.entries(metrica.thresholds || {}).map(([u, r]) => `${r.ok ? 'OK' : 'FALLA'} \`${u}\``).join('<br>');
  return `| \`${nombre}\` | ${partes.join(', ')} | ${umbrales} |`;
}

export function resumen(nombre, descripcion) {
  return (datos) => {
    const m = datos.metrics;
    const filas = Object.keys(m)
      .filter((k) => Object.keys(m[k].thresholds || {}).length > 0 || ['http_req_duration', 'http_reqs', 'http_req_failed', 'iterations', 'vus_max'].includes(k))
      .map((k) => fila(k, m[k]));
    const todo = Object.values(m).every((x) => Object.values(x.thresholds || {}).every((r) => r.ok));
    const md = [
      `# ${nombre}`,
      '',
      descripcion,
      '',
      `Resultado: **${todo ? 'CUMPLE todos los umbrales' : 'NO cumple algún umbral (ver tabla)'}** — duración de la prueba: ${(datos.state.testRunDurationMs / 1000).toFixed(0)} s`,
      '',
      '| Métrica | Valores | Umbrales |',
      '| :--- | :--- | :--- |',
      ...filas,
      '',
    ].join('\n');
    return { [`${DIRECTORIO}/${nombre}.json`]: JSON.stringify(datos, null, 2), [`${DIRECTORIO}/${nombre}.md`]: md, stdout: md };
  };
}
