-- Azure SQL: TokenLogs telemetry (aligns with R2KOptimizer Dapper INSERT).
-- Run once when provisioning the database.

IF OBJECT_ID(N'dbo.TokenLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TokenLogs (
        Id INT IDENTITY(1, 1) NOT NULL PRIMARY KEY,
        Kind NVARCHAR(20) NOT NULL CONSTRAINT DF_TokenLogs_Kind DEFAULT N'command',
        Command NVARCHAR(MAX) NOT NULL,
        OriginalTokens INT NOT NULL,
        OptimizedTokens INT NOT NULL,
        SavingsPercent DECIMAL(18, 2) NOT NULL,
        Timestamp DATETIME2 NOT NULL
    );
END
ELSE IF COL_LENGTH(N'dbo.TokenLogs', N'Timestamp') IS NULL
BEGIN
    ALTER TABLE dbo.TokenLogs ADD Timestamp DATETIME2 NOT NULL CONSTRAINT DF_TokenLogs_Timestamp DEFAULT SYSUTCDATETIME();
END

IF COL_LENGTH(N'dbo.TokenLogs', N'Kind') IS NULL
BEGIN
    ALTER TABLE dbo.TokenLogs ADD Kind NVARCHAR(20) NOT NULL CONSTRAINT DF_TokenLogs_Kind DEFAULT N'command';
END
