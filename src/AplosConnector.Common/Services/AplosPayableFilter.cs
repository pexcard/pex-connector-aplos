using Aplos.Api.Client.Models.Detail;
using AplosConnector.Common.Models.Aplos;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AplosConnector.Common.Services
{
    public static class AplosPayableFilter
    {
        // The list endpoint reports amount/paid with the opposite sign to the detail endpoint, so every
        // comparison and everything handed to PEX works off the magnitude.
        public static decimal NormalizeAmount(decimal amount) => Math.Abs(amount);

        public static bool HasOutstandingBalance(AplosApiPayableDetail payable)
            => NormalizeAmount(payable.Amount) - NormalizeAmount(payable.PaidAmount) > 0m;

        public static bool IsPartiallyPaid(AplosApiPayableDetail payable)
        {
            var paid = NormalizeAmount(payable.PaidAmount);
            return paid > 0m && paid < NormalizeAmount(payable.Amount);
        }

        public static List<AplosApiPayableDetail> SelectUnpaid(IEnumerable<AplosApiPayableDetail> payables)
        {
            return (payables ?? Enumerable.Empty<AplosApiPayableDetail>())
                .Where(HasOutstandingBalance)
                .Where(payable => !IsPartiallyPaid(payable))
                .ToList();
        }

        public static AplosOutstandingBillModel ToOutstandingBill(AplosApiPayableDetail payable)
        {
            return new AplosOutstandingBillModel
            {
                Id = payable.Id,
                BillNumber = payable.ReferenceNumber,
                BillDate = payable.BillDate,
                DueDate = payable.DueDate,
                Amount = NormalizeAmount(payable.Amount),
                ContactId = payable.Contact?.Id ?? 0,
                ContactName = GetContactName(payable.Contact),
                Note = payable.Note
            };
        }

        public static string GetContactName(AplosApiContactDetail contact)
        {
            if (contact == null) return null;
            if (!string.IsNullOrWhiteSpace(contact.CompanyName)) return contact.CompanyName.Trim();
            return $"{contact.FirstName} {contact.LastName}".Trim();
        }
    }
}
