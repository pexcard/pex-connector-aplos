using Microsoft.Azure.Cosmos.Table;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Core.Storages
{
    public abstract class AzureTableStorageAbstract
    {
        protected readonly string ConnectionString;
        protected readonly string StorageTableName;
        protected readonly string PartitionKey;
        protected CloudTable Table;

        protected AzureTableStorageAbstract(string connectionString, string storageTableName, string partitionKey)
        {
            ConnectionString = connectionString;
            StorageTableName = storageTableName;
            PartitionKey = partitionKey;
        }

        protected async Task InitTableAsync(CancellationToken cancellationToken)
        {
            var storageAccount = CloudStorageAccount.Parse(ConnectionString);
            var tableClient = storageAccount.CreateCloudTableClient();
            tableClient.DefaultRequestOptions = new TableRequestOptions
            {
                RetryPolicy = new LinearRetry(TimeSpan.FromMilliseconds(500), 3)
            };
            Table = tableClient.GetTableReference(StorageTableName);
            await Table.CreateIfNotExistsAsync(cancellationToken);
        }
    }
}
