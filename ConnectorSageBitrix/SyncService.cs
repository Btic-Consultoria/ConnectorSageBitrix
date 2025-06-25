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
using System.IO;
using System.Diagnostics;

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
        private readonly int _startupTimeoutMs = 20000; // 20 segundos máximo para iniciar

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
            // Registrar evento al empezar
            try
            {
                EventLog.WriteEntry(ServiceName, "Iniciando el servicio ConnectorSageBitrix", EventLogEntryType.Information);
            }
            catch { /* Ignorar errores de registro de eventos */ }

            // Crear directorio de diagnóstico
            string diagDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ConnectorSageBitrix");

            try
            {
                Directory.CreateDirectory(diagDir);
                File.AppendAllText(
                    Path.Combine(diagDir, "service-start.log"),
                    $"{DateTime.Now}: Servicio iniciándose...\r\n"
                );
            }
            catch { /* Ignorar errores de archivo */ }

            // Iniciar servicio en un hilo separado para evitar el timeout
            Thread startThread = new Thread(() =>
            {
                try
                {
                    StartService(args);
                }
                catch (Exception ex)
                {
                    try
                    {
                        string errorMsg = $"{DateTime.Now}: Error al iniciar: {ex.Message}\r\n{ex.StackTrace}\r\n";
                        File.AppendAllText(Path.Combine(diagDir, "startup-error.log"), errorMsg);

                        EventLog.WriteEntry(ServiceName,
                            $"Error al iniciar el servicio: {ex.Message}",
                            EventLogEntryType.Error);
                    }
                    catch { /* Ignorar errores de registro */ }

                    Stop();
                }
            });

            startThread.IsBackground = true;
            startThread.Start();
        }

        protected override void OnStop()
        {
            try
            {
                EventLog.WriteEntry(ServiceName, "Deteniendo servicio ConnectorSageBitrix", EventLogEntryType.Information);
            }
            catch { /* Ignorar errores de registro de eventos */ }

            StopService();
        }

        public void StartService(string[] args)
        {
            string diagFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ConnectorSageBitrix",
                "startup-steps.log");

            try
            {
                File.AppendAllText(diagFile, $"{DateTime.Now}: Iniciando servicio\r\n");
            }
            catch { /* Ignorar error de archivo */ }

            try
            {
                _isRunning = true;
                _cancellationTokenSource = new CancellationTokenSource();

                // Setup logging
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ConnectorSageBitrix");
                Directory.CreateDirectory(logDir);

                // Initialize logger
                _logger = new Logger(logDir, "[SAGE-BITRIX] ");
                _logger.Info("ConnectorSageBitrix starting up");

                File.AppendAllText(diagFile, $"{DateTime.Now}: Logger inicializado\r\n");

                // Load configuration
                _logger.Info("Loading configuration");
                _config = ConfigManager.Load(_logger);
                if (_config == null)
                {
                    _logger.Fatal("Failed to load configuration");
                    File.AppendAllText(diagFile, $"{DateTime.Now}: ERROR - Falló carga de configuración\r\n");
                    Stop();
                    return;
                }

                File.AppendAllText(diagFile, $"{DateTime.Now}: Configuración cargada correctamente\r\n");

                // Check test mode
                bool testMode = false;
                if (args != null && args.Length > 0)
                {
                    testMode = args[0].ToLower() == "-test";
                }

                // Create offline-mode flag if needed in test mode
                if (testMode)
                {
                    string offlinePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "Btic", "licenses", "offline-mode.txt");

                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(offlinePath));
                        File.WriteAllText(offlinePath,
                            $"Test mode enabled on: {DateTime.Now}\r\n" +
                            $"This file allows the service to run without contacting the license server.");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Failed to create offline mode marker: {ex.Message}");
                    }
                }

                // Check license (with timing)
                _logger.Info("Checking license validity");
                File.AppendAllText(diagFile, $"{DateTime.Now}: Iniciando verificación de licencia\r\n");

                Stopwatch licenseTimer = Stopwatch.StartNew();
                bool licenseValid = CheckLicense();
                licenseTimer.Stop();

                _logger.Info($"License check completed in {licenseTimer.ElapsedMilliseconds}ms");
                File.AppendAllText(diagFile,
                    $"{DateTime.Now}: Verificación de licencia completada en {licenseTimer.ElapsedMilliseconds}ms. " +
                    $"Resultado: {(licenseValid ? "Válida" : "Inválida")}\r\n");

                if (!licenseValid)
                {
                    _logger.Fatal("Invalid license. Application will exit.");
                    File.AppendAllText(diagFile, $"{DateTime.Now}: ERROR - Licencia inválida\r\n");
                    Stop();
                    return;
                }

                // Initialize database connection
                _logger.Info("Initializing database connection");
                File.AppendAllText(diagFile, $"{DateTime.Now}: Inicializando conexión a base de datos\r\n");

                try
                {
                    _databaseManager = new DatabaseManager(_config, _logger);
                }
                catch (Exception dbEx)
                {
                    _logger.Fatal($"Database connection failed: {dbEx.Message}");
                    _logger.Error(dbEx.ToString());
                    File.AppendAllText(diagFile, $"{DateTime.Now}: ERROR - Falló conexión a base de datos: {dbEx.Message}\r\n");
                    Stop();
                    return;
                }

                File.AppendAllText(diagFile, $"{DateTime.Now}: Conexión a base de datos establecida\r\n");

                // Create repositories
                var socioRepository = new SocioRepository(_databaseManager, _logger);
                var cargoRepository = new CargoRepository(_databaseManager, _logger);
                var actividadRepository = new ActividadRepository(_databaseManager, _logger);
                var modeloRepository = new ModeloRepository(_databaseManager, _logger);
                var companyRepository = new CompanyRepository(_databaseManager, _logger);
                var productRepository = new ProductRepository(_databaseManager, _logger);

                File.AppendAllText(diagFile, $"{DateTime.Now}: Repositorios creados\r\n");

                // Create Bitrix client
                var bitrixClient = new BitrixClient(_config.Bitrix.URL, _logger);

                File.AppendAllText(diagFile, $"{DateTime.Now}: Cliente Bitrix creado\r\n");

                // Initialize sync manager
                _logger.Info("Initializing sync manager");
                _syncManager = new SyncManager(
                    bitrixClient,
                    socioRepository,
                    cargoRepository,
                    actividadRepository,
                    modeloRepository,
                    companyRepository,
                    productRepository,
                    _logger,
                    _config
                );

                File.AppendAllText(diagFile, $"{DateTime.Now}: SyncManager inicializado\r\n");

                // Setup timer for periodic sync
                _timer = new Timer();
                _timer.Interval = _config.Sync.Interval.TotalMilliseconds;
                _timer.Elapsed += async (sender, e) => await RunSyncAsync();
                _timer.Start();

                File.AppendAllText(diagFile,
                    $"{DateTime.Now}: Timer iniciado con intervalo de {_config.Sync.Interval.TotalMinutes} minutos\r\n");

                _logger.Info("Application is now running");
                File.AppendAllText(diagFile, $"{DateTime.Now}: Aplicación iniciada correctamente\r\n");

                // Run initial sync with a small delay
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(5000);
                        if (_isRunning && _config.PackEmpresa)
                        {
                            File.AppendAllText(diagFile, $"{DateTime.Now}: Iniciando sincronización inicial\r\n");
                            await RunSyncAsync();
                            File.AppendAllText(diagFile, $"{DateTime.Now}: Sincronización inicial completada\r\n");
                        }
                    }
                    catch (Exception syncEx)
                    {
                        _logger.Error($"Initial sync error: {syncEx.Message}");
                        File.AppendAllText(diagFile, $"{DateTime.Now}: ERROR en sincronización inicial: {syncEx.Message}\r\n");
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
                else
                {
                    File.AppendAllText(diagFile, $"{DateTime.Now}: ERROR CRÍTICO: {ex.Message}\r\n{ex.StackTrace}\r\n");
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

                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ConnectorSageBitrix");

                try
                {
                    File.AppendAllText(
                        Path.Combine(logDir, "stop-error.log"),
                        $"{DateTime.Now}: Error deteniendo servicio: {ex.Message}\r\n{ex.StackTrace}\r\n"
                    );
                }
                catch { /* Ignorar errores de archivo */ }
            }
        }

        private bool CheckLicense()
        {
            try
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

                // Initial license check with timeout
                _logger.Info("Performing initial license verification...");
                Task<bool> licenseTask = Task.Run(() => licenseManager.IsValid());

                // Esperar con timeout
                if (!licenseTask.Wait(_startupTimeoutMs))
                {
                    _logger.Error($"License verification timed out after {_startupTimeoutMs / 1000} seconds. Using offline mode for startup.");

                    // Crear archivo de modo offline para permitir el inicio
                    try
                    {
                        string offlinePath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                            "Btic", "licenses", "offline-mode.txt");

                        Directory.CreateDirectory(Path.GetDirectoryName(offlinePath));
                        File.WriteAllText(offlinePath,
                            $"Auto-generated on: {DateTime.Now}\r\n" +
                            $"Created due to license server timeout.\r\n" +
                            $"Delete this file to restore normal license validation.");

                        _logger.Info("Created offline-mode.txt to bypass license server temporarily");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error creating offline mode file: {ex.Message}");
                    }

                    // Continuar como válido temporalmente para permitir inicio de servicio
                    return true;
                }

                bool licenseValid = licenseTask.Result;
                if (!licenseValid)
                {
                    _logger.Error("License verification failed. Application will exit.");
                    return false;
                }

                _logger.Info("License is valid. Proceeding with application startup.");

                // Setup periodic license checking in a separate task
                Task.Run(async () =>
                {
                    // Wait a bit before first check
                    await Task.Delay(TimeSpan.FromHours(1));

                    // Check license every 24 hours
                    while (_isRunning)
                    {
                        try
                        {
                            _logger.Info("Performing periodic license check...");
                            bool isValid = licenseManager.IsValid();

                            if (!isValid)
                            {
                                _logger.Error("Periodic license check failed - license may have expired");
                                // No detener el servicio, sólo registrar el error
                            }
                            else
                            {
                                _logger.Info("Periodic license check passed");

                                // Borrar archivo de modo offline si existe
                                string offlinePath = Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                                    "Btic", "licenses", "offline-mode.txt");

                                if (File.Exists(offlinePath))
                                {
                                    try
                                    {
                                        File.Delete(offlinePath);
                                        _logger.Info("Deleted offline-mode.txt file after successful license verification");
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"Error during periodic license check: {ex.Message}");
                        }

                        // Esperar 24 horas hasta el próximo chequeo
                        await Task.Delay(TimeSpan.FromHours(24));
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error durante la comprobación de licencia: {ex.Message}");
                _logger.Error(ex.ToString());

                // Crear archivo de modo offline para permitir el inicio
                try
                {
                    string offlinePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "Btic", "licenses", "offline-mode.txt");

                    Directory.CreateDirectory(Path.GetDirectoryName(offlinePath));
                    File.WriteAllText(offlinePath,
                        $"Auto-generated on: {DateTime.Now}\r\n" +
                        $"Created due to error during license validation: {ex.Message}\r\n" +
                        $"Delete this file to restore normal license validation.");

                    _logger.Info("Created offline-mode.txt to bypass license server due to error");

                    // Permitir inicio a pesar del error
                    return true;
                }
                catch
                {
                    // Si ni siquiera podemos crear el archivo, fallamos
                    return false;
                }
            }
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