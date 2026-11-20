// Sustituto de Identidad SOLO para las pruebas de carga: genera una clave ES256 efímera, publica su JWKS y la lista de revocaciones (lo que el Inventario
// verifica con CacheDeClavesDelInventario) y emite credenciales de usuario de corta vida. Nada de esto es un secreto real: la clave vive en la carpeta
// de la prueba (fuera del repositorio) y la credencial solo la acepta el entorno de carga que apunta a este servidor.
//   node servidor-de-claves.mjs servir <carpeta> [puerto]      publica /jwks.json y /revocaciones (queda en ejecución)
//   node servidor-de-claves.mjs emitir <carpeta> [usuarios]    escribe <carpeta>/credenciales.json (administrador y N clientes, 14 minutos)
import { createServer } from 'node:http';
import { generateKeyPairSync, createPrivateKey, sign, randomUUID } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';

const [orden, carpeta, extra] = process.argv.slice(2);
if (!orden || !carpeta) { console.error('uso: servidor-de-claves.mjs servir|emitir <carpeta> [puerto|usuarios]'); process.exit(2); }
mkdirSync(carpeta, { recursive: true });
const archivoClave = join(carpeta, 'clave-es256.json');
const kid = 'carga-1';
if (!existsSync(archivoClave)) {
  const { privateKey } = generateKeyPairSync('ec', { namedCurve: 'P-256' });
  writeFileSync(archivoClave, JSON.stringify(privateKey.export({ format: 'jwk' })), { mode: 0o600 });
}
const jwkPrivada = JSON.parse(readFileSync(archivoClave, 'utf8'));
const clave = createPrivateKey({ key: jwkPrivada, format: 'jwk' });
const b64 = (b) => Buffer.from(b).toString('base64url');

function firmar(reclamos) {
  const cabecera = b64(JSON.stringify({ alg: 'ES256', typ: 'JWT', kid }));
  const carga = b64(JSON.stringify(reclamos));
  const firma = sign('sha256', Buffer.from(`${cabecera}.${carga}`), { key: clave, dsaEncoding: 'ieee-p1363' });
  return `${cabecera}.${carga}.${b64(firma)}`;
}

function credencial(rol, sucursal) {
  const ahora = Math.floor(Date.now() / 1000);
  const r = { iss: 'babelhub-identidad', sub: randomUUID(), aud: ['babelhub-ecommerce', 'babelhub-inventario'], rol, iat: ahora, nbf: ahora, exp: ahora + 14 * 60 };
  if (sucursal) r.sucursal = sucursal;
  return { sub: r.sub, token: firmar(r) };
}

if (orden === 'servir') {
  const publica = { kty: 'EC', crv: 'P-256', x: jwkPrivada.x, y: jwkPrivada.y, kid, alg: 'ES256', use: 'sig' };
  const puerto = Number(extra ?? 8083);
  createServer((req, res) => {
    res.setHeader('content-type', 'application/json');
    if (req.url?.startsWith('/jwks.json')) return res.end(JSON.stringify({ keys: [publica] }));
    if (req.url?.startsWith('/revocaciones')) return res.end(JSON.stringify({ revocadas: [], generadoEn: Math.floor(Date.now() / 1000) }));
    res.statusCode = 404; res.end('{}');
  }).listen(puerto, '0.0.0.0', () => console.log(`claves de carga en :${puerto}`));
} else if (orden === 'emitir') {
  const n = Number(extra ?? 2200);
  const salida = { emitidas: new Date().toISOString(), administrador: credencial('administrador-central'), usuarios: Array.from({ length: n }, () => credencial('cliente')) };
  writeFileSync(join(carpeta, 'credenciales.json'), JSON.stringify(salida), { mode: 0o600 });
  console.log(`credenciales emitidas: 1 administrador y ${n} clientes (14 minutos)`);
}
