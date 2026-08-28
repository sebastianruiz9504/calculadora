# Autenticacion de produccion

## Estado actual

Desde el 20 de julio de 2026, `calculadoradt` autentica la aplicacion confidencial de Microsoft Identity mediante una identidad administrada y una credencial federada. El inicio de sesion ya no depende de un secreto con fecha de vencimiento.

- App Service: `calculadoradt`
- Grupo de recursos: `DigitalTechAppAI`
- App registration: `3a00b1a6-a85b-4ddf-9014-8eb939e22aac`
- Identidad administrada, principal ID: `7fcb66d4-2477-4adf-bd89-b561179dcfbb`
- Credencial federada: `calculadoradt-system-mi`
- Configuracion activa: `AzureAd__ClientCredentials__0__SourceType=SignedAssertionFromManagedIdentity`

No debe existir una configuracion `AzureAd:ClientSecret` ni una credencial `ClientSecret` dentro de `AzureAd__ClientCredentials` en este App Service.

## Comprobacion operativa

1. Confirmar que la identidad asignada al App Service conserva el principal ID documentado.
2. Confirmar que la credencial federada conserva este emisor:
   `https://login.microsoftonline.com/cab7ea42-4a21-4548-952f-fcde81f2bdd6/v2.0`.
3. Confirmar que el asunto de la federacion coincide exactamente con el principal ID de la identidad.
4. Cerrar la sesion local de la aplicacion e iniciar una nueva.
5. Verificar que la portada muestra el usuario, los modulos y las tareas provenientes de Dataverse.

## Recuperacion

Si una autenticacion falla, revisar primero la identidad administrada, la credencial federada y la configuracion `ClientCredentials`. No crear un secreto nuevo como solucion permanente. Un secreto temporal solo debe usarse como respaldo durante una reparacion controlada y debe retirarse despues de validar un inicio de sesion nuevo.

## Credenciales de integraciones

`Dataverse__ClientSecret` y `M365__ClientSecret` pertenecen a otra app registration (`51896c3b-b1e8-4eb6-a484-4bb56da348ae`) y no participan en el inicio de sesion del portal. Sus vencimientos deben controlarse por separado porque pueden afectar procesos app-only de Dataverse y Microsoft 365, aunque no deberian producir el bucle de autenticacion del portal.

Application Insights y la regla `Failure Anomalies - calculadoradt` permanecen disponibles para detectar incrementos de errores de la aplicacion.
