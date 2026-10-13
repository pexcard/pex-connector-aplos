using System;

namespace AplosConnector.Common.Models;

public class AplosVendorCardOrderModel
{
    public int PEXBusinessAcctId { get; set; }
    public int AplosContactId { get; set; }
    public int PexVendorId { get; set; }
    public string CardName { get; set; }
    // Null when the order was attempted but its id never got recorded; such a card is never re-ordered.
    public int? CardOrderId { get; set; }
    public int? CardAcctId { get; set; }
    public DateTime? LinkedUtc { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
