using MySql.Data.MySqlClient;
using System;
using System.Threading.Tasks;

namespace Audit_final.Services
{
    public class MySqlCheckpointStore
    {
        private readonly string _mySqlConn;

        public MySqlCheckpointStore(string mySqlConn)
        {
            _mySqlConn = mySqlConn;
        }

        public async Task<DateTime> GetLastCheckpointAsync(string serverName)
        {
            using var conn = new MySqlConnection(_mySqlConn);
            await conn.OpenAsync();

            var cmd = new MySqlCommand("SELECT LastEventTime FROM AuditCheckpoint WHERE ServerName = @server", conn);
            cmd.Parameters.AddWithValue("@server", serverName);
            var result = await cmd.ExecuteScalarAsync();

            return result != null ? Convert.ToDateTime(result) : DateTime.MinValue;
        }

        public async Task UpdateCheckpointAsync(string serverName, DateTime lastEventTime)
        {
            using var conn = new MySqlConnection(_mySqlConn);
            await conn.OpenAsync();

            var cmd = new MySqlCommand(@"
                INSERT INTO AuditCheckpoint (ServerName, LastEventTime) 
                VALUES (@server, @time) 
                ON DUPLICATE KEY UPDATE LastEventTime = @time", conn);

            cmd.Parameters.AddWithValue("@server", serverName);
            cmd.Parameters.AddWithValue("@time", lastEventTime);

            await cmd.ExecuteNonQueryAsync();
        }
    }
}
