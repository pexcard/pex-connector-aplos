using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AplosConnector.Common.Models;

namespace AplosConnector.Common.VendorCards
{
    public interface IVendorCardRepository
    {
        Task<VendorCardsOrdered> GetVendorCardsOrderedAsync(Pex2AplosMappingModel mapping, int orderId, CancellationToken cancelToken = default);

        Task<List<VendorCardsOrdered>> GetAllVendorCardsOrderedAsync(Pex2AplosMappingModel mapping, CancellationToken cancelToken = default);

        Task SaveVendorCardsOrderedAsync(Pex2AplosMappingModel mapping, VendorCardsOrdered vendorCardsOrdered, CancellationToken cancelToken = default);
    }
}