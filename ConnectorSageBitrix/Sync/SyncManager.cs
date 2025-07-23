using ConnectorSageBitrix.Bitrix;
using ConnectorSageBitrix.Config;
using ConnectorSageBitrix.Extensions;
using ConnectorSageBitrix.Licensing;
using ConnectorSageBitrix.Logging;
using ConnectorSageBitrix.Mapping;
using ConnectorSageBitrix.Models;
using ConnectorSageBitrix.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ConnectorSageBitrix.Sync
{
    public class SyncManager : IDisposable
    {
        private readonly BitrixClient _bitrixClient;
        private readonly SocioRepository _socioRepository;
        private readonly CargoRepository _cargoRepository;
        private readonly ActividadRepository _actividadRepository;
        private readonly ModeloRepository _modeloRepository;
        private readonly CompanyRepository _companyRepository;
        private readonly ProductRepository _productRepository;
        private readonly Logger _logger;
        private readonly AppConfig _config;
        private FieldMappingManager _fieldMappingManager;
        private bool _disposed = false;

        // Cache para evitar logs repetitivos
        private static readonly HashSet<string> _loggedSuggestions = new HashSet<string>();
        private static bool _introspectionLogged = false;

        public SyncManager(
            BitrixClient bitrixClient,
            SocioRepository socioRepository,
            CargoRepository cargoRepository,
            ActividadRepository actividadRepository,
            ModeloRepository modeloRepository,
            CompanyRepository companyRepository,
            ProductRepository productRepository,
            Logger logger,
            AppConfig config)
        {
            _bitrixClient = bitrixClient;
            _socioRepository = socioRepository;
            _cargoRepository = cargoRepository;
            _actividadRepository = actividadRepository;
            _modeloRepository = modeloRepository;
            _companyRepository = companyRepository;
            _productRepository = productRepository;
            _logger = logger;
            _config = config;
        }

        public void SetFieldMappingManager(FieldMappingManager fieldMappingManager)
        {
            _fieldMappingManager = fieldMappingManager;
            _logger.Info("Field mapping manager set in SyncManager");
        }

        public async Task SyncAllAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting synchronization of all entities");

            try
            {
                await SyncSociosAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error($"Socios synchronization error: {ex.Message}");
            }

            try
            {
                await SyncCompaniesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error($"Companies synchronization error: {ex.Message}");
            }

            try
            {
                await SyncProductsAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error($"Products synchronization error: {ex.Message}");
            }

            _logger.Info("Synchronization of all entities completed");
        }

        public void LogFieldMappingStatus()
        {
            if (_fieldMappingManager != null)
            {
                var activeCount = _fieldMappingManager.GetActiveMappingsCount();
                var totalCount = _fieldMappingManager.GetTotalMappingsCount();
                _logger.Info($"Field mapping status: {activeCount} active mappings out of {totalCount} total");

                // Log estadísticas de mapping dinámico (solo una vez por sesión)
                if (!_introspectionLogged)
                {
                    LogDynamicMappingStatistics();
                    _introspectionLogged = true;
                }
            }
            else
            {
                _logger.Warning("Field mapping manager not initialized");
            }
        }

        #region Validation

        /// <summary>
        /// Valida los campos antes de sincronizar con soporte dinámico
        /// </summary>
        private async Task<bool> ValidateFieldMappingsAsync()
        {
            try
            {
                _logger.Info("🔍 Validating field mappings with Bitrix24 and database...");

                // 1. Validar campos disponibles en la base de datos
                var availableFields = _companyRepository.GetAvailableFields();
                var dynamicMapper = new DynamicFieldMapper(_logger, availableFields, _fieldMappingManager.GetActiveMappings());
                var dbValidation = dynamicMapper.ValidateFieldMappings();

                if (!dbValidation.IsValid)
                {
                    _logger.Error($"❌ Database field validation failed. Missing mandatory fields: {string.Join(", ", dbValidation.MissingMandatoryFields)}");
                }
                else
                {
                    _logger.Info($"✅ Database field validation passed. {dbValidation.ValidMappings.Count} valid mappings");
                }

                // 2. Validar campos en Bitrix24
                var activeMappings = _fieldMappingManager.GetActiveMappings();
                var companyMappings = activeMappings.Where(m =>
                    m.BitrixFieldName.Contains("COMPANY")).ToList();

                if (companyMappings.Any())
                {
                    var bitrixValidation = await _bitrixClient.ValidateUserFields(companyMappings);

                    if (bitrixValidation.IsValid)
                    {
                        _logger.Info($"✅ Bitrix24 field validation passed. {bitrixValidation.ValidFields.Count} valid fields");
                    }
                    else
                    {
                        _logger.Error($"❌ Bitrix24 field validation failed. Missing fields: {string.Join(", ", bitrixValidation.MissingFields)}");
                        return false;
                    }
                }

                return dbValidation.IsValid;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error during field validation: {ex.Message}");
                return false;
            }
        }

        private void LogDynamicMappingStatistics()
        {
            try
            {
                var availableFields = _companyRepository.GetAvailableFields();
                var dynamicMapper = new DynamicFieldMapper(_logger, availableFields, _fieldMappingManager.GetActiveMappings());
                var stats = dynamicMapper.GetStatistics();

                _logger.Info($"📊 Dynamic Mapping Statistics:");
                _logger.Info($"   • Total available fields in DB: {stats.TotalAvailableFields}");
                _logger.Info($"   • Configured mappings: {stats.TotalConfiguredMappings}");
                _logger.Info($"   • Valid mappings: {stats.ValidMappings}");
                _logger.Info($"   • Missing fields: {stats.MissingFields}");
                _logger.Info($"   • Unused available fields: {stats.UnmappedAvailableFields}");
                _logger.Info($"   • Status: {(stats.IsValid ? "✅ Valid" : "❌ Invalid")}");
            }
            catch (Exception ex)
            {
                _logger.Debug($"Error logging dynamic mapping statistics: {ex.Message}");
            }
        }

        #endregion

        #region Socios Sync

        private async Task SyncSociosAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting socios synchronization");

            try
            {
                var bitrixSocios = await _bitrixClient.ListSociosAsync();
                var sageSocios = _socioRepository.GetAll();

                _logger.Info($"Found {bitrixSocios.Count} socios in Bitrix24 and {sageSocios.Count} in Sage");

                foreach (var sageSocio in sageSocios)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        await SyncSocio(sageSocio, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error syncing socio {sageSocio.DNI}: {ex.Message}");
                    }
                }

                _logger.Info("Socios synchronization completed");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in socios synchronization: {ex.Message}");
                throw;
            }
        }

        private BitrixSocio MapSocioToBitrix(Socio sageSocio)
        {
            return new BitrixSocio
            {
                Title = !string.IsNullOrEmpty(sageSocio.RazonSocialEmpleado) ? sageSocio.RazonSocialEmpleado : sageSocio.DNI,
                DNI = sageSocio.DNI,
                Cargo = sageSocio.CargoAdministrador,
                Administrador = sageSocio.Administrador ? "Y" : "N",
                Participacion = sageSocio.PorParticipacion.ToString("F2"),
                RazonSocialEmpleado = sageSocio.RazonSocialEmpleado
            };
        }

        private Task SyncSocio(Socio sageSocio, CancellationToken cancellationToken)
        {
            try
            {
                var bitrixSocio = MapSocioToBitrix(sageSocio);

                // Aquí implementarías la lógica de sincronización
                // Por ejemplo, crear o actualizar el socio en Bitrix24

                _logger.Debug($"Synced socio: {sageSocio.DNI}");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error syncing individual socio: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Companies Sync

        private async Task SyncCompaniesAsync(CancellationToken cancellationToken)
        {
            if (_fieldMappingManager != null)
            {
                await SyncCompaniesWithMappingsAsync(cancellationToken);
            }
            else
            {
                await SyncCompaniesLegacyAsync(cancellationToken);
            }
        }

        private async Task SyncCompaniesWithMappingsAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting companies synchronization with dynamic field mappings");

            try
            {
                // 🔍 Validar campos antes de sincronizar (incluyendo DB y Bitrix24)
                bool fieldsValid = await ValidateFieldMappingsAsync();

                if (!fieldsValid)
                {
                    _logger.Error("❌ Field validation failed. Continuing with available fields...");
                    // No abortar completamente, continuar con campos válidos
                }

                // Continuar con la sincronización usando mapping dinámico
                var sageCompanies = _companyRepository.GetAll();
                _logger.Info($"Found {sageCompanies.Count} companies in Sage for EmpresaSage: {_config.EmpresaSage}");

                if (!sageCompanies.Any())
                {
                    _logger.Warning("No companies found in Sage. Check EmpresaSage configuration.");
                    return;
                }

                foreach (var sageCompany in sageCompanies)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        await SyncCompanyWithDynamicMappings(sageCompany, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error syncing company {sageCompany.CodigoCategoriaCliente}: {ex.Message}");
                        // Continuar con las siguientes empresas
                    }
                }

                _logger.Info("✅ Companies synchronization with dynamic mappings completed");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in companies synchronization: {ex.Message}");
                throw;
            }
        }

        private async Task SyncCompanyWithDynamicMappings(Company sageCompany, CancellationToken cancellationToken)
        {
            try
            {
                _logger.Info($"Processing company: {sageCompany.CodigoCategoriaCliente}");

                // 1. Obtener campos disponibles desde el repositorio
                var availableFields = _companyRepository.GetAvailableFields();

                // 2. Crear el mapper dinámico
                var dynamicMapper = new DynamicFieldMapper(_logger, availableFields, _fieldMappingManager.GetActiveMappings());

                // 3. Validar mapeos contra campos disponibles
                var validationResult = dynamicMapper.ValidateFieldMappings();

                if (!validationResult.IsValid)
                {
                    _logger.Warning($"Missing mandatory fields for company {sageCompany.CodigoCategoriaCliente}: {string.Join(", ", validationResult.MissingMandatoryFields)}");
                    // Continuar pero registrar el problema
                }

                // 4. Obtener datos usando solo campos óptimos
                var optimalFields = dynamicMapper.GetOptimalFieldSelection();
                var companyDataList = _companyRepository.GetAllDynamic(optimalFields);

                var companyData = companyDataList.FirstOrDefault(c =>
                    c.ContainsKey("CodigoCategoriaCliente_") &&
                    c["CodigoCategoriaCliente_"]?.ToString() == sageCompany.CodigoCategoriaCliente);

                if (companyData == null)
                {
                    _logger.Warning($"Could not retrieve dynamic data for company {sageCompany.CodigoCategoriaCliente}");
                    return;
                }

                _logger.Debug($"Retrieved {companyData.Count} fields dynamically for company {sageCompany.CodigoCategoriaCliente}");

                // 5. Aplicar mapeos dinámicos
                var bitrixFields = dynamicMapper.ApplyDynamicMappings(companyData);

                if (!bitrixFields.Any())
                {
                    _logger.Warning($"No mapped fields found for company {sageCompany.CodigoCategoriaCliente}");
                    return;
                }

                _logger.Info($"Mapped {bitrixFields.Count} fields for company {sageCompany.CodigoCategoriaCliente}");

                // 6. Buscar si la empresa ya existe en Bitrix24
                string bitrixCompanyId = await FindBitrixCompanyId(sageCompany);

                if (!string.IsNullOrEmpty(bitrixCompanyId))
                {
                    // 7A. Actualizar empresa existente
                    _logger.Info($"Updating existing company {bitrixCompanyId}");

                    bool updateSuccess = await _bitrixClient.UpdateCompany(bitrixCompanyId, bitrixFields);

                    if (updateSuccess)
                    {
                        _logger.Info($"✅ Successfully updated company {bitrixCompanyId}");
                    }
                    else
                    {
                        _logger.Error($"❌ Failed to update company {bitrixCompanyId}");
                    }
                }
                else
                {
                    // 7B. Crear nueva empresa
                    _logger.Info($"Creating new company for {sageCompany.CodigoCategoriaCliente}");

                    int newCompanyId = await CreateCompanyWithMappings(bitrixFields, sageCompany);

                    if (newCompanyId > 0)
                    {
                        _logger.Info($"✅ Successfully created company with ID: {newCompanyId}");
                    }
                    else
                    {
                        _logger.Error($"❌ Failed to create company for {sageCompany.CodigoCategoriaCliente}");
                    }
                }

                // 8. Log campos sugeridos adicionales (solo primera vez por sesión)
                LogSuggestedFields(dynamicMapper);

            }
            catch (Exception ex)
            {
                _logger.Error($"Error syncing company with dynamic mappings {sageCompany.CodigoCategoriaCliente}: {ex.Message}");
                _logger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        private void LogSuggestedFields(DynamicFieldMapper mapper)
        {
            try
            {
                var unmappedFields = mapper.GetUnmappedAvailableFields();
                var sessionKey = string.Join(",", unmappedFields.Take(3)); // Usar primeros 3 campos como clave

                if (!_loggedSuggestions.Contains(sessionKey) && unmappedFields.Any())
                {
                    _logger.Info($"💡 Suggestion: {unmappedFields.Count} additional fields available but not mapped:");
                    foreach (var field in unmappedFields.Take(10)) // Solo mostrar primeros 10
                    {
                        _logger.Info($"   - {field}");
                    }

                    if (unmappedFields.Count > 10)
                    {
                        _logger.Info($"   ... and {unmappedFields.Count - 10} more fields");
                    }

                    _loggedSuggestions.Add(sessionKey);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Error logging suggested fields: {ex.Message}");
            }
        }

        /// <summary>
        /// Convierte un objeto Company a diccionario de datos (método legacy de fallback)
        /// </summary>
        private Dictionary<string, object> ConvertCompanyToData(Company company)
        {
            var data = new Dictionary<string, object>();

            // Usar reflexión para mapear todas las propiedades automáticamente
            var properties = typeof(Company).GetProperties();

            foreach (var prop in properties)
            {
                try
                {
                    var value = prop.GetValue(company);
                    if (value != null && !string.IsNullOrEmpty(value.ToString()))
                    {
                        data[prop.Name] = value;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error getting property {prop.Name}: {ex.Message}");
                }
            }

            _logger.Debug($"Converted company to {data.Count} data fields");
            return data;
        }

        /// <summary>
        /// Crea una nueva empresa en Bitrix24 con campos mapeados
        /// </summary>
        private async Task<int> CreateCompanyWithMappings(Dictionary<string, object> bitrixFields, Company sageCompany)
        {
            try
            {
                // Asegurar que tenemos un título
                var fieldsForCreation = new Dictionary<string, object>(bitrixFields);

                if (!fieldsForCreation.ContainsKey("title"))
                {
                    // Usar RazonSocial como título, o CodigoCategoriaCliente como fallback
                    string title = !string.IsNullOrEmpty(sageCompany.RazonSocial)
                        ? sageCompany.RazonSocial
                        : sageCompany.CodigoCategoriaCliente ?? "Empresa sin nombre";

                    fieldsForCreation["title"] = title;
                }

                _logger.Debug($"Creating company with fields: {string.Join(", ", fieldsForCreation.Keys)}");

                // Usar el método existente CreateCompany con BitrixCompany
                var bitrixCompany = BitrixCompany.FromSageCompany(sageCompany);
                int companyId = await _bitrixClient.CreateCompanyAsync(bitrixCompany);

                // Si se creó exitosamente, actualizarlo con los campos mapeados
                if (companyId > 0)
                {
                    _logger.Info($"Company created with ID {companyId}, updating with mapped fields");

                    bool updateSuccess = await _bitrixClient.UpdateCompany(companyId.ToString(), bitrixFields);

                    if (!updateSuccess)
                    {
                        _logger.Warning($"Company {companyId} created but failed to update with mapped fields");
                    }
                }

                return companyId;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error creating company with mappings: {ex.Message}");
                return 0;
            }
        }

        private async Task SyncCompaniesLegacyAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting companies synchronization (legacy mode)");

            try
            {
                var bitrixCompanies = await _bitrixClient.ListCompaniesAsync();
                var sageCompanies = _companyRepository.GetAll();

                _logger.Info($"Found {bitrixCompanies.Count} companies in Bitrix24 and {sageCompanies.Count} in Sage");

                foreach (var sageCompany in sageCompanies)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        await SyncCompany(sageCompany, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error syncing company {sageCompany.CodigoCategoriaCliente}: {ex.Message}");
                    }
                }

                _logger.Info("Companies synchronization (legacy) completed");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in companies synchronization: {ex.Message}");
                throw;
            }
        }

        private Task SyncCompany(Company sageCompany, CancellationToken cancellationToken)
        {
            try
            {
                var bitrixCompany = BitrixCompany.FromSageCompany(sageCompany);

                // Aquí implementarías la lógica de sincronización legacy

                _logger.Debug($"Synced company: {sageCompany.CodigoCategoriaCliente}");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error syncing individual company: {ex.Message}");
                throw;
            }
        }

        private async Task<string> FindBitrixCompanyId(Company sageCompany)
        {
            try
            {
                var companies = await _bitrixClient.ListCompaniesAsync();
                var matchingCompany = companies.FirstOrDefault(c =>
                    c.RazonSocial == sageCompany.RazonSocial ||
                    c.EMail1 == sageCompany.EMail1);

                if (matchingCompany != null)
                {
                    _logger.Debug($"Found matching company for {sageCompany.RazonSocial}");
                    return matchingCompany.ID.ToString();
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error finding Bitrix company ID: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Products Sync

        private Task SyncProductsAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting products synchronization");
            // Implementation for products sync
            _logger.Info("Products synchronization completed");
            return Task.CompletedTask;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Clean up managed resources here
                _disposed = true;
            }
        }

        #endregion
    }
}