# Foto obligatoria y aprobación de lecturas

## Lecturista

1. Inicia la hoja de ruta y registra la lectura real.
2. Adjunta una foto JPEG o PNG de hasta 2 MB donde se vean el medidor y su lectura. La API valida el contenido y guarda el archivo en `EvidenciaLecturaLaboral`, asociado a la medición y al usuario.
3. Guarda el valor y la foto juntos. No se aceptan direcciones de imágenes como reemplazo de la evidencia almacenada.
4. Puede guardar varias lecturas; se verifican las fotos antes de comenzar el guardado. Si una operación falla, se conservan los datos aún no guardados.
5. Firma y finaliza el recorrido cuando todas las visitas estén resueltas y todas las lecturas realizadas tengan evidencia almacenada. La API aplica la misma restricción.

Una visita no realizada se registra con su motivo y detalle; no se convierte en una lectura ni exige una foto de un valor que no se pudo obtener.

## Encargado

1. Abre Revisión de lecturas, busca la medición y consulta el resumen.
2. Visualiza la foto protegida por autenticación y permisos del área. Puede ampliarla.
3. Si hay un error, registra la corrección con un motivo activo del catálogo del área. El sistema recalcula el consumo y, cuando corresponde, el de la siguiente lectura.
4. Verifica la coincidencia entre foto, medidor y valor y aprueba mediante SweetAlert.
5. La aprobación guarda medición, versión exacta, valor, consumo, responsable y fecha UTC en `AprobacionLecturaLaboral`. La API comprueba el área, la existencia de evidencia y que la medición no haya cambiado durante la revisión.

`Medicion.VersionRevision` es un rowversion de SQL Server. Si se modifica la medición o el consumo de la siguiente, una aprobación de una versión anterior deja de ser vigente. Se conserva en el historial y se requiere una nueva revisión. La aprobación repetida de la misma versión es idempotente.

## Consulta desde tareas

Las tareas de categoría `Ruta` se conservan como consulta e historial del recorrido firmado. No permiten asignar, aprobar, devolver, rechazar, cerrar ni adjuntar archivos desde el flujo genérico de tareas; la API aplica la restricción.

El botón **Ir a Revisión de lecturas** abre únicamente las mediciones del recorrido. Cada visita realizada también permite abrir directamente su medición en el modal de revisión. Allí se consultan los valores actuales, las fotos, las correcciones y las aprobaciones. El resultado original firmado permanece visible como tal en el historial del recorrido.

En **Tareas - Agua Potable**, los trabajos operativos y el historial de recorridos tienen consultas separadas y paginación real. La búsqueda por título, número de trabajo, socio o responsable se aplica en la API antes de paginar. Los recorridos archivados no cuentan como trabajos operativos pendientes de los integrantes del equipo. Las asignaciones y permisos existentes se conservan.

## Exportación contable

La exportación contable del período y servicio elegidos se bloquea si alguna lectura exportable no tiene aprobación vigente. Informa la cantidad pendiente y no genera un lote parcial. Internet mantiene su intercambio propio, sin mediciones.

## Datos anteriores y compatibilidad

- No se crean aprobaciones históricas por suposición.
- Las hojas abiertas con lecturas ya guardadas sin evidencia permiten adjuntar la foto pendiente sin cambiar el valor. Solo puede hacerlo su lecturista y solo si no existe una foto almacenada.
- Las hojas cerradas conservan su estado y evidencia histórica. Una lectura histórica sin foto no se puede aprobar; requiere resolver la falta de evidencia mediante un procedimiento autorizado, no inventar una foto ni reabrir automáticamente la hoja.
- El registro directo antiguo `POST /api/medicion` indica usar la hoja de ruta; así no elude la foto obligatoria.
- La migración idempotente es `backend-snippets/laboral/20261015_AprobacionLecturas.sql`, para `Cooperativa`.

## Comprobación de esta entrega

Se compilaron frontend y API. No se ejecutaron pruebas de interacción con cuentas ni se alteraron lecturas para simular aprobaciones.
