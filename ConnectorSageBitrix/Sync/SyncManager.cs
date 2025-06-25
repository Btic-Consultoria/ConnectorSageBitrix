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
        private readonly CompanyRepository _companyRepository;
        private readonly ProductRepository _productRepository;
        private readonly Logger _logger;
        private readonly AppConfig _config;
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

            try
            {
                // Step 1: Get all cargos from Bitrix24
                List<BitrixCargo> bitrixCargos = await _bitrixClient.ListCargosAsync();
                _logger.Info($"Retrieved {bitrixCargos.Count} cargos from Bitrix24");

                // Create a dictionary to store Bitrix cargos by DNI for efficient lookup
                Dictionary<string, BitrixCargo> bitrixCargosByDni = new Dictionary<string, BitrixCargo>(StringComparer.OrdinalIgnoreCase);

                // Log existing cargos for debugging
                _logger.Debug("Existing cargos in Bitrix24:");
                foreach (var bitrixCargo in bitrixCargos)
                {
                    string dni = bitrixCargo.DNI;
                    if (!string.IsNullOrEmpty(dni))
                    {
                        _logger.Debug($"ID: {bitrixCargo.ID}, DNI: {dni}, Title: {bitrixCargo.Title}");
                        // Add to dictionary for lookup (normalize DNI - remove spaces, lowercase)
                        bitrixCargosByDni[dni.Trim().ToLower().Replace(" ", "")] = bitrixCargo;
                    }
                    else
                    {
                        _logger.Debug($"ID: {bitrixCargo.ID}, DNI: [EMPTY], Title: {bitrixCargo.Title}");
                    }
                }

                // Step 2: Get all cargos from Sage
                List<Cargo> sageCargos = _cargoRepository.GetAll();
                _logger.Info($"Retrieved {sageCargos.Count} cargos from Sage");

                // Log Sage cargos for debugging
                _logger.Debug("Cargos in Sage:");
                foreach (var sageCargo in sageCargos)
                {
                    string dni = sageCargo.DNI;
                    if (!string.IsNullOrEmpty(dni))
                    {
                        _logger.Debug($"DNI: {dni}, CargoAdministrador: {sageCargo.CargoAdministrador}");
                    }
                    else
                    {
                        _logger.Debug($"DNI: [EMPTY], CargoAdministrador: {sageCargo.CargoAdministrador}");
                    }
                }

                // List to track processed DNIs
                List<string> processedDnis = new List<string>();

                // Step 3: Process each cargo from Sage
                foreach (var sageCargo in sageCargos)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    // Skip if DNI is empty
                    if (string.IsNullOrEmpty(sageCargo.DNI))
                    {
                        _logger.Info($"Skipping cargo with empty DNI");
                        continue;
                    }

                    // Normalize DNI (trim, lowercase, remove spaces)
                    string normalizedDni = sageCargo.DNI.Trim().ToLower().Replace(" ", "");

                    // Check if this cargo already exists in Bitrix24
                    if (bitrixCargosByDni.TryGetValue(normalizedDni, out BitrixCargo existingBitrix))
                    {
                        // Update if needed
                        if (BitrixCargo.NeedsCargoUpdate(existingBitrix, sageCargo))
                        {
                            _logger.Info($"Updating cargo with DNI {normalizedDni} in Bitrix24");
                            BitrixCargo updatedBitrixCargo = BitrixCargo.FromSageCargo(sageCargo);
                            await _bitrixClient.UpdateCargoAsync(existingBitrix.ID, updatedBitrixCargo);
                        }
                        else
                        {
                            _logger.Info($"No update needed for cargo with DNI {normalizedDni}");
                        }
                    }
                    else
                    {
                        // Create new cargo
                        _logger.Info($"Creating new cargo with DNI {normalizedDni} in Bitrix24");
                        BitrixCargo newBitrixCargo = BitrixCargo.FromSageCargo(sageCargo);
                        await _bitrixClient.CreateCargoAsync(newBitrixCargo);
                    }

                    // Add this DNI to the processed list
                    processedDnis.Add(normalizedDni);
                }

                // Step 4: Optional - Check for obsolete cargos in Bitrix24
                foreach (var bitrixCargo in bitrixCargos)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    if (!string.IsNullOrEmpty(bitrixCargo.DNI))
                    {
                        string normalizedDni = bitrixCargo.DNI.Trim().ToLower().Replace(" ", "");
                        if (!processedDnis.Contains(normalizedDni))
                        {
                            _logger.Info($"Found obsolete cargo in Bitrix24 with DNI {bitrixCargo.DNI} - consider removing");
                            // Uncomment if you want to delete obsolete items
                            // await _bitrixClient.DeleteCargoAsync(bitrixCargo.ID);
                        }
                    }
                }

                _logger.Info("Cargos synchronization completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error during Cargos synchronization: {ex.Message}");
                _logger.Error(ex.StackTrace);
                throw;
            }
        }

        #endregion

        #region Actividades Sync

        private async Task SyncActividadesAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting Actividades synchronization");

            try
            {
                // Step 1: Get all actividades from Bitrix24
                List<BitrixActividad> bitrixActividades = await _bitrixClient.ListActividadesAsync();
                _logger.Info($"Retrieved {bitrixActividades.Count} actividades from Bitrix24");

                // Create a dictionary to store Bitrix actividades by GuidActividad for easier lookup
                Dictionary<string, BitrixActividad> bitrixActividadesByGuid = new Dictionary<string, BitrixActividad>(StringComparer.OrdinalIgnoreCase);

                // Create a list to store GUIDs of processed actividades
                List<string> processedGuids = new List<string>();

                // Log the GUIDs of existing activities for debugging
                _logger.Debug("Existing actividades in Bitrix24:");
                foreach (var bitrixActividad in bitrixActividades)
                {
                    string guid = bitrixActividad.GuidActividad;
                    if (!string.IsNullOrEmpty(guid))
                    {
                        _logger.Debug($"ID: {bitrixActividad.ID}, GUID: {guid}, Title: {bitrixActividad.Title}");
                        // Add to dictionary for lookup
                        bitrixActividadesByGuid[guid] = bitrixActividad;
                    }
                    else
                    {
                        _logger.Debug($"ID: {bitrixActividad.ID}, GUID: [EMPTY], Title: {bitrixActividad.Title}");
                    }
                }

                // Step 2: Get all actividades from Sage
                List<Actividad> sageActividades = _actividadRepository.GetAll();
                _logger.Info($"Retrieved {sageActividades.Count} actividades from Sage");

                // Log the GUIDs of Sage activities for debugging
                _logger.Debug("Actividades in Sage:");
                foreach (var sageActividad in sageActividades)
                {
                    string guid = sageActividad.GuidActividad;
                    if (!string.IsNullOrEmpty(guid))
                    {
                        _logger.Debug($"GUID: {guid}, CodigoEpigrafe: {sageActividad.CodigoEpigrafe}");
                    }
                    else
                    {
                        _logger.Debug($"GUID: [EMPTY], CodigoEpigrafe: {sageActividad.CodigoEpigrafe}");
                    }
                }

                // Step 3: Process each actividad from Sage
                foreach (var sageActividad in sageActividades)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    // Skip if GuidActividad is empty
                    if (string.IsNullOrEmpty(sageActividad.GuidActividad))
                    {
                        _logger.Info($"Skipping actividad with empty GUID, CodigoEpigrafe: {sageActividad.CodigoEpigrafe}");
                        continue;
                    }

                    // Normalize GUID (trim and lowercase)
                    string normalizedGuid = sageActividad.GuidActividad.Trim().ToLower();

                    // Check if this actividad already exists in Bitrix24
                    if (bitrixActividadesByGuid.TryGetValue(normalizedGuid, out BitrixActividad existingBitrix))
                    {
                        // Update if needed
                        if (BitrixActividad.NeedsActividadUpdate(existingBitrix, sageActividad))
                        {
                            _logger.Info($"Found existing actividad with GUID {sageActividad} in Bitrix24");
                            _logger.Info($"Updating actividad with GUID {normalizedGuid} in Bitrix24");
                            BitrixActividad updatedBitrixActividad = BitrixActividad.FromSageActividad(sageActividad);
                            await _bitrixClient.UpdateActividadAsync(existingBitrix.ID, updatedBitrixActividad);
                        }
                        else
                        {
                            _logger.Info($"No update needed for actividad with GUID {normalizedGuid}");
                        }
                    }
                    else
                    {
                        // Create new actividad
                        _logger.Info($"Creating new actividad with GUID {normalizedGuid} in Bitrix24");
                        _logger.Info($"Found created actividad with GUID {sageActividad.GuidActividad} in Bitrix24");
                        BitrixActividad newBitrixActividad = BitrixActividad.FromSageActividad(sageActividad);
                        await _bitrixClient.CreateActividadAsync(newBitrixActividad);
                    }

                    // Add this GUID to the processed list
                    processedGuids.Add(normalizedGuid);
                }

                // Step 4: Check for obsolete actividades in Bitrix24 (optional)
                foreach (var bitrixActividad in bitrixActividades)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    if (!string.IsNullOrEmpty(bitrixActividad.GuidActividad) &&
                        !processedGuids.Contains(bitrixActividad.GuidActividad.Trim().ToLower()))
                    {
                        _logger.Info($"Found obsolete actividad in Bitrix24 with GUID {bitrixActividad.GuidActividad} - consider removing");
                        // Uncomment the following line if you want to automatically delete obsolete actividades
                        // await _bitrixClient.DeleteActividadAsync(bitrixActividad.ID);
                    }
                }

                _logger.Info("Actividades synchronization completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error during Actividades synchronization: {ex.Message}");
                _logger.Error(ex.StackTrace);
                throw;
            }
        }

        #endregion

        #region Modelos Sync

        private async Task SyncModelosAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting Modelos synchronization");

            // Step 1: Get all modelos from Bitrix24
            List<BitrixModelo> bitrixModelos = await _bitrixClient.ListModelosAsync();
            _logger.Info($"Retrieved {bitrixModelos.Count} modelos from Bitrix24");

            // Create a list to store codes of processed modelos and a dictionary for lookup by code
            List<string> processedCodigos = new List<string>();
            Dictionary<string, BitrixModelo> bitrixModelosByCodigo = new Dictionary<string, BitrixModelo>(StringComparer.OrdinalIgnoreCase);

            // Step 2: Process existing modelos from Bitrix24
            foreach (var bitrixModelo in bitrixModelos)
            {
                if (cancellationToken.IsCancellationRequested) return;

                // INICIO DE LA MODIFICACIÓN ----------------------------------------
                string codigo = bitrixModelo.CodigoModeloImp;

                // Si CodigoModeloImp está vacío, intenta extraer el código del título
                if (string.IsNullOrEmpty(codigo) && !string.IsNullOrEmpty(bitrixModelo.Title))
                {
                    // El título tiene formato "Modelo XXX"
                    string title = bitrixModelo.Title;
                    if (title.StartsWith("Modelo "))
                    {
                        codigo = title.Substring("Modelo ".Length).Trim();
                        _logger.Info($"Extracted codigo '{codigo}' from title '{title}'");
                    }
                }

                if (string.IsNullOrEmpty(codigo))
                {
                    _logger.Info("Omitiendo modelo con CodigoModeloImp nulo o vacío y título sin código");
                    continue;
                }

                // Normalize codigo (trim and convert to consistent case)
                string normalizedCodigo = codigo.Trim();
                // FIN DE LA MODIFICACIÓN ------------------------------------------

                // Add to processed list and lookup dictionary
                processedCodigos.Add(normalizedCodigo);
                bitrixModelosByCodigo[normalizedCodigo] = bitrixModelo;

                // Get the corresponding modelo from Sage using the codigo as key
                Modelo sageModelo = _modeloRepository.GetByCodigoModelo(normalizedCodigo);
                if (sageModelo == null)
                {
                    _logger.Info($"Modelo with codigo {normalizedCodigo} not found in Sage");
                    continue;
                }

                // If the modelo exists in Sage and needs to be updated in Bitrix24
                if (BitrixModelo.NeedsModeloUpdate(bitrixModelo, sageModelo))
                {
                    _logger.Info($"Updating modelo with codigo {normalizedCodigo} in Bitrix24");
                    BitrixModelo updatedBitrixModelo = BitrixModelo.FromSageModelo(sageModelo);
                    await _bitrixClient.UpdateModeloAsync(bitrixModelo.ID, updatedBitrixModelo);
                }
            }

            // Step 3: Get modelos from Sage that don't exist in Bitrix24
            List<Modelo> sageModelos = _modeloRepository.GetAllExcept(processedCodigos);

            // Create filtered list to prevent duplicates
            List<Modelo> newModelosToCreate = new List<Modelo>();
            foreach (var sageModelo in sageModelos)
            {
                // Skip empty modelos
                if (string.IsNullOrEmpty(sageModelo.CodigoModeloImp))
                    continue;

                string normalizedCodigo = sageModelo.CodigoModeloImp.Trim();

                // Skip if the model already exists in Bitrix (using case-insensitive comparison)
                if (bitrixModelosByCodigo.ContainsKey(normalizedCodigo))
                {
                    _logger.Debug($"Modelo with codigo {normalizedCodigo} already exists in Bitrix24, skipping creation");
                    continue;
                }

                // Add to list of models to create
                newModelosToCreate.Add(sageModelo);
            }

            _logger.Info($"Found {newModelosToCreate.Count} new modelos in Sage to create in Bitrix24");

            // Step 4: Create new modelos in Bitrix24
            foreach (var sageModelo in newModelosToCreate)
            {
                if (cancellationToken.IsCancellationRequested) return;

                string normalizedCodigo = sageModelo.CodigoModeloImp.Trim();
                _logger.Info($"Creating new modelo with codigo {normalizedCodigo} in Bitrix24");
                BitrixModelo newBitrixModelo = BitrixModelo.FromSageModelo(sageModelo);
                await _bitrixClient.CreateModeloAsync(newBitrixModelo);
            }

            _logger.Info("Modelos synchronization completed successfully");
        }

        #endregion

        #region Companies Sync

        private async Task SyncCompaniesAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting Companies synchronization");

            try
            {
                // Step 1: Get all companies from Bitrix24
                List<BitrixCompany> bitrixCompanies = await _bitrixClient.ListCompaniesAsync();
                _logger.Info($"Retrieved {bitrixCompanies.Count} companies from Bitrix24");

                // Create a dictionary to store Bitrix companies by CodigoCategoriaCliente for efficient lookup
                Dictionary<string, BitrixCompany> bitrixCompaniesByCodigo = new Dictionary<string, BitrixCompany>(StringComparer.OrdinalIgnoreCase);

                // Log existing companies for debugging
                _logger.Debug("Existing companies in Bitrix24:");
                foreach (var bitrixCompany in bitrixCompanies)
                {
                    string codigo = bitrixCompany.CodigoCategoriaCliente;
                    if (!string.IsNullOrEmpty(codigo))
                    {
                        _logger.Debug($"ID: {bitrixCompany.ID}, Codigo: {codigo}, Title: {bitrixCompany.Title}");
                        // Add to dictionary for lookup (normalize codigo - remove spaces, lowercase)
                        bitrixCompaniesByCodigo[codigo.Trim().ToLower().Replace(" ", "")] = bitrixCompany;
                    }
                    else
                    {
                        _logger.Debug($"ID: {bitrixCompany.ID}, Codigo: [EMPTY], Title: {bitrixCompany.Title}");
                    }
                }

                // Step 2: Get all companies from Sage
                List<Company> sageCompanies = _companyRepository.GetAll();
                _logger.Info($"Retrieved {sageCompanies.Count} companies from Sage");

                // Log Sage companies for debugging
                _logger.Debug("Companies in Sage:");
                foreach (var sageCompany in sageCompanies)
                {
                    string codigo = sageCompany.CodigoCategoriaCliente;
                    if (!string.IsNullOrEmpty(codigo))
                    {
                        _logger.Debug($"Codigo: {codigo}, RazonSocial: {sageCompany.RazonSocial}");
                    }
                    else
                    {
                        _logger.Debug($"Codigo: [EMPTY], RazonSocial: {sageCompany.RazonSocial}");
                    }
                }

                // List to track processed codigos
                List<string> processedCodigos = new List<string>();

                // Step 3: Process each company from Sage
                foreach (var sageCompany in sageCompanies)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    // Skip if codigo is empty
                    if (string.IsNullOrEmpty(sageCompany.CodigoCategoriaCliente))
                    {
                        _logger.Info($"Skipping company with empty codigo");
                        continue;
                    }

                    // Normalize codigo (trim, lowercase, remove spaces)
                    string normalizedCodigo = sageCompany.CodigoCategoriaCliente.Trim().ToLower().Replace(" ", "");

                    // Check if this company already exists in Bitrix24
                    if (bitrixCompaniesByCodigo.TryGetValue(normalizedCodigo, out BitrixCompany existingBitrix))
                    {
                        // Update if needed
                        if (BitrixCompany.NeedsCompanyUpdate(existingBitrix, sageCompany))
                        {
                            _logger.Info($"Updating company with codigo {normalizedCodigo} in Bitrix24");
                            BitrixCompany updatedBitrixCompany = BitrixCompany.FromSageCompany(sageCompany);
                            await _bitrixClient.UpdateCompanyAsync(existingBitrix.ID, updatedBitrixCompany);
                        }
                        else
                        {
                            _logger.Info($"No update needed for company with codigo {normalizedCodigo}");
                        }
                    }
                    else
                    {
                        // Create new company
                        _logger.Info($"Creating new company with codigo {normalizedCodigo} in Bitrix24");
                        BitrixCompany newBitrixCompany = BitrixCompany.FromSageCompany(sageCompany);
                        await _bitrixClient.CreateCompanyAsync(newBitrixCompany);
                    }

                    // Add this codigo to the processed list
                    processedCodigos.Add(normalizedCodigo);
                }

                // Step 4: Optional - Check for obsolete companies in Bitrix24
                foreach (var bitrixCompany in bitrixCompanies)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    if (!string.IsNullOrEmpty(bitrixCompany.CodigoCategoriaCliente))
                    {
                        string normalizedCodigo = bitrixCompany.CodigoCategoriaCliente.Trim().ToLower().Replace(" ", "");
                        if (!processedCodigos.Contains(normalizedCodigo))
                        {
                            _logger.Info($"Found obsolete company in Bitrix24 with codigo {bitrixCompany.CodigoCategoriaCliente} - consider removing");
                            // Uncomment if you want to delete obsolete items
                            // await _bitrixClient.DeleteCompanyAsync(bitrixCompany.ID);
                        }
                    }
                }

                _logger.Info("Companies synchronization completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error during Companies synchronization: {ex.Message}");
                _logger.Error(ex.StackTrace);
                throw;
            }
        }

        #endregion

        #region Products Sync

        private async Task SyncProductsAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting Products synchronization");

            try
            {
                // Step 1: Get all products from Bitrix24
                List<BitrixProduct> bitrixProducts = await _bitrixClient.ListProductsAsync();
                _logger.Info($"Retrieved {bitrixProducts.Count} products from Bitrix24");

                // Create a dictionary to store Bitrix products by CodigoArticulo for efficient lookup
                Dictionary<string, BitrixProduct> bitrixProductsByCodigo = new Dictionary<string, BitrixProduct>(StringComparer.OrdinalIgnoreCase);

                // Log existing products for debugging
                _logger.Debug("Existing products in Bitrix24:");
                foreach (var bitrixProduct in bitrixProducts)
                {
                    string codigo = bitrixProduct.CodigoArticulo;
                    if (!string.IsNullOrEmpty(codigo))
                    {
                        _logger.Debug($"ID: {bitrixProduct.ID}, Codigo: {codigo}, Title: {bitrixProduct.Title}");
                        // Add to dictionary for lookup (normalize codigo - remove spaces, lowercase)
                        bitrixProductsByCodigo[codigo.Trim().ToLower().Replace(" ", "")] = bitrixProduct;
                    }
                    else
                    {
                        _logger.Debug($"ID: {bitrixProduct.ID}, Codigo: [EMPTY], Title: {bitrixProduct.Title}");
                    }
                }

                // Step 2: Get all products from Sage
                List<Product> sageProducts = _productRepository.GetAll();
                _logger.Info($"Retrieved {sageProducts.Count} products from Sage");

                // Log Sage products for debugging
                _logger.Debug("Products in Sage:");
                foreach (var sageProduct in sageProducts)
                {
                    string codigo = sageProduct.CodigoArticulo;
                    if (!string.IsNullOrEmpty(codigo))
                    {
                        _logger.Debug($"Codigo: {codigo}, Descripcion: {sageProduct.DescripcionArticulo}");
                    }
                    else
                    {
                        _logger.Debug($"Codigo: [EMPTY], Descripcion: {sageProduct.DescripcionArticulo}");
                    }
                }

                // List to track processed codigos
                List<string> processedCodigos = new List<string>();

                // Step 3: Process each product from Sage
                foreach (var sageProduct in sageProducts)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    // Skip if codigo is empty
                    if (string.IsNullOrEmpty(sageProduct.CodigoArticulo))
                    {
                        _logger.Info($"Skipping product with empty codigo");
                        continue;
                    }

                    // Normalize codigo (trim, lowercase, remove spaces)
                    string normalizedCodigo = sageProduct.CodigoArticulo.Trim().ToLower().Replace(" ", "");

                    // Check if this product already exists in Bitrix24
                    if (bitrixProductsByCodigo.TryGetValue(normalizedCodigo, out BitrixProduct existingBitrix))
                    {
                        // Update if needed
                        if (BitrixProduct.NeedsProductUpdate(existingBitrix, sageProduct))
                        {
                            _logger.Info($"Updating product with codigo {normalizedCodigo} in Bitrix24");
                            BitrixProduct updatedBitrixProduct = BitrixProduct.FromSageProduct(sageProduct);
                            await _bitrixClient.UpdateProductAsync(existingBitrix.ID, updatedBitrixProduct);
                        }
                        else
                        {
                            _logger.Info($"No update needed for product with codigo {normalizedCodigo}");
                        }
                    }
                    else
                    {
                        // Create new product
                        _logger.Info($"Creating new product with codigo {normalizedCodigo} in Bitrix24");
                        BitrixProduct newBitrixProduct = BitrixProduct.FromSageProduct(sageProduct);
                        await _bitrixClient.CreateProductAsync(newBitrixProduct);
                    }

                    // Add this codigo to the processed list
                    processedCodigos.Add(normalizedCodigo);
                }

                // Step 4: Optional - Check for obsolete products in Bitrix24
                foreach (var bitrixProduct in bitrixProducts)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    if (!string.IsNullOrEmpty(bitrixProduct.CodigoArticulo))
                    {
                        string normalizedCodigo = bitrixProduct.CodigoArticulo.Trim().ToLower().Replace(" ", "");
                        if (!processedCodigos.Contains(normalizedCodigo))
                        {
                            _logger.Info($"Found obsolete product in Bitrix24 with codigo {bitrixProduct.CodigoArticulo} - consider removing");
                            // Uncomment if you want to delete obsolete items
                            // await _bitrixClient.DeleteProductAsync(bitrixProduct.ID);
                        }
                    }
                }

                _logger.Info("Products synchronization completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error during Products synchronization: {ex.Message}");
                _logger.Error(ex.StackTrace);
                throw;
            }
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