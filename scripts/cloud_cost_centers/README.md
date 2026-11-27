# Centros de costo de Productos Cloud

La facturacion con TRM obtiene el centro de costo de cada fila de **Productos
CLOUD**, tabla `cr07a_salesperformancerecord`, y crea facturas FV codigo 1
(`document.id = 7481`). En Gerencia esta tabla se abre desde **Productos CLOUD**,
vista **Productos x Cliente - CLOUD**. Cada asignacion de producto a un cliente
puede tener su propio centro, aunque varias asignaciones usen el mismo catalogo.
El campo **Centro de costo**, nombre logico `cr07a_centrocosto`, es una opcion
local con estas correspondencias:

| Opcion visible | Valor Dataverse | ID Siigo |
| --- | ---: | ---: |
| 101-00 - Ventas | 10100 | 589 |
| 102-00 - Ventas | 10200 | 602 |
| 103-00 - Ventas | 10300 | 611 |
| 104-00 - Ventas | 10400 | 622 |
| 105-00 - Ventas | 10500 | 633 |

El formulario principal y las vistas de productos activos e inactivos muestran
el campo en la segunda posicion. El valor inicial y
predeterminado del formulario es 101-00. La inicializacion solo completa nulos
en ejecuciones posteriores; conserva las selecciones manuales.

Siigo recibe `cost_center` en la cabecera de la factura. Por eso el flujo agrupa
por **cliente y centro de costo**. Varias lineas del mismo cliente y centro
siguen compartiendo factura; centros distintos producen facturas distintas.
La marca de facturado se actualiza solamente para las lineas del grupo enviado.
Los grupos siguen ejecutandose secuencialmente y el consecutivo de FV-1 se
consulta antes de cada envio. Una opcion nula usa 101-00; una opcion desconocida
no produce un payload valido. Los IDs se verifican contra el catalogo de Siigo.
La expansion `cr07a_Producto` consulta solo el codigo Siigo; la columna del
catalogo `cr07a_precioscloud` no determina el centro de costo de la factura.
El campo previo del catalogo se conserva para respaldo, pero se retiro de sus
formularios y vistas. El lugar de edicion operativo es Productos CLOUD.

## Alcance

- Organizacion: `https://orgc79ca19c.crm2.dynamics.com`.
- Entorno: `Default-cab7ea42-4a21-4548-952f-fcde81f2bdd6`.
- Flujo: `50a4b698-8c77-4c02-a309-d6d7acae2256`, Facturacion con TRM.
- Solucion: `CloudFacturacionTRM`, publicador existente `Cr44b61` (`cr07a`).
- La programacion diaria de las 05:00 de Bogota, conversion TRM, impuestos,
  pagos y controles de periodo se conservan.

## Operacion

Ejecutar desde la raiz del repositorio, con la sesion Azure CLI autorizada para
Digital Tech. El SDK recibe siempre el tenant y la organizacion explicitos.
No se utiliza el perfil activo de Dataverse CLI, que puede ser de otro cliente.

```powershell
python -m pip install -r scripts/cloud_cost_centers/requirements.txt
python scripts/cloud_cost_centers/manage.py plan
python scripts/cloud_cost_centers/manage.py schema
python scripts/cloud_cost_centers/manage.py ui
python scripts/cloud_cost_centers/manage.py initialize
python scripts/cloud_cost_centers/validate_platform.py
python scripts/cloud_cost_centers/manage.py deploy
python scripts/cloud_cost_centers/manage.py retire_catalog_ui
python scripts/cloud_cost_centers/manage.py verify
python scripts/cloud_cost_centers/manage.py export
```

`plan` guarda respaldos y un diff limitado a los cambios del centro de costo y
comprobante. `deploy` exige las pruebas aprobadas y rechaza una definicion de
produccion modificada desde ese respaldo. No ejecuta el flujo de produccion.

La validacion crea flujos temporales con datos sinteticos y acciones de datos
de Power Automate. Prueba los cinco IDs, varios productos de un mismo cliente,
clientes diferentes, el valor nulo y una opcion invalida. El catalogo tiene un
centro distinto en las pruebas para comprobar que manda la fila de Productos
CLOUD. No contiene acciones
HTTP, conectores, correo ni creacion de facturas. Elimina los flujos de prueba
al terminar. La prueba valida expresiones nativas, no reemplaza la observacion
de la siguiente facturacion real programada.

Los respaldos de datos y resultados locales se guardan bajo
`tmp/cloud-cost-centers-20261127-products-by-client/`, excluido de Git. La solucion exportada esta
en `solutions/CloudFacturacionTRM/`. `FlowSource/Facturacion-con-TRM.redacted.json`
es la definicion activa con secretos redactados; no debe desplegarse literalmente
con los marcadores `[REDACTED]`. El script reutiliza en memoria la autenticacion
del flujo existente, sin guardarla en archivos.

## Reversion

Si se necesita volver al comportamiento anterior, restaurar la definicion del
respaldo `flow-before.redacted.json` combinandola con la autenticacion de Siigo
de la definicion viva (los secretos nunca se respaldan en texto plano). Verificar
el diff antes de guardar. Mantener el nuevo campo y las elecciones de productos
evita perder configuracion manual. No borrar ni volver a emitir facturas para
revertir la configuracion del flujo.

Los resultados de la configuracion y las pruebas se guardan en
`solutions/CloudFacturacionTRM/Validation/products-by-client-20261127.json`.
Las pruebas de expresiones no sustituyen la observacion de la primera factura
real de la siguiente ejecucion programada.
