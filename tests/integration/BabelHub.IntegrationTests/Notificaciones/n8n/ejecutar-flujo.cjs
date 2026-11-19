// Ejecuta los nodos de código de infra/n8n/flujo-de-avisos.json con un doble mínimo del entorno de n8n (sin red, sin dependencias):
// lee un escenario JSON de la entrada estándar y escribe el resultado en la salida estándar. Lo usa FlujoDeN8nEjecutadoTests.
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const flujo = JSON.parse(fs.readFileSync(path.join(process.argv[2], 'infra', 'n8n', 'flujo-de-avisos.json'), 'utf8'));
const codigoDe = (nombre) => flujo.nodes.find((n) => n.name === nombre).parameters.jsCode;
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;

async function ejecutar(nombre, contexto) {
  const f = new AsyncFunction('require', '$input', '$env', '$getWorkflowStaticData', '$', '$json', codigoDe(nombre));
  const datos = contexto.datos;
  const llamada = f.bind({ helpers: { getBinaryDataBuffer: async () => Buffer.from(contexto.cuerpoCrudo ?? '', 'utf8') } });
  const porNombre = contexto.anteriores ?? {};
  return llamada(
    require,
    { first: () => ({ json: { headers: contexto.cabeceras ?? {} } }) },
    contexto.env ?? {},
    () => datos,
    (n) => ({ first: () => ({ json: porNombre[n] }) }),
    contexto.json ?? {},
  );
}

(async () => {
  const entrada = JSON.parse(fs.readFileSync(0, 'utf8'));
  const salida = [];
  const datos = entrada.datosEstaticos ?? {};
  for (const paso of entrada.pasos) {
    const real = Date.now;
    if (paso.ahoraMs) { Date.now = () => paso.ahoraMs; }
    try {
      salida.push(await ejecutar(paso.nodo, { ...paso, datos }));
    } finally {
      Date.now = real;
    }
  }
  process.stdout.write(JSON.stringify({ salida, datos }));
})().catch((e) => { process.stderr.write(String(e && e.stack || e)); process.exit(1); });
