# Pruebas de usabilidad

Plan para medir RNF27 (compra fácil de completar sin ayuda) y detectar problemas de la interfaz durante el desarrollo, no solo al final (AD29). Es un plan **ligero y repetible**, adecuado a un proyecto académico con recursos limitados.

## 1. Objetivos y metas

| Meta | Valor | Fuente |
| :--- | :---: | :--- |
| Éxito de la tarea de compra con recojo, sin ayuda | ≥ 90 % de las personas | RNF27, WEB-22 |
| Puntuación **SUS** (escala de usabilidad del sistema) | ≥ 68 | RNF27 |
| Errores por tarea | < 1 en promedio | RNF27 |
| Pasos de la compra | ≤ 5 (≤ 4 si es solo digital) | RNF13, CAR-15 |

La puntuación SUS de 68 se toma como referencia de «promedio» ampliamente citada; la meta es no quedar por debajo.

## 2. Método

| Aspecto | Decisión |
| :--- | :--- |
| Tipo | Prueba moderada con pensamiento en voz alta (presencial o por videollamada) sobre el prototipo o la versión real. |
| Participantes | Rondas de **5 personas** (se detectan la mayoría de los problemas graves en cada ronda); al menos 2 rondas acumuladas antes de medir el 90 % (WEB-22). Perfiles de las [personas](05-personas-y-tareas.md) (lectoras de provincia, estudiantes) y, en cada ronda, al menos una persona con tecnología de apoyo. |
| Tareas | T01 a T04 y T09 para clientes; T05 a T08 para el personal ([personas y tareas](05-personas-y-tareas.md)). Guion sin pistas. |
| Dispositivos | Celular (mayoría) y computadora. |
| Datos | Entorno de pruebas con datos de ejemplo; nunca datos reales. |
| Consentimiento | Se informa el propósito, se pide permiso para registrar la pantalla y se pueden retirar en cualquier momento; no se recopilan datos personales identificables. |
| Moderación | Una persona modera y otra anota; el moderador no ayuda salvo que la persona esté bloqueada más de 2 minutos (cuenta como «con ayuda»). |

## 3. Qué se registra

| Dato | Cómo |
| :--- | :--- |
| Éxito de cada tarea | Completa sin ayuda / con ayuda / no completa. |
| Tiempo por tarea | Cronómetro. |
| Errores | Conteo y descripción (clic equivocado, mensaje no entendido, retroceso). |
| Satisfacción | Cuestionario SUS de 10 preguntas al terminar. |
| Observaciones | Frases textuales, dudas y gestos de frustración. |
| Gravedad | Cada hallazgo de 0 (cosmético) a 4 (catastrófico). |

## 4. Análisis y decisiones

1. Se agrupan los hallazgos por pantalla y por gravedad.
2. Los de gravedad 3 y 4 se corrigen antes de seguir con la spec (bloquean **Done**).
3. Se actualizan los prototipos y el sistema de diseño si el problema es de patrón.
4. Se registra el resultado en `progress/` con fecha, número de personas, tareas y métricas.
5. Se repite la prueba sobre las tareas con problemas hasta cumplir las metas.

## 5. Calendario por iteración

| Momento | Qué se prueba |
| :--- | :--- |
| Tras aprobar el prototipo | T01 a T03 y T09 sobre el prototipo (ronda 1). |
| Tras `web-base`, catálogo y carrito | T01 y T02 reales. |
| Tras checkout, pagos y biblioteca | T02 y T03 completas (ronda 2 para medir el 90 %). |
| Tras identidad | T04 (recuperación) con códigos reales. |
| Tras las áreas de Sucursal y Administración | T05 a T08 con personal o estudiantes que hagan de personal. |
| Antes de cerrar | Pasada final con lista de [accesibilidad](06-accesibilidad-wcag.md). |

## 6. Plantilla de informe

```
Fecha: …            Ronda: …            Versión probada: …
Participantes: n =  (perfil, dispositivo)
Resultados por tarea: éxito %, tiempo mediano, errores promedio
SUS promedio: …
Hallazgos (gravedad, pantalla, evidencia, propuesta)
Decisiones y responsables
```

## 7. Limitaciones

Con pocas personas, los porcentajes son indicativos: 9 de 10 equivale a 90 %, pero con mucha incertidumbre. Por eso se acumulan rondas y las decisiones se apoyan en los hallazgos cualitativos, no solo en el número.
