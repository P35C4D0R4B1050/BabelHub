---
id: nombre-de-la-feature
titulo: Título corto de la feature
estado: Draft
servicio: Inventario | E-commerce | Identidad | Asistente | Web | Transversal
prefijo: XXX
origen: [RF00, NN00]
calidad: [AC00, RNF00]
depende: []
---

# Spec — Título de la feature

> **Qué y por qué, nunca cómo.** Esta spec describe comportamiento observable. No nombra tecnologías (bases de datos, frameworks, proveedores): eso va en `plan.md`. La valida `dotnet run --project tools/SpecCheck`.

## 1. Propósito

Dos o tres frases: qué problema resuelve y para quién. Enlaza las necesidades (NN) del negocio.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Lo que cubre esta feature. | Lo que se deja para otra spec (indica cuál). |

## 3. Requisitos (EARS)

Plantillas EARS: **Ubicuo** («El sistema deberá…»), **Evento** («Cuando…, el sistema deberá…»), **Estado** («Mientras…, el sistema deberá…»), **No deseado** («Si…, entonces el sistema deberá…») y **Opcional** («Donde…, el sistema deberá…»). Un requisito, un comportamiento verificable.

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| XXX-01 | Ubicuo | El sistema deberá … | RF00 |

## 4. Criterios de aceptación

Cada requisito debe estar cubierto por al menos un criterio. Cada criterio tendrá al menos una prueba que lo nombre.

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-XXX-01 | XXX-01 | **Dado** … **cuando** … **entonces** … |

## 5. Reglas e invariantes

- Reglas del negocio que siempre se cumplen (las del dominio).

## 6. Casos límite y errores

- Qué pasa ante entradas inválidas, concurrencia, fallos de dependencias, permisos.

## 7. Preguntas abiertas

Una spec no puede aprobarse con preguntas sin resolver. Marca con `[x]` cuando se resuelva y escribe la respuesta.

- [ ] Pregunta, con la persona que debe responder.
