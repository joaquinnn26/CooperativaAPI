IF COL_LENGTH('dbo.Persona', 'NombreContactoEmergencia') IS NULL
    ALTER TABLE dbo.Persona ADD NombreContactoEmergencia varchar(120) NULL;

IF COL_LENGTH('dbo.Persona', 'TelefonoContactoEmergencia') IS NULL
    ALTER TABLE dbo.Persona ADD TelefonoContactoEmergencia varchar(25) NULL;
