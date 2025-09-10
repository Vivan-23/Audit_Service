using Audit_final.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Audit_final.Services
{
    public class ServerRepository 
    {
        private readonly string _mySqlConnectionString;
        private readonly ILogger<ServerRepository> _logger;

        public ServerRepository(string mySqlConnectionString, ILogger<ServerRepository> logger)
        {
            _mySqlConnectionString = mySqlConnectionString;
            _logger = logger;
        }

        public async Task<List<SqlServerConfig>> GetActiveServersAsync()
        {
            var servers = new List<SqlServerConfig>();

            try
            {
                using var connection = new MySqlConnection(_mySqlConnectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT Id, Name, ServerAddress, Username, Password, AuditFolder, IsSelected, UpdatedAt 
                    FROM Servers 
                    WHERE IsSelected = true 
                    ORDER BY Name";

                using var command = new MySqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    servers.Add(new SqlServerConfig
                    {
                        Id = reader.GetInt32("Id"),
                        Name = reader.GetString("Name"),
                        ServerAddress = reader.GetString("ServerAddress"),
                        Username = reader.GetString("Username"),
                        Password = reader.GetString("Password"),
                        AuditFolder = reader.GetString("AuditFolder"),
                        IsSelected = reader.GetBoolean("IsSelected"),
                        LastUpdated = reader.GetDateTime("UpdatedAt")
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch active servers from MySQL");
                throw;
            }

            return servers;
        }

        public async Task<bool> ValidateConnectionAsync(string connectionString)
        {
            try
            {
                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();
                await connection.CloseAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
