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

        public ProxmoxService(IConfiguration configuration)
        {
            _proxmoxHost = configuration["Proxmox:Host"];
            _apiToken = configuration["Proxmox:ApiToken"];
            _node = configuration["Proxmox:Node"];

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("PVEAPIToken", _apiToken);

            // Accept self-signed certificates (for dev only!)
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("PVEAPIToken", _apiToken);
        }

        // Clone VM from template
        public async Task<int> CloneVmFromTemplate(int templateId, string newVmName)
        {
            var newVmId = await GetNextVmId();
            var url = $"{_proxmoxHost}/api2/json/nodes/{_node}/qemu/{templateId}/clone";

            var payload = new
            {
                newid = newVmId,
                name = newVmName,
                full = 1, // Full clone
                target = _node
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            return newVmId;
        }

        // Start VM
        public async Task<bool> StartVm(int vmId)
        {
            var url = $"{_proxmoxHost}/api2/json/nodes/{_node}/qemu/{vmId}/status/start";
            var response = await _httpClient.PostAsync(url, null);
            return response.IsSuccessStatusCode;
        }

        // Stop VM
        public async Task<bool> StopVm(int vmId)
        {
            var url = $"{_proxmoxHost}/api2/json/nodes/{_node}/qemu/{vmId}/status/stop";
            var response = await _httpClient.PostAsync(url, null);
            return response.IsSuccessStatusCode;
        }

        // Get VM Status
        public async Task<VmStatus> GetVmStatus(int vmId)
        {
            var url = $"{_proxmoxHost}/api2/json/nodes/{_node}/qemu/{vmId}/status/current";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ProxmoxApiResponse<VmStatusData>>(json);

            return new VmStatus
            {
                VmId = vmId,
                Status = result.Data.Status,
                Name = result.Data.Name,
                Uptime = result.Data.Uptime
            };
        }

        // Delete VM
        public async Task<bool> DeleteVm(int vmId)
        {
            var url = $"{_proxmoxHost}/api2/json/nodes/{_node}/qemu/{vmId}";
            var response = await _httpClient.DeleteAsync(url);
            return response.IsSuccessStatusCode;
        }

        // Get VNC WebSocket ticket for terminal access
        public async Task<VncTicket> GetVncTicket(int vmId)
        {
            var url = $"{_proxmoxHost}/api2/json/nodes/{_node}/qemu/{vmId}/vncproxy";
            var payload = new { websocket = 1 };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ProxmoxApiResponse<VncTicketData>>(json);

            return new VncTicket
            {
                Ticket = result.Data.Ticket,
                Port = result.Data.Port,
                Upid = result.Data.Upid
            };
        }

        // Helper to get next available VM ID
        private async Task<int> GetNextVmId()
        {
            var url = $"{_proxmoxHost}/api2/json/cluster/nextid";
            var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ProxmoxApiResponse<int>>(json);
            return result.Data;
        }

        // List all VMs
        public async Task<List<VmInfo>> ListVms()
        {
            var url = $"{_proxmoxHost}/api2/json/nodes/{_node}/qemu";
            var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ProxmoxApiResponse<List<VmInfoData>>>(json);

            return result.Data.Select(vm => new VmInfo
            {
                VmId = vm.VmId,
                Name = vm.Name,
                Status = vm.Status
            }).ToList();
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
        public int Port { get; set; }
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
