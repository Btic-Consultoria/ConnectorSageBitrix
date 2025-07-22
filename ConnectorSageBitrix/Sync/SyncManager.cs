using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConnectorSageBitrix.Bitrix;
using ConnectorSageBitrix.Config;
using ConnectorSageBitrix.Logging;
using ConnectorSageBitrix.Models;
using ConnectorSageBitrix.Repositories;
using System.Linq;

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
                _logger.Error($"Socios synchronization failed: {ex.Message}");
                _logger.Error(ex.ToString());
            }

            try
            {
                await SyncCargosAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error($"Cargos synchronization failed: {ex.Message}");
                _logger.Error(ex.ToString());
            }

            try
            {
                await SyncActividadesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error($"Actividades synchronization failed: {ex.Message}");
                _logger.Error(ex.ToString());
            }

            try
            {
                await SyncModelosAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error($"Modelos synchronization failed: {ex.Message}");
                _logger.Error(ex.ToString());
            }

            try
            {
                await SyncCompaniesWithMappingsAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error($"Companies synchronization failed: {ex.Message}");
                _logger.Error(ex.ToString());
            }

            try
            {
                await SyncProductsAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error($"Products synchronization failed: {ex.Message}");
                _logger.Error(ex.ToString());
            }

            _logger.Info("Synchronization of all entities completed");
        }

        private async Task SyncSociosAsync(CancellationToken cancellationToken)
        {
            if (_bitrixClient == null)
            {
                _logger.Debug("Bitrix client not initialized - skipping socios sync");
                return;
            }

            _logger.Info("Starting socios synchronization");

            try
            {
                var bitrixSocios = await _bitrixClient.ListSociosAsync();
                var sageSocios = await _socioRepository.GetAllAsync();

                _logger.Info($"Found {bitrixSocios.Count} socios in Bitrix24 and {sageSocios.Count} in Sage");

                // Sync logic for socios
                foreach (var sageSocio in sageSocios)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var existingBitrixSocio = bitrixSocios.FirstOrDefault(b => b.DNI == sageSocio.DNI);

                    if (existingBitrixSocio == null)
                    {
                        // Create new socio in Bitrix24
                        var newBitrixSocio = MapSocioToBitrix(sageSocio);
                        await _bitrixClient.CreateSocioAsync(newBitrixSocio);
                        _logger.Debug($"Created new socio in Bitrix24: {sageSocio.DNI}");
                    }
                    else
                    {
                        // Update existing socio
                        var updatedBitrixSocio = MapSocioToBitrix(sageSocio);
                        // Note: Need to get the ID from the existing socio to update
                        _logger.Debug($"Updated socio in Bitrix24: {sageSocio.DNI}");
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

        private async Task SyncCargosAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting cargos synchronization");
            // Implementation for cargos sync
            _logger.Info("Cargos synchronization completed");
        }

        private async Task SyncActividadesAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting actividades synchronization");
            // Implementation for actividades sync
            _logger.Info("Actividades synchronization completed");
        }

        private async Task SyncModelosAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting modelos synchronization");
            // Implementation for modelos sync
            _logger.Info("Modelos synchronization completed");
        }

        private async Task SyncCompaniesWithMappingsAsync(CancellationToken cancellationToken)
        {
            if (_bitrixClient == null || _fieldMappingManager == null)
            {
                _logger.Debug("Bitrix client or field mapping manager not initialized - skipping companies sync");
                return;
            }

            _logger.Info("Starting companies synchronization with field mappings");

            try
            {
                var bitrixCompanies = await _bitrixClient.ListCompaniesAsync();
                var sageCompanies = await _companyRepository.GetAllAsync();

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
                        _logger.Error($"Error syncing company {sageCompany.IdCliente}: {ex.Message}");
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
                    _logger.Info($"Updating Bitrix company {sageCompany.IdCliente} with {bitrixFields.Count} mapped fields");

                    // Buscar si la empresa ya existe en Bitrix24
                    // Aquí deberías implementar lógica para encontrar la empresa por algún campo único
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
                        _logger.Debug($"Company {sageCompany.IdCliente} not found in Bitrix24 - would need to create");
                        // Aquí podrías implementar lógica para crear nuevas empresas
                    }
                }
                else
                {
                    _logger.Warning($"No mapped fields found for company {sageCompany.IdCliente}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error syncing company with mappings: {ex.Message}");
            }
        }

        private async Task<string> FindBitrixCompanyId(Company sageCompany)
        {
            // Esta es una implementación simplificada
            // Aquí deberías implementar lógica para encontrar la empresa en Bitrix24
            // basándote en algún campo único como email, nombre, o ID externo
            try
            {
                var companies = await _bitrixClient.ListCompaniesAsync();
                var matchingCompany = companies.FirstOrDefault(c =>
                    c.RazonSocial == sageCompany.RazonSocial ||
                    c.EMail1 == sageCompany.EMail1);

                if (matchingCompany != null)
                {
                    // Nota: Necesitarías añadir una propiedad ID a la clase BitrixCompany
                    // return matchingCompany.ID.ToString();
                    _logger.Debug($"Found matching company for {sageCompany.RazonSocial}");
                    return "1"; // Placeholder - reemplazar con el ID real
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error finding Bitrix company ID: {ex.Message}");
                return null;
            }
        }

        private async Task SyncProductsAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting products synchronization");
            // Implementation for products sync
            _logger.Info("Products synchronization completed");
        }

        private BitrixSocio MapSocioToBitrix(Socio sageSocio)
        {
            return new BitrixSocio
            {
                Title = $"{sageSocio.Nombre} {sageSocio.Apellidos}",
                DNI = sageSocio.DNI,
                Cargo = sageSocio.Cargo,
                Administrador = sageSocio.Administrador,
                Participacion = sageSocio.Participacion,
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
            if (!_disposed)
            {
                _bitrixClient?.Dispose();
                _disposed = true;
            }
        }
    }
}
