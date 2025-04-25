using System;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using ConnectorSageBitrix.Config;
using ConnectorSageBitrix.Logging;
using ConnectorSageBitrix.Sync;
using ConnectorSageBitrix.Licensing;
using ConnectorSageBitrix.Database;
using ConnectorSageBitrix.Bitrix;
using ConnectorSageBitrix.Repositories;
using System.ComponentModel;
using MyLicenseManager = ConnectorSageBitrix.Licensing.LicenseManager;
using Timer = System.Timers.Timer;

namespace ConnectorSageBitrix
{
    public partial class SyncService : ServiceBase
    {
        private Logger _logger;
        private AppConfig _config;
        private SyncManager _syncManager;
        private System.Timers.Timer _timer;
        private CancellationTokenSource _cancellationTokenSource;
        private DatabaseManager _databaseManager;
        private bool _isRunning = false;

        public SyncService()
        {
            InitializeComponent();
            this.ServiceName = "ConnectorSageBitrix";
            this.CanStop = true;
            this.CanPauseAndContinue = false;
            this.AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            StartService(args);
        }

        protected override void OnStop()
        {
            StopService();
        }

        public void StartService(string[] args)
        {
            try
            {
                _isRunning = true;
                _cancellationTokenSource = new CancellationTokenSource();

                // Setup logging
                string logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ConnectorSageBitrix");
                System.IO.Directory.CreateDirectory(logDir);

                // Initialize logger
                _logger = new Logger(logDir, "[SAGE-BITRIX] ");
                _logger.Info("ConnectorSageBitrix starting up");

                // Load configuration
                _logger.Info("Loading configuration");
                _config = ConfigManager.Load(_logger);
                if (_config == null)
                {
                    _logger.Fatal("Failed to load configuration");
                    Stop();
                    return;
                }

                // Check test mode
                bool testMode = false;
                if (args != null && args.Length > 0)
                {
                    testMode = args[0].ToLower() == "-test";
                }

                // Skip license check in test mode
                if (!testMode)
                {
                    // Check license
                    _logger.Info("Checking license validity");
                    bool licenseValid = CheckLicense();
                    if (!licenseValid)
                    {
                        _logger.Fatal("Invalid license. Application will exit.");
                        Stop();
                        return;
                    }
                }
                else
                {
                    _logger.Info("Running in test mode, license check skipped");
                }

                // Initialize database connection
                _logger.Info("Initializing database connection");
                _databaseManager = new DatabaseManager(_config, _logger);

                // Create repositories
                var socioRepository = new SocioRepository(_databaseManager, _logger);
                var cargoRepository = new CargoRepository(_databaseManager, _logger);
                var actividadRepository = new ActividadRepository(_databaseManager, _logger);
                var modeloRepository = new ModeloRepository(_databaseManager, _logger);

                // Create Bitrix client
                var bitrixClient = new BitrixClient(_config.Bitrix.URL, _logger);

                // Initialize sync manager
                _syncManager = new SyncManager(
                    bitrixClient,
                    socioRepository,
                    cargoRepository,
                    actividadRepository,
                    modeloRepository,
                    _logger,
                    _config
                );

                // Setup timer for periodic sync
                _timer = new Timer();
                _timer.Interval = _config.Sync.Interval.TotalMilliseconds;
                _timer.Elapsed += async (sender, e) => await RunSyncAsync();
                _timer.Start();

                _logger.Info("Application is now running");

                // Run initial sync with a small delay
                Task.Delay(5000).ContinueWith(async _ =>
                {
                    if (_isRunning && _config.PackEmpresa)
                    {
                        await RunSyncAsync();
                    }
                });
            }
            catch (Exception ex)
            {
                if (_logger != null)
                {
                    _logger.Fatal($"Error starting service: {ex.Message}");
                    _logger.Error(ex.ToString());
                }
                Stop();
            }
        }

        public void StopService()
        {
            try
            {
                _isRunning = false;

                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Dispose();
                }

                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                }

                if (_syncManager != null)
                {
                    _syncManager.Dispose();
                }

                if (_databaseManager != null)
                {
                    _databaseManager.Close();
                }

                if (_logger != null)
                {
                    _logger.Info("Application has been shut down gracefully");
                    _logger.Close();
                }
            }
            catch (Exception ex)
            {
                if (_logger != null)
                {
                    _logger.Error($"Error stopping service: {ex.Message}");
                }
            }
        }

        private bool CheckLicense()
        {
            // Get license ID from configuration
            string licenseID = _config.DB.LicenseID;
            if (string.IsNullOrEmpty(licenseID))
            {
                _logger.Error("No license ID found in configuration");
                return false;
            }

            // Get client code from configuration
            string clientCode = _config.BitrixClientCode;
            if (string.IsNullOrEmpty(clientCode))
            {
                _logger.Error("No Bitrix client code found in configuration");
                return false;
            }

            // Create license instance
            var licenseManager = new MyLicenseManager(clientCode, licenseID, _logger);

            // Initial license check
            _logger.Info("Performing initial license verification (with retry logic)...");
            if (!licenseManager.IsValid())
            {
                _logger.Error("License verification failed after all retry attempts. Application will exit.");
                return false;
            }

            _logger.Info("License is valid. Proceeding with application startup.");

            // Setup periodic license checking in a separate task
            Task.Run(async () =>
            {
                // Check license every 24 hours
                while (_isRunning)
                {
                    await Task.Delay(TimeSpan.FromHours(24));

                    if (!_isRunning) break;

                    _logger.Info("Performing periodic license check...");
                    if (!licenseManager.IsValid())
                    {
                        _logger.Error("License has become invalid. Application will exit");
                        Stop();
                        Environment.Exit(1);
                        break;
                    }

                    _logger.Info("Periodic license check passed");
                }
            });

            return true;
        }

        private async Task RunSyncAsync()
        {
            try
            {
                if (!_isRunning) return;

                // Check if sync is enabled
                if (!_config.PackEmpresa)
                {
                    _logger.Info("Skipping scheduled synchronization (pack_empresa = false)");
                    return;
                }

                _logger.Info("Starting scheduled synchronization");
                await _syncManager.SyncAllAsync(_cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error during synchronization: {ex.Message}");
                _logger.Error(ex.ToString());
            }
        }
    }
}