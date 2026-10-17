# Matriz de trazabilidad

> Generada por `dotnet run --project tools/SpecCheck -- --write-matrix`. No se edita a mano.

## Requisitos funcionales → specs

| RF | Specs | Requisitos EARS | Criterios |
| :--- | :--- | :--- | :--- |
| RF01 | ecommerce-catalogo | CAT-09 | AC-CAT-06 |
| RF02 | ecommerce-catalogo, inventario-fuentes | CAT-10, FUE-05 | AC-CAT-07, AC-FUE-03 |
| RF03 | ecommerce-carrito-checkout | CAR-01, CAR-02, CAR-03, CAR-04, CAR-16, CAR-24 | AC-CAR-01, AC-CAR-02, AC-CAR-03, AC-CAR-13, AC-CAR-22 |
| RF04 | ecommerce-carrito-checkout, inventario-seleccion-fuente | CAR-05, CAR-07, CAR-08, CAR-09, CAR-10, CAR-11, CAR-19, CAR-23, SEL-20 | AC-CAR-04, AC-CAR-06, AC-CAR-07, AC-CAR-08, AC-CAR-09, AC-CAR-10, AC-CAR-17, AC-CAR-21, AC-SEL-20 |
| RF05 | ecommerce-pagos | PAG-01, PAG-03, PAG-04, PAG-05, PAG-06, PAG-07, PAG-08, PAG-09, PAG-10, PAG-13, PAG-14, PAG-15 | AC-PAG-01, AC-PAG-03, AC-PAG-04, AC-PAG-05, AC-PAG-06, AC-PAG-07, AC-PAG-08, AC-PAG-11, AC-PAG-12, AC-PAG-13 |
| RF06 | ecommerce-pedidos | PED-01, PED-02, PED-03, PED-04, PED-09, PED-10, PED-11, PED-12, PED-15, PED-16, PED-17, PED-18 | AC-PED-01, AC-PED-02, AC-PED-03, AC-PED-07, AC-PED-08, AC-PED-09, AC-PED-10, AC-PED-13, AC-PED-14, AC-PED-15, AC-PED-16 |
| RF07 | ecommerce-pagos, ecommerce-pedidos, inventario-traslados, transversal-notificaciones | NOT-01, NOT-02, NOT-05, NOT-10, NOT-11, PAG-14, PED-05, PED-06, PED-15, TRA-10, TRA-11, TRA-22 | AC-NOT-01, AC-NOT-04, AC-NOT-09, AC-NOT-10, AC-NOT-12, AC-PAG-12, AC-PED-04, AC-PED-05, AC-PED-13, AC-TRA-08, AC-TRA-09, AC-TRA-21 |
| RF08 | asistente-compra-ia | ASI-01, ASI-02, ASI-03, ASI-04, ASI-06, ASI-10, ASI-17 | AC-ASI-01, AC-ASI-02, AC-ASI-03, AC-ASI-04, AC-ASI-06, AC-ASI-16, AC-ASI-18 |
| RF09 | ecommerce-devoluciones, inventario-ajustes | AJU-11, AJU-16, AJU-18, DEV-01, DEV-02, DEV-03, DEV-07, DEV-08, DEV-09, DEV-10, DEV-11, DEV-13, DEV-14, DEV-15 | AC-AJU-09, AC-AJU-14, AC-DEV-01, AC-DEV-02, AC-DEV-03, AC-DEV-07, AC-DEV-08, AC-DEV-09, AC-DEV-10, AC-DEV-12, AC-DEV-13, AC-DEV-14 |
| RF10 | ecommerce-catalogo | CAT-01, CAT-02, CAT-03, CAT-04, CAT-05, CAT-11, CAT-13, CAT-16 | AC-CAT-01, AC-CAT-02, AC-CAT-03, AC-CAT-08, AC-CAT-10, AC-CAT-12 |
| RF11 | ecommerce-envios | ENV-01, ENV-02, ENV-03, ENV-04, ENV-05, ENV-06, ENV-07, ENV-09, ENV-10, ENV-11, ENV-12 | AC-ENV-01, AC-ENV-02, AC-ENV-03, AC-ENV-04, AC-ENV-05, AC-ENV-06, AC-ENV-07, AC-ENV-09, AC-ENV-10, AC-ENV-11 |
| RF12 | inventario-fuentes | FUE-01, FUE-02, FUE-09, FUE-11, FUE-16, FUE-18 | AC-FUE-01, AC-FUE-07, AC-FUE-09, AC-FUE-13, AC-FUE-15 |
| RF13 | inventario-fuentes | FUE-03, FUE-05, FUE-07, FUE-15 | AC-FUE-02, AC-FUE-03, AC-FUE-05, AC-FUE-12 |
| RF14 | ecommerce-envios, inventario-seleccion-fuente | ENV-11, SEL-01, SEL-05, SEL-06, SEL-07, SEL-08, SEL-09, SEL-11, SEL-12, SEL-17, SEL-21, SEL-22, SEL-26, SEL-27, SEL-28, SEL-30, SEL-31, SEL-32 | AC-ENV-10, AC-SEL-01, AC-SEL-05, AC-SEL-06, AC-SEL-07, AC-SEL-08, AC-SEL-09, AC-SEL-11, AC-SEL-12, AC-SEL-17, AC-SEL-21, AC-SEL-22, AC-SEL-24, AC-SEL-26, AC-SEL-27, AC-SEL-28, AC-SEL-30, AC-SEL-31, AC-SEL-32 |
| RF15 | inventario-fuentes, inventario-seleccion-fuente, inventario-traslados | FUE-12, FUE-13, FUE-14, FUE-17, SEL-02, SEL-03, SEL-20, SEL-23, SEL-24, SEL-25, TRA-02, TRA-14, TRA-21 | AC-FUE-10, AC-FUE-11, AC-FUE-14, AC-SEL-02, AC-SEL-03, AC-SEL-20, AC-SEL-23, AC-SEL-25, AC-SEL-33, AC-SEL-34, AC-TRA-01, AC-TRA-13, AC-TRA-20, AC-TRA-24 |
| RF16 | inventario-traslados, transversal-notificaciones | NOT-01, NOT-09, TRA-01, TRA-03, TRA-04, TRA-05, TRA-06, TRA-07, TRA-08, TRA-10, TRA-12, TRA-14, TRA-15, TRA-16, TRA-17, TRA-18, TRA-19, TRA-20, TRA-21, TRA-22 | AC-NOT-01, AC-NOT-08, AC-TRA-01, AC-TRA-02, AC-TRA-03, AC-TRA-04, AC-TRA-05, AC-TRA-06, AC-TRA-07, AC-TRA-08, AC-TRA-10, AC-TRA-12, AC-TRA-13, AC-TRA-14, AC-TRA-15, AC-TRA-16, AC-TRA-17, AC-TRA-18, AC-TRA-19, AC-TRA-20, AC-TRA-21, AC-TRA-23, AC-TRA-24 |
| RF17 | inventario-ajustes | AJU-01, AJU-02, AJU-03, AJU-04, AJU-05, AJU-06, AJU-07, AJU-08, AJU-12, AJU-13, AJU-15, AJU-17, AJU-18 | AC-AJU-01, AC-AJU-02, AC-AJU-03, AC-AJU-04, AC-AJU-05, AC-AJU-06, AC-AJU-07, AC-AJU-10, AC-AJU-11, AC-AJU-12, AC-AJU-13, AC-AJU-14 |
| RF18 | identidad-seguridad | IDE-02, IDE-03, IDE-10, IDE-14, IDE-46, IDE-60 | AC-IDE-02, AC-IDE-03, AC-IDE-09, AC-IDE-12, AC-IDE-41, AC-IDE-50 |
| RF19 | identidad-seguridad | IDE-01, IDE-04, IDE-05, IDE-06, IDE-07, IDE-08, IDE-13, IDE-23, IDE-38, IDE-49 | AC-IDE-01, AC-IDE-02, AC-IDE-04, AC-IDE-05, AC-IDE-06, AC-IDE-07, AC-IDE-21, AC-IDE-35, AC-IDE-44 |
| RF20 | ecommerce-pedidos, inventario-traslados, transversal-auditoria | AUD-01, AUD-02, AUD-07, AUD-09, AUD-14, AUD-15, AUD-16, AUD-17, AUD-18, AUD-20, AUD-21, AUD-22, PED-13, TRA-13 | AC-AUD-01, AC-AUD-06, AC-AUD-11, AC-AUD-13, AC-AUD-14, AC-AUD-15, AC-AUD-16, AC-AUD-17, AC-AUD-18, AC-AUD-19, AC-PED-11, AC-TRA-11 |
| RF21 | *diferido* | | |
| RF22 | ecommerce-biblioteca-digital | BIB-01, BIB-02 | AC-BIB-01, AC-BIB-02 |
| RF23 | ecommerce-biblioteca-digital | BIB-03, BIB-04, BIB-05, BIB-06, BIB-08, BIB-10, BIB-11, BIB-13, BIB-16, BIB-17 | AC-BIB-03, AC-BIB-04, AC-BIB-05, AC-BIB-07, AC-BIB-09, AC-BIB-11, AC-BIB-14, AC-BIB-15 |
| RF24 | ecommerce-biblioteca-digital, ecommerce-catalogo | BIB-09, CAT-06, CAT-07, CAT-08, CAT-15, CAT-16 | AC-BIB-08, AC-CAT-03, AC-CAT-04, AC-CAT-05, AC-CAT-12 |
| RF25 | ecommerce-libro-reclamaciones, web-base | REC-01, REC-02, REC-03, REC-04, REC-05, REC-06, REC-07, REC-09, REC-12, REC-13, WEB-08 | AC-REC-01, AC-REC-02, AC-REC-03, AC-REC-04, AC-REC-05, AC-REC-06, AC-REC-07, AC-REC-10, AC-REC-11, AC-WEB-05 |
| RF26 | identidad-seguridad | IDE-01, IDE-16, IDE-17, IDE-18, IDE-19, IDE-29, IDE-56 | AC-IDE-01, AC-IDE-14, AC-IDE-15, AC-IDE-16, AC-IDE-17, AC-IDE-26, AC-IDE-49 |
| RF27 | identidad-seguridad | IDE-12, IDE-24, IDE-25, IDE-26, IDE-27, IDE-28, IDE-32, IDE-37, IDE-47, IDE-48 | AC-IDE-11, AC-IDE-22, AC-IDE-23, AC-IDE-24, AC-IDE-25, AC-IDE-29, AC-IDE-34, AC-IDE-43 |
| RF28 | ecommerce-carrito-checkout, identidad-seguridad, inventario-seleccion-fuente | CAR-17, CAR-19, CAR-21, IDE-29, SEL-13, SEL-14, SEL-15, SEL-16, SEL-17, SEL-18, SEL-19 | AC-CAR-14, AC-CAR-17, AC-CAR-19, AC-IDE-16, AC-IDE-26, AC-SEL-13, AC-SEL-14, AC-SEL-15, AC-SEL-16, AC-SEL-17, AC-SEL-18, AC-SEL-19 |
| RF29 | web-base, web-indice-infinito | IND-01, IND-02, IND-03, IND-04, IND-05, IND-06, IND-08, IND-09, WEB-17 | AC-IND-01, AC-IND-02, AC-IND-03, AC-IND-04, AC-IND-05, AC-IND-07, AC-WEB-13 |

## Specs

| Spec | Servicio | Estado | Requisitos | Criterios | Depende de |
| :--- | :--- | :--- | ---: | ---: | :--- |
| asistente-compra-ia | Asistente | Draft | 19 | 18 | ecommerce-catalogo, ecommerce-pedidos, identidad-seguridad, transversal-seguridad |
| ecommerce-biblioteca-digital | E-commerce | Draft | 18 | 15 | ecommerce-catalogo, ecommerce-pagos, transversal-auditoria, transversal-seguridad |
| ecommerce-carrito-checkout | E-commerce | Draft | 24 | 22 | ecommerce-catalogo, inventario-seleccion-fuente, identidad-seguridad, transversal-seguridad |
| ecommerce-catalogo | E-commerce | Draft | 16 | 12 | inventario-fuentes, transversal-auditoria |
| ecommerce-devoluciones | E-commerce | Draft | 15 | 14 | ecommerce-pedidos, ecommerce-pagos, ecommerce-biblioteca-digital, inventario-ajustes, transversal-auditoria |
| ecommerce-envios | E-commerce | Draft | 12 | 11 | ecommerce-pedidos, transversal-notificaciones, transversal-auditoria |
| ecommerce-libro-reclamaciones | E-commerce | Draft | 13 | 11 | transversal-auditoria, transversal-seguridad |
| ecommerce-pagos | E-commerce | Draft | 15 | 13 | ecommerce-carrito-checkout, inventario-seleccion-fuente, transversal-auditoria |
| ecommerce-pedidos | E-commerce | Draft | 18 | 16 | ecommerce-pagos, inventario-seleccion-fuente, transversal-notificaciones, transversal-auditoria |
| identidad-seguridad | Identidad | Draft | 61 | 50 | transversal-auditoria, transversal-seguridad |
| inventario-ajustes | Inventario | Draft | 18 | 14 | inventario-fuentes, identidad-seguridad, transversal-auditoria |
| inventario-fuentes | Inventario | Draft | 18 | 15 | identidad-seguridad, transversal-auditoria |
| inventario-seleccion-fuente | Inventario | Draft | 32 | 34 | inventario-fuentes, identidad-seguridad, transversal-auditoria, transversal-seguridad |
| inventario-traslados | Inventario | Draft | 23 | 24 | inventario-fuentes, inventario-seleccion-fuente, identidad-seguridad, transversal-auditoria, transversal-notificaciones |
| transversal-auditoria | Transversal | Draft | 22 | 19 | — |
| transversal-notificaciones | Transversal | Draft | 13 | 13 | identidad-seguridad, transversal-seguridad |
| transversal-seguridad | Transversal | Draft | 29 | 26 | transversal-auditoria |
| web-base | Web | Draft | 35 | 29 | identidad-seguridad, ecommerce-libro-reclamaciones, ecommerce-catalogo, ecommerce-carrito-checkout, ecommerce-biblioteca-digital, transversal-auditoria, transversal-seguridad |
| web-indice-infinito | Web | Draft | 9 | 7 | web-base, transversal-seguridad |
