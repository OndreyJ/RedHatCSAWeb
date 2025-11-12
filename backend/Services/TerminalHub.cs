using Microsoft.AspNetCore.SignalR;
using Renci.SshNet;
using System.Collections.Concurrent;
using System.Text;

namespace RHCSAExam.Services
{
    public class TerminalHub : Hub
    {
        private static ConcurrentDictionary<string, SshClient> _sshConnections = new();
        private static ConcurrentDictionary<string, ShellStream> _shellStreams = new();
        private readonly IConfiguration _configuration;

        public TerminalHub(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // Connect to VM via SSH
        public async Task ConnectToVm(string sessionId, string vmIp, string username, string password)
        {
            try
            {
                var connectionId = Context.ConnectionId;

                // Create SSH connection
                var sshClient = new SshClient(vmIp, 22, username, password);
                sshClient.Connect();

                // Create shell stream
                var stream = sshClient.CreateShellStream("xterm", 80, 24, 800, 600, 1024);

                _sshConnections[connectionId] = sshClient;
                _shellStreams[connectionId] = stream;

                // Start reading from shell in background
                _ = Task.Run(() => ReadFromShell(connectionId, stream));

                await Clients.Caller.SendAsync("Connected", "Terminal connected successfully");
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("Error", $"Connection failed: {ex.Message}");
            }
        }

        // Send command to SSH shell
        public async Task SendCommand(string command)
        {
            var connectionId = Context.ConnectionId;

            if (_shellStreams.TryGetValue(connectionId, out var stream))
            {
                try
                {
                    stream.WriteLine(command);
                }
                catch (Exception ex)
                {
                    await Clients.Caller.SendAsync("Error", $"Failed to send command: {ex.Message}");
                }
            }
        }

        // Send raw input (for key presses, etc.)
        public async Task SendInput(string input)
        {
            var connectionId = Context.ConnectionId;

            if (_shellStreams.TryGetValue(connectionId, out var stream))
            {
                try
                {
                    stream.Write(input);
                    stream.Flush();
                }
                catch (Exception ex)
                {
                    await Clients.Caller.SendAsync("Error", $"Failed to send input: {ex.Message}");
                }
            }
        }

        // Resize terminal
        public void ResizeTerminal(int cols, int rows)
        {
            var connectionId = Context.ConnectionId;

            if (_shellStreams.TryGetValue(connectionId, out var stream))
            {
                // SSH.NET doesn't support dynamic resize, but we store for reconnection
                // You might need a different library or approach for true resize support
            }
        }

        // Read from shell and send to client
        private async Task ReadFromShell(string connectionId, ShellStream stream)
        {
            try
            {
                var buffer = new byte[4096];

                while (stream.CanRead && _shellStreams.ContainsKey(connectionId))
                {
                    if (stream.DataAvailable)
                    {
                        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            var output = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            await Clients.Client(connectionId).SendAsync("Output", output);
                        }
                    }
                    else
                    {
                        await Task.Delay(10);
                    }
                }
            }
            catch (Exception ex)
            {
                await Clients.Client(connectionId).SendAsync("Error", $"Stream error: {ex.Message}");
            }
        }

        // Disconnect and cleanup
        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var connectionId = Context.ConnectionId;

            if (_shellStreams.TryRemove(connectionId, out var stream))
            {
                stream.Dispose();
            }

            if (_sshConnections.TryRemove(connectionId, out var client))
            {
                client.Disconnect();
                client.Dispose();
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
