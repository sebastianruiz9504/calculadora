> Sustituido por [Centros de costo compartidos](../shared_cost_centers/README.md).
> Las escrituras de opciones locales estan deshabilitadas; los campos anteriores son respaldo historico.

# Centro de costo de Productos COPIERS

En Gerencia, **Productos COPIERS** abre la tabla `cr07a_productoscopiers`.
La columna **Centro de costo** (`cr07a_centrocosto`) es una opcion local con
101-00, 102-00, 103-00, 104-00 y 105-00, todas de Ventas. El predeterminado es
**102-00 - Ventas**. La inicializacion completa solo nulos para conservar las
selecciones manuales posteriores.

La vista activa muestra el campo despues de Cliente. Tambien esta en la vista
inactiva y en el formulario principal, editable despues del cliente. El alcance
es esquema, interfaz y datos; no crea ni modifica automatizaciones.

Se usa la organizacion Digital Tech `https://orgc79ca19c.crm2.dynamics.com`,
la solucion `CopiersCentrosCosto` y el publicador existente `Cr44b61` (`cr07a`).
El acceso reutiliza la sesion Azure CLI con tenant explicito. Las dependencias
estan en `scripts/cloud_cost_centers/requirements.txt`.

```powershell
python scripts/copiers_cost_centers/manage.py plan
python scripts/copiers_cost_centers/manage.py schema
python scripts/copiers_cost_centers/manage.py ui
python scripts/copiers_cost_centers/manage.py initialize
python scripts/copiers_cost_centers/manage.py verify
python scripts/copiers_cost_centers/manage.py export
```

`verify` comprueba la carga, el formulario, las vistas y la conservacion de los
campos editables previos. Los respaldos y datos privados quedan en
`tmp/copiers-cost-centers-20261127/`, excluido de Git. La solucion se exporta y
desempaqueta en `solutions/CopiersCentrosCosto`; solo incluye metadatos.

Verificado el 27 de noviembre de 2026: 42 productos con 102-00, un formulario y
dos vistas publicados, valores previos conservados. La vista y el selector de
las cinco opciones se comprobaron en la sesion autenticada de Gerencia. La
evidencia sin datos de clientes esta en
`solutions/CopiersCentrosCosto/Validation/cost-centers-20261127.json`.

Para revertir la presentacion, usar los respaldos de formularios y vistas sin
sobrescribir otros cambios posteriores. Conservar la columna y sus valores
evita perder las selecciones manuales.
