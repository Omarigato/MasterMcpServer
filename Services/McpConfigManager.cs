using System.Text.Json;
using MasterMcpServer.Models;
using Microsoft.Extensions.Logging;

namespace MasterMcpServer.Services;

public interface IMcpConfigManager
{
    Task<bool> AddServerToConfigAsync(ServerDefinition server, string? configPath = null);
    Task<bool> RemoveServerFromConfigAsync(string serverName, string? configPath = null);
    Task<bool> UpdateServerConfigAsync(ServerDefinition server, string? configPath = null);
    Task<McpConfiguration> GetCurrentConfigAsync(string? configPath = null);
    Task<bool> BackupConfigAsync(string? configPath = null);
    Task<bool> RestoreConfigAsync(string backupPath, string? configPath = null);
    Task<List<string>> FindMcpConfigFilesAsync();
}

public class McpConfigManager : IMcpConfigManager
{
    private readonly ILogger<McpConfigManager> _logger;
    private readonly string _defaultConfigPath;

    public McpConfigManager(ILogger<McpConfigManager> logger)
    {
        _logger = logger;
        _defaultConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                                         "Documents", "WeatherMcpServer", ".vscode", "mcp.json");
    }

    public async Task<bool> AddServerToConfigAsync(ServerDefinition server, string? configPath = null)
    {
        try
        {
            configPath ??= _defaultConfigPath;
            
            var config = await GetCurrentConfigAsync(configPath);
            
            var serverConfig = new ServerConfig
            {
                Type = "stdio",
                Command = "dotnet",
                Args = new List<string> { "run", "--project", server.ProjectPath },
                Env = server.EnvironmentVariables,
                Cwd = Path.GetDirectoryName(server.ProjectPath) ?? ""
            };

            config.Servers[server.Name] = serverConfig;

            await SaveConfigAsync(config, configPath);
            
            _logger.LogInformation("Added server {ServerName} to MCP configuration", server.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding server {ServerName} to configuration", server.Name);
            return false;
        }
    }

    public async Task<bool> RemoveServerFromConfigAsync(string serverName, string? configPath = null)
    {
        try
        {
            configPath ??= _defaultConfigPath;
            
            var config = await GetCurrentConfigAsync(configPath);
            
            if (config.Servers.Remove(serverName))
            {
                await SaveConfigAsync(config, configPath);
                _logger.LogInformation("Removed server {ServerName} from MCP configuration", serverName);
                return true;
            }

            _logger.LogWarning("Server {ServerName} not found in configuration", serverName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing server {ServerName} from configuration", serverName);
            return false;
        }
    }

    public async Task<bool> UpdateServerConfigAsync(ServerDefinition server, string? configPath = null)
    {
        return await AddServerToConfigAsync(server, configPath);
    }

    public async Task<McpConfiguration> GetCurrentConfigAsync(string? configPath = null)
    {
        try
        {
            configPath ??= _defaultConfigPath;
            
            if (!File.Exists(configPath))
            {
                _logger.LogInformation("Configuration file not found, creating new one: {ConfigPath}", configPath);
                var newConfig = new McpConfiguration();
                await SaveConfigAsync(newConfig, configPath);
                return newConfig;
            }

            var json = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<McpConfiguration>(json);
            
            return config ?? new McpConfiguration();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading MCP configuration from {ConfigPath}", configPath);
            return new McpConfiguration();
        }
    }

    public async Task<bool> BackupConfigAsync(string? configPath = null)
    {
        try
        {
            configPath ??= _defaultConfigPath;
            
            if (!File.Exists(configPath))
            {
                _logger.LogWarning("Configuration file does not exist: {ConfigPath}", configPath);
                return false;
            }

            var backupPath = $"{configPath}.backup.{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            File.Copy(configPath, backupPath);
            
            _logger.LogInformation("Configuration backed up to {BackupPath}", backupPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error backing up configuration");
            return false;
        }
    }

    public async Task<bool> RestoreConfigAsync(string backupPath, string? configPath = null)
    {
        try
        {
            configPath ??= _defaultConfigPath;
            
            if (!File.Exists(backupPath))
            {
                _logger.LogError("Backup file does not exist: {BackupPath}", backupPath);
                return false;
            }

            File.Copy(backupPath, configPath, overwrite: true);
            
            _logger.LogInformation("Configuration restored from {BackupPath}", backupPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring configuration from {BackupPath}", backupPath);
            return false;
        }
    }

    public async Task<List<string>> FindMcpConfigFilesAsync()
    {
        var configFiles = new List<string>();
        
        try
        {
            // Search common VS Code locations
            var searchPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "projects"),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            foreach (var searchPath in searchPaths)
            {
                if (Directory.Exists(searchPath))
                {
                    var files = Directory.GetFiles(searchPath, "mcp.json", SearchOption.AllDirectories)
                                        .Where(f => f.Contains(".vscode"))
                                        .ToList();
                    configFiles.AddRange(files);
                }
            }

            configFiles = configFiles.Distinct().ToList();
            _logger.LogInformation("Found {Count} MCP configuration files", configFiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for MCP configuration files");
        }

        return configFiles;
    }

    private async Task SaveConfigAsync(McpConfiguration config, string configPath)
    {
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(config, options);
        await File.WriteAllTextAsync(configPath, json);
    }
}