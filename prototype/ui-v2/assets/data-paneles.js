/* Datos ficticios de los paneles (entrega 2). Nada de esto es real. */
window.BH = window.BH || {};

BH.roles = { cliente: 'Cliente', encargado: 'Encargado de sucursal', logistica: 'Personal de logística', admin: 'Administrador central' };

/* Traslados. Estados: solicitado → aprobado → transito → recibido (o rechazado) */
BH.trasladosSemilla = [
  { id: 'TR-0042', obra: 5, cant: 1, origen: 'cus', destino: 'aya', estado: 'transito', fecha: '2026-10-08', motivo: 'Pedido BH-2026-000123', guia: 'CA-771204' },
  { id: 'TR-0043', obra: 4, cant: 2, origen: 'lim', destino: 'aya', estado: 'solicitado', fecha: '2026-10-09', motivo: 'Reposición de stock' },
  { id: 'TR-0044', obra: 3, cant: 3, origen: 'aya', destino: 'cus', estado: 'solicitado', fecha: '2026-10-09', motivo: 'Solicitud de Cusco' },
  { id: 'TR-0040', obra: 7, cant: 1, origen: 'lim', destino: 'aya', estado: 'recibido', fecha: '2026-10-02', motivo: 'Pedido BH-2026-000101', guia: 'CA-770980' },
  { id: 'TR-0045', obra: 6, cant: 6, origen: 'aya', destino: 'aqp', estado: 'aprobado', fecha: '2026-10-10', motivo: 'Solicitud de Arequipa' },
  { id: 'TR-0046', obra: 1, cant: 4, origen: 'lim', destino: 'piu', estado: 'transito', fecha: '2026-10-07', motivo: 'Reposición de stock', guia: 'CA-771190' },
  { id: 'TR-0047', obra: 12, cant: 3, origen: 'lim', destino: 'tru', estado: 'solicitado', fecha: '2026-10-10', motivo: 'Reposición de stock' },
  { id: 'TR-0041', obra: 2, cant: 2, origen: 'cus', destino: 'lim', estado: 'rechazado', fecha: '2026-10-03', motivo: 'Sin stock vendible en el origen' }
];

/* Pedidos de recojo de Ayacucho. Estados: confirmado → preparacion → listo → entregado */
BH.recojosSemilla = [
  { id: 'BH-2026-000124', cliente: 'Rosa Quispe', tel: '966 112 330', lineas: [{ obra: 6, cant: 2 }], estado: 'confirmado' },
  { id: 'BH-2026-000125', cliente: 'Luis Huamán', tel: '955 004 871', lineas: [{ obra: 1, cant: 1 }], estado: 'preparacion' },
  { id: 'BH-2026-000123', cliente: 'Diego (cuenta de ejemplo)', tel: '987 654 321', lineas: [{ obra: 5, cant: 1 }], estado: 'confirmado', espera: 'TR-0042' },
  { id: 'BH-2026-000120', cliente: 'Ana Flores', tel: '944 220 019', lineas: [{ obra: 9, cant: 1 }, { obra: 11, cant: 1 }], estado: 'listo' },
  { id: 'BH-2026-000118', cliente: 'Diego (cuenta de ejemplo)', tel: '987 654 321', lineas: [{ obra: 1, cant: 1 }], estado: 'entregado' }
];

/* Envíos a domicilio. Estados: pendiente → generada → reparto → entregado */
BH.guiasSemilla = [
  { id: 'EN-001', pedido: 'BH-2026-000140', cliente: 'María Ccanto', dir: 'Jr. Lima 345, Huamanga', ciudad: 'Ayacucho', estado: 'pendiente', guia: null, operador: 'Courier Andino (ejemplo)' },
  { id: 'EN-002', pedido: 'BH-2026-000138', cliente: 'Jorge Palomino', dir: 'Av. El Sol 120, Cusco', ciudad: 'Cusco', estado: 'generada', guia: 'CA-882013', operador: 'Courier Andino (ejemplo)' },
  { id: 'EN-003', pedido: 'BH-2026-000135', cliente: 'Karina Soto', dir: 'Calle Mercaderes 88, Arequipa', ciudad: 'Arequipa', estado: 'reparto', guia: 'CA-881940', operador: 'Courier Andino (ejemplo)' },
  { id: 'EN-004', pedido: 'BH-2026-000129', cliente: 'Pedro Alarcón', dir: 'Jr. Ayacucho 500, Huancayo', ciudad: 'Junín', estado: 'entregado', guia: 'CA-881502', operador: 'Courier Andino (ejemplo)' }
];

BH.usuariosSemilla = [
  { id: 1, nombre: 'Diego Amiquero', email: 'diego@ejemplo.pe', rol: 'cliente', sucursal: null, activo: true },
  { id: 2, nombre: 'Rosa Quispe', email: 'enc.ayacucho@babelhub.pe', rol: 'encargado', sucursal: 'aya', activo: true },
  { id: 3, nombre: 'Marco Paucar', email: 'enc.cusco@babelhub.pe', rol: 'encargado', sucursal: 'cus', activo: true },
  { id: 4, nombre: 'Lucía Ramos', email: 'enc.lima@babelhub.pe', rol: 'encargado', sucursal: 'lim', activo: true },
  { id: 5, nombre: 'Carlos Mendoza', email: 'logistica@babelhub.pe', rol: 'logistica', sucursal: null, activo: true },
  { id: 6, nombre: 'Administración Central', email: 'admin@babelhub.pe', rol: 'admin', sucursal: null, activo: true },
  { id: 7, nombre: 'Elena Vargas', email: 'elena@ejemplo.pe', rol: 'cliente', sucursal: null, activo: false }
];

BH.reclamosSemilla = [
  { id: 'HR-2026-1042', fecha: '2026-10-08', tipo: 'Reclamo', nombre: 'María Ccanto', desc: 'Libro impreso con la tapa dañada', det: 'El libro llegó con la tapa doblada y manchas.', ped: 'Cambio por otro ejemplar.', pedido: 'BH-2026-000102', estado: 'pendiente' },
  { id: 'HR-2026-1031', fecha: '2026-10-05', tipo: 'Queja', nombre: 'Jorge Palomino', desc: 'Atención en la sucursal Cusco', det: 'Esperé 40 minutos para recoger mi pedido.', ped: 'Mejorar la atención.', pedido: '', estado: 'respondido', respuesta: 'Lamentamos la espera. Reforzamos el personal en horas punta.' },
  { id: 'HR-2026-1020', fecha: '2026-10-03', tipo: 'Reclamo', nombre: 'Karina Soto', desc: 'Descarga fallida de un EPUB', det: 'El archivo descargado no abre en mi lector.', ped: 'Reembolso o archivo nuevo.', pedido: 'BH-2026-000099', estado: 'pendiente' }
];

/* Pedidos de otros clientes, para la vista de administración */
BH.adminPedidosSemilla = [
  { id: 'BH-2026-000140', cliente: 'María Ccanto', fecha: '2026-10-10', entrega: 'domicilio', sucursal: 'aya', estado: 'confirmado', lineas: [{ obra: 12, formato: 'impreso', cant: 1 }] },
  { id: 'BH-2026-000138', cliente: 'Jorge Palomino', fecha: '2026-10-09', entrega: 'domicilio', sucursal: 'cus', estado: 'preparacion', lineas: [{ obra: 2, formato: 'impreso', cant: 1 }, { obra: 15, formato: 'digital', cant: 1 }] },
  { id: 'BH-2026-000135', cliente: 'Karina Soto', fecha: '2026-10-08', entrega: 'domicilio', sucursal: 'aqp', estado: 'transito', lineas: [{ obra: 1, formato: 'impreso', cant: 2 }] },
  { id: 'BH-2026-000122', cliente: 'Ana Flores', fecha: '2026-10-06', entrega: null, sucursal: null, estado: 'entregado', lineas: [{ obra: 7, formato: 'digital', cant: 1 }] }
];

BH.auditSemilla = [
  { fecha: '2026-10-09 16:42', usuario: 'admin.central', rol: 'Administrador central', accion: 'Cambio de precio', detalle: 'Clean Architecture (impreso): S/ 129.00 → S/ 139.00' },
  { fecha: '2026-10-09 11:05', usuario: 'enc.cusco', rol: 'Encargado de sucursal', accion: 'Ajuste de inventario', detalle: 'Cusco · Los ríos profundos · −1 (producto dañado)' },
  { fecha: '2026-10-08 09:30', usuario: 'enc.ayacucho', rol: 'Encargado de sucursal', accion: 'Aprobación de traslado', detalle: 'TR-0042 Cusco → Ayacucho' },
  { fecha: '2026-10-07 18:12', usuario: 'admin.central', rol: 'Administrador central', accion: 'Anulación de pedido', detalle: 'BH-2026-000097 · pago duplicado' },
  { fecha: '2026-10-06 10:20', usuario: 'admin.central', rol: 'Administrador central', accion: 'Retiro de libro digital', detalle: 'El Principito (digital): licencia vencida' }
];
