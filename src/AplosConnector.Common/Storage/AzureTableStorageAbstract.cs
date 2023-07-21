using Azure.Data.Tables;

namespace AplosConnector.Common.Storage
{
    public abstract class AzureTableStorageAbstract
    {
        protected readonly TableClient TableClient;

        protected AzureTableStorageAbstract(TableClient tableClient)
        {
            TableClient = tableClient;
        }
    }
}
