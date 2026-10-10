# Matriz de trazabilidad

> Generada por `dotnet run --project tools/SpecCheck -- --write-matrix`. No se edita a mano.

## Requisitos funcionales → specs

| RF | Specs | Requisitos EARS | Criterios |
| :--- | :--- | :--- | :--- |
| RF01 | ecommerce-catalogo | CAT-09 | AC-CAT-06 |
| RF02 | ecommerce-catalogo, inventario-fuentes | CAT-10, FUE-05 | AC-CAT-07, AC-FUE-03 |
| RF03 | ecommerce-carrito-checkout | CAR-01, CAR-02, CAR-03, CAR-04, CAR-16 | AC-CAR-01, AC-CAR-02, AC-CAR-03, AC-CAR-13 |
| RF04 | ecommerce-carrito-checkout | CAR-05, CAR-07, CAR-08, CAR-09, CAR-10, CAR-11 | AC-CAR-04, AC-CAR-06, AC-CAR-07, AC-CAR-08, AC-CAR-09, AC-CAR-10 |
| RF05 | ecommerce-pagos | PAG-01, PAG-03, PAG-04, PAG-05, PAG-06, PAG-07, PAG-08, PAG-09, PAG-10 | AC-PAG-01, AC-PAG-03, AC-PAG-04, AC-PAG-05, AC-PAG-06, AC-PAG-07, AC-PAG-08 |
| RF06 | ecommerce-pedidos | PED-01, PED-02, PED-03, PED-04, PED-09, PED-10, PED-11, PED-12 | AC-PED-01, AC-PED-02, AC-PED-03, AC-PED-07, AC-PED-08, AC-PED-09, AC-PED-10 |
| RF07 | ecommerce-pedidos, inventario-traslados, transversal-notificaciones | NOT-01, NOT-02, NOT-05, NOT-10, NOT-11, PED-05, PED-06, TRA-10, TRA-11 | AC-NOT-01, AC-NOT-04, AC-NOT-09, AC-NOT-10, AC-PED-04, AC-PED-05, AC-TRA-08, AC-TRA-09 |
| RF08 | asistente-compra-ia | ASI-01, ASI-02, ASI-03, ASI-04, ASI-06, ASI-10 | AC-ASI-01, AC-ASI-02, AC-ASI-03, AC-ASI-04, AC-ASI-06 |
| RF09 | ecommerce-devoluciones, inventario-ajustes | AJU-11, DEV-01, DEV-02, DEV-03, DEV-07, DEV-08, DEV-09, DEV-10, DEV-11 | AC-AJU-09, AC-DEV-01, AC-DEV-02, AC-DEV-03, AC-DEV-07, AC-DEV-08, AC-DEV-09, AC-DEV-10 |
| RF10 | ecommerce-catalogo | CAT-01, CAT-02, CAT-03, CAT-04, CAT-05, CAT-11, CAT-13 | AC-CAT-01, AC-CAT-02, AC-CAT-03, AC-CAT-08, AC-CAT-10 |
| RF11 | ecommerce-envios | ENV-01, ENV-02, ENV-03, ENV-04, ENV-05, ENV-06, ENV-07, ENV-09, ENV-10 | AC-ENV-01, AC-ENV-02, AC-ENV-03, AC-ENV-04, AC-ENV-05, AC-ENV-06, AC-ENV-07, AC-ENV-09 |
| RF12 | inventario-fuentes | FUE-01, FUE-02, FUE-09, FUE-11 | AC-FUE-01, AC-FUE-07, AC-FUE-09 |
| RF13 | inventario-fuentes | FUE-03, FUE-05, FUE-07 | AC-FUE-02, AC-FUE-03, AC-FUE-05 |
| RF14 | inventario-seleccion-fuente | SEL-01, SEL-05, SEL-06, SEL-07, SEL-08, SEL-09, SEL-11, SEL-12 | AC-SEL-01, AC-SEL-05, AC-SEL-06, AC-SEL-07, AC-SEL-08, AC-SEL-09, AC-SEL-11, AC-SEL-12 |
| RF15 | inventario-seleccion-fuente, inventario-traslados | SEL-02, SEL-03, TRA-02 | AC-SEL-02, AC-SEL-03, AC-TRA-01 |
| RF16 | inventario-traslados, transversal-notificaciones | NOT-01, NOT-09, TRA-01, TRA-03, TRA-04, TRA-05, TRA-06, TRA-07, TRA-08, TRA-10, TRA-12 | AC-NOT-01, AC-NOT-08, AC-TRA-01, AC-TRA-02, AC-TRA-03, AC-TRA-04, AC-TRA-05, AC-TRA-06, AC-TRA-07, AC-TRA-08, AC-TRA-10 |
| RF17 | inventario-ajustes | AJU-01, AJU-02, AJU-03, AJU-04, AJU-05, AJU-06, AJU-07, AJU-08, AJU-12 | AC-AJU-01, AC-AJU-02, AC-AJU-03, AC-AJU-04, AC-AJU-05, AC-AJU-06, AC-AJU-07 |
| RF18 | identidad-seguridad | IDE-02, IDE-03, IDE-10, IDE-14 | AC-IDE-02, AC-IDE-03, AC-IDE-09, AC-IDE-12 |
| RF19 | identidad-seguridad | IDE-01, IDE-04, IDE-05, IDE-06, IDE-07, IDE-08, IDE-13 | AC-IDE-01, AC-IDE-02, AC-IDE-04, AC-IDE-05, AC-IDE-06, AC-IDE-07 |
| RF20 | ecommerce-pedidos, inventario-traslados, transversal-auditoria | AUD-01, AUD-02, AUD-07, AUD-09, PED-13, TRA-13 | AC-AUD-01, AC-AUD-06, AC-PED-11, AC-TRA-11 |
| RF21 | *diferido* | | |
| RF22 | ecommerce-biblioteca-digital | BIB-01, BIB-02 | AC-BIB-01, AC-BIB-02 |
| RF23 | ecommerce-biblioteca-digital | BIB-03, BIB-04, BIB-05, BIB-06, BIB-08, BIB-10, BIB-11, BIB-13 | AC-BIB-03, AC-BIB-04, AC-BIB-05, AC-BIB-07, AC-BIB-09, AC-BIB-11 |
| RF24 | ecommerce-biblioteca-digital, ecommerce-catalogo | BIB-09, CAT-06, CAT-07, CAT-08, CAT-15 | AC-BIB-08, AC-CAT-03, AC-CAT-04, AC-CAT-05 |
| RF25 | ecommerce-libro-reclamaciones, web-base | REC-01, REC-02, REC-03, REC-04, REC-05, REC-06, REC-07, REC-09, WEB-08 | AC-REC-01, AC-REC-02, AC-REC-03, AC-REC-04, AC-REC-05, AC-REC-06, AC-REC-07, AC-WEB-05 |
| RF26 | identidad-seguridad | IDE-01, IDE-16, IDE-17, IDE-18, IDE-19 | AC-IDE-01, AC-IDE-14, AC-IDE-15, AC-IDE-16, AC-IDE-17 |
| RF27 | identidad-seguridad | IDE-21, IDE-22 | AC-IDE-19, AC-IDE-20 |
| RF28 | ecommerce-carrito-checkout, inventario-seleccion-fuente | CAR-17, SEL-13, SEL-14, SEL-15 | AC-CAR-14, AC-SEL-13, AC-SEL-14, AC-SEL-15 |
| RF29 | web-base, web-indice-infinito | IND-01, IND-02, IND-03, IND-04, IND-05, IND-06, IND-08, WEB-17 | AC-IND-01, AC-IND-02, AC-IND-03, AC-IND-04, AC-IND-05, AC-WEB-13 |

## Specs

| Spec | Servicio | Estado | Requisitos | Criterios | Depende de |
| :--- | :--- | :--- | ---: | ---: | :--- |
| asistente-compra-ia | Asistente | Draft | 14 | 13 | ecommerce-catalogo, ecommerce-pedidos, identidad-seguridad |
| ecommerce-biblioteca-digital | E-commerce | Draft | 15 | 13 | ecommerce-catalogo, ecommerce-pagos, transversal-auditoria |
| ecommerce-carrito-checkout | E-commerce | Draft | 17 | 14 | ecommerce-catalogo, inventario-seleccion-fuente |
| ecommerce-catalogo | E-commerce | Draft | 15 | 11 | inventario-fuentes, transversal-auditoria |
| ecommerce-devoluciones | E-commerce | Draft | 12 | 11 | ecommerce-pedidos, ecommerce-pagos, ecommerce-biblioteca-digital, inventario-ajustes |
| ecommerce-envios | E-commerce | Draft | 10 | 9 | ecommerce-pedidos, transversal-notificaciones |
| ecommerce-libro-reclamaciones | E-commerce | Draft | 10 | 8 | transversal-auditoria, transversal-notificaciones |
| ecommerce-pagos | E-commerce | Draft | 12 | 10 | ecommerce-carrito-checkout, inventario-seleccion-fuente, transversal-auditoria |
| ecommerce-pedidos | E-commerce | Draft | 14 | 12 | ecommerce-pagos, inventario-seleccion-fuente, transversal-notificaciones, transversal-auditoria |
| identidad-seguridad | Identidad | Draft | 22 | 20 | transversal-auditoria |
| inventario-ajustes | Inventario | Draft | 12 | 9 | inventario-fuentes, transversal-auditoria |
| inventario-fuentes | Inventario | Draft | 11 | 9 | transversal-auditoria |
| inventario-seleccion-fuente | Inventario | Draft | 15 | 15 | inventario-fuentes, transversal-auditoria |
| inventario-traslados | Inventario | Draft | 13 | 11 | inventario-fuentes, inventario-seleccion-fuente, transversal-auditoria, transversal-notificaciones |
| transversal-auditoria | Transversal | Draft | 12 | 10 | — |
| transversal-notificaciones | Transversal | Draft | 11 | 10 | identidad-seguridad |
| transversal-seguridad | Transversal | Draft | 15 | 13 | transversal-auditoria |
| web-base | Web | Draft | 24 | 19 | identidad-seguridad, ecommerce-libro-reclamaciones |
| web-indice-infinito | Web | Draft | 8 | 6 | web-base |
