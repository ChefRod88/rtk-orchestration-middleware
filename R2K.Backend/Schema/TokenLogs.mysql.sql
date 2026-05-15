-- MySQL 8.x: TokenLogs telemetry (aligns with R2KOptimizer Dapper INSERT).
-- Run once when provisioning the database.

CREATE TABLE IF NOT EXISTS TokenLogs (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
    Kind VARCHAR(20) NOT NULL DEFAULT 'command',
    Command TEXT NOT NULL,
    OriginalTokens INT NOT NULL,
    OptimizedTokens INT NOT NULL,
    SavingsPercent DECIMAL(5, 2) NOT NULL,
    StrategyUsed VARCHAR(32) NULL,
    OriginalContextTokens INT NULL,
    PrunedContextTokens INT NULL,
    PruningEfficiency DECIMAL(5, 2) NULL,
    SessionId CHAR(36) DEFAULT (UUID())
);

ALTER TABLE TokenLogs
    ADD COLUMN IF NOT EXISTS Kind VARCHAR(20) NOT NULL DEFAULT 'command' AFTER Timestamp;

ALTER TABLE TokenLogs
    ADD COLUMN IF NOT EXISTS StrategyUsed VARCHAR(32) NULL AFTER SavingsPercent,
    ADD COLUMN IF NOT EXISTS OriginalContextTokens INT NULL AFTER StrategyUsed,
    ADD COLUMN IF NOT EXISTS PrunedContextTokens INT NULL AFTER OriginalContextTokens,
    ADD COLUMN IF NOT EXISTS PruningEfficiency DECIMAL(5, 2) NULL AFTER PrunedContextTokens;
