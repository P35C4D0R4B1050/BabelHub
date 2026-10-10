/* Datos ficticios del prototipo v2. Nada de esto es real. */
window.BH = window.BH || {};

BH.hoy = '2026-10-10';            // fecha fija para que las licencias sean reproducibles
BH.limiteDescargas = 5;

BH.sucursales = [
  { id: 'lim', nombre: 'Lima Centro', region: 'Lima' },
  { id: 'aqp', nombre: 'Arequipa', region: 'Arequipa' },
  { id: 'tru', nombre: 'Trujillo', region: 'La Libertad' },
  { id: 'cus', nombre: 'Cusco', region: 'Cusco' },
  { id: 'piu', nombre: 'Piura', region: 'Piura' },
  { id: 'aya', nombre: 'Ayacucho', region: 'Ayacucho' }
];

BH.categorias = ['Ficción', 'No ficción', 'Poesía', 'Infantil', 'Escolar', 'Técnico'];

/* Paletas de portada derivadas de la marca: [fondo, texto, acento] */
BH.paletas = [
  ['#172554', '#F5EBD8', '#FF665A'],
  ['#FF665A', '#172554', '#F5EBD8'],
  ['#7857D9', '#F5EBD8', '#A9E85E'],
  ['#F5EBD8', '#172554', '#7857D9'],
  ['#36C8D4', '#172554', '#FF665A'],
  ['#A9E85E', '#172554', '#7857D9']
];

/* stock: S([físico, reservado] por sucursal: lim, aqp, tru, cus, piu, aya) — vendible = físico − reservado */
const S = (...p) => Object.fromEntries(['lim', 'aqp', 'tru', 'cus', 'piu', 'aya'].map((id, i) => [id, p[i] || [0, 0]]));
const PD = { tipo: 'Dominio público', vence: null, territorios: 'Mundial' };
const LC = (vence, terr = 'Perú y Latinoamérica') => ({ tipo: 'Licencia comercial', vence, territorios: terr });

/* Una obra puede tener edición impresa, digital o ambas; cada edición tiene su ISBN. */
BH.obras = [
  { id: 1, titulo: 'Conversación en La Catedral', autor: 'Mario Vargas Llosa', categoria: 'Ficción', editorial: 'Alfaguara', motivo: 'laberinto', paleta: 0,
    desc: 'Novela sobre el Perú de los años cincuenta: política, poder y desencanto contados en una larga conversación.',
    impreso: { isbn: '978-0-00-000001-1', precio: 69.9, stock: S([24, 3], [10, 1], [8, 0], [6, 2], [5, 0], [9, 1]) },
    digital: { isbn: '978-0-00-100001-8', precio: 39.9, formato: 'EPUB', paginas: 608, tamano: '2,4 MB', titular: 'Alfaguara (ejemplo)', licencia: LC('2027-06-30') } },
  { id: 2, titulo: 'Los ríos profundos', autor: 'José María Arguedas', categoria: 'Ficción', editorial: 'Horizonte', motivo: 'ondas', paleta: 2,
    desc: 'Ernesto, un adolescente entre dos mundos, recorre Abancay y el Cusco en una novela esencial de la literatura andina.',
    impreso: { isbn: '978-0-00-000002-8', precio: 42.0, stock: S([12, 2], [4, 0], [3, 0], [14, 1], [0, 0], [7, 2]) },
    digital: { isbn: '978-0-00-100002-5', precio: 24.9, formato: 'EPUB', paginas: 296, tamano: '1,1 MB', titular: 'Horizonte (ejemplo)', licencia: LC('2028-01-31') } },
  { id: 3, titulo: 'Cien años de soledad', autor: 'Gabriel García Márquez', categoria: 'Ficción', editorial: 'Alfaguara', motivo: 'orbita', paleta: 1,
    desc: 'La saga de la familia Buendía en Macondo, un clásico del realismo mágico.',
    impreso: { isbn: '978-0-00-000003-5', precio: 59.9, stock: S([30, 4], [12, 2], [9, 1], [11, 0], [7, 0], [5, 5]) },
    digital: null },
  { id: 4, titulo: 'Tradiciones peruanas', autor: 'Ricardo Palma', categoria: 'No ficción', editorial: 'Peisa', motivo: 'arcos', paleta: 3,
    desc: 'Relatos breves que mezclan historia, humor y crónica de la Lima virreinal y republicana.',
    impreso: { isbn: '978-0-00-000004-2', precio: 48.5, stock: S([3, 1], [0, 0], [2, 0], [0, 0], [1, 0], [0, 0]) },
    digital: { isbn: '978-0-00-100004-9', precio: 19.9, formato: 'EPUB', paginas: 540, tamano: '1,8 MB', titular: 'Dominio público', licencia: PD } },
  { id: 5, titulo: 'El mundo es ancho y ajeno', autor: 'Ciro Alegría', categoria: 'Ficción', editorial: 'Peisa', motivo: 'laberinto', paleta: 5,
    desc: 'La lucha de una comunidad indígena por su tierra en los Andes del norte.',
    impreso: { isbn: '978-0-00-000005-9', precio: 54.0, stock: S([0, 0], [0, 0], [0, 0], [4, 1], [0, 0], [0, 0]) },
    digital: { isbn: '978-0-00-100005-6', precio: 34.9, formato: 'EPUB', paginas: 480, tamano: '1,6 MB', titular: 'Peisa (ejemplo)', licencia: LC('2027-12-31') } },
  { id: 6, titulo: 'Aves sin nido', autor: 'Clorinda Matto de Turner', categoria: 'Escolar', editorial: 'Escolar Andina', motivo: 'lineas', paleta: 4,
    desc: 'Novela fundacional del indigenismo peruano, lectura habitual en el colegio.',
    impreso: { isbn: '978-0-00-000006-6', precio: 29.9, stock: S([20, 2], [8, 0], [6, 0], [9, 1], [5, 0], [15, 3]) },
    digital: { isbn: '978-0-00-100006-3', precio: 9.9, formato: 'PDF', paginas: 240, tamano: '3,2 MB', titular: 'Dominio público', licencia: PD } },
  { id: 7, titulo: 'Clean Architecture', autor: 'Robert C. Martin', categoria: 'Técnico', editorial: 'Prentice Hall', motivo: 'rejilla', paleta: 0,
    desc: 'Guía práctica para ordenar el software en capas y dependencias que apuntan hacia el dominio.',
    impreso: { isbn: '978-0-00-000007-3', precio: 139.0, stock: S([6, 1], [2, 0], [0, 0], [0, 0], [0, 0], [2, 0]) },
    digital: { isbn: '978-0-00-100007-0', precio: 99.0, formato: 'PDF', paginas: 432, tamano: '8,7 MB', titular: 'Prentice Hall (ejemplo)', licencia: LC('2027-03-31', 'Perú, Colombia y Chile') } },
  { id: 8, titulo: 'El Principito', autor: 'Antoine de Saint-Exupéry', categoria: 'Infantil', editorial: 'Infantil Sur', motivo: 'orbita', paleta: 2,
    desc: 'El cuento más leído sobre la amistad y lo esencial, para todas las edades.',
    impreso: { isbn: '978-0-00-000008-0', precio: 35.0, stock: S([18, 0], [9, 1], [7, 0], [8, 0], [6, 1], [10, 0]) },
    digital: { isbn: '978-0-00-100008-7', precio: 14.9, formato: 'EPUB', paginas: 96, tamano: '0,9 MB', titular: 'Infantil Sur (ejemplo)', licencia: LC('2026-06-30') } },
  { id: 9, titulo: 'Paco Yunque', autor: 'César Vallejo', categoria: 'Infantil', editorial: 'Infantil Sur', motivo: 'arcos', paleta: 1,
    desc: 'Un cuento corto y duro sobre la desigualdad vista desde una escuela de provincia.',
    impreso: { isbn: '978-0-00-000009-7', precio: 22.0, stock: S([10, 0], [5, 0], [4, 0], [6, 0], [3, 0], [8, 0]) },
    digital: { isbn: '978-0-00-100009-4', precio: 7.9, formato: 'EPUB', paginas: 48, tamano: '0,5 MB', titular: 'Dominio público', licencia: PD } },
  { id: 10, titulo: 'Domain-Driven Design', autor: 'Eric Evans', categoria: 'Técnico', editorial: 'Addison-Wesley', motivo: 'rejilla', paleta: 3,
    desc: 'Modelar el software alrededor del dominio del negocio y su lenguaje.',
    impreso: { isbn: '978-0-00-000010-3', precio: 165.0, stock: S() },
    digital: { isbn: '978-0-00-100010-0', precio: 119.0, formato: 'PDF', paginas: 560, tamano: '11,4 MB', titular: 'Addison-Wesley (ejemplo)', licencia: LC('2027-09-30', 'Perú, Colombia y Chile') } },
  { id: 11, titulo: 'Los heraldos negros', autor: 'César Vallejo', categoria: 'Poesía', editorial: 'Peisa', motivo: 'ondas', paleta: 0,
    desc: 'El primer libro de poemas de Vallejo: dolor, fe y una voz inconfundible.',
    impreso: { isbn: '978-0-00-000011-0', precio: 31.0, stock: S([7, 0], [3, 0], [2, 0], [4, 0], [2, 0], [5, 1]) },
    digital: { isbn: '978-0-00-100011-7', precio: 12.9, formato: 'EPUB', paginas: 128, tamano: '0,7 MB', titular: 'Dominio público', licencia: PD } },
  { id: 12, titulo: 'La ciudad y los perros', autor: 'Mario Vargas Llosa', categoria: 'Ficción', editorial: 'Alfaguara', motivo: 'rejilla', paleta: 4,
    desc: 'La vida en un colegio militar de Lima, retratada con crudeza y precisión.',
    impreso: { isbn: '978-0-00-000012-7', precio: 64.9, stock: S([14, 2], [6, 0], [5, 0], [4, 1], [3, 0], [6, 0]) },
    digital: { isbn: '978-0-00-100012-4', precio: 36.9, formato: 'EPUB', paginas: 392, tamano: '1,9 MB', titular: 'Alfaguara (ejemplo)', licencia: LC('2027-06-30') } },
  { id: 13, titulo: 'Redoble por Rancas', autor: 'Manuel Scorza', categoria: 'Ficción', editorial: 'Horizonte', motivo: 'lineas', paleta: 5,
    desc: 'Crónica novelada de la lucha de los comuneros de Cerro de Pasco.',
    impreso: { isbn: '978-0-00-000013-4', precio: 52.0, stock: S([5, 0], [2, 0], [0, 0], [3, 0], [0, 0], [2, 0]) },
    digital: null },
  { id: 14, titulo: 'Historia de la República del Perú', autor: 'Jorge Basadre', categoria: 'No ficción', editorial: 'Peisa', motivo: 'arcos', paleta: 2,
    desc: 'Panorama de la vida política y social del Perú republicano.',
    impreso: { isbn: '978-0-00-000014-1', precio: 120.0, stock: S([4, 1], [0, 0], [0, 0], [1, 0], [0, 0], [0, 0]) },
    digital: null },
  { id: 15, titulo: 'Aprende C# con .NET', autor: 'A. Quispe (ficticio)', categoria: 'Técnico', editorial: 'Editorial Andes Tech', motivo: 'laberinto', paleta: 1,
    desc: 'Introducción práctica a C# y .NET con ejercicios pensados para estudiantes de Ingeniería de Sistemas.',
    impreso: { isbn: '978-0-00-000015-8', precio: 89.0, stock: S([8, 0], [5, 0], [4, 0], [3, 0], [2, 0], [9, 1]) },
    digital: { isbn: '978-0-00-100015-5', precio: 59.0, formato: 'PDF', paginas: 380, tamano: '6,3 MB', titular: 'Andes Tech (ejemplo)', licencia: LC('2028-12-31', 'Mundial') } },
  { id: 16, titulo: 'Cuentos andinos para niños', autor: 'L. Huamán (ficticio)', categoria: 'Infantil', editorial: 'Infantil Sur', motivo: 'ondas', paleta: 4,
    desc: 'Relatos de zorros, cóndores y montañas, ilustrados para leer en voz alta.',
    impreso: { isbn: '978-0-00-000016-5', precio: 28.0, stock: S([9, 0], [4, 0], [3, 0], [7, 0], [2, 0], [12, 0]) },
    digital: { isbn: '978-0-00-100016-2', precio: 14.9, formato: 'EPUB', paginas: 72, tamano: '4,5 MB', titular: 'Infantil Sur (ejemplo)', licencia: LC('2027-10-31', 'Mundial') } }
];

/* Biblioteca inicial del cliente de ejemplo (libros digitales ya comprados) */
BH.bibliotecaSemilla = [
  { obra: 4, fecha: '2026-09-28', usadas: 2 },
  { obra: 7, fecha: '2026-10-02', usadas: 5 },
  { obra: 9, fecha: '2026-10-09', usadas: 0 },
  { obra: 8, fecha: '2026-03-12', usadas: 1 }
];

/* Pedidos de ejemplo. Cada línea tiene su propio cumplimiento. */
BH.pedidosSemilla = [
  { id: 'BH-2026-000123', fecha: '2026-10-07', entrega: 'recojo', sucursal: 'aya', estado: 'transito',
    lineas: [{ obra: 5, formato: 'impreso', cant: 1 }, { obra: 2, formato: 'digital', cant: 1 }] },
  { id: 'BH-2026-000131', fecha: '2026-10-09', entrega: null, sucursal: null, estado: 'entregado',
    lineas: [{ obra: 9, formato: 'digital', cant: 1 }] },
  { id: 'BH-2026-000118', fecha: '2026-09-29', entrega: 'recojo', sucursal: 'aya', estado: 'entregado',
    lineas: [{ obra: 1, formato: 'impreso', cant: 1 }] }
];
