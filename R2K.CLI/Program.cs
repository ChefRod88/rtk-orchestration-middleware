using System.Diagnostics;
using System.Net.Http.Json;
using Newtonsoft.Json.Linq;

// Override with env RTK_API_URL; Function auth: set RTK_FUNCTION_KEY (x-functions-key).
var apiUrl = Environment.GetEnvironmentVariable("RTK_API_URL")
    ?? "";

if (args.Length == 0) return;

string fullCommand = string.Join(" ", args);

using var client = new HttpClient();
var functionKey = Environment.GetEnvironmentVariable("RTK_FUNCTION_KEY");
if (!string.IsNullOrWhiteSpace(functionKey))
    client.DefaultRequestHeaders.Add("x-functions-key", functionKey.Trim());

Console.WriteLine("[⚡ RTK] Optimizing...");

var response = await client.PostAsJsonAsync(apiUrl, new { command = fullCommand });
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
