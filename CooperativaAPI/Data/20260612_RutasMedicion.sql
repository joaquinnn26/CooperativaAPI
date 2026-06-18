IF OBJECT_ID('dbo.RutaMedicion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RutaMedicion (
        idRutaMedicion INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RutaMedicion PRIMARY KEY,
        idEmpleado INT NOT NULL,
        Nombre NVARCHAR(120) NOT NULL,
        Fecha DATE NOT NULL,
        Estado NVARCHAR(20) NOT NULL CONSTRAINT DF_RutaMedicion_Estado DEFAULT ('Pendiente'),
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_RutaMedicion_FechaCreacion DEFAULT (SYSUTCDATETIME()),
        FechaInicio DATETIME2 NULL,
        FechaFinalizacion DATETIME2 NULL,
        FirmaDigital VARBINARY(MAX) NULL,
        FirmaMimeType NVARCHAR(50) NULL,
        Observacion NVARCHAR(500) NULL
    );
END;
GO

IF OBJECT_ID('dbo.RutaMedicionDetalle', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RutaMedicionDetalle (
        idRutaMedicionDetalle INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RutaMedicionDetalle PRIMARY KEY,
        idRutaMedicion INT NOT NULL,
        idMedidor INT NOT NULL,
        Orden INT NOT NULL,
        Estado NVARCHAR(30) NOT NULL CONSTRAINT DF_RutaMedicionDetalle_Estado DEFAULT ('Pendiente'),
        idMedicion INT NULL,
        LecturaInformada DECIMAL(18,2) NULL,
        FechaHoraLectura DATETIME2 NULL,
        Observacion NVARCHAR(500) NULL,
        Latitud DECIMAL(10,7) NULL,
        Longitud DECIMAL(10,7) NULL,
        FotoMedidorUrl NVARCHAR(500) NULL
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_RutaMedicion_Empleado')
BEGIN
    ALTER TABLE dbo.RutaMedicion ADD CONSTRAINT FK_RutaMedicion_Empleado FOREIGN KEY (idEmpleado) REFERENCES dbo.Empleado(idEmpleado);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_RutaMedicionDetalle_Ruta')
BEGIN
    ALTER TABLE dbo.RutaMedicionDetalle ADD CONSTRAINT FK_RutaMedicionDetalle_Ruta FOREIGN KEY (idRutaMedicion) REFERENCES dbo.RutaMedicion(idRutaMedicion);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_RutaMedicionDetalle_Medidor')
BEGIN
    ALTER TABLE dbo.RutaMedicionDetalle ADD CONSTRAINT FK_RutaMedicionDetalle_Medidor FOREIGN KEY (idMedidor) REFERENCES dbo.Medidor(idMedidor);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_RutaMedicionDetalle_Medicion')
BEGIN
    ALTER TABLE dbo.RutaMedicionDetalle ADD CONSTRAINT FK_RutaMedicionDetalle_Medicion FOREIGN KEY (idMedicion) REFERENCES dbo.Medicion(idMedicion);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_RutaMedicion_Estado')
BEGIN
    ALTER TABLE dbo.RutaMedicion ADD CONSTRAINT CK_RutaMedicion_Estado CHECK (Estado IN ('Pendiente', 'EnCurso', 'Finalizada', 'Cancelada'));
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_RutaMedicionDetalle_Estado')
BEGIN
    ALTER TABLE dbo.RutaMedicionDetalle ADD CONSTRAINT CK_RutaMedicionDetalle_Estado CHECK (Estado IN ('Pendiente', 'Medido', 'NoAcceso', 'MedidorDanado', 'Observado'));
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RutaMedicion_Empleado_Fecha')
BEGIN
    CREATE INDEX IX_RutaMedicion_Empleado_Fecha ON dbo.RutaMedicion(idEmpleado, Fecha);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RutaMedicion_Estado_Fecha')
BEGIN
    CREATE INDEX IX_RutaMedicion_Estado_Fecha ON dbo.RutaMedicion(Estado, Fecha);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RutaMedicionDetalle_Ruta_Orden')
BEGIN
    CREATE INDEX IX_RutaMedicionDetalle_Ruta_Orden ON dbo.RutaMedicionDetalle(idRutaMedicion, Orden);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_RutaMedicionDetalle_Ruta_Medidor')
BEGIN
    CREATE UNIQUE INDEX UX_RutaMedicionDetalle_Ruta_Medidor ON dbo.RutaMedicionDetalle(idRutaMedicion, idMedidor);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RutaMedicionDetalle_Medidor_Estado')
BEGIN
    CREATE INDEX IX_RutaMedicionDetalle_Medidor_Estado ON dbo.RutaMedicionDetalle(idMedidor, Estado);
END;
GO
