using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Queue;
using Microsoft.Azure.Storage.RetryPolicies;
using System;
using System.Threading;
using System.Threading.Tasks;


namespace AplosConnector.Core.Storages
{
    public class AzureQueueAbstract
    {
        protected readonly string ConnectionString;
        protected readonly string QueueName;
        protected CloudQueue Queue;

        protected AzureQueueAbstract(string connectionString, string queueName)
        {
            ConnectionString = connectionString;
            QueueName = queueName;
        }

        public async Task<AzureQueueAbstract> InitQueueAsync(CancellationToken token)
        {
            var storageAccount = CloudStorageAccount.Parse(ConnectionString);
            var queueClient = storageAccount.CreateCloudQueueClient();
            queueClient.DefaultRequestOptions = new QueueRequestOptions
            {
                RetryPolicy = new LinearRetry(TimeSpan.FromMilliseconds(500), 3)
            };
            Queue = queueClient.GetQueueReference(QueueName);
            await Queue.CreateIfNotExistsAsync(token);
            return this;
        }
    }

    public static class AzureQueueExtensions
    {
        public static TProvider InitQueue<TProvider>(
            this TProvider azureQueue,
            CancellationToken token = default) where TProvider : AzureQueueAbstract
        {
            return (TProvider)azureQueue.InitQueueAsync(token).GetAwaiter().GetResult();
        }
    }
}
