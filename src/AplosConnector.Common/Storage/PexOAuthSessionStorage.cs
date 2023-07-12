using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AplosConnector.Common.Entities;
using AplosConnector.Common.Models;
using Azure.Data.Tables;

namespace AplosConnector.Common.Storage
{
    public class PexOAuthSessionStorage : AzureTableStorageAbstract
    {
        public const string TABLE_NAME = "PexOAuthSession";
        public const string PARTITION_KEY = "PexOauth";


        public PexOAuthSessionStorage(TableClient tableClient) : base(tableClient) { }

        public async Task CreateAsync(PexOAuthSessionModel model, CancellationToken cancellationToken)
        {
            var entity = new PexOAuthSessionEntity(model, PARTITION_KEY);
            await TableClient.AddEntityAsync(entity, cancellationToken);
        }

        public async Task DeleteBySessionGuidAsync(Guid sessionGuid, CancellationToken cancellationToken)
        {
            var entity = await GetEntityBySessionGuidAsync(sessionGuid, cancellationToken);
            if (entity != null)
            {
                await TableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, default, cancellationToken);
            }
        }

        public async Task<PexOAuthSessionModel> GetBySessionGuidAsync(Guid sessionGuid, CancellationToken cancellationToken)
        {
            var entity = await GetEntityBySessionGuidAsync(sessionGuid, cancellationToken);

            return entity?.ToModel();
        }

        private async Task<PexOAuthSessionEntity> GetEntityBySessionGuidAsync(Guid sessionGuid, CancellationToken cancellationToken)
        {
            var tableEntity = await TableClient.GetEntityAsync<PexOAuthSessionEntity>(PARTITION_KEY,
                sessionGuid.ToString(), null, cancellationToken);

            return tableEntity;
        }

        public async Task<List<PexOAuthSessionModel>> GetAllSessions(CancellationToken cancellationToken)
        {
            var tableEntities = TableClient.QueryAsync<PexOAuthSessionEntity>();

            var results = new List<PexOAuthSessionEntity>();
            await foreach (var tableEntity in tableEntities)
            {
                if (tableEntity != null)
                {
                    results.Add(tableEntity);
                }
            }

            return results.ConvertAll(entity => entity.ToModel());
        }
    }
}
