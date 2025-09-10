using Audit_final.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Audit_final.Services
{
    public interface IFolderManager
    {
        Task<bool> EnsureAuditFolderAsync(SqlServerConfig serverConfig, CancellationToken stoppingToken);
    }

    public class FolderManager : IFolderManager
    {
        private readonly ILogger<FolderManager> _logger;

        public FolderManager(ILogger<FolderManager> logger)
        {
            _logger = logger;
        }

        public async Task<bool> EnsureAuditFolderAsync(SqlServerConfig serverConfig, CancellationToken stoppingToken)
        {
            if (stoppingToken.IsCancellationRequested)
                return false;

            try
            {
                _logger.LogInformation("Checking audit folder for server: {ServerName}", serverConfig.Name);

                // Determine if this is a local or remote server
                bool isLocalServer = IsLocalServer(serverConfig.ServerAddress);

                if (isLocalServer)
                {
                    return await EnsureLocalAuditFolderAsync(serverConfig.AuditFolder);
                }
                else
                {
                    return await EnsureRemoteAuditFolderAsync(serverConfig);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure audit folder for server: {ServerName}", serverConfig.Name);
                return false;
            }
        }

        private bool IsLocalServer(string serverAddress)
        {
            var localAddresses = new[] { "localhost", "127.0.0.1", ".", "(local)" };

            // Use string.Equals for comparison instead of instance method
            return localAddresses.Any(addr => string.Equals(addr, serverAddress, StringComparison.OrdinalIgnoreCase)) ||
                   string.Equals(serverAddress, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> EnsureLocalAuditFolderAsync(string auditFolderPath)
        {
            try
            {
                _logger.LogInformation("Processing local audit folder: {FolderPath}", auditFolderPath);

                // Create directory if it doesn't exist
                if (!Directory.Exists(auditFolderPath))
                {
                    Directory.CreateDirectory(auditFolderPath);
                    _logger.LogInformation("Created audit folder: {FolderPath}", auditFolderPath);
                }

                // Set permissions for SQL Server service
                SetFolderPermissions(auditFolderPath);

                _logger.LogInformation("Granted MSSQLSERVER permissions to folder: {FolderPath}", auditFolderPath);
                _logger.LogInformation("Audit folder check complete for local server");

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure local audit folder: {FolderPath}", auditFolderPath);
                return false;
            }
        }

        private async Task<bool> EnsureRemoteAuditFolderAsync(SqlServerConfig serverConfig)
        {
            try
            {
                _logger.LogInformation("Processing remote audit folder on server: {ServerAddress}", serverConfig.ServerAddress);

                // Check if we have admin credentials for remote access
                if (string.IsNullOrEmpty(serverConfig.AdminUsername) || string.IsNullOrEmpty(serverConfig.AdminPassword))
                {
                    _logger.LogWarning("Admin credentials not provided for remote server: {ServerName}", serverConfig.Name);
                    return false;
                }

                // Use PowerShell remoting to create folder and set permissions
                var success = await CreateRemoteFolderWithPowerShell(
                    serverConfig.ServerAddress,
                    serverConfig.AdminUsername,
                    serverConfig.AdminPassword,
                    serverConfig.AuditFolder
                );

                if (success)
                {
                    _logger.LogInformation("Audit folder check complete for remote server: {ServerName}", serverConfig.Name);
                    return true;
                }

                // Fallback to SQL method if PowerShell fails
                return await CreateFolderViaSql(serverConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure remote audit folder for server: {ServerName}", serverConfig.Name);
                return false;
            }
        }

        private void SetFolderPermissions(string folderPath)
        {
            try
            {
                var directoryInfo = new DirectoryInfo(folderPath);
                var directorySecurity = directoryInfo.GetAccessControl();

                // Add NT SERVICE\MSSQLSERVER with Modify rights
                var sqlServiceAccount = new NTAccount("NT SERVICE", "MSSQLSERVER");
                directorySecurity.AddAccessRule(new FileSystemAccessRule(
                    sqlServiceAccount,
                    FileSystemRights.Modify | FileSystemRights.Write | FileSystemRights.ReadAndExecute,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));

                // Apply the new permissions
                directoryInfo.SetAccessControl(directorySecurity);

                _logger.LogInformation("Set folder permissions for: {FolderPath}", folderPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not set folder permissions (may require admin rights): {Message}", ex.Message);
                throw;
            }
        }

        private async Task<bool> CreateRemoteFolderWithPowerShell(string serverAddress, string username, string password, string auditFolderPath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"& {{" +
                               $"$cred = New-Object System.Management.Automation.PSCredential('{username}', (ConvertTo-SecureString '{password}' -AsPlainText -Force)); " +
                               $"$session = New-PSSession -ComputerName '{serverAddress}' -Credential $cred; " +
                               $"Invoke-Command -Session $session -ScriptBlock {{ " +
                               $"$folderPath = '{auditFolderPath}'; " +
                               $"if (!(Test-Path $folderPath)) {{ New-Item -Path $folderPath -ItemType Directory -Force }}; " +
                               $"$acl = Get-Acl $folderPath; " +
                               $"$rule = New-Object System.Security.AccessControl.FileSystemAccessRule('NT SERVICE\\\\MSSQLSERVER', 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow'); " +
                               $"$acl.AddAccessRule($rule); " +
                               $"Set-Acl -Path $folderPath -AclObject $acl; " +
                               $"Write-Output 'Folder created and permissions set' }}; " +
                               $"Remove-PSSession $session; }}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();

                    await process.WaitForExitAsync();

                    if (process.ExitCode == 0)
                    {
                        _logger.LogInformation("Remote folder created successfully: {Output}", output);
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning("Remote folder creation failed: {Error}", error);
                        return false;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("PowerShell remoting failed: {Message}", ex.Message);
                return false;
            }
        }

        private async Task<bool> CreateFolderViaSql(SqlServerConfig serverConfig)
        {
            try
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(serverConfig.ConnectionString);
                await conn.OpenAsync();

                // Use xp_create_subdir to create the folder
                var createDirCmd = new Microsoft.Data.SqlClient.SqlCommand(
                    $"EXEC xp_create_subdir '{serverConfig.AuditFolder.Replace("'", "''")}'", conn);

                await createDirCmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Created audit folder via SQL: {Folder}", serverConfig.AuditFolder);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create folder via SQL for server: {ServerName}", serverConfig.Name);
                return false;
            }
        }
    }
}