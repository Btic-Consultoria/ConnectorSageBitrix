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
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Obtiene los user fields de Company desde Bitrix24
        /// </summary>
        public async Task<List<BitrixUserField>> GetCompanyUserFields()
        {
            try
            {
                var body = new { };

                _logger.Debug("Requesting company user fields from Bitrix24");

                var response = await DoRequestAsync<BitrixUserFieldListResponse>("crm.company.userfield.list", body);

                if (response?.Result == null)
                {
                    _logger.Warning("No user fields received from Bitrix24");
                    return new List<BitrixUserField>();
                }

                var fields = response.Result
                    .Where(f => f.EntityId == "CRM_COMPANY")
                    .ToList();

                _logger.Debug($"Retrieved {fields.Count} company user fields from Bitrix24");

                return fields;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error getting company user fields: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Crea user fields faltantes en Bitrix24 (opcional)
        /// </summary>
        public async Task<bool> CreateMissingUserFields(List<string> missingFields)
        {
            try
            {
                _logger.Info($"Creating {missingFields.Count} missing user fields in Bitrix24");

                foreach (var fieldName in missingFields)
                {
                    var success = await CreateUserField(fieldName);
                    if (success)
                    {
                        _logger.Info($"✅ Created user field: {fieldName}");
                    }
                    else
                    {
                        _logger.Error($"❌ Failed to create user field: {fieldName}");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error creating missing user fields: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> CreateUserField(string fieldName)
        {
            try
            {
                var body = new
                {
                    fields = new
                    {
                        ENTITY_ID = "CRM_COMPANY",
                        FIELD_NAME = fieldName,
                        USER_TYPE_ID = "string",
                        SORT = 100,
                        MULTIPLE = "N",
                        MANDATORY = "N",
                        SHOW_FILTER = "Y",
                        SHOW_IN_LIST = "Y",
                        EDIT_IN_LIST = "Y",
                        IS_SEARCHABLE = "Y"
                    }
                };

                var response = await DoRequestAsync<dynamic>("crm.company.userfield.add", body);
                return response != null;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error creating user field {fieldName}: {ex.Message}");
                return false;
            }
        }

        #endregion

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

    #endregion

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