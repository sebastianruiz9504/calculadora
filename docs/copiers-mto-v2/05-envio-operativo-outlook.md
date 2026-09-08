# Envío operativo MTO Firmado V2

Contrato: `outlook-send-once-v1` · 8 de septiembre de 2026.

Read-back operativo: flujo `66e7671c-17d4-4f82-b0f3-59070c0a00b8`, creado el `2026-09-08T23:21:25Z`, estado **Started**, sin suspensión, 34 acciones. Verificados trigger limitado a V2, concurrencia 1, salida del trigger protegida, envío sin reintento y cero referencias a ubicación/tablas antiguas. Cero ejecuciones al cierre de la instalación: no se envió ningún correo. La definición exportada tiene SHA-256 `9BFAE930B3B571F788821FB4E69D4CDADD497E458445FC04ED9D8014CB5C4825`.

## Alcance

Flujo independiente `Copiers MTO V2 - Enviar reporte firmado`. No modifica ni utiliza la tabla MTO antigua ni sus flujos. Reutiliza las conexiones activas y propiedad de `sruiz@digitaltechcolombia.com` verificadas en `Acta de entrega mantenimientos`: Dataverse y Office 365 Outlook. No cambia el remitente vigente ni crea credenciales/conexiones nuevas.

Artefactos:

- `scripts/Provision-CopiersMtoV2MailFlow.ps1`: plan por defecto, creación solamente con `-Apply`; no sobrescribe un flujo ya existente. Relee las conexiones y exige cuenta propietaria coincidente y estado Connected.
- `artifacts/power-automate-outlook-send-once-v1.deployed.json`: definición releída tras crear/activar, sin credenciales, callback URL ni datos de clientes. El ID y estado del flujo corresponden a ese read-back.

## Secuencia

1. Modificación en `dtc_copiersmtov2`, filtrada por `ReadyToSend` y `Pending`; concurrencia global 1. La salida del trigger está protegida en el historial.
2. Nueva lectura allowlisted de la fila con esos estados. Una ejecución duplicada que encuentra Processing/Sent/Failed termina sin efectos.
3. Cambia el correo a Processing antes de preparar o enviar archivos. No utiliza una clave de claim ETag: la serialización depende del flujo único con concurrencia 1 y de no cambiar manualmente Processing/Sent a Pending.
4. Resuelve el PDF exclusivamente por su evidenceKey, padre y propósito SignedReport. Confirma nombre, MIME, SHA-256 declarado y tamaño. Relee su ETag después de descargar para detectar cambios.
5. Valida el manifiesto JSON. Para cada evidencia CustomerAttachment exige padre, clave, secuencia, nombre genérico, MIME JPG/PNG, tamaño, hash declarado y estado ScanPassed. Rechaza claves/secuencias repetidas; compara el ETag antes/después de la descarga. No descubre ni envía originales o la firma separada.
6. Exige PDF más todos los adjuntos del manifiesto y un presupuesto conservador de 25 MiB codificados. Nunca omite archivos para forzar un envío.
7. `SendEmailV2` recibe destinatario, asunto y HTML congelados en la fila y los archivos ya preparados. La política de reintento es `none`.
8. Si Outlook confirma éxito, marca Sent. Si falla o vence el tiempo en el envío, conserva Processing y registra `EMAIL_REVIEW_REQUIRED`; no reenvía automáticamente. Si falla la preparación, marca Failed sin enviar.

Si la actualización Sent falla después del envío, la fila permanece Processing y el run fallido requiere revisar Enviados antes de cualquier reintento. No hay garantía transaccional de exactly-once entre Outlook y Dataverse, ni reconciliación automática de mensajes. Un administrador no debe restablecer Pending sin revisar el buzón y el historial.

## Privacidad y binarios

Las proyecciones de lectura y expresiones no incluyen ubicación, notas internas, dirección interna ni tablas antiguas. Las acciones de conectores usan Secure Inputs/Outputs; Parse JSON usa Secure Inputs (su único ajuste compatible). Append to array no admite esos ajustes: consume directamente la salida protegida de la descarga, por lo que el motor protege esa entrada derivada. El envío protege entradas y salidas.

La descarga utiliza el action real `GetEntityFileImageFieldContentWithOrganization`; `ContentBytes` consume `body('Download...')`, igual que el flujo existente de Copiers. No transforma el contenido binario en texto. La app previamente verifica SHA-256 contra la descarga de Dataverse; el flujo compara metadata declarada y ETags, no recalcula hashes.

## Qué acredita la configuración

`PowerAutomateDeliveryConfigured=true` significa que se verificaron el flujo separado, sus conexiones activas, definición y trigger Started. No significa `PowerAutomateDraftReconciliationVerified`, ni certifica recepción del cliente. Durante la instalación no se crean tickets de prueba ni se envían correos. La primera entrega real debe comprobarse por estado de la fila, ejecución y buzón.

Referencias oficiales: [Office 365 Outlook](https://learn.microsoft.com/en-us/connectors/office365/), [Microsoft Dataverse](https://learn.microsoft.com/en-us/connectors/commondataserviceforapps/), [protección de entradas y salidas](https://learn.microsoft.com/en-us/azure/logic-apps/logic-apps-securing-a-logic-app).
