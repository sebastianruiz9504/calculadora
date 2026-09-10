# Actividades Copiers V2

Extensión aditiva de `/CopiersMtoV2`, construida sobre producción
`e4a4d64c3c288203c4bcf7cce3a8e329cfe87f7a`.

## Alcance

- Preventivo/correctivo conservan su diario y flujo de correo existentes.
- Movimiento de equipo y entrega de tóner usan formularios propios, firma,
  evidencias, PDF empresarial de una página y el mismo calendario interno.
- `dtc_copiersactivityv2` y `dtc_copiersactivityevidencev2` conservan las actas
  firmadas, el consecutivo `ACT-{SEQNUM:6}`, las instantáneas y la ubicación
  interna. No se crean mantenimientos ficticios para representar estas operaciones.
- Los movimientos se escriben en `cr07a_movimientosequipos` y actualizan el cliente
  del equipo. Las entregas se escriben en `cr07a_entrega` y descuentan existencias
  de `cr07a_suministro`. El registro y la actualización usan un changeset atómico,
  ETag y clave determinista; el PDF se verifica antes de habilitar el envío.
- Los reintentos reutilizan la misma operación. Una actividad no puede cambiar
  de tipo, cliente o equipo después de reservar su clave.
- En mantenimiento, únicamente un cliente confirmado sin equipos permite serial
  externo manual. Conserva sus contadores en el reporte, sin crear un equipo ni
  una fila de contadores sin relación real.
- `Tomar foto` utiliza un input de imagen con `capture="environment"`; la
  disponibilidad de cámara y los permisos dependen del dispositivo/navegador.
- `Crear otro registro` aparece sólo con reporte listo y correo confirmado como
  enviado. Reinicia formulario y clave; no reenvía la operación anterior.

## Aprovisionamiento autorizado

Entorno: Digital Tech Copiers (default). Se reutilizan la solución
`CopiersMtoFirmadoV2`, el publisher `DigitalTechCopiers`, el rol
`Copiers MTO V2 App Runtime` y el perfil `Copiers MTO V2 App Fields`.

1. Exportar la solución antes de cambiar el esquema.
2. `scripts/provision_copiers_activity_v2.py`: plan previo y aplicación aditiva.
   La comprobación posterior exige contrato exacto y claves alternas activas.
   En este entorno, `CreateRelationship` dejó inicialmente desactivada la
   auditoría de los tres lookups nuevos. La normalización acotada se conserva en
   `scripts/Repair-CopiersActivityV2LookupAudit.ps1`: IDs explícitos, único cambio
   `IsAuditEnabled.Value`, definición restante intacta y read-back estricto.
3. `scripts/Provision-CopiersActivityV2Security.ps1`: plan previo y grants
   limitados a la aplicación existente; sin administrador ni eliminación.
   En esta aplicación se verifican exactamente 16 privilegios previos más 24
   autorizados. `scripts/Reconcile-CopiersActivityV2Privileges.ps1` conserva la
   reconciliación de cuatro permisos SharePoint inesperados, comprobándolos contra
   el ZIP anterior y sin retirar ningún permiso previo ni autorizado.
4. `scripts/Provision-CopiersActivityV2MailFlow.ps1`: nuevo flujo aislado,
   reutilizando las conexiones del flujo original sin modificarlo.
5. `scripts/register_copiers_activity_v2_solution.py` registra los diez componentes
   existentes autorizados en la solución: dos tablas completas nuevas y ocho
   columnas de negocio, sin ampliar las tablas anteriores completas. Exportar y
   conservar la solución actualizada y sus componentes descomprimidos.
6. Activar `CopiersMtoV2__ActivitiesEnabled=true` con el artefacto validado.

El flujo de actividades creado es `1cd00bbc-adc3-4c8f-a56a-88258158ce53`.
El flujo original es `66e7671c-17d4-4f82-b0f3-59070c0a00b8`.
El correo conserva la conexión de Office 365 existente (Sebastián Ruiz), con
copia a Germanruiz@digitaltechcolombia.com y
soportecopiers@digitaltechcolombia.com. Esta extensión no cambia el remitente
dinámicamente al técnico. No se deben reintentar automáticamente envíos de
resultado incierto en estado Processing.

## Verificación y reversión

Pruebas C# de negocio, ciclo de vida, calendario y PDF; pruebas Node de formulario,
selector y calendario; pruebas Python del contrato de esquema; inspección visual
de ambos PDF de muestra. Las pruebas sintéticas no sustituyen una firma real ni
una entrega de correo a un cliente.

La publicación conserva la configuración y todos los recursos ajenos al alcance.
El paquete delta incluye DLL/PDB, manifiesto estático combinado con producción y
los cuatro CSS/JS de formulario y calendario con sus variantes comprimidas.
Se conserva un ZIP de reversión exacto y evidencia de hashes. Ante una reversión,
desactivar el flag y restaurar el artefacto previo; no borrar tablas, filas,
permisos ni flujos como parte de una reversión de código.
