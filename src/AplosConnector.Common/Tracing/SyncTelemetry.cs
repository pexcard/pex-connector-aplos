#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PexCard.Shared.OpenTelemetry.Abstractions.Constants;

namespace AplosConnector.Common.Tracing;

// Sync spans. Each host must AddSource(ActivitySourceName) on its tracer or these spans are dropped.
public static class SyncTelemetry
{
    public const string ActivitySourceName = "AplosConnector.Sync";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    // Log scopes keep their existing keys so App Insights queries still match; spans get the fleet's pex.* names.
    private static readonly IReadOnlyDictionary<string, string> SpanTagNames = new Dictionary<string, string>
    {
        ["BusinessAccountId"] = DiagnosticNames.BusinessPlatformAccountId,
        ["SyncSessionId"] = DiagnosticNames.SyncSessionId,
        ["CardholderAccountId"] = DiagnosticNames.CardholderPlatformAccountId,
        ["TransactionId"] = DiagnosticNames.TransactionId,
        ["TransactionDescription"] = DiagnosticNames.TransactionDescription,
        ["TransactionAmount"] = DiagnosticNames.TransactionAmount,
        ["TransactionTime"] = DiagnosticNames.TransactionTime,
        ["SettlementTime"] = DiagnosticNames.TransactionSettlementTime,
        ["InvoiceId"] = DiagnosticNames.InvoiceId,
        ["InvoiceAmount"] = DiagnosticNames.InvoiceAmount,
        ["InvoiceDueDate"] = DiagnosticNames.InvoiceDueDate,
        ["InvoiceStatus"] = DiagnosticNames.InvoiceStatus,
        ["PaymentRequestId"] = DiagnosticNames.PaymentRequestId,
        ["PaymentRequestType"] = DiagnosticNames.PaymentRequestType,
        ["Amount"] = DiagnosticNames.PaymentRequestAmount,
        ["MerchantName"] = DiagnosticNames.PaymentRequestMerchantName,
        ["PurchaseDate"] = DiagnosticNames.PaymentRequestPurchaseDate,
    };

    // Starts a child span named spanName tagged with the fields, and opens a logger scope with the same fields.
    public static SyncItemContext BeginItemContext(ILogger logger, string spanName, IDictionary<string, object> fields, string? transactionKind = null)
    {
        var span = ActivitySource.StartActivity(spanName);
        if (span is not null)
        {
            foreach (var field in fields)
            {
                span.SetTag(SpanTagNames.TryGetValue(field.Key, out var name) ? name : field.Key, field.Value);
            }
            if (transactionKind is not null)
            {
                span.SetTag(DiagnosticNames.TransactionKind, transactionKind);
            }
        }
        return new SyncItemContext(span, logger.BeginScope(fields));
    }

    // Marks a sync span failed with the exception recorded.
    public static void RecordError(Activity? span, Exception ex)
    {
        span?.SetStatus(ActivityStatusCode.Error, ex.Message);
        span?.AddException(ex);
    }
}

// Disposing ends the logger scope, then the span.
public readonly struct SyncItemContext : IDisposable
{
    private readonly IDisposable? _logScope;

    internal SyncItemContext(Activity? span, IDisposable? logScope)
    {
        Span = span;
        _logScope = logScope;
    }

    public Activity? Span { get; }

    public void Dispose()
    {
        _logScope?.Dispose();
        Span?.Dispose();
    }
}
