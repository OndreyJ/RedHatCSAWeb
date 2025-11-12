using Microsoft.AspNetCore.Mvc;
using RHCSAExam.Services;
using System.Collections.Concurrent;

namespace RHCSAExam.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VmController : ControllerBase
    {
        private readonly ProxmoxService _proxmoxService;
        private readonly IConfiguration _configuration;

        // Store user session VMs (in production, use Redis or database)
        private static ConcurrentDictionary<string, UserVmSession> _userSessions = new();

        public VmController(ProxmoxService proxmoxService, IConfiguration configuration)
        {
            _proxmoxService = proxmoxService;
            _configuration = configuration;
        }

        // Initialize exam environment - creates 3 VMs from templates
        [HttpPost("session/start")]
        public async Task<ActionResult<ExamSession>> StartExamSession([FromBody] StartSessionRequest request)
        {
            try
            {
                var sessionId = Guid.NewGuid().ToString();

                // Get template IDs from configuration
                var template1Id = int.Parse(_configuration["Proxmox:Templates:Server1"]);
                var template2Id = int.Parse(_configuration["Proxmox:Templates:Server2"]);
                var template3Id = int.Parse(_configuration["Proxmox:Templates:Server3"]);

                // Clone VMs from templates
                var vm1Id = await _proxmoxService.CloneVmFromTemplate(
                    template1Id,
                    $"exam-{sessionId}-server1"
                );

                var vm2Id = await _proxmoxService.CloneVmFromTemplate(
                    template2Id,
                    $"exam-{sessionId}-server2"
                );

                var vm3Id = await _proxmoxService.CloneVmFromTemplate(
                    template3Id,
                    $"exam-{sessionId}-server3"
                );

                // Store session
                var session = new UserVmSession
                {
                    SessionId = sessionId,
                    UserId = request.UserId,
                    Vm1Id = vm1Id,
                    Vm2Id = vm2Id,
                    Vm3Id = vm3Id,
                    CreatedAt = DateTime.UtcNow
                };

                _userSessions[sessionId] = session;

                return Ok(new ExamSession
                {
                    SessionId = sessionId,
                    Server1VmId = vm1Id,
                    Server2VmId = vm2Id,
                    Server3VmId = vm3Id,
                    Message = "Exam environment created successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Start a specific VM
        [HttpPost("session/{sessionId}/vm/{vmName}/start")]
        public async Task<ActionResult> StartVm(string sessionId, string vmName)
        {
            if (!_userSessions.TryGetValue(sessionId, out var session))
                return NotFound("Session not found");

            var vmId = GetVmIdByName(session, vmName);
            if (vmId == 0)
                return BadRequest("Invalid VM name");

            var success = await _proxmoxService.StartVm(vmId);

            if (success)
                return Ok(new { message = $"{vmName} started successfully" });

            return StatusCode(500, "Failed to start VM");
        }

        // Stop a specific VM
        [HttpPost("session/{sessionId}/vm/{vmName}/stop")]
        public async Task<ActionResult> StopVm(string sessionId, string vmName)
        {
            if (!_userSessions.TryGetValue(sessionId, out var session))
                return NotFound("Session not found");

            var vmId = GetVmIdByName(session, vmName);
            if (vmId == 0)
                return BadRequest("Invalid VM name");

            var success = await _proxmoxService.StopVm(vmId);

            if (success)
                return Ok(new { message = $"{vmName} stopped successfully" });

            return StatusCode(500, "Failed to stop VM");
        }

        // Get VM status
        [HttpGet("session/{sessionId}/vm/{vmName}/status")]
        public async Task<ActionResult<VmStatus>> GetVmStatus(string sessionId, string vmName)
        {
            if (!_userSessions.TryGetValue(sessionId, out var session))
                return NotFound("Session not found");

            var vmId = GetVmIdByName(session, vmName);
            if (vmId == 0)
                return BadRequest("Invalid VM name");

            var status = await _proxmoxService.GetVmStatus(vmId);

            if (status == null)
                return NotFound("VM not found");

            return Ok(status);
        }

        // Get all VMs status for session
        [HttpGet("session/{sessionId}/status")]
        public async Task<ActionResult<SessionStatus>> GetSessionStatus(string sessionId)
        {
            if (!_userSessions.TryGetValue(sessionId, out var session))
                return NotFound("Session not found");

            var vm1Status = await _proxmoxService.GetVmStatus(session.Vm1Id);
            var vm2Status = await _proxmoxService.GetVmStatus(session.Vm2Id);
            var vm3Status = await _proxmoxService.GetVmStatus(session.Vm3Id);

            return Ok(new SessionStatus
            {
                SessionId = sessionId,
                Server1 = vm1Status,
                Server2 = vm2Status,
                Server3 = vm3Status
            });
        }

        // Get VNC ticket for terminal access
        [HttpGet("session/{sessionId}/vm/{vmName}/console")]
        public async Task<ActionResult<VncTicket>> GetConsoleTicket(string sessionId, string vmName)
        {
            if (!_userSessions.TryGetValue(sessionId, out var session))
                return NotFound("Session not found");

            var vmId = GetVmIdByName(session, vmName);
            if (vmId == 0)
                return BadRequest("Invalid VM name");

            var ticket = await _proxmoxService.GetVncTicket(vmId);
            return Ok(ticket);
        }

        // End exam session - cleanup VMs
        [HttpDelete("session/{sessionId}")]
        public async Task<ActionResult> EndSession(string sessionId)
        {
            if (!_userSessions.TryGetValue(sessionId, out var session))
                return NotFound("Session not found");

            try
            {
                // Delete all VMs
                await _proxmoxService.DeleteVm(session.Vm1Id);
                await _proxmoxService.DeleteVm(session.Vm2Id);
                await _proxmoxService.DeleteVm(session.Vm3Id);

                // Remove session
                _userSessions.TryRemove(sessionId, out _);

                return Ok(new { message = "Session ended and VMs deleted" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Cleanup old sessions (call this periodically)
        [HttpPost("cleanup")]
        public async Task<ActionResult> CleanupOldSessions()
        {
            var oldSessions = _userSessions
                .Where(s => (DateTime.UtcNow - s.Value.CreatedAt).TotalHours > 4)
                .Select(s => s.Key)
                .ToList();

            foreach (var sessionId in oldSessions)
            {
                if (_userSessions.TryGetValue(sessionId, out var session))
                {
                    try
                    {
                        await _proxmoxService.DeleteVm(session.Vm1Id);
                        await _proxmoxService.DeleteVm(session.Vm2Id);
                        await _proxmoxService.DeleteVm(session.Vm3Id);
                        _userSessions.TryRemove(sessionId, out _);
                    }
                    catch { }
                }
            }

            return Ok(new { cleanedSessions = oldSessions.Count });
        }

        private int GetVmIdByName(UserVmSession session, string vmName)
        {
            return vmName.ToLower() switch
            {
                "server1" => session.Vm1Id,
                "server2" => session.Vm2Id,
                "server3" => session.Vm3Id,
                _ => 0
            };
        }
    }

    // Request/Response models
    public class StartSessionRequest
    {
        public string UserId { get; set; }
    }

    public class ExamSession
    {
        public string SessionId { get; set; }
        public int Server1VmId { get; set; }
        public int Server2VmId { get; set; }
        public int Server3VmId { get; set; }
        public string Message { get; set; }
    }

    public class SessionStatus
    {
        public string SessionId { get; set; }
        public VmStatus Server1 { get; set; }
        public VmStatus Server2 { get; set; }
        public VmStatus Server3 { get; set; }
    }

    public class UserVmSession
    {
        public string SessionId { get; set; }
        public string UserId { get; set; }
        public int Vm1Id { get; set; }
        public int Vm2Id { get; set; }
        public int Vm3Id { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
