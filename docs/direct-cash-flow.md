# Flujo de caja directo

Implementado sobre `origin/production` (`89d2ce6`) el 17 de noviembre de 2026.

## Uso

- Abrir **Conciliación → Flujo de caja directo**. Conserva la navegación lateral del módulo y abre el mes seleccionado; los filtros permiten ampliar el período.
- Abrir un movimiento desde Conciliación 2 o desde una celda del reporte y guardar su número y concepto. El catálogo filtra ingresos y salidas, con validación adicional en el servidor.
- **Registro manual** permite incorporar movimientos bancarios faltantes, identificados en morado y con estado **Solo banco**. Se pueden editar o anular, conservando el registro. No generan comprobantes ni llamadas a Siigo.
- Completar el saldo inicial consolidado y los cuatro saldos de cierre seleccionando sus celdas. Cero es un valor válido; vacío significa pendiente. CHECK compara el cierre calculado con Cloud + Copiers + Bolsillos + Fidurenta únicamente cuando todos están informados.
- Descargar Excel conserva el formato, orden, códigos, estilos y hojas ocultas del archivo entregado. Las columnas corresponden al período seleccionado. Los datos pendientes no se inventan.
- **Control de extractos** separa el cotejo bancario de la clasificación contable. Comprueba cada registro, ingresos, egresos y saldo de cierre, incluso si aún no tiene concepto. La descarga agrega la hoja **Control bancario**, con saldos, fórmulas y diferencias por cuenta.
- Los registros provenientes de extractos se distinguen en morado, con etiqueta **Extracto**, referencia de página/fila y sin envío a Siigo. Se puede editar su concepto; sus importes originales se conservan para comprobar los saldos. El detalle ofrece el filtro **Extracto** y páginas de 50 movimientos.

## Piloto de octubre

Los dos PDF de octubre se procesaron localmente el 17/11/2026. Contienen 346 movimientos: Cloud 261 y Copiers 85. Se verificó cada saldo consecutivo y los totales mensuales de entradas, salidas y cierre antes de cualquier escritura.

Las 145 clasificaciones están guardadas como `Sugerido`, pendientes de revisión, sin alterar importes ni estados de conciliación existentes. Noviembre y diciembre no reciben sugerencias automáticas. El botón de octubre conserva las clasificaciones existentes; puede sugerir nuevos registros del mismo mes.

Los 171 movimientos existentes coinciden exactamente por cuenta, fecha, sentido e importe. Se agregaron 175 faltantes (135 Cloud y 40 Copiers) y se importaron los saldos iniciales y finales. No se modificaron los movimientos originales. La segunda ejecución del plan propone cero altas: consume coincidencias como multiconjunto, preservando comisiones repetidas legítimas.

Ambas cuentas cuadran, sin registros faltantes, adicionales, modificados ni diferencias monetarias. Dos de los nuevos movimientos forman un traslado entre cuentas propias: quedan 173 nuevos movimientos por categorizar y 145 sugerencias anteriores por revisar. Bolsillos y Fiduciaria permanecen vacíos porque esos saldos no están acreditados en los PDF entregados. El CHECK del flujo por concepto sigue pendiente mientras falten categorías o saldos complementarios; el cotejo de las dos cuentas bancarias ya está completo.

## Cálculo y plantilla

- 52 conceptos de detalle. El código impreso 903 aparece dos veces en el modelo: claves estables `903:51` (viáticos) y `903:52` (representación).
- El flujo usa el importe efectivamente registrado en banco, incluyendo registros manuales. Los movimientos sin concepto aparecen en los indicadores bancarios y en pendientes, pero no se distribuyen en filas del reporte.
- Las sugerencias sí suman; la pantalla y la descarga indican que requieren revisión.
- Los traslados explícitos se excluyen del consolidado. Para registros omitidos con descripción CONCILIO, se reconoce una contrapartida única del mismo mes, igual importe, sentido opuesto, otra cuenta y diferencia máxima de cuatro días. Los registros manuales no se infieren como contrapartidas. Los traslados sin contrapartida generan advertencia.
- Para filas procedentes de los extractos se reconoce el par único del mismo día e importe, cuentas distintas y sentidos opuestos, cuando ambas tienen la descripción bancaria `TRANSFERENCIA CTA SUC VIRTUAL`. No se aplica esta inferencia a movimientos manuales ordinarios ni a grupos ambiguos.
- El cierre anterior se arrastra únicamente dentro del período seleccionado, cuando no quedan conceptos pendientes ni traslados con diferencia. También se pueden usar los saldos iniciales ya registrados para ambas cuentas de conciliación; el saldo consolidado ingresado explícitamente tiene prioridad.
- Se corrigieron las sumas del modelo que omitían socios, apertura de CDT o contenían totales fijos en diciembre. Las mismas fórmulas se aplican a todos los meses. La hoja visible no conserva importes de ejemplo.

## Persistencia y rendimiento

Tablas aditivas de la solución existente `CotizadorInternoCRM`:

| Tabla | Propósito |
|---|---|
| `cr07a_clasificacionflujodirecto` | Concepto, origen, fecha, estado de revisión y motivo; GUID del movimiento original |
| `cr07a_movimientomanualbanco` | Movimientos exclusivamente bancarios; `EstadoSiigo=NoEnviar`; anulación mediante estado inactivo |
| `cr07a_saldoflujodirecto` | Saldo inicial y cierres por período; `cr07a_extractos` conserva el resumen, hash de los PDF y referencias de cada fila para su cotejo |

Las escrituras usan ETag y creación condicional. Los reintentos manuales conservan el UUID del cliente y se comprueba duplicidad de fecha, cuenta, importe, descripción y referencia. El servidor valida dirección del concepto, fechas y precisión decimal. Los endpoints requieren acceso al módulo Conciliación y antifalsificación para POST.

La vista del reporte evita construir el tablero completo o consultar Siigo. Las consultas están limitadas al período y usan selección de columnas y paginación Dataverse. El detalle responde en páginas de 50, con máximo de 100 por petición. Se comparte una caché de 20 segundos, invalidada al escribir, con exclusión de lecturas simultáneas repetidas. La pantalla refresca inmediatamente después de guardar y cada 25 segundos mientras está visible y sin formularios abiertos.

El control bancario reutiliza la consulta mensual de saldos, sin peticiones por cada fila ni lecturas nuevas de Siigo. La consulta inicial de octubre–diciembre con los extractos tomó aproximadamente 3,2 segundos en la prueba local y la repetida desde caché menos de 1 ms; no es una garantía de latencia de cada cliente.

## Instalación y validación

1. Verificar que el App Service productivo `dtc-calculadora-linux-7018`, dueño de `calculadora.digitaltechcolombia.com`, utiliza `https://orgc79ca19c.crm2.dynamics.com`. El servicio Windows `calculadoradt` contiene únicamente la redirección de la migración; el script y el workflow quedan alineados al destino Linux.
2. `python scripts/provision_direct_cash_flow.py` muestra el plan; `--apply` agrega únicamente tablas/columnas faltantes al entorno verificado.
3. Agregar cada tabla completa con `pac solution add-solution-component --solutionUniqueName CotizadorInternoCRM --component <logical name> --componentType 1 --AddRequiredComponents`, conectado al mismo entorno. Exportar y desempaquetar la solución. Las tres raíces deben tener `behavior="0"` y ninguna dependencia faltante consigo mismas.
4. Ejecutar las pruebas .NET y comprobar sintaxis de ambos JavaScript de conciliación. Desplegar con `scripts/Deploy-AppService.ps1 -VerifyPath /healthz -KeepPackage`; el proyecto excluye configuraciones locales, credenciales y `tmp` de la publicación.
5. Verificar reporte autenticado, detalle paginado, conceptos por dirección, descarga y hash de los archivos publicados. Las pruebas de creación/edición de movimientos se hicieron en un servidor local con persistencia en memoria para no incorporar cifras ficticias en producción.

El paquete y la carpeta de la solución contienen la exportación actual del entorno, incluyendo cambios de metadatos que ya existían en producción antes de esta funcionalidad.

## Importación verificada de extractos

`scripts/import_bank_statements.py` recibe filas previamente extraídas en JSON y los PDF originales para calcular su SHA-256. El modo predeterminado prepara un plan y respaldo local. `--apply` verifica entorno y solución, valida todos los saldos, crea únicamente los faltantes con UUID determinista, relee lo persistido y guarda el control bancario. Falla si hay registros actuales sin correspondencia o un extracto diferente ya cargado para el período. No versionar PDF, datos bancarios, planes ni respaldos; usar `tmp`.

Las cifras OCR no se aceptan por confianza visual solamente: toda fila debe cumplir saldo anterior + movimiento = saldo nuevo, y las sumas deben coincidir con ambos totales del extracto. El cotejo usa identidad y valor de cada registro, además del saldo, para detectar diferencias que se compensen entre sí.

## Alcance pendiente

Los dos extractos entregados están importados y se comprueban en pantalla y Excel. El cargador mensual de PDF dentro de la interfaz y el envío de registros exclusivamente bancarios a Siigo siguen pendientes. La importación actual es asistida y validada antes de escribir.
