using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Reflection;

namespace TownSuite.CodeSigning.Service
{
    /// <summary>
    /// Readiness health check with two signals. Both are answered from memory so probes return
    /// immediately: nothing on this path waits on signtool, the filesystem or the network.
    ///
    /// 1. A signing canary: signs a throwaway copy of a real PE file with the configured
    ///    signtool settings, minus timestamping. If signing fails (for example
    ///    "SignTool Error: No private key is available.") the service is reported unhealthy.
    ///    signtool shells out and can take seconds, so it is never run inside a probe request.
    ///    <see cref="SigningCanaryService"/> calls <see cref="RefreshAsync"/> on a timer and
    ///    <see cref="CheckHealthAsync"/> only reads the last published result.
    ///
    ///    The canary does not timestamp. The timestamp authority is a third party subject to rate
    ///    limiting and outages, and a canary running every <see cref="RefreshInterval"/> is a
    ///    steady source of requests against it; a throttled authority made signtool stall past
    ///    <see cref="Settings.SigntoolTimeoutInMs"/> and failed readiness for a service whose
    ///    signing was in fact fine. What the canary is here to prove - signtool runs and the
    ///    private key is reachable - needs no timestamp. Real signing requests still timestamp,
    ///    so a broken timestamp authority surfaces there, on the requests it actually breaks.
    ///
    ///    A single failed run does not fail readiness either: failures have to repeat
    ///    <see cref="Settings.HealthCheckFailureThreshold"/> times in a row before the canary
    ///    reports Unhealthy, so one blip degrades rather than pulling the instance from rotation.
    ///
    /// 2. A queue-drain check: batch signing runs asynchronously on <see cref="BackgroundQueue"/>,
    ///    so signtool passing the canary does not prove queued jobs are being processed. If the
    ///    queue has pending jobs but the worker has not completed one within the configured stall
    ///    window, the service is reported unhealthy. This signal is cheap and evaluated live.
    /// </summary>
    public class SigningHealthCheck : IHealthCheck
    {
        /// <summary>
        /// A canary result older than this many refresh intervals is treated as stale: it means the
        /// background refresher stopped running, so the last result no longer describes reality.
        /// </summary>
        private const int StaleAfterIntervals = 3;

        /// <summary>
        /// Working folder for the canary, directly under the batch temp folder. Unlike a batch job
        /// folder this one is long lived and reused by every canary run, so <see cref="CleanerService"/>
        /// must leave it alone and <see cref="StatusService"/> must not count it as a pending job.
        /// </summary>
        public const string CanaryFolderName = "healthcheck";

        public static string CanaryFolderPath =>
            Path.Combine(BatchedSigning.GetTempFolder(), CanaryFolderName);

        /// <summary>
        /// Default for <see cref="Settings.HealthCheckFailureThreshold"/>.
        /// </summary>
        private const int DefaultFailureThreshold = 3;

        private readonly Settings _settings;
        private readonly ILogger _logger;
        private readonly BackgroundQueue _queue;

        // Resolved once: the regex strip in SignToolArguments should not run on every refresh.
        private readonly string _canarySignToolOptions;

        // Only ever touched inside _refreshGate, so a plain field is enough.
        private int _consecutiveFailures;

        // Collapses overlapping refreshes; never waited on by a request thread.
        private readonly SemaphoreSlim _refreshGate = new SemaphoreSlim(1, 1);

        // Single reference swap so readers always see a result and its timestamp together.
        private volatile CanarySnapshot _snapshot;

        private sealed record CanarySnapshot(HealthCheckResult Result, DateTimeOffset CompletedAt);

        public SigningHealthCheck(Settings settings, ILogger logger)
            : this(settings, logger, BackgroundQueue.Instance)
        {
        }

        public SigningHealthCheck(Settings settings, ILogger logger, BackgroundQueue queue)
        {
            _settings = settings;
            _logger = logger;
            _queue = queue;
            _canarySignToolOptions = !string.IsNullOrWhiteSpace(settings.CanarySignToolOptions)
                ? settings.CanarySignToolOptions
                : SignToolArguments.StripTimestampOptions(settings.SignToolOptions);
        }

        /// <summary>
        /// How often the background canary re-runs signtool.
        /// </summary>
        public TimeSpan RefreshInterval =>
            TimeSpan.FromMilliseconds(_settings.HealthCheckCacheInMs > 0 ? _settings.HealthCheckCacheInMs : 30000);

        private TimeSpan StaleAfter => RefreshInterval * StaleAfterIntervals;

        /// <summary>
        /// Consecutive failed canary runs required before readiness reports Unhealthy.
        /// </summary>
        private int FailureThreshold =>
            _settings.HealthCheckFailureThreshold > 0 ? _settings.HealthCheckFailureThreshold : DefaultFailureThreshold;

        private TimeSpan QueueStallWindow =>
            TimeSpan.FromMilliseconds(_settings.HealthCheckQueueStallInMs > 0 ? _settings.HealthCheckQueueStallInMs : 60000);

        /// <summary>
        /// Returns the current readiness verdict without blocking. Deliberately synchronous work
        /// only — see the class remarks.
        /// </summary>
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var queueResult = CheckQueueDraining();
            if (queueResult.Status == HealthStatus.Unhealthy)
            {
                return Task.FromResult(queueResult);
            }

            return Task.FromResult(ReadCanaryResult());
        }

        /// <summary>
        /// Runs the signing canary and publishes the result for <see cref="CheckHealthAsync"/> to
        /// read. Called on a timer by <see cref="SigningCanaryService"/>, and directly by tests.
        /// If a run is already in progress this returns immediately rather than queueing behind it.
        /// </summary>
        public async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            if (!await _refreshGate.WaitAsync(0, cancellationToken))
            {
                return;
            }

            try
            {
                var result = await RunSignCanaryAsync();
                _snapshot = new CanarySnapshot(ApplyFailureThreshold(result), DateTimeOffset.UtcNow);
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        /// <summary>
        /// Holds a failing canary at Degraded until it has failed <see cref="FailureThreshold"/>
        /// times in a row. Signing is a shell-out with real-world flakiness - signtool can time
        /// out, a file can be briefly locked - and one bad run is not evidence the service is
        /// broken. Degraded still returns HTTP 200, so the instance stays in rotation while the
        /// streak builds, and the published timestamp still moves so the staleness check in
        /// <see cref="ReadCanaryResult"/> does not fire on top of it.
        /// </summary>
        private HealthCheckResult ApplyFailureThreshold(HealthCheckResult result)
        {
            if (result.Status != HealthStatus.Unhealthy)
            {
                _consecutiveFailures = 0;
                return result;
            }

            _consecutiveFailures++;

            if (_consecutiveFailures >= FailureThreshold)
            {
                _logger.LogError(
                    $"Readiness signing canary has failed {_consecutiveFailures} time(s) in a row: {result.Description}");
                return result;
            }

            var message =
                $"Code signing canary failed {_consecutiveFailures} of {FailureThreshold} consecutive runs " +
                $"needed to report unhealthy. Last failure: {result.Description}";
            _logger.LogWarning(message);
            return HealthCheckResult.Degraded(message);
        }

        private HealthCheckResult ReadCanaryResult()
        {
            var snapshot = _snapshot;
            if (snapshot == null)
            {
                // Degraded rather than Unhealthy: a probe that lands before the first canary run
                // finishes should not fail the container during startup. Degraded still returns 200.
                return HealthCheckResult.Degraded("Code signing canary has not completed a run yet.");
            }

            // A failing canary stays failing until a later run says otherwise.
            if (snapshot.Result.Status == HealthStatus.Unhealthy)
            {
                return snapshot.Result;
            }

            var age = DateTimeOffset.UtcNow - snapshot.CompletedAt;
            if (age > StaleAfter)
            {
                var message =
                    $"Code signing canary result is stale: {age.TotalSeconds:n0}s old, " +
                    $"refresh interval {RefreshInterval.TotalSeconds:n0}s. " +
                    $"Last known result: {snapshot.Result.Description}";
                _logger.LogError(message);
                return HealthCheckResult.Degraded(message);
            }

            return snapshot.Result;
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
            var workingFolder = new DirectoryInfo(CanaryFolderPath);
            var canaryPath = Path.Combine(workingFolder.FullName, $"healthcheck-{Guid.NewGuid()}.dll");
            try
            {
                workingFolder.CreateIfNotExists();

                // Sign a throwaway copy of a real PE file (this assembly) so signtool exercises
                // the full signing path including access to the private key.
                var source = Assembly.GetExecutingAssembly().Location;
                File.Copy(source, canaryPath, true);

                var signer = new Signer(_settings, _logger, _canarySignToolOptions);
                var result = await signer.SignAsync(workingFolder.FullName, new[] { canaryPath });

                if (result.IsSigned)
                {
                    return HealthCheckResult.Healthy("Code signing is operational.");
                }

                // Logged by ApplyFailureThreshold, which knows whether this is a blip or a streak.
                return HealthCheckResult.Unhealthy($"Code signing is failing. {result.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Readiness signing canary threw an exception");
                return HealthCheckResult.Unhealthy($"Code signing is failing. {ex.Message}", ex);
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
