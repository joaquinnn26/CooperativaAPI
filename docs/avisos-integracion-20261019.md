# Avisos: integración y verificación del 19/10/2026

Se integró `origin/feature/emiliano` en `develop` del backend (merge `d909918`).
Se preservaron los cambios locales de Web.config, WebApiConfig.cs y del proyecto.

El archivo recibido `Cooperativa_Actualizar_Avisos_SQL2022.sql` estaba truncado:
comenzaba con un comentario sin cerrar y faltaban el encabezado, las validaciones
y la apertura de la transacción. También incluía `FILLFACTOR = 0`, rechazado por
SQL Server en estas sentencias, y omitía las opciones SET de los índices filtrados.
La versión corregida es `20261019_Avisos.sql`. El archivo de Downloads no se modificó.

## Base local

- Servidor: `DESKTOP-P2TCJ19\MSSQLSERVER01`.
- Base actualizada: `Cooperativa`, compatibilidad 160.
- Antes: no existía `dbo.Aviso`; las dependencias ya existían.
- Después: 15 columnas, 5 índices habilitados (incluida PK), 2 FK verificadas,
  10 restricciones CHECK verificadas y 0 avisos iniciales.
- Respaldo COPY_ONLY con CHECKSUM y RESTORE VERIFYONLY satisfactorio:
  `D:\Microsoft SQL Server\MSSQL16.MSSQLSERVER01\MSSQL\Backup\Cooperativa_antes_avisos_20261019.bak`.
- Copia aislada de pruebas conservada: `Cooperativa_AvisosTest_20261019`.
  Contiene fixtures; no debe usarse como base de la aplicación.

## Pruebas realizadas

- Backend compilado con MSBuild de Visual Studio 2019.
- 29 comprobaciones con HttpServer de Web API, filtros JWT reales, controladores
  y SQL Server en la copia aislada. Incluyen aprobación, propuesta, aceptación,
  rechazo, cancelación, deduplicación, contador, lectura, archivo independiente
  de lectura, historial, paginación y aislamiento entre dos socios.
- Se forzó un fallo de inserción del aviso en la copia para verificar que se
  revierte también la decisión del turno.
- 5 comprobaciones de solo lectura sobre la base local actualizada: conexiones,
  autenticación obligatoria, listado, contador e historial.
- 86 pruebas del frontend aprobadas y compilación Vite satisfactoria.
- Segunda ejecución de la migración en la copia: no modifica la tabla existente.

Comandos desde esta carpeta de trabajo:

```powershell
& './backend-snippets/avisos/Run-AvisosFlowTests.ps1'
& './backend-snippets/avisos/Run-AvisosFlowTests.ps1' -ReadOnly
```

Las pruebas completas solo permiten la copia aislada nombrada arriba; el modo
ReadOnly solo realiza GET sobre la API y consultas SELECT a Cooperativa.
Se prueba el pipeline HTTP dentro del proceso, no un despliegue IIS ni una sesión
visual de navegador. No se modificó la base de datos publicada ni se hizo push.

El productor automático de esta implementación corresponde a decisiones de
turnos. No genera avisos retroactivos ni avisos automáticos de otros módulos.
