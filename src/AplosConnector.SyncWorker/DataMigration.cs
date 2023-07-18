using System;
using System.Threading;
using System.Threading.Tasks;
using AplosConnector.Common.Entities;
using AplosConnector.Common.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace AplosConnector.SyncWorker
{
    public class DataMigration
    {
        private readonly SyncResultStorage _resultStorage;
        private readonly SyncHistoryStorage _historyStorage; 
        private readonly ILogger<DataMigration> _log;

        public DataMigration(SyncResultStorage resultStorage, SyncHistoryStorage historyStorage, ILogger<DataMigration> log)
        {
            _resultStorage = resultStorage;
            _historyStorage = historyStorage;
            _log = log;
        }

        [FunctionName("DataMigration")]
        public async Task Run([HttpTrigger(AuthorizationLevel.Function, Route = "ticks/{ticks:long}")] HttpRequest request,
            long ticks, CancellationToken cancellationToken)
        {
            _log.LogInformation($"Ticks: {ticks}");

            var watch = System.Diagnostics.Stopwatch.StartNew();

            await MigrateSyncResults(ticks, cancellationToken);
            watch.Stop();
            _log.LogInformation($"Execution time: {watch.ElapsedMilliseconds} ms");
        }

        private async Task MigrateSyncResults(long ticks, CancellationToken cancellationToken)
        {
            _log.LogInformation("Sync Results Migration started.");

            var copyToDate = new DateTime(ticks, DateTimeKind.Utc);

            var syncResultsEntitiesToCopy = _resultStorage.TableClient.QueryAsync<SyncResultEntity>(r => r.CreatedUtc < copyToDate, 500, null, cancellationToken);

            var totalCount = 0;
            var createdCount = 0;
            var errorCount = 0;

            await foreach (var page in syncResultsEntitiesToCopy.AsPages().WithCancellation(cancellationToken))
            {
                foreach (var syncResultEntity in page.Values)
                {
                    totalCount++;

                    try
                    {
                        await _historyStorage.CreateAsync(syncResultEntity.ToModel(), cancellationToken);
                        createdCount++;
                    }
                    catch (Exception e)
                    {
                        _log.LogError($"Failed to add entity with RowKey: '{syncResultEntity.RowKey}'");
                        errorCount++;
                    }
                }
            }

            _log.LogInformation($"Sync Results Migration completed. Total records: {totalCount}, Created records: {createdCount}, Errors: {errorCount}");
        }
    }
}
