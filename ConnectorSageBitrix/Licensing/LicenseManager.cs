using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConnectorSageBitrix.Logging;
using System.Collections.Generic;

namespace ConnectorSageBitrix.Licensing
{
    public class LicenseManager
    {
        private const string LicenseFolder = @"C:\ProgramData\Btic\licenses";
        private const string APIBaseURL = "https://api.btic.cat";
        private const string LicenseType = "Bitrix24";
        private const int MaxRetryAttempts = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(30);

        private readonly string _clientCode;
        private readonly string _licenseID;
        private readonly Logger _logger;
        private readonly HttpClient _httpClient;
        private string _licenseType;

        public LicenseManager(string clientCode, string licenseID, Logger logger)
        {
            _clientCode = clientCode;
            _licenseID = licenseID;
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public bool IsValid()
        {
            _logger.Info($"Checking license validity for ID: {_licenseID}");

            // Validate inputs
            if (string.IsNullOrEmpty(_licenseID))
            {
                _logger.Error("License ID is empty");
                return false;
            }

            if (string.IsNullOrEmpty(_clientCode))
            {
                _logger.Error("Client code is empty");
                return false;
            }

            // Construct license file path
            string licenseFile = Path.Combine(LicenseFolder, $"{_licenseID}.txt");
            _logger.Info($"Looking for license file at: {licenseFile}");

            // Check if license file exists
            if (!File.Exists(licenseFile))
            {
                _logger.Error($"License file not found: {licenseFile}");
                return false;
            }

            // Read license file
            byte[] content;
            try
            {
                content = File.ReadAllBytes(licenseFile);
                if (content.Length == 0)
                {
                    _logger.Error("License file is empty");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to read license file: {ex.Message}");
                return false;
            }

            _logger.Info($"Read license file, size: {content.Length} bytes");

            // Convert content to URL-safe Base64
            string encodedLicense = Convert.ToBase64String(content);
            encodedLicense = encodedLicense.Replace("+", "-").Replace("/", "_").Replace("=", "");

            // Show first few characters for debugging
            int previewLen = Math.Min(10, encodedLicense.Length);
            _logger.Debug($"Encoded license (first {previewLen} chars): {encodedLicense.Substring(0, previewLen)}...");

            // Retry logic for license verification
            for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
            {
                _logger.Info($"License verification attempt {attempt} of {MaxRetryAttempts}");

                // Verify license with API
                bool isValid = VerifyLicenseWithAPI(encodedLicense).GetAwaiter().GetResult();
                if (isValid)
                {
                    _logger.Info($"License verification successful on attempt {attempt}");
                    return true;
                }

                // If this was the last attempt, we're done
                if (attempt >= MaxRetryAttempts)
                {
                    _logger.Error($"License verification failed after {MaxRetryAttempts} attempts");
                    return false;
                }

                // Log retry information and wait before next attempt
                _logger.Info($"License verification failed on attempt {attempt}. Retrying in {RetryDelay}...");
                Task.Delay(RetryDelay).Wait();
            }

            return false;
        }

        private async Task<bool> VerifyLicenseWithAPI(string encodedLicense)
        {
            try
            {
                // Get authentication token
                string token = await GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    _logger.Error("Failed to get authentication token");
                    return false;
                }

                _logger.Debug("Got authentication token for API verification");

                // Create request to license verification endpoint
                string verifyURL = $"{APIBaseURL}/llicencies/{encodedLicense}";
                _logger.Debug($"Sending license verification request to: {verifyURL}");

                // Create request
                var request = new HttpRequestMessage(HttpMethod.Get, verifyURL);
                request.Headers.Add("Authorization", $"Bearer {token}");

                // Execute the request
                HttpResponseMessage response = await _httpClient.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();

                // Check status code with more detailed logging
                if (!response.IsSuccessStatusCode)
                {
                    string errDetails = !string.IsNullOrEmpty(responseContent) ? $" Response: {responseContent}" : "";

                    // Log based on status code
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.Unauthorized:
                            _logger.Error($"License verification failed: Unauthorized (401). Invalid credentials.{errDetails}");
                            break;
                        case HttpStatusCode.Forbidden:
                            _logger.Error($"License verification failed: Forbidden (403). Access denied.{errDetails}");
                            break;
                        case HttpStatusCode.NotFound:
                            _logger.Error($"License verification failed: Not Found (404). License ID may be invalid.{errDetails}");
                            break;
                        case (HttpStatusCode)429: // TooManyRequests
                            _logger.Error($"License verification failed: Too Many Requests (429). Rate limit exceeded.{errDetails}");
                            break;
                        case HttpStatusCode.InternalServerError:
                        case HttpStatusCode.BadGateway:
                        case HttpStatusCode.ServiceUnavailable:
                            _logger.Error($"License verification failed: Server Error ({(int)response.StatusCode}). API service may be down.{errDetails}");
                            break;
                        default:
                            _logger.Error($"License verification failed: Unexpected status code {(int)response.StatusCode}.{errDetails}");
                            break;
                    }
                    return false;
                }

                // Parse response
                JObject licenseResp = JObject.Parse(responseContent);
                string status = licenseResp.Value<string>("status");
                string tipus = licenseResp.Value<string>("tipus");

                // Check license status and type - only validate against "Bitrix24"
                if (status == "OK" && tipus == LicenseType)
                {
                    // Store the license type
                    _licenseType = tipus;
                    _logger.Info($"License verified successfully, type: {_licenseType}");
                    return true;
                }

                // Log details about why verification failed (not OK or wrong type)
                if (status != "OK")
                {
                    _logger.Error($"License verification failed: Invalid status={status}");
                }
                else if (tipus != LicenseType)
                {
                    _logger.Error($"License verification failed: Wrong license type. Expected={LicenseType}, Got={tipus}");
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"License verification request failed: {ex.Message}");
                return false;
            }
        }

        private async Task<string> GetToken()
        {
            try
            {
                // Create password for token request
                string password = CreatePassword();
                if (string.IsNullOrEmpty(password))
                {
                    return null;
                }

                // Create form data for token request
                var formData = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("username", _clientCode),
                    new KeyValuePair<string, string>("password", password)
                });

                // Create request to token endpoint
                string tokenURL = $"{APIBaseURL}/token";
                _logger.Debug($"Requesting token from: {tokenURL}");

                // Execute the request
                HttpResponseMessage response = await _httpClient.PostAsync(tokenURL, formData);
                string responseContent = await response.Content.ReadAsStringAsync();

                // Check response status
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Error($"Token request returned status {(int)response.StatusCode}: {responseContent}");
                    return null;
                }

                // Parse response
                JObject tokenResp = JObject.Parse(responseContent);
                string accessToken = tokenResp.Value<string>("access_token");

                if (string.IsNullOrEmpty(accessToken))
                {
                    _logger.Error("Token response did not contain access_token");
                    return null;
                }

                return accessToken;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get token: {ex.Message}");
                return null;
            }
        }

        private string CreatePassword()
        {
            try
            {
                // Get computer info
                string computerInfo = GetComputerInfo();
                _logger.Debug($"Using computer info for password: {computerInfo}");

                // Generate encryption key and IV using 'X' as pad character
                string charKey = "X";
                byte[] key = GetKey(32, computerInfo, charKey);
                byte[] iv = GetKey(16, computerInfo, charKey);

                // Debug logging
                _logger.Debug($"Key (hex): {BitConverter.ToString(key).Replace("-", "")}");
                _logger.Debug($"IV (hex): {BitConverter.ToString(iv).Replace("-", "")}");
                _logger.Debug($"Input to encryption: {_clientCode}");

                // Encrypt client code
                byte[] encrypted = EncryptAES(Encoding.UTF8.GetBytes(_clientCode), key, iv);
                if (encrypted == null)
                {
                    _logger.Error("Failed to encrypt password");
                    return null;
                }

                // Log raw encrypted data
                _logger.Debug($"Encrypted (hex): {BitConverter.ToString(encrypted).Replace("-", "")}");

                // Return Base64 encoded encrypted data
                return Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error creating password: {ex.Message}");
                return null;
            }
        }

        private byte[] EncryptAES(byte[] data, byte[] key, byte[] iv)
        {
            try
            {
                using (Aes aesAlg = Aes.Create())
                {
                    aesAlg.Key = key;
                    aesAlg.IV = iv;
                    aesAlg.Mode = CipherMode.CBC;
                    aesAlg.Padding = PaddingMode.PKCS7;

                    // Create encryptor
                    ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                    // Create the streams used for encryption
                    using (MemoryStream msEncrypt = new MemoryStream())
                    {
                        using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                        {
                            csEncrypt.Write(data, 0, data.Length);
                            csEncrypt.FlushFinalBlock();
                            return msEncrypt.ToArray();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"AES encryption error: {ex.Message}");
                return null;
            }
        }

        private string GetComputerInfo()
        {
            string macAddress = string.Empty;

            try
            {
                // Get all network interfaces
                NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();

                foreach (NetworkInterface adapter in nics)
                {
                    string ifaceName = adapter.Name.ToLower();

                    if (adapter.OperationalStatus == OperationalStatus.Up &&
                        !adapter.Description.ToLower().Contains("virtual") &&
                        !adapter.Description.ToLower().Contains("vpn") &&
                        !ifaceName.Contains("vethernet") &&
                        !ifaceName.Contains("loopback") &&
                        (ifaceName.Contains("ethernet") && !ifaceName.Contains("vethernet") ||
                        ifaceName.Contains("wi-fi") ||
                        ifaceName.Contains("wlan")))
                    {
                        PhysicalAddress address = adapter.GetPhysicalAddress();
                        if (address != null && address.GetAddressBytes().Length > 0)
                        {
                            macAddress = BitConverter.ToString(address.GetAddressBytes()).Replace("-", "");
                            break;
                        }
                    }
                }

                // Fallback with less strict criteria
                if (string.IsNullOrEmpty(macAddress))
                {
                    foreach (NetworkInterface adapter in nics)
                    {
                        if (adapter.OperationalStatus == OperationalStatus.Up &&
                            adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
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
                _logger.Error($"Error getting MAC address: {ex.Message}");
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
                _logger.Error($"Error getting hostname: {ex.Message}");
            }

            // Combine MAC and hostname
            return macAddress + hostname;
        }

        private byte[] GetKey(int keyLength, string computerInfo, string padChar)
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
    }
}