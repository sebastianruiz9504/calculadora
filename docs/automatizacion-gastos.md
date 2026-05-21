# Automatizacion de gastos, pagos y comprobantes

Ultima revision: 2026-05-21

Este documento deja el mapa base para automatizar gastos, cuentas de cobro,
flujo de caja, pagos de clientes, pagos a proveedores y comprobantes contables.
Dataverse queda como centro operativo y Siigo como sistema contable oficial.

## Decisiones base

- Mantener temporalmente `categoria` y agregar `cuenta contable`. No son el
  mismo dato: la categoria soporta contabilidad interna y distribucion
  Cloud/Copiers; la cuenta contable soporta el asiento en Siigo.
- No subir a Siigo movimientos con confianza media o baja. Esos deben quedar en
  bandeja de supervision.
- Usar reglas versionadas en Dataverse para categoria, vertical y cuenta
  contable. Las correcciones humanas deben poder guardarse como nueva regla.
- Usar tolerancia de redondeo de COP 1 para conciliacion contable exacta y una
  tolerancia operacional de COP 5.000 para aplicar pagos de clientes cuando la
  diferencia corresponda a retenciones o ajuste al peso.

## Fuentes actuales

### Dataverse

Tabla de gastos:

- Tabla logica: `cr07a_gastodelaempresa`
- Entity set: `cr07a_gastodelaempresas`
- Id: `cr07a_gastodelaempresaid`

Campos relevantes observados:

| Campo | Tipo | Uso |
| --- | --- | --- |
| `cr07a_categoria` | Picklist | Categoria interna actual |
| `cr07a_cloud` | Decimal | Valor distribuido a Cloud |
| `cr07a_copiers` | Decimal | Valor distribuido a Copiers |
| `cr07a_fechaemision` | DateTime | Fecha de emision del gasto |
| `cr07a_fechagasto` | DateTime | Fecha gasto/importacion |
| `cr07a_fechadepago` | DateTime | Fecha de pago |
| `cr07a_total` | Decimal | Total gasto |
| `cr07a_iva` | Decimal | IVA |
| `cr07a_totalantesdeiva` | Decimal | Base antes de IVA |
| `cr07a_retefuente` | Decimal | Retencion fuente |
| `cr07a_reteica` | Decimal | ReteICA |
| `cr07a_nombreemisor` | String | Proveedor/emisor |
| `cr07a_nitemisor` | String | NIT proveedor/emisor |
| `cr07a_nombrereceptor` | String | Receptor |
| `cr07a_nitreceptor` | String | NIT receptor |
| `cr07a_valorpago` | String | Valor de pago actual |

Campos propuestos para agregar:

| Campo propuesto | Tipo | Uso |
| --- | --- | --- |
| `cr07a_cuentacontablecodigo` | String | Codigo de cuenta Siigo a usar |
| `cr07a_cuentacontablenombre` | String | Nombre legible de cuenta |
| `cr07a_siigodocumentid` | String | Id del documento creado/encontrado en Siigo |
| `cr07a_siigodocumentname` | String | Nombre Siigo, por ejemplo `FC-1-123` |
| `cr07a_fuenteautomatizacion` | Choice/String | DIAN, CuentaCobro, FlujoCaja, Manual |
| `cr07a_estadoautomatizacion` | Choice/String | Pendiente, Clasificado, ListoSiigo, EnviadoSiigo, Error, Conciliado |
| `cr07a_reglacategoriaid` | Lookup/String | Regla que asigno categoria |
| `cr07a_reglaverticalid` | Lookup/String | Regla que asigno Cloud/Copiers |
| `cr07a_reglacontableid` | Lookup/String | Regla que asigno cuenta contable |
| `cr07a_confianzaautomatizacion` | Decimal | 0 a 100 |
| `cr07a_motivorevision` | Multiline text | Motivo por el que queda en bandeja |

Estado de implementacion:

- Campos creados y publicados en Dataverse el 2026-05-20.
- `cr07a_confianzaautomatizacion` quedo como Decimal.
- `cr07a_motivorevision` quedo como Memo.
- Los demas campos quedaron como texto para facilitar iteracion inicial.

Tabla de facturacion:

- Tabla logica: `cr07a_facturacion`
- Entity set: `cr07a_facturacions`
- Campos usados para reglas: `cr07a_fechadeemision`, `cr07a_vertical`,
  `cr07a_totalfactura`, `cr07a_name`, `cr07a_nitempresa`.

## Categorias internas observadas

Opciones configuradas en `cr07a_categoria`:

| Valor | Categoria |
| ---: | --- |
| 645250016 | Arriendo Oficina |
| 645250013 | Bodegaje |
| 645250014 | Equipamiento |
| 645250006 | Gastos internos |
| 645250007 | Impuestos |
| 645250010 | Licenciamiento |
| 645250008 | Maquinas |
| 645250005 | Marketing |
| 645250002 | Personal Administrativo |
| 645250000 | Personal Cloud |
| 645250001 | Personal Copiers |
| 645250012 | Primas/Cesantias |
| 645250011 | Recurrente |
| 645250015 | Servicio Tecnico |
| 645250009 | Suministros |
| 645250003 | Transporte Equipos |
| 645250004 | Viaticos |

Uso historico actual en gastos:

| Categoria | Registros | Total | IVA | Cloud | Copiers |
| --- | ---: | ---: | ---: | ---: | ---: |
| Licenciamiento | 99 | 2.183.768.581,87 | 2.196.833,92 | 2.164.173.353,65 | 18.966.000,00 |
| Personal Cloud | 173 | 533.831.695,90 | 198.487,86 | 507.894.550,56 | 24.908.307,53 |
| Personal Administrativo | 100 | 302.270.008,67 | 47.030,59 | 204.728.798,24 | 87.904.140,44 |
| Maquinas | 72 | 253.985.055,84 | 34.443.195,04 | 15.860.270,30 | 235.398.626,46 |
| Suministros | 283 | 227.335.149,09 | 31.710.512,26 | 18.217.102,28 | 208.076.560,43 |
| Equipamiento | 82 | 190.496.590,61 | 15.172.911,15 | 166.674.173,07 | 23.577.260,00 |
| Personal Copiers | 53 | 182.099.291,39 | 108.604,00 | 713.800,00 | 178.037.473,39 |
| Impuestos | 37 | 124.346.900,00 | 0,00 | 63.740.961,00 | 52.501.739,00 |
| Arriendo Oficina | 25 | 67.823.583,00 | 6.999.543,00 | 39.327.872,00 | 26.825.721,00 |
| Recurrente | 273 | 43.458.353,67 | 1.908.241,85 | 35.900.168,06 | 7.279.839,40 |
| Transporte Equipos | 178 | 42.233.461,09 | 1.985.093,36 | 7.971.816,05 | 34.084.205,24 |
| Primas/Cesantias | 13 | 35.821.859,00 | 0,00 | 25.291.572,50 | 5.993.130,50 |
| Marketing | 78 | 31.027.488,77 | 1.685.458,87 | 30.412.044,77 | 560.444,00 |
| Gastos internos | 201 | 29.331.658,86 | 1.618.731,83 | 22.487.427,17 | 6.722.917,99 |
| Sin categoria | 10 | 274.545.756,00 | 278.551,58 | 37.730,00 | 49.123,20 |
| Bodegaje | 19 | 8.396.575,00 | 53.734,34 | 102.400,00 | 8.194.175,00 |
| Viaticos | 14 | 4.229.987,80 | 980.304,41 | 3.320.171,81 | 909.816,00 |
| Servicio Tecnico | 5 | 1.878.511,00 | 71.611,00 | 0,00 | 2.039.216,00 |

## Reglas iniciales Cloud/Copiers

Analisis de facturacion:

- 2025-01 a 2026-04: Cloud 82,22%, Copiers 17,78%.
- 2026-01 a 2026-04: Cloud 86,39%, Copiers 13,61%.
- Regla recomendada para gastos compartidos: usar proporcion mensual de
  facturacion del mismo mes. Si el mes no tiene suficiente data, usar trailing
  12 meses. Fallback actual: Cloud 86,39%, Copiers 13,61%.

Reglas propuestas por categoria:

| Categoria | Regla Cloud/Copiers |
| --- | --- |
| Personal Cloud | 100% Cloud |
| Licenciamiento | 100% Cloud, salvo proveedor/regla especifica |
| Marketing | 100% Cloud por historico actual, revisar excepciones |
| Personal Copiers | 100% Copiers |
| Maquinas | 100% Copiers |
| Suministros | 100% Copiers, salvo proveedor/regla especifica |
| Transporte Equipos | 100% Copiers |
| Bodegaje | 100% Copiers |
| Servicio Tecnico | 100% Copiers |
| Personal Administrativo | Distribucion por mix mensual de facturacion |
| Arriendo Oficina | Distribucion por mix mensual de facturacion |
| Impuestos | Distribucion por mix mensual de facturacion |
| Primas/Cesantias | Distribucion por mix mensual de facturacion |
| Recurrente | Regla por proveedor; si no existe, mix mensual |
| Gastos internos | Regla por proveedor/texto; si no existe, mix mensual |
| Viaticos | Regla por responsable/proyecto; si no existe, mix mensual |
| Equipamiento | Regla por proveedor/producto; si no existe, revision |

Reglas fijas por proveedor:

| Proveedor/NIT | Regla |
| --- | --- |
| DIAN / `800.197.268` | Siempre categoria `Impuestos` |

## Siigo: catalogos observados

### Impuestos y retenciones activos

Fuente Siigo: `/v1/taxes`.

| Id | Nombre | Tipo | Porcentaje |
| ---: | --- | --- | ---: |
| 4021 | IVA 19% | IVA | 19 |
| 4022 | IVA 5% | IVA | 5 |
| 13852 | IVA 0% | IVA | 0 |
| 4027 | Retefuente 2.5% | Retefuente | 2,5 |
| 4038 | Retefuente 3.5% | Retefuente | 3,5 |
| 4026 | Retefuente 4% | Retefuente | 4 |
| 4025 | Retefuente 6% | Retefuente | 6 |
| 4039 | Retefuente 7% | Retefuente | 7 |
| 4024 | Retefuente 10% | Retefuente | 10 |
| 4023 | Retefuente 11% | Retefuente | 11 |
| 4041 | Retefuente 1% | Retefuente | 1 |
| 4040 | Retefuente 2% | Retefuente | 2 |
| 4030 | ReteICA 9.66 | ReteICA | 9,66 |
| 4028 | ReteICA 11.04 | ReteICA | 11,04 |
| 4033 | ReteICA 6.9 | ReteICA | 6,9 |
| 4034 | ReteICA 4.14 | ReteICA | 4,14 |
| 4035 | ReteIVA 15% | ReteIVA | 15 |

### Tipos de documento activos

Fuente Siigo: `/v1/document-types`.

| Tipo | Id | Codigo | Nombre |
| --- | ---: | --- | --- |
| FV | 7481 | 1 | Factura electronica de venta |
| FV | 31072 | 2 | Factura electronica de venta |
| FC | 7486 | 1 | Compra |
| FC | 31114 | 2 | Compras enero a marzo |
| RC | 7480 | 1 | Recibo |
| RC | 31113 | 2 | Recibo de caja enero a marzo |
| RP | 7485 | 1 | Recibo de pago / Egreso |
| RP | 31115 | 2 | Recibos de pago enero a marzo |
| DS | 25573 | 1 | Documento soporte por compras a sujetos no obligados a facturar |
| CC | 31112 | 11 | Conciliacion Bancaria |
| CC | 31254 | 12 | Comprobante de egreso |
| CC | 31321 | 17 | Comprobante de ingreso |
| CC | 7509 | 8 | Nomina |
| CC | 7502 | 1 | Ajustes contables |

Nota: el flujo automatico `Facturacion con TRM` debe emitir facturas de venta con el documento FV codigo 2, id 31072.

### Medios de pago / bancos activos

Fuente Siigo: `/v1/payment-types`.

| Documento | Id | Nombre | Tipo | Vencimiento |
| --- | ---: | --- | --- | --- |
| FV | 8854 | Clientes Nacionales | Cartera | Si |
| FV | 8855 | Clientes Extranjero | Cartera | Si |
| FV | 13566 | Bancolombia Cloud Ventas | Cartera | No |
| FV | 13568 | Bancolombia Copiers Ventas | Cartera | No |
| FC | 1726 | Credito proveedores | Proveedor | Si |
| FC | 13567 | Bancolombia Cloud Compras | Proveedor | No |
| FC | 13569 | Bancolombia Copiers Compras | Proveedor | No |
| FC | 13768 | Cajas menores | Proveedor | No |
| FC | 13571 | Caja Manejo | Proveedor | No |
| RC | 13566 | Bancolombia Cloud Ventas | Cartera | No |
| RC | 13568 | Bancolombia Copiers Ventas | Cartera | No |

### Cuentas contables observadas en Siigo

No se encontro en la documentacion oficial un endpoint de plan de cuentas
general equivalente a `/accounts`. Las APIs de comprobantes usan
`items.account.code`, y el codigo debe existir y estar activo en Siigo. Por eso
el mapa inicial se debe construir minando comprobantes historicos y luego
validarlo con contabilidad.

Cuentas mas usadas observadas en comprobantes enero-abril 2026:

| Cuenta | Movimiento usual | Uso observado |
| --- | --- | --- |
| 13050501 | Credit/Debit | Clientes nacionales |
| 22050501 | Debit/Credit | Proveedores nacionales |
| 11100504 | Debit/Credit | Bancolombia Cloud 8100 |
| 11100505 | Debit/Credit | Bancolombia Copiers 7316 |
| 11100502 | Debit/Credit | Pagos en linea |
| 11051001 | Debit | Cajas menores |
| 1105050102 | Debit | Caja manejo para pagos |
| 13551513 | Debit | Retefuente 3.5% |
| 13551503 | Debit | Retefuente 4% |
| 13551501 | Debit | Retefuente 2.5% |
| 13551805 | Debit | ReteICA 9.66 |
| 13551801 | Debit | ReteICA 11.04 |
| 13551701 | Debit | ReteIVA 15% |
| 42958101 | Debit/Credit | Ajuste al peso |
| 53050502 | Debit | Gravamen 4 x 1000 |
| 53050501 | Debit | Gastos bancarios |
| 53054001 | Debit | IVA bancario |
| 51201001 | Debit | Arrendamientos - construcciones y edificaciones |
| 51353001 | Debit | Servicios publicos - energia electrica |
| 51352501 | Debit | Servicios publicos - acueducto |
| 51353501 | Debit | Servicios publicos - telefono |
| 61355401 | Debit | Servicios de nube IaaS / Cloud |
| 61355402 | Debit | Servicios de nube |
| 613599 | Debit | Costo de ventas suministros |
| 613510 | Debit | Mercancias no fabricadas por la empresa |

## Modelo de reglas en Dataverse

Tablas recomendadas:

Estado de implementacion:

| Tabla logica | Entity set | Estado |
| --- | --- | --- |
| `cr07a_cuentacontablesiigo` | `cr07a_cuentacontablesiigos` | Creada, sembrada con 150 cuentas historicas de Siigo y con sincronizacion mensual automatica |
| `cr07a_reglaclasificaciongasto` | `cr07a_reglaclasificaciongastos` | Creada |
| `cr07a_regladistribucionvertical` | `cr07a_regladistribucionverticals` | Creada |
| `cr07a_reglacuentacontable` | `cr07a_reglacuentacontables` | Creada y conectada al motor automatico de asignacion de cuentas |
| `cr07a_movimientobancario` | `cr07a_movimientobancarios` | Creada |
| `cr07a_excepcionautomatizacion` | `cr07a_excepcionautomatizacions` | Creada |

### `cr07a_cuentacontablesiigo`

Catalogo para que la app busque por nombre y guarde internamente el codigo
contable requerido por Siigo.

| Campo | Uso |
| --- | --- |
| `cr07a_name` | Nombre primario: codigo + nombre |
| `cr07a_codigo` | Codigo contable Siigo |
| `cr07a_nombre` | Nombre de la cuenta |
| `cr07a_tipo` | Banco/Caja, Cliente, Proveedor/CxP, Retencion, IVA, Ajuste, Gasto/Costo, etc. |
| `cr07a_activo` | Cuenta disponible para reglas/busquedas |
| `cr07a_origen` | Siigo historico, manual, validado |
| `cr07a_ultimaactualizacion` | Fecha de ultima carga |

Sincronizacion automatica:

- Servicio: `ISiigoAccountCatalogSyncService`.
- Job: `MonthlySiigoAccountCatalogSyncHostedService`.
- Configuracion: `SiigoAccountCatalogSync`.
- Frecuencia actual: dia 1 de cada mes a las 06:00, hora Colombia.
- Ventana actual: ultimos 6 meses de documentos Siigo.
- Fuentes Siigo minadas: `/v1/journals`, `/v1/payment-receipts`,
  `/v1/vouchers` y `/v1/purchases`.
- Endpoint manual: `POST /automation/siigo-account-catalog/sync`.
- Endpoint manual con periodo: `POST /automation/siigo-account-catalog/sync?startDate=2026-01-01&endDate=2026-04-30`.

Regla operativa: si una cuenta ya existe con origen `manual` o `validado`, la
sincronizacion no pisa nombre ni tipo. Solo mantiene la cuenta activa y marca
la ultima actualizacion. Si viene de Siigo historico/automatico, si puede
actualizar nombre/tipo con lo observado en Siigo.

### `cr07a_reglaclasificaciongasto`

| Campo | Uso |
| --- | --- |
| `cr07a_name` | Nombre de regla |
| `cr07a_prioridad` | Orden de aplicacion |
| `cr07a_nitemisor` | Match por proveedor |
| `cr07a_textocontiene` | Match por concepto/descripcion |
| `cr07a_categoria` | Categoria a asignar |
| `cr07a_confianza` | Confianza base |
| `cr07a_activa` | Activa/inactiva |

Regla activa creada:

| Regla | NIT | Categoria | Confianza |
| --- | --- | --- | ---: |
| DIAN => Impuestos | `800.197.268` | Impuestos (`645250007`) | 100 |

### `cr07a_regladistribucionvertical`

| Campo | Uso |
| --- | --- |
| `cr07a_categoria` | Categoria afectada |
| `cr07a_nitemisor` | Proveedor opcional |
| `cr07a_cloudporcentaje` | Porcentaje Cloud |
| `cr07a_copiersporcentaje` | Porcentaje Copiers |
| `cr07a_usarmixfacturacion` | Si debe calcularse por facturacion del mes |
| `cr07a_requiereaprobacion` | Si no se aplica automatico |

### `cr07a_reglacuentacontable`

| Campo | Uso |
| --- | --- |
| `cr07a_prioridad` | Orden de aplicacion; menor numero gana si dos reglas tienen la misma especificidad |
| `cr07a_categoriavalor` | Valor numerico de categoria interna |
| `cr07a_categorianombre` | Nombre de categoria interna |
| `cr07a_nitemisor` | Proveedor opcional |
| `cr07a_textocontiene` | Texto opcional que debe existir en proveedor/factura/descripcion |
| `cr07a_tipomovimiento` | Compra, documento soporte, comprobante, pago cliente, pago proveedor |
| `cr07a_cuentadebitocodigo` | Codigo cuenta debito |
| `cr07a_cuentadebitonombre` | Nombre cuenta debito |
| `cr07a_cuentacreditocodigo` | Codigo cuenta credito |
| `cr07a_cuentacreditonombre` | Nombre cuenta credito |
| `cr07a_impuestoid` | Id de impuesto/retencion Siigo cuando aplique |
| `cr07a_documenttypeid` | Tipo de documento Siigo por defecto |
| `cr07a_paymenttypeid` | Medio de pago Siigo por defecto |
| `cr07a_activa` | Activa/inactiva |

Motor automatico implementado:

- Servicio: `IExpenseAccountingRuleService`.
- Job: `WeeklyExpenseAccountingRulesHostedService`.
- Configuracion: `ExpenseAccountingRules`.
- Frecuencia actual: lunes a las 08:00, hora Colombia.
- Ventana actual: ultimos 45 dias.
- Endpoint manual: `POST /automation/expense-accounting-rules/apply`.
- Endpoint manual con periodo: `POST /automation/expense-accounting-rules/apply?startDate=2026-05-01&endDate=2026-05-31&movementType=Compra`.
- Por defecto no sobrescribe gastos que ya tengan `cr07a_cuentacontablecodigo`.
  Para forzar recalculo: agregar `overwrite=true`.

Orden de seleccion de regla:

1. La regla debe estar activa y tener `cr07a_cuentadebitocodigo`.
2. Si tiene `cr07a_tipomovimiento`, debe coincidir con el movimiento ejecutado.
3. Si tiene `cr07a_nitemisor`, debe coincidir contra NIT emisor o receptor.
4. Si tiene categoria, debe coincidir por valor numerico o por nombre.
5. Si tiene `cr07a_textocontiene`, ese texto debe aparecer en el gasto.
6. Gana la regla con mas especificidad; en empate gana menor `cr07a_prioridad`.
7. La cuenta debito debe existir y estar activa en `cr07a_cuentacontablesiigo`.

Campos que actualiza en `cr07a_gastodelaempresa`:

| Campo | Valor |
| --- | --- |
| `cr07a_cuentacontablecodigo` | Cuenta debito de la regla |
| `cr07a_cuentacontablenombre` | Nombre de cuenta de la regla/catalogo |
| `cr07a_reglacontableid` | Id de la regla aplicada |
| `cr07a_estadoautomatizacion` | `Clasificado` o `PendienteRevision` |
| `cr07a_confianzaautomatizacion` | Confianza calculada 0-100 |
| `cr07a_motivorevision` | Explicacion de regla aplicada o causa de revision |

Para que el motor asigne cuentas, contabilidad debe mantener reglas activas en
`cr07a_reglacuentacontable`. Si no existe regla para un gasto, el sistema no
inventa una cuenta: deja el gasto en `PendienteRevision`. Si no hay ninguna
regla activa en la tabla, el motor no modifica gastos.

Primer lote de reglas contables activas creado el 2026-05-20:

- Criterio usado: proveedor + categoria + cuenta observada en compras Siigo.
- Evidencia minima: 2 coincidencias exactas por NIT y total.
- Resultado: 20 reglas proveedor/categoria activas.
- Validacion posterior sin tocar gastos reales, periodo 2026-04-01 a
  2026-05-20: 153 gastos revisados, 22 gastos asignables, 131 sin regla,
  0 reglas invalidas.

Ejemplos del primer lote:

| Proveedor | Categoria | Cuenta |
| --- | --- | --- |
| COLOMBIA MOVIL S.A. ESP | Recurrente | `51353501` - Servicios publicos - Telefono |
| GRUPO EMPRESARIAL OIKOS S.A.S. | Arriendo Oficina | `51201001` - Arrendamientos |
| HUBSPOT LATIN AMERICA S.A.S | Recurrente | `61355402` - Servicios de nube |
| XCB de Colombia Limited Suc. Colombiana | Licenciamiento | `61355402` - Servicios de nube |
| Papelera Fk Sas | Suministros | `51953001` - Utiles papeleria y fotocopias |

Segundo lote de reglas contables activas creado el 2026-05-20:

- Criterio usado: compras Siigo con coincidencia por NIT + total, y revision
  adicional de journals/comprobantes para detectar posibles reglas no DIAN.
- Resultado: 2 reglas nuevas activas.
- Aplicacion sobre 2026-04-01 a 2026-05-20: 153 gastos revisados, 22 ya
  estaban asignados, 8 gastos nuevos asignados, 123 sin regla, 0 reglas
  invalidas.

Reglas creadas:

| Proveedor | Categoria | Cuenta |
| --- | --- | --- |
| ALHUM LIMITADA | Suministros | `613510` - Mercancias no fabricadas por la empresa no inventa |
| COLOMBIA MOVIL S.A. ESP | Fallback por proveedor sin categoria | `51353501` - Servicios publicos - Telefono |

Regla operativa del segundo lote: los journals/comprobantes solo se usaron
como evidencia para diagnostico. No se crearon reglas desde una sola
coincidencia porque los gastos bancarios, impuestos y movimientos de flujo de
caja pueden compartir NIT/valor pero requerir cuentas distintas.

Tercer y cuarto lote de reglas contables activas creado el 2026-05-20:

- Criterio usado: categorias con cuenta dominante clara y reglas por proveedor
  cuando el NIT/categoria identifica un gasto contable especifico.
- Se agrego al catalogo `cr07a_cuentacontablesiigo` la cuenta `51303001` -
  Seguros - Terremoto, observada en Siigo y faltante en Dataverse.
- Aplicacion acumulada sobre 2026-04-01 a 2026-05-20: 153 gastos revisados,
  85 con cuenta contable asignada, 68 sin regla, 0 reglas invalidas.

Reglas principales creadas:

| Regla | Cuenta |
| --- | --- |
| Categoria Maquinas | `613510` - Mercancias no fabricadas por la empresa no inventa |
| Categoria Arriendo Oficina | `51201001` - Arrendamientos - Construcciones y edificaciones |
| Categoria Viaticos | `510521` - Viaticos |
| ENEL / Recurrente | `51353001` - Servicios publicos - Energia electrica |
| ACUEDUCTO / Recurrente | `51352501` - Servicios publicos - Acueducto y alcantarillado |
| COORDINADORA y transportadoras especificas / Transporte Equipos | `529501` - Gastos de transporte y fletes |
| BR GLOBAL, OPENAI, TRIADA, CANVA, INTL MICROSOFT y XCB | `61355402` - Servicios de nube |
| D1 / Recurrente | `51952501` - Elementos de aseo y cafeteria |
| COMPAÑIA MUNDIAL DE SEGUROS / Licenciamiento | `51303001` - Seguros - Terremoto |

Pendientes despues del lote:

- Personal Cloud, Personal Administrativo y Personal Copiers, especialmente
  registros de `DIGITAL TECH COPIERS S A S`: requieren plantilla contable
  multi-linea de nomina/aportes, no una sola cuenta debito.
- Pagos `MI PLANILLA`/Compensar: requieren desglose por EPS, pension, ARL y
  caja de compensacion.
- Impuestos DIAN y Secretaria de Hacienda: requieren identificar el tipo de
  impuesto antes de asignar cuenta.
- Movimientos internos de `DIGITAL TECH COPIERS S A S` en recurrente,
  bodegaje, licenciamiento, equipamiento y transporte: requieren regla por
  fuente/flujo de caja o aprobacion manual.
- Gastos unitarios ambiguos sin evidencia suficiente: marketing, suministros
  mixtos, Colpatria, SIIGO, restaurantes/personas naturales y gastos bancarios
  sin categoria.

### Plantillas contables multi-linea

Estado de implementacion:

- Tablas creadas y publicadas en Dataverse el 2026-05-20.
- Motor implementado para proponer/generar lineas contables desde plantillas.
- No envia documentos a Siigo todavia; deja lineas en Dataverse para revision.
- No se sembraron plantillas activas sin validacion contable de banco/tercero,
  para evitar asientos incompletos.
- Script reutilizable: `scripts/Provision-ExpenseAccountingTemplatesDataverse.ps1`.

| Tabla logica | Entity set | Uso |
| --- | --- | --- |
| `cr07a_plantillacontablegasto` | `cr07a_plantillacontablegastos` | Cabecera de regla multi-linea |
| `cr07a_lineaplantillacontablegasto` | `cr07a_lineaplantillacontablegastos` | Lineas de la plantilla |
| `cr07a_lineacontablegasto` | `cr07a_lineacontablegastos` | Lineas generadas por gasto |

Campos principales de `cr07a_plantillacontablegasto`:

| Campo | Uso |
| --- | --- |
| `cr07a_prioridad` | Orden; menor numero gana en empate |
| `cr07a_categoriavalor` / `cr07a_categorianombre` | Match por categoria |
| `cr07a_nitemisor` | Match por proveedor |
| `cr07a_textocontiene` | Match por texto en proveedor/factura/descripcion |
| `cr07a_tipomovimiento` | Compra, documento soporte, comprobante, pago cliente, pago proveedor |
| `cr07a_activa` | Activa/inactiva |
| `cr07a_requiereaprobacion` | Si genera lineas pero queda en revision |

Campos principales de `cr07a_lineaplantillacontablegasto`:

| Campo | Uso |
| --- | --- |
| `cr07a_plantillaid` | Id de la plantilla cabecera |
| `cr07a_orden` | Orden de la linea |
| `cr07a_lado` | `Debito` o `Credito` |
| `cr07a_cuentacodigo` / `cr07a_cuentanombre` | Cuenta Siigo a usar |
| `cr07a_formula` | Base de calculo |
| `cr07a_porcentaje` | Porcentaje cuando la formula lo requiere |
| `cr07a_valorconstante` | Valor fijo cuando la formula sea `Constante` |
| `cr07a_activa` | Activa/inactiva |

Formulas soportadas:

| Formula | Valor calculado |
| --- | --- |
| `Total` | `cr07a_total` |
| `Base` / `Subtotal` | `cr07a_totalantesdeiva` |
| `Iva` | `cr07a_iva` |
| `ReteFuente` | `cr07a_retefuente` |
| `ReteIca` | `cr07a_reteica` |
| `ValorPago` / `Pago` | `cr07a_valorpago` |
| `Constante` | `cr07a_valorconstante` |
| `TotalPorcentaje` | total por `cr07a_porcentaje` |
| `BasePorcentaje` | base por `cr07a_porcentaje` |
| `IvaPorcentaje` | IVA por `cr07a_porcentaje` |

Motor automatico implementado:

- Servicio: `IExpenseAccountingTemplateService`.
- Job: `WeeklyExpenseAccountingTemplateHostedService`.
- Configuracion: `ExpenseAccountingTemplates`.
- Frecuencia actual: lunes a las 08:20, hora Colombia.
- Ventana actual: ultimos 45 dias.
- Endpoint manual de prueba: `POST /automation/expense-accounting-templates/apply?startDate=2026-05-01&endDate=2026-05-31&dryRun=true`.
- Endpoint manual real: `POST /automation/expense-accounting-templates/apply?startDate=2026-05-01&endDate=2026-05-31`.
- Por defecto no procesa gastos que ya tengan `cr07a_cuentacontablecodigo` ni
  gastos que ya tengan lineas generadas. Para reprocesar: `overwrite=true`.

Regla operativa: las plantillas deben usarse para casos que no se resuelven con
una sola cuenta debito, por ejemplo nomina/aportes, impuestos, comprobantes
bancarios y movimientos de flujo de caja. Si las lineas no cuadran debito vs
credito con tolerancia de COP 1, el gasto queda en `PendienteRevision`.

Plantillas piloto creadas el 2026-05-20:

| Plantilla | Match | Lineas |
| --- | --- | --- |
| TPL Servicios publicos energia - ENEL | NIT `860063875` + texto `Enel` | Debito `51353001` Base, debito `24080601` IVA |
| TPL Servicios publicos acueducto | Texto `Acueducto` | Debito `51352501` Base, debito `24080601` IVA |
| TPL Servicios telefonia - Colombia Movil | NIT `830114921` + texto `Colombia Movil` | Debito `51353501` Base, debito `24080601` IVA |

Todas quedan con `cr07a_requiereaprobacion = true`; por eso el resultado queda
en `PendienteRevision`, aunque cuadre, hasta confirmar banco/tercero antes de
activar envio a Siigo.

Regla de bancos: no se debe asumir banco en la plantilla del gasto. Hay dos
flujos de caja y dos bancos:

| Flujo de caja | Cuenta banco Siigo |
| --- | --- |
| Cloud | `11100504` - Bancolombia Cloud 8100 |
| Copiers | `11100505` - Bancolombia Copiers 7316 |

La linea credito del banco se debe crear solo despues de cruzar el gasto contra
el flujo de caja correspondiente. Mientras no exista ese cruce, la plantilla
solo propone gasto/IVA y queda pendiente de banco.

Prueba en seco ejecutada el 2026-05-20:

| Periodo | Dry run | Revisados | Con plantilla | Lineas sugeridas | Invalidas | Actualizados |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 2026-01-01 a 2026-05-20 | Si | 408 | 33 | 60 | 0 | 0 |

Resultado observado: las 33 coincidencias generaron gasto/IVA correctamente,
pero quedaron sin credito de banco a proposito. No se modifico ningun gasto ni
se envio nada a Siigo.

### `cr07a_movimientobancario`

| Campo | Uso |
| --- | --- |
| `cr07a_fecha` | Fecha bancaria |
| `cr07a_banco` | Banco/cuenta |
| `cr07a_descripcion` | Texto original |
| `cr07a_valorentrada` | Entrada |
| `cr07a_valorsalida` | Salida |
| `cr07a_referencia` | Referencia |
| `cr07a_tipomovimiento` | Pago cliente, pago proveedor, gasto bancario, transferencia, otro |
| `cr07a_estado` | Pendiente, sugerido, aprobado, enviado Siigo, error |
| `cr07a_origenflujo` | Cloud o Copiers |
| `cr07a_bancocuentacodigo` / `cr07a_bancocuentanombre` | Banco Siigo que corresponde al flujo |
| `cr07a_destinatario` | Destinatario del Excel |
| `cr07a_bancodestino` | Banco destino del Excel |
| `cr07a_tipodocumento` | Tipo documento del Excel |
| `cr07a_observaciones` | Observaciones del Excel |
| `cr07a_siigoestado` | Estado Siigo digitado en el Excel |
| `cr07a_claveexterna` | Llave idempotente `cashflow:{flujo}:{tabla}:{fila}` |
| `cr07a_hashorigen` | Hash de la fila para no actualizar si no cambio |
| `cr07a_archivoorigen` / `cr07a_tablaorigen` / `cr07a_filaorigen` | Trazabilidad al Excel de SharePoint |

### Importador flujo de caja SharePoint

Fuente:

- Sitio/grupo: Financiero.
- Archivo: `Shared Documents/General/Pagos de facturas copiers y cloud.xlsx`.
- Hoja/table Cloud: `Flujo de caja CLOUD` / `Flujodecajacloud`.
- Hoja/table Copiers: `Flujo de caja COPIERS` / `Flujodecajacopiers`.
- Columnas leidas: `Fecha`, `Tipo de movimiento`, `Categoria`, `Entrada`,
  `Salida`, `Descripcion`, `Destinatario`, `Banco destino`,
  `Tipo Documento`, `Observaciones`, `Siigo`.

Reglas:

- La columna `Descripcion` es la llave operativa para cruzar entradas/salidas
  con facturas, documentos soporte o comprobantes.
- Cloud se registra con banco `11100504` - Bancolombia Cloud 8100.
- Copiers se registra con banco `11100505` - Bancolombia Copiers 7316.
- `TRASLADO` no va a Siigo. Se guarda en tabla independiente
  `cr07a_trasladointernoflujocaja`.
- Por defecto no se importan filas futuras; quedan fuera hasta que llegue la
  fecha. Esto evita convertir planeacion de caja en movimiento real.
- La automatizacion es idempotente por fila del Excel y usa `cr07a_hashorigen`
  para dejar intacto lo que no cambio.

Automatizacion implementada:

- Servicio: `ICashFlowImportService`.
- Job: `WeeklyCashFlowImportHostedService`.
- Configuracion: `CashFlowImport`.
- Frecuencia actual: lunes a las 07:30, hora Colombia.
- Endpoint manual seco: `POST /automation/cash-flow/import?dryRun=true`.
- Endpoint manual real: `POST /automation/cash-flow/import?dryRun=false`.

Tabla de traslados internos:

| Campo | Uso |
| --- | --- |
| `cr07a_fecha` | Fecha del movimiento |
| `cr07a_origenflujo` | Flujo donde aparece la fila |
| `cr07a_flujodesde` / `cr07a_flujohacia` | Flujo origen/destino inferido |
| `cr07a_entrada` / `cr07a_salida` / `cr07a_valor` | Valores del traslado |
| `cr07a_descripcion` | Texto original |
| `cr07a_destinatario` / `cr07a_bancodestino` / `cr07a_tipodocumento` | Datos auxiliares |
| `cr07a_estado` | `InternoNoSiigo` |
| `cr07a_claveexterna` / `cr07a_hashorigen` | Idempotencia |
| `cr07a_archivoorigen` / `cr07a_tablaorigen` / `cr07a_filaorigen` | Trazabilidad |

Prueba seca ejecutada el 2026-05-20 leyendo SharePoint y consultando Dataverse:

| Resultado | Valor |
| --- | ---: |
| Filas validas hasta el 2026-05-20 | 3.714 |
| Movimientos bancarios | 3.084 |
| Traslados internos | 630 |
| Filas futuras omitidas | 6 |
| Registros que crearia en Dataverse | 3.714 |
| Registros que actualizaria | 0 |
| Registros sin cambios | 0 |

Resumen por flujo:

| Flujo | Filas | Movimientos | Traslados | Entradas | Salidas | Valor traslados |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Cloud | 2.840 | 2.461 | 379 | 7.660.487.557,04 | 7.626.699.566,94 | 990.922.950 |
| Copiers | 874 | 623 | 251 | 509.789.340 | 510.729.009,72 | 356.198.338 |

No se escribio ningun movimiento en Dataverse durante esta prueba y no se envio
nada a Siigo.

Importacion real ejecutada el 2026-05-20:

| Resultado | Valor |
| --- | ---: |
| Movimientos bancarios creados en `cr07a_movimientobancario` | 3.084 |
| Traslados internos creados en `cr07a_trasladointernoflujocaja` | 630 |
| Total importado a Dataverse | 3.714 |
| Filas futuras omitidas | 6 |

Validacion posterior de idempotencia:

| Resultado dry run posterior | Valor |
| --- | ---: |
| Registros nuevos pendientes | 0 |
| Registros por actualizar | 0 |
| Registros sin cambios | 3.714 |

No se envio nada a Siigo.

### Cruce de pagos de clientes desde flujo de caja

Objetivo: tomar entradas bancarias importadas desde los dos flujos (`Cloud` y
`Copiers`), leer la columna `Descripcion`, buscar las facturas mencionadas en
`cr07a_facturacion` y dejar un borrador revisable del comprobante de ingreso.

Estado actual:

- Servicio: `ICashFlowMatchingService`.
- Job: `WeeklyCashFlowMatchingHostedService`.
- Configuracion: `CashFlowMatching`.
- Endpoint manual: `POST /automation/cash-flow/match-client-payments`.
- No envia nada a Siigo.

Tabla nueva: `cr07a_cruceflujocaja`.

| Campo | Uso |
| --- | --- |
| `cr07a_tipo` | `PagoCliente` |
| `cr07a_estado` | `Sugerido`, `SinFacturaDescripcion`, `FacturaNoEncontrada`, `FacturaAmbigua`, `DiferenciaFueraRango` |
| `cr07a_confianza` | Puntaje 0-100 del match |
| `cr07a_motivo` | Explicacion para supervision |
| `cr07a_movimientobancarioid` / `cr07a_movimientoclaveexterna` | Movimiento bancario origen |
| `cr07a_fechamovimiento` / `cr07a_origenflujo` | Fecha y flujo (`Cloud` o `Copiers`) |
| `cr07a_bancocuentacodigo` / `cr07a_bancocuentanombre` | Banco que recibio el dinero |
| `cr07a_descripcionmovimiento` | Descripcion original del Excel |
| `cr07a_facturacionid` / `cr07a_facturanumero` | Facturas encontradas |
| `cr07a_cliente` | Cliente de la factura |
| `cr07a_valorfactura` / `cr07a_valorpago` | Total factura(s) y entrada bancaria |
| `cr07a_reteftevalor` / `cr07a_reteicavalor` / `cr07a_rteivavalor` | Retenciones calculadas con porcentajes registrados en facturacion |
| `cr07a_diferencia` | Factura - pago - retenciones |
| `cr07a_jsonborradorsiigo` | Lineas sugeridas para un futuro comprobante de ingreso |
| `cr07a_claveexterna` / `cr07a_hashorigen` | Idempotencia |

Reglas iniciales:

- Solo revisa movimientos bancarios con `valor entrada > 0`.
- Busca facturas explicitas en la descripcion con prefijos como `FV`, `FEV`,
  `FEM`, `FE`, `FEDT`, `FEKT`.
- Si encuentra todas las facturas y la diferencia queda entre `-5000` y
  `5000`, deja estado `Sugerido`.
- Si falta factura, hay ambiguedad o la diferencia supera la tolerancia, deja
  `Pendiente de revision` por medio del estado especifico.
- El borrador Siigo usa banco, clientes nacionales `13050501`, retenciones
  conocidas y ajuste al peso `42958101` cuando aplique.

Prueba y carga inicial ejecutada el 2026-05-20 para 2026-01-01 a
2026-05-20:

| Resultado | Valor |
| --- | ---: |
| Entradas bancarias revisadas | 481 |
| Cruces creados en `cr07a_cruceflujocaja` | 481 |
| Sugeridos | 264 |
| Pendientes de revision | 217 |
| Sin factura detectable en descripcion | 153 |
| Factura no encontrada | 4 |
| Factura ambigua | 6 |
| Diferencia fuera de tolerancia | 54 |
| Valor entradas revisadas | 3.096.136.531,73 |
| Valor entradas sugeridas | 1.347.902.344,86 |
| Valor entradas pendientes de revision | 1.748.234.186,87 |

Validacion posterior: una segunda corrida dry run dejo `created=0`,
`updated=0`, `unchanged=481`, confirmando idempotencia. No se envio nada a
Siigo.

### Modulo Conciliacion en la app

Estado implementado el 2026-05-21:

- Modulo MVC: `Conciliacion`.
- Ruta: `/Conciliacion`.
- Opcion de acceso en `cr07a_empleado.cr07a_modulos`: `Conciliacion`
  (`645250022`).
- Acceso piloto asignado solo a `sruiz@digitaltechcolombia.com`.
- Vista simplificada con menu lateral como filtros. Las fases actuales son:
  `Flujo de caja por banco`, `Registro de Salidas FE`,
  `Registro de Entradas FE`, `Registro de cuentas de cobro`,
  `Registro de comprobantes contables` y `Registros huerfanos`.
- Cada fase muestra un cuadro `Ya esta` / `Hace falta`, pasos de estado y una
  tabla filtrada del periodo.
- `Flujo de caja por banco` muestra las filas importadas con tipo de
  comprobante detectado, estado de validacion y estado Dataverse/Siigo.
- `Registro de Entradas FE` sigue siendo la primera fase funcional completa:
  usa `cr07a_cruceflujocaja` para aprobar, revisar, rechazar y prevalidar.
- Acciones disponibles sobre `cr07a_cruceflujocaja`: aprobar, marcar revision
  manual y rechazar. Estas acciones solo actualizan Dataverse; no crean
  documentos en Siigo.
- Prevalidacion pre-Siigo agregada para pagos de clientes. El boton `Validar`
  revisa el borrador contable antes de cualquier envio: debe estar balanceado,
  tener factura, cliente, banco, cuenta contable por linea y cuentas activas en
  el catalogo Siigo de Dataverse.
- Estados de preparacion:
  - `ValidadoPendienteAprobacion`: el borrador cuadra, pero aun falta aprobar.
  - `ListoSiigo`: el cruce esta aprobado y paso prevalidacion.
  - `BloqueadoSiigo`: falta corregir cuenta, datos base o balance contable.
- Campos agregados a `cr07a_cruceflujocaja`: `cr07a_preflightestado`,
  `cr07a_preflightmensaje`, `cr07a_preflightfecha`,
  `cr07a_preflightdebito`, `cr07a_preflightcredito`.
- La prevalidacion tampoco envia nada a Siigo; solo deja el registro preparado
  o bloqueado para supervision.
- El popup de reasignacion de categoria ya existe a nivel visual y restringe
  opciones segun `Entrada`, `Salida` o `Traslado`. Falta guardar la
  reasignacion en Dataverse y reprocesar la fila.

Estado por fase dentro del modulo:

| Fase | Ya esta | Hace falta |
| --- | --- | --- |
| Flujo de caja por banco | Importacion Cloud/Copiers a Dataverse; traslados internos separados; columna visual de tipo detectado | Guardar categoria reasignada; cruce mensual con extractos; bloqueo de envio Siigo si no esta validado/completo |
| Registro de Salidas FE | Filtro y tabla de salidas con factura electronica; checks visuales Dataverse/Siigo/pago/saldo | Cruce real contra DIAN/Dataverse; consulta de factura y saldo en Siigo; prevalidacion completa antes del envio |
| Registro de Entradas FE | Cruce de entradas contra facturacion Dataverse; aprobacion/revision/rechazo; prevalidacion pre-Siigo con retenciones | Envio real a Siigo para `ListoSiigo`; marca definitiva de pago registrado; sincronizacion posterior Siigo -> Dataverse |
| Registro de cuentas de cobro | Deteccion inicial desde flujo; formulario actual de retenciones en modulo cuentas de cobro | Crear automaticamente la cuenta de cobro desde flujo; subir a Siigo al completar retenciones; marcar subida a Dataverse en importacion DIAN siguiente |
| Registro de comprobantes contables | Deteccion de MI PLANILLA, ENEL, ETB, intereses, inversiones, gravamen y gastos bancarios; catalogo y plantillas base | Consolidar gravamen mensual; partir MI PLANILLA por concepto; validar asiento completo antes de crear Siigo/Dataverse |
| Registros huerfanos | Vista dedicada y popup visual de reasignacion por entrada/salida | Guardar la reasignacion en Dataverse; crear reglas desde correcciones; reprocesar filas |

Clasificacion objetivo desde flujo de caja:

| Direccion | Tipo de comprobante | Criterio inicial |
| --- | --- | --- |
| Entrada | Pago de factura | La descripcion contiene numero de factura, por ejemplo `FV`, `FEV`, `FE`, `FEDT`, `FEKT` |
| Entrada | Comprobante contable | Abono de intereses, apertura/cancelacion de inversion, rendimientos u otros ingresos sin factura |
| Entrada/Salida | Traslado interno | Solo entre cuentas; traslados de bolsillos se ignoran |
| Salida | Factura electronica | Factura electronica de proveedor |
| Salida | Documento soporte | Cuenta de cobro/documento soporte |
| Salida | Comprobante contable | MI PLANILLA, ETB, ENEL, cancelacion de inversion, gravamen, comisiones, intereses, impuestos |

Pendientes operativos:

- Gravamen/GMF: no se debe subir fila por fila si el extracto lo consolida
  mensual. La propuesta es acumular por banco y periodo en una tabla
  `cr07a_consolidadogmf`, validar contra extracto al cierre y crear un solo
  comprobante de fin de mes por banco con debito a `53050502` y credito al
  banco correspondiente.
- MI PLANILLA: el flujo de caja trae un solo valor, pero el comprobante debe
  dividirse en salud, pension, ARL y caja de compensacion. La propuesta es una
  plantilla multi-linea obligatoria con captura/carga del soporte de planilla;
  la prevalidacion bloquea si la suma de lineas no coincide con la salida
  bancaria.
- Cambios posteriores en Siigo: guardar siempre `siigoDocumentId`, numero,
  fecha, saldo, estado y hash de lineas en Dataverse. Un job de sincronizacion
  mensual debe consultar Siigo por ids/documentos del periodo, actualizar el
  espejo en Dataverse y marcar `DiferenciaSiigo` cuando cambien saldo, estado
  o lineas despues de conciliado.
- Frecuencia base mientras el flujo siga en Excel: importar/actualizar flujo de
  caja a Dataverse semanalmente o cada vez que se cierre una jornada de pagos.
  Una vez al mes se carga el extracto bancario, se cruza banco vs flujo y se
  llena una tabla de cierre mensual con saldo inicial, entradas, salidas,
  traslados, saldo final extracto y diferencia por banco. El mes solo se cierra
  si todas las filas estan clasificadas, validadas y los saldos finales
  coinciden.

### `cr07a_excepcionautomatizacion`

| Campo | Uso |
| --- | --- |
| `cr07a_origen` | Gasto, pago cliente, pago proveedor, flujo caja |
| `cr07a_origenid` | Id registro origen |
| `cr07a_motivo` | Motivo revision |
| `cr07a_sugerencia` | Sugerencia automatica |
| `cr07a_estado` | Pendiente, aprobado, rechazado, corregido |
| `cr07a_error_siigo` | Error devuelto por Siigo |

## Tolerancias propuestas

| Proceso | Tolerancia | Accion |
| --- | ---: | --- |
| Conciliacion total/IVA exacta | COP 1 | Aceptar redondeo |
| Cloud + Copiers vs total/base | COP 1 | Aceptar redondeo |
| Pago cliente vs factura + retenciones | COP 5.000 | Subir automatico si cliente/factura/banco coinciden |
| Ajuste al peso | COP 5.000 | Crear linea `42958101` |
| Pago proveedor vs saldo documento | COP 1.000 | Sugerir; automatico solo si proveedor/documento/banco coinciden |
| Match por NIT + factura + total | COP 1 | Confianza alta |
| Match por NIT + fecha + total | COP 1.000 | Confianza media, requiere aprobacion |

## Capacidades Siigo validadas en documentacion oficial

La documentacion oficial de Siigo confirma:

- `/v1/purchases`: crear, editar, eliminar y consultar facturas de compra.
- `/v1/purchase-support-documents`: crear, editar, eliminar y consultar
  documentos soporte.
- `/v1/vouchers`: crear y consultar recibos de caja.
- `/v1/payment-receipts`: crear, editar, eliminar y consultar recibos de
  pago/egreso.
- `/v1/journals`: crear y consultar comprobantes contables.
- `/v1/document-types`: consultar tipos de comprobante.
- `/v1/payment-types`: consultar medios/formas de pago.
- `/v1/taxes`: consultar impuestos y retenciones.

Fuentes oficiales:

- https://developers.siigo.com/docs/siigoapi/
- https://developers.siigo.com/docs/siigoapi/purchase/1-create-purchase
- https://developers.siigo.com/docs/siigoapi/purcsupporting-document/3-get-support-documents/
- https://developers.siigo.com/docs/siigoapi/voucher/1-create-voucher
- https://developers.siigo.com/docs/siigoapi/payment-receipts/1-create-payment-receipts
- https://developers.siigo.com/docs/siigoapi/journal-entry/1-create-journal/
- https://developers.siigo.com/docs/siigoapi/catalog/6-ge-types-of-receipt
- https://developers.siigo.com/docs/siigoapi/catalog/7-get-payment-methods
- https://developers.siigo.com/docs/siigoapi/catalog/2-get-taxes

## Orden de ejecucion recomendado

1. Crear campos temporales en `Gastos de la empresa`: cuenta contable, estado,
   fuente, regla aplicada, confianza y motivo revision.
2. Crear tablas de reglas: categoria, vertical, cuenta contable, movimiento
   bancario y excepciones.
3. Actualizar automaticamente catalogo de cuentas contables desde Siigo.
4. Aplicar reglas contables sobre gastos recientes y dejar excepciones en
   revision.
5. Crear importador DIAN con staging y deduplicacion por CUFE/CUDE, NIT,
   factura, fecha y total.
6. Aplicar autoclasificacion de categoria y Cloud/Copiers.
7. Construir bandeja de supervision para gastos sin regla o con baja confianza.
8. Automatizar cuentas de cobro hacia documento soporte Siigo.
9. Importar flujo de caja a Dataverse como movimientos bancarios.
10. Automatizar pagos de clientes con tolerancia de COP 5.000.
11. Automatizar pagos a proveedores con sugerencia y aprobacion.
12. Automatizar comprobantes contables varios desde reglas de banco/texto.
13. Integrar todo al reporte mensual de conciliacion.
