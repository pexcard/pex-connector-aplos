using System;

namespace AplosConnector.Common.VendorCards
{
    public class VendorCardOrder
    {
        public VendorCardOrder()
        {
        }

        public VendorCardOrder(string id, string name, bool autoFunding, double? initialFunding = default, int? groupId = default)
        {
            Id = id;
            Name = name;
            AutoFunding = autoFunding;
            InitialFunding = initialFunding;
            GroupId = groupId;
        }

        public string Id { get; set; }

        public string Name { get; set; }

        public bool AutoFunding { get; set; }

        public double? InitialFunding { get; set; }

        public int? GroupId { get; set; }
    }

    public class VendorCardOrdered : VendorCardOrder
    {
        public VendorCardOrdered()
        {
        }

        public VendorCardOrdered(int orderId, string id, string name, bool autoFunding, double? initialFunding = default, int? groupId = default, int? accountId = default, DateTime? orderDate = default)
            : base(id, name, autoFunding, initialFunding, groupId)
        {
            OrderId = orderId;
            AccountId = accountId;
            OrderDate = orderDate;
        }

        public int OrderId { get; set; }

        public int? AccountId { get; set; }

        public Uri AccountUrl => AccountId.HasValue ? new Uri($"https://dashboard.pexcard.com/cards/{AccountId}/detail") : default;

        public string Status { get; set; }

        public string Error { get; set; }

        public DateTime? OrderDate { get; set; }

        public static VendorCardOrdered FromOrder(VendorCardOrder order, int orderId, string status = default, string error = default)
        {
            return new VendorCardOrdered(orderId, order.Id, order.Name, order.AutoFunding, order.InitialFunding, order.GroupId)
            {
                Status = status,
                Error = error
            };
        }
    }
}
