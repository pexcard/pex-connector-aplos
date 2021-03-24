using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Queue;
using Microsoft.Azure.Storage.RetryPolicies;
using System;
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

        protected async Task InitQueueAsync()
        {
            var storageAccount = CloudStorageAccount.Parse(ConnectionString);
            var queueClient = storageAccount.CreateCloudQueueClient();
            queueClient.DefaultRequestOptions = new QueueRequestOptions
            {
                RetryPolicy = new LinearRetry(TimeSpan.FromMilliseconds(500), 3)
            };
            Queue = queueClient.GetQueueReference(QueueName);
            await Queue.CreateIfNotExistsAsync();
        }

    }
}
