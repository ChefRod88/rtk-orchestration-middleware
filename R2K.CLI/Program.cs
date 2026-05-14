using System.Diagnostics;
using System.Net.Http.Json;
using Newtonsoft.Json.Linq;

// Replace host/path after deployment; Function auth may require ?code=...
const string ApiUrl =
    "https://rtk-ochestration-middleware-e9ebb3erg8cacbhh.westus3-01.azurewebsites.net/api/OptimizeCommand";

if (args.Length == 0) return;

string fullCommand = string.Join(" ", args);

using var client = new HttpClient();

Console.WriteLine("[⚡ RTK] Optimizing...");

var response = await client.PostAsJsonAsync(ApiUrl, new { command = fullCommand });
response.EnsureSuccessStatusCode();

var result = JObject.Parse(await response.Content.ReadAsStringAsync());

string optimized = result["command_executed"]!.ToString();

var psi = new ProcessStartInfo {
    FileName = "/bin/bash",
    UseShellExecute = false
};
psi.ArgumentList.Add("-c");
psi.ArgumentList.Add(optimized);

using var process = Process.Start(psi);

process?.WaitForExit();

var m = result["metrics"]!;
Console.WriteLine("\n======================================");
Console.WriteLine($"🤖 RTK SAVINGS: {m["savings_percentage"]}%");
Console.WriteLine($"📊 SESSION TOTAL: {m["total_session_savings"]} tokens");
Console.WriteLine("======================================");
