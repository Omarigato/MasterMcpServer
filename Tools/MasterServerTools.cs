using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using MasterMcpServer.Models;
using MasterMcpServer.Services;
using Microsoft.Extensions.Logging;
using System.Text;

namespace MasterMcpServer.Tools;

public class MasterServerTools
{
    private readonly ILogger<MasterServerTools> _logger;
    private readonly IServerProcessManager _processManager;
    private readonly IMcpConfigManager _configManager;
    private readonly IServerCodeGenerator _codeGenerator;
    private readonly List<ServerDefinition> _servers = new();
    private readonly string _masterServerPath;

    public MasterServerTools(
        ILogger<MasterServerTools> logger,
        IServerProcessManager processManager,
        IMcpConfigManager configManager,
        IServerCodeGenerator codeGenerator)
    {
        _logger = logger;
        _processManager = processManager;
        _configManager = configManager;
        _codeGenerator = codeGenerator;
        
        // Create MasterMcpServer directory structure
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _masterServerPath = Path.Combine(documentsPath, "Documents", "MasterMcpServer");
        
        // Ensure master directory exists
        if (!Directory.Exists(_masterServerPath))
        {
            Directory.CreateDirectory(_masterServerPath);
            _logger.LogInformation("Created MasterMcpServer directory: {Path}", _masterServerPath);
        }
    }

    [McpServerTool]
    [Description("🚀 Creates a complete new MCP server with specified functionality and automatically configures it. Creates project in MasterMcpServer/{ServerName} structure.")]
    public async Task<string> CreateMcpServer(
        [Description("Name of the new server (e.g., 'Weather', 'Racing', 'ChatBot')")] string serverName,
        [Description("Detailed description of what the server should do")] string description,
        [Description("Type of server: database, api, files, email, weather, gaming, chatbot, custom")] string serverType = "custom",
        [Description("Comma-separated list of tools to create (e.g., 'GetWeather,GetForecast,GetAlerts')")] string tools = "")
    {
        try
        {
            _logger.LogInformation("🚀 Creating new MCP server: {ServerName}", serverName);

            var spec = new ServerSpec
            {
                Name = serverName,
                Description = description,
                Type = serverType,
                Tools = ParseToolsFromString(tools, description)
            };

            // Create server in MasterMcpServer/{ServerName} structure
            var projectPath = await _codeGenerator.GenerateFullServerProjectAsync(spec, _masterServerPath);
            
            var server = new ServerDefinition
            {
                Name = serverName,
                DisplayName = $"{serverName} MCP Server",
                Description = description,
                ProjectPath = Path.Combine(projectPath, $"{serverName}.csproj"),
                Status = ServerStatus.Stopped,
                Tags = new List<string> { serverType, "generated", "auto-created" }
            };

            _servers.Add(server);

            // Add to VS Code configuration
            await _configManager.AddServerToConfigAsync(server);

            // Try to build the project
            var buildResult = await BuildServerProject(projectPath);

            return $"""
                🎉 **MCP Server Created Successfully!**
                
                📝 **Server Name:** {serverName}
                📋 **Description:** {description}
                📁 **Project Path:** {projectPath}
                🏗️ **Structure:** MasterMcpServer/{serverName}/
                🔧 **Type:** {serverType}
                🛠️ **Tools Created:** {spec.Tools.Count}
                
                📦 **Build Status:** {(buildResult ? "✅ Success" : "⚠️ Manual build required")}
                ⚙️ **VS Code Config:** Updated automatically
                
                🚀 **Next Steps:**
                1. Use `StartServer {serverName}` to run it immediately
                2. Or restart VS Code to load the new server
                3. Test the server with the generated tools
                
                💡 **Generated Tools:** {string.Join(", ", spec.Tools.Select(t => t.Name))}
                
                📂 **Project Structure:**
                ```
                MasterMcpServer/
                ├── {serverName}/
                │   ├── Tools/
                │   ├── Models/
                │   ├── Services/
                │   ├── .mcp/
                │   └── .vscode/
                ```
                """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating MCP server: {ServerName}", serverName);
            return $"❌ **Error creating server:** {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("⚡ Starts a previously created or configured MCP server from the MasterMcpServer ecosystem.")]
    public async Task<string> StartServer(
        [Description("Name of the server to start")] string serverName)
    {
        try
        {
            var server = _servers.FirstOrDefault(s => s.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));
            if (server == null)
            {
                // Try to find server in the MasterMcpServer directory
                var serverPath = Path.Combine(_masterServerPath, serverName);
                if (Directory.Exists(serverPath))
                {
                    var csprojPath = Path.Combine(serverPath, $"{serverName}.csproj");
                    if (File.Exists(csprojPath))
                    {
                        server = new ServerDefinition
                        {
                            Name = serverName,
                            DisplayName = $"{serverName} MCP Server",
                            Description = $"Auto-discovered {serverName} server",
                            ProjectPath = csprojPath,
                            Status = ServerStatus.Stopped
                        };
                        _servers.Add(server);
                    }
                }
                
                if (server == null)
                {
                    return $"""
                        ❌ **Server not found:** {serverName}
                        
                        📁 **Available servers in MasterMcpServer:**
                        {string.Join("\n", GetAvailableServers())}
                        
                        💡 Use `CreateMcpServer` to create a new server first!
                        """;
                }
            }

            if (await _processManager.IsServerRunningAsync(serverName))
            {
                return $"ℹ️ **Server already running:** {serverName}";
            }

            var success = await _processManager.StartServerAsync(server);
            
            if (success)
            {
                return $"""
                    ✅ **Server started successfully!**
                    
                    🚀 **Server:** {serverName}
                    📊 **Status:** Running
                    🆔 **Process ID:** {server.ProcessId}
                    ⏰ **Started:** {server.LastStarted:HH:mm:ss}
                    📁 **Location:** MasterMcpServer/{serverName}/
                    
                    🎯 **Ready to accept commands in VS Code chat!**
                    """;
            }
            else
            {
                return $"❌ **Failed to start server:** {serverName}\n\nCheck the project path and build status.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting server: {ServerName}", serverName);
            return $"❌ **Error starting server:** {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("🛑 Stops a running MCP server gracefully.")]
    public async Task<string> StopServer(
        [Description("Name of the server to stop")] string serverName)
    {
        try
        {
            var success = await _processManager.StopServerAsync(serverName);
            
            if (success)
            {
                return $"✅ **Server stopped successfully:** {serverName}";
            }
            else
            {
                return $"❌ **Failed to stop server:** {serverName} (may not be running)";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping server: {ServerName}", serverName);
            return $"❌ **Error stopping server:** {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("🔄 Restarts an MCP server (stops and starts it again) with updated configuration.")]
    public async Task<string> RestartServer(
        [Description("Name of the server to restart")] string serverName)
    {
        try
        {
            var success = await _processManager.RestartServerAsync(serverName);
            
            if (success)
            {
                return $"""
                    🔄 **Server restarted successfully:** {serverName}
                    
                    ✅ Ready to use with updated configuration!
                    📁 Location: MasterMcpServer/{serverName}/
                    """;
            }
            else
            {
                return $"❌ **Failed to restart server:** {serverName}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restarting server: {ServerName}", serverName);
            return $"❌ **Error restarting server:** {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("📊 Shows the status and metrics of all managed MCP servers in the ecosystem.")]
    public async Task<string> GetServerStatus()
    {
        try
        {
            // Discover servers in MasterMcpServer directory
            await DiscoverServersInDirectory();
            
            var metrics = await _processManager.GetAllServerMetricsAsync();
            
            if (!_servers.Any())
            {
                return $"""
                    📭 **No servers configured yet.**
                    
                    📁 **MasterMcpServer Path:** {_masterServerPath}
                    
                    🚀 Use `CreateMcpServer` to create your first server!
                    
                    **Example:**
                    ```
                    CreateMcpServer Weather "Advanced weather service" weather "GetWeather,GetForecast,GetAlerts"
                    ```
                    """;
            }

            var result = new StringBuilder();
            result.AppendLine("🖥️ **MCP Server Ecosystem Dashboard**");
            result.AppendLine($"📁 **Master Path:** {_masterServerPath}");
            result.AppendLine();
            
            foreach (var server in _servers)
            {
                var isRunning = await _processManager.IsServerRunningAsync(server.Name);
                var metric = metrics.FirstOrDefault(m => m.ServerName == server.Name);
                
                var statusIcon = isRunning ? "🟢" : "🔴";
                var statusText = isRunning ? "Running" : "Stopped";
                
                result.AppendLine($"{statusIcon} **{server.Name}**");
                result.AppendLine($"   📊 Status: {statusText}");
                result.AppendLine($"   📋 Description: {server.Description}");
                result.AppendLine($"   📁 Path: MasterMcpServer/{server.Name}/");
                
                if (metric != null && isRunning)
                {
                    result.AppendLine($"   💾 Memory: {metric.MemoryUsage:F1} MB");
                    result.AppendLine($"   🕐 Uptime: {metric.Uptime:hh\\:mm\\:ss}");
                    result.AppendLine($"   🧵 Threads: {metric.ThreadCount}");
                }
                
                if (isRunning)
                {
                    result.AppendLine($"   🆔 PID: {server.ProcessId}");
                    result.AppendLine($"   ⏰ Started: {server.LastStarted:MMM dd, HH:mm:ss}");
                }
                
                result.AppendLine();
            }

            var runningCount = _servers.Count(s => s.Status == ServerStatus.Running);
            result.AppendLine($"📈 **Summary:** {_servers.Count} total servers, {runningCount} running");
            
            return result.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting server status");
            return $"❌ **Error getting status:** {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("🔧 Adds a new tool/capability to an existing MCP server in the ecosystem.")]
    public async Task<string> AddToolToServer(
        [Description("Name of the server to modify")] string serverName,
        [Description("Name of the new tool")] string toolName,
        [Description("Description of what the tool does")] string toolDescription,
        [Description("Parameters for the tool (format: 'param1:type:description,param2:type:description')")] string parameters = "")
    {
        try
        {
            var server = _servers.FirstOrDefault(s => s.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));
            if (server == null)
            {
                return $"❌ **Server not found:** {serverName}\n\nUse `GetServerStatus` to see available servers.";
            }

            var tool = new ToolSpec
            {
                Name = toolName,
                Description = toolDescription,
                Parameters = ParseParametersFromString(parameters)
            };

            var projectDirectory = Path.GetDirectoryName(server.ProjectPath);
            if (string.IsNullOrEmpty(projectDirectory))
            {
                return $"❌ **Invalid server project path:** {server.ProjectPath}";
            }

            await _codeGenerator.AddToolToServerAsync(tool, projectDirectory);

            return $"""
                ✅ **Tool added successfully!**
                
                🔧 **Tool:** {toolName}
                📝 **Description:** {toolDescription}
                🎯 **Server:** {serverName}
                📁 **Location:** MasterMcpServer/{serverName}/
                
                🔄 **Next step:** Restart the server to use the new tool
                Use: `RestartServer {serverName}`
                """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding tool to server: {ServerName}", serverName);
            return $"❌ **Error adding tool:** {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("⚙️ Updates the VS Code MCP configuration to include all managed servers from the ecosystem.")]
    public async Task<string> UpdateVSCodeConfig()
    {
        try
        {
            await _configManager.BackupConfigAsync();
            
            var updatedCount = 0;
            foreach (var server in _servers)
            {
                await _configManager.UpdateServerConfigAsync(server);
                updatedCount++;
            }

            return $"""
                ✅ **VS Code configuration updated!**
                
                📄 **Updated servers:** {updatedCount}
                💾 **Backup created:** Yes
                📁 **Config location:** Documents/MasterMcpServer/.vscode/mcp.json
                
                🔄 **Restart VS Code** to apply changes
                🎯 **All servers ready** for use in chat!
                """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating VS Code configuration");
            return $"❌ **Error updating configuration:** {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("🎨 Generates server project templates based on common use cases for rapid development.")]
    public async Task<string> GenerateServerTemplate(
        [Description("Template type: database, api, files, email, weather, social, crypto, ai, gaming, chatbot")] string templateType,
        [Description("Custom name for the server")] string serverName = "")
    {
        try
        {
            if (string.IsNullOrEmpty(serverName))
            {
                serverName = $"{templateType.Substring(0, 1).ToUpper() + templateType.Substring(1)}Server";
            }

            var spec = templateType.ToLower() switch
            {
                "database" => CreateDatabaseServerSpec(serverName),
                "api" => CreateApiServerSpec(serverName),
                "files" => CreateFileServerSpec(serverName),
                "email" => CreateEmailServerSpec(serverName),
                "weather" => CreateWeatherServerSpec(serverName),
                "social" => CreateSocialServerSpec(serverName),
                "crypto" => CreateCryptoServerSpec(serverName),
                "ai" => CreateAiServerSpec(serverName),
                "gaming" => CreateGamingServerSpec(serverName),
                "chatbot" => CreateChatBotServerSpec(serverName),
                _ => throw new ArgumentException($"Unknown template type: {templateType}")
            };

            return await CreateMcpServer(spec.Name, spec.Description, spec.Type, 
                                       string.Join(",", spec.Tools.Select(t => t.Name)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating server template: {TemplateType}", templateType);
            return $"❌ **Error generating template:** {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("🛑 Emergency stop - Stops all running MCP servers managed by the Master server.")]
    public async Task<string> StopAllServers()
    {
        try
        {
            await _processManager.KillAllServersAsync();
            
            return """
                🛑 **All servers stopped successfully!**
                
                💡 Use `StartServer <name>` to restart individual servers
                📊 Use `GetServerStatus` to check current status
                📁 All projects remain in MasterMcpServer/ directory
                """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping all servers");
            return $"❌ **Error stopping servers:** {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("📁 Lists all servers in the MasterMcpServer ecosystem, including auto-discovered ones.")]
    public async Task<string> ListServers()
    {
        try
        {
            await DiscoverServersInDirectory();
            
            var availableServers = GetAvailableServers();
            
            if (!availableServers.Any())
            {
                return $"""
                    📭 **No servers found in ecosystem.**
                    
                    📁 **Master Path:** {_masterServerPath}
                    
                    🚀 **Create your first server:**
                    ```
                    CreateMcpServer Weather "Weather service" weather "GetWeather,GetForecast"
                    ```
                    """;
            }

            var result = new StringBuilder();
            result.AppendLine("📁 **MasterMcpServer Ecosystem**");
            result.AppendLine($"📂 **Location:** {_masterServerPath}");
            result.AppendLine();
            
            foreach (var serverInfo in availableServers)
            {
                result.AppendLine($"📦 **{serverInfo}**");
            }
            
            result.AppendLine();
            result.AppendLine($"📈 **Total:** {availableServers.Count} servers available");
            result.AppendLine("💡 Use `StartServer <name>` to run any server");
            
            return result.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing servers");
            return $"❌ **Error listing servers:** {ex.Message}";
        }
    }

    // Helper methods
    private async Task DiscoverServersInDirectory()
    {
        try
        {
            await Task.Run(() =>
            {
                if (!Directory.Exists(_masterServerPath))
                    return;

                var serverDirectories = Directory.GetDirectories(_masterServerPath);

                foreach (var serverDir in serverDirectories)
                {
                    var serverName = Path.GetFileName(serverDir);

                    // Skip hidden directories and the main config directory
                    if (serverName.StartsWith(".") || serverName == ".vscode")
                        continue;

                    var csprojPath = Path.Combine(serverDir, $"{serverName}.csproj");

                    if (File.Exists(csprojPath) && !_servers.Any(s => s.Name == serverName))
                    {
                        var server = new ServerDefinition
                        {
                            Name = serverName,
                            DisplayName = $"{serverName} MCP Server",
                            Description = $"Auto-discovered {serverName} server",
                            ProjectPath = csprojPath,
                            Status = ServerStatus.Stopped,
                            Tags = new List<string> { "auto-discovered" }
                        };

                        _servers.Add(server);
                        _logger.LogInformation("Auto-discovered server: {ServerName}", serverName);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering servers in directory");
        }
    }

    private List<string> GetAvailableServers()
    {
        var servers = new List<string>();
        
        if (Directory.Exists(_masterServerPath))
        {
            var directories = Directory.GetDirectories(_masterServerPath)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name) && !name.StartsWith("."))
                .ToList();
            
            servers.AddRange(directories.Where(name => name != null)!);
        }
        
        return servers;
    }

    private List<ToolSpec> ParseToolsFromString(string toolsString, string serverDescription)
    {
        var tools = new List<ToolSpec>();
        
        if (string.IsNullOrEmpty(toolsString))
        {
            // Generate default tool based on server description
            tools.Add(new ToolSpec
            {
                Name = "ProcessRequest",
                Description = $"Processes requests for {serverDescription}",
                Parameters = new List<ParameterSpec>
                {
                    new() { Name = "input", Type = "string", Description = "Input data to process" }
                }
            });
            return tools;
        }

        var toolNames = toolsString.Split(',', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var toolName in toolNames)
        {
            tools.Add(new ToolSpec
            {
                Name = toolName.Trim(),
                Description = $"Executes {toolName.Trim()} operation",
                Parameters = new List<ParameterSpec>
                {
                    new() { Name = "input", Type = "string", Description = "Input parameter" }
                }
            });
        }

        return tools;
    }

    private List<ParameterSpec> ParseParametersFromString(string parametersString)
    {
        var parameters = new List<ParameterSpec>();
        
        if (string.IsNullOrEmpty(parametersString))
        {
            parameters.Add(new ParameterSpec
            {
                Name = "input",
                Type = "string",
                Description = "Input parameter"
            });
            return parameters;
        }

        var paramParts = parametersString.Split(',', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var part in paramParts)
        {
            var components = part.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (components.Length >= 2)
            {
                parameters.Add(new ParameterSpec
                {
                    Name = components[0].Trim(),
                    Type = components[1].Trim(),
                    Description = components.Length > 2 ? components[2].Trim() : $"{components[0].Trim()} parameter"
                });
            }
        }

        return parameters;
    }

    private async Task<bool> BuildServerProject(string projectPath)
    {
        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "build",
                    WorkingDirectory = projectPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();
            
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // Template specifications
    private ServerSpec CreateDatabaseServerSpec(string name) => new()
    {
        Name = name,
        Description = "Database operations server with support for multiple database types",
        Type = "database",
        Tools = new List<ToolSpec>
        {
            new() { Name = "ExecuteQuery", Description = "Execute SQL query", Parameters = new() { new() { Name = "query", Type = "string", Description = "SQL query to execute" } } },
            new() { Name = "GetTableData", Description = "Get data from table", Parameters = new() { new() { Name = "tableName", Type = "string", Description = "Name of the table" } } },
            new() { Name = "CreateTable", Description = "Create new table", Parameters = new() { new() { Name = "tableSchema", Type = "string", Description = "Table schema definition" } } }
        }
    };

    private ServerSpec CreateApiServerSpec(string name) => new()
    {
        Name = name,
        Description = "HTTP API client server for making REST API calls",
        Type = "api",
        Tools = new List<ToolSpec>
        {
            new() { Name = "GetRequest", Description = "Make GET request", Parameters = new() { new() { Name = "url", Type = "string", Description = "API endpoint URL" } } },
            new() { Name = "PostRequest", Description = "Make POST request", Parameters = new() { new() { Name = "url", Type = "string", Description = "API endpoint" }, new() { Name = "data", Type = "string", Description = "Request body data" } } },
            new() { Name = "AuthenticateApi", Description = "Set API authentication", Parameters = new() { new() { Name = "apiKey", Type = "string", Description = "API key for authentication" } } }
        }
    };

    private ServerSpec CreateFileServerSpec(string name) => new()
    {
        Name = name,
        Description = "File system operations server for managing files and directories",
        Type = "files",
        Tools = new List<ToolSpec>
        {
            new() { Name = "ReadFile", Description = "Read file contents", Parameters = new() { new() { Name = "filePath", Type = "string", Description = "Path to the file" } } },
            new() { Name = "WriteFile", Description = "Write content to file", Parameters = new() { new() { Name = "filePath", Type = "string", Description = "File path" }, new() { Name = "content", Type = "string", Description = "Content to write" } } },
            new() { Name = "ListDirectory", Description = "List directory contents", Parameters = new() { new() { Name = "directoryPath", Type = "string", Description = "Directory path" } } }
        }
    };

    private ServerSpec CreateEmailServerSpec(string name) => new()
    {
        Name = name,
        Description = "Email operations server for sending and managing emails",
        Type = "email",
        Tools = new List<ToolSpec>
        {
            new() { Name = "SendEmail", Description = "Send email message", Parameters = new() { new() { Name = "to", Type = "string", Description = "Recipient email" }, new() { Name = "subject", Type = "string", Description = "Email subject" }, new() { Name = "body", Type = "string", Description = "Email body" } } },
            new() { Name = "SendBulkEmail", Description = "Send email to multiple recipients", Parameters = new() { new() { Name = "recipients", Type = "string", Description = "Comma-separated email list" }, new() { Name = "template", Type = "string", Description = "Email template" } } }
        }
    };

    private ServerSpec CreateWeatherServerSpec(string name) => new()
    {
        Name = name,
        Description = "Weather information server with forecasts and alerts",
        Type = "weather",
        Tools = new List<ToolSpec>
        {
            new() { Name = "GetCurrentWeather", Description = "Get current weather", Parameters = new() { new() { Name = "city", Type = "string", Description = "City name" } } },
            new() { Name = "GetForecast", Description = "Get weather forecast", Parameters = new() { new() { Name = "city", Type = "string", Description = "City name" }, new() { Name = "days", Type = "int", Description = "Number of days" } } },
            new() { Name = "GetWeatherAlerts", Description = "Get weather alerts", Parameters = new() { new() { Name = "region", Type = "string", Description = "Region for alerts" } } }
        }
    };

    private ServerSpec CreateSocialServerSpec(string name) => new()
    {
        Name = name,
        Description = "Social media operations server for multiple platforms",
        Type = "social",
        Tools = new List<ToolSpec>
        {
            new() { Name = "PostToTwitter", Description = "Post to Twitter", Parameters = new() { new() { Name = "message", Type = "string", Description = "Tweet content" } } },
            new() { Name = "PostToLinkedIn", Description = "Post to LinkedIn", Parameters = new() { new() { Name = "message", Type = "string", Description = "Post content" } } },
            new() { Name = "GetSocialMetrics", Description = "Get social media metrics", Parameters = new() { new() { Name = "platform", Type = "string", Description = "Social platform name" } } }
        }
    };

    private ServerSpec CreateCryptoServerSpec(string name) => new()
    {
        Name = name,
        Description = "Cryptocurrency operations server for prices and trading",
        Type = "crypto",
        Tools = new List<ToolSpec>
        {
            new() { Name = "GetCryptoPrice", Description = "Get cryptocurrency price", Parameters = new() { new() { Name = "symbol", Type = "string", Description = "Crypto symbol (BTC, ETH, etc.)" } } },
            new() { Name = "GetMarketData", Description = "Get market data", Parameters = new() { new() { Name = "symbols", Type = "string", Description = "Comma-separated symbols" } } },
            new() { Name = "CalculatePortfolio", Description = "Calculate portfolio value", Parameters = new() { new() { Name = "holdings", Type = "string", Description = "Portfolio holdings JSON" } } }
        }
    };

    private ServerSpec CreateAiServerSpec(string name) => new()
    {
        Name = name,
        Description = "AI operations server for machine learning and AI API integration",
        Type = "ai",
        Tools = new List<ToolSpec>
        {
            new() { Name = "GenerateText", Description = "Generate text using AI", Parameters = new() { new() { Name = "prompt", Type = "string", Description = "Text generation prompt" } } },
            new() { Name = "AnalyzeImage", Description = "Analyze image content", Parameters = new() { new() { Name = "imageUrl", Type = "string", Description = "URL to image" } } },
            new() { Name = "TranslateText", Description = "Translate text", Parameters = new() { new() { Name = "text", Type = "string", Description = "Text to translate" }, new() { Name = "targetLanguage", Type = "string", Description = "Target language" } } }
        }
    };

    private ServerSpec CreateGamingServerSpec(string name) => new()
    {
        Name = name,
        Description = "Gaming server for creating interactive games and entertainment",
        Type = "gaming",
        Tools = new List<ToolSpec>
        {
            new() { Name = "CreateGame", Description = "Create a new game", Parameters = new() { new() { Name = "gameType", Type = "string", Description = "Type of game (racing, puzzle, rpg)" } } },
            new() { Name = "StartGame", Description = "Start game session", Parameters = new() { new() { Name = "gameId", Type = "string", Description = "Game identifier" } } },
            new() { Name = "GetGameState", Description = "Get current game state", Parameters = new() { new() { Name = "gameId", Type = "string", Description = "Game identifier" } } },
            new() { Name = "MakeMove", Description = "Make a move in the game", Parameters = new() { new() { Name = "gameId", Type = "string", Description = "Game identifier" }, new() { Name = "move", Type = "string", Description = "Player move" } } }
        }
    };

    private ServerSpec CreateChatBotServerSpec(string name) => new()
    {
        Name = name,
        Description = "Intelligent chatbot server with conversation management",
        Type = "chatbot",
        Tools = new List<ToolSpec>
        {
            new() { Name = "SendMessage", Description = "Send message to chatbot", Parameters = new() { new() { Name = "message", Type = "string", Description = "User message" }, new() { Name = "userId", Type = "string", Description = "User identifier" } } },
            new() { Name = "GetConversationHistory", Description = "Get conversation history", Parameters = new() { new() { Name = "userId", Type = "string", Description = "User identifier" } } },
            new() { Name = "SetPersonality", Description = "Set chatbot personality", Parameters = new() { new() { Name = "personality", Type = "string", Description = "Personality description" } } },
            new() { Name = "ClearConversation", Description = "Clear conversation history", Parameters = new() { new() { Name = "userId", Type = "string", Description = "User identifier" } } }
        }
    };
}