// Servidor estático mínimo para abrir la app de pruebas humanas desde otros dispositivos de la red local.
// Sin dependencias: solo Node. Sirve únicamente `index.html` (en `/` y en `/index.html`); cualquier otra ruta responde 404.
// Uso:  node tools/pruebas-humanas/servir.mjs [puerto]        (por omisión, 8090; con 0 elige uno libre)
// Escucha en todas las interfaces para que un iPhone o un Android de la misma red puedan abrirla: ejecútalo solo en una red de confianza
// y ciérralo (Ctrl+C) al terminar. La página no envía datos a ningún sitio: los resultados se quedan en cada dispositivo.
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { networkInterfaces } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const carpeta = dirname(fileURLToPath(import.meta.url));
const puertoPedido = process.argv[2] === undefined ? 8090 : Number(process.argv[2]);
if (!Number.isInteger(puertoPedido) || puertoPedido < 0 || puertoPedido > 65535) {
  console.error('El puerto debe ser un número entre 0 y 65535.');
  process.exit(1);
}

const servidor = createServer(async (peticion, respuesta) => {
  // Sin `URL`: una petición rara (por ejemplo `//`) no debe poder tumbar el servidor.
  const ruta = (peticion.url ?? '/').split('?')[0].split('#')[0];
  if ((peticion.method !== 'GET' && peticion.method !== 'HEAD') || (ruta !== '/' && ruta !== '/index.html')) {
    respuesta.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
    respuesta.end('No encontrado');
    return;
  }
  try {
    const cuerpo = await readFile(join(carpeta, 'index.html'));
    respuesta.writeHead(200, {
      'Content-Type': 'text/html; charset=utf-8',
      'Cache-Control': 'no-store',
      'X-Content-Type-Options': 'nosniff',
      'Referrer-Policy': 'no-referrer',
      'X-Frame-Options': 'DENY',
      'Content-Security-Policy': "frame-ancestors 'none'",
    });
    respuesta.end(peticion.method === 'HEAD' ? undefined : cuerpo);
  } catch {
    respuesta.writeHead(500, { 'Content-Type': 'text/plain; charset=utf-8' });
    respuesta.end('No se pudo leer index.html');
  }
});

servidor.on('clientError', (_, socket) => socket.end('HTTP/1.1 400 Bad Request\r\n\r\n'));

servidor.listen(puertoPedido, '0.0.0.0', () => {
  const { port } = servidor.address();
  console.log('App de pruebas humanas de BabelHub (Ctrl+C para cerrar).');
  console.log(`  En este equipo:  http://localhost:${port}/`);
  for (const [nombre, direcciones] of Object.entries(networkInterfaces())) {
    for (const d of direcciones ?? []) {
      if (d.family === 'IPv4' && !d.internal) console.log(`  En la red (${nombre}): http://${d.address}:${port}/`);
    }
  }
  console.log('Abre la dirección de «En la red» en el celular (misma red wifi). Si no carga, revisa el cortafuegos del equipo.');
});
