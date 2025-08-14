using System.Diagnostics;
using System.Management;
using MasterMcpServer.Models;
using Microsoft.Extensions.Logging;

namespace MasterMcpServer.Services;

public interface IServerProcessManager
{
    Task<bool> StartServerAsync(ServerDefinition server);
    Task<bool> StopServerAsync(string serverName);
    Task<bool> RestartServerAsync(string serverName);
    Task<ServerStatus> GetServerStatusAsync(string serverName);
    Task<List<ServerMetrics>> GetAllServerMetricsAsync();
    Task<bool> IsServerRunningAsync(string serverName);
    Task KillAllServersAsync();
}

public class ServerProcessManager : IServerProcessManager
{
    private readonly ILogger<ServerProcessManager> _logger;
    private readonly Dictionary<string, Process> _runningProcesses = new();
    private readonly Dictionary<string, ServerDefinition> _serverDefinitions = new();

    public ServerProcessManager(ILogger<ServerProcessManager> logger)
    {
        _logger = logger;
    }

    public async Task<bool> StartServerAsync(ServerDefinition server)
    {
        try
        {
            if (_runningProcesses.ContainsKey(server.Name))
            {
                _logger.LogWarning("Server {ServerName} is already running", server.Name);
                return false;
            }

            var processInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{server.ProjectPath}\"",
                WorkingDirectory = Path.GetDirectoryName(server.ProjectPath) ?? "",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Add environment variables
            foreach (var env in server.EnvironmentVariables)
            {
                processInfo.EnvironmentVariables[env.Key] = env.Value;
            }

            var process = Process.Start(processInfo);
            if (process == null)
            {
                _logger.LogError("Failed to start server {ServerName}", server.Name);
                return false;
            }

            _runningProcesses[server.Name] = process;
            _serverDefinitions[server.Name] = server;
            
            server.Status = ServerStatus.Running;
            server.ProcessId = process.Id;
            server.LastStarted = DateTime.UtcNow;

            _logger.LogInformation("Server {ServerName} started with PID {ProcessId}", server.Name, process.Id);

            // Monitor process exit
            _ = Task.Run(async () =>
            {
                await process.WaitForExitAsync();
                _runningProcesses.Remove(server.Name);
                server.Status = ServerStatus.Stopped;
                server.ProcessId = 0;
                _logger.LogInformation("Server {ServerName} has stopped", server.Name);
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting server {ServerName}", server.Name);
            return false;
        }
    }

    public async Task<bool> StopServerAsync(string serverName)
    {
        try
        {
            if (!_runningProcesses.TryGetValue(serverName, out var process))
            {
                _logger.LogWarning("Server {ServerName} is not running", serverName);
                return false;
            }

            if (_serverDefinitions.TryGetValue(serverName, out var server))
            {
                server.Status = ServerStatus.Stopping;
            }

            // Try graceful shutdown first
            try
            {
                process.StandardInput.WriteLine("quit");
                if (await WaitForExitAsync(process, TimeSpan.FromSeconds(10)))
                {
                    _runningProcesses.Remove(serverName);
                    if (server != null)
                    {
                        server.Status = ServerStatus.Stopped;
                        server.ProcessId = 0;
                    }
                    _logger.LogInformation("Server {ServerName} stopped gracefully", serverName);
                    return true;
                }
            }
            catch
            {
                // Graceful shutdown failed, force kill
            }

            // Force kill
            process.Kill(true);
            _runningProcesses.Remove(serverName);
            
            if (server != null)
            {
                server.Status = ServerStatus.Stopped;
                server.ProcessId = 0;
            }

            _logger.LogInformation("Server {ServerName} force stopped", serverName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping server {ServerName}", serverName);
            return false;
        }
    }

    public async Task<bool> RestartServerAsync(string serverName)
    {
        if (!_serverDefinitions.TryGetValue(serverName, out var server))
        {
            _logger.LogError("Server definition not found for {ServerName}", serverName);
            return false;
        }

        var stopResult = await StopServerAsync(serverName);
        if (!stopResult)
        {
            _logger.LogError("Failed to stop server {ServerName} for restart", serverName);
            return false;
        }

        // Wait a bit before starting
        await Task.Delay(2000);

        return await StartServerAsync(server);
    }

    public async Task<ServerStatus> GetServerStatusAsync(string serverName)
    {
        if (_serverDefinitions.TryGetValue(serverName, out var server))
        {
            return server.Status;
        }

        return ServerStatus.Unknown;
    }

    public async Task<List<ServerMetrics>> GetAllServerMetricsAsync()
    {
        var metrics = new List<ServerMetrics>();

        foreach (var kvp in _runningProcesses)
        {
            try
            {
                var process = kvp.Value;
                var serverName = kvp.Key;

                if (process.HasExited)
                    continue;

                var metric = new ServerMetrics
                {
                    ServerName = serverName,
                    CpuUsage = GetProcessCpuUsage(process),
                    MemoryUsage = process.WorkingSet64 / 1024.0 / 1024.0, // MB
                    ThreadCount = process.Threads.Count,
                    Uptime = DateTime.UtcNow - process.StartTime,
                    Timestamp = DateTime.UtcNow
                };

                metrics.Add(metric);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting metrics for server {ServerName}", kvp.Key);
            }
        }

        return metrics;
    }

    public async Task<bool> IsServerRunningAsync(string serverName)
    {
        return _runningProcesses.ContainsKey(serverName) && 
               !_runningProcesses[serverName].HasExited;
    }

    public async Task KillAllServersAsync()
    {
        var tasks = _runningProcesses.Keys.Select(StopServerAsync);
        await Task.WhenAll(tasks);
    }

    private async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<bool>();
        
        process.Exited += (_, _) => tcs.SetResult(true);
        process.EnableRaisingEvents = true;

        var timeoutTask = Task.Delay(timeout);
        var exitTask = tcs.Task;

        var completedTask = await Task.WhenAny(exitTask, timeoutTask);
        return completedTask == exitTask;
    }

    private double GetProcessCpuUsage(Process process)
    {
        try
        {
            // This is a simplified CPU usage calculation
            // In a real implementation, you might want to use performance counters
            return 0.0; // Placeholder
        }
        catch
        {
            return 0.0;
        }
    }
}