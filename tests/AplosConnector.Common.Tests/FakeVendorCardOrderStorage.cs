using AplosConnector.Common.Models;
using AplosConnector.Common.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Common.Tests;

// Mirrors AplosVendorCardOrderStorage: rows are copied like a table round trip, and the caller's model changes
// only after a write succeeds.
public sealed class FakeVendorCardOrderStorage : IAplosVendorCardOrderStorage
{
    public readonly Dictionary<(int ContactId, int VendorId), AplosVendorCardOrderModel> Rows = [];

    public bool FailRead { get; set; }

    public bool FailAdd { get; set; }

    public bool FailSetOrderId { get; set; }

    public HashSet<int> FailMarkLinkedForContacts { get; } = [];

    public void Seed(AplosVendorCardOrderModel model) => Rows[(model.AplosContactId, model.PexVendorId)] = Copy(model);

    public Task<List<AplosVendorCardOrderModel>> GetByBusinessAsync(int pexBusinessAcctId, CancellationToken cancellationToken)
    {
        if (FailRead) throw new InvalidOperationException("Table storage is unavailable.");
        return Task.FromResult(Rows.Values.Where(row => row.PEXBusinessAcctId == pexBusinessAcctId).Select(Copy).ToList());
    }

    public Task AddAsync(AplosVendorCardOrderModel model, CancellationToken cancellationToken)
    {
        if (FailAdd) throw new InvalidOperationException("Table storage is unavailable.");
        if (!Rows.TryAdd((model.AplosContactId, model.PexVendorId), Copy(model)))
        {
            throw new Azure.RequestFailedException(409, "The specified entity already exists.");
        }

        return Task.CompletedTask;
    }

    public Task SetOrderIdAsync(AplosVendorCardOrderModel model, int cardOrderId, CancellationToken cancellationToken)
    {
        if (FailSetOrderId) throw new InvalidOperationException("Table storage is unavailable.");
        Rows[(model.AplosContactId, model.PexVendorId)].CardOrderId = cardOrderId;
        model.CardOrderId = cardOrderId;
        return Task.CompletedTask;
    }

    public Task MarkLinkedAsync(AplosVendorCardOrderModel model, int cardAcctId, DateTime linkedUtc, CancellationToken cancellationToken)
    {
        if (FailMarkLinkedForContacts.Contains(model.AplosContactId)) throw new InvalidOperationException("Table storage is unavailable.");
        Rows[(model.AplosContactId, model.PexVendorId)].CardAcctId = cardAcctId;
        Rows[(model.AplosContactId, model.PexVendorId)].LinkedUtc = linkedUtc;
        model.CardAcctId = cardAcctId;
        model.LinkedUtc = linkedUtc;
        return Task.CompletedTask;
    }

    private static AplosVendorCardOrderModel Copy(AplosVendorCardOrderModel row) => new()
    {
        PEXBusinessAcctId = row.PEXBusinessAcctId,
        AplosContactId = row.AplosContactId,
        PexVendorId = row.PexVendorId,
        CardName = row.CardName,
        CardOrderId = row.CardOrderId,
        CardAcctId = row.CardAcctId,
        LinkedUtc = row.LinkedUtc,
        CreatedUtc = row.CreatedUtc
    };
}
