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
    SessionId CHAR(36) DEFAULT (UUID())
);

ALTER TABLE TokenLogs
    ADD COLUMN IF NOT EXISTS Kind VARCHAR(20) NOT NULL DEFAULT 'command' AFTER Timestamp;
