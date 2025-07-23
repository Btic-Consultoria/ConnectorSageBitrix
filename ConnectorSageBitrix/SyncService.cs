using System;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using ConnectorSageBitrix.Config;
using ConnectorSageBitrix.Logging;
using ConnectorSageBitrix.Extensions;
using ConnectorSageBitrix.Sync;
using ConnectorSageBitrix.Licensing;
using ConnectorSageBitrix.Database;
using ConnectorSageBitrix.Bitrix;
using ConnectorSageBitrix.Repositories;
using ConnectorSageBitrix.Models;
using System.IO;
using System.Diagnostics;
using Timer = System.Timers.Timer;

namespace ConnectorSageBitrix
{
    public partial class SyncService : ServiceBase
    {
        private Logger _logger;
        private AppConfig _config;
        private SyncManager _syncManager;
        private FieldMappingManager _fieldMappingManager;
        private Timer _timer;
        private CancellationTokenSource _cancellationTokenSource;
        private DatabaseManager _databaseManager;
        private bool _isRunning = false;

        public SyncService()
        {
            InitializeComponent();
            ServiceName = "ConnectorSageBitrix";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
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
            var startThread = new Thread(() =>
            {
                try
                {
                    StartService();
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

        public void StartService()
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
                _config = ConfigManager.LoadConfig();
                if (_config == null)
                {
                    throw new Exception("No se pudo cargar la configuración");
                }

                _logger.Info($"Configuration loaded - PackEmpresa: {_config.PackEmpresa}");
                _logger.Info($"Field mappings loaded: {_config.FieldMappings?.Count ?? 0}");

                File.AppendAllText(diagFile, $"{DateTime.Now}: Configuración cargada\r\n");

                // Initialize field mapping manager
                _logger.Info("Initializing field mapping manager");
                _fieldMappingManager = new FieldMappingManager(_config.FieldMappings, _logger);

                // Validate mandatory mappings
                var missingMappings = _fieldMappingManager.ValidateMandatoryMappings();
                if (missingMappings.Count > 0)
                {
                    _logger.Warning($"Missing mandatory field mappings: {string.Join(", ", missingMappings)}");
                }

                _logger.Info($"Field mapping manager initialized with {_config.FieldMappings.Count} mappings " +
                            $"({_fieldMappingManager.GetActiveMappings().Count} active)");

                File.AppendAllText(diagFile, $"{DateTime.Now}: FieldMappingManager inicializado\r\n");

                // Validate license
                var licenseManager = new ConnectorSageBitrix.Licensing.LicenseManager(_config.ClientCode, _config.DB.LicenseID, _logger);
                if (!licenseManager.IsValid())
                {
                    throw new Exception("Licencia inválida o expirada");
                }

                _logger.Info("License validation passed");
                File.AppendAllText(diagFile, $"{DateTime.Now}: Licencia validada\r\n");

                // Initialize database manager
                _databaseManager = new DatabaseManager(_config, _logger);

                _logger.Info("Database connection established");
                File.AppendAllText(diagFile, $"{DateTime.Now}: Base de datos conectada\r\n");

                // Obtener EmpresaSage de la configuración (por defecto "1")
                string empresaSage = _config.EmpresaSage ?? "1";
                _logger.Info($"Using EmpresaSage: {empresaSage}");

                // Initialize repositories with EmpresaSage
                var socioRepository = new SocioRepository(_databaseManager, _logger);
                var cargoRepository = new CargoRepository(_databaseManager, _logger);
                var actividadRepository = new ActividadRepository(_databaseManager, _logger);
                var modeloRepository = new ModeloRepository(_databaseManager, _logger);
                var companyRepository = new CompanyRepository(_databaseManager, _logger, empresaSage);
                var productRepository = new ProductRepository(_databaseManager, _logger);

                File.AppendAllText(diagFile, $"{DateTime.Now}: Repositorios creados con EmpresaSage: {empresaSage}\r\n");

                // Realizar introspección de campos disponibles
                try
                {
                    _logger.Info("Performing database field introspection...");
                    var availableFields = companyRepository.GetAvailableFields();
                    _logger.Info($"Database introspection found {availableFields.Count} available fields in Clientes table");

                    // Log algunos campos importantes para debug
                    var importantFields = new[] { "CodigoCategoriaCliente_", "RazonSocial", "EMail1", "Telefono", "CodigoDivisa" };
                    foreach (var field in importantFields)
                    {
                        if (availableFields.ContainsKey(field))
                        {
                            _logger.Debug($"✅ Found important field: {field} ({availableFields[field].Name})");
                        }
                        else
                        {
                            _logger.Warning($"❌ Missing important field: {field}");
                        }
                    }

                    File.AppendAllText(diagFile, $"{DateTime.Now}: Introspección de BD completada - {availableFields.Count} campos\r\n");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Could not perform database introspection: {ex.Message}");
                    File.AppendAllText(diagFile, $"{DateTime.Now}: Error en introspección: {ex.Message}\r\n");
                }

                // Initialize Bitrix client
                BitrixClient bitrixClient = null;
                if (_config.PackEmpresa)
                {
                    bitrixClient = new BitrixClient(_config.Bitrix.URL, _logger);
                    _logger.Info("Bitrix client initialized");
                }

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

                // Set field mapping manager in sync manager
                _syncManager.SetFieldMappingManager(_fieldMappingManager);

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
                        if (_logger != null)
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
                    _syncManager.Dispose();

                if (_databaseManager != null)
                    _databaseManager.Close();

                if (_logger != null)
                {
                    _logger.Info("Application has been shut down gracefully");
                    _logger.Close();
                }
            }
            catch (Exception ex)
            {
                if (_logger != null)
                    _logger.Error($"Error stopping service: {ex.Message}");
            }
        }

        private async Task RunSyncAsync()
        {
            if (!_isRunning || _syncManager == null)
                return;

            try
            {
                _logger.Debug("Starting synchronization cycle");

                // Log field mapping status periodically
                _syncManager.LogFieldMappingStatus();

                await _syncManager.SyncAllAsync(_cancellationTokenSource.Token);
                _logger.Debug("Synchronization cycle completed");
            }
            catch (OperationCanceledException)
            {
                _logger.Info("Synchronization cancelled");
            }
            catch (Exception ex)
            {
                _logger.Error($"Synchronization error: {ex.Message}");
                _logger.Error(ex.ToString());
            }
        }
    }
}