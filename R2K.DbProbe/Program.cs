using System.Net;
using System.Net.Sockets;
using MySqlConnector;

internal static class Program
{
    private static void Main()
    {
        var host = Environment.GetEnvironmentVariable("MYSQL_HOST")
            ?? "database-rtk.cxkyikqe45yz.us-east-2.rds.amazonaws.com";
        var port = int.TryParse(Environment.GetEnvironmentVariable("MYSQL_PORT"), out var p) ? p : 3306;
        var connectSeconds = uint.TryParse(Environment.GetEnvironmentVariable("MYSQL_CONNECT_TIMEOUT"), out var cs)
            ? cs
            : 15u;

        // Retrieve Secrets Manager password to ENV: DB_PASSWORD
        var password = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "";
        var sslCa = Path.Combine(AppContext.BaseDirectory, "global-bundle.pem");

        PrintPreflight(host, port, sslCa, connectSeconds, string.IsNullOrEmpty(password));

        var csb = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = (uint)port,
            Database = "mysql",
            UserID = "MasterRtk",
            Password = password,
            SslMode = MySqlSslMode.Required,
            SslCa = sslCa,
            ConnectionTimeout = connectSeconds,
        };

        MySqlConnection? conn = null;
        try
        {
            conn = new MySqlConnection(csb.ConnectionString);
            conn.Open();
            using var cmd = new MySqlCommand("SELECT VERSION();", conn);
            Console.WriteLine(cmd.ExecuteScalar());
        }
        catch (MySqlException ex) when (ex.Message.Contains("Connect Timeout", StringComparison.OrdinalIgnoreCase)
                                       || ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("TCP connection to MySQL did not complete before the timeout.");
            Console.Error.WriteLine("This usually means the client cannot reach RDS on the network (not a wrong password).");
            PrintNetworkHints(host, port);
            throw;
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

    private static void PrintPreflight(string host, int port, string sslCa, uint connectSeconds, bool passwordMissing)
    {
        Console.WriteLine($"Target: {host}:{port} (ConnectionTimeout={connectSeconds}s)");
        if (passwordMissing)
            Console.WriteLine("Warning: DB_PASSWORD is empty; auth will fail after the network path works.");

        if (!File.Exists(sslCa))
            Console.WriteLine($"Warning: CA bundle missing at {sslCa} (TLS may fail).");

        try
        {
            var addrs = Dns.GetHostAddresses(host);
            Console.WriteLine($"DNS: {host} -> {string.Join(", ", addrs.Select(a => a.ToString()))}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DNS resolution failed: {ex.Message}");
        }

        // Quick SYN probe so “timeout” vs “refused” is visible when possible.
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync(host, port);
            if (!task.Wait(TimeSpan.FromSeconds(Math.Min(connectSeconds, 5))))
                Console.WriteLine($"TCP probe: no response within 5s (RDS may be unreachable from this network).");
            else if (client.Connected)
                Console.WriteLine("TCP probe: port is reachable from this host.");
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"TCP probe: {ex.SocketErrorCode} — {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TCP probe: {ex.Message}");
        }

        Console.WriteLine();
    }

    private static void PrintNetworkHints(string host, int port)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("Checklist:");
        Console.Error.WriteLine($"  1. RDS instance: “Publicly accessible” = Yes (if you connect from dev cloud/codespace/home IP).");
        Console.Error.WriteLine($"  2. VPC security group inbound: MySQL/Aurora TCP {port} from your current public IP (or 0.0.0.0/0 short-term test).");
        Console.Error.WriteLine($"  3. Corporate / Codespace / VPN: outbound TCP {port} may be blocked — try from an EC2 in the same VPC or an SSH tunnel via bastion.");
        Console.Error.WriteLine($"  4. Same-VPC workloads: use private endpoint; SG must allow the caller’s ENI security group.");
        Console.Error.WriteLine($"  5. Optional tunnel: ssh -N -L 3307:{host}:{port} ec2-user@<bastion> then MYSQL_HOST=127.0.0.1 MYSQL_PORT=3307");
        Console.Error.WriteLine();
    }
}
