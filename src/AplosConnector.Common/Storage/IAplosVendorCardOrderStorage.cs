using AplosConnector.Common.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Common.Storage;

public interface IAplosVendorCardOrderStorage
{
    Task<List<AplosVendorCardOrderModel>> GetByBusinessAsync(int pexBusinessAcctId, CancellationToken cancellationToken);

    Task AddAsync(AplosVendorCardOrderModel model, CancellationToken cancellationToken);

    Task SetOrderIdAsync(AplosVendorCardOrderModel model, int cardOrderId, CancellationToken cancellationToken);

    Task MarkLinkedAsync(AplosVendorCardOrderModel model, int cardAcctId, DateTime linkedUtc, CancellationToken cancellationToken);
}
