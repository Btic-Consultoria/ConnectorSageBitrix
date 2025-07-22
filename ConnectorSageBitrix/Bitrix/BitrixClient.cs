using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConnectorSageBitrix.Logging;
using ConnectorSageBitrix.Extensions;
using System.Linq;

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
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        #region Companies with Dynamic Field Mapping

        /// <summary>
        /// Actualiza una empresa en Bitrix24 con campos mapeados dinámicamente
        /// </summary>
        public async Task<bool> UpdateCompany(string companyId, Dictionary<string, object> mappedFields)
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

                    var response = await client.PostAsync(apiUrl, content);
                    var responseContent = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        var result = JsonConvert.DeserializeObject<dynamic>(responseContent);
                        
                        if (result?.result == true)
                        {
                            _logger.Info($"Successfully updated company {companyId}");
                            return true;
                        }
                        else
                        {
                            _logger.Error($"Bitrix24 returned error: {responseContent}");
                            return false;
                        }
                    }
                    else
                    {
                        _logger.Error($"HTTP error updating company {companyId}: {response.StatusCode} - {responseContent}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Exception updating company {companyId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Limpia y valida valores de campos antes de enviarlos a Bitrix24
        /// </summary>
        private object CleanFieldValue(object value, string fieldName)
        {
            if (value == null)
                return null;

            try
            {
                string stringValue = value.ToString().Trim();
                
                if (string.IsNullOrEmpty(stringValue))
                    return null;

                // Limpiar según el tipo de campo
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
                else if (fieldName.Contains("DIVISA"))
                {
                    // Convertir divisa a mayúsculas
                    return stringValue.ToUpper();
                }
                else
                {
                    // Campo de texto general - limitar longitud
                    const int maxLength = 255;
                    if (stringValue.Length > maxLength)
                    {
                        _logger.Warning($"Truncating field {fieldName} from {stringValue.Length} to {maxLength} characters");
                        return stringValue.Substring(0, maxLength);
                    }
                    return stringValue;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error cleaning field value for {fieldName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Método de utilidad para probar la conexión con campos mapeados
        /// </summary>
        public async Task<bool> TestFieldMappings(Dictionary<string, object> testFields)
        {
            try
            {
                _logger.Info("Testing field mappings with Bitrix24");

                // Crear empresa de prueba
                var testCompany = new
                {
                    fields = new
                    {
                        TITLE = "Test Company for Field Mapping",
                        COMPANY_TYPE = "OTHER"
                    }
                };

                // Crear empresa temporal
                string apiUrl = $"{_baseUrl}crm.company.add.json";
                using (var client = new HttpClient())
                {
                    var jsonContent = JsonConvert.SerializeObject(testCompany);
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(apiUrl, content);
                    var responseContent = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        dynamic result = JsonConvert.DeserializeObject(responseContent);
                        string companyId = result?.result?.ToString();

                        if (!string.IsNullOrEmpty(companyId))
                        {
                            _logger.Info($"Created test company {companyId}");

                            // Probar actualización con campos mapeados
                            bool updateSuccess = await UpdateCompany(companyId, testFields);

                            // Eliminar empresa de prueba
                            await DeleteTestCompany(companyId);

                            return updateSuccess;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error testing field mappings: {ex.Message}");
                return false;
            }
        }

        private async Task DeleteTestCompany(string companyId)
        {
            try
            {
                string apiUrl = $"{_baseUrl}crm.company.delete.json";
                var deleteData = new { id = companyId };

                using (var client = new HttpClient())
                {
                    var jsonContent = JsonConvert.SerializeObject(deleteData);
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                    await client.PostAsync(apiUrl, content);
                }

                _logger.Info($"Deleted test company {companyId}");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Could not delete test company {companyId}: {ex.Message}");
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

        private async Task<T> DoRequestAsync<T>(string method, object body)
        {
            string url = $"{_baseUrl}{method}";
            _logger.Debug($"Making request to: {url}");

            string jsonBody = JsonConvert.SerializeObject(body);
            _logger.Debug($"Request body: {jsonBody}");
            StringContent content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync(url, content);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Invalid status code: {response.StatusCode}, body: {responseContent}");
            }

            if (string.IsNullOrEmpty(responseContent) || responseContent == "[]")
            {
                _logger.Debug("Received empty response");
                return default(T);
            }

            return JsonConvert.DeserializeObject<T>(responseContent);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        #endregion
    }

    #region Response Types

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
