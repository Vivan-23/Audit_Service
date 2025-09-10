//using Audit_final.Models;
//using Audit_final.Services.Audit_final.Services;
//using Microsoft.Extensions.Logging;
//using System.IO;

//namespace Audit_final.Services
//{
//    public class DynamicAuditManager
//    {
//        private readonly ILogger<DynamicAuditManager> _logger;
//        private readonly string _mySqlConnectionString;

//        public DynamicAuditManager(string mySqlConnectionString, ILogger<DynamicAuditManager> logger)
//        {
//            _mySqlConnectionString = mySqlConnectionString;
//            _logger = logger;
//        }

//        public async Task ProcessServerAsync(SqlServerConfig server, CancellationToken cancellationToken)
//        {
//            if (cancellationToken.IsCancellationRequested)
//                return;

//            try
//            {
//                _logger.LogInformation($"Starting audit processing for server: {server.Name}  {DateTime.Now}" );

//                // Ensure audit folder exists
//                await EnsureAuditFolderExistsAsync(server.AuditFolder);

//                // Your existing audit logic
//                var auditManager = new SqlServerAuditManager(
//                    server.ConnectionString,
//                    server.AuditFolder,
//                    "EpmAudit",
//                    _logger);

//                auditManager.EnsureAuditOn();

//                // Read audit logs
//                var sqlReader = new SqlServerAuditReader(server.ConnectionString);
//                var checkpointStore = new MySqlCheckpointStore(_mySqlConnectionString);
//                var lastCheckpoint = await checkpointStore.GetLastCheckpointAsync(server.Name);

//                var logs = await sqlReader.GetAuditLogsAsync(lastCheckpoint);

//                // Write to MySQL
//                var writer = new MySqlAuditWriter(_mySqlConnectionString);
//                await writer.SaveLogsAsync(logs);

//                // Update checkpoint
//                if (logs.Any())
//                {
//                    var latestEventTime = logs.Max(l => l.EventTime);
//                    await checkpointStore.UpdateCheckpointAsync(server.Name, latestEventTime);
//                    _logger.LogInformation("✅ Synced {LogCount} logs from {ServerName}", logs.Count, server.Name);
//                }
//                else
//                {
//                    // Update checkpoint to current time to avoid re-fetching old logs
//                    await checkpointStore.UpdateCheckpointAsync(server.Name, DateTime.UtcNow);
//                    _logger.LogInformation("⏩ No new logs from {ServerName}", server.Name);
//                }

//                _logger.LogInformation($"Completed audit processing for server: {server.Name} {DateTime.Now}\n");
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error processing server {ServerName}\n", server.Name);
//            }
//        }

//        private async Task EnsureAuditFolderExistsAsync(string auditFolder)
//        {
//            try
//            {
//                if (!Directory.Exists(auditFolder))
//                {
//                    Directory.CreateDirectory(auditFolder);
//                    _logger.LogInformation("Created audit folder: {Folder}", auditFolder);
//                }
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Failed to create audit folder: {Folder}", auditFolder);
//                throw;
//            }
//        }
//    }
//}
using Audit_final.Models;
using Audit_final.Services.Audit_final.Services;
using Microsoft.Extensions.Logging;

namespace Audit_final.Services
{
    public class DynamicAuditManager
    {
        private readonly ILogger<DynamicAuditManager> _logger;
        private readonly string _mySqlConnectionString;
        private readonly IFolderManager _folderManager;

        public DynamicAuditManager(string mySqlConnectionString, ILogger<DynamicAuditManager> logger, IFolderManager folderManager)
        {
            _mySqlConnectionString = mySqlConnectionString;
            _logger = logger;
            _folderManager = folderManager;
        }

        public async Task ProcessServerAsync(SqlServerConfig server, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            try
            {
                _logger.LogInformation($"Starting audit processing for server: {server.Name}  {DateTime.Now}");

                // Step 1: Ensure audit folder exists with proper permissions
                var folderReady = await _folderManager.EnsureAuditFolderAsync(server, cancellationToken);

                if (!folderReady)
                {
                    _logger.LogWarning("Skipping server {ServerName} due to folder setup issues", server.Name);
                    return;
                }

                // Step 2: Your existing audit logic
                var auditManager = new SqlServerAuditManager(
                    server.ConnectionString,
                    server.AuditFolder,
                    "EpmAudit",
                    _logger);

                auditManager.EnsureAuditOn();

                // Read audit logs
                var sqlReader = new SqlServerAuditReader(server.ConnectionString);
                var checkpointStore = new MySqlCheckpointStore(_mySqlConnectionString);
                var lastCheckpoint = await checkpointStore.GetLastCheckpointAsync(server.Name);

                var logs = await sqlReader.GetAuditLogsAsync(lastCheckpoint);

                // Write to MySQL
                var writer = new MySqlAuditWriter(_mySqlConnectionString);
                await writer.SaveLogsAsync(logs);

                // Update checkpoint
                if (logs.Any())
                {
                    var latestEventTime = logs.Max(l => l.EventTime);
                    await checkpointStore.UpdateCheckpointAsync(server.Name, latestEventTime);
                    _logger.LogInformation("✅ Synced {LogCount} logs from {ServerName}", logs.Count, server.Name);
                }
                else
                {
                    // Update checkpoint to current time to avoid re-fetching old logs
                    await checkpointStore.UpdateCheckpointAsync(server.Name, DateTime.UtcNow);
                    _logger.LogInformation("⏩ No new logs from {ServerName}", server.Name);
                }

                _logger.LogInformation($"Completed audit processing for server: {server.Name} {DateTime.Now}\n");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing server {ServerName}\n", server.Name);
            }
        }
    }
}