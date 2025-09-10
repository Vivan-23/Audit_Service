using Audit_final.Models;
using Audit_final.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Audit_final
{
    public class DynamicAuditWorker : BackgroundService
    {
        private readonly ServerRepository _serverRepository;
        private readonly DynamicAuditManager _auditManager;
        private readonly ILogger<DynamicAuditWorker> _logger;

        private readonly ConcurrentDictionary<int, SqlServerConfig> _activeServers = new();
        private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(30);

        public DynamicAuditWorker(
            ServerRepository serverRepository,
            DynamicAuditManager auditManager,
            ILogger<DynamicAuditWorker> logger)
        {
            _serverRepository = serverRepository;
            _auditManager = auditManager;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation($"Dynamic Audit Worker started {DateTime.Now}");

            // Initial load
            await RefreshServersAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_refreshInterval, stoppingToken);
                    await RefreshServersAsync(stoppingToken);
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

            _logger.LogInformation("Dynamic Audit Worker stopped");
        }

        private async Task RefreshServersAsync(CancellationToken cancellationToken)
        {
            try
            {
                var currentServers = await _serverRepository.GetActiveServersAsync();

                // Find new servers to add
                var serversToAdd = currentServers
                    .Where(newServer => !_activeServers.ContainsKey(newServer.Id))
                    .ToList();

                // Find servers to remove
                var serversToRemove = _activeServers.Values
                    .Where(existing => !currentServers.Any(newServer => newServer.Id == existing.Id))
                    .ToList();

                // Add new servers
                foreach (var server in serversToAdd)
                {
                    if (_activeServers.TryAdd(server.Id, server))
                    {
                        _logger.LogInformation("✅ Added server to active list: {ServerName}", server.Name);
                    }
                }

                // Remove deactivated servers
                foreach (var server in serversToRemove)
                {
                    if (_activeServers.TryRemove(server.Id, out _))
                    {
                        _logger.LogInformation("❌ Removed server from active list: {ServerName}", server.Name);
                    }
                }

                // Update existing servers if changed
                foreach (var currentServer in currentServers)
                {
                    if (_activeServers.TryGetValue(currentServer.Id, out var existingServer) &&
                        existingServer.LastUpdated < currentServer.LastUpdated)
                    {
                        _activeServers[currentServer.Id] = currentServer;
                        _logger.LogInformation("🔄 Updated server configuration: {ServerName}", currentServer.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh servers list");
            }
        }

        private async Task ProcessActiveServersAsync(CancellationToken cancellationToken)
        {
            var currentServers = _activeServers.Values.ToList();

            var processingTasks = currentServers
                .Select(server => ProcessSingleServerAsync(server, cancellationToken))
                .ToArray();

            await Task.WhenAll(processingTasks);
        }

        private async Task ProcessSingleServerAsync(SqlServerConfig server, CancellationToken cancellationToken)
        {
            try
            {
                await _auditManager.ProcessServerAsync(server, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process server: {ServerName}", server.Name);

                // Remove problematic server from active list
                if (_activeServers.TryRemove(server.Id, out _))
                {
                    _logger.LogWarning("🚫 Removed problematic server: {ServerName}", server.Name);
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Dynamic Audit Worker...");
            await base.StopAsync(cancellationToken);
        }
    }
}