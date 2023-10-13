using PexCard.Api.Client.Core.Models;
using System;

namespace AplosConnector.Common.Models
{
    public static class PexTransactionExtensions
    {
        public static DateTime GetPostDate(this TransactionModel transaction, PostDateType type)
        {
            if (transaction is null)
            {
                throw new ArgumentNullException(nameof(transaction));
            }

            return type == PostDateType.Transaction ? transaction.TransactionTime : transaction.SettlementTime.GetValueOrDefault(transaction.TransactionTime);
        }
    }
}
