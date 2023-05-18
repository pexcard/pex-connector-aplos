using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AplosConnector.Common.Models;

namespace AplosConnector.Common.VendorCards
{
    public interface IVendorCardService
    {
        Task<VendorCardsOrdered> OrderVendorCardsAsync(Pex2AplosMappingModel mapping, IEnumerable<VendorCardOrder> cardOrders, CancellationToken cancelToken = default);
    }
}