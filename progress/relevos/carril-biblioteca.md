# Encargo del carril de la biblioteca digital (nuevo) — ECO-8

Lee primero `AGENTS.md`, `progress/coordinacion.md`, `progress/relevos/coordinador.md` §6 y `specs/ecommerce-biblioteca-digital/` (spec `Approved`, plan `Planned`, 26 tareas en subbloques ECO-8a a 8e: `progress/log/2026-11-18-planes-eco-7.md`). Nivel C (archivos digitales, URLs firmadas, licencias, acceso).

## Qué existe ya
`IAccesosParaDevoluciones` declarada por V en `Application/Biblioteca` (aditivo, a tu cargo adoptarla); contenedores `libros` y `portadas` y credenciales del almacén (`scripts/almacen.sh`, `infra/almacen/`); `IPosesionDeEdiciones.Posee` → `ResultadoDePosesion` (decisión DD-06 de B), que Biblioteca implementa para el Carrito. Los archivos digitales nunca van al repositorio ni a la base: solo almacén de objetos y URL firmada de corta duración (AGENTS §3.11); un libro digital no consulta al Inventario (§3.12).

## Reglas
Las del resto de carriles: fixtures sin contraseñas literales, espejar catálogos de auditoría en los dos servicios, texto español del motivo en el presentador, ninguna excepción a pruebas de arquitectura, `Shared` solo aditivo, no declarar contratos de otros módulos, mensaje de una línea al coordinador al cerrar cada bloque, integración completa solo con turno, sin `push`. Orden de ejecución: ECO-8a (Dominio) a 8e; cierra con revisores de seguridad y arquitectura.
