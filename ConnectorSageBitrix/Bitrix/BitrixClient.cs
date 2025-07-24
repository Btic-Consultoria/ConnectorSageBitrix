using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ConnectorSageBitrix.Logging;
using ConnectorSageBitrix.Extensions;
using ConnectorSageBitrix.Models;
using System.Linq;
using System.Net;

namespace ConnectorSageBitrix.Bitrix
{
    public class BitrixClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly Logger _logger;

        public BitrixClient(string baseUrl, Logger logger)
        {
            _baseUrl = baseUrl;
            _logger = logger;

            // 🔥 Configuración mejorada para servicios de Windows
            _httpClient = new HttpClient();

            // Timeout más corto para detectar problemas más rápido
            _httpClient.Timeout = TimeSpan.FromSeconds(15); // Era 30, ahora 15

            // Headers importantes para servicios de Windows
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ConnectorSageBitrix/1.0");
            _httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");

            // Configurar para evitar problemas de proxy/firewall
            ServicePointManager.DefaultConnectionLimit = 10;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;

            _logger.Info("✅ BitrixClient initialized with improved timeout and retry logic");
        }

        #region User Fields Validation

        /// <summary>
        /// Valida que los user fields mapeados existen en Bitrix24
        /// </summary>
        public async Task<FieldValidationResult> ValidateUserFields(List<FieldMapping> fieldMappings)
        {
            var result = new FieldValidationResult();

            try
            {
                _logger.Info("Validating user fields in Bitrix24");

                // Obtener campos de empresas desde Bitrix24
                var companyFields = await GetCompanyUserFields();

                // Validar cada mapeo
                foreach (var mapping in fieldMappings.Where(m => m.IsActive))
                {
                    var field = companyFields.FirstOrDefault(f => f.FieldName == mapping.BitrixFieldName);

                    if (field != null)
                    {
                        result.ValidFields.Add(new ValidFieldInfo
                        {
                            FieldName = mapping.BitrixFieldName,
                            SageFieldName = mapping.SageFieldName,
                            UserTypeId = field.UserTypeId,
                            IsMandatory = field.Mandatory,
                            IsActive = true
                        });

                        _logger.Debug($"✅ Field validated: {mapping.BitrixFieldName}");
                    }
                    else
                    {
                        result.MissingFields.Add(mapping.BitrixFieldName);
                        _logger.Warning($"❌ Field missing in Bitrix24: {mapping.BitrixFieldName}");
                    }
                }

                result.IsValid = result.MissingFields.Count == 0;

                _logger.Info($"Field validation completed. Valid: {result.ValidFields.Count}, Missing: {result.MissingFields.Count}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error validating user fields: {ex.Message}");
                result.IsValid = false;
                return result;
            }
        }

        /// <summary>
        /// Obtiene los user fields de companies desde Bitrix24
        /// </summary>
        public async Task<List<BitrixUserField>> GetCompanyUserFields()
        {
            try
            {
                _logger.Debug("Requesting company user fields from Bitrix24");

                var response = await DoRequestAsync<BitrixUserFieldListResponse>("crm.userfield.list", new
                {
                    entityId = "CRM_COMPANY"
                });

                if (response?.Result == null)
                {
                    _logger.Warning("No user fields found for CRM_COMPANY");
                    return new List<BitrixUserField>();
                }

                _logger.Debug($"Retrieved {response.Result.Count} company user fields");
                return response.Result;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error getting company user fields: {ex.Message}");
                return new List<BitrixUserField>();
            }
        }

        #endregion

        #region Companies (Dynamic Fields)

        /// <summary>
        /// Actualiza una empresa en Bitrix24 usando mapeo dinámico de campos
        /// </summary>
        public async Task<bool> UpdateCompanyWithMappedFields(int companyId, Dictionary<string, object> mappedFields)
        {
            try
            {
                if (mappedFields == null || !mappedFields.Any())
                {
                    _logger.Warning($"No mapped fields provided for company {companyId}");
                    return false;
                }

                _logger.Info($"Updating Bitrix company {companyId} with mapped fields");

                // Preparar datos para Bitrix24
                var updateData = new Dictionary<string, object>();

                foreach (var field in mappedFields)
                {
                    // Validar y limpiar valores antes de enviar
                    var cleanValue = CleanFieldValue(field.Value, field.Key);
                    if (cleanValue != null)
                    {
                        updateData[field.Key] = cleanValue;
                        _logger.Debug($"Adding field {field.Key}: {cleanValue}");
                    }
                }

                if (!updateData.Any())
                {
                    _logger.Warning($"No valid fields to update for company {companyId}");
                    return false;
                }

                // Construir URL de la API
                string apiUrl = $"{_baseUrl}crm.company.update.json";

                var requestData = new
                {
                    id = companyId,
                    fields = updateData
                };

                _logger.Debug($"Sending update request to: {apiUrl}");
                _logger.Debug($"Request data: {JsonConvert.SerializeObject(requestData, Formatting.Indented)}");

                // Realizar petición HTTP
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(30);

                    var jsonContent = JsonConvert.SerializeObject(requestData);
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await client.PostAsync(apiUrl, content);
                    string responseContent = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.Error($"Failed to update company {companyId}: {response.StatusCode} - {responseContent}");
                        return false;
                    }

                    _logger.Info($"✅ Successfully updated company {companyId}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error updating company {companyId} with mapped fields: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Método UpdateCompany que usa SyncManager (wrapper)
        /// </summary>
        public async Task<bool> UpdateCompany(string companyId, Dictionary<string, object> mappedFields)
        {
            if (!int.TryParse(companyId, out int id))
            {
                _logger.Error($"Invalid company ID: {companyId}");
                return false;
            }

            return await UpdateCompanyWithMappedFields(id, mappedFields);
        }

        private object CleanFieldValue(object value, string fieldName)
        {
            try
            {
                if (value == null)
                    return null;

                string stringValue = value.ToString()?.Trim();

                if (string.IsNullOrEmpty(stringValue))
                    return null;

                // Limpiar valores específicos por tipo de campo
                if (fieldName.Contains("EMAIL"))
                {
                    // Validar formato de email básico
                    if (stringValue.Contains("@") && stringValue.Contains("."))
                        return stringValue.ToLower();
                    else
                    {
                        _logger.Warning($"Invalid email format for {fieldName}: {stringValue}");
                        return null;
                    }
                }
                else if (fieldName.Contains("TELEFONO"))
                {
                    // Limpiar formato de teléfono
                    string cleanPhone = System.Text.RegularExpressions.Regex.Replace(stringValue, @"[^\d+\-\s\(\)]", "");
                    return string.IsNullOrEmpty(cleanPhone) ? null : cleanPhone;
                }
                else
                {
                    // Para campos de texto normales, solo limpiar espacios
                    return stringValue;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error cleaning field value for {fieldName}: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Socios

        public async Task<List<BitrixSocio>> ListSociosAsync()
        {
            var body = new
            {
                entityTypeId = BitrixConstants.EntityTypeSocios
            };

            _logger.Debug("Requesting socios list from Bitrix24");

            var response = await DoRequestAsync<BitrixItemListResponse<BitrixSocio>>("crm.item.list", body);

            if (response?.Result?.Items == null)
            {
                return new List<BitrixSocio>();
            }

            _logger.Debug($"Retrieved {response.Result.Items.Count} socios from Bitrix24");
            return response.Result.Items;
        }

        public async Task<int> CreateSocioAsync(BitrixSocio socio)
        {
            var body = new
            {
                fields = new
                {
                    title = socio.Title,
                    ufCrm55Dni = socio.DNI,
                    ufCrm55Cargo = socio.Cargo,
                    ufCrm55Admin = socio.Administrador,
                    ufCrm55Participacion = socio.Participacion,
                    ufCrm55RazonSocial = socio.RazonSocialEmpleado
                },
                entityTypeId = BitrixConstants.EntityTypeSocios
            };

            _logger.Debug($"Creating socio with DNI: {socio.DNI}");

            var response = await DoRequestAsync<BitrixItemAddResponse>("crm.item.add", body);

            int id = response?.Result?.Item?.ID ?? 0;
            _logger.Debug($"Created socio with ID: {id}");

            return id;
        }

        public async Task UpdateSocioAsync(int id, BitrixSocio socio)
        {
            var body = new
            {
                id = id,
                fields = new
                {
                    title = socio.Title,
                    ufCrm55Dni = socio.DNI,
                    ufCrm55Cargo = socio.Cargo,
                    ufCrm55Admin = socio.Administrador,
                    ufCrm55Participacion = socio.Participacion,
                    ufCrm55RazonSocial = socio.RazonSocialEmpleado
                },
                entityTypeId = BitrixConstants.EntityTypeSocios
            };

            _logger.Debug($"Updating socio with ID: {id}");

            await DoRequestAsync<BitrixUpdateResponse>("crm.item.update", body);

            _logger.Debug($"Successfully updated socio with ID: {id}");
        }

        #endregion

        #region Companies (Legacy)

        public async Task<List<BitrixCompany>> ListCompaniesAsync()
        {
            var body = new
            {
                entityTypeId = BitrixConstants.EntityTypeCompanies
            };

            _logger.Debug("Requesting companies list from Bitrix24");

            var response = await DoRequestAsync<BitrixItemListResponse<BitrixCompany>>("crm.item.list", body);

            if (response?.Result?.Items == null)
            {
                return new List<BitrixCompany>();
            }

            _logger.Debug($"Retrieved {response.Result.Items.Count} companies from Bitrix24");
            return response.Result.Items;
        }

        public async Task<int> CreateCompanyAsync(BitrixCompany company)
        {
            var body = new
            {
                fields = new
                {
                    title = company.Title,
                    ufCrmCompanyCategoria = company.CodigoCategoriaCliente,
                    ufCrmCompanyRazon = company.RazonSocial,
                    ufCrmCompanyDivisa = company.CodigoDivisa,
                    ufCrmCompanyDomicilio = company.Domicilio,
                    ufCrmCompanyDomicilio2 = company.Domicilio2,
                    ufCrmCompanyMunicipio = company.Municipio,
                    ufCrmCompanyCodigoPostal = company.CodigoPostal,
                    ufCrmCompanyProvincia = company.Provincia,
                    ufCrmCompanyNacion = company.Nacion,
                    ufCrmCompanyCodigoNacion = company.CodigoNacion,
                    ufCrmCompanyTelefono = company.Telefono,
                    ufCrmCompanyEmail = company.EMail1
                },
                entityTypeId = BitrixConstants.EntityTypeCompanies
            };

            _logger.Debug($"Creating company with codigo: {company.CodigoCategoriaCliente}");

            var response = await DoRequestAsync<BitrixItemAddResponse>("crm.item.add", body);

            int id = response?.Result?.Item?.ID ?? 0;
            _logger.Debug($"Created company with ID: {id}");

            return id;
        }

        public async Task UpdateCompanyAsync(int id, BitrixCompany company)
        {
            var body = new
            {
                id = id,
                fields = new
                {
                    title = company.Title,
                    ufCrmCompanyCategoria = company.CodigoCategoriaCliente,
                    ufCrmCompanyRazon = company.RazonSocial,
                    ufCrmCompanyDivisa = company.CodigoDivisa,
                    ufCrmCompanyDomicilio = company.Domicilio,
                    ufCrmCompanyDomicilio2 = company.Domicilio2,
                    ufCrmCompanyMunicipio = company.Municipio,
                    ufCrmCompanyCodigoPostal = company.CodigoPostal,
                    ufCrmCompanyProvincia = company.Provincia,
                    ufCrmCompanyNacion = company.Nacion,
                    ufCrmCompanyCodigoNacion = company.CodigoNacion,
                    ufCrmCompanyTelefono = company.Telefono,
                    ufCrmCompanyEmail = company.EMail1
                },
                entityTypeId = BitrixConstants.EntityTypeCompanies
            };

            _logger.Debug($"Updating company with ID: {id}");

            await DoRequestAsync<BitrixUpdateResponse>("crm.item.update", body);

            _logger.Debug($"Successfully updated company with ID: {id}");
        }

        #endregion

        #region Helper Methods

        // 🔥 MÉTODO MEJORADO CON MANEJO ROBUSTO DE EXCEPCIONES
        private async Task<T> DoRequestAsync<T>(string method, object body)
        {
            const int maxRetries = 3;
            const int retryDelayMs = 2000;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    string url = $"{_baseUrl}{method}";
                    _logger.Debug($"Making request to: {url} (attempt {attempt}/{maxRetries})");

                    string jsonBody = JsonConvert.SerializeObject(body);
                    _logger.Debug($"Request body: {jsonBody}");

                    using (var content = new StringContent(jsonBody, Encoding.UTF8, "application/json"))
                    {
                        // 🔥 CLAVE: Usar timeout más corto y reintentos
                        HttpResponseMessage response = await _httpClient.PostAsync(url, content);
                        string responseContent = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            string errorMsg = $"HTTP {response.StatusCode}: {responseContent}";
                            _logger.Warning($"API request failed: {errorMsg}");

                            // Retry en ciertos códigos de error
                            if (ShouldRetryStatusCode(response.StatusCode) && attempt < maxRetries)
                            {
                                _logger.Info($"Retrying in {retryDelayMs}ms...");
                                await Task.Delay(retryDelayMs);
                                continue;
                            }

                            throw new HttpRequestException(errorMsg);
                        }

                        if (string.IsNullOrEmpty(responseContent) || responseContent == "[]")
                        {
                            _logger.Debug("Received empty response");
                            return default(T);
                        }

                        var result = JsonConvert.DeserializeObject<T>(responseContent);
                        _logger.Debug($"✅ Request successful on attempt {attempt}");
                        return result;
                    }
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.CancellationToken.IsCancellationRequested == false)
                {
                    // 🔥 TIMEOUT - Este era tu problema principal
                    string timeoutMsg = $"⏰ Timeout on Bitrix24 API call (attempt {attempt}/{maxRetries}): {method}";
                    _logger.Warning(timeoutMsg);

                    if (attempt == maxRetries)
                    {
                        _logger.Error($"❌ All {maxRetries} attempts failed due to timeout: {method}");
                        throw new Exception($"Bitrix24 API timeout after {maxRetries} attempts: {method}", ex);
                    }

                    _logger.Info($"Retry {attempt + 1} in {retryDelayMs}ms...");
                    await Task.Delay(retryDelayMs);
                }
                catch (HttpRequestException ex)
                {
                    // 🔥 ERRORES DE RED
                    string networkMsg = $"🌐 Network error on Bitrix24 API (attempt {attempt}/{maxRetries}): {ex.Message}";
                    _logger.Warning(networkMsg);

                    if (attempt == maxRetries)
                    {
                        _logger.Error($"❌ All {maxRetries} attempts failed due to network error: {method}");
                        throw new Exception($"Bitrix24 API network error after {maxRetries} attempts: {method}", ex);
                    }

                    _logger.Info($"Retry {attempt + 1} in {retryDelayMs}ms...");
                    await Task.Delay(retryDelayMs);
                }
                catch (JsonException ex)
                {
                    // 🔥 ERRORES DE DESERIALIZACIÓN
                    _logger.Error($"❌ JSON deserialization error for {method}: {ex.Message}");
                    throw new Exception($"Invalid JSON response from Bitrix24 API: {method}", ex);
                }
                catch (Exception ex)
                {
                    // 🔥 OTROS ERRORES INESPERADOS
                    _logger.Error($"❌ Unexpected error on Bitrix24 API call: {method} - {ex.Message}");

                    if (attempt == maxRetries)
                    {
                        throw new Exception($"Bitrix24 API unexpected error after {maxRetries} attempts: {method}", ex);
                    }

                    _logger.Info($"Retry {attempt + 1} in {retryDelayMs}ms...");
                    await Task.Delay(retryDelayMs);
                }
            }

            // No debería llegar aquí nunca
            throw new Exception($"Unexpected end of retry loop for method: {method}");
        }

        // Método helper para determinar si reintentar según código HTTP
        private static bool ShouldRetryStatusCode(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.InternalServerError ||
                   statusCode == HttpStatusCode.BadGateway ||
                   statusCode == HttpStatusCode.ServiceUnavailable ||
                   statusCode == HttpStatusCode.GatewayTimeout ||
                   (int)statusCode == 429;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        #endregion
    }

    #region Validation Models

    public class BitrixUserField
    {
        [JsonProperty("ID")]
        public string Id { get; set; }

        [JsonProperty("FIELD_NAME")]
        public string FieldName { get; set; }

        [JsonProperty("ENTITY_ID")]
        public string EntityId { get; set; }

        [JsonProperty("USER_TYPE_ID")]
        public string UserTypeId { get; set; }

        [JsonProperty("MANDATORY")]
        public string MandatoryFlag { get; set; }

        public bool Mandatory => MandatoryFlag == "Y";

        [JsonProperty("MULTIPLE")]
        public string MultipleFlag { get; set; }

        public bool Multiple => MultipleFlag == "Y";

        [JsonProperty("SORT")]
        public int Sort { get; set; }
    }

    public class BitrixUserFieldListResponse
    {
        [JsonProperty("result")]
        public List<BitrixUserField> Result { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("error_description")]
        public string ErrorDescription { get; set; }
    }

    #endregion

    #region Validation and Response Models

    public class FieldValidationResult
    {
        public bool IsValid { get; set; }
        public List<ValidFieldInfo> ValidFields { get; set; }
        public List<string> MissingFields { get; set; }
        public string ErrorMessage { get; set; }

        public FieldValidationResult()
        {
            ValidFields = new List<ValidFieldInfo>();
            MissingFields = new List<string>();
            IsValid = false;
        }
    }

    public class ValidFieldInfo
    {
        public string FieldName { get; set; }
        public string SageFieldName { get; set; }
        public string UserTypeId { get; set; }
        public bool IsMandatory { get; set; }
        public bool IsActive { get; set; }
    }

    public class BitrixItemListResponse<T>
    {
        [JsonProperty("result")]
        public BitrixItemListResult<T> Result { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }
    }

    public class BitrixItemListResult<T>
    {
        [JsonProperty("items")]
        public List<T> Items { get; set; }
    }

    public class BitrixItemAddResponse
    {
        [JsonProperty("result")]
        public BitrixItemAddResult Result { get; set; }
    }

    public class BitrixItemAddResult
    {
        [JsonProperty("item")]
        public BitrixItemId Item { get; set; }
    }

    public class BitrixItemId
    {
        [JsonProperty("id")]
        public int ID { get; set; }
    }

    public class BitrixUpdateResponse
    {
        [JsonProperty("result")]
        public BitrixUpdateResult Result { get; set; }
    }

    public class BitrixUpdateResult
    {
        [JsonProperty("success")]
        public bool Success { get; set; }
    }

    #endregion
}