using MySqlConnector;

internal static class Program
{
    private static void Main()
    {
        // Retrieve Secrets Manager password to ENV: DB_PASSWORD
        var password = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "";
        var sslCa = Path.Combine(AppContext.BaseDirectory, "global-bundle.pem");

        var csb = new MySqlConnectionStringBuilder
        {
            Server = "database-rtk.cxkyikqe45yz.us-east-2.rds.amazonaws.com",
            Port = 3306,
            Database = "mysql",
            UserID = "MasterRtk",
            Password = password,
            SslMode = MySqlSslMode.Required,
            SslCa = sslCa,
        };

        MySqlConnection? conn = null;
        try
        {
            conn = new MySqlConnection(csb.ConnectionString);
            conn.Open();
            using var cmd = new MySqlCommand("SELECT VERSION();", conn);
            Console.WriteLine(cmd.ExecuteScalar());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Database error: {ex.Message}");
            throw;
        }
        finally
        {
            conn?.Close();
            conn?.Dispose();
        }
    }
}
