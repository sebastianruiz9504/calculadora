# CRM Cotizador Interno

## Objetivo

Construir el CRM comercial dentro de CotizadorInterno, usando Dataverse como fuente de datos e integrándolo con la autenticación, los empleados, la Calculadora, Puntajes, aprovisionamiento y Contratos que ya existen.

La solución de Dataverse se llama `CotizadorInternoCRM`.

## Estado actual

- Aplicación: el módulo CRM, su navegación, repositorio Dataverse, fichas completas de cada registro y la integración con la Calculadora están implementados en el código local.
- Esquema: el repositorio contiene la solución Dataverse `CotizadorInternoCRM` exportada y desempaquetada con las tablas y campos descritos en este documento.
- Validación en vivo: el 24 de julio de 2026 se verificó `https://orgc79ca19c.crm2.dynamics.com/`; los metadatos, relaciones y claves del CRM estaban listos. La lectura confirmó 123 empresas CRM vinculadas uno a uno con los 123 clientes operativos, sin pendientes y sin crear leads artificiales.
- App Service: esta versión se desplegó el 24 de julio de 2026 en `calculadoradt` mediante OneDeploy `b960baa9b46c4a40bf14ab232fa29468`. El paquete definitivo pasó 221 pruebas automatizadas y la comprobación de 127 activos web; en producción pasaron `/healthz`, las redirecciones anónimas a Entra y la comparación exacta de `crm.css`, `crm.js` y `crm-detail.js`. El acceso autenticado estaba comprobado antes del reinicio; la repetición visual posterior quedó detenida únicamente en la confirmación interactiva de Windows Hello/FIDO solicitada por Entra.

## Principios

- `cr07a_crmempresa` es la base comercial de empresas del CRM.
- `cr07a_cliente` conserva su función operativa para facturación, soporte, contratos y demás módulos existentes; no se reutiliza como maestro general de prospectos.
- Una empresa CRM puede existir como lead sin registro en `cr07a_cliente`. Cuando se convierte en cliente activo, se vincula mediante `cr07a_clienteoperativo`.
- El ciclo de vida de empresa distingue `Lead`, `Cliente activo` e `Inactivo`; la inactivación conserva su historial.
- El acceso se administra con `cr07a_modulos`: `CRM Usuario` ve únicamente sus registros y `CRM Administrador` puede ver todos o consultar la vista de un usuario.
- Cada objeto CRM tiene propietario editable. El aislamiento se aplica en servidor a listas, fichas, búsquedas, métricas y mutaciones.
- Una oportunidad puede crearse sin Calculadora con contrato estimado, puntaje estimado, categoría y descripción breve, o puede iniciarse desde un escenario de la Calculadora.
- La Calculadora es la fuente de verdad para productos, cantidades, precios y demás líneas del escenario. El CRM no ofrece un editor paralelo.
- Los valores financieros de un negocio cotizado se recalculan en el servidor desde el escenario guardado; no se aceptan como autoridad los valores enviados por el navegador.
- Una oportunidad estimada conserva su valor estimado. Un negocio cotizado conserva tanto su puntaje como el valor total del contrato.
- La acción `Editar` del CRM abre la Calculadora con los identificadores del escenario y del negocio; los cambios regresan al CRM por la sincronización de la Calculadora.
- Un negocio solo puede pasar a `Ganado` después de que la Calculadora haya enviado y registrado una solicitud de aprovisionamiento.
- Un contrato se asociará por su identificador y solo si pertenece a la misma empresa del negocio.
- Todo envío comercial debe respetar consentimiento, exclusiones, bajas y trazabilidad.
- Las métricas deben indicar claramente si representan un total o solo la página visible.

## Modelo de empresas

- Las empresas nuevas creadas desde el CRM nacen como `Lead`.
- `Cliente activo` requiere el vínculo con el cliente operativo correspondiente en `cr07a_cliente`.
- `Inactivo` representa una empresa que ya no se gestiona comercialmente, sin borrar contactos, negocios, actividades ni historial.
- Los contactos pertenecen a `cr07a_crmempresa`; su ciclo de vida se ajusta al de la empresa y mantiene las exclusiones `No enviar correo` y `No llamar`.
- La separación permite prospectar empresas que aún no existen en los procesos operativos sin contaminar la base `cr07a_cliente`.

## Modelo de oportunidades y negocios

Los registros del pipeline pueden nacer como oportunidades estimadas manuales sin `ScenarioId` o desde la Calculadora.

- `Oportunidad estimada`: registro aún sin cotización definitiva; conserva contrato y puntaje estimados, categoría y descripción breve.
- `Negocio cotizado`: escenario calculado y guardado; registra el puntaje y el valor del contrato obtenidos por recálculo del escenario almacenado.
- Un escenario puede corresponder a un solo registro CRM. Puede evolucionar de oportunidad estimada a negocio cotizado, pero un negocio cotizado no vuelve a oportunidad.
- La edición se realiza en la Calculadora usando `scenarioId` y `crmDealId`; la sincronización actualiza el mismo registro CRM.
- Si cambian el puntaje o el valor del contrato después de haber solicitado aprovisionamiento, la evidencia anterior deja de ser válida y debe solicitarse nuevamente. Si el negocio ya estaba `Ganado`, la actualización lo reabre atómicamente en `Negociación`, limpia el cierre y registra el cambio en el historial.
- El pipeline tiene siete etapas: Prospección, Descubrimiento, Calificación, Propuesta, Negociación, Ganado y Perdido.
- `Ganado` exige que el registro sea un negocio cotizado y tenga evidencia de aprovisionamiento: indicador, fecha y `RequestId`.
- `Perdido` exige un motivo y registra el cambio en el historial de etapas.

## Fase 1 — Núcleo comercial local

Estado: implementado en el código local, representado en el esquema Dataverse exportado y desplegado en el App Service. La prueba técnica de producción está aprobada; falta únicamente repetir la revisión visual autenticada después de confirmar el acceso interactivo solicitado por Entra.

- Base independiente de empresas CRM con ciclo `Lead`, `Cliente activo` e `Inactivo`.
- Contactos asociados a empresa.
- Pipeline de negocios con siete etapas.
- Creación de oportunidades estimadas manuales o con Calculadora; actualización de negocios cotizados desde la Calculadora.
- Visualización de puntaje y valor del contrato en negocios cotizados.
- Enlace `Editar en calculadora` para cada registro con escenario.
- Bloqueo de `Ganado` hasta registrar la solicitud de aprovisionamiento.
- Motivo obligatorio al marcar un negocio como perdido.
- Historial atómico de cambios de etapa.
- Actividades: llamadas, reuniones, correos, tareas, notas y ofertas.
- Estados de actividad: planeada, completada y cancelada.
- Indicadores de llamadas, reuniones y ofertas completadas.
- Búsqueda y paginación independiente.
- Hojas de vida navegables para empresas, contactos, negocios y actividades.
- Información general y auditoría de propietario, creación y última modificación.
- Objetos asociados paginados: contactos, empresas, negocios, actividades e historial de etapas según el tipo de registro.
- Creación contextual desde cada ficha: contactos, actividades y oportunidades estimadas dentro del CRM; los negocios con escenario se editan en la Calculadora.
- Navegación directa a la ficha del contacto o actividad después de crearla.
- Validación de coherencia de las asociaciones antes de mostrar contactos, negocios o actividades; una relación cruzada entre empresas se rechaza en vez de mezclarse en la ficha.
- El usuario autorizado del CRM puede abrir desde un negocio el escenario original de otro comercial, editarlo sin duplicarlo y conservar su propietario.
- Los usuarios con acceso únicamente a la Calculadora pueden seguir solicitando aprovisionamiento, pero esa acción no les concede escritura implícita en el CRM.
- Fechas y horas comerciales presentadas explícitamente en la zona `America/Bogota`.
- Interfaz responsive alineada con el módulo de Contratos.
- Acceso inicial asignado únicamente a `sruiz@digitaltechcolombia.com`.

Tablas CRM:

- `cr07a_crmempresa`
- `cr07a_crmcontacto`
- `cr07a_crmnegocio`
- `cr07a_crmactividad`
- `cr07a_crmhistorialetapa`

Dependencias existentes, no sustituidas por el CRM:

- `cr07a_cliente`, vinculada opcionalmente desde `cr07a_crmempresa.cr07a_clienteoperativo`.
- `cr07a_negocioscomerciales`, que conserva los escenarios de la Calculadora.
- Las tablas y snapshots existentes de Puntajes, aprovisionamiento y Contratos.

## Fase 2 — Conexión comercial completa

Prioridad siguiente.

- Vista de solo lectura de productos y servicios del escenario, sin duplicar su edición en CRM.
- Asociación versionada con el snapshot inmutable de Puntajes.
- Acceso a la oferta mediante el servicio autorizado existente, sin guardar URLs temporales.
- Asociación explícita con Contratos.
- Vista cronológica única por empresa, contacto y negocio.
- Propietario comercial, equipos, territorios y reglas de reasignación.
- Tareas, recordatorios y próximos pasos vencidos.
- Importación controlada, detección de duplicados y combinación de contactos.

## Fase 3 — Marketing

- Plantillas reutilizables y editor de contenido.
- Campañas y listas dinámicas o estáticas.
- Segmentación por empresa, ciclo de vida, negocio, actividad y preferencias.
- Vista previa y envío de prueba.
- Programación y procesamiento por lotes.
- Exclusión obligatoria por `No enviar correo`.
- Centro de preferencias y enlace de cancelación de suscripción.
- Lista de supresión para bajas, rebotes y quejas.
- Métricas de entrega, rebote, apertura y clic cuando el proveedor lo permita.
- Límites de frecuencia para evitar saturar contactos.

Antes de activar envíos reales se debe definir:

- buzón o servicio remitente;
- dominio de seguimiento;
- SPF, DKIM y DMARC;
- límites diarios y por minuto;
- política de consentimiento y conservación;
- tratamiento de bajas y rebotes.

## Fase 4 — Secuencias

- Secuencias con pasos de correo, tarea, llamada y espera.
- Inscripción manual o por reglas.
- Pausa automática cuando el contacto responde, se da de baja o cambia de etapa.
- Horarios laborales y festivos de Colombia.
- Asignación de tareas al propietario.
- Reintentos controlados e idempotencia.
- Historial de cada paso y motivo de salida.
- Límites por contacto, dominio y remitente.

## Fase 5 — Reportería y gestión

- Conversión de empresas `Lead` → `Cliente activo` y de contactos Lead → MQL → SQL → Cliente.
- Velocidad y tiempo promedio por etapa.
- Pipeline ponderado y forecast por fecha de cierre.
- Tasa Ganado/Perdido y motivos de pérdida.
- Actividad por comercial y cumplimiento de metas.
- Ingresos por línea de negocio, empresa y periodo.
- Atribución básica de campañas.
- Calidad de datos, registros sin propietario y seguimientos vencidos.
- Exportación a Excel y vistas preparadas para Power BI.

## Funcionalidades complementarias

- Campos personalizados administrables.
- Lead scoring y priorización.
- Reglas de distribución de leads.
- Playbooks comerciales.
- Catálogo de productos y listas de precios.
- Adjuntos y documentos por negocio.
- Auditoría de cambios y bitácora de integraciones.
- Webhooks o API para formularios externos.
- Panel de salud de automatizaciones.
- Gestión de cuotas, objetivos y forecast.

## Criterio de salida a producción

- Confirmación explícita del entorno Dataverse objetivo y de la solución antes de cualquier cambio de metadatos o datos.
- Export de `CotizadorInternoCRM` actualizado y versionado.
- Compilación sin errores y pruebas automatizadas verdes.
- Despliegue controlado de la versión aprobada y prueba autenticada en el App Service.
- Acceso directo permitido solo a usuarios con el módulo CRM.
- CRUD controlado con lectura posterior en Dataverse.
- Verificación de que una empresa lead no crea automáticamente un `cr07a_cliente`.
- Verificación de que oportunidades y negocios solo se crean desde la Calculadora.
- Verificación de puntaje y valor del contrato contra el escenario guardado.
- Verificación de que `Ganado` permanece bloqueado antes del aprovisionamiento y se habilita solo después de registrar su evidencia.
- Verificación de que `Editar en calculadora` abre y actualiza el escenario correcto.
- Validación visual en escritorio y móvil.
- Sin registros de prueba ni envíos comerciales accidentales.
