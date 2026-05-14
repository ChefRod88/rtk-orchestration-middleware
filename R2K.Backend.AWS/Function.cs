using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using MySqlConnector;
using Dapper;
using Newtonsoft.Json;

// This attribute tells Lambda to use System.Text.Json for serialization
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace R2K.Backend.AWS;

public class Function
{
    /// <summary>
    /// Handles the POST request from your CLI tool, optimizes the command,
    /// and logs telemetry to your AWS RDS MySQL instance.
    /// </summary>
    public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
    {
        try
        {
            // 1. Parse the incoming command from the CLI payload
            var data = JsonConvert.DeserializeObject<dynamic>(request.Body);
            string rawCommand = data?.command ?? "";

            if (string.IsNullOrEmpty(rawCommand))
            {
                return new APIGatewayProxyResponse { StatusCode = 400, Body = "No command provided." };
            }

            // 2. Optimization Logic (4-char per token heuristic)
            int originalTokens = rawCommand.Length / 4;
            // Basic optimization: strip whitespace and redundant spaces
            string optimizedCommand = rawCommand.Trim().Replace("  ", " ");
            int optimizedTokens = optimizedCommand.Length / 4;

            decimal savings = originalTokens > 0
                ? Math.Round(((decimal)(originalTokens - optimizedTokens) / originalTokens) * 100, 2)
                : 0;

            // 3. Telemetry Persistence (AWS RDS MySQL)
            // Pulls the connection string from Lambda Environment Variables
            string? connString = Environment.GetEnvironmentVariable("SqlConnectionString");
            if (string.IsNullOrWhiteSpace(connString))
            {
                context.Logger.LogLine("Missing SqlConnectionString environment variable.");
                return new APIGatewayProxyResponse { StatusCode = 500, Body = "Database configuration missing." };
            }

            using var conn = new MySqlConnection(connString);

            string sql = @"INSERT INTO TokenLogs (Command, OriginalTokens, OptimizedTokens, SavingsPercent)
                           VALUES (@c, @orig, @opt, @p)";

            await conn.ExecuteAsync(sql, new
            {
                c = rawCommand,
                orig = originalTokens,
                opt = optimizedTokens,
                p = savings
            });

            // 4. Construct the successful response
            var responseBody = new
            {
                command_executed = optimizedCommand,
                metrics = new
                {
                    tokens_original = originalTokens,
                    tokens_optimized = optimizedTokens,
                    savings_percentage = savings
                }
            };

            return new APIGatewayProxyResponse
            {
                StatusCode = 200,
                Body = JsonConvert.SerializeObject(responseBody),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }
        catch (Exception ex)
        {
            context.Logger.LogLine($"Critical Error: {ex.Message}");
            return new APIGatewayProxyResponse { StatusCode = 500, Body = "Internal Server Error" };
        }
    }
}
