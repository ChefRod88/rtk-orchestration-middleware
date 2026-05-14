-- MySQL 8.x: TokenLogs telemetry (aligns with R2KOptimizer Dapper INSERT).
-- Run once when provisioning the database.

CREATE TABLE IF NOT EXISTS TokenLogs (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    Command TEXT NOT NULL,
    OriginalTokens INT NOT NULL,
    OptimizedTokens INT NOT NULL,
    SavingsPercent DECIMAL(18, 2) NOT NULL,
    Timestamp DATETIME(3) NOT NULL
);
