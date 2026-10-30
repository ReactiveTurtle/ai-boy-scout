using BoyScout.Application.Reports;
using BoyScout.Application.Tasks;
using BoyScout.Domain.Reports;
using BoyScout.Domain.Tasks;
using BoyScout.Infrastructure.Reports;
using BoyScout.Infrastructure.Tasks;
using BoyScout.Infrastructure.Persistence;
using BoyScout.Infrastructure.Configuration;
using BoyScout.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var config = new BoyScoutConfigService();
if (config.LoadStatus().Backend != StorageBackend.Sqlite)
{
    throw new InvalidOperationException("Firebase storage is not available until the remote workflow is implemented.");
}

var database = new BoyScoutDatabase(BoyScoutDatabasePath.Resolve());
database.Migrate();

builder.Services.AddSingleton(database);
builder.Services.AddSingleton<IReportStore>(_ => new ReportStore(database, config));
builder.Services.AddSingleton<ITaskStore>(_ => new TaskStore(database, config));
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<BoyScoutReportTools>();

await builder.Build().RunAsync();
