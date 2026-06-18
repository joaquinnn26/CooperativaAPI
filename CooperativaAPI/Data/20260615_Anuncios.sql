IF OBJECT_ID('dbo.PrioridadAnuncio', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PrioridadAnuncio (
        idPrioridadAnuncio INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PrioridadAnuncio PRIMARY KEY,
        Nombre NVARCHAR(20) NOT NULL,
        Orden INT NOT NULL,
        Activo BIT NOT NULL CONSTRAINT DF_PrioridadAnuncio_Activo DEFAULT (1)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PrioridadAnuncio WHERE Nombre = 'Alta')
    INSERT INTO dbo.PrioridadAnuncio (Nombre, Orden, Activo) VALUES ('Alta', 1, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.PrioridadAnuncio WHERE Nombre = 'Media')
    INSERT INTO dbo.PrioridadAnuncio (Nombre, Orden, Activo) VALUES ('Media', 2, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.PrioridadAnuncio WHERE Nombre = 'Baja')
    INSERT INTO dbo.PrioridadAnuncio (Nombre, Orden, Activo) VALUES ('Baja', 3, 1);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PrioridadAnuncio_Nombre')
BEGIN
    CREATE UNIQUE INDEX UX_PrioridadAnuncio_Nombre ON dbo.PrioridadAnuncio(Nombre);
END
GO

IF OBJECT_ID('dbo.Anuncio', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Anuncio (
        idAnuncio INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Anuncio PRIMARY KEY,
        Titulo NVARCHAR(120) NOT NULL,
        Contenido NVARCHAR(1000) NOT NULL,
        idPrioridadAnuncio INT NULL,
        FechaPublicacion DATETIME2 NOT NULL CONSTRAINT DF_Anuncio_FechaPublicacion DEFAULT (SYSUTCDATETIME()),
        FechaInicio DATETIME2 NULL,
        FechaFin DATETIME2 NULL,
        Activo BIT NOT NULL CONSTRAINT DF_Anuncio_Activo DEFAULT (1),
        Fijo BIT NOT NULL CONSTRAINT DF_Anuncio_Fijo DEFAULT (0),
        idEmpleado INT NULL
    );
END
GO

IF COL_LENGTH('dbo.Anuncio', 'idPrioridadAnuncio') IS NULL
BEGIN
    ALTER TABLE dbo.Anuncio ADD idPrioridadAnuncio INT NULL;
END
GO

IF COL_LENGTH('dbo.Anuncio', 'Prioridad') IS NOT NULL
BEGIN
    UPDATE a
    SET idPrioridadAnuncio = p.idPrioridadAnuncio
    FROM dbo.Anuncio a
    INNER JOIN dbo.PrioridadAnuncio p
        ON p.Nombre = CASE
            WHEN LOWER(LTRIM(RTRIM(a.Prioridad))) IN ('alta', 'high') THEN 'Alta'
            WHEN LOWER(LTRIM(RTRIM(a.Prioridad))) IN ('baja', 'low') THEN 'Baja'
            ELSE 'Media'
        END
    WHERE a.idPrioridadAnuncio IS NULL;
END
GO

UPDATE dbo.Anuncio
SET idPrioridadAnuncio = (
    SELECT TOP (1) idPrioridadAnuncio
    FROM dbo.PrioridadAnuncio
    WHERE Nombre = 'Media'
)
WHERE idPrioridadAnuncio IS NULL;
GO

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Anuncio')
      AND name = 'idPrioridadAnuncio'
      AND is_nullable = 1
)
BEGIN
    ALTER TABLE dbo.Anuncio ALTER COLUMN idPrioridadAnuncio INT NOT NULL;
END
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Anuncio_Prioridad')
BEGIN
    ALTER TABLE dbo.Anuncio DROP CONSTRAINT CK_Anuncio_Prioridad;
END
GO

IF COL_LENGTH('dbo.Anuncio', 'Prioridad') IS NOT NULL
BEGIN
    DECLARE @DefaultPrioridad SYSNAME;
    SELECT @DefaultPrioridad = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c
        ON c.default_object_id = dc.object_id
    WHERE dc.parent_object_id = OBJECT_ID('dbo.Anuncio')
      AND c.name = 'Prioridad';

    IF @DefaultPrioridad IS NOT NULL
        EXEC('ALTER TABLE dbo.Anuncio DROP CONSTRAINT [' + @DefaultPrioridad + ']');

    ALTER TABLE dbo.Anuncio DROP COLUMN Prioridad;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Anuncio_Fechas')
BEGIN
    ALTER TABLE dbo.Anuncio ADD CONSTRAINT CK_Anuncio_Fechas CHECK (FechaFin IS NULL OR FechaInicio IS NULL OR FechaFin >= FechaInicio);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Anuncio_PrioridadAnuncio')
BEGIN
    ALTER TABLE dbo.Anuncio ADD CONSTRAINT FK_Anuncio_PrioridadAnuncio
        FOREIGN KEY (idPrioridadAnuncio) REFERENCES dbo.PrioridadAnuncio(idPrioridadAnuncio);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Anuncio_Empleado')
BEGIN
    ALTER TABLE dbo.Anuncio ADD CONSTRAINT FK_Anuncio_Empleado
        FOREIGN KEY (idEmpleado) REFERENCES dbo.Empleado(idEmpleado);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Anuncio_Activo_Fechas')
BEGIN
    CREATE INDEX IX_Anuncio_Activo_Fechas ON dbo.Anuncio(Activo, FechaInicio, FechaFin, FechaPublicacion DESC);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Anuncio_Fijo')
BEGIN
    CREATE INDEX IX_Anuncio_Fijo ON dbo.Anuncio(Fijo, Activo, FechaPublicacion DESC);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Anuncio_Prioridad')
BEGIN
    CREATE INDEX IX_Anuncio_Prioridad ON dbo.Anuncio(idPrioridadAnuncio, Activo, FechaPublicacion DESC);
END
GO
