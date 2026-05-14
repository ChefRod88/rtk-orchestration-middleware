using MySqlConnector;

namespace R2K.Backend;

/// <summary>
/// Builds a telemetry DB connection string from <c>SqlConnectionString</c> (non-secret parts)
/// plus <c>DB_PASSWORD</c> from AWS Secrets Manager or local env. Resolves <c>SslCa</c> to the
/// bundled RDS global CA next to the deployed assembly.
/// </summary>
internal static class MySqlTelemetryConnection
{
    internal static string? BuildConnectionString()
    {
        var template = Environment.GetEnvironmentVariable("SqlConnectionString");
        if (string.IsNullOrWhiteSpace(template))
            return null;

        var password = Environment.GetEnvironmentVariable("DB_PASSWORD");
        var pemPath = Path.Combine(AppContext.BaseDirectory, "global-bundle.pem");

        var builder = new MySqlConnectionStringBuilder(template)
        {
            Password = password ?? "",
        };

        if (File.Exists(pemPath))
            builder.SslCa = pemPath;

        return builder.ConnectionString;
    }
}
