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

namespace ConnectorSageBitrix.Config
{
    public static class ConfigManager
    {
        private const string EncryptedConfigPath = @"C:\ProgramData\Btic\ConfigConnectorBitrix\config";

        public static AppConfig Load()
        {
            try
            {
                // Initialize a new config with default values
                var config = new AppConfig();

                Console.WriteLine("Default configuration: Synchronization disabled");

                // First try to load encrypted config
                var encryptedConfig = LoadEncryptedConfig();

                if (encryptedConfig != null)
                {
                    Console.WriteLine("Using encrypted configuration file");

                    // Set client code from encrypted config
                    config.ClientCode = encryptedConfig.GetValue<string>("CodigoCliente");

                    // Always set BitrixClientCode to the root CodigoCliente for now
                    config.BitrixClientCode = config.ClientCode;

                    // Set DB configuration from encrypted config
                    var dbSection = encryptedConfig.GetValue<JObject>("DB");
                    if (dbSection != null)
                    {
                        config.DB.Host = dbSection.GetValue<string>("DB_Host_Sage");
                        config.DB.Port = dbSection.GetValue<string>("DB_Port");
                        config.DB.Database = dbSection.GetValue<string>("DB_Database");
                        config.DB.User = dbSection.GetValue<string>("DB_Username");
                        config.DB.Password = dbSection.GetValue<string>("DB_Password");
                        config.DB.LicenseID = dbSection.GetValue<string>("IdLlicencia");
                    }

                    // Set Bitrix URL from encrypted config
                    var bitrixSection = encryptedConfig.GetValue<JObject>("Bitrix24");
                    if (bitrixSection != null)
                    {
                        string apiTenant = bitrixSection.GetValue<string>("API_Tenant");
                        if (!string.IsNullOrEmpty(apiTenant))
                        {
                            config.Bitrix.URL = apiTenant;
                        }

                        // Set PackEmpresa flag from encrypted config
                        config.PackEmpresa = bitrixSection.GetValue<bool>("pack_empresa");
                        Console.WriteLine($"Set PackEmpresa to {config.PackEmpresa} from encrypted config");
                    }
                }
                else
                {
                    Console.WriteLine("Failed to load encrypted config - falling back to App.config");

                    // Load from App.config as fallback
                    config.DB.Host = ConfigurationManager.AppSettings["DB_HOST"];
                    config.DB.Port = ConfigurationManager.AppSettings["DB_PORT"];
                    config.DB.Database = ConfigurationManager.AppSettings["DB_DATABASE"];
                    config.DB.User = ConfigurationManager.AppSettings["DB_USERNAME"];
                    config.DB.Password = ConfigurationManager.AppSettings["DB_PASSWD"];
                    config.DB.LicenseID = ConfigurationManager.AppSettings["LICENSE_ID"];
                    config.Bitrix.URL = ConfigurationManager.AppSettings["BITRIX_URL"];
                    config.BitrixClientCode = ConfigurationManager.AppSettings["BITRIX_CLIENT_CODE"];

                    if (string.IsNullOrEmpty(config.BitrixClientCode))
                    {
                        config.BitrixClientCode = config.ClientCode;
                    }

                    // Check for explicit PACK_EMPRESA in App.config
                    string packEmpresa = ConfigurationManager.AppSettings["PACK_EMPRESA"];
                    if (!string.IsNullOrEmpty(packEmpresa) && packEmpresa.ToLower() == "true")
                    {
                        config.PackEmpresa = true;
                        Console.WriteLine("Set PackEmpresa to true from App.config");
                    }
                    else
                    {
                        Console.WriteLine($"PACK_EMPRESA in App.config is '{packEmpresa}' (not 'true'), keeping as false");
                    }
                }

                // Validate configuration
                if (!ValidateConfig(config))
                {
                    return null;
                }

                Console.WriteLine($"Final configuration: PackEmpresa = {config.PackEmpresa}");
                return config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading configuration: {ex.Message}");
                return null;
            }
        }

        private static JObject LoadEncryptedConfig()
        {
            try
            {
                Console.WriteLine($"Attempting to load encrypted config from: {EncryptedConfigPath}");

                // Check if the file exists
                if (!File.Exists(EncryptedConfigPath))
                {
                    Console.WriteLine($"Encrypted config file not found at {EncryptedConfigPath}");
                    return null;
                }

                // Read encrypted file
                byte[] encryptedData = File.ReadAllBytes(EncryptedConfigPath);
                Console.WriteLine($"Read {encryptedData.Length} bytes from encrypted config file");

                // Get computer info for key generation
                string computerInfo = GetComputerInfo();
                Console.WriteLine($"Using computer info for decryption: {computerInfo}");

                // Generate key and IV
                string charKey = "T"; // Use 'T' for config files
                byte[] key = GetKey(32, computerInfo, charKey);
                byte[] iv = GetKey(16, computerInfo, charKey);

                // Decrypt the data
                byte[] decryptedData = DecryptAESData(encryptedData, key, iv);
                if (decryptedData == null || decryptedData.Length == 0)
                {
                    Console.WriteLine("Decryption resulted in empty data");
                    return null;
                }

                // Convert to string
                string jsonConfig = Encoding.UTF8.GetString(decryptedData);

                // Parse JSON
                JObject config = JObject.Parse(jsonConfig);

                // Validate basic config
                if (config.GetValue<string>("CodigoCliente") == null)
                {
                    Console.WriteLine("Decrypted config is missing CodigoCliente");
                    return null;
                }

                // Log the pack_empresa value
                var bitrix24 = config.GetValue<JObject>("Bitrix24");
                if (bitrix24 != null)
                {
                    bool packEmpresa = bitrix24.GetValue<bool>("pack_empresa");
                    Console.WriteLine($"Decrypted config - pack_empresa: {packEmpresa}");
                }

                Console.WriteLine($"Successfully decrypted config with CodigoCliente: {config.GetValue<string>("CodigoCliente")}");
                return config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading encrypted config: {ex.Message}");
                return null;
            }
        }

        private static bool ValidateConfig(AppConfig config)
        {
            // Check client code
            if (string.IsNullOrEmpty(config.ClientCode))
            {
                Console.WriteLine("Missing CLIENT_CODE configuration");
                return false;
            }

            if (string.IsNullOrEmpty(config.BitrixClientCode))
            {
                config.BitrixClientCode = config.ClientCode;
            }

            // Now validate it's not empty (should always pass now)
            if (string.IsNullOrEmpty(config.BitrixClientCode))
            {
                Console.WriteLine("Missing Bitrix client code configuration");
                return false;
            }

            // Check license ID
            if (string.IsNullOrEmpty(config.DB.LicenseID))
            {
                Console.WriteLine("Missing LICENSE_ID configuration");
                return false;
            }

            // Check database configuration
            if (string.IsNullOrEmpty(config.DB.Host))
            {
                Console.WriteLine("Missing DB_HOST configuration");
                return false;
            }

            if (string.IsNullOrEmpty(config.DB.Database))
            {
                Console.WriteLine("Missing DB_DATABASE configuration");
                return false;
            }

            if (string.IsNullOrEmpty(config.DB.User))
            {
                Console.WriteLine("Missing DB_USERNAME configuration");
                return false;
            }

            if (string.IsNullOrEmpty(config.DB.Password))
            {
                Console.WriteLine("Missing DB_PASSWD configuration");
                return false;
            }

            // Check Bitrix configuration
            if (string.IsNullOrEmpty(config.Bitrix.URL))
            {
                Console.WriteLine("Missing BITRIX_URL configuration");
                return false;
            }

            return true;
        }

        #region Decryption Utilities

        private static string GetComputerInfo()
        {
            string macAddress = string.Empty;

            try
            {
                // Get the MAC address
                System.Net.NetworkInformation.NetworkInterface[] nics =
                    System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();

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
                            break;
                        }
                    }
                }

                // If still no MAC found, try with less strict criteria
                if (string.IsNullOrEmpty(macAddress))
                {
                    foreach (var adapter in nics)
                    {
                        if (adapter.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                            adapter.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                        {
                            PhysicalAddress address = adapter.GetPhysicalAddress();
                            if (address != null && address.GetAddressBytes().Length > 0)
                            {
                                macAddress = BitConverter.ToString(address.GetAddressBytes()).Replace("-", "");
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting MAC address: {ex.Message}");
            }

            // If still no MAC found, use hardcoded value
            if (string.IsNullOrEmpty(macAddress))
            {
                macAddress = "902E16B9AC1";
            }

            // Get hostname
            string hostname = "unknown";
            try
            {
                hostname = Environment.MachineName;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting hostname: {ex.Message}");
            }

            // Combine MAC and hostname
            return macAddress + hostname;
        }

        private static byte[] GetKey(int keyLength, string computerInfo, string padChar)
        {
            // If computer info is longer than needed, truncate
            if (computerInfo.Length > keyLength)
            {
                computerInfo = computerInfo.Substring(0, keyLength);
            }
            else
            {
                // Pad with the specified character
                while (computerInfo.Length < keyLength)
                {
                    computerInfo += padChar;
                }
            }

            return Encoding.UTF8.GetBytes(computerInfo);
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

                    // Create a decryptor
                    using (ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV))
                    {
                        // Create the streams used for decryption
                        using (MemoryStream msDecrypt = new MemoryStream(encryptedData))
                        {
                            using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                            {
                                using (MemoryStream resultStream = new MemoryStream())
                                {
                                    byte[] buffer = new byte[1024];
                                    int read;
                                    while ((read = csDecrypt.Read(buffer, 0, buffer.Length)) > 0)
                                    {
                                        resultStream.Write(buffer, 0, read);
                                    }
                                    return resultStream.ToArray();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Decryption error: {ex.Message}");
                return null;
            }
        }

        #endregion
    }
}