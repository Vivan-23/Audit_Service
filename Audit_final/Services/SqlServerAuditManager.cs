using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Audit_final.Services
{
    
    using Microsoft.Data.SqlClient;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;

    namespace Audit_final.Services
    {
        public class SqlServerAuditManager
        {
            private readonly string _conn;
            private readonly string _auditPath;
            private readonly string _auditName;
            private readonly ILogger _logger;

            public SqlServerAuditManager(string conn, string auditPath, string auditName, ILogger logger)
            {
                _conn = conn;
                _auditPath = auditPath;
                _auditName = auditName;
                _logger = logger;
            }

            public void EnsureAuditOn()
            {
                using var conn = new SqlConnection(_conn);
                conn.Open();

                try
                {
                    // 1. Ensure Server Audit exists and is enabled
                    EnsureServerAudit(conn);

                    // 2. Ensure Server Audit Specification exists and is enabled
                    EnsureServerAuditSpecification(conn);

                    // 3. Ensure Database Audit Specifications for all user databases
                    EnsureDatabaseAuditSpecifications(conn);

                    _logger.LogInformation("✅ Comprehensive audit setup completed for '{auditName}'", _auditName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error configuring comprehensive audit");
                    throw;
                }
            }

            private void EnsureServerAudit(SqlConnection conn)
            {
                var existsCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM sys.server_audits WHERE name = @auditName", conn);
                existsCmd.Parameters.AddWithValue("@auditName", _auditName);

                var count = (int)existsCmd.ExecuteScalar();

                if (count == 0)
                {
                    _logger.LogInformation("🔧 Creating server audit '{auditName}'", _auditName);

                    // Create audit directory
                    try
                    {
                        var createDirCmd = new SqlCommand(
                            $"EXEC xp_create_subdir '{_auditPath.Replace("\\", "\\\\")}'", conn);
                        createDirCmd.ExecuteNonQuery();
                    }
                    catch (SqlException ex)
                    {
                        _logger.LogInformation("📁 Audit directory already exists or cannot be created: {message}", ex.Message);
                    }

                    // Create Server Audit
                    var createCmd = new SqlCommand($@"
                    CREATE SERVER AUDIT [{_auditName}]
                    TO FILE (FILEPATH = '{_auditPath}', MAXSIZE = 2 GB, MAX_ROLLOVER_FILES = 10)
                    WITH (ON_FAILURE = CONTINUE, QUEUE_DELAY = 1000);
                    ALTER SERVER AUDIT [{_auditName}] WITH (STATE = ON);", conn);
                    createCmd.ExecuteNonQuery();

                    _logger.LogInformation("✅ Server audit '{auditName}' created and enabled", _auditName);
                }
                else
                {
                    _logger.LogInformation("📋 Ensuring server audit '{auditName}' is enabled", _auditName);

                    // Just ensure audit is enabled
                    var alterCmd = new SqlCommand(
                        $"ALTER SERVER AUDIT [{_auditName}] WITH (STATE = ON);", conn);
                    alterCmd.ExecuteNonQuery();

                    _logger.LogInformation("✅ Server audit '{auditName}' is enabled", _auditName);
                }
            }

            private void EnsureServerAuditSpecification(SqlConnection conn)
            {
                var existsCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM sys.server_audit_specifications WHERE name = @specName", conn);
                existsCmd.Parameters.AddWithValue("@specName", $"{_auditName}_ServerSpec");

                var count = (int)existsCmd.ExecuteScalar();

                if (count == 0)
                {
                    _logger.LogInformation("🔧 Creating server audit specification");

                    var createSpecCmd = new SqlCommand($@"
                    CREATE SERVER AUDIT SPECIFICATION [{_auditName}_ServerSpec]
                    FOR SERVER AUDIT [{_auditName}]
                    ADD (FAILED_LOGIN_GROUP),
                    ADD (SUCCESSFUL_LOGIN_GROUP),
                    ADD (SERVER_ROLE_MEMBER_CHANGE_GROUP),
                    ADD (SERVER_PRINCIPAL_CHANGE_GROUP),
                    ADD (SERVER_PRINCIPAL_IMPERSONATION_GROUP),
                    ADD (SERVER_OBJECT_PERMISSION_CHANGE_GROUP),
                    ADD (SERVER_OBJECT_OWNERSHIP_CHANGE_GROUP),
                    ADD (SERVER_OPERATION_GROUP),
                    ADD (SERVER_PERMISSION_CHANGE_GROUP),
                    ADD (SERVER_STATE_CHANGE_GROUP),
                    ADD (TRACE_CHANGE_GROUP),
                    ADD (AUDIT_CHANGE_GROUP),
                    ADD (DATABASE_CHANGE_GROUP),
                    ADD (DATABASE_OBJECT_CHANGE_GROUP),
                    ADD (DATABASE_OWNERSHIP_CHANGE_GROUP),
                    ADD (DATABASE_PERMISSION_CHANGE_GROUP),
                    ADD (DATABASE_PRINCIPAL_CHANGE_GROUP),
                    ADD (DATABASE_ROLE_MEMBER_CHANGE_GROUP),
                    ADD (SCHEMA_OBJECT_CHANGE_GROUP),
                    ADD (SCHEMA_OBJECT_OWNERSHIP_CHANGE_GROUP),
                    ADD (SCHEMA_OBJECT_PERMISSION_CHANGE_GROUP)
                    WITH (STATE = ON);", conn);
                    createSpecCmd.ExecuteNonQuery();

                    _logger.LogInformation("✅ Server audit specification created");
                }
                else
                {
                    _logger.LogInformation("📋 Ensuring server audit specification is enabled");

                    var alterSpecCmd = new SqlCommand(
                        $"ALTER SERVER AUDIT SPECIFICATION [{_auditName}_ServerSpec] WITH (STATE = ON);", conn);
                    try
                    {
                        alterSpecCmd.ExecuteNonQuery();
                        _logger.LogInformation("✅ Server audit specification is enabled");
                    }
                    catch (SqlException ex)
                    {
                        _logger.LogWarning("⚠️ Could not enable server audit specification: {message}", ex.Message);
                    }
                }
            }

            private void EnsureDatabaseAuditSpecifications(SqlConnection conn)
            {
                _logger.LogInformation("🔧 Setting up database audit specifications");

                // Get all user databases
                var getDbsCmd = new SqlCommand(
                    "SELECT name FROM sys.databases WHERE database_id > 4 AND state = 0", conn);

                var databases = new List<string>();
                using (var reader = getDbsCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        databases.Add(reader.GetString(0));
                    }
                }

                foreach (var dbName in databases)
                {
                    try
                    {
                        // Use dynamic SQL to switch database context
                        var sql = $@"
                        USE [{dbName}];
                        
                        IF NOT EXISTS (SELECT 1 FROM sys.database_audit_specifications WHERE name = 'AllDMLOperations_{dbName}')
                        BEGIN
                            CREATE DATABASE AUDIT SPECIFICATION [AllDMLOperations_{dbName}]
                            FOR SERVER AUDIT [{_auditName}]
                            ADD (SELECT ON DATABASE::[{dbName}] BY PUBLIC),
                            ADD (INSERT ON DATABASE::[{dbName}] BY PUBLIC),
                            ADD (UPDATE ON DATABASE::[{dbName}] BY PUBLIC),
                            ADD (DELETE ON DATABASE::[{dbName}] BY PUBLIC),
                            ADD (EXECUTE ON DATABASE::[{dbName}] BY PUBLIC),
                            ADD (SCHEMA_OBJECT_ACCESS_GROUP),
                            ADD (DATABASE_OBJECT_ACCESS_GROUP)
                            WITH (STATE = ON);
                            
                            SELECT 1 as Created;
                        END
                        ELSE
                        BEGIN
                            ALTER DATABASE AUDIT SPECIFICATION [AllDMLOperations_{dbName}] WITH (STATE = ON);
                            SELECT 2 as Enabled;
                        END";

                        var setupCmd = new SqlCommand(sql, conn);
                        var result = setupCmd.ExecuteScalar();

                        if (result != null)
                        {
                            if ((int)result == 1)
                                _logger.LogInformation("✅ Created DML auditing for: {dbName}", dbName);
                            else
                                _logger.LogInformation("✅ Enabled DML auditing for: {dbName}", dbName);
                        }
                    }
                    catch (SqlException ex)
                    {
                        _logger.LogWarning("⚠️ Could not setup DML auditing for {dbName}: {message}",
                            dbName, ex.Message);
                    }
                }
            }
        }
    }
}
//public class SqlServerAuditManager
    //{
    //    private readonly string _conn;
    //    private readonly string _auditPath;
    //    private readonly string _auditName;
    //    private readonly ILogger _logger;
    //    public SqlServerAuditManager(string conn, string auditPath, string auditName, ILogger logger)
    //    {
    //        _conn = conn;
    //        _auditPath = auditPath;
    //        _auditName = auditName;
    //        _logger = logger;
    //    }
    //    public void EnsureAuditOn()
    //    {
    //        using var conn = new SqlConnection(_conn);
    //        conn.Open();

    //        try
    //        {
    //            var existsCmd = new SqlCommand(
    //                "SELECT COUNT(*) FROM sys.server_audits WHERE name = @auditName", conn);
    //            existsCmd.Parameters.AddWithValue("@auditName", _auditName);

    //            var count = (int)existsCmd.ExecuteScalar();

    //            if (count == 0)
    //            {
    //                _logger.LogInformation("🔧 Creating comprehensive audit '{auditName}'", _auditName);

    //                // Create audit directory
    //                try
    //                {
    //                    var createDirCmd = new SqlCommand(
    //                        $"EXEC xp_create_subdir '{_auditPath.Replace("\\", "\\\\")}'", conn);
    //                    createDirCmd.ExecuteNonQuery();
    //                }
    //                catch { /* Directory might exist */ }

    //                // Create Server Audit (NO FILTERS)
    //                var createCmd = new SqlCommand($@"
    //            CREATE SERVER AUDIT [{_auditName}]
    //            TO FILE (FILEPATH = '{_auditPath}', MAXSIZE = 2 GB, MAX_ROLLOVER_FILES = 10)
    //            WITH (ON_FAILURE = CONTINUE, QUEUE_DELAY = 1000);
    //            ALTER SERVER AUDIT [{_auditName}] WITH (STATE = ON);", conn);
    //                createCmd.ExecuteNonQuery();

    //                // Create Server Audit Specification
    //                var serverSpecCmd = new SqlCommand($@"
    //            CREATE SERVER AUDIT SPECIFICATION [{_auditName}_ServerSpec]
    //            FOR SERVER AUDIT [{_auditName}]
    //            -- Server-level actions
    //    ADD (FAILED_LOGIN_GROUP),
    //    ADD (SUCCESSFUL_LOGIN_GROUP),
    //    ADD (SERVER_ROLE_MEMBER_CHANGE_GROUP),
    //    ADD (SERVER_PRINCIPAL_CHANGE_GROUP),
    //    ADD (SERVER_PRINCIPAL_IMPERSONATION_GROUP),
    //    ADD (SERVER_OBJECT_PERMISSION_CHANGE_GROUP),
    //    ADD (SERVER_OBJECT_OWNERSHIP_CHANGE_GROUP),
    //    ADD (SERVER_OPERATION_GROUP),
    //    ADD (SERVER_PERMISSION_CHANGE_GROUP),
    //    ADD (SERVER_STATE_CHANGE_GROUP),
    //    ADD (TRACE_CHANGE_GROUP),
    //    ADD (AUDIT_CHANGE_GROUP),

    //    -- Database-level actions (across ALL databases)
    //    ADD (DATABASE_CHANGE_GROUP),
    //    ADD (DATABASE_OBJECT_CHANGE_GROUP),
    //    ADD (DATABASE_OWNERSHIP_CHANGE_GROUP),
    //    ADD (DATABASE_PERMISSION_CHANGE_GROUP),
    //    ADD (DATABASE_PRINCIPAL_CHANGE_GROUP),
    //    ADD (DATABASE_ROLE_MEMBER_CHANGE_GROUP),
    //    ADD (SCHEMA_OBJECT_CHANGE_GROUP),
    //    ADD (SCHEMA_OBJECT_OWNERSHIP_CHANGE_GROUP),
    //    ADD (SCHEMA_OBJECT_PERMISSION_CHANGE_GROUP)
    //            WITH (STATE = ON);", conn);
    //                serverSpecCmd.ExecuteNonQuery();

    //                _logger.LogInformation("✅ Comprehensive audit '{auditName}' created", _auditName);
    //            }
    //            else
    //            {
    //                _logger.LogInformation("📋 Ensuring audit '{auditName}' is enabled", _auditName);

    //                // Just ensure audit is enabled
    //                var alterCmd = new SqlCommand(
    //                    $"ALTER SERVER AUDIT [{_auditName}] WITH (STATE = ON);", conn);
    //                alterCmd.ExecuteNonQuery();

    //                // Ensure server specification is enabled
    //                var alterSpecCmd = new SqlCommand(
    //                    $"ALTER SERVER AUDIT SPECIFICATION [{_auditName}_ServerSpec] WITH (STATE = ON);", conn);
    //                try { alterSpecCmd.ExecuteNonQuery(); } catch { /* Might not exist yet */ }

    //                _logger.LogInformation("✅ Audit '{auditName}' is enabled", _auditName);
    //            }
    //        }
    //        catch (Exception ex)
    //        {
    //            _logger.LogError(ex, "❌ Error configuring comprehensive audit");
    //            throw;
    //        }
    //    }
    //}


    //public class SqlServerAuditManager
    //{
    //    private readonly string _conn;
    //    private readonly string _auditPath;
    //    private readonly string _auditName;
    //    private readonly ILogger _logger;

    //    public SqlServerAuditManager(string conn, string auditPath, string auditName, ILogger logger)
    //    {
    //        _conn = conn;
    //        _auditPath = auditPath;
    //        _auditName = auditName;
    //        _logger = logger;
    //    }

    //    public void EnsureAuditOn()
    //    {
    //        using var conn = new SqlConnection(_conn);
    //        conn.Open();

    //        try
    //        {
    //            // Check if audit exists
    //            var existsCmd = new SqlCommand(
    //                "SELECT COUNT(*) FROM sys.server_audits WHERE name = @auditName", conn);
    //            existsCmd.Parameters.AddWithValue("@auditName", _auditName);

    //            var count = (int)existsCmd.ExecuteScalar();

    //            if (count == 0)
    //            {
    //                _logger.LogInformation("🔧 Creating new audit '{auditName}' at path: {path}", _auditName, _auditPath);

    //                // Create the audit directory if it doesn't exist
    //                try
    //                {
    //                    var createDirCmd = new SqlCommand(
    //                        $"EXEC xp_create_subdir '{_auditPath.Replace("\\", "\\\\")}'", conn);
    //                    createDirCmd.ExecuteNonQuery();
    //                }
    //                catch (SqlException)
    //                {
    //                    _logger.LogInformation("📁 Audit directory already exists or cannot be created");
    //                }

    //                var createCmd = new SqlCommand($@"
    //                    CREATE SERVER AUDIT [{_auditName}]
    //                    TO FILE (FILEPATH = '{_auditPath}', MAXSIZE = 1 GB)
    //                    WITH (ON_FAILURE = CONTINUE);
    //                    ALTER SERVER AUDIT [{_auditName}] WITH (STATE = ON);", conn);
    //                createCmd.ExecuteNonQuery();

    //                _logger.LogInformation("✅ Audit '{auditName}' created and enabled", _auditName);
    //            }
    //            else
    //            {
    //                _logger.LogInformation("📋 Audit '{auditName}' exists, checking status", _auditName);

    //                // Check if audit is enabled using is_state_enabled column
    //                var statusCmd = new SqlCommand(
    //                    "SELECT is_state_enabled FROM sys.server_audits WHERE name = @auditName", conn);
    //                statusCmd.Parameters.AddWithValue("@auditName", _auditName);

    //                var isEnabled = Convert.ToBoolean(statusCmd.ExecuteScalar());
    //                var status = isEnabled? "ENABLED" : "DISABLED";

    //                _logger.LogInformation("Audit '{auditName}' current status: {status}", _auditName, status);

    //                if (!isEnabled)
    //                {
    //                    _logger.LogInformation("🔄 Turning ON audit '{auditName}'", _auditName);
    //                    var alterCmd = new SqlCommand(
    //                        $"ALTER SERVER AUDIT [{_auditName}] WITH (STATE = ON);", conn);
    //                    alterCmd.ExecuteNonQuery();
    //                    _logger.LogInformation("✅ Audit '{auditName}' is now ENABLED", _auditName);
    //                }
    //                else
    //                {
    //                    _logger.LogInformation("✅ Audit '{auditName}' is already ENABLED", _auditName);
    //                }
    //            }

    //            // Verify final status
    //            var verifyCmd = new SqlCommand(
    //                "SELECT is_state_enabled FROM sys.server_audits WHERE name = @auditName", conn);
    //            verifyCmd.Parameters.AddWithValue("@auditName", _auditName);
    //            var finalStatus = Convert.ToBoolean(verifyCmd.ExecuteScalar())? "ENABLED" : "DISABLED";

    //            _logger.LogInformation("📊 Final status for '{auditName}': {status}", _auditName, finalStatus);
    //        }
    //        catch (Exception ex)
    //        {
    //            _logger.LogError(ex, "❌ Error managing audit '{auditName}'", _auditName);
    //            throw;
    //        }
    //    }
    //}