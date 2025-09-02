namespace Serilog.Sinks.LoQi;

/// <summary>
/// Configuration options for LoQi sink
/// </summary>
public class LoQiSinkOptions
{
    /// <summary>
    /// LoQi server hostname or IP address
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// LoQi UDP port (default: 10080)
    /// </summary>
    public int Port { get; set; } = 10080;

    /// <summary>
    /// Connection timeout in milliseconds
    /// </summary>
    public int TimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Enable batching for better throughput.
    /// True: Async batched sending (higher throughput, slight latency)
    /// False: Synchronous immediate sending (lower latency, blocks thread)
    /// Note: Log ordering is preserved in BOTH modes
    /// </summary>
    public bool EnableBatching { get; set; } = true;

    /// <summary>
    /// Batch size for batching mode
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Batch timeout in milliseconds
    /// </summary>
    public int BatchTimeoutMs { get; set; } = 2000;

    /// <summary>
    /// Maximum number of retries on failure
    /// </summary>
    public int MaxRetries { get; set; } = 3;
}

