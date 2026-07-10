using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Reflection;

namespace TownSuite.CodeSigning.Service
{
    /// <summary>
    /// Readiness health check with two signals:
    ///
    /// 1. A signing canary: signs a throwaway copy of a real PE file with the configured
    ///    signtool settings. If signing fails (for example
    ///    "SignTool Error: No private key is available.") the service is reported unhealthy.
    ///    This result is cached for a short interval so frequent probes do not hammer signtool
    ///    and the timestamp server.
    ///
    /// 2. A queue-drain check: batch signing runs asynchronously on <see cref="BackgroundQueue"/>,
    ///    so signtool passing the canary does not prove queued jobs are being processed. If the
    ///    queue has pending jobs but the worker has not completed one within the configured stall
    ///    window, the service is reported unhealthy. This signal is cheap and evaluated live.
    /// </summary>
    public class SigningHealthCheck : IHealthCheck
    {
        private readonly Settings _settings;
        private readonly ILogger _logger;
        private readonly BackgroundQueue _queue;

        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;
        private HealthCheckResult _cachedResult;

        public SigningHealthCheck(Settings settings, ILogger logger)
            : this(settings, logger, BackgroundQueue.Instance)
        {
        }

        public SigningHealthCheck(Settings settings, ILogger logger, BackgroundQueue queue)
        {
            _settings = settings;
            _logger = logger;
            _queue = queue;
        }

        private TimeSpan CacheDuration =>
            TimeSpan.FromMilliseconds(_settings.HealthCheckCacheInMs > 0 ? _settings.HealthCheckCacheInMs : 30000);

        private TimeSpan QueueStallWindow =>
            TimeSpan.FromMilliseconds(_settings.HealthCheckQueueStallInMs > 0 ? _settings.HealthCheckQueueStallInMs : 60000);

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            // Cheap, live signal first: is the async signing queue actually draining?
            var queueResult = CheckQueueDraining();
            if (queueResult.Status == HealthStatus.Unhealthy)
            {
                return queueResult;
            }

            var now = DateTimeOffset.UtcNow;
            if (_cachedResult.Status != HealthStatus.Unhealthy && now - _cachedAt < CacheDuration
                && _cachedAt != DateTimeOffset.MinValue)
            {
                return _cachedResult;
            }

            await _gate.WaitAsync(cancellationToken);
            try
            {
                // Re-check the cache after acquiring the gate in case another probe just refreshed it.
                now = DateTimeOffset.UtcNow;
                if (_cachedResult.Status != HealthStatus.Unhealthy && now - _cachedAt < CacheDuration
                    && _cachedAt != DateTimeOffset.MinValue)
                {
                    return _cachedResult;
                }

                _cachedResult = await RunSignCanaryAsync();
                _cachedAt = DateTimeOffset.UtcNow;
                return _cachedResult;
            }
            finally
            {
                _gate.Release();
            }
        }

        private HealthCheckResult CheckQueueDraining()
        {
            var queue = _queue;
            int depth = queue.QueueDepth;
            if (depth == 0)
            {
                // Nothing waiting; the queue is fully drained regardless of timing.
                return HealthCheckResult.Healthy("Signing queue is drained.");
            }

            var lastActivity = queue.LastActivityUtc;
            if (lastActivity.HasValue)
            {
                var idle = DateTimeOffset.UtcNow - lastActivity.Value;
                if (idle > QueueStallWindow)
                {
                    var message =
                        $"Signing queue is not draining: {depth} job(s) pending, {queue.InFlight} in flight, " +
                        $"no job completed for {idle.TotalSeconds:n0}s (stall window {QueueStallWindow.TotalSeconds:n0}s).";
                    _logger.LogError(message);
                    return HealthCheckResult.Unhealthy(message);
                }
            }

            return HealthCheckResult.Healthy($"Signing queue is draining ({depth} pending).");
        }

        private async Task<HealthCheckResult> RunSignCanaryAsync()
        {
            var workingFolder = new DirectoryInfo(Path.Combine(BatchedSigning.GetTempFolder(), "healthcheck"));
            var canaryPath = Path.Combine(workingFolder.FullName, $"healthcheck-{Guid.NewGuid()}.dll");
            try
            {
                workingFolder.CreateIfNotExists();

                // Sign a throwaway copy of a real PE file (this assembly) so signtool exercises
                // the full signing path including access to the private key.
                var source = Assembly.GetExecutingAssembly().Location;
                File.Copy(source, canaryPath, true);

                var signer = new Signer(_settings, _logger);
                var result = await signer.SignAsync(workingFolder.FullName, new[] { canaryPath });

                if (result.IsSigned)
                {
                    return HealthCheckResult.Healthy("Code signing is operational.");
                }

                _logger.LogError($"Readiness signing canary failed: {result.Message}");
                return HealthCheckResult.Unhealthy($"Code signing is failing. {result.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Readiness signing canary threw an exception");
                return HealthCheckResult.Unhealthy("Code signing is failing.", ex);
            }
            finally
            {
                try
                {
                    if (File.Exists(canaryPath))
                    {
                        File.Delete(canaryPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogInformation($"Failed to cleanup health check canary {canaryPath}: {ex.Message}");
                }
            }
        }
    }
}
