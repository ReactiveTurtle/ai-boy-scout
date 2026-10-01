using BoyScout.Core.Reports;
using BoyScout.Core.Storage;
using BoyScout.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var database = new BoyScoutDatabase(BoyScoutDatabasePath.Resolve());
database.Migrate();

builder.Services.AddSingleton(database);
builder.Services.AddSingleton<ReportStore>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<BoyScoutReportTools>();

await builder.Build().RunAsync();
