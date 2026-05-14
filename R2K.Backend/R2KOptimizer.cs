using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Data.SqlClient;
using Dapper;

namespace R2K.Backend
{
    public class R2KOptimizer
    {
        [Function("OptimizeCommand")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonConvert.DeserializeObject<dynamic>(requestBody);
            string rawCommand = data?.command;

            // 1. Token Calculation (Standard Developer Heuristic: 1 token ≈ 4 chars)
            int originalTokens = rawCommand.Length / 4;
            string optimizedCommand = rawCommand.Trim().Replace("  ", " "); // Basic optimization logic
            int optimizedTokens = optimizedCommand.Length / 4;
            decimal savings = originalTokens > 0 ? Math.Round(((decimal)(originalTokens - optimizedTokens) / originalTokens) * 100, 2) : 0;

            // 2. Telemetry Persistence[span_3](start_span)[span_3](end_span)
            string connString = Environment.GetEnvironmentVariable("SqlConnectionString");
            using var conn = new SqlConnection(connString);
            await conn.ExecuteAsync("INSERT INTO TokenLogs (Command, OriginalTokens, OptimizedTokens, SavingsPercent) VALUES (@c, @orig, @opt, @p)", 
                new { c = rawCommand, orig = originalTokens, opt = optimizedTokens, p = savings });

            // 3. Response Construction[span_4](start_span)[span_4](end_span)
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new {
                command_executed = optimizedCommand,
                metrics = new {
                    tokens_original = originalTokens,
                    tokens_optimized = optimizedTokens,
                    savings_percentage = savings,
                    total_session_savings = await conn.ExecuteScalarAsync<int>("SELECT SUM(OriginalTokens - OptimizedTokens) FROM TokenLogs")
                }
            });
            return response;
        }
    }
}
