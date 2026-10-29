# Cierre de mes

La pestaña `/Dashboard?tab=month-close` consulta versiones guardadas en Dataverse. No consulta Siigo ni recalcula el histórico al navegar. El corte usa America/Bogota (UTC−5).

## Persistencia y operación

- Tabla organizacional `cr07a_cierremensual`, colección `cr07a_cierremensuals`, solución `InvoiceProcessing`. `scripts/provision_month_close.py --apply` crea el esquema de forma idempotente y comprueba organización y publicador. Antes de capturar 2025, ejecutar este comando: amplía también el límite de año de las instalaciones existentes, con respaldo de metadatos y verificación posterior.
- Una fila por período y revisión, con resumen ligero y contenido JSON comprimido. GUID determinista y `If-None-Match: *`: las carreras y reintentos no sobrescriben versiones. El contenido se verifica con SHA-256 al guardar y al leer. El hash detecta alteraciones; no es una firma externa ni sustituye una política de auditoría del entorno.
- `MonthCloseHostedService` revisa cada diez minutos los meses cerrados que faltan, empezando 45 segundos después del arranque. El App Service debe conservar Always On. El primero del mes, durante la primera hora de Colombia, se captura además el estado operativo disponible. Una recuperación posterior queda marcada como reconstrucción; no inventa inventario ni saldos de vacaciones anteriores.
- Las correcciones crean otra revisión y conservan la anterior. No existe endpoint web para sobrescribir cierres. La captura no modifica facturas, gastos ni pagos.
- Índice en caché cinco minutos, contenido verificado treinta minutos, consultas concurrentes agrupadas, detalle de 50 filas (máximo 100), búsqueda y Excel desde la versión guardada. El navegador no persiste datos financieros en almacenamiento local.
- Acceso al módulo Dashboard obligatorio. Las cifras, filas y exportaciones de vacaciones requieren adicionalmente RH o Nómina. El resumen compartido en caché no se modifica al ocultar información salarial.
- Los cierres admiten períodos desde enero de 2025. El detalle y las cifras de bancos requieren adicionalmente el permiso existente de Conciliación; también se ocultan del índice para usuarios sin ese permiso.

## Alcance financiero

**Cartera:** facturas Siigo anteriores al corte, aplicaciones de la cuenta 1305 y recibos por NIT/documento/fecha, menos notas crédito por su fecha de expedición. Se comprueba que el histórico completo explica el saldo actual. Si no concilia, el saldo histórico es nulo y se excluye del subtotal identificable. Los documentos legados sin auxiliar se conservan como pendientes; una fecha única de pago no prueba que toda una obligación esté cancelada.

**Proveedores:** compras Siigo y aplicaciones de la cuenta 2205 en comprobantes y recibos de egreso. Se cruzan por NIT y número interno o factura del proveedor, normalizando ceros iniciales. Una referencia ambigua o una diferencia contra el saldo actual impide presentar un saldo histórico. La cobertura es parcial: documentos soporte, otros pasivos y comprobantes sin desglose requieren auxiliar adicional. Los registros de gastos de Dataverse se usan para identificar proveedores, no para presumir deuda.

**Facturación:** base de facturas emitidas durante el mes menos base de notas crédito del mismo mes. Impuestos y retenciones se separan del total neto. Se conserva la clasificación Cloud/Copiers disponible al reconstruir; `Sin asignar` permanece dentro del total. La clasificación actual no prueba cuál era la clasificación histórica.

**Facturación anterior a Siigo:** se incluyen documentos de Dataverse que no aparecen en Siigo. Para documentos migrados `DT<n>` / `FV-2-<n>`, se exige una coincidencia única de NIT, importe y emisión con máximo un día de diferencia. Se conservan los importes de Siigo y la clasificación disponible en Dataverse, sin duplicar el documento. Los registros marcados DEMO se excluyen con advertencia. La base e IVA legados deben sumar el total bruto; si no concilian, quedan sin valor. Las retenciones imposibles se muestran pendientes. La ausencia de notas crédito históricas mantiene el reporte parcial.

**Bancos:** soporte opcional entregado al comando de mantenimiento. Se verifican hashes de procedencia, secuencias, saldo tras cada movimiento, abonos/cargos totales y continuidad entre meses. Las filas se agrupan por período del extracto: un ajuste con fecha valor del mes anterior se conserva en el extracto que lo reporta, sin alterar el cierre bancario previo. Se guarda el saldo inicial/final por cuenta, los movimientos y las referencias al PDF/página. Los abonos bancarios no se aplican automáticamente a facturas ni se clasifican todos como ingresos; incluyen transferencias internas. El Excel contiene movimientos, saldos por cuenta y hashes de los soportes.

Cuando todas las obligaciones de un reporte tienen saldo indeterminado, el subtotal queda pendiente, no cero. Los cierres de 2025 necesitan conciliar el auxiliar inicial al 31/12/2024. Los cierres previamente guardados conservan su contenido y hash.

Estos cierres reconstruidos representan la evidencia contable disponible al capturarlos, incluyendo documentos registrados retroactivamente. No prueban lo que mostraba el sistema en aquella fecha. Los subtotales de cartera/proveedores no deben interpretarse como saldos contables completos.

## Información para completar los siete reportes

Se propone cargar archivos CSV/Excel por período en una zona de preparación, validar duplicados y totales, conciliar contra el auxiliar, y publicar una nueva revisión conservando la anterior. Esta versión muestra los requisitos por reporte; aún no incluye un importador general desde la interfaz.

| Reporte | Columnas mínimas del archivo |
| --- | --- |
| Cartera | período, NIT, factura, fecha emisión/vencimiento, base, IVA, retenciones, saldo inicial; aplicaciones con fecha, valor y comprobante |
| Proveedores | período, NIT, factura/documento soporte, moneda, base, impuestos, retenciones; saldo inicial, abonos y notas con fecha y comprobante |
| Facturación por negocio | factura, NIT, línea y sublínea comercial; relación de ítems y notas crédito con documento original |
| Facturación siguiente mes | corte, contrato, cliente, producto, cantidad, moneda, TRM, precio, vigencia, fecha de facturación, consumos variables |
| Inventario | corte, referencia, bodega, cantidad inicial; entradas/salidas con fecha, costo unitario y método de valoración |
| Activos fijos | corte, ID/serial, propiedad, fecha alta/baja, costo, vida útil, residual, depreciación acumulada |
| Vacaciones | corte, empleado, ingreso/retiro, saldo inicial, días causados/tomados con fechas, base salarial vigente y provisión |

El estado operativo futuro cubre recurrentes Cloud en USD (sin consolidar Copiers/variables), cantidades de suministros sin valoración, inventario físico de equipos sin depreciación y vacaciones estimadas con sueldo/30. Ninguno se etiqueta como cierre contable completo.

## Comandos de mantenimiento

Ejecutar desde la raíz y compilar Release primero. El script obtiene únicamente la configuración de integración del App Service, la pasa al proceso hijo y no guarda credenciales.

```powershell
./scripts/Run-MonthClose.ps1 -Mode Preview -Year 2026
./scripts/Run-MonthClose.ps1 -Mode Capture -Year 2026
# Soporte bancario local verificado; nunca se incorpora a Git ni al ZIP del sitio:
./scripts/Run-MonthClose.ps1 -Mode Preview -Year 2025 -BankEvidencePath tmp/bank-evidence.json
./scripts/Run-MonthClose.ps1 -Mode Capture -Year 2025 -BankEvidencePath tmp/bank-evidence.json
# Solo después de corregir/conciliar la fuente; conserva v1 y crea v2:
./scripts/Run-MonthClose.ps1 -Mode Capture -Year 2026 -RevisePeriod 2026-09
```

`--month-close-replay` recalcula únicamente archivos locales de previsualización a partir de `tmp/month-close-source.json`; no persiste cierres. `MonthClose:Enabled=false` detiene la captura automática sin afectar la consulta de cierres existentes. No se deben versionar fuentes financieras, archivos temporales ni configuración de producción.

El soporte bancario JSON usa `archiveSha256`, `statements` (file, account enmascarada a cuatro dígitos, period AAAAMM, opening, credits, debits, closing, rows, documentSha256) y `transactions` (id, date AAAA-MM-DD, account, description, value con signo, balance, source, page, sequence). La validación rechaza duplicados, saldos inconsistentes y movimientos fuera del extracto. La carga es administrativa por CLI; no se ha abierto un endpoint de importación ni se publican los PDFs en `wwwroot`.

## Verificación

Las pruebas `MonthCloseTests` cubren pagos posteriores, abonos parciales, notas crédito, cruces por tercero/documento, obligaciones no conciliadas, límites de calendario de Colombia, rechazo de meses abiertos, ausencia de inventario histórico, integridad SHA-256, revisiones deterministas, caché sin llamadas operativas y permisos salariales. La validación de despliegue debe incluir lectura de los cierres persistidos, segunda captura sin duplicados, navegación de meses, búsqueda/paginación y exportación autenticadas.
