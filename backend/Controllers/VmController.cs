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
        private readonly ILogger<VmController> _logger;

        // Store user session VMs (in production, use Redis or database)
        private static ConcurrentDictionary<string, UserVmSession> _userSessions = new();

        public VmController(ProxmoxService proxmoxService, IConfiguration configuration, ILogger<VmController> logger)
        {
            _proxmoxService = proxmoxService;
            _configuration = configuration;
            _logger = logger;
        }

        // POST test endpoint to verify backend is working
        [HttpPost("ping")]
        public ActionResult PingPost()
        {
            _logger.LogInformation("Ping endpoint called");
            return Ok(new
            {
                status = "ok",
                message = "Backend POST endpoint is working"
            });
        }

        // Initialize exam environment - creates 3 VMs from templates
        [HttpPost("session/start")]
        public async Task<ActionResult<ExamSession>> StartExamSession([FromBody] StartSessionRequest request)
        {
            _logger.LogInformation("=== StartExamSession called ===");
            _logger.LogInformation("Request UserId: {UserId}", request?.UserId ?? "NULL");

            try
            {
                var sessionId = Guid.NewGuid().ToString();
                _logger.LogInformation("Generated Session ID: {SessionId}", sessionId);

                // Get template IDs from configuration
                _logger.LogInformation("Reading template IDs from configuration...");

                var template1Config = _configuration["Proxmox:Templates:Server1"];
                var template2Config = _configuration["Proxmox:Templates:Server2"];
                var template3Config = _configuration["Proxmox:Templates:Server3"];

                _logger.LogInformation("Template1 Config Value: {Template1}", template1Config ?? "NULL");
                _logger.LogInformation("Template2 Config Value: {Template2}", template2Config ?? "NULL");
                _logger.LogInformation("Template3 Config Value: {Template3}", template3Config ?? "NULL");

                if (string.IsNullOrEmpty(template1Config) || string.IsNullOrEmpty(template2Config) || string.IsNullOrEmpty(template3Config))
                {
                    _logger.LogError("One or more template IDs are missing from configuration!");
                    return StatusCode(500, new { error = "Template configuration is missing. Check appsettings or environment variables." });
                }

                int template1Id, template2Id, template3Id;

                try
                {
                    template1Id = int.Parse(template1Config);
                    template2Id = int.Parse(template2Config);
                    template3Id = int.Parse(template3Config);
                    _logger.LogInformation("Parsed Template IDs - Server1: {T1}, Server2: {T2}, Server3: {T3}",
                        template1Id, template2Id, template3Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse template IDs");
                    return StatusCode(500, new { error = "Invalid template ID format in configuration" });
                }

                // Clone VMs from templates
                _logger.LogInformation("Starting VM cloning process...");

                _logger.LogInformation("Cloning VM1 from template {TemplateId}...", template1Id);
                var vm1Id = await _proxmoxService.CloneVmFromTemplate(
                    template1Id,
                    $"exam-{sessionId}-server1"
                );
                _logger.LogInformation("VM1 cloned successfully with ID: {VmId}", vm1Id);

                _logger.LogInformation("Cloning VM2 from template {TemplateId}...", template2Id);
                var vm2Id = await _proxmoxService.CloneVmFromTemplate(
                    template2Id,
                    $"exam-{sessionId}-server2"
                );
                _logger.LogInformation("VM2 cloned successfully with ID: {VmId}", vm2Id);

                _logger.LogInformation("Cloning VM3 from template {TemplateId}...", template3Id);
                var vm3Id = await _proxmoxService.CloneVmFromTemplate(
                    template3Id,
                    $"exam-{sessionId}-server3"
                );
                _logger.LogInformation("VM3 cloned successfully with ID: {VmId}", vm3Id);

                // Store session
                _logger.LogInformation("Creating session object...");
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
                _logger.LogInformation("Session stored. Total active sessions: {Count}", _userSessions.Count);

                var response = new ExamSession
                {
                    SessionId = sessionId,
                    Server1VmId = vm1Id,
                    Server2VmId = vm2Id,
                    Server3VmId = vm3Id,
                    Message = "Exam environment created successfully"
                };

                _logger.LogInformation("=== StartExamSession completed successfully ===");
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FATAL ERROR in StartExamSession - Exception Type: {ExceptionType}, Message: {Message}",
                    ex.GetType().Name, ex.Message);
                _logger.LogError("Stack Trace: {StackTrace}", ex.StackTrace);

                if (ex.InnerException != null)
                {
                    _logger.LogError("Inner Exception: {InnerMessage}", ex.InnerException.Message);
                    _logger.LogError("Inner Stack Trace: {InnerStackTrace}", ex.InnerException.StackTrace);
                }

                return StatusCode(500, new
                {
                    error = ex.Message,
                    type = ex.GetType().Name,
                    innerError = ex.InnerException?.Message
                });
            }
        }

        // Start a specific VM
        [HttpPost("session/{sessionId}/vm/{vmName}/start")]
        public async Task<ActionResult> StartVm(string sessionId, string vmName)
        {
            _logger.LogInformation("StartVm called - SessionId: {SessionId}, VmName: {VmName}", sessionId, vmName);

            if (!_userSessions.TryGetValue(sessionId, out var session))
            {
                _logger.LogWarning("Session not found: {SessionId}", sessionId);
                return NotFound("Session not found");
            }

            var vmId = GetVmIdByName(session, vmName);
            if (vmId == 0)
            {
                _logger.LogWarning("Invalid VM name: {VmName}", vmName);
                return BadRequest("Invalid VM name");
            }

            _logger.LogInformation("Starting VM {VmId}...", vmId);
            var success = await _proxmoxService.StartVm(vmId);

            if (success)
            {
                _logger.LogInformation("VM {VmId} started successfully", vmId);
                return Ok(new { message = $"{vmName} started successfully" });
            }

            _logger.LogError("Failed to start VM {VmId}", vmId);
            return StatusCode(500, "Failed to start VM");
        }

        // Stop a specific VM
        [HttpPost("session/{sessionId}/vm/{vmName}/stop")]
        public async Task<ActionResult> StopVm(string sessionId, string vmName)
        {
            _logger.LogInformation("StopVm called - SessionId: {SessionId}, VmName: {VmName}", sessionId, vmName);

            if (!_userSessions.TryGetValue(sessionId, out var session))
            {
                _logger.LogWarning("Session not found: {SessionId}", sessionId);
                return NotFound("Session not found");
            }

            var vmId = GetVmIdByName(session, vmName);
            if (vmId == 0)
            {
                _logger.LogWarning("Invalid VM name: {VmName}", vmName);
                return BadRequest("Invalid VM name");
            }

            _logger.LogInformation("Stopping VM {VmId}...", vmId);
            var success = await _proxmoxService.StopVm(vmId);

            if (success)
            {
                _logger.LogInformation("VM {VmId} stopped successfully", vmId);
                return Ok(new { message = $"{vmName} stopped successfully" });
            }

            _logger.LogError("Failed to stop VM {VmId}", vmId);
            return StatusCode(500, "Failed to stop VM");
        }

        // Get VM status
        [HttpGet("session/{sessionId}/vm/{vmName}/status")]
        public async Task<ActionResult<VmStatus>> GetVmStatus(string sessionId, string vmName)
        {
            _logger.LogInformation("GetVmStatus called - SessionId: {SessionId}, VmName: {VmName}", sessionId, vmName);

            if (!_userSessions.TryGetValue(sessionId, out var session))
            {
                _logger.LogWarning("Session not found: {SessionId}", sessionId);
                return NotFound("Session not found");
            }

            var vmId = GetVmIdByName(session, vmName);
            if (vmId == 0)
            {
                _logger.LogWarning("Invalid VM name: {VmName}", vmName);
                return BadRequest("Invalid VM name");
            }

            var status = await _proxmoxService.GetVmStatus(vmId);

            if (status == null)
            {
                _logger.LogWarning("VM {VmId} not found", vmId);
                return NotFound("VM not found");
            }

            return Ok(status);
        }

        // Get all VMs status for session
        [HttpGet("session/{sessionId}/status")]
        public async Task<ActionResult<SessionStatus>> GetSessionStatus(string sessionId)
        {
            _logger.LogInformation("GetSessionStatus called - SessionId: {SessionId}", sessionId);

            if (!_userSessions.TryGetValue(sessionId, out var session))
            {
                _logger.LogWarning("Session not found: {SessionId}", sessionId);
                return NotFound("Session not found");
            }

            var vm1Status = await _proxmoxService.GetVmStatus(session.Vm1Id);
            var vm2Status = await _proxmoxService.GetVmStatus(session.Vm2Id);
            var vm3Status = await _proxmoxService.GetVmStatus(session.Vm3Id);

            return Ok(new SessionStatus
            {
                SessionId = sessionId,
                Server1 = vm1Status ?? new VmStatus { VmId = session.Vm1Id, Status = "unknown", Name = "server1", Uptime = 0 },
                Server2 = vm2Status ?? new VmStatus { VmId = session.Vm2Id, Status = "unknown", Name = "server2", Uptime = 0 },
                Server3 = vm3Status ?? new VmStatus { VmId = session.Vm3Id, Status = "unknown", Name = "server3", Uptime = 0 }
            });
        }

        // Get noVNC console URL for VM - Cookie-based authentication
        [HttpPost("session/{sessionId}/vm/{vmName}/console")]
        public async Task<ActionResult> GetVmConsole(string sessionId, string vmName)
        {
            _logger.LogInformation("GetVmConsole called - SessionId: {SessionId}, VmName: {VmName}", sessionId, vmName);

            if (!_userSessions.TryGetValue(sessionId, out var session))
            {
                _logger.LogWarning("Session not found: {SessionId}", sessionId);
                return NotFound(new { message = "Session not found" });
            }

            var vmId = GetVmIdByName(session, vmName);
            if (vmId == 0)
            {
                _logger.LogWarning("Invalid VM name: {VmName}", vmName);
                return BadRequest(new { message = $"Invalid VM name: {vmName}" });
            }

            _logger.LogInformation("Getting console URL for VM {VmId}...", vmId);

            try
            {
                // Get VNC console info (URL, port, ticket, cookie)
                var consoleInfo = await _proxmoxService.GetVncConsoleUrl(vmId);

                _logger.LogInformation("Console URL generated for VM {VmId}: {Url}", vmId, consoleInfo.Url);

                return Ok(new
                {
                    url = consoleInfo.Url,
                    port = consoleInfo.Port,
                    ticket = consoleInfo.Ticket,
                    csrfToken = consoleInfo.CSRFToken
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get console URL for VM {VmId}", vmId);
                return StatusCode(500, new { message = $"Failed to get console URL: {ex.Message}" });
            }
        }

        // Get basic url
        [HttpPost("session/{sessionId}/vm/{vmName}/url")]
        public async Task<ActionResult> GetVmUrl(string sessionId, string vmName)
        {
            _logger.LogInformation("GetVmConsole called - SessionId: {SessionId}, VmName: {VmName}", sessionId, vmName);

            if (!_userSessions.TryGetValue(sessionId, out var session))
            {
                _logger.LogWarning("Session not found: {SessionId}", sessionId);
                return NotFound(new { message = "Session not found" });
            }

            var vmId = GetVmIdByName(session, vmName);
            if (vmId == 0)
            {
                _logger.LogWarning("Invalid VM name: {VmName}", vmName);
                return BadRequest(new { message = $"Invalid VM name: {vmName}" });
            }

            _logger.LogInformation("Getting console URL for VM {VmId}...", vmId);

            try
            {
                // Get VNC console info (URL, port, ticket, cookie)
                var consoleInfo = await _proxmoxService.GetBasicConsoleUrl(vmId);

                _logger.LogInformation("Console URL generated for VM {VmId}: {Url}", vmId, consoleInfo.Url);

                return Ok(new
                {
                    url = consoleInfo.Url
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get console URL for VM {VmId}", vmId);
                return StatusCode(500, new { message = $"Failed to get console URL: {ex.Message}" });
            }
        }

        // End exam session - cleanup VMs
        [HttpDelete("session/{sessionId}")]
        public async Task<ActionResult> EndSession(string sessionId)
        {
            _logger.LogInformation("EndSession called - SessionId: {SessionId}", sessionId);

            if (!_userSessions.TryGetValue(sessionId, out var session))
            {
                _logger.LogWarning("Session not found: {SessionId}", sessionId);
                return NotFound("Session not found");
            }

            try
            {
                _logger.LogInformation("Deleting VMs for session {SessionId}...", sessionId);

                // Delete all VMs
                await _proxmoxService.DeleteVm(session.Vm1Id);
                _logger.LogInformation("Deleted VM1: {VmId}", session.Vm1Id);

                await _proxmoxService.DeleteVm(session.Vm2Id);
                _logger.LogInformation("Deleted VM2: {VmId}", session.Vm2Id);

                await _proxmoxService.DeleteVm(session.Vm3Id);
                _logger.LogInformation("Deleted VM3: {VmId}", session.Vm3Id);

                // Remove session
                _userSessions.TryRemove(sessionId, out _);
                _logger.LogInformation("Session {SessionId} removed. Remaining sessions: {Count}",
                    sessionId, _userSessions.Count);

                return Ok(new { message = "Session ended and VMs deleted" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ending session {SessionId}", sessionId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Cleanup old sessions (call this periodically)
        [HttpPost("cleanup")]
        public async Task<ActionResult> CleanupOldSessions()
        {
            _logger.LogInformation("CleanupOldSessions called");

            var oldSessions = _userSessions
                .Where(s => (DateTime.UtcNow - s.Value.CreatedAt).TotalHours > 4)
                .Select(s => s.Key)
                .ToList();

            _logger.LogInformation("Found {Count} old sessions to clean up", oldSessions.Count);

            foreach (var sessionId in oldSessions)
            {
                if (_userSessions.TryGetValue(sessionId, out var session))
                {
                    try
                    {
                        _logger.LogInformation("Cleaning up session {SessionId}...", sessionId);
                        await _proxmoxService.DeleteVm(session.Vm1Id);
                        await _proxmoxService.DeleteVm(session.Vm2Id);
                        await _proxmoxService.DeleteVm(session.Vm3Id);
                        _userSessions.TryRemove(sessionId, out _);
                        _logger.LogInformation("Session {SessionId} cleaned up successfully", sessionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error cleaning up session {SessionId}", sessionId);
                    }
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
