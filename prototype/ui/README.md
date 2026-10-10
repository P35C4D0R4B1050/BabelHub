# Prototipo de interfaz de BabelHub

Prototipo **descartable** en HTML estático para validar diseño y recorridos antes de escribir las vistas Razor de `BabelHub.Web`. No forma parte de la solución .NET.

- **Stack:** Tailwind CSS 4 (build del navegador) y daisyUI 5, ambos por CDN. No hay build ni dependencias locales; necesita conexión a Internet para cargar CDN y fuentes.
- **Datos:** ficticios, en `assets/data.js`. El carrito y la auditoría se guardan en `localStorage`.
- **Tema:** `assets/theme.css` define los temas `babelhub` (claro) y `babelhub-dark`.
- **Estados de pantalla:** cada página tiene una barra inferior izquierda con un selector de estado (`?estado=cargando|vacio|error|...`).

## Cómo abrirlo

Abre `index.html` con un doble clic, o sirve la carpeta:

```bash
python -m http.server 8080 --directory prototype/ui
```

## Qué cubre

| Área | Páginas | Requisitos |
| :--- | :--- | :--- |
| Tienda | catálogo, detalle, carrito, checkout (5 pasos), seguimiento, asistente de compra | RF01–RF09, RNF13, AC03, AC08 |
| Sucursal | inventario con ajustes, traslados | RF12, RF16, RF17 |
| Admin | catálogo, auditoría | RF10, RF20 |

Lo que se aprueba aquí pasa a `docs/08-interfaz-de-usuario.md` y a la decisión AD19.
