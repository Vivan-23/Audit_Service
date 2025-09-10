using Audit_final.Models;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Audit_final.Services
{
    public class MySqlAuditWriter
    {
        private readonly string _mySqlConn;

        public MySqlAuditWriter(string mySqlConn)
        {
            _mySqlConn = mySqlConn;
        }

        public async Task SaveLogsAsync(List<AuditRecord> logs)
        {
            using var conn = new MySqlConnection(_mySqlConn);
            await conn.OpenAsync();

            foreach (var log in logs)
            {
                var cmd = new MySqlCommand(@"
            INSERT INTO AuditLog 
            (EventTime, ActionId, Succeeded, UserName, DatabaseName, SchemaName, ObjectName, StatementText)
            VALUES (@EventTime, @ActionId, @Succeeded, @UserName, @DatabaseName, @SchemaName, @ObjectName, @StatementText)", conn);

                cmd.Parameters.AddWithValue("@EventTime", log.EventTime);
                cmd.Parameters.AddWithValue("@ActionId", log.ActionId);
                cmd.Parameters.AddWithValue("@Succeeded", log.Succeeded);
                cmd.Parameters.AddWithValue("@UserName", log.User ?? "");
                cmd.Parameters.AddWithValue("@DatabaseName", log.Database ?? "");
                cmd.Parameters.AddWithValue("@SchemaName", log.Schema ?? "");
                cmd.Parameters.AddWithValue("@ObjectName", log.Object ?? "");
                cmd.Parameters.AddWithValue("@StatementText", log.Statement ?? "");

                await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}
