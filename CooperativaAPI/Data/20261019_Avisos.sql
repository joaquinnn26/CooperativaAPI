-- Reconstruido del archivo recibido: faltaba el encabezado y las validaciones.
-- Crea exclusivamente Aviso y sus restricciones; no copia ni modifica datos previos.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.Aviso', N'U') IS NOT NULL
    BEGIN
        PRINT N'dbo.Aviso ya existe; no se modifica.';
        COMMIT TRANSACTION;
        RETURN;
    END;
    IF OBJECT_ID(N'dbo.UsuarioSistema',N'U') IS NULL OR OBJECT_ID(N'dbo.EventoOperativo',N'U') IS NULL
        THROW 51001, N'Faltan UsuarioSistema o EventoOperativo.', 1;
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.UsuarioSistema') AND name=N'idUsuarioSistema' AND system_type_id=56)
        OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.EventoOperativo') AND name=N'idEventoOperativo' AND system_type_id=127)
        THROW 51002, N'Los identificadores de las dependencias no tienen los tipos esperados.', 1;
    CREATE TABLE [dbo].[Aviso]
    (
        [idAviso] bigint IDENTITY(1,1) NOT NULL,
        [idUsuarioSistema] int NOT NULL,
        [Ambito] nvarchar(10) COLLATE Modern_Spanish_CI_AS NOT NULL,
        [TipoAviso] nvarchar(80) COLLATE Modern_Spanish_CI_AS NOT NULL,
        [Titulo] nvarchar(120) COLLATE Modern_Spanish_CI_AS NOT NULL,
        [Mensaje] nvarchar(1000) COLLATE Modern_Spanish_CI_AS NOT NULL,
        [Prioridad] tinyint NOT NULL
            CONSTRAINT [DF_Aviso_Prioridad] DEFAULT ((2)),
        [TipoOrigen] nvarchar(60) COLLATE Modern_Spanish_CI_AS NOT NULL,
        [idOrigen] nvarchar(80) COLLATE Modern_Spanish_CI_AS NOT NULL,
        [ClaveEvento] nvarchar(256) COLLATE Modern_Spanish_CI_AS NOT NULL,
        [FechaCreacionUtc] datetime2(3) NOT NULL
            CONSTRAINT [DF_Aviso_FechaCreacionUtc] DEFAULT (sysutcdatetime()),
        [FechaLecturaUtc] datetime2(3) NULL,
        [FechaArchivoUtc] datetime2(3) NULL,
        [RequiereAccion] bit NOT NULL
            CONSTRAINT [DF_Aviso_RequiereAccion] DEFAULT ((0)),
        [idEventoOperativo] bigint NULL,

        CONSTRAINT [PK_Aviso] PRIMARY KEY CLUSTERED ([idAviso] ASC)
            WITH (PAD_INDEX = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
            ON [PRIMARY],

        CONSTRAINT [CK_Aviso_Ambito]
            CHECK ([Ambito]=N'Laboral' OR [Ambito]=N'Personal'),
        CONSTRAINT [CK_Aviso_ClaveEvento]
            CHECK (len(ltrim(rtrim([ClaveEvento])))>(0)),
        CONSTRAINT [CK_Aviso_FechaArchivo]
            CHECK ([FechaArchivoUtc] IS NULL OR [FechaArchivoUtc]>=[FechaCreacionUtc]),
        CONSTRAINT [CK_Aviso_FechaLectura]
            CHECK ([FechaLecturaUtc] IS NULL OR [FechaLecturaUtc]>=[FechaCreacionUtc]),
        CONSTRAINT [CK_Aviso_idOrigen]
            CHECK (len(ltrim(rtrim([idOrigen])))>(0)),
        CONSTRAINT [CK_Aviso_Mensaje]
            CHECK (len(ltrim(rtrim([Mensaje])))>(0)),
        CONSTRAINT [CK_Aviso_Prioridad]
            CHECK ([Prioridad]=(3) OR [Prioridad]=(2) OR [Prioridad]=(1)),
        CONSTRAINT [CK_Aviso_TipoAviso]
            CHECK (len(ltrim(rtrim([TipoAviso])))>(0)),
        CONSTRAINT [CK_Aviso_TipoOrigen]
            CHECK (len(ltrim(rtrim([TipoOrigen])))>(0)),
        CONSTRAINT [CK_Aviso_Titulo]
            CHECK (len(ltrim(rtrim([Titulo])))>(0)),

        CONSTRAINT [FK_Aviso_UsuarioSistema]
            FOREIGN KEY ([idUsuarioSistema])
            REFERENCES [dbo].[UsuarioSistema] ([idUsuarioSistema])
            ON DELETE NO ACTION ON UPDATE NO ACTION,
        CONSTRAINT [FK_Aviso_EventoOperativo]
            FOREIGN KEY ([idEventoOperativo])
            REFERENCES [dbo].[EventoOperativo] ([idEventoOperativo])
            ON DELETE NO ACTION ON UPDATE NO ACTION
    ) ON [PRIMARY];

    CREATE UNIQUE NONCLUSTERED INDEX [UX_Aviso_DestinatarioEvento]
        ON [dbo].[Aviso] ([idUsuarioSistema] ASC, [ClaveEvento] ASC)
        WITH (PAD_INDEX = OFF, IGNORE_DUP_KEY = OFF,
              ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
        ON [PRIMARY];

    CREATE NONCLUSTERED INDEX [IX_Aviso_Bandeja]
        ON [dbo].[Aviso] ([idUsuarioSistema] ASC, [Ambito] ASC,
                         [FechaCreacionUtc] DESC, [idAviso] DESC)
        INCLUDE ([FechaLecturaUtc], [FechaArchivoUtc], [Prioridad],
                 [RequiereAccion], [TipoAviso], [TipoOrigen], [idOrigen])
        WITH (PAD_INDEX = OFF, IGNORE_DUP_KEY = OFF,
              ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
        ON [PRIMARY];

    CREATE NONCLUSTERED INDEX [IX_Aviso_NoLeidos]
        ON [dbo].[Aviso] ([idUsuarioSistema] ASC, [Ambito] ASC,
                         [FechaCreacionUtc] DESC, [idAviso] DESC)
        INCLUDE ([FechaLecturaUtc], [FechaArchivoUtc], [Prioridad], [RequiereAccion])
        WHERE ([FechaLecturaUtc] IS NULL AND [FechaArchivoUtc] IS NULL)
        WITH (PAD_INDEX = OFF, IGNORE_DUP_KEY = OFF,
              ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
        ON [PRIMARY];

    CREATE NONCLUSTERED INDEX [IX_Aviso_EventoOperativo]
        ON [dbo].[Aviso] ([idEventoOperativo] ASC)
        WHERE ([idEventoOperativo] IS NOT NULL)
        WITH (PAD_INDEX = OFF, IGNORE_DUP_KEY = OFF,
              ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
        ON [PRIMARY];

    COMMIT TRANSACTION;
    PRINT N'Aviso creada con claves, restricciones e indices.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
