# Cartera y cobranza

La vista Dashboard → Cartera → Detalle de facturas muestra únicamente facturas
vencidas sin pago con consecutivo numérico desde 7504. Se ocultan, sin borrar,
los registros anteriores y las facturas demo del rango inferior. No se alteran
las facturas, los pagos ni los documentos contables.

## Consulta y pantalla

- Paginación del servidor de aplicación: 25, 50 (predeterminado) o 100 filas.
- Dataverse filtra facturas sin fecha ni valor de pago y con total positivo.
  El servidor descuenta las notas crédito aceptadas, aplica el corte de
  consecutivo, la búsqueda y la paginación. Nunca se consulta todo el histórico
  de facturas para esta vista.
- Una instantánea de pendientes por identidad y fecha de Bogotá dura 60 s,
  con exclusión mutua al cargar. Buscar y paginar reutilizan esa instantánea.
  Actualizar fuerza una lectura; los envíos siempre revalidan el cliente en vivo.
- Búsqueda con espera de 300 ms, cancelación y descarte de respuestas antiguas.
- El detalle paginado incluye sus indicadores sin una consulta adicional de
  resumen. Las gráficas mensuales se cargan al visitar su pestaña.
- Ventana accesible con historial de 30 entradas por página, observaciones,
  destinatarios editables, plantillas y vista previa del estado de cuenta.

## Esquema de Dataverse

Entorno existente: `https://orgc79ca19c.crm2.dynamics.com`.
Solución existente: `InvoiceProcessing`, editor `Cr44b61`, prefijo `cr07a`.

| Elemento | Nombre lógico | Tipo |
|---|---|---|
| Correo cobranza de Cliente | `cr07a_correocobranza` | Texto, 4.000 caracteres |
| Gestión de cartera | `cr07a_carteragestion` | Tabla de organización |
| Factura / cliente | `cr07a_factura` / `cr07a_cliente` | Texto GUID |
| Facturas incluidas | `cr07a_facturas` | Texto largo, GUID separados por `;` |
| Tipo / estado | `cr07a_tipo` / `cr07a_estado` | Texto |
| Autor / destinatarios / asunto | `cr07a_autor` / `cr07a_destinatarios` / `cr07a_asunto` | Texto |
| Mensaje y detalle enviado | `cr07a_mensaje` | Texto largo |
| Enviado el | `cr07a_enviadoel` | Fecha/hora |

`scripts/provision_portfolio_collections.py` comprueba el entorno, la solución
y el editor antes de agregar el esquema. Sin `--apply` solo inspecciona.
La exportación generada por Dataverse se conserva bajo `solutions/InvoiceProcessing`.
Se conservó el XML previo de CierreMensual porque la diferencia de mínimo de año
encontrada en vivo pertenece a un trabajo anterior y queda fuera de este cambio.

## Correos y operación

Configuración de App Service:

```text
Collections__Enabled=true
Collections__ActivationDate=2026-11-28
Collections__Sender=cartera@digitaltechcolombia.com
Collections__IntervalMinutes=15
```

Se requiere `Migration__BackgroundWorkersEnabled=true` y Always On. La ausencia
de `Collections:Enabled` mantiene el trabajador apagado en desarrollo.

Cada 15 minutos y al arrancar, se revisan las facturas que entraron en mora
desde la fecha de activación, según America/Bogota. El vencimiento ocurre el
día siguiente a la fecha límite de pago. No se envía una campaña retroactiva
por las facturas que ya estaban vencidas al publicar.

Los destinatarios predeterminados combinan `cr07a_correoelectronico`,
`cr07a_correofacturacion` y `cr07a_correocobranza`, sin duplicados. Los adicionales
se pueden guardar por separado o al enviar; la actualización mezcla con los
valores actuales y usa ETag para no perder cambios simultáneos.

Las plantillas de segundo correo, aviso de suspensión y suspensión son editables.
Solo se envía el mensaje: estas acciones no suspenden ningún servicio. La plantilla
de suspensión requiere que el operador confirme que corresponde comunicarla.
Se adjunta un PDF y una tabla HTML con todas las facturas vencidas visibles del
mismo cliente. El historial conserva el texto, destinatarios, autor, fechas,
facturas y valores incluidos. Las observaciones se guardan solo en la factura
seleccionada; un correo aparece en el historial de todas las facturas incluidas.

Antes del envío se leen nuevamente pagos y notas crédito. Un cambio de saldo
o del conjunto de facturas exige revisar una nueva vista previa. Si falla la
lectura de notas crédito, se bloquea el envío. Si falta el cliente o su correo,
se registra un aviso pendiente y se reintentan los requisitos en la siguiente revisión.

## Duplicados y fallos

El primer aviso automático tiene un GUID determinista por factura. La creación
condicional con `If-None-Match: *` en Dataverse evita envíos simultáneos entre
instancias. El envío manual usa un identificador estable para los reintentos
del navegador. Los registros de salida se crean antes de llamar a Graph.

- `recorded`: observación guardada.
- `sending`: salida reservada; también puede indicar un reinicio o fallo al
  guardar la confirmación. Revisar el buzón antes de intentar otro envío.
- `sent`: Microsoft Graph aceptó el correo y se guardó la fecha. No acredita
  entrega final ni lectura del destinatario.
- `uncertain`: no se pudo confirmar el resultado. No se reintenta automáticamente.
- `blocked`: faltan requisitos; se revisan nuevamente en la siguiente ejecución.

En un resultado incierto o una reserva antigua, revisar Enviados del buzón de
cartera antes de iniciar otro envío manual. La aplicación prioriza evitar
duplicados cuando Microsoft 365 no permite conocer con certeza el resultado.

Referencias: [Graph sendMail](https://learn.microsoft.com/en-us/graph/api/user-sendmail?view=graph-rest-1.0),
[operaciones condicionales Dataverse](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/perform-conditional-operations-using-web-api).

## Validación de esta versión

- 1.170 pruebas .NET, incluidas 22 de cartera: corte, fechas, búsqueda,
  paginación, concurrencia, pagos entre vista previa y envío, notas de crédito,
  historial, PDF y resultado incierto.
- Navegador aislado con Razor y JavaScript reales, datos y envío simulados:
  escritorio, móvil, modo oscuro, búsqueda, notas, destinatarios y plantillas.
- Lectura real app-only: 182 pendientes, 55 vencidas visibles; 2.731 ms en frío,
  0,11 ms al reutilizar la instantánea. No son mediciones P95/P99 de sesiones reales.
- Lectura del nuevo campo, escritura y lectura de historial, creación duplicada
  rechazada en Dataverse y permiso Graph `Mail.Send` verificados. El registro
  temporal de prueba fue eliminado; no se enviaron correos reales de prueba.
- El primer envío automático real y su entrega al cliente se comprobarán cuando
  una factura cumpla la condición después de la activación.

Evidencia y reversión: `C:/cwt/artifacts/cartera-cobranza-20261127`.
