namespace AplosConnector.Common.Correlation;

// Shared by the web and SyncWorker registrations so both processes mint the same "ext-aplos-{32 hex}" ids.
public static class CorrelationConstants
{
    public const string Prefix = "ext-aplos";

    // Inbound X-Correlation-Id values longer than this are replaced with a fresh id.
    public const int MaxInboundLength = 128;
}
