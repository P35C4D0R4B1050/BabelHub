/* Datos ficticios del prototipo. Nada de esto es real. */
window.BH = window.BH || {};

BH.sucursales = [
  { id: 'lim', nombre: 'Lima Centro', region: 'Lima' },
  { id: 'aqp', nombre: 'Arequipa', region: 'Arequipa' },
  { id: 'tru', nombre: 'Trujillo', region: 'La Libertad' },
  { id: 'cus', nombre: 'Cusco', region: 'Cusco' },
  { id: 'piu', nombre: 'Piura', region: 'Piura' },
  { id: 'aya', nombre: 'Ayacucho', region: 'Ayacucho' }
];

BH.categorias = ['Ficción', 'No ficción', 'Infantil', 'Escolar', 'Técnico'];

/* stock: { idSucursal: [físico, reservado] } — vendible = físico − reservado */
BH.libros = [
  { id: 1, titulo: 'Conversación en La Catedral', autor: 'Mario Vargas Llosa', editorial: 'Alfaguara', isbn: '978-0-00-000001-1', categoria: 'Ficción', precio: 69.9, hue: 8,
    desc: 'Novela sobre el Perú de los años cincuenta: política, poder y desencanto contados en una larga conversación.',
    stock: { lim: [24, 3], aqp: [10, 1], tru: [8, 0], cus: [6, 2], piu: [5, 0], aya: [9, 1] } },
  { id: 2, titulo: 'Los ríos profundos', autor: 'José María Arguedas', editorial: 'Horizonte', isbn: '978-0-00-000002-8', categoria: 'Ficción', precio: 42.0, hue: 150,
    desc: 'Ernesto, un adolescente entre dos mundos, recorre Abancay y el Cusco en una novela esencial de la literatura andina.',
    stock: { lim: [12, 2], aqp: [4, 0], tru: [3, 0], cus: [14, 1], piu: [0, 0], aya: [7, 2] } },
  { id: 3, titulo: 'Cien años de soledad', autor: 'Gabriel García Márquez', editorial: 'Alfaguara', isbn: '978-0-00-000003-5', categoria: 'Ficción', precio: 59.9, hue: 38,
    desc: 'La saga de la familia Buendía en Macondo, un clásico del realismo mágico.',
    stock: { lim: [30, 4], aqp: [12, 2], tru: [9, 1], cus: [11, 0], piu: [7, 0], aya: [5, 5] } },
  { id: 4, titulo: 'Tradiciones peruanas', autor: 'Ricardo Palma', editorial: 'Peisa', isbn: '978-0-00-000004-2', categoria: 'No ficción', precio: 48.5, hue: 28,
    desc: 'Relatos breves que mezclan historia, humor y crónica de la Lima virreinal y republicana.',
    stock: { lim: [3, 1], aqp: [0, 0], tru: [2, 0], cus: [0, 0], piu: [1, 0], aya: [0, 0] } },
  { id: 5, titulo: 'El mundo es ancho y ajeno', autor: 'Ciro Alegría', editorial: 'Peisa', isbn: '978-0-00-000005-9', categoria: 'Ficción', precio: 54.0, hue: 95,
    desc: 'La lucha de una comunidad indígena por su tierra en los Andes del norte.',
    stock: { lim: [0, 0], aqp: [0, 0], tru: [0, 0], cus: [4, 1], piu: [0, 0], aya: [0, 0] } },
  { id: 6, titulo: 'Aves sin nido', autor: 'Clorinda Matto de Turner', editorial: 'Escolar Andina', isbn: '978-0-00-000006-6', categoria: 'Escolar', precio: 29.9, hue: 205,
    desc: 'Novela fundacional del indigenismo peruano, lectura habitual en el colegio.',
    stock: { lim: [20, 2], aqp: [8, 0], tru: [6, 0], cus: [9, 1], piu: [5, 0], aya: [15, 3] } },
  { id: 7, titulo: 'Clean Architecture', autor: 'Robert C. Martin', editorial: 'Prentice Hall', isbn: '978-0-00-000007-3', categoria: 'Técnico', precio: 139.0, hue: 215,
    desc: 'Guía práctica para ordenar el software en capas y dependencias que apuntan hacia el dominio.',
    stock: { lim: [6, 1], aqp: [2, 0], tru: [0, 0], cus: [0, 0], piu: [0, 0], aya: [2, 0] } },
  { id: 8, titulo: 'El Principito', autor: 'Antoine de Saint-Exupéry', editorial: 'Infantil Sur', isbn: '978-0-00-000008-0', categoria: 'Infantil', precio: 35.0, hue: 265,
    desc: 'El cuento más leído sobre la amistad y lo esencial, para todas las edades.',
    stock: { lim: [18, 0], aqp: [9, 1], tru: [7, 0], cus: [8, 0], piu: [6, 1], aya: [10, 0] } },
  { id: 9, titulo: 'Paco Yunque', autor: 'César Vallejo', editorial: 'Infantil Sur', isbn: '978-0-00-000009-7', categoria: 'Infantil', precio: 22.0, hue: 320,
    desc: 'Un cuento corto y duro sobre la desigualdad vista desde una escuela de provincia.',
    stock: { lim: [10, 0], aqp: [5, 0], tru: [4, 0], cus: [6, 0], piu: [3, 0], aya: [8, 0] } },
  { id: 10, titulo: 'Domain-Driven Design', autor: 'Eric Evans', editorial: 'Addison-Wesley', isbn: '978-0-00-000010-3', categoria: 'Técnico', precio: 165.0, hue: 190,
    desc: 'Modelar el software alrededor del dominio del negocio y su lenguaje.',
    stock: { lim: [0, 0], aqp: [0, 0], tru: [0, 0], cus: [0, 0], piu: [0, 0], aya: [0, 0] } }
];

/* Traslados de ejemplo. Estados: solicitado → aprobado → transito → recibido */
BH.traslados = [
  { id: 'TR-0042', libro: 5, cant: 2, origen: 'cus', destino: 'aya', estado: 'transito', fecha: '2026-10-08', motivo: 'Pedido BH-2026-000118' },
  { id: 'TR-0043', libro: 4, cant: 2, origen: 'lim', destino: 'aya', estado: 'solicitado', fecha: '2026-10-09', motivo: 'Reposición' },
  { id: 'TR-0044', libro: 3, cant: 4, origen: 'aya', destino: 'cus', estado: 'solicitado', fecha: '2026-10-09', motivo: 'Solicitud de Cusco' },
  { id: 'TR-0040', libro: 7, cant: 1, origen: 'lim', destino: 'aya', estado: 'recibido', fecha: '2026-10-02', motivo: 'Pedido BH-2026-000101' },
  { id: 'TR-0045', libro: 6, cant: 6, origen: 'aya', destino: 'aqp', estado: 'aprobado', fecha: '2026-10-10', motivo: 'Solicitud de Arequipa' }
];

BH.auditSemilla = [
  { fecha: '2026-10-09 16:42', usuario: 'admin.central', rol: 'Administrador central', accion: 'Cambio de precio', detalle: 'Clean Architecture: S/ 129.00 → S/ 139.00' },
  { fecha: '2026-10-09 11:05', usuario: 'enc.cusco', rol: 'Encargado de sucursal', accion: 'Ajuste de inventario', detalle: 'Cusco · Los ríos profundos · −1 (daño)' },
  { fecha: '2026-10-08 09:30', usuario: 'enc.ayacucho', rol: 'Encargado de sucursal', accion: 'Aprobación de traslado', detalle: 'TR-0042 Cusco → Ayacucho' },
  { fecha: '2026-10-07 18:12', usuario: 'admin.central', rol: 'Administrador central', accion: 'Anulación de pedido', detalle: 'BH-2026-000097 · pago duplicado' }
];
