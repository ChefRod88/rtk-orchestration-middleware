global using R2K.Backend;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tiktoken;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((_, services) =>
    {
        services.AddSingleton(_ => TikTokenEncoder.CreateForModel(Models.Gpt4o));
        services.AddSingleton<ICommandOptimizer, CliCommandOptimizer>();
        services.AddSingleton<ICommandOptimizationService, CommandOptimizationService>();
        services.AddSingleton<IPromptOptimizationService, PromptOptimizationService>();
    })
    .Build();

await host.RunAsync();
