
### Usage Examples:

#### Basic usage - batched mode (high throughput, preserved ordering)
```
Log.Logger = new LoggerConfiguration()
        .WriteTo.LoQi("192.168.1.100", 10080)
    .CreateLogger();
```

#### Immediate mode - each log sent immediately (lower latency)
```
Log.Logger = new LoggerConfiguration()
        .WriteTo.LoQi(options =>
    {
        options.Host = "192.168.1.100";
        options.Port = 10080;
        options.EnableBatching = false; // Synchronous sending
    })
    .CreateLogger();
```

#### Performance optimized configuration
```
Log.Logger = new LoggerConfiguration()
        .WriteTo.LoQi(options =>
    {
        options.Host = "localhost";
        options.Port = 10080;
        options.EnableBatching = true;
        options.BatchSize = 100; // Larger batches
        options.BatchTimeoutMs = 500; // Faster flushes
        options.MaxRetries = 3;
        options.TimeoutMs = 5000;
    })
    .CreateLogger();
```


#### Thread-safe logging - multiple threads can log safely
```
Log.Information("User {UserId} logged in", userId);
Log.Error(exception, "Order processing failed: {OrderId}", orderId);
```

#### Safe disposal - can be called multiple times from different threads
```
Log.CloseAndFlush();
```

#### Configuration via appsettings.json
```
{
    "Serilog": {
        "WriteTo": [
            {
                "Name": "LoQi",
                "Args": {
                    "host": "localhost",
                    "port": 10080,
                    "enableBatching": true
                }
            }
        ]
    }
}
```