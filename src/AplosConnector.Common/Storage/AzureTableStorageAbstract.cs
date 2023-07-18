using Azure.Data.Tables;

namespace AplosConnector.Common.Storage
{
    public abstract class AzureTableStorageAbstract
    {
        public readonly TableClient TableClient;

        protected AzureTableStorageAbstract(TableClient tableClient)
        {
            TableClient = tableClient;
        }
    }
}
