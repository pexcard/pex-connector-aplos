using AplosConnector.Common.Services;
using Moq;
using PexCard.Api.Client.Core;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests;

public class PexBillPaymentPagerTests
{
    private const string Token = "token";
    private static readonly DateTime WindowFrom = new(2026, 8, 1, 4, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowTo = new(2026, 9, 12, 3, 59, 59, DateTimeKind.Utc);

    private readonly Mock<IPexApiClient> _pexApiClient = new(MockBehavior.Strict);
    private readonly List<int> _payoutPagesRead = [];

    public PexBillPaymentPagerTests()
    {
        SetupBillDatePages(totalItems: 0);
        SetupPayoutDatePages(totalItems: 0);
    }

    [Fact]
    public async Task SendsTheBillDateWindowAsEstCalendarDaysWithTheStatusesAndPageSize()
    {
        await GetAllBillPayments();

        _pexApiClient.Verify(client => client.GetBillPayments(Token,
            It.Is<BillPaymentListRequestModel>(r => r.BillDateFrom == new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
                                                    && r.BillDateTo == new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1)
                                                    && r.PayoutDateFrom == null && r.PayoutDateTo == null && r.CreatedDateFrom == null
                                                    && HasClosedAndPaymentException(r)),
            1, PexBillPaymentPager.PageSize, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendsThePayoutDateWindowWithTheStatusesAndPageSize()
    {
        await GetAllBillPayments();

        _pexApiClient.Verify(client => client.GetBillPayments(Token,
            It.Is<BillPaymentListRequestModel>(r => r.PayoutDateFrom == WindowFrom && r.PayoutDateTo == WindowTo
                                                    && r.BillDateFrom == null && r.BillDateTo == null && r.CreatedDateFrom == null
                                                    && HasClosedAndPaymentException(r)),
            1, PexBillPaymentPager.PageSize, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ABillDatedLongBeforeTheWindowButPaidInItIsIncluded()
    {
        SetupBillDatePages(totalItems: 2, Bills(1, 2));
        SetupPayoutDatePages(totalItems: 1, [new BillPaymentModel { BillId = 900, BillDate = WindowFrom.AddDays(-90) }]);

        var bills = await GetAllBillPayments();

        Assert.Equal([1, 2, 900], bills.Select(bill => bill.BillId));
    }

    [Fact]
    public async Task ABillDatedAndPaidInTheWindowIsReturnedOnce()
    {
        SetupBillDatePages(totalItems: 2, Bills(1, 2));
        SetupPayoutDatePages(totalItems: 2, Bills(2, 2));

        var bills = await GetAllBillPayments();

        Assert.Equal([1, 2, 3], bills.Select(bill => bill.BillId));
    }

    [Fact]
    public async Task EveryPayoutPageIsReadUntilAllItemsAreRead()
    {
        SetupPayoutDatePages(totalItems: 100, Bills(1, PexBillPaymentPager.PageSize), Bills(51, PexBillPaymentPager.PageSize), Bills(101, 1));

        var bills = await GetAllBillPayments();

        Assert.Equal(100, bills.Count);
        Assert.Equal([1, 2], _payoutPagesRead);
    }

    private static bool HasClosedAndPaymentException(BillPaymentListRequestModel request)
        => request.PaymentRequestStatuses is { } statuses
           && statuses.Contains(PaymentRequestStatus.Closed)
           && statuses.Contains(PaymentRequestStatus.PaymentException);

    private Task<List<BillPaymentModel>> GetAllBillPayments()
        => _pexApiClient.Object.GetAllBillPaymentsAsync(Token, WindowFrom, WindowTo, CancellationToken.None);

    private void SetupBillDatePages(long totalItems, params List<BillPaymentModel>[] pages)
        => SetupPages(r => r.BillDateFrom != null, totalItems, pages, []);

    private void SetupPayoutDatePages(long totalItems, params List<BillPaymentModel>[] pages)
        => SetupPages(r => r.PayoutDateFrom != null, totalItems, pages, _payoutPagesRead);

    private void SetupPages(Expression<Func<BillPaymentListRequestModel, bool>> isRequest, long totalItems, List<BillPaymentModel>[] pages, List<int> pagesRead)
        => _pexApiClient
            .Setup(client => client.GetBillPayments(Token, It.Is(isRequest), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, BillPaymentListRequestModel _, int page, int _, CancellationToken _) =>
            {
                pagesRead.Add(page);
                return new BillPaymentListResponseModel
                {
                    Items = page <= pages.Length ? pages[page - 1] : [],
                    PageInfo = new PageInfoModel { Page = page, PageSize = PexBillPaymentPager.PageSize, TotalItems = totalItems }
                };
            });

    private static List<BillPaymentModel> Bills(int firstBillId, int count)
        => [.. Enumerable.Range(firstBillId, count).Select(id => new BillPaymentModel { BillId = id })];
}
