using AplosConnector.Common.Entities;
using AplosConnector.Common.Models;
using AplosConnector.Common.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Common.Tests;

public abstract partial class BillPaymentsTestBase
{
    protected sealed class FakeBillMappingStorage : IAplosBillMappingStorage
    {
        public readonly Dictionary<string, AplosBillMappingModel> Rows = new(StringComparer.OrdinalIgnoreCase);

        public bool FailNextWrite { get; set; }

        public bool FailNextRead { get; set; }

        public void Seed(AplosBillMappingModel model) => Rows[Key(model.PEXBusinessAcctId, model.AplosPayableId)] = Copy(model);

        // Copies in and out, like a table: a model the service mutates is not the stored row until it is written.
        public Task<List<AplosBillMappingModel>> GetByBusinessAsync(int pexBusinessAcctId, CancellationToken cancellationToken)
        {
            if (FailNextRead)
            {
                FailNextRead = false;
                return Task.FromException<List<AplosBillMappingModel>>(new InvalidOperationException("Table storage is unavailable."));
            }

            return Task.FromResult(Rows.Values.Where(row => row.PEXBusinessAcctId == pexBusinessAcctId).Select(Copy).ToList());
        }

        public Task AddAsync(AplosBillMappingModel model, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            Rows[Key(model.PEXBusinessAcctId, model.AplosPayableId)] = Copy(model);
            return Task.CompletedTask;
        }

        // Mirrors AplosBillMappingStorage: update-only, and only the paid column changes.
        public Task MarkPaidAsync(AplosBillMappingModel model, DateTime paidUtc, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            model.PaidSyncedUtc = paidUtc;
            Rows[Key(model.PEXBusinessAcctId, model.AplosPayableId)].PaidSyncedUtc = paidUtc;
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(AplosBillMappingModel model, DateTime failedUtc, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            model.FirstFailedUtc = failedUtc;
            Rows[Key(model.PEXBusinessAcctId, model.AplosPayableId)].FirstFailedUtc = failedUtc;
            return Task.CompletedTask;
        }

        // Mirrors AplosBillMappingStorage: a full replace that clears the paid marker and the retry clock.
        public Task MarkReversedAsync(AplosBillMappingModel model, DateTime reversedUtc, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            model.ReversedUtc = reversedUtc;
            model.PaidSyncedUtc = null;
            model.FirstFailedUtc = null;
            model.AwaitingChargeSinceUtc = null;
            Rows[Key(model.PEXBusinessAcctId, model.AplosPayableId)] = Copy(model);
            return Task.CompletedTask;
        }

        public Task ClearReversedAsync(AplosBillMappingModel model, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            model.ReversedUtc = null;
            Rows[Key(model.PEXBusinessAcctId, model.AplosPayableId)].ReversedUtc = null;
            return Task.CompletedTask;
        }

        public Task MarkAwaitingChargeAsync(AplosBillMappingModel model, DateTime awaitingSinceUtc, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            model.AwaitingChargeSinceUtc = awaitingSinceUtc;
            Rows[Key(model.PEXBusinessAcctId, model.AplosPayableId)].AwaitingChargeSinceUtc = awaitingSinceUtc;
            return Task.CompletedTask;
        }

        public Task MarkPaidFromClearingAsync(AplosBillMappingModel model, decimal clearingAccountNumber, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            model.PaidFromClearingAccountNumber = clearingAccountNumber;
            Rows[Key(model.PEXBusinessAcctId, model.AplosPayableId)].PaidFromClearingAccountNumber = clearingAccountNumber;
            return Task.CompletedTask;
        }

        public Task ClearPaidFromClearingAsync(AplosBillMappingModel model, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            model.PaidFromClearingAccountNumber = null;
            Rows[Key(model.PEXBusinessAcctId, model.AplosPayableId)].PaidFromClearingAccountNumber = null;
            return Task.CompletedTask;
        }

        public Task MarkCardChargeClaimedAsync(AplosBillMappingModel model, long cardTransactionId, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            model.CardTransactionId = cardTransactionId;
            Rows[Key(model.PEXBusinessAcctId, model.AplosPayableId)].CardTransactionId = cardTransactionId;
            return Task.CompletedTask;
        }

        public Task ClaimPexCreatedCardChargeAsync(int pexBusinessAcctId, int pexPaymentRequestId, long cardTransactionId, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            Rows[Key(pexBusinessAcctId, AplosBillMappingEntity.GetPexCreatedRowKey(pexPaymentRequestId))] = new AplosBillMappingModel
            {
                PEXBusinessAcctId = pexBusinessAcctId,
                PexPaymentRequestId = pexPaymentRequestId,
                CardTransactionId = cardTransactionId
            };
            return Task.CompletedTask;
        }

        // Mirrors AplosBillMappingStorage: update-only.
        public Task MarkPexCreatedClaimPostedAsync(int pexBusinessAcctId, int pexPaymentRequestId, DateTime postedUtc, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            Rows[Key(pexBusinessAcctId, AplosBillMappingEntity.GetPexCreatedRowKey(pexPaymentRequestId))].PaidSyncedUtc = postedUtc;
            return Task.CompletedTask;
        }

        private void ThrowIfFailing()
        {
            if (!FailNextWrite) return;

            FailNextWrite = false;
            throw new InvalidOperationException("Table storage is unavailable.");
        }

        private static string Key(int pexBusinessAcctId, string aplosPayableId) => $"{pexBusinessAcctId}|{aplosPayableId}";

        private static AplosBillMappingModel Copy(AplosBillMappingModel model) => new()
        {
            PEXBusinessAcctId = model.PEXBusinessAcctId,
            AplosPayableId = model.AplosPayableId,
            AplosReferenceNumber = model.AplosReferenceNumber,
            PexBillInboxId = model.PexBillInboxId,
            MetadataRelationId = model.MetadataRelationId,
            Amount = model.Amount,
            PaidSyncedUtc = model.PaidSyncedUtc,
            FirstFailedUtc = model.FirstFailedUtc,
            ReversedUtc = model.ReversedUtc,
            AwaitingChargeSinceUtc = model.AwaitingChargeSinceUtc,
            PaidFromClearingAccountNumber = model.PaidFromClearingAccountNumber,
            CardTransactionId = model.CardTransactionId,
            PexPaymentRequestId = model.PexPaymentRequestId,
            CreatedUtc = model.CreatedUtc
        };
    }
}
