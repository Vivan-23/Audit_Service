//using Audit_final.Models;
//using Audit_final.Services;
//using Audit_final.Services.Audit_final.Services;
//using Microsoft.Extensions.Hosting;
//using Microsoft.Extensions.Logging;
//using System.Text.Json;
//namespace Audit_final
//{


//    public class Worker : BackgroundService
//    {
//        private readonly ILogger<Worker> _logger;
//        private readonly IConfiguration _config;

//        public Worker(ILogger<Worker> logger, IConfiguration config)
//        {
//            _logger = logger;
//            _config = config;
//        }

//        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
//        {
//            var json = File.ReadAllText("servers.json");
//            var config = JsonSerializer.Deserialize<ServerConfigFile>(json);

//            //var servers = _config.GetSection("SqlServers").Get<List<SqlServerConfig>>();
//            var mySqlConn = _config["MySql"]; ;

//            var writer = new MySqlAuditWriter(mySqlConn);

//            while (!stoppingToken.IsCancellationRequested)
//            {
//                foreach (var server in config.SqlServers)
//                {
//                    try
//                    {
//                        _logger.LogInformation("Processing {server} at {time}", server.Name, DateTimeOffset.Now);

//                        // Step 1: Ensure Audit is ON
//                        var auditManager = new SqlServerAuditManager(
//                    server.ConnectionString,
//                    server.AuditFolder,
//                    "EpmAudit",
//                    _logger); // Pass the logger

//                        auditManager.EnsureAuditOn();
//                        // Step 2: Read Audit Logs
//                        var sqlReader = new SqlServerAuditReader(server.ConnectionString);
//                        var checkpointStore = new MySqlCheckpointStore(mySqlConn);
//                        var lastCheckpoint = await checkpointStore.GetLastCheckpointAsync(server.Name);
//                        var logs = await sqlReader.GetAuditLogsAsync(lastCheckpoint);

//                        if (logs.Any())
//                        {
//                            var latestEventTime = logs.Max(l => l.EventTime);
//                            await checkpointStore.UpdateCheckpointAsync(server.Name, latestEventTime);
//                            _logger.LogInformation("✅ Synced {count} logs from {server}", logs.Count, server.Name);
//                        }
//                        else
//                        {
//                            // Use current time as checkpoint to avoid re-fetching old logs
//                            var latestEventTime = DateTime.UtcNow;
//                            await checkpointStore.UpdateCheckpointAsync(server.Name, latestEventTime);
//                            _logger.LogInformation("⏩ No new logs from {server}", server.Name);
//                        }

//                        // Step 3: Write logs to MySQL
//                        await writer.SaveLogsAsync(logs);

//                        _logger.LogInformation("✅ Synced {count} logs from {server}", logs.Count, server.Name);
//                    }
//                    catch (Exception ex)
//                    {
//                        _logger.LogError(ex, "❌ Error processing {server}", server.Name);
//                    }
//                }

//                // Wait before next batch
//                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
//            }
//        }
//    }
//}
using Audit_final.Models;
using Audit_final.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Audit_final
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _config;
        private readonly ServerRepository _serverRepository;
        private readonly DynamicAuditManager _auditManager;

        private readonly List<SqlServerConfig> _activeServers = new();
        private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(30);

        public Worker(ILogger<Worker> logger, IConfiguration config,
                     ServerRepository serverRepository, DynamicAuditManager auditManager)
        {
            _logger = logger;
            _config = config;
            _serverRepository = serverRepository;
            _auditManager = auditManager;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation($"Audit Worker started with dynamic server management {DateTime.UtcNow}" );

            // Initial load of servers
            await RefreshServersAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_refreshInterval, stoppingToken);

                    // Refresh server list from MySQL
                    await RefreshServersAsync(stoppingToken);

                    // Process all active servers
                    await ProcessActiveServersAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in main execution loop");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }

            _logger.LogInformation($"Audit Worker stopped {DateTime.Now}");
        }

        private async Task RefreshServersAsync(CancellationToken cancellationToken)
        {
            try
            {
                var currentServers = await _serverRepository.GetActiveServersAsync();

                // Find new servers to add
                var serversToAdd = currentServers
                    .Where(newServer => !_activeServers.Any(existing => existing.Id == newServer.Id))
                    .ToList();

                // Find servers to remove
                var serversToRemove = _activeServers
                    .Where(existing => !currentServers.Any(newServer => newServer.Id == existing.Id))
                    .ToList();

                // Add new servers
                foreach (var server in serversToAdd)
                {
                    _activeServers.Add(server);
                    _logger.LogInformation(" Added server to active list: {ServerName}", server.Name);
                }

                // Remove deactivated servers
                foreach (var server in serversToRemove)
                {
                    _activeServers.RemoveAll(s => s.Id == server.Id);
                    _logger.LogInformation(" Removed server from active list: {ServerName}", server.Name);
                }

                // Update existing servers if changed
                foreach (var currentServer in currentServers)
                {
                    var existingServer = _activeServers.FirstOrDefault(s => s.Id == currentServer.Id);
                    if (existingServer != null && existingServer.LastUpdated < currentServer.LastUpdated)
                    {
                        _activeServers.RemoveAll(s => s.Id == currentServer.Id);
                        _activeServers.Add(currentServer);
                        _logger.LogInformation(" Updated server configuration: {ServerName}", currentServer.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh servers list from MySQL");
            }
        }

        private async Task ProcessActiveServersAsync(CancellationToken cancellationToken)
        {
            var currentServers = new List<SqlServerConfig>(_activeServers);

            foreach (var server in currentServers)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    await _auditManager.ProcessServerAsync(server, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process server: {ServerName}", server.Name);

                    // Remove problematic server from active list
                    _activeServers.RemoveAll(s => s.Id == server.Id);
                    _logger.LogWarning("🚫 Removed problematic server: {ServerName}", server.Name);
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Audit Worker...");
            await base.StopAsync(cancellationToken);
        }
    }

    
}
