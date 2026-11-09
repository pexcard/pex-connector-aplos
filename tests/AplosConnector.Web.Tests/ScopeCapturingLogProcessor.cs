using System.Collections.Concurrent;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace AplosConnector.Web.Tests
{
    /// <summary>
    /// OpenTelemetry log processor that records every scope value on each exported record, read the same way the
    /// OTLP exporter reads them.
    /// </summary>
    internal sealed class ScopeCapturingLogProcessor : BaseProcessor<LogRecord>
    {
        /// <summary>
        /// Gets every scope value seen on a record, rendered as <c>key=value</c>.
        /// </summary>
        public ConcurrentBag<string> ScopeValues { get; } = new();

        /// <summary>
        /// Gets the number of records the processor has seen.
        /// </summary>
        public int RecordCount => _recordCount;

        /// <summary>
        /// Backing counter for <see cref="RecordCount"/>, updated atomically.
        /// </summary>
        private int _recordCount;

        /// <summary>
        /// Records the scope values of a finished log record.
        /// </summary>
        /// <param name="data">
        /// The finished log record.
        /// </param>
        public override void OnEnd(LogRecord data)
        {
            System.Threading.Interlocked.Increment(ref _recordCount);
            data.ForEachScope((scope, values) =>
            {
                foreach (var pair in scope)
                {
                    values.Add($"{pair.Key}={pair.Value}");
                }
            }, ScopeValues);
        }
    }
}
