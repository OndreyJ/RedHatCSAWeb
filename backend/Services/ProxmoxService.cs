using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RHCSAExam.Services
{
    public class ProxmoxService
    {
        private readonly HttpClient _httpClient;
        private readonly string _proxmoxHost;
        private readonly string _apiToken;
        private readonly string _node;
        private readonly ILogger<ProxmoxService> _logger;

        public ProxmoxService(IConfiguration configuration, ILogger<ProxmoxService> logger)
        {
            _logger = logger;
            _proxmoxHost = configuration["Proxmox:Host"];
            _apiToken = configuration["Proxmox:ApiToken"];
            _node = configuration["Proxmox:Node"];

            _logger.LogInformation("=== Initializing ProxmoxService ===");
            _logger.LogInformation("Host: {Host}", _proxmoxHost);
            _logger.LogInformation("Node: {Node}", _node);
            _logger.LogInformation("API Token present: {HasToken}", !string.IsNullOrEmpty(_apiToken));

            if (string.IsNullOrEmpty(_proxmoxHost) || string.IsNullOrEmpty(_apiToken) || string.IsNullOrEmpty(_node))
            {
                _logger.LogError("Missing required Proxmox configuration!");
                throw new InvalidOperationException("Proxmox configuration is incomplete");
            }

            // Accept self-signed certificates (for dev only!)
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "RHCSAExam/1.0");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("PVEAPIToken", _apiToken);

            _logger.LogInformation("ProxmoxService initialized successfully");
        }

        // Clone VM from template
        public async Task<int> CloneVmFromTemplate(int templateId, string newVmName)
        {
            _logger.LogInformation("=== Cloning VM from template ===");
            _logger.LogInformation("Template ID: {TemplateId}, New VM Name: {Name}", templateId, newVmName);

            var newVmId = await GetNextVmId();
            _logger.LogInformation("Next available VM ID: {VmId}", newVmId);

            var url = $"{_proxmoxHost}/api2/json/nodes/{_node}/qemu/{templateId}/clone";
            _logger.LogDebug("Clone URL: {Url}", url);

            var payload = new
            {
                newid = newVmId,
                name = newVmName,
                full = 0, // Full clone
                target = _node
            };

            var payloadJson = JsonSerializer.Serialize(payload);
            _logger.LogDebug("Clone payload: {Payload}", payloadJson);

            var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(url, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                _logger.LogDebug("Clone response status: {StatusCode}", response.StatusCode);
                _logger.LogDebug("Clone response body: {Body}", responseBody);

                response.EnsureSuccessStatusCode();
                _logger.LogInformation("VM cloned successfully with ID: {VmId}", newVmId);

                return newVmId;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error cloning VM from template {TemplateId}: {Message}", templateId, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clone VM from template {TemplateId}: {Message}", templateId, ex.Message);
                throw;
            }
        }

        // Start VM
        public async Task<bool> StartVm(int vmId)
        {
            _logger.LogInformation("Starting VM {VmId}", vmId);

            var url = $"{_proxmoxHost}/api2/json/nodes/{_node}/qemu/{vmId}/status/start";
            _logger.LogDebug("Start VM URL: {Url}", url);

            try
            {
                var response = await _httpClient.PostAsync(url, null);
                var responseBody = await response.Content.ReadAsStringAsync();

                _logger.LogDebug("Start VM response: {StatusCode} - {Body}", response.StatusCode, responseBody);

                var success = response.IsSuccessStatusCode;
                _logger.LogInformation("Start VM {VmId} result: {Success}", vmId, success);
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start VM {VmId}", vmId);
                return false;
            }
        }

        // Stop VM
        public async Task<bool> StopVm(int vmId)
        {
            _logger.LogInformation("Stopping VM {VmId}", vmId);

            var url = $"{_proxmoxHost}/api2/json/nodes/{_node}/qemu/{vmId}/status/stop";
            _logger.LogDebug("Stop VM URL: {Url}", url);

            try
            {
                var response = await _httpClient.PostAsync(url, null);
                var responseBody = await response.Content.ReadAsStringAsync();

                _logger.LogDebug("Stop VM response: {StatusCode} - {Body}", response.StatusCode, responseBody);

                var success = response.IsSuccessStatusCode;
                _logger.LogInformation("Stop VM {VmId} result: {Success}", vmId, success);
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop VM {VmId}", vmId);
                return false;
            }
        }

        // Get VM Status
        public async Task<VmStatus?> GetVmStatus(int vmId)
        {
            _logger.LogDebug("Getting status for VM {VmId}", vmId);

            var url = $"{_proxmoxHost}/api2/json/nodes/{_node}/qemu/{vmId}/status/current";

            try
            {
                var response = await _httpClient.GetAsync(url);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to get VM {VmId} status: {StatusCode} - {Body}", vmId, response.StatusCode, responseBody);
                    return null;
                }

                _logger.LogDebug("VM status response: {Body}", responseBody);

                // Parse JSON manually to handle missing fields
                using var doc = JsonDocument.Parse(responseBody);
                var data = doc.RootElement.GetProperty("data");

                var status = data.GetProperty("status").GetString() ?? "unknown";
                var name = data.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : $"VM-{vmId}";
                var uptime = data.TryGetProperty("uptime", out var uptimeElement) ? uptimeElement.GetInt32() : 0;

                _logger.LogDebug("Parsed VM {VmId} - Status: {Status}, Name: {Name}, Uptime: {Uptime}",
                    vmId, status, name, uptime);

                return new VmStatus
                {
                    VmId = vmId,
                    Status = status,
                    Name = name ?? $"VM-{vmId}",
                    Uptime = uptime
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting VM {VmId} status: {Message}", vmId, ex.Message);
                return null;
            }
        }

        // Delete VM
        public async Task<bool> DeleteVm(int vmId)
        {
            _logger.LogInformation("Deleting VM {VmId}", vmId);

            var url = $"{_proxmoxHost}/api2/json/nodes/{_node}/qemu/{vmId}";
            _logger.LogDebug("Delete VM URL: {Url}", url);

            try
            {
                var response = await _httpClient.DeleteAsync(url);
                var responseBody = await response.Content.ReadAsStringAsync();

                _logger.LogDebug("Delete VM response: {StatusCode} - {Body}", response.StatusCode, responseBody);

                var success = response.IsSuccessStatusCode;
                _logger.LogInformation("Delete VM {VmId} result: {Success}", vmId, success);
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete VM {VmId}", vmId);
                return false;
            }
        }

        // Get VNC WebSocket ticket for terminal access
        public async Task<VncTicket> GetVncTicket(int vmId)
        {
            _logger.LogInformation("Getting VNC ticket for VM {VmId}", vmId);

            var url = $"{_proxmoxHost}/api2/json/nodes/{_node}/qemu/{vmId}/vncproxy";

            var payload = new { websocket = 1 };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(url, content);
                var body = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("VNC ticket response status: {Status}", response.StatusCode);
                _logger.LogInformation("VNC ticket response body: {Body}", body);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to get VNC ticket. Status: {Status}, Body: {Body}", response.StatusCode, body);
                    throw new HttpRequestException($"Proxmox API returned {response.StatusCode}: {body}");
                }

                if (string.IsNullOrWhiteSpace(body))
                {
                    _logger.LogError("Empty response body from Proxmox VNC ticket endpoint");
                    throw new InvalidOperationException("Empty response from Proxmox API");
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var result = JsonSerializer.Deserialize<ProxmoxApiResponse<VncTicketData>>(body, options);

                if (result == null || result.Data == null)
                {
                    _logger.LogError("Failed to deserialize VNC ticket response. Body: {Body}", body);
                    throw new InvalidOperationException("Invalid response format from Proxmox API");
                }

                // Use the Port property which handles the conversion
                var port = result.Data.Port;
                _logger.LogInformation("VNC ticket obtained successfully - Port: {Port}", port);

                return new VncTicket
                {
                    Ticket = result.Data.Ticket ?? throw new InvalidOperationException("Ticket is null"),
                    Port = port,
                    Upid = result.Data.Upid ?? ""
                };
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON parsing error for VNC ticket. Response may not be valid JSON.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to obtain VNC ticket for VM {VmId}", vmId);
                throw;
            }
        }

        // Build complete noVNC console URL - NEW METHOD
        public string BuildNoVncConsoleUrl(int vmId, VncTicket ticket)
        {
            _logger.LogInformation("Building noVNC console URL for VM {VmId}", vmId);

            // Encode the ticket for URL safety
            var encodedTicket = Uri.EscapeDataString(ticket.Ticket);

            // Build the complete noVNC URL that can be embedded in an iframe
            // This URL points to Proxmox's built-in noVNC viewer
            var consoleUrl = $"{_proxmoxHost}/novnc/vnc.html?console=kvm&novnc=1&vmid={vmId}&node={_node}&port={ticket.Port}&ticket={encodedTicket}";

            _logger.LogInformation("Generated console URL: {Url}", consoleUrl);

            return consoleUrl;
        }

        // Helper to get next available VM ID
        private async Task<int> GetNextVmId()
        {
            _logger.LogDebug("=== Getting next available VM ID ===");

            var url = $"{_proxmoxHost}/api2/json/cluster/nextid";
            _logger.LogDebug("NextID URL: {Url}", url);

            try
            {
                var response = await _httpClient.GetAsync(url);
                var responseBody = await response.Content.ReadAsStringAsync();

                _logger.LogDebug("NextID response status: {StatusCode}", response.StatusCode);
                _logger.LogDebug("NextID response body: {Body}", responseBody);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to get next VM ID. Status: {StatusCode}, Body: {Body}",
                        response.StatusCode, responseBody);
                    throw new HttpRequestException($"Proxmox API returned {response.StatusCode}: {responseBody}");
                }

                if (string.IsNullOrWhiteSpace(responseBody))
                {
                    _logger.LogError("Empty response from Proxmox API");
                    throw new InvalidOperationException("Proxmox returned empty response");
                }

                // Proxmox sometimes returns the ID as a string instead of int
                var jsonDoc = JsonDocument.Parse(responseBody);
                var dataElement = jsonDoc.RootElement.GetProperty("data");

                int vmId;
                if (dataElement.ValueKind == JsonValueKind.String)
                {
                    vmId = int.Parse(dataElement.GetString()!);
                }
                else
                {
                    vmId = dataElement.GetInt32();
                }

                _logger.LogInformation("Next VM ID: {VmId}", vmId);
                return vmId;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON parsing error. Response may not be valid JSON.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get next VM ID: {Message}", ex.Message);
                throw;
            }
        }

        // List all VMs
        public async Task<List<VmInfo>> ListVms()
        {
            _logger.LogInformation("Listing all VMs on node {Node}", _node);

            var url = $"{_proxmoxHost}/api2/json/nodes/{_node}/qemu";

            try
            {
                var response = await _httpClient.GetAsync(url);
                var responseBody = await response.Content.ReadAsStringAsync();

                _logger.LogDebug("List VMs response: {StatusCode} - {Body}", response.StatusCode, responseBody);

                response.EnsureSuccessStatusCode();

                var result = JsonSerializer.Deserialize<ProxmoxApiResponse<List<VmInfoData>>>(responseBody);

                return result.Data.Select(vm => new VmInfo
                {
                    VmId = vm.VmId,
                    Name = vm.Name,
                    Status = vm.Status
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list VMs");
                throw;
            }
        }

        public async Task<string> GetVncWebsocketUrl(int vmId)
        {
            _logger.LogInformation("Building noVNC WebSocket URL for VM {VmId}", vmId);

            var ticketInfo = await GetVncTicket(vmId);

            // Encode ticket for URL
            var encodedTicket = Uri.EscapeDataString(ticketInfo.Ticket);

            // Final websocket URL that noVNC connects to
            var wsUrl =
                $"{_proxmoxHost.Replace("https", "wss")}/api2/json/nodes/{_node}/qemu/{vmId}/vncwebsocket?port={ticketInfo.Port}&vncticket={encodedTicket}";

            _logger.LogInformation("Generated noVNC WebSocket URL: {Url}", wsUrl);

            return wsUrl;
        }
    }

    // Response models
    public class ProxmoxApiResponse<T>
    {
        public T Data { get; set; }
    }

    public class VmStatusData
    {
        public string Status { get; set; }
        public string Name { get; set; }
        public int Uptime { get; set; }
    }

    public class VmStatus
    {
        public int VmId { get; set; }
        public string Status { get; set; }
        public string Name { get; set; }
        public int Uptime { get; set; }
    }

    public class VncTicketData
    {
        public string Ticket { get; set; }

        private object _port;

        [System.Text.Json.Serialization.JsonPropertyName("port")]
        public object PortRaw
        {
            get => _port;
            set => _port = value;
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public int Port
        {
            get
            {
                if (_port == null) return 0;

                if (_port is int intPort)
                    return intPort;

                if (_port is string strPort && int.TryParse(strPort, out int parsedPort))
                    return parsedPort;

                if (_port is System.Text.Json.JsonElement jsonElement)
                {
                    if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Number)
                        return jsonElement.GetInt32();
                    if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var str = jsonElement.GetString();
                        if (int.TryParse(str, out int parsed))
                            return parsed;
                    }
                }

                return 0;
            }
        }

        public string Upid { get; set; }
    }

    public class VncTicket
    {
        public string Ticket { get; set; }
        public int Port { get; set; }
        public string Upid { get; set; }
    }

    public class VmInfoData
    {
        public int VmId { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
    }

    public class VmInfo
    {
        public int VmId { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
    }
}
