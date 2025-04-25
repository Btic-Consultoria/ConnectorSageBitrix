using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConnectorSageBitrix.Logging;
using System.Net.NetworkInformation;
using System.Xml.Linq;
using System.Linq;

namespace ConnectorSageBitrix.Config
{
    public static class ConfigManager
    {
        private const string EncryptedConfigPath = @"C:\ProgramData\Btic\ConfigConnectorBitrix\config";

        private static Logger _logger;

        public static AppConfig Load(Logger logger = null)
        {
            _logger = logger;

            try
            {
                Log("Starting configuration loading process");

                // Initialize a new config with default values
                var config = new AppConfig();
                Log("Default configuration: Synchronization disabled");

                // First try to load encrypted config
                Log("Attempting to load encrypted configuration");
                var encryptedConfig = LoadEncryptedConfig();

                if (encryptedConfig != null)
                {
                    Log("Successfully loaded encrypted configuration");

                    // Set client code from encrypted config
                    config.ClientCode = encryptedConfig["CodigoCliente"]?.Value<string>();
                    Log($"Client code from encrypted config: {config.ClientCode}");

                    // Always set BitrixClientCode to the root CodigoCliente for now
                    config.BitrixClientCode = config.ClientCode;

                    // Set DB configuration from encrypted config
                    var dbSection = encryptedConfig["DB"] as JObject;
                    if (dbSection != null)
                    {
                        Log("Processing DB section from encrypted config");
                        config.DB.Host = dbSection["DB_Host"]?.Value<string>();
                        Log($"DB Host: {config.DB.Host}");

                        config.DB.Port = dbSection["DB_Port"]?.Value<string>();
                        Log($"DB Port: {config.DB.Port}");

                        config.DB.Database = dbSection["DB_Database"]?.Value<string>();
                        Log($"DB Database: {config.DB.Database}");

                        config.DB.User = dbSection["DB_Username"]?.Value<string>();
                        Log($"DB Username: {config.DB.User}");

                        config.DB.Password = dbSection["DB_Password"]?.Value<string>();
                        Log("DB Password: [REDACTED]");

                        config.DB.LicenseID = dbSection["IdLlicencia"]?.Value<string>();
                        Log($"License ID: {config.DB.LicenseID}");
                    }
                    else
                    {
                        Log("DB section not found in encrypted config");
                    }

                    // Set Bitrix URL from encrypted config
                    var bitrixSection = encryptedConfig["Bitrix24"]?.Value<JObject>();
                    if (bitrixSection != null)
                    {
                        Log("Processing Bitrix24 section from encrypted config");
                        string apiTenant = bitrixSection["API_Tenant"]?.Value<string>();
                        if (!string.IsNullOrEmpty(apiTenant))
                        {
                            config.Bitrix.URL = apiTenant;
                            Log($"Bitrix URL: {config.Bitrix.URL}");
                        }

                        // Set PackEmpresa flag from encrypted config
                        config.PackEmpresa = bitrixSection["pack_empresa"]?.Value<bool>() ?? false;
                        Log($"PackEmpresa: {config.PackEmpresa}");
                    }
                    else
                    {
                        Log("Bitrix24 section not found in encrypted config");
                    }
                }
                else
                {
                    Log("Failed to load encrypted config - falling back to App.config");
                    // Rest of App.config fallback logic...
                }

                // Validate configuration
                Log("Validating configuration");
                if (!ValidateConfig(config))
                {
                    Log("Configuration validation failed");
                    return null;
                }

                Log("Configuration loaded successfully");
                return config;
            }
            catch (Exception ex)
            {
                Log($"Critical error loading configuration: {ex.Message}", LogLevel.Error);
                Log($"Stack trace: {ex.StackTrace}", LogLevel.Error);
                return null;
            }
        }

        // Método auxiliar para logs
        private static void Log(string message, LogLevel level = LogLevel.Info)
        {
            if (_logger == null)
            {
                Console.WriteLine(message);
                return;
            }

            switch (level)
            {
                case LogLevel.Info:
                    _logger.Info(message);
                    break;
                case LogLevel.Debug:
                    _logger.Debug(message);
                    break;
                case LogLevel.Error:
                    _logger.Error(message);
                    break;
                case LogLevel.Fatal:
                    _logger.Fatal(message);
                    break;
            }
        }

        private enum LogLevel
        {
            Info,
            Debug,
            Error,
            Fatal
        }

        private static JObject LoadEncryptedConfig()
        {
            try
            {
                string configPath = EncryptedConfigPath;
                Log($"Attempting to load encrypted config from: {configPath}");

                // Check if the file exists
                if (!File.Exists(configPath))
                {
                    Log($"Encrypted config file not found at {configPath}");
                    return null;
                }

                // Read encrypted file
                byte[] encryptedData;
                try
                {
                    encryptedData = File.ReadAllBytes(configPath);
                    Log($"Read {encryptedData.Length} bytes from encrypted config file");

                    // DIAGNOSTIC: Add hex dump of first 32 bytes
                    StringBuilder hexDump = new StringBuilder("First 32 bytes (hex): ");
                    for (int i = 0; i < Math.Min(32, encryptedData.Length); i++)
                    {
                        hexDump.Append(encryptedData[i].ToString("X2") + " ");
                    }
                    Log(hexDump.ToString());

                    // Check if file has metadata header (first 4 bytes = metadata length)
                    if (encryptedData.Length >= 4)
                    {
                        int metadataLen = BitConverter.ToInt32(encryptedData, 0);
                        Log($"Detected metadata header. Indicated metadata length: {metadataLen} bytes");

                        if (metadataLen > 0 && metadataLen < 200 && encryptedData.Length >= 4 + metadataLen)
                        {
                            string metadataStr = Encoding.UTF8.GetString(encryptedData, 4, metadataLen);
                            Log($"Found metadata: {metadataStr}");

                            // Try to parse metadata to extract info
                            string extractedMac = "";
                            string extractedHost = "";
                            string extractedKeyChar = "T";

                            foreach (var part in metadataStr.Split(';'))
                            {
                                if (part.StartsWith("MAC="))
                                    extractedMac = part.Substring(4);
                                else if (part.StartsWith("HOST="))
                                    extractedHost = part.Substring(5);
                                else if (part.StartsWith("KEY_CHAR="))
                                    extractedKeyChar = part.Substring(9);
                            }

                            if (!string.IsNullOrEmpty(extractedMac) && !string.IsNullOrEmpty(extractedHost))
                            {
                                Log($"Will use MAC={extractedMac}, HOST={extractedHost}, KEY_CHAR={extractedKeyChar} from metadata");

                                // Use extracted info for decryption
                                string metadataComputerInfo = extractedMac + extractedHost;
                                Log($"Using computer info from metadata: {metadataComputerInfo}");

                                char keyChar = extractedKeyChar.Length > 0 ? extractedKeyChar[0] : 'T';

                                // Generate key and IV
                                byte[] metadataKey = GetKey(32, metadataComputerInfo, keyChar);
                                byte[] metadataIv = GetKey(16, metadataComputerInfo, keyChar);
                                Log($"Generated key length: {metadataKey.Length}, IV length: {metadataIv.Length}");

                                // Log key/IV hexadecimal representation
                                Log($"Key (hex): {BitConverter.ToString(metadataKey).Replace("-", "")}");
                                Log($"IV (hex): {BitConverter.ToString(metadataIv).Replace("-", "")}");

                                // Extract just the encrypted part
                                byte[] actualEncryptedData = new byte[encryptedData.Length - (4 + metadataLen)];
                                Array.Copy(encryptedData, 4 + metadataLen, actualEncryptedData, 0, actualEncryptedData.Length);

                                // Decrypt with metadata-extracted info
                                byte[] metadataDecryptedData = DecryptAESData(actualEncryptedData, metadataKey, metadataIv);
                                if (metadataDecryptedData != null && metadataDecryptedData.Length > 0)
                                {
                                    Log("Successfully decrypted using metadata info");

                                    // Convert to string and parse JSON
                                    string metadataJsonConfig = Encoding.UTF8.GetString(metadataDecryptedData);
                                    Log($"Decrypted JSON length: {metadataJsonConfig.Length} characters");
                                    Log($"First 100 chars: {(metadataJsonConfig.Length > 100 ? metadataJsonConfig.Substring(0, 100) + "..." : metadataJsonConfig)}");

                                    // Parse JSON
                                    try
                                    {
                                        JObject metadataConfig = JObject.Parse(metadataJsonConfig);
                                        Log("Successfully parsed JSON from metadata-based decryption");
                                        return metadataConfig;
                                    }
                                    catch (Exception ex)
                                    {
                                        Log($"Failed to parse JSON from metadata-based decryption: {ex.Message}");
                                    }
                                }
                                else
                                {
                                    Log("Failed to decrypt using metadata info, falling back to standard method");
                                }
                            }
                        }
                    }
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
                    Log($"Generated key length: {standardKey.Length}, IV length: {standardIv.Length}");

                    // Add hex representation logging
                    Log($"Key (hex): {BitConverter.ToString(standardKey).Replace("-", "")}");
                    Log($"IV (hex): {BitConverter.ToString(standardIv).Replace("-", "")}");
                }
                catch (Exception ex)
                {
                    Log($"Error generating encryption keys: {ex.Message}");
                    return null;
                }

                // Try with different file processing approaches

                // Approach 1: Decrypt the entire file
                byte[] standardDecryptedData = null;
                try
                {
                    Log("Attempting standard decryption of entire file...");
                    standardDecryptedData = DecryptAESData(encryptedData, standardKey, standardIv);
                    if (standardDecryptedData != null && standardDecryptedData.Length > 0)
                    {
                        Log($"Successfully decrypted entire file, got {standardDecryptedData.Length} bytes");
                    }
                    else
                    {
                        Log("Standard decryption failed, trying other approaches");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error in standard decryption: {ex.Message}");
                    standardDecryptedData = null;
                }

                // If standard decryption failed, try approach 2: Skip first 4 bytes (metadata length)
                byte[] partialDecryptedData = null;
                if (standardDecryptedData == null && encryptedData.Length > 4)
                {
                    try
                    {
                        Log("Attempting to decrypt without metadata length (skip first 4 bytes)...");
                        byte[] dataWithoutHeader = new byte[encryptedData.Length - 4];
                        Array.Copy(encryptedData, 4, dataWithoutHeader, 0, dataWithoutHeader.Length);

                        partialDecryptedData = DecryptAESData(dataWithoutHeader, standardKey, standardIv);
                        if (partialDecryptedData != null && partialDecryptedData.Length > 0)
                        {
                            Log($"Successfully decrypted without metadata length, got {partialDecryptedData.Length} bytes");
                        }
                        else
                        {
                            Log("Decryption without metadata length failed");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Error in second approach: {ex.Message}");
                    }
                }

                // Approach 3: Try with fixed key
                byte[] fixedKeyDecryptedData = null;
                if (standardDecryptedData == null && partialDecryptedData == null)
                {
                    try
                    {
                        Log("Attempting with fixed key 'HARDCODED_KEY'...");
                        string fixedComputerInfo = "902E16B9AC1PC-004"; // Hardcoded common value

                        byte[] fixedKey = GetKey(32, fixedComputerInfo, 'T');
                        byte[] fixedIv = GetKey(16, fixedComputerInfo, 'T');

                        Log($"Fixed key (hex): {BitConverter.ToString(fixedKey).Replace("-", "")}");
                        Log($"Fixed IV (hex): {BitConverter.ToString(fixedIv).Replace("-", "")}");

                        fixedKeyDecryptedData = DecryptAESData(encryptedData, fixedKey, fixedIv);
                        if (fixedKeyDecryptedData != null && fixedKeyDecryptedData.Length > 0)
                        {
                            Log($"Successfully decrypted with fixed key, got {fixedKeyDecryptedData.Length} bytes");
                        }
                        else
                        {
                            Log("Decryption with fixed key failed");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Error in fixed key approach: {ex.Message}");
                    }
                }

                // Choose which decrypted data to use, based on which method succeeded
                byte[] finalDecryptedData = null;
                if (standardDecryptedData != null && standardDecryptedData.Length > 0)
                {
                    finalDecryptedData = standardDecryptedData;
                    Log("Using standard decryption results");
                }
                else if (partialDecryptedData != null && partialDecryptedData.Length > 0)
                {
                    finalDecryptedData = partialDecryptedData;
                    Log("Using partial decryption results (skipped metadata)");
                }
                else if (fixedKeyDecryptedData != null && fixedKeyDecryptedData.Length > 0)
                {
                    finalDecryptedData = fixedKeyDecryptedData;
                    Log("Using fixed key decryption results");
                }

                // If all approaches failed
                if (finalDecryptedData == null || finalDecryptedData.Length == 0)
                {
                    Log("All decryption approaches failed");
                    return null;
                }

                // Convert to string
                string standardJsonConfig;
                try
                {
                    standardJsonConfig = Encoding.UTF8.GetString(finalDecryptedData);
                    Log($"Config JSON length: {standardJsonConfig.Length} characters");
                    Log($"First 100 chars of config: {(standardJsonConfig.Length > 100 ? standardJsonConfig.Substring(0, 100) + "..." : standardJsonConfig)}");
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
                    // Try alternative approach if the first attempt fails
                    Log($"Error parsing JSON with default settings: {ex.Message}");
                    try
                    {
                        // Try explicit UTF-8 encoding through a TextReader
                        using (var reader = new StringReader(standardJsonConfig))
                        {
                            var serializer = new JsonSerializer();
                            finalConfig = (JObject)serializer.Deserialize(reader, typeof(JObject));
                            Log("Successfully parsed JSON config using alternative method");
                        }
                    }
                    catch (Exception innerEx)
                    {
                        Log($"All JSON parsing attempts failed: {innerEx.Message}");
                        return null;
                    }
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

                // Log the pack_empresa value
                var bitrix24 = finalConfig["Bitrix24"]?.Value<JObject>();
                if (bitrix24 != null)
                {
                    bool packEmpresa = bitrix24["pack_empresa"]?.Value<bool>() ?? false;
                    Log($"Decrypted config - pack_empresa: {packEmpresa}");
                }
                else
                {
                    Log("Bitrix24 section not found in config");
                }

                Log($"Successfully decrypted config with CodigoCliente: {finalConfig["CodigoCliente"]?.Value<string>()}");
                return finalConfig;
            }
            catch (Exception ex)
            {
                Log($"Unexpected error loading encrypted config: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
                return null;
            }
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

            // Now validate it's not empty (should always pass now)
            if (string.IsNullOrEmpty(config.BitrixClientCode))
            {
                Log("Missing Bitrix client code configuration");
                return false;
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
                Log("Missing DB_PASSWD configuration");
                return false;
            }

            // Check Bitrix configuration
            if (string.IsNullOrEmpty(config.Bitrix.URL))
            {
                Log("Missing BITRIX_URL configuration");
                return false;
            }

            return true;
        }

        #region Decryption Utilities

        private static string GetComputerInfo()
        {
            string macAddress = string.Empty;
            Log("Starting MAC address detection...");

            try
            {
                // Get the MAC address
                System.Net.NetworkInformation.NetworkInterface[] nics =
                    System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();

                Log($"Found {nics.Length} network interfaces");

                // Log all interfaces for diagnostics
                foreach (var adapter in nics)
                {
                    PhysicalAddress pa = adapter.GetPhysicalAddress();
                    string macStr = string.Join("", pa.GetAddressBytes().Select(b => b.ToString("X2")));

                    Log($"Interface: {adapter.Name}, Type: {adapter.NetworkInterfaceType}, " +
                        $"Status: {adapter.OperationalStatus}, MAC: {macStr}, " +
                        $"Description: {adapter.Description}");
                }

                foreach (var adapter in nics)
                {
                    string adapterName = adapter.Name.ToLower();

                    if (adapter.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                        !adapter.Description.ToLower().Contains("virtual") &&
                        !adapter.Description.ToLower().Contains("vpn") &&
                        !adapterName.Contains("vethernet") &&
                        !adapterName.Contains("loopback") &&
                        (adapterName.Contains("ethernet") && !adapterName.Contains("vethernet") ||
                        adapterName.Contains("wi-fi") ||
                        adapterName.Contains("wlan")))
                    {
                        PhysicalAddress address = adapter.GetPhysicalAddress();
                        if (address != null && address.GetAddressBytes().Length > 0)
                        {
                            macAddress = BitConverter.ToString(address.GetAddressBytes()).Replace("-", "");
                            Log($"Selected interface: {adapter.Name} with MAC: {macAddress}");
                            break;
                        }
                    }
                }

                // If still no MAC found, try with less strict criteria
                if (string.IsNullOrEmpty(macAddress))
                {
                    Log("No MAC found with strict criteria, trying less strict criteria...");
                    foreach (var adapter in nics)
                    {
                        if (adapter.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                            adapter.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                        {
                            PhysicalAddress address = adapter.GetPhysicalAddress();
                            if (address != null && address.GetAddressBytes().Length > 0)
                            {
                                macAddress = BitConverter.ToString(address.GetAddressBytes()).Replace("-", "");
                                Log($"Selected fallback interface: {adapter.Name} with MAC: {macAddress}");
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error getting MAC address: {ex.Message}");
            }

            // If still no MAC found, use hardcoded value
            if (string.IsNullOrEmpty(macAddress))
            {
                macAddress = "902E16B9AC1";
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
                    using (MemoryStream resultStream = new MemoryStream())
                    {
                        try
                        {
                            byte[] buffer = new byte[1024];
                            int read;
                            while ((read = csDecrypt.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                resultStream.Write(buffer, 0, read);
                            }

                            byte[] decryptedData = resultStream.ToArray();
                            Log($"Decryption successful, got {decryptedData.Length} bytes");

                            // Check for UTF-16 BOM and convert to UTF-8 if needed
                            if (decryptedData.Length >= 2 &&
                                ((decryptedData[0] == 0xFF && decryptedData[1] == 0xFE) ||
                                 (decryptedData[0] == 0xFE && decryptedData[1] == 0xFF)))
                            {
                                // Detect if it's UTF-16 (has BOM)
                                string utf16String = Encoding.Unicode.GetString(decryptedData);
                                Log("Detected UTF-16 encoding, converting to UTF-8");
                                // Convert back to UTF-8
                                return Encoding.UTF8.GetBytes(utf16String);
                            }

                            return decryptedData;
                        }
                        catch (CryptographicException ce)
                        {
                            Log($"Cryptographic exception during decryption: {ce.Message}");
                            return null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Decryption error: {ex.Message}");
                return null;
            }
        }

        #endregion
    }
}