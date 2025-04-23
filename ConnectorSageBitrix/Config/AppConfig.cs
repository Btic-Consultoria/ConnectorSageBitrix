using System;
using System.IO;

namespace ConnectorSageBitrix.Config
{
    public class AppConfig
    {
        // Client code for licensing
        public string ClientCode { get; set; }

        public string BitrixClientCode { get; set; }

        // Database settings
        public DatabaseConfig DB { get; set; }

        // Bitrix settings
        public BitrixConfig Bitrix { get; set; }

        // Sync settings
        public SyncConfig Sync { get; set; }

        // App settings
        public AppSettings App { get; set; }

        // Sync enabled flag
        public bool PackEmpresa { get; set; }

        public AppConfig()
        {
            // Initialize with default values
            DB = new DatabaseConfig();
            Bitrix = new BitrixConfig();
            Sync = new SyncConfig();
            App = new AppSettings();

            // Default sync settings
            Sync.Interval = TimeSpan.FromMinutes(5);
            Sync.RetryInterval = TimeSpan.FromSeconds(30);
            Sync.MaxRetries = 3;

            // Default app settings
            App.LogDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ConnectorSageBitrix");

            // Default to disabling synchronization
            PackEmpresa = false;
        }
    }

    public class DatabaseConfig
    {
        public string Host { get; set; }
        public string Port { get; set; }
        public string Database { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public string LicenseID { get; set; }

        public string ConnectionString =>
            $"Server={Host};Database={Database};User Id={User};Password={Password};" +
            (string.IsNullOrEmpty(Port) ? "" : $"Port={Port};");
    }

    public class BitrixConfig
    {
        public string URL { get; set; }
    }

    public class SyncConfig
    {
        public TimeSpan Interval { get; set; }
        public TimeSpan RetryInterval { get; set; }
        public int MaxRetries { get; set; }
    }

    public class AppSettings
    {
        public string LogDir { get; set; }
    }
}