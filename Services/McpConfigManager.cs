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
}

public class McpConfigManager : IMcpConfigManager
{
    private readonly ILogger<McpConfigManager> _logger;
    private readonly string _defaultConfigPath;

    public McpConfigManager(ILogger<McpConfigManager> logger)
    {
        _logger = logger;
        
        // Create default config path in Documents/MasterMcpServer/.vscode/mcp.json
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _defaultConfigPath = Path.Combine(documentsPath, "Documents", "MasterMcpServer", ".vscode", "mcp.json");
        
        // Ensure directory exists
        var directory = Path.GetDirectoryName(_defaultConfigPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task<bool> AddServerToConfigAsync(ServerDefinition server, string? configPath = null)
    {
        try
        {
            var actualConfigPath = configPath ?? _defaultConfigPath;
            
            var config = await GetCurrentConfigAsync(actualConfigPath);
            
            var serverConfig = new ServerConfig
            {
                Type = "stdio",
                Command = "dotnet",
                Args = new List<string> { "run", "--project", server.ProjectPath },
                Env = server.EnvironmentVariables,
                Cwd = Path.GetDirectoryName(server.ProjectPath) ?? ""
            };

            config.Servers[server.Name] = serverConfig;

            await SaveConfigAsync(config, actualConfigPath);
            
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
            var actualConfigPath = configPath ?? _defaultConfigPath;
            
            var config = await GetCurrentConfigAsync(actualConfigPath);
            
            if (config.Servers.Remove(serverName))
            {
                await SaveConfigAsync(config, actualConfigPath);
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
            var actualConfigPath = configPath ?? _defaultConfigPath;
            
            if (!File.Exists(actualConfigPath))
            {
                _logger.LogInformation("Configuration file not found, creating new one: {ConfigPath}", actualConfigPath);
                var newConfig = new McpConfiguration();
                await SaveConfigAsync(newConfig, actualConfigPath);
                return newConfig;
            }

            var json = await File.ReadAllTextAsync(actualConfigPath);
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
            var actualConfigPath = configPath ?? _defaultConfigPath;
            
            if (!File.Exists(actualConfigPath))
            {
                _logger.LogWarning("Configuration file does not exist: {ConfigPath}", actualConfigPath);
                return false;
            }

            var backupPath = $"{actualConfigPath}.backup.{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            await Task.Run(() => File.Copy(actualConfigPath, backupPath));
            
            _logger.LogInformation("Configuration backed up to {BackupPath}", backupPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error backing up configuration");
            return false;
        }
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