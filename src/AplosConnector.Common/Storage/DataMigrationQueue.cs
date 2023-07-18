namespace AplosConnector.Common.Storage
{
    public class DataMigrationQueue : AzureQueueAbstract
    {

        public const string QUEUE_NAME = "pex-aplos-data-migration";

        public DataMigrationQueue(string connectionString) : base(connectionString, QUEUE_NAME)
        {
        }
    }
}
