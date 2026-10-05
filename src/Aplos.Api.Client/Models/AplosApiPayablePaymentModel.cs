using System;

namespace Aplos.Api.Client.Models;

// Settles the whole outstanding balance. Aplos discards note and memo on this call (verified live 2026-09-17).
public sealed class AplosApiPayablePaymentModel
{
    public DateOnly PaidDate { get; set; }
    public decimal CashAccountNumber { get; set; }
}
