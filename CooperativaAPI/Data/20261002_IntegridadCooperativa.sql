-- Limpieza de restricciones redundantes verificada contra Cooperativa 2026-10-02.
-- Aplicar primero a una restauraci?n de prueba. No modifica filas de negocio.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Las tres restricciones deben seguir siendo la misma clave ?nica no filtrada.
IF (SELECT COUNT(*) FROM sys.indexes i
    WHERE i.object_id=OBJECT_ID(N'dbo.Medidor') AND i.is_unique=1 AND i.has_filter=0
      AND i.name IN (N'UQ_Medidor_Numero',N'UQ_Numero',N'UQ_NumeroSerie')
      AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=1
      AND EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                  WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=1 AND c.name=N'Numero')) NOT IN (1,3)
    THROW 51020, 'Las restricciones ?nicas de Medidor.Numero cambiaron; revisar antes de continuar.', 1;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Medidor') AND name=N'UQ_Numero')
   AND (SELECT COUNT(*) FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.Medidor') AND i.is_unique=1 AND i.has_filter=0
        AND i.name IN (N'UQ_Medidor_Numero',N'UQ_Numero',N'UQ_NumeroSerie'))<>3
    THROW 51023, 'Conjunto parcial de restricciones ?nicas; revisar manualmente.', 1;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Medidor') AND name=N'UQ_NumeroSerie')
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Medidor') AND name=N'UQ_Numero')
    THROW 51024, 'Conjunto parcial de restricciones ?nicas; revisar manualmente.', 1;

-- Mantener UQ_Medidor_Numero como nombre canonico.
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Medidor') AND name=N'UQ_Numero')
    ALTER TABLE dbo.Medidor DROP CONSTRAINT UQ_Numero;
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Medidor') AND name=N'UQ_NumeroSerie')
    ALTER TABLE dbo.Medidor DROP CONSTRAINT UQ_NumeroSerie;

-- Conservar FK_UsuarioSistema_Socio (NO ACTION). Validar que todas las FK
-- afectadas sean exactamente idSocio -> Socio.idSocio, sin claves compuestas.
IF EXISTS (
    SELECT 1 FROM sys.foreign_keys f
    WHERE f.parent_object_id=OBJECT_ID(N'dbo.UsuarioSistema')
      AND EXISTS (SELECT 1 FROM sys.foreign_key_columns fc JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id
                  WHERE fc.constraint_object_id=f.object_id AND pc.name=N'idSocio')
      AND (f.name NOT IN (N'FK_UsuarioSistema_Socio',N'FK_UsuarioSistema_Socios',N'FK__UsuarioSi__idSoc__5BE2A6F2')
           OR f.referenced_object_id<>OBJECT_ID(N'dbo.Socio')
           OR (SELECT COUNT(*) FROM sys.foreign_key_columns fc WHERE fc.constraint_object_id=f.object_id)<>1
           OR NOT EXISTS (SELECT 1 FROM sys.foreign_key_columns fc
                          JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id
                          JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id
                          WHERE fc.constraint_object_id=f.object_id AND pc.name=N'idSocio' AND rc.name=N'idSocio')
           OR (f.name=N'FK_UsuarioSistema_Socio' AND f.delete_referential_action<>0))
) THROW 51021, 'Las FK de UsuarioSistema.idSocio cambiaron; revisar manualmente.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.UsuarioSistema') AND name=N'FK_UsuarioSistema_Socio'
               AND referenced_object_id=OBJECT_ID(N'dbo.Socio') AND delete_referential_action=0)
    THROW 51022, 'No existe la FK can?nica no cascada FK_UsuarioSistema_Socio.', 1;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.UsuarioSistema') AND name=N'FK_UsuarioSistema_Socios')
    ALTER TABLE dbo.UsuarioSistema DROP CONSTRAINT FK_UsuarioSistema_Socios;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.UsuarioSistema') AND name=N'FK__UsuarioSi__idSoc__5BE2A6F2')
    ALTER TABLE dbo.UsuarioSistema DROP CONSTRAINT [FK__UsuarioSi__idSoc__5BE2A6F2];

COMMIT TRANSACTION;
GO

-- Validaci?n posterior (debe devolver una UQ y una FK NO_ACTION).
SELECT i.name, c.name AS Columna, i.is_unique, i.has_filter
FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE i.object_id=OBJECT_ID(N'dbo.Medidor') AND i.is_unique=1 AND ic.key_ordinal=1 AND c.name=N'Numero';
SELECT f.name, f.delete_referential_action_desc
FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
WHERE f.parent_object_id=OBJECT_ID(N'dbo.UsuarioSistema') AND c.name=N'idSocio';
