using Azure.Data.Tables;

namespace AplosConnector.Common.Storage
{
    public abstract class AzureTableStorageAbstract
    {
        //TODO set back to protected after migration
        public readonly TableClient TableClient;

        protected AzureTableStorageAbstract(TableClient tableClient)
        {
            TableClient = tableClient;
        }
    }
}
