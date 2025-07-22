using ConnectorSageBitrix.Bitrix;
using ConnectorSageBitrix.Config;
using ConnectorSageBitrix.Licensing;
using ConnectorSageBitrix.Logging;
using ConnectorSageBitrix.Models;
using ConnectorSageBitrix.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
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

        private async Task SyncCompaniesWithMappingsAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting companies synchronization with field mappings");

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
                        await SyncCompanyWithMappings(sageCompany, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error syncing company {sageCompany.CodigoCategoriaCliente}: {ex.Message}");
                    }
                }

                _logger.Info("Companies synchronization with mappings completed");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in companies synchronization: {ex.Message}");
                throw;
            }
        }

        private async Task SyncCompanyWithMappings(Company sageCompany, CancellationToken cancellationToken)
        {
            try
            {
                // Convertir datos de Sage a diccionario
                var sageData = new Dictionary<string, object>();

                // Mapear propiedades de Sage según la estructura de tu modelo Company
                if (!string.IsNullOrEmpty(sageCompany.CodigoCategoriaCliente))
                    sageData["CodigoCategoriaCliente"] = sageCompany.CodigoCategoriaCliente;
                if (!string.IsNullOrEmpty(sageCompany.RazonSocial))
                    sageData["RazonSocial"] = sageCompany.RazonSocial;
                if (!string.IsNullOrEmpty(sageCompany.CodigoDivisa))
                    sageData["CodigoDivisa"] = sageCompany.CodigoDivisa;
                if (!string.IsNullOrEmpty(sageCompany.Domicilio))
                    sageData["Domicilio"] = sageCompany.Domicilio;
                if (!string.IsNullOrEmpty(sageCompany.Telefono))
                    sageData["Telefono"] = sageCompany.Telefono;
                if (!string.IsNullOrEmpty(sageCompany.EMail1))
                    sageData["EMail1"] = sageCompany.EMail1;

                _logger.Debug($"Sage data for mapping: {string.Join(", ", sageData.Keys)}");

                // Aplicar mapeos para obtener datos de Bitrix
                var bitrixFields = _fieldMappingManager.ApplyMappings(sageData);

                if (bitrixFields.Any())
                {
                    _logger.Info($"Updating Bitrix company {sageCompany.CodigoCategoriaCliente} with {bitrixFields.Count} mapped fields");

                    // Buscar si la empresa ya existe en Bitrix24
                    string bitrixCompanyId = await FindBitrixCompanyId(sageCompany);

                    if (!string.IsNullOrEmpty(bitrixCompanyId))
                    {
                        // Actualizar empresa existente
                        bool success = await _bitrixClient.UpdateCompany(bitrixCompanyId, bitrixFields);
                        if (success)
                        {
                            _logger.Info($"Successfully updated company {bitrixCompanyId}");
                        }
                        else
                        {
                            _logger.Warning($"Failed to update company {bitrixCompanyId}");
                        }
                    }
                    else
                    {
                        _logger.Debug($"Company {sageCompany.CodigoCategoriaCliente} not found in Bitrix24 - would need to create");
                    }
                }
                else
                {
                    _logger.Warning($"No mapped fields found for company {sageCompany.CodigoCategoriaCliente}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error syncing company with mappings: {ex.Message}");
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

        private Task SyncProductsAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting products synchronization");
            // Implementation for products sync
            _logger.Info("Products synchronization completed");
            return Task.CompletedTask;
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

        /// <summary>
        /// Método para obtener información de debug sobre mapeos
        /// </summary>
        public void LogFieldMappingStatus()
        {
            if (_fieldMappingManager == null)
            {
                _logger.Warning("Field mapping manager not initialized");
                return;
            }

            var activeMappings = _fieldMappingManager.GetActiveMappings();
            _logger.Info($"Field Mapping Status:");
            _logger.Info($"- Total mappings: {activeMappings.Count}");
            _logger.Info($"- Mandatory mappings: {activeMappings.Count(m => m.IsMandatory)}");
            _logger.Info($"- Optional mappings: {activeMappings.Count(m => !m.IsMandatory)}");

            foreach (var mapping in activeMappings.Take(5)) // Solo mostrar los primeros 5 para no saturar logs
            {
                _logger.Debug($"  {mapping.SageFieldName} -> {mapping.BitrixFieldName} " +
                             $"({(mapping.IsMandatory ? "Required" : "Optional")})");
            }

            if (activeMappings.Count > 5)
            {
                _logger.Debug($"  ... and {activeMappings.Count - 5} more mappings");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _bitrixClient?.Dispose();
                _disposed = true;
            }
        }
    }
}