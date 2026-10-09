using AplosConnector.Common.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Common.Storage
{
    public interface IAplosBillMappingStorage
    {
        Task<List<AplosBillMappingModel>> GetByBusinessAsync(int pexBusinessAcctId, CancellationToken cancellationToken);

        Task AddAsync(AplosBillMappingModel model, CancellationToken cancellationToken);

        Task MarkPaidAsync(AplosBillMappingModel model, DateTime paidUtc, CancellationToken cancellationToken);

        Task MarkFailedAsync(AplosBillMappingModel model, DateTime failedUtc, CancellationToken cancellationToken);

        Task MarkAwaitingChargeAsync(AplosBillMappingModel model, DateTime awaitingSinceUtc, CancellationToken cancellationToken);
    }
}
