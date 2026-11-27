> Sustituido por [Centros de costo compartidos](../shared_cost_centers/README.md).
> Las escrituras de opciones locales estan deshabilitadas; los campos anteriores son respaldo historico.

# Centro de costo de Contratos Boris

Columna **Centro de costo** (`cr07a_centrocosto`, opcion local) en
`cr07a_contratosboris`. Permite elegir los mismos cinco centros de Ventas que
Productos Cloud. El valor inicial y predeterminado es **103-00 - Ventas**.

| Opcion | Valor Dataverse | ID Siigo |
| --- | ---: | ---: |
| 101-00 - Ventas | 10100 | 589 |
| 102-00 - Ventas | 10200 | 602 |
| 103-00 - Ventas | 10300 | 611 |
| 104-00 - Ventas | 10400 | 622 |
| 105-00 - Ventas | 10500 | 633 |

El formulario principal y las vistas activa/inactiva muestran la columna junto
al nombre. El usuario puede ajustar cada contrato. La inicializacion completa
solo nulos para que una ejecucion posterior conserve esas selecciones.

## Facturacion

El flujo `BORIS - Facturacion COP mensual y anual`,
`76ea371e-9a4f-4862-90cd-4df8fe3a606a`, usa el comprobante **FV-1**, ID **7481**,
por indicacion expresa del usuario. Obtiene el centro del contrato y envia su
ID de Siigo como `cost_center` en la cabecera. Un nulo usa 103-00; un valor ajeno
a las cinco opciones impide construir la solicitud.

La numeracion inicial y el reintento exclusivo por colision consultan FV-1.
El reintento cambia solo el numero y conserva el centro de costo. La solicitud
exacta se guarda en el control de cobro antes del envio. Un payload pendiente
de otro comprobante o centro requiere revision: nunca se convierte ni reenvia
silenciosamente con una configuracion diferente.

Se conservan una factura por contrato, el precio mensual/anual, IVA, vencimiento,
clave de idempotencia, controles por periodo y actualizacion de la renovacion.
La programacion sigue a las 06:00 de Bogota, en modo Produccion. No se modifica
el flujo de Productos Cloud/TRM. Las facturas anteriores conservan su codigo.

## Aplicacion y validacion

Usa la sesion Azure CLI de Digital Tech con el tenant y la organizacion fijados
en el helper de acceso. Las dependencias estan en
`scripts/cloud_cost_centers/requirements.txt`.

```powershell
python scripts/boris_cost_centers/manage.py plan
python scripts/boris_cost_centers/manage.py schema
python scripts/boris_cost_centers/manage.py ui
python scripts/boris_cost_centers/manage.py initialize
python scripts/boris_cost_centers/validate_platform.py
python scripts/boris_cost_centers/manage.py deploy
python scripts/boris_cost_centers/manage.py verify
python scripts/boris_cost_centers/manage.py export
```

El script valida las expresiones reales en flujos temporales de Power Automate:
cinco centros y el predeterminado, consecutivos inicial y de reintento FV-1,
importes mensual/anual, rechazo de solicitudes de otro comprobante, otro centro
o sin centro, y rechazo de una opcion invalida. Los flujos solo tienen acciones
de datos; no contienen HTTP, conectores, correo ni creacion de facturas. Se
eliminan al terminar.

`deploy` exige las pruebas aprobadas, rechaza cambios concurrentes en la
definicion y no dispara el flujo original. `verify` comprueba contratos,
formularios, vistas, controles de cobro y que el flujo TRM siga igual.

Verificado el 27 de noviembre de 2026: ocho contratos con 103-00, un formulario
y dos vistas publicados, flujo Started/Produccion con FV-1. Los cuatro controles
de cobro y los marcadores de facturacion de los contratos permanecieron iguales.
La vista y el selector de las cinco opciones se comprobaron en la aplicacion
Gerencia con la sesion autenticada. Las pruebas sinteticas de Power Automate
pasaron y los dos flujos temporales se eliminaron; no se emitieron facturas reales.

Los respaldos y resultados locales estan en
`tmp/boris-cost-centers-20261127/`. La solucion existente `BorisFacturacionCOP`
usa el publicador `Cr44b61` (`cr07a`). Se exporta a `solutions/BorisFacturacionCOP`
despues de redactar en memoria las credenciales, antes de escribir archivos.
Los marcadores `__SIIGO_*__` no son credenciales utilizables para un despliegue.

Para revertir el flujo, revisar el respaldo `flow-before.redacted.json` y
restaurar solo la definicion anterior, reutilizando en memoria la autenticacion
del flujo vivo. No eliminar controles ni volver a emitir facturas. Conservar
la columna evita perder selecciones manuales.
