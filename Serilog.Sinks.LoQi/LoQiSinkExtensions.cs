using Serilog.Configuration;

namespace Serilog.Sinks.LoQi;

/// <summary>
/// Extension methods for configuring LoQi sink
/// </summary>
public static class LoQiSinkExtensions
{
    /// <summary>
    /// Writes log events to LoQi system via UDP
    /// </summary>
    /// <param name="sinkConfiguration">Logger sink configuration</param>
    /// <param name="host">LoQi server hostname or IP</param>
    /// <param name="port">LoQi UDP port (default: 10080)</param>
    /// <returns>Configuration object allowing method chaining</returns>
    public static LoggerConfiguration LoQi(
        this LoggerSinkConfiguration sinkConfiguration,
        string host = "localhost",
        int port = 10080)
    {
        var options = new LoQiSinkOptions
        {
            Host = host,
            Port = port
        };

        return sinkConfiguration.Sink(new LoQiSink(options));
    }

    /// <summary>
    /// Writes log events to LoQi system with advanced configuration
    /// </summary>
    /// <param name="sinkConfiguration">Logger sink configuration</param>
    /// <param name="options">LoQi sink options</param>
    /// <returns>Configuration object allowing method chaining</returns>
    public static LoggerConfiguration LoQi(
        this LoggerSinkConfiguration sinkConfiguration,
        LoQiSinkOptions options)
    {
        return sinkConfiguration.Sink(new LoQiSink(options));
    }

    /// <summary>
    /// Writes log events to LoQi system with configuration action
    /// </summary>
    /// <param name="sinkConfiguration">Logger sink configuration</param>
    /// <param name="configure">Configuration action</param>
    /// <returns>Configuration object allowing method chaining</returns>
    public static LoggerConfiguration LoQi(
        this LoggerSinkConfiguration sinkConfiguration,
        Action<LoQiSinkOptions> configure)
    {
        var options = new LoQiSinkOptions();
        configure(options);
        return sinkConfiguration.Sink(new LoQiSink(options));
    }
}