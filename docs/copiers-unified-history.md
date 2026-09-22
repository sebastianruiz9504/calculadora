# Historial unificado de mantenimientos

La fuente operativa es `dtc_copiersmtov2`; `cr07a_mantenimiento` queda como archivo original conservado. Dashboard y Soporte consultan V2. Cada visita se cuenta una vez; el historial por equipo incluye también los equipos adicionales del reporte firmado.

Los históricos se reconocen por `dtc_formversion=copiers-legacy-v1`, una clave GUID de origen y `dtc_workflowstate=827270004`. Conservan el estado comercial antiguo en `dtc_businessstatus`, la referencia de solicitud en `dtc_legacyrequestkey` y el registro completo original en `dtc_legacyjson`. El técnico corresponde al propietario original, como en la vista anterior. El propietario técnico de las copias V2 y sus evidencias debe ser la identidad existente `copiers-mto-v2-worker`, que tiene acceso por propietario; no se amplían sus permisos. `scripts/assign_copiers_history_owner.py` permite reconciliar únicamente las copias verificadas cuando una importación administrativa las creó con otro propietario. Las fechas conservan el día original sin conversión horaria.

Los documentos originales usan propósito `827270004`. Se permiten hasta 32 MiB exclusivamente para su lectura histórica; los reportes nuevos mantienen el límite de 12 MiB. Las descargas comprueban relación con el mantenimiento, tamaño, contenido, SHA-256 y versión de la evidencia. La migración no inventa firmas, aceptación del cliente, horas de visita ni envíos. Su estado de correo es `827270005` (no aplica), fuera del disparador del flujo.

Soporte filtra siempre por el técnico autenticado. Dashboard requiere acceso a Copiers para consultar el conjunto. Un histórico propio permite cambiar únicamente su estado comercial con control de concurrencia; el documento y la copia de los datos originales permanecen intactos. Las altas nuevas se dirigen a MTO V2 y la carga de actas por el formulario antiguo se rechaza en el servidor.

## Migración aprobada

`scripts/migrate_copiers_history.py` contiene los diez GUID excluidos: cinco coincidencias y cinco registros sin cliente. El inventario del 22 de septiembre de 2026 tiene 757 originales y 747 candidatos, con 705 archivos. Se incluyen pendientes, registros sin equipo y posibles coincidencias internas según la instrucción del usuario.

El script conserva una instantánea de origen y usa UUID determinista por registro/archivo. Primero crea el destino como borrador, comprueba todos sus campos y relaciones, descarga el archivo de destino para comparar SHA-256 y solo entonces publica el histórico. Es reanudable y rechaza divergencias de origen o destino. No borra ni actualiza originales. Ejecutar con el mismo directorio de evidencias; cualquier cambio del inventario requiere reconciliación explícita.

```
python scripts/migrate_copiers_history.py --output <evidencias>
python scripts/migrate_copiers_history.py --schema --output <evidencias>
python scripts/migrate_copiers_history.py --apply --output <evidencias>
```

## Remitente

El flujo `66e7671c-17d4-4f82-b0f3-59070c0a00b8` conserva su conexión Outlook existente y selecciona `From` y `ReplyTo` desde `dtc_technicianemailsnapshot`. No usa un remitente alterno cuando falta el técnico. Conserva destinatarios, copias, adjuntos, procesamiento serial y política sin reintentos de envío.

La cuenta de la conexión requiere SendAs sobre los buzones de los técnicos; inicialmente Jeison Romero y Luis Carlos Rivera. Un nuevo técnico debe tener buzón y ese permiso antes de utilizar los envíos. `scripts/update_copiers_mto_sender.py` verifica identidad del flujo, ausencia de ejecuciones activas, versión y lectura posterior del cambio. La prueba interna del 22 de septiembre confirmó entrega en Exchange desde ambos técnicos a Sebastián; no se reenviaron reportes de clientes.

## Publicación

Los scripts `Prepare-CopiersUnifiedRelease.ps1` y `Deploy-CopiersUnifiedRelease.ps1` exigen la base de producción verificada y un commit limpio. Preparan DLL/PDB y únicamente los tres JavaScript modificados con sus variantes comprimidas y manifiesto combinado. Conservan configuración, dependencias y activos ajenos; generan ZIP de reversión y verifican los archivos publicados. Desactivar el flujo antiguo solo después de reconciliar la migración y cerrar las escrituras antiguas con la publicación.
