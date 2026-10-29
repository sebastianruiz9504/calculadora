# Cierre de mes

La pestaña `/Dashboard?tab=month-close` consulta versiones guardadas en Dataverse. No consulta Siigo ni recalcula el histórico al navegar. El corte usa America/Bogota (UTC−5).

## Persistencia y operación

- Tabla organizacional `cr07a_cierremensual`, colección `cr07a_cierremensuals`, solución `InvoiceProcessing`. `scripts/provision_month_close.py --apply` crea el esquema de forma idempotente y comprueba organización y publicador.
- Una fila por período y revisión, con resumen ligero y contenido JSON comprimido. GUID determinista y `If-None-Match: *`: las carreras y reintentos no sobrescriben versiones. El contenido se verifica con SHA-256 al guardar y al leer. El hash detecta alteraciones; no es una firma externa ni sustituye una política de auditoría del entorno.
- `MonthCloseHostedService` revisa cada diez minutos los meses cerrados que faltan, empezando 45 segundos después del arranque. El App Service debe conservar Always On. El primero del mes, durante la primera hora de Colombia, se captura además el estado operativo disponible. Una recuperación posterior queda marcada como reconstrucción; no inventa inventario ni saldos de vacaciones anteriores.
- Las correcciones crean otra revisión y conservan la anterior. No existe endpoint web para sobrescribir cierres. La captura no modifica facturas, gastos ni pagos.
- Índice en caché cinco minutos, contenido verificado treinta minutos, consultas concurrentes agrupadas, detalle de 50 filas (máximo 100), búsqueda y Excel desde la versión guardada. El navegador no persiste datos financieros en almacenamiento local.
- Acceso al módulo Dashboard obligatorio. Las cifras, filas y exportaciones de vacaciones requieren adicionalmente RH o Nómina. El resumen compartido en caché no se modifica al ocultar información salarial.

## Alcance financiero

**Cartera:** facturas Siigo anteriores al corte, aplicaciones de la cuenta 1305 y recibos por NIT/documento/fecha, menos notas crédito por su fecha de expedición. Se comprueba que el histórico completo explica el saldo actual. Si no concilia, el saldo histórico es nulo y se excluye del subtotal identificable. Los documentos legados sin auxiliar se conservan como pendientes; una fecha única de pago no prueba que toda una obligación esté cancelada.

**Proveedores:** compras Siigo y aplicaciones de la cuenta 2205 en comprobantes y recibos de egreso. Se cruzan por NIT y número interno o factura del proveedor, normalizando ceros iniciales. Una referencia ambigua o una diferencia contra el saldo actual impide presentar un saldo histórico. La cobertura es parcial: documentos soporte, otros pasivos y comprobantes sin desglose requieren auxiliar adicional. Los registros de gastos de Dataverse se usan para identificar proveedores, no para presumir deuda.

**Facturación:** base de facturas emitidas durante el mes menos base de notas crédito del mismo mes. Impuestos y retenciones se separan del total neto. Se conserva la clasificación Cloud/Copiers disponible al reconstruir; `Sin asignar` permanece dentro del total. La clasificación actual no prueba cuál era la clasificación histórica.

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
# Solo después de corregir/conciliar la fuente; conserva v1 y crea v2:
./scripts/Run-MonthClose.ps1 -Mode Capture -Year 2026 -RevisePeriod 2026-09
```

`--month-close-replay` recalcula únicamente archivos locales de previsualización a partir de `tmp/month-close-source.json`; no persiste cierres. `MonthClose:Enabled=false` detiene la captura automática sin afectar la consulta de cierres existentes. No se deben versionar fuentes financieras, archivos temporales ni configuración de producción.

## Verificación

Las pruebas `MonthCloseTests` cubren pagos posteriores, abonos parciales, notas crédito, cruces por tercero/documento, obligaciones no conciliadas, límites de calendario de Colombia, rechazo de meses abiertos, ausencia de inventario histórico, integridad SHA-256, revisiones deterministas, caché sin llamadas operativas y permisos salariales. La validación de despliegue debe incluir lectura de los cierres persistidos, segunda captura sin duplicados, navegación de meses, búsqueda/paginación y exportación autenticadas.
