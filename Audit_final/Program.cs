////using Audit_final;

////var builder = Host.CreateApplicationBuilder(args);
////builder.Services.AddHostedService<Worker>();

////var host = builder.Build();
////host.Run();
//using Audit_final;
//using Audit_final.Services;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Hosting;
//using Microsoft.Extensions.Logging;

//var builder = Host.CreateApplicationBuilder(args);

//// Configuration
//builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

//// Get MySQL connection string
//var mySqlConnectionString = builder.Configuration["MySql"]
//    ?? throw new InvalidOperationException("MySQL connection string is missing");

//// Services
//builder.Services.AddSingleton<ServerRepository>(provider =>
//{
//    var logger = provider.GetRequiredService<ILogger<ServerRepository>>();
//    return new ServerRepository(mySqlConnectionString, logger);
//});

//builder.Services.AddSingleton<DynamicAuditManager>(provider =>
//{
//    var logger = provider.GetRequiredService<ILogger<DynamicAuditManager>>();
//    return new DynamicAuditManager(mySqlConnectionString, logger);
//});

//builder.Services.AddHostedService<DynamicAuditWorker>();

//var host = builder.Build();
//await host.RunAsync();
using Audit_final;
using Audit_final.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Hardcoded MySQL connection string
var mySqlConnectionString = "Server=192.168.1.15;Port=3306;Database=TestDb;User=audituser;Password=pass@12323;";

// Services
builder.Services.AddSingleton<ServerRepository>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<ServerRepository>>();
    return new ServerRepository(mySqlConnectionString, logger);
});

//builder.Services.AddSingleton<DynamicAuditManager>(provider =>
//{
//    var logger = provider.GetRequiredService<ILogger<DynamicAuditManager>>();
//    return new DynamicAuditManager(mySqlConnectionString, logger);
//});
builder.Services.AddSingleton<DynamicAuditManager>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<DynamicAuditManager>>();
    var folderManager = provider.GetRequiredService<IFolderManager>();
    return new DynamicAuditManager(mySqlConnectionString, logger, folderManager);
});
builder.Services.AddSingleton<IFolderManager, FolderManager>();


builder.Services.AddHostedService<DynamicAuditWorker>();

// Logging configuration
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
    logging.AddFilter("Microsoft", LogLevel.Warning);
});

var host = builder.Build();

// Validate MySQL connection on startup
try
{
    var serverRepository = host.Services.GetRequiredService<ServerRepository>();

    if (await serverRepository.ValidateConnectionAsync(mySqlConnectionString))
    {
        Console.WriteLine("✅ MySQL connection validated");
    }
    else
    {
        Console.WriteLine("❌ MySQL connection failed");
        Environment.Exit(1);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Startup validation failed: {ex.Message}");
    Environment.Exit(1);
}

await host.RunAsync();