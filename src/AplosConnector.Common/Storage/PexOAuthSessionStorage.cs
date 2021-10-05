using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AplosConnector.Common.Models;
using AplosConnector.Common.Entities;
using Microsoft.Azure.Cosmos.Table;
using System.Threading;

namespace AplosConnector.Core.Storages
{
    public class PexOAuthSessionStorage : AzureTableStorageAbstract
    {
        public PexOAuthSessionStorage(string connectionString)
            : base(connectionString, "PexOAuthSession", "PexOauth") { }

        public async Task CreateAsync(PexOAuthSessionModel model, CancellationToken cancellationToken)
        {
            var entity = new PexOAuthSessionEntity(model, PartitionKey);
            var operation = TableOperation.Insert(entity);
            await Table.ExecuteAsync(operation);
        }

        public async Task UpdateAsync(PexOAuthSessionModel model, CancellationToken cancellationToken)
        {
            if (model.SessionGuid == Guid.Empty)
            {
                throw new ArgumentException("session.SessionGuid is not specified");
            }
            var entity = await GetEntityBySessionGuidAsync(model.SessionGuid, cancellationToken);
            entity.ExternalToken = model.ExternalToken;
            entity.RevokedUtc = model.RevokedUtc;
            entity.LastRenewedUtc = model.LastRenewedUtc;
            var operation = TableOperation.InsertOrReplace(entity);
            await Table.ExecuteAsync(operation);
        }

        public async Task DeleteBySessionGuidAsync(Guid sessionGuid, CancellationToken cancellationToken)
        {
            var entity = await GetEntityBySessionGuidAsync(sessionGuid, cancellationToken);
            if (entity != null)
            {
                var operation = TableOperation.Delete(entity);
                await Table.ExecuteAsync(operation);
            }
        }

        public async Task<PexOAuthSessionModel> GetBySessionGuidAsync(Guid sessionGuid, CancellationToken cancellationToken)
        {
            var entity = await GetEntityBySessionGuidAsync(sessionGuid, cancellationToken);

            return entity?.ToModel();
        }

        private async Task<PexOAuthSessionEntity> GetEntityBySessionGuidAsync(Guid sessionGuid, CancellationToken cancellationToken)
        {
            var operation = TableOperation.Retrieve<PexOAuthSessionEntity>(PartitionKey, sessionGuid.ToString());
            var result = await Table.ExecuteAsync(operation, cancellationToken);
            return (PexOAuthSessionEntity) result.Result;
        }

        public async Task<List<PexOAuthSessionModel>> GetAllSessions(CancellationToken cancellationToken)
        {
            TableContinuationToken token = null;
            var entities = new List<PexOAuthSessionEntity>();
            do
            {
                var queryResult = await Table.ExecuteQuerySegmentedAsync(new TableQuery<PexOAuthSessionEntity>(), token, cancellationToken);
                entities.AddRange(queryResult.Results);
                token = queryResult.ContinuationToken;
            } while (token != null);

            return entities.ConvertAll(entity => entity.ToModel());

        }
    }
}
