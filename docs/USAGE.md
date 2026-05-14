# R2K Usage Guide

This guide documents the current application purpose, the work completed in this Codespace, and the exact ways to test and use the optimizer from Cursor CLI / Agent mode.

## Purpose

R2K is a command optimizer and telemetry middleware. It sits between a developer or agent and shell commands such as `git status`, `npm install`, `dotnet build`, or `curl ...`.

The intended loop is:

1. A command is sent to the optimizer HTTP endpoint.
2. The endpoint returns an optimized command and metrics.
3. The local `rtk` CLI executes the optimized command in the Codespace shell.
4. Token metrics are written to MySQL in AWS RDS.

R2K optimizes terminal commands. It does not automatically rewrite Cursor's natural-language prompts to the model. In Cursor Agent mode, it helps when the commands the agent runs go through `rtk`, aliases, or the MCP tool.

## Current Runtime Paths

There are two backend paths in this repository:

| Path | Runtime | Status |
|------|---------|--------|
| `R2K.Backend/` | Azure Functions isolated worker | Original backend. Now uses MySQL-compatible telemetry support in code. |
| `R2K.Backend.AWS/` | AWS Lambda + API Gateway | Current tested path. Deployed as `R2KOptimizer` and reachable through API Gateway. |

The AWS path was validated with:

```bash
curl -i -X POST 'https://awv3cnqcx0.execute-api.us-east-2.amazonaws.com/OptimizeCommand' \
  -H 'Content-Type: application/json' \
  -d '{"command":"git status"}'
```

Expected successful response:

```json
{
  "command_executed": "git status",
  "metrics": {
    "tokens_original": 2,
    "tokens_optimized": 2,
    "savings_percentage": 0.0
  }
}
```

## What Was Completed

- Added `MySqlConnector` to the backend and created `R2K.DbProbe` for RDS connectivity checks.
- Downloaded and bundled the AWS RDS `global-bundle.pem` CA bundle for TLS checks.
- Created `R2K.Backend/Schema/TokenLogs.mysql.sql` for the MySQL telemetry table.
- Migrated the Azure backend telemetry code from `SqlConnection` to `MySqlConnection`.
- Added AWS Lambda support under `R2K.Backend.AWS/`.
- Added Lambda/API Gateway connector packages:
  - `Amazon.Lambda.APIGatewayEvents`
  - `Dapper`
  - `MySqlConnector`
  - `Newtonsoft.Json`
- Retargeted the AWS Lambda project to `net8.0` / `dotnet8`.
- Deployed the AWS Lambda function as `R2KOptimizer`.
- Created API Gateway route `POST /OptimizeCommand`.
- Verified API Gateway returns `HTTP 200`.
- Verified RDS telemetry insert into `r2k_telemetry.TokenLogs`.

## Important Security Notes

Secrets must not be committed.

Files that may contain local secrets:

| File | Git status |
|------|------------|
| `R2K.Backend/local.settings.json` | Ignored by `.gitignore` |
| `.cursor/mcp.json` | Ignored by `.gitignore` |
| `~/.aws/credentials` | Outside repo |

Rotate any AWS keys or database passwords that were pasted into chat or logs. Use AWS Secrets Manager, Lambda environment variables, or Codespaces secrets for future values.

## Codebase Review

### `R2K.CLI`

`R2K.CLI/Program.cs` is the local command wrapper. It:

1. Reads `RTK_API_URL`.
2. Optionally reads `RTK_FUNCTION_KEY`.
3. Sends `{ "command": "<full command>" }` to the optimizer endpoint.
4. Reads `command_executed`.
5. Executes the optimized command locally through `/bin/bash -c`.
6. Prints metric output.

Use this binary as `/usr/local/bin/rtk`.

### `R2K.Backend.AWS`

`R2K.Backend.AWS/Function.cs` is the current AWS Lambda handler.

It:

1. Accepts an API Gateway request.
2. Reads `request.Body`.
3. Extracts `command`.
4. Applies a simple whitespace optimization.
5. Calculates approximate token savings using a 4-character heuristic.
6. Inserts telemetry into MySQL table `TokenLogs`.
7. Returns JSON with `command_executed` and metrics.

Required Lambda environment variable:

```text
SqlConnectionString=Server=database-rtk.cxkyikqe45yz.us-east-2.rds.amazonaws.com;Port=3306;Database=r2k_telemetry;User ID=MasterRtk;Password=<password>;SslMode=Required;Connection Timeout=30;
```

### `R2K.Backend`

`R2K.Backend/` is the Azure Functions backend. It has richer optimizer logic than the AWS Lambda starter:

- `CommandOptimizationService` counts tokens using `Tiktoken`.
- `CliCommandOptimizer` normalizes whitespace and removes adjacent duplicate flags.
- `R2KOptimizer` exposes `OptimizeCommand`.
- `MySqlTelemetryConnection` builds a MySQL connection string from `SqlConnectionString` plus `DB_PASSWORD`.

### `R2K.DbProbe`

`R2K.DbProbe` is a diagnostic console app for RDS. It:

- Resolves the RDS hostname.
- Probes TCP reachability on port `3306`.
- Loads `DB_PASSWORD` from the environment, then falls back to `R2K.Backend/local.settings.json`.
- Runs `SELECT VERSION();`.

Expected success:

```text
TCP probe: port is reachable from this host.
8.4.8
```

### MySQL Tables

The active telemetry table for Lambda is:

```text
r2k_telemetry.TokenLogs
```

The table shape used by the current Lambda:

```sql
CREATE TABLE IF NOT EXISTS TokenLogs (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
    Command TEXT NOT NULL,
    OriginalTokens INT NOT NULL,
    OptimizedTokens INT NOT NULL,
    SavingsPercent DECIMAL(5, 2) NOT NULL,
    SessionId CHAR(36) DEFAULT (UUID())
);
```

There is also a lowercase `tokenlogs` table that was created during an earlier test. The current Lambda does not use it.

## Codespaces Setup

From the repo root:

```bash
cd /workspaces/r2k-orchestration-middleware
```

New Codespaces run `scripts/setup-r2k-codespace.sh` from `.devcontainer/devcontainer.json`. To apply the same setup in an existing terminal, run:

```bash
bash scripts/setup-r2k-codespace.sh
source ~/.bashrc
```

The setup script refreshes `/usr/local/bin/rtk`, sets the AWS API Gateway endpoint, and adds these aliases:

```bash
alias agent='rtk agent'
alias git='rtk git'
alias npm='rtk npm'
alias npx='rtk npx'
```

Verify:

```bash
type agent
type git
```

Test direct CLI use:

```bash
rtk git status
```

The command output appears first, then the RTK savings block:

```text
<git status output>

======================================
RTK SAVINGS
Original tokens: 2
Optimized tokens: 2
Tokens saved: 0
Savings: 0%
======================================
```

## Make Cursor Agent Terminal Commands Use R2K

Cursor Agent mode cannot be forced to rewrite every natural-language prompt through this app. What you can do is make common terminal commands route through `rtk` so agent-run shell commands are optimized before execution.

The requested `agent` alias is installed by `scripts/setup-r2k-codespace.sh`:

```bash
alias agent='rtk agent'
```

Recommended default command aliases:

```bash
alias git='rtk git'
alias npm='rtk npm'
alias npx='rtk npx'
```

Be careful aliasing `dotnet`, `curl`, and `aws` globally because deployment tools and scripts may expect exact command behavior. If an alias causes trouble, bypass it:

```bash
command git status
/usr/bin/git status
command dotnet build
```

The project rule `.cursor/rules/r2k-agent-optimizer.mdc` also instructs agents to show the answer or command result first, then show RTK savings underneath.

## Cursor MCP Option

The MCP server exposes a tool named `run_rtk_command`. It lets Cursor call `rtk` through MCP instead of relying only on shell aliases.

Build the MCP server:

```bash
cd /workspaces/r2k-orchestration-middleware/extras/mcp-rtk-server
npm ci
npm run build
```

Example Cursor MCP configuration:

```json
{
  "mcpServers": {
    "r2k-optimizer": {
      "command": "node",
      "args": ["/workspaces/r2k-orchestration-middleware/extras/mcp-rtk-server/dist/index.js"],
      "env": {
        "RTK_API_URL": "https://awv3cnqcx0.execute-api.us-east-2.amazonaws.com/OptimizeCommand",
        "RTK_CLI_PATH": "/usr/local/bin/rtk"
      }
    }
  }
}
```

Save real MCP config in `.cursor/mcp.json` or `~/.cursor/mcp.json`. Do not commit secrets there.

## Test Checklist

### 1. Build Everything

```bash
dotnet build R2K.Backend/R2K.Backend.csproj
dotnet build R2K.Backend.AWS/R2K.Backend.AWS.csproj
dotnet build R2K.CLI/R2K.CLI.csproj
dotnet build R2K.DbProbe/R2K.DbProbe.csproj
```

### 2. Run Unit Tests

```bash
dotnet test R2K.Backend.Tests/R2K.Backend.Tests.csproj
```

Expected:

```text
Passed: 7
```

### 3. Test RDS Connectivity

```bash
dotnet run --project R2K.DbProbe/R2K.DbProbe.csproj
```

Expected:

```text
TCP probe: port is reachable from this host.
8.4.8
```

### 4. Test API Gateway

```bash
curl -i -X POST 'https://awv3cnqcx0.execute-api.us-east-2.amazonaws.com/OptimizeCommand' \
  -H 'Content-Type: application/json' \
  -d '{"command":"git status"}'
```

Expected:

```text
HTTP/2 200
```

### 5. Verify RDS Telemetry

```bash
MYSQL_PWD='<password>' mysql \
  -h database-rtk.cxkyikqe45yz.us-east-2.rds.amazonaws.com \
  -u MasterRtk \
  --ssl \
  r2k_telemetry \
  -e "SELECT Id, Command, OriginalTokens, OptimizedTokens, SavingsPercent, Timestamp FROM TokenLogs ORDER BY Id DESC LIMIT 5;"
```

Expected: recent command rows such as `git status`.

### 6. Test `rtk`

```bash
rtk git status
```

Expected:

```text
[... RTK ...] Optimizing...
```

Then normal `git status` output.

### 7. Test Agent Mode Command Interception

After aliases are loaded:

```bash
type git
git status
```

Expected:

```text
git is aliased to `rtk git'
```

Any Cursor Agent terminal command that invokes `git`, `npm`, or `npx` in that shell will go through `rtk`.

## Deployment Notes

### AWS Lambda

From the Lambda project:

```bash
cd /workspaces/r2k-orchestration-middleware/R2K.Backend.AWS
dotnet lambda deploy-function R2KOptimizer
```

Required IAM permissions include Lambda deploy permissions plus IAM role listing/pass-role permissions.

### Lambda Environment Variable

Set:

```text
SqlConnectionString
```

Use `Database=r2k_telemetry`, not `db_rtk`.

### API Gateway

Route:

```text
POST /OptimizeCommand
```

Integration target:

```text
R2KOptimizer
```

## Known Gaps

- AWS Lambda currently uses a simple 4-character token heuristic. The Azure backend has the richer Tiktoken-based optimizer.
- `R2K.CLI` expects `total_session_savings` in the response metrics, but the current AWS Lambda response does not include it. The CLI can still execute commands, but the metric summary should be made AWS-compatible in a future cleanup.
- Cursor model prompt tokens are not automatically intercepted. Only terminal commands routed through `rtk`, aliases, or MCP are optimized.
- Secrets were handled locally during setup. Rotate any credentials that were pasted into chat or terminal history.
- There is an extra lowercase `tokenlogs` table in MySQL that is not used by the current Lambda.

## Recommended Next Cleanup

1. Move the richer optimizer logic from `R2K.Backend` into a shared library used by both Azure and AWS.
2. Update `R2K.CLI` to support both Azure and AWS metric shapes.
3. Add an explicit `SessionId` to the Lambda response and telemetry insert.
4. Replace raw Lambda `SqlConnectionString` password with AWS Secrets Manager or SSM Parameter Store.
5. Decide whether to keep both Azure and AWS backends, or retire one path to reduce confusion.
