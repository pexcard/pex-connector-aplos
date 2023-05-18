using System.Collections.Generic;
using System.Linq;

namespace AplosConnector.Common.VendorCards
{
    public class VendorCardsOrdered
    {
        public VendorCardsOrdered()
        {
        }

        public VendorCardsOrdered(int id)
        {
            Id = id;
        }

        public VendorCardsOrdered(int id, IEnumerable<VendorCardOrdered> cardOrders)
        {
            Id = id;
            CardOrders = cardOrders?.ToList() ?? new List<VendorCardOrdered>();
        }

        public int Id { get; set; }

        public List<VendorCardOrdered> CardOrders { get; set; } = new List<VendorCardOrdered>();
    }
}
