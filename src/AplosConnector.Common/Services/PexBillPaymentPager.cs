using PexCard.Api.Client.Core;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Common.Services;

internal static class PexBillPaymentPager
{
    internal const int PageSize = 50;

    // A bill paid in the window can be dated long before it, and one dated in the window can still be unpaid.
    internal static async Task<List<BillPaymentModel>> GetAllBillPaymentsAsync(
        this IPexApiClient pexApiClient,
        string externalToken,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        // PEX stores a bill date at midnight, so it is searched by the window's EST calendar days, like the bill inbox.
        var billedInWindow = pexApiClient.GetAllPagesAsync(externalToken, new BillPaymentListRequestModel
        {
            BillDateFrom = fromUtc.ToEstCalendarDate().ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            BillDateTo = toUtc.ToEstCalendarDate().ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
            PaymentRequestStatuses = [PaymentRequestStatus.Closed, PaymentRequestStatus.PaymentException]
        }, cancellationToken);

        var paidInWindow = pexApiClient.GetAllPagesAsync(externalToken, new BillPaymentListRequestModel
        {
            PayoutDateFrom = fromUtc,
            PayoutDateTo = toUtc,
            PaymentRequestStatuses = [PaymentRequestStatus.Closed, PaymentRequestStatus.PaymentException]
        }, cancellationToken);

        await Task.WhenAll(billedInWindow, paidInWindow);

        return [.. billedInWindow.Result.Concat(paidInWindow.Result).DistinctBy(bill => bill.BillId)];
    }

    private static async Task<List<BillPaymentModel>> GetAllPagesAsync(
        this IPexApiClient pexApiClient,
        string externalToken,
        BillPaymentListRequestModel request,
        CancellationToken cancellationToken)
    {
        List<BillPaymentModel> bills = [];
        var page = 1;

        while (true)
        {
            var response = await pexApiClient.GetBillPayments(externalToken, request, page, PageSize, cancellationToken);
            if (response?.Items is not { Count: > 0 }) break;

            bills.AddRange(response.Items);
            if (response.Items.Count < PageSize || response.PageInfo is null || bills.Count >= response.PageInfo.TotalItems) break;

            page++;
        }

        return bills;
    }
}
