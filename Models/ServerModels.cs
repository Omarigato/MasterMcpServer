namespace MasterMcpServer.Models;

public class ServerDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public List<string> Arguments { get; set; } = new();
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
    public ServerStatus Status { get; set; } = ServerStatus.Stopped;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastStarted { get; set; }
    public int ProcessId { get; set; }
    public string Version { get; set; } = "1.0.0";
    public List<string> Tags { get; set; } = new();
}

public enum ServerStatus
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Error,
    Unknown
}

public class ServerSpec
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "generic"; // weather, database, api, files, etc.
    public List<ToolSpec> Tools { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
    public Dictionary<string, string> Configuration { get; set; } = new();
}

public class ToolSpec
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<ParameterSpec> Parameters { get; set; } = new();
    public string ReturnType { get; set; } = "string";
    public string Implementation { get; set; } = string.Empty;
}

public class ParameterSpec
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public string Description { get; set; } = string.Empty;
    public bool Required { get; set; } = true;
    public string DefaultValue { get; set; } = string.Empty;
}

public class ServerMetrics
{
    public string ServerName { get; set; } = string.Empty;
    public double CpuUsage { get; set; }
    public double MemoryUsage { get; set; }
    public int ThreadCount { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public TimeSpan Uptime { get; set; }
    public int RequestCount { get; set; }
    public int ErrorCount { get; set; }
}

public class McpConfiguration
{
    public Dictionary<string, ServerConfig> Servers { get; set; } = new();
    public List<object> Inputs { get; set; } = new();
}

public class ServerConfig
{
    public string Type { get; set; } = "stdio";
    public string Command { get; set; } = string.Empty;
    public List<string> Args { get; set; } = new();
    public Dictionary<string, string> Env { get; set; } = new();
    public string Cwd { get; set; } = string.Empty;
}