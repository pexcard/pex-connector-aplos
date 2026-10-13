using AplosConnector.Common.Models;
using Azure;
using Azure.Data.Tables;
using System;

namespace AplosConnector.Common.Entities;

public class AplosVendorCardOrderEntity : ITableEntity
{
    public AplosVendorCardOrderEntity()
    {
        CreatedUtc = DateTime.UtcNow;
    }

    public AplosVendorCardOrderEntity(AplosVendorCardOrderModel model)
    {
        PartitionKey = model.PEXBusinessAcctId.ToString();
        RowKey = GetRowKey(model);
        PEXBusinessAcctId = model.PEXBusinessAcctId;
        AplosContactId = model.AplosContactId;
        PexVendorId = model.PexVendorId;
        CardName = model.CardName;
        CardOrderId = model.CardOrderId;
        CardAcctId = model.CardAcctId;
        LinkedUtc = model.LinkedUtc?.ToUniversalTime();
        CreatedUtc = model.CreatedUtc.ToUniversalTime();
    }

    public int PEXBusinessAcctId { get; set; }
    public int AplosContactId { get; set; }
    public int PexVendorId { get; set; }
    public string CardName { get; set; }
    public int? CardOrderId { get; set; }
    public int? CardAcctId { get; set; }
    public DateTime? LinkedUtc { get; set; }
    public DateTime CreatedUtc { get; set; }

    // Keyed by vendor too: a contact whose vendor is recreated needs a row of its own.
    public static string GetRowKey(AplosVendorCardOrderModel model) => $"{model.AplosContactId}-{model.PexVendorId}";

    public AplosVendorCardOrderModel ToModel() => new()
    {
        PEXBusinessAcctId = PEXBusinessAcctId,
        AplosContactId = AplosContactId,
        PexVendorId = PexVendorId,
        CardName = CardName,
        CardOrderId = CardOrderId,
        CardAcctId = CardAcctId,
        LinkedUtc = LinkedUtc,
        CreatedUtc = CreatedUtc
    };

    public string PartitionKey { get; set; }
    public string RowKey { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}
