using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConnectorSageBitrix.Models;

namespace ConnectorSageBitrix.Config
{
    public static class ConfigManager
    {
        private static readonly object _lockObject = new object();
        private static AppConfig _cachedConfig = null;
        private static DateTime _configLoadTime = DateTime.MinValue;
        private static readonly TimeSpan ConfigCacheTime = TimeSpan.FromMinutes(5);

        public static AppConfig LoadConfig()
        {
            lock (_lockObject)
            {
                // Use cached config if it's still valid
                if (_cachedConfig != null && (DateTime.Now - _configLoadTime) < ConfigCacheTime)
                {
                    Log("Using cached configuration");
                    return _cachedConfig;
                }

                Log("Loading configuration...");

                // Try to load from encrypted file first
                var encryptedConfig = LoadEncryptedConfig();
                if (encryptedConfig != null)
                {
                    Log("Loaded configuration from encrypted file");
                    _cachedConfig = encryptedConfig;
                    _configLoadTime = DateTime.Now;
                    return encryptedConfig;
                }

                // Fallback to App.config
                Log("Loading configuration from App.config");
                var config = LoadFromAppConfig();

                if (config != null && ValidateConfig(config))
                {
                    _cachedConfig = config;
                    _configLoadTime = DateTime.Now;
                    return config;
                }

                Log("Failed to load valid configuration from any source");
                return null;
            }
        }

        private static AppConfig LoadFromAppConfig()
        {
            try
            {
                var config = new AppConfig
                {
                    ClientCode = ConfigurationManager.AppSettings["CLIENT_CODE"],
                    BitrixClientCode = ConfigurationManager.AppSettings["BITRIX_CLIENT_CODE"],
                    PackEmpresa = bool.Parse(ConfigurationManager.AppSettings["PACK_EMPRESA"] ?? "false"),

                    DB = new DatabaseConfig
                    {
                        Host = ConfigurationManager.AppSettings["DB_HOST"],
                        Port = ConfigurationManager.AppSettings["DB_PORT"] ?? "1433",
                        Database = ConfigurationManager.AppSettings["DB_DATABASE"],
                        User = ConfigurationManager.AppSettings["DB_USERNAME"],
                        Password = ConfigurationManager.AppSettings["DB_PASSWD"],
                        LicenseID = ConfigurationManager.AppSettings["LICENSE_ID"]
                    },

                    Bitrix = new BitrixConfig
                    {
                        URL = ConfigurationManager.AppSettings["BITRIX_URL"]
                    },

                    Sync = new SyncConfig
                    {
                        Interval = TimeSpan.FromMinutes(
                            int.Parse(ConfigurationManager.AppSettings["SYNC_INTERVAL_MINUTES"] ?? "5"))
                    },

                    // Load default field mappings for App.config
                    FieldMappings = GetDefaultFieldMappings()
                };

                Log("Configuration loaded from App.config");
                return config;
            }
            catch (Exception ex)
            {
                Log($"Error loading App.config: {ex.Message}");
                return null;
            }
        }

        private static AppConfig LoadEncryptedConfig()
        {
            try
            {
                // Standard config file path
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Btic", "ConfigConnectorBitrix", "config");

                Log($"Looking for encrypted config at: {configPath}");

                if (!File.Exists(configPath))
                {
                    Log("Encrypted config file not found");
                    return null;
                }

                byte[] encryptedData;
                try
                {
                    encryptedData = File.ReadAllBytes(configPath);
                    Log($"Read {encryptedData.Length} bytes from encrypted config file");
                }
                catch (Exception ex)
                {
                    Log($"Error reading encrypted config file: {ex.Message}");
                    return null;
                }

                // Get computer info for key generation
                string computerInfo;
                try
                {
                    computerInfo = GetComputerInfo();
                    Log($"Using computer info for decryption: {computerInfo}");
                }
                catch (Exception ex)
                {
                    Log($"Error getting computer info: {ex.Message}");
                    return null;
                }

                // Generate key and IV
                byte[] standardKey;
                byte[] standardIv;
                try
                {
                    string charKey = "T"; // Use 'T' for config files
                    standardKey = GetKey(32, computerInfo, charKey[0]);
                    standardIv = GetKey(16, computerInfo, charKey[0]);
                    Log($"Generated keys - Key: {standardKey.Length} bytes, IV: {standardIv.Length} bytes");
                }
                catch (Exception ex)
                {
                    Log($"Error generating keys: {ex.Message}");
                    return null;
                }

                // Decrypt the data
                byte[] decryptedBytes;
                try
                {
                    decryptedBytes = DecryptAESData(encryptedData, standardKey, standardIv);
                    Log($"Decrypted {decryptedBytes.Length} bytes");
                }
                catch (Exception ex)
                {
                    Log($"Error decrypting data: {ex.Message}");
                    return null;
                }

                // Convert to string
                string standardJsonConfig;
                try
                {
                    standardJsonConfig = Encoding.UTF8.GetString(decryptedBytes).Trim();
                    Log($"Converted to string: {(standardJsonConfig.Length > 100 ?
                        standardJsonConfig.Substring(0, 100) + "..." : standardJsonConfig)}");
                }
                catch (Exception ex)
                {
                    Log($"Error converting decrypted data to string: {ex.Message}");
                    return null;
                }

                // Parse JSON
                JObject finalConfig;
                try
                {
                    JsonSerializerSettings settings = new JsonSerializerSettings
                    {
                        // Set encoding-related settings if needed
                    };
                    finalConfig = JsonConvert.DeserializeObject<JObject>(standardJsonConfig, settings);
                    Log("Successfully parsed JSON config");
                }
                catch (JsonException ex)
                {
                    Log($"Error parsing JSON: {ex.Message}");
                    return null;
                }

                // Mostrar estructura del JSON para debugging
                Log("Estructura del JSON desencriptado:");
                foreach (var prop in finalConfig.Properties())
                {
                    if (prop.Name.ToLower().Contains("password"))
                        Log($"- {prop.Name}: [REDACTED]");
                    else
                        Log($"- {prop.Name}: {(prop.Value is JObject || prop.Value is JArray ? "[Object/Array]" : prop.Value)}");
                }

                // Validate basic config
                if (finalConfig["CodigoCliente"]?.Value<string>() == null)
                {
                    Log("Decrypted config is missing CodigoCliente");
                    return null;
                }

                // Load FieldMappings
                var fieldMappings = new List<FieldMapping>();
                if (finalConfig["FieldMappings"] is JArray mappingsArray)
                {
                    Log($"Loading {mappingsArray.Count} field mappings");

                    foreach (var mappingToken in mappingsArray)
                    {
                        try
                        {
                            var mapping = new FieldMapping
                            {
                                BitrixFieldName = mappingToken["bitrixFieldName"]?.Value<string>(),
                                BitrixFieldType = mappingToken["bitrixFieldType"]?.Value<string>(),
                                SageFieldName = mappingToken["sageFieldName"]?.Value<string>(),
                                SageFieldDescription = mappingToken["sageFieldDescription"]?.Value<string>(),
                                IsActive = mappingToken["isActive"]?.Value<bool>() ?? true,
                                IsMandatory = mappingToken["isMandatory"]?.Value<bool>() ?? false
                            };

                            // Validar mapeo
                            if (!string.IsNullOrEmpty(mapping.BitrixFieldName) &&
                                !string.IsNullOrEmpty(mapping.SageFieldName))
                            {
                                fieldMappings.Add(mapping);
                                Log($"Loaded mapping: {mapping.SageFieldName} -> {mapping.BitrixFieldName}");
                            }
                            else
                            {
                                Log($"Skipped invalid mapping: {mappingToken}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Error loading field mapping: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Log("No FieldMappings found in config, using default mappings");
                    fieldMappings = GetDefaultFieldMappings();
                }

                // Log the pack_empresa value
                var bitrix24 = finalConfig["Bitrix24"]?.Value<JObject>();
                bool packEmpresa = false;
                if (bitrix24 != null)
                {
                    packEmpresa = bitrix24["pack_empresa"]?.Value<bool>() ?? false;
                    Log($"Decrypted config - pack_empresa: {packEmpresa}");
                }
                else
                {
                    Log("Bitrix24 section not found in config");
                }

                // Create AppConfig from JSON
                var config = new AppConfig
                {
                    ClientCode = finalConfig["CodigoCliente"]?.Value<string>(),
                    PackEmpresa = packEmpresa,
                    FieldMappings = fieldMappings,

                    DB = new DatabaseConfig
                    {
                        Host = finalConfig["DB"]?["DB_Host"]?.Value<string>(),
                        Port = finalConfig["DB"]?["DB_Port"]?.Value<string>() ?? "1433",
                        Database = finalConfig["DB"]?["DB_Database"]?.Value<string>(),
                        User = finalConfig["DB"]?["DB_Username"]?.Value<string>(),
                        Password = finalConfig["DB"]?["DB_Password"]?.Value<string>(),
                        LicenseID = finalConfig["DB"]?["IdLlicencia"]?.Value<string>()
                    },

                    Bitrix = new BitrixConfig
                    {
                        URL = finalConfig["Bitrix24"]?["API_Tenant"]?.Value<string>()
                    }
                };

                // Set BitrixClientCode
                config.BitrixClientCode = config.ClientCode;

                Log($"Successfully loaded encrypted config with {fieldMappings.Count} field mappings");
                return config;
            }
            catch (Exception ex)
            {
                Log($"Unexpected error loading encrypted config: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        private static List<FieldMapping> GetDefaultFieldMappings()
        {
            return new List<FieldMapping>
            {
                new FieldMapping
                {
                    BitrixFieldName = "UF_CRM_COMPANY_CATEGORIA",
                    BitrixFieldType = "string",
                    SageFieldName = "CodigoCategoriaCliente",
                    SageFieldDescription = "Código de categoría del cliente",
                    IsActive = true,
                    IsMandatory = true
                },
                new FieldMapping
                {
                    BitrixFieldName = "UF_CRM_COMPANY_RAZON",
                    BitrixFieldType = "string",
                    SageFieldName = "RazonSocial",
                    SageFieldDescription = "Razón social de la empresa",
                    IsActive = true,
                    IsMandatory = true
                },
                new FieldMapping
                {
                    BitrixFieldName = "UF_CRM_COMPANY_DIVISA",
                    BitrixFieldType = "string",
                    SageFieldName = "CodigoDivisa",
                    SageFieldDescription = "Código de divisa",
                    IsActive = true,
                    IsMandatory = false
                },
                new FieldMapping
                {
                    BitrixFieldName = "UF_CRM_COMPANY_DOMICILIO",
                    BitrixFieldType = "string",
                    SageFieldName = "Domicilio",
                    SageFieldDescription = "Dirección principal",
                    IsActive = true,
                    IsMandatory = false
                },
                new FieldMapping
                {
                    BitrixFieldName = "UF_CRM_COMPANY_TELEFONO",
                    BitrixFieldType = "string",
                    SageFieldName = "Telefono",
                    SageFieldDescription = "Número de teléfono",
                    IsActive = true,
                    IsMandatory = false
                },
                new FieldMapping
                {
                    BitrixFieldName = "UF_CRM_COMPANY_EMAIL",
                    BitrixFieldType = "string",
                    SageFieldName = "EMail1",
                    SageFieldDescription = "Correo electrónico principal",
                    IsActive = true,
                    IsMandatory = false
                }
            };
        }

        private static bool ValidateConfig(AppConfig config)
        {
            // Check client code
            if (string.IsNullOrEmpty(config.ClientCode))
            {
                Log("Missing CLIENT_CODE configuration");
                return false;
            }

            if (string.IsNullOrEmpty(config.BitrixClientCode))
            {
                config.BitrixClientCode = config.ClientCode;
            }

            // Check license ID
            if (string.IsNullOrEmpty(config.DB.LicenseID))
            {
                Log("Missing LICENSE_ID configuration");
                return false;
            }

            // Check database configuration
            if (string.IsNullOrEmpty(config.DB.Host))
            {
                Log("Missing DB_HOST configuration");
                return false;
            }

            if (string.IsNullOrEmpty(config.DB.Database))
            {
                Log("Missing DB_DATABASE configuration");
                return false;
            }

            if (string.IsNullOrEmpty(config.DB.User))
            {
                Log("Missing DB_USERNAME configuration");
                return false;
            }

            if (string.IsNullOrEmpty(config.DB.Password))
            {
                Log("Missing DB_PASSWORD configuration");
                return false;
            }

            // Check Bitrix configuration only if PackEmpresa is enabled
            if (config.PackEmpresa && string.IsNullOrEmpty(config.Bitrix.URL))
            {
                Log("Missing BITRIX_URL configuration (required when PackEmpresa is enabled)");
                return false;
            }

            Log("Configuration validation passed");
            return true;
        }

        private static string GetComputerInfo()
        {
            // Get MAC address
            string macAddress = "00:00:00:00:00:00"; // Default fallback
            try
            {
                macAddress = "02:00:00:00:00:00"; // Hardcoded for testing
                Log($"Using hardcoded MAC address: {macAddress}");
            }
            catch (Exception ex)
            {
                Log($"Error getting MAC address: {ex.Message}");
                Log($"Using hardcoded MAC address: {macAddress}");
            }

            // Get hostname
            string hostname = "unknown";
            try
            {
                hostname = Environment.MachineName;
                Log($"Detected hostname: {hostname}");
            }
            catch (Exception ex)
            {
                Log($"Error getting hostname: {ex.Message}");
            }

            // Combine MAC and hostname
            string result = macAddress + hostname;
            Log($"Final computer info for key generation: {result}");
            return result;
        }

        private static byte[] GetKey(int keyLength, string computerInfo, char paddingChar)
        {
            string paddedInfo = pad_with_char(computerInfo, keyLength, paddingChar);
            Log($"Padded key info (length {keyLength}): {paddedInfo}");

            // Usar ASCII para coincidir con el otro sistema
            byte[] result = Encoding.ASCII.GetBytes(paddedInfo);
            return result;
        }

        private static string pad_with_char(string input, int length, char padChar)
        {
            // This function mirrors the Rust implementation
            if (input.Length > length)
            {
                return input.Substring(0, length);
            }
            else
            {
                return input.PadRight(length, padChar);
            }
        }

        private static byte[] DecryptAESData(byte[] encryptedData, byte[] key, byte[] iv)
        {
            try
            {
                using (Aes aesAlg = Aes.Create())
                {
                    aesAlg.Key = key;
                    aesAlg.IV = iv;
                    aesAlg.Mode = CipherMode.CBC;
                    aesAlg.Padding = PaddingMode.PKCS7;

                    Log($"Decrypting {encryptedData.Length} bytes with AES-CBC");

                    using (ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV))
                    using (MemoryStream msDecrypt = new MemoryStream(encryptedData))
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        List<byte> decryptedData = new List<byte>();
                        int b;
                        while ((b = csDecrypt.ReadByte()) != -1)
                        {
                            decryptedData.Add((byte)b);
                        }
                        return decryptedData.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Decryption error: {ex.Message}");
                throw;
            }
        }

        private static void Log(string message)
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ConnectorSageBitrix");

                Directory.CreateDirectory(logDir);

                string logFile = Path.Combine(logDir, "config-manager.log");
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}\r\n";

                File.AppendAllText(logFile, logEntry);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public static void ClearCache()
        {
            lock (_lockObject)
            {
                _cachedConfig = null;
                _configLoadTime = DateTime.MinValue;
                Log("Configuration cache cleared");
            }
        }
    }
}
