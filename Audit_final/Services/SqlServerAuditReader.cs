using Audit_final.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Audit_final.Services
{
    public class SqlServerAuditReader
    {
        private readonly string _sqlConn;

        public SqlServerAuditReader(string sqlConn)
        {
            _sqlConn = sqlConn;
        }

       
        public async Task<List<AuditRecord>> GetAuditLogsAsync(DateTime lastEventTime)
        {
            var logs = new List<AuditRecord>();

            using var conn = new SqlConnection(_sqlConn);
            await conn.OpenAsync();

            var query = @"
        SELECT
            CONVERT(datetime2(6), event_time) as event_time,
            action_id,
            succeeded,
            server_principal_name,
            database_name,
            schema_name,
            object_name,    
            statement
        FROM sys.fn_get_audit_file('C:\SQLAudit\*', DEFAULT, DEFAULT)
        WHERE 
            schema_name NOT IN ('sys', 'INFORMATION_SCHEMA')
            AND statement NOT LIKE 'set %'
            AND statement NOT LIKE '-- network protocol%'
            AND event_time > DATEADD(MICROSECOND, 1, @LastEventTime)
            AND action_id NOT IN ('SCHE', 'DACC')
        ORDER BY event_time ASC";

            using var cmd = new SqlCommand(query, conn);

            // Use SqlDbType.DateTime2 which supports a wider date range
            var param = cmd.Parameters.Add("@LastEventTime", System.Data.SqlDbType.DateTime2);
            param.Value = lastEventTime;

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                logs.Add(new AuditRecord
                {
                    EventTime = reader.GetDateTime(0),
                    ActionId = reader.GetString(1),
                    Succeeded = reader.GetBoolean(2),
                    User = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Database = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Schema = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Object = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Statement = reader.IsDBNull(7) ? null : reader.GetString(7)
                });
            }

            return logs;
        }
    }
}
