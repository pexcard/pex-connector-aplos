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

        public async Task<AzureTableStorageAbstract> InitTableAsync(CancellationToken cancellationToken)
        {
            var storageAccount = CloudStorageAccount.Parse(ConnectionString);
            var tableClient = storageAccount.CreateCloudTableClient();
            tableClient.DefaultRequestOptions = new TableRequestOptions
            {
                RetryPolicy = new LinearRetry(TimeSpan.FromMilliseconds(500), 3)
            };
            Table = tableClient.GetTableReference(StorageTableName);
            await Table.CreateIfNotExistsAsync(cancellationToken);
            return this;
        }
    }

    public static class AzureTableStorageExtensions
    {
        public static TProvider InitTable<TProvider>(
            this TProvider provider,
            CancellationToken token = default) where TProvider : AzureTableStorageAbstract
        {
            return (TProvider)provider.InitTableAsync(token).GetAwaiter().GetResult();
        }
    }
}
