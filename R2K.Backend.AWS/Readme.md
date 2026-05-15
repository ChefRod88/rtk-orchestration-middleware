# R2K AWS Lambda (`R2KOptimizer`)

This project is the **production AWS path** for RTK: API Gateway invokes `Function.cs`, which refines pruned context from the CLI, computes metrics, and persists telemetry to **RDS MySQL** (`r2k_telemetry.TokenLogs`).

For CLI, hooks, and Cursor setup, see [docs/USAGE.md](../docs/USAGE.md) and [docs/END_USER_GUIDE.md](../docs/END_USER_GUIDE.md).

---

## Architecture

```text
rtk CLI  --POST JSON-->  API Gateway  -->  R2KOptimizer (Lambda)
                                                |
                                                v
                                         MySQL TokenLogs
```

---

## Request body (orchestrator)

The CLI sends a JSON payload shaped by `AwsLambdaClient`:

| Field | Type | Description |
|-------|------|-------------|
| `command` | string | Original command line (e.g. `cursor path/File.cs:42`). |
| `pruned_context` | string | Locally pruned file/git context. |
| `original_token_count` | int | Estimated tokens before pruning. |
| `pruned_token_count` | int | Estimated tokens after local pruning. |
| `pruning_strategy` | string | `minimal`, `agentic`, or `diff-only`. |

Legacy prompt-only requests may send `{ "prompt": "…" }` without `command`; the handler returns optimized prompt metrics without shell execution.

---

## Response (typical)

```json
{
  "command_executed": "cursor R2K.CLI/Program.cs",
  "metrics": {
    "tokens_original": 1200,
    "tokens_optimized": 180,
    "savings_percentage": 85.0,
    "strategy_used": "agentic",
    "original_context_tokens": 1200,
    "pruned_context_tokens": 180,
    "pruning_efficiency": 85.0,
    "orchestration_decision": "refined",
    "context_density": 0.15
  }
}
```

Exact fields depend on handler version; clients should tolerate optional metric keys.

---

## Lambda environment

| Variable | Required | Purpose |
|----------|----------|---------|
| `SqlConnectionString` | Yes (for persistence) | MySQL connection to `r2k_telemetry` |

Example shape (replace secrets):

```text
Server=database-rtk.….us-east-2.rds.amazonaws.com;Port=3306;Database=r2k_telemetry;User ID=…;Password=…;SslMode=Required;Connection Timeout=30;
```

Apply schema from [`R2K.Backend/Schema/TokenLogs.mysql.sql`](../R2K.Backend/Schema/TokenLogs.mysql.sql) (includes `StrategyUsed`, context token columns, `PruningEfficiency`).

---

## Deploy

Prerequisites: [Amazon.Lambda.Tools](https://github.com/aws/aws-extensions-for-dotnet-cli#aws-lambda-amazonlambdatools), AWS credentials, IAM for Lambda + pass-role.

```bash
dotnet tool install -g Amazon.Lambda.Tools   # or dotnet tool update -g Amazon.Lambda.Tools
cd R2K.Backend.AWS
dotnet lambda deploy-function R2KOptimizer
```

Wire API Gateway:

- **Method:** `POST`
- **Path:** `/OptimizeCommand`
- **Integration:** Lambda proxy → `R2KOptimizer`

---

## Local verification

```bash
curl -sS -X POST 'https://<api-id>.execute-api.<region>.amazonaws.com/OptimizeCommand' \
  -H 'Content-Type: application/json' \
  -d '{"command":"git status"}'
```

Expect HTTP 200 and `command_executed` in the body.

---

## Project files

| File | Role |
|------|------|
| `Function.cs` | API Gateway handler, orchestrator pipeline, telemetry insert |
| `aws-lambda-tools-defaults.json` | Default deploy profile |
| `R2K.Backend.AWS.csproj` | `net8.0` Lambda assembly |

Unit tests for shared pruning/orchestration logic live in `R2K.Backend.Tests` (CLI + backend behaviors).
