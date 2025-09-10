using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Audit_final.Models
{
    public class SqlServerConfig
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ServerAddress { get; set; } = string.Empty; // IP or hostname
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string AuditFolder { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
        public DateTime LastUpdated { get; set; }

        public string AdminUsername { get; set; } = string.Empty;
        public string AdminPassword { get; set; } = string.Empty;


        // Build connection string from components
        public string ConnectionString =>
            $"Server={ServerAddress};Database=master;User Id={Username};Password={Password};TrustServerCertificate=True;";
    }
}
