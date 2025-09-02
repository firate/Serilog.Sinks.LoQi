using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using Serilog.Core;
using Serilog.Events;

namespace Serilog.Sinks.LoQi;

/// <summary>
/// Thread-safe Serilog sink that sends logs to LoQi system via UDP
/// Uses PeriodicTimer for safe batch processing and simple lock-based thread safety
/// </summary>
public sealed class LoQiSink : ILogEventSink, IDisposable
{
    private readonly LoQiSinkOptions _options;
    private readonly UdpClient _udpClient;
    private readonly PeriodicTimer? _periodicTimer;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ConcurrentQueue<LogEvent> _pendingLogs;
    private readonly SemaphoreSlim _batchSemaphore;
    
    // Simple disposal flag with lock-based thread safety
    private bool _isDisposed;
    private readonly object _disposeLock = new object();

    public LoQiSink(LoQiSinkOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _pendingLogs = new ConcurrentQueue<LogEvent>();
        _batchSemaphore = new SemaphoreSlim(1, 1);
        _cancellationTokenSource = new CancellationTokenSource();
        _isDisposed = false;

        try
        {
            _udpClient = new UdpClient();
            _udpClient.Connect(_options.Host, _options.Port);

            // Configure socket timeout
            _udpClient.Client.SendTimeout = _options.TimeoutMs;
            _udpClient.Client.ReceiveTimeout = _options.TimeoutMs;

            // Start periodic batch processing if batching is enabled
            if (_options.EnableBatching)
            {
                _periodicTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.BatchTimeoutMs));
                _ = Task.Run(PeriodicBatchProcessing, _cancellationTokenSource.Token);
            }
        }
        catch (Exception ex)
        {
            // Dispose what we already created
            _cancellationTokenSource?.Dispose();
            _periodicTimer?.Dispose();
            _udpClient?.Dispose();
            throw new InvalidOperationException($"Failed to initialize LoQi sink: {ex.Message}", ex);
        }
    }

    public void Emit(LogEvent logEvent)
    {
        // Thread-safe disposed check
        lock (_disposeLock)
        {
            if (_isDisposed || logEvent == null) 
                return;
        }

        if (_options.EnableBatching)
        {
            // Add to queue for batch processing
            _pendingLogs.Enqueue(logEvent);

            // Trigger immediate batch processing if queue is full
            if (_pendingLogs.Count >= _options.BatchSize)
            {
                _ = Task.Run(async () => await ProcessBatchAsync(), _cancellationTokenSource.Token);
            }
        }
        else
        {
            // Send immediately (synchronous for ordering)
            SendLogEventSync(logEvent);
        }
    }

    /// <summary>
    /// Periodic batch processing loop using PeriodicTimer
    /// Prevents overlapping executions and handles cancellation gracefully
    /// </summary>
    private async Task PeriodicBatchProcessing()
    {
        try
        {
            while (await _periodicTimer!.WaitForNextTickAsync(_cancellationTokenSource.Token))
            {
                // Check if disposed
                lock (_disposeLock)
                {
                    if (_isDisposed) 
                        break;
                }

                await ProcessBatchAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal - exit gracefully
            LogError("Periodic batch processing cancelled");
        }
        catch (Exception ex)
        {
            LogError($"Error in periodic batch processing: {ex.Message}");
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        // Skip if disposed or no pending logs
        bool shouldSkip;
        lock (_disposeLock)
        {
            shouldSkip = _isDisposed;
        }
        
        if (shouldSkip || _pendingLogs.IsEmpty) 
            return;

        await _batchSemaphore.WaitAsync(_cancellationTokenSource.Token);
        try
        {
            var batch = new List<LogEvent>();
            
            // Collect up to BatchSize logs (FIFO order)
            while (batch.Count < _options.BatchSize && _pendingLogs.TryDequeue(out var logEvent))
            {
                batch.Add(logEvent);
            }

            if (batch.Count == 0) return;

            // Send each log sequentially to preserve order
            foreach (var logEvent in batch)
            {
                // Check disposal state before each send
                lock (_disposeLock)
                {
                    if (_isDisposed) 
                        break;
                }
                    
                await SendLogEventAsync(logEvent);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal
        }
        finally
        {
            _batchSemaphore.Release();
        }
    }

    private async Task SendLogEventAsync(LogEvent logEvent)
    {
        // Check disposed state before processing
        lock (_disposeLock)
        {
            if (_isDisposed) return;
        }
        
        try
        {
            var message = BuildLogMessage(logEvent);
            await SendMessageWithRetryAsync(message);
        }
        catch (Exception ex)
        {
            LogError($"Failed to send log event: {ex.Message}");
        }
    }

    private void SendLogEventSync(LogEvent logEvent)
    {
        // Check disposed state before processing
        lock (_disposeLock)
        {
            if (_isDisposed) return;
        }
        
        try
        {
            var message = BuildLogMessage(logEvent);
            SendMessageWithRetrySync(message);
        }
        catch (Exception ex)
        {
            LogError($"Failed to send log event: {ex.Message}");
        }
    }

    private string BuildLogMessage(LogEvent logEvent)
    {
        // Render the log message - LoQi will handle all parsing
        var message = logEvent.RenderMessage();
        
        // Include exception details if present
        if (logEvent.Exception != null)
        {
            message += Environment.NewLine + logEvent?.Exception?.ToString();
        }

        return message;
    }

    private async Task SendMessageWithRetryAsync(string message)
    {
        for (var attempt = 0; attempt < _options.MaxRetries; attempt++)
        {
            if (IsDisposed()) return;
            
            try
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                await _udpClient.SendAsync(bytes);
                return; // Success
            }
            catch (Exception ex)
            {
                if (attempt == _options.MaxRetries - 1)
                {
                    LogError($"Failed to send after {_options.MaxRetries} retries: {ex.Message}");
                    return;
                }

                // Exponential backoff
                await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)));
            }
        }
    }

    private void SendMessageWithRetrySync(string message)
    {
        for (var attempt = 0; attempt < _options.MaxRetries; attempt++)
        {
            if (IsDisposed()) return;
            
            try
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                _udpClient.Send(bytes, bytes.Length);
                return; // Success
            }
            catch (Exception ex)
            {
                if (attempt == _options.MaxRetries - 1)
                {
                    LogError($"Failed to send after {_options.MaxRetries} retries: {ex.Message}");
                    return;
                }

                // Exponential backoff
                Thread.Sleep(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)));
            }
        }
    }

    /// <summary>
    /// Thread-safe disposal check with lock
    /// </summary>
    private bool IsDisposed()
    {
        lock (_disposeLock)
        {
            return _isDisposed;
        }
    }

    private async ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
        }

        try
        {
            _cancellationTokenSource?.Cancel();

            // Kalan batch’i gönder (2 saniye timeout)
            if (_options.EnableBatching && !_pendingLogs.IsEmpty)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await ProcessBatchAsync(cts.Token);
                }
                catch (Exception ex)
                {
                    LogError($"Error processing final batch: {ex.Message}");
                }
            }

            _periodicTimer?.Dispose();

            // Batch semaforunun tamamlanmasını bekle (3 saniye timeout)
            try
            {
                if (_batchSemaphore is not null)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _batchSemaphore.WaitAsync(cts.Token);
                }
            }
            catch (Exception ex)
            {
                LogError($"Error waiting for batch completion: {ex.Message}");
            }

            _udpClient?.Dispose();
        }
        catch (Exception ex)
        {
            LogError($"Error during disposal: {ex.Message}");
        }
        finally
        {
            _batchSemaphore?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }

    // Eğer Serilog sink kontratı gereği IDisposable zorunlu ise:
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static void LogError(string message)
    {
        // Log to system diagnostics - don't throw from logging sink
        System.Diagnostics.Debug.WriteLine($"LoQi Sink: {message}");
    }
}