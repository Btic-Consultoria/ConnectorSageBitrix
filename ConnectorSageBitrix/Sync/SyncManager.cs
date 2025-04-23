using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConnectorSageBitrix.Bitrix;
using ConnectorSageBitrix.Config;
using ConnectorSageBitrix.Logging;
using ConnectorSageBitrix.Models;
using ConnectorSageBitrix.Repositories;

namespace ConnectorSageBitrix.Sync
{
    public class SyncManager : IDisposable
    {
        private readonly BitrixClient _bitrixClient;
        private readonly SocioRepository _socioRepository;
        private readonly CargoRepository _cargoRepository;
        private readonly ActividadRepository _actividadRepository;
        private readonly ModeloRepository _modeloRepository;
        private readonly Logger _logger;
        private readonly AppConfig _config;
        private bool _disposed = false;

        public SyncManager(
            BitrixClient bitrixClient,
            SocioRepository socioRepository,
            CargoRepository cargoRepository,
            ActividadRepository actividadRepository,
            ModeloRepository modeloRepository,
            Logger logger,
            AppConfig config)
        {
            _bitrixClient = bitrixClient;
            _socioRepository = socioRepository;
            _cargoRepository = cargoRepository;
            _actividadRepository = actividadRepository;
            _modeloRepository = modeloRepository;
            _logger = logger;
            _config = config;
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

            _logger.Info("Synchronization completed successfully");
        }

        #region Socios Sync

        private async Task SyncSociosAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting Socios synchronization");

            // Step 1: Get all socios from Bitrix24
            List<BitrixSocio> bitrixSocios = await _bitrixClient.ListSociosAsync();
            _logger.Info($"Retrieved {bitrixSocios.Count} socios from Bitrix24");

            // Create a list to store DNIs of processed socios
            List<string> processedDNIs = new List<string>();

            // Step 2: Update existing socios in Bitrix24 with data from Sage
            foreach (var bitrixSocio in bitrixSocios)
            {
                if (cancellationToken.IsCancellationRequested) return;

                // Get the corresponding socio from Sage
                Socio sageSocio = _socioRepository.GetByDNI(bitrixSocio.DNI);
                if (sageSocio == null)
                {
                    _logger.Info($"Socio with DNI {bitrixSocio.DNI} not found in Sage");
                    continue;
                }

                // If the socio exists in Sage and needs to be updated in Bitrix24
                if (BitrixSocio.NeedsUpdate(bitrixSocio, sageSocio))
                {
                    _logger.Info($"Updating socio with DNI {bitrixSocio.DNI} in Bitrix24");
                    BitrixSocio updatedBitrixSocio = BitrixSocio.FromSageSocio(sageSocio);
                    await _bitrixClient.UpdateSocioAsync(bitrixSocio.ID, updatedBitrixSocio);
                }

                // Add this DNI to the processed list
                processedDNIs.Add(bitrixSocio.DNI);
            }

            // Step 3: Get socios from Sage that don't exist in Bitrix24
            List<Socio> sageSocios = _socioRepository.GetAllExcept(processedDNIs);
            _logger.Info($"Found {sageSocios.Count} new socios in Sage to create in Bitrix24");

            // Step 4: Create new socios in Bitrix24
            foreach (var sageSocio in sageSocios)
            {
                if (cancellationToken.IsCancellationRequested) return;

                _logger.Info($"Creating new socio with DNI {sageSocio.DNI} in Bitrix24");
                BitrixSocio newBitrixSocio = BitrixSocio.FromSageSocio(sageSocio);
                await _bitrixClient.CreateSocioAsync(newBitrixSocio);
            }

            _logger.Info("Socios synchronization completed successfully");
        }

        #endregion

        #region Cargos Sync

        private async Task SyncCargosAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting Cargos synchronization");

            // Step 1: Get all cargos from Bitrix24
            List<BitrixCargo> bitrixCargos = await _bitrixClient.ListCargosAsync();
            _logger.Info($"Retrieved {bitrixCargos.Count} cargos from Bitrix24");

            // Create a list to store DNIs of processed cargos
            List<string> processedDNIs = new List<string>();

            // Step 2: Update existing cargos in Bitrix24 with data from Sage
            foreach (var bitrixCargo in bitrixCargos)
            {
                if (cancellationToken.IsCancellationRequested) return;

                // Get the corresponding cargo from Sage
                Cargo sageCargo = _cargoRepository.GetByDNI(bitrixCargo.DNI);
                if (sageCargo == null)
                {
                    _logger.Info($"Cargo with DNI {bitrixCargo.DNI} not found in Sage");
                    continue;
                }

                // If the cargo exists in Sage and needs to be updated in Bitrix24
                if (BitrixCargo.NeedsCargoUpdate(bitrixCargo, sageCargo))
                {
                    _logger.Info($"Updating cargo with DNI {bitrixCargo.DNI} in Bitrix24");
                    BitrixCargo updatedBitrixCargo = BitrixCargo.FromSageCargo(sageCargo);
                    await _bitrixClient.UpdateCargoAsync(bitrixCargo.ID, updatedBitrixCargo);
                }

                // Add this DNI to the processed list
                processedDNIs.Add(bitrixCargo.DNI);
            }

            // Step 3: Get cargos from Sage that don't exist in Bitrix24
            List<Cargo> sageCargos = _cargoRepository.GetAllExcept(processedDNIs);
            _logger.Info($"Found {sageCargos.Count} new cargos in Sage to create in Bitrix24");

            // Step 4: Create new cargos in Bitrix24
            foreach (var sageCargo in sageCargos)
            {
                if (cancellationToken.IsCancellationRequested) return;

                // Skip if DNI is empty
                if (string.IsNullOrEmpty(sageCargo.DNI))
                {
                    continue;
                }

                _logger.Info($"Creating new cargo with DNI {sageCargo.DNI} in Bitrix24");
                BitrixCargo newBitrixCargo = BitrixCargo.FromSageCargo(sageCargo);
                await _bitrixClient.CreateCargoAsync(newBitrixCargo);
            }

            _logger.Info("Cargos synchronization completed successfully");
        }

        #endregion

        #region Actividades Sync

        private async Task SyncActividadesAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting Actividades synchronization");

            // Step 1: Get all actividades from Bitrix24
            List<BitrixActividad> bitrixActividades = await _bitrixClient.ListActividadesAsync();
            _logger.Info($"Retrieved {bitrixActividades.Count} actividades from Bitrix24");

            // Create a list to store epigrafes of processed actividades
            List<string> processedEpigrafes = new List<string>();

            // Step 2: Update existing actividades in Bitrix24 with data from Sage
            foreach (var bitrixActividad in bitrixActividades)
            {
                if (cancellationToken.IsCancellationRequested) return;

                // Get the corresponding actividad from Sage
                Actividad sageActividad = _actividadRepository.GetByEpigrafe(bitrixActividad.Epigrafe);
                if (sageActividad == null)
                {
                    _logger.Info($"Actividad with epigrafe {bitrixActividad.Epigrafe} not found in Sage");
                    continue;
                }

                // If the actividad exists in Sage and needs to be updated in Bitrix24
                if (BitrixActividad.NeedsActividadUpdate(bitrixActividad, sageActividad))
                {
                    _logger.Info($"Updating actividad with epigrafe {bitrixActividad.Epigrafe} in Bitrix24");
                    BitrixActividad updatedBitrixActividad = BitrixActividad.FromSageActividad(sageActividad);
                    await _bitrixClient.UpdateActividadAsync(bitrixActividad.ID, updatedBitrixActividad);
                }

                // Add this epigrafe to the processed list
                processedEpigrafes.Add(bitrixActividad.Epigrafe);
            }

            // Step 3: Get actividades from Sage that don't exist in Bitrix24
            List<Actividad> sageActividades = _actividadRepository.GetAllExcept(processedEpigrafes);
            _logger.Info($"Found {sageActividades.Count} new actividades in Sage to create in Bitrix24");

            // Step 4: Create new actividades in Bitrix24
            foreach (var sageActividad in sageActividades)
            {
                if (cancellationToken.IsCancellationRequested) return;

                // Skip if CodigoEpigrafe is empty
                if (string.IsNullOrEmpty(sageActividad.CodigoEpigrafe))
                {
                    continue;
                }

                _logger.Info($"Creating new actividad with epigrafe {sageActividad.CodigoEpigrafe} in Bitrix24");
                BitrixActividad newBitrixActividad = BitrixActividad.FromSageActividad(sageActividad);
                await _bitrixClient.CreateActividadAsync(newBitrixActividad);
            }

            _logger.Info("Actividades synchronization completed successfully");
        }

        #endregion

        #region Modelos Sync

        private async Task SyncModelosAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting Modelos synchronization");

            // Step 1: Get all modelos from Bitrix24
            List<BitrixModelo> bitrixModelos = await _bitrixClient.ListModelosAsync();
            _logger.Info($"Retrieved {bitrixModelos.Count} modelos from Bitrix24");

            // Create a list to store codes of processed modelos
            List<string> processedCodigos = new List<string>();

            // Step 2: Update existing modelos in Bitrix24 with data from Sage
            foreach (var bitrixModelo in bitrixModelos)
            {
                if (cancellationToken.IsCancellationRequested) return;

                // Get the corresponding modelo from Sage using the codigo as key
                Modelo sageModelo = _modeloRepository.GetByCodigoModelo(bitrixModelo.CodigoModeloImp);
                if (sageModelo == null)
                {
                    _logger.Info($"Modelo with codigo {bitrixModelo.CodigoModeloImp} not found in Sage");
                    continue;
                }

                // If the modelo exists in Sage and needs to be updated in Bitrix24
                if (BitrixModelo.NeedsModeloUpdate(bitrixModelo, sageModelo))
                {
                    _logger.Info($"Updating modelo with codigo {bitrixModelo.CodigoModeloImp} in Bitrix24");
                    BitrixModelo updatedBitrixModelo = BitrixModelo.FromSageModelo(sageModelo);
                    await _bitrixClient.UpdateModeloAsync(bitrixModelo.ID, updatedBitrixModelo);
                }

                // Add this codigo to the processed list, regardless of whether it was updated
                processedCodigos.Add(bitrixModelo.CodigoModeloImp);
            }

            // Step 3: Get modelos from Sage that don't exist in Bitrix24
            List<Modelo> sageModelos = _modeloRepository.GetAllExcept(processedCodigos);
            _logger.Info($"Found {sageModelos.Count} new modelos in Sage to create in Bitrix24");

            // Step 4: Create new modelos in Bitrix24
            foreach (var sageModelo in sageModelos)
            {
                if (cancellationToken.IsCancellationRequested) return;

                // Skip empty modelos
                if (string.IsNullOrEmpty(sageModelo.CodigoModeloImp))
                {
                    continue;
                }

                _logger.Info($"Creating new modelo with codigo {sageModelo.CodigoModeloImp} in Bitrix24");
                BitrixModelo newBitrixModelo = BitrixModelo.FromSageModelo(sageModelo);
                await _bitrixClient.CreateModeloAsync(newBitrixModelo);
            }

            _logger.Info("Modelos synchronization completed successfully");
        }

        #endregion

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