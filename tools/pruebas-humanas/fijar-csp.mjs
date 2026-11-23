// Recalcula el hash SHA-256 del único bloque <script> de index.html y lo escribe en la política de contenido (<meta http-equiv="Content-Security-Policy">).
// Así el navegador solo ejecuta ese script exacto: un script o manejador inyectado (por ejemplo, desde un JSON importado) no corre.
// Ejecútalo después de CADA cambio al JavaScript de index.html:  node tools/pruebas-humanas/fijar-csp.mjs
// La prueba `privacidad-y-sin-red.spec.ts` falla si el hash no coincide.
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ruta = join(dirname(fileURLToPath(import.meta.url)), 'index.html');
const html = readFileSync(ruta, 'utf8');
const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)];
if (scripts.length !== 1) {
  console.error(`index.html debe tener exactamente un <script> sin atributos (tiene ${scripts.length}).`);
  process.exit(1);
}
const hash = createHash('sha256').update(scripts[0][1], 'utf8').digest('base64');
const nuevo = html.replace(/script-src (?:'unsafe-inline'|'sha256-[A-Za-z0-9+/=]+')/, `script-src 'sha256-${hash}'`);
if (nuevo === html) {
  console.log(`El hash ya estaba al día: sha256-${hash}`);
} else {
  writeFileSync(ruta, nuevo);
  console.log(`Política actualizada: script-src 'sha256-${hash}'`);
}
