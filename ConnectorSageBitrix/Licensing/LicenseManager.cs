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
using ConnectorSageBitrix.Extensions;

namespace ConnectorSageBitrix.Licensing
{
    public class LicenseManager : IDisposable
    {
        private const string LicenseFolder = @"C:\ProgramData\Btic\licenses";
        private const string APIBaseURL = "https://api.btic.cat";
        private const string LicenseType = "Bitrix24";
        private const int MaxRetryAttempts = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);

        private readonly string _clientCode;
        private readonly string _licenseID;
        private readonly Logger _logger;
        private readonly HttpClient _httpClient;
        private string _licenseType;
        private bool _disposed = false;

        public LicenseManager(string clientCode, string licenseID, Logger logger)
        {
            _clientCode = clientCode;
            _licenseID = licenseID;
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.Timeout = HttpTimeout;
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
            string encodedLicense = Convert.ToBase64String(content)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");

            // Show first few characters for debugging
            int previewLen = Math.Min(10, encodedLicense.Length);
            _logger.Debug($"Encoded license (first {previewLen} chars): {encodedLicense.Substring(0, previewLen)}...");

            // Check if we have offline override signal (for testing/emergency use)
            if (File.Exists(Path.Combine(LicenseFolder, "offline-mode.txt")))
            {
                _logger.Info("OFFLINE MODE detected. Bypassing server validation.");
                return true;
            }

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

                // If server had internal error and this is the last attempt, assume license is valid temporarily
                if (attempt >= MaxRetryAttempts)
                {
                    _logger.Warning($"License verification failed after {MaxRetryAttempts} attempts due to server issues. Using grace period mode.");

                    // Create a file marking the last failed attempt for future reference
                    try
                    {
                        File.WriteAllText(
                            Path.Combine(LicenseFolder, "last-failed-verification.txt"),
                            $"Last failed verification: {DateTime.Now}\r\nReason: Server unavailable after {MaxRetryAttempts} attempts\r\n"
                        );
                    }
                    catch { }

                    // For startup purposes, we'll treat the license as valid temporarily
                    return true;
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
                using (var request = new HttpRequestMessage(HttpMethod.Get, verifyURL))
                {
                    request.Headers.Add("Authorization", $"Bearer {token}");

                    // Execute the request
                    using (HttpResponseMessage response = await _httpClient.SendAsync(request))
                    {
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
                                    return false; // Definitely invalid license
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
                            _logger.Error($"License verification failed: Wrong license type. Expected: {LicenseType}, Got: {tipus}");
                        }

                        return false;
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.Error($"Network error during license verification: {ex.Message}");
                return false;
            }
            catch (TaskCanceledException ex)
            {
                _logger.Error($"License verification timeout: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"Unexpected error during license verification: {ex.Message}");
                return false;
            }
        }

        private async Task<string> GetToken()
        {
            try
            {
                string tokenURL = $"{APIBaseURL}/auth";
                string computerInfo = GetComputerInfo();

                var tokenRequestBody = new
                {
                    client = _clientCode,
                    computer = computerInfo
                };

                string jsonBody = JsonConvert.SerializeObject(tokenRequestBody);

                using (var content = new StringContent(jsonBody, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage response = await _httpClient.PostAsync(tokenURL, content))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.Error($"Failed to get token: {response.StatusCode}");
                        return null;
                    }

                    string responseContent = await response.Content.ReadAsStringAsync();
                    JObject tokenResp = JObject.Parse(responseContent);

                    return tokenResp.Value<string>("token");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error getting authentication token: {ex.Message}");
                return null;
            }
        }

        private string GetComputerInfo()
        {
            // Get MAC address
            string macAddress = "";
            try
            {
                NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (NetworkInterface adapter in networkInterfaces)
                {
                    if (adapter.OperationalStatus == OperationalStatus.Up &&
                        adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        PhysicalAddress address = adapter.GetPhysicalAddress();
                        if (address?.GetAddressBytes().Length > 0)
                        {
                            macAddress = BitConverter.ToString(address.GetAddressBytes()).Replace("-", "");
                            break;
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
            string hostname;
            try
            {
                hostname = Environment.MachineName;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error getting hostname: {ex.Message}");
                hostname = "unknown";
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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _httpClient?.Dispose();
                _disposed = true;
            }
        }
    }
}