# Esquema Dataverse requerido por Conciliacion Siigo

Esta especificacion acompana los cambios de conciliacion. No constituye una
autorizacion para modificar un entorno real. Antes de crear los atributos se
deben confirmar la URL exacta del entorno, la solucion no administrada y el
publicador cuyo prefijo es `cr07a`.

`CC-12` y `CC-17` identifican configuraciones de tipo documental (`CC` con
codigo `12` o `17`); no son los consecutivos del comprobante. El consecutivo es
un dato independiente y Siigo lo asigna automaticamente en estas
configuraciones.

## Movimiento bancario

Tabla logica: `cr07a_movimientobancario`.

| Nombre logico | Nombre visible sugerido | Tipo | Longitud / rango | Requerimiento |
| --- | --- | --- | --- | --- |
| `cr07a_siigoterceroclave` | Clave tercero Siigo | Texto | 100 | Opcional |
| `cr07a_siigoterceroidentificacion` | Identificacion tercero Siigo | Texto | 50 | Opcional |
| `cr07a_siigoterceronombre` | Nombre tercero Siigo | Texto | 250 | Opcional |
| `cr07a_siigotercerosucursal` | Sucursal tercero Siigo | Numero entero | Minimo 0 | Opcional |

Los cuatro atributos forman una unidad. La aplicacion solo considera un
tercero persistido cuando existen como minimo la clave y la identificacion; en
los comprobantes de salida valida nuevamente contra un tercero activo de Siigo
antes del envio. Deben mantenerse opcionales para no invalidar registros
historicos y habilitar auditoria si la politica del entorno lo permite.

## Cuentas de cobro

La tabla canonica para los gastos creados desde una salida bancaria es
`cr07a_gastodelaempresa`. `cr07a_cuentasdecobro` se conserva como origen
historico compatible, pero no es el destino de los nuevos documentos soporte
creados desde Conciliacion.

| Nombre logico | Nombre visible sugerido | Tipo | Longitud / precision | Requerimiento |
| --- | --- | --- | --- | --- |
| `cr07a_excelkey` | ExcelKey DIAN | Texto | 200 | Obligatorio para la idempotencia del flujo |
| `cr07a_cuentacontablecodigo` | Cuenta contable codigo | Texto | 50 | Obligatorio para el flujo |
| `cr07a_cuentacontablenombre` | Cuenta contable nombre | Texto | 250 | Obligatorio para el flujo |
| `cr07a_estadoautomatizacion` | Estado automatizacion | Texto | 100 | Obligatorio para el flujo |
| `cr07a_motivorevision` | Motivo revision | Texto de varias lineas (Memo) | 4000 | Obligatorio para el flujo |
| `cr07a_retencionesjson` | Detalle de retenciones | Texto de varias lineas (Memo) | 100000 | Obligatorio para el flujo |
| `cr07a_iva` | IVA | Decimal o moneda | Precision compatible con 2 decimales | Obligatorio para el flujo; crear si falta |
| `cr07a_siigodocumentid` | Siigo document id | Texto | 150 | Obligatorio para el flujo |
| `cr07a_siigodocumentname` | Siigo document name | Texto | 150 | Obligatorio para el flujo |
| `cr07a_siigopaymentid` | Siigo payment id | Texto | 150 | Obligatorio para el flujo |
| `cr07a_siigopaymentname` | Siigo payment name | Texto | 150 | Obligatorio para el flujo |
| `cr07a_siigorespuesta` | Respuesta documento Siigo | Texto de varias lineas (Memo) | 100000 | Obligatorio para el flujo |
| `cr07a_siigopaymentresponse` | Respuesta pago Siigo | Texto de varias lineas (Memo) | 100000 | Obligatorio para el flujo |

Las columnas se mantienen con nivel de obligatoriedad `None` en Dataverse para
no invalidar registros historicos. "Obligatorio para el flujo" significa que la
aplicacion verifica su existencia antes de guardar o enviar y falla cerrado si
falta alguna.

`cr07a_retencionesjson` conserva cada retencion con tipo, etiqueta, impuesto
Siigo, cuenta, base, tarifa y valor. `cr07a_iva` conserva el valor incluido en
el total y permite calcular RteIVA sobre su base correcta. Las dos respuestas
Siigo son independientes: una audita el documento soporte y la otra el
comprobante de pago.

### Clave alterna de idempotencia

`cr07a_gastodelaempresa` debe tener una clave alterna activa, de una sola
columna, sobre `cr07a_excelkey`. Si no existe una clave equivalente, el
provisioning crea:

| Nombre de esquema de la clave | Atributo | Estado requerido |
| --- | --- | --- |
| `cr07a_GastoEmpresaDianExcelKey` | `cr07a_excelkey` | `Active` |

Para cuentas de cobro, `cr07a_excelkey` recibe la clave externa estable del
movimiento bancario o, si esta no existe, `cashflow-record:{guid}`. El servicio
solo hace el upsert cuando encuentra una clave alterna activa sobre ese
atributo; sin ella bloquea el guardado para evitar duplicados. El script
`scripts/Provision-CashFlowImportDataverse.ps1` comprueba duplicados, crea o
reutiliza la clave, verifica que apunte exclusivamente a `cr07a_excelkey` y
espera hasta que su indice quede activo. Si ya existe una clave de otro nombre
con ese unico atributo, la reutiliza en vez de crear una duplicada.

## Secuencia segura de despliegue

1. Confirmar la URL del entorno, la solucion no administrada y su publicador.
2. Consultar metadatos y comprobar que no existan atributos incompatibles con
   estos nombres logicos.
3. Ejecutar el provisioning confirmado para crear solo las columnas faltantes;
   si una columna existente tiene tipo o longitud incompatible, detenerse sin
   reemplazarla.
4. Publicar y leer nuevamente los metadatos para verificar nombres logicos,
   tipos, longitudes y que la clave alterna de `cr07a_excelkey` este activa.
5. Ejecutar una prueba controlada sin envio a Siigo: guardar y releer un gasto
   de cuenta de cobro con IVA y dos retenciones; comprobar que
   `total = pago + retenciones`.
6. Sincronizar y revisar el catalogo `cr07a_cuentacontablesiigo`. Antes del
   piloto deben estar aprobadas y activas `22050501`, las cuentas bancarias,
   `42958101` y las cuentas `236*` que realmente se utilizaran. El preflight
   falla cerrado si alguna no aparece.
7. Verificar que los IDs de impuestos seleccionados existan y esten activos en
   el catalogo actual de `/v1/taxes`, con el tipo y la tarifa esperados.
8. Autorizar por separado el primer envio financiero real.
