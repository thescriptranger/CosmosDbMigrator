using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace CosmosDbMigrator.Web.Services
{
    public class CosmosDbService : IDisposable
    {
        private CosmosClient cosmosClient;
        private bool isInitialized = false;

        public CosmosDbService() { }

        public void Initialize(string endpointUri, string primaryKey)
        {
            if (isInitialized)
            {
                cosmosClient.Dispose();
            }

            cosmosClient = new CosmosClient(endpointUri, primaryKey);
            isInitialized = true;
        }

        public async Task<List<string>> GetDatabasesAsync()
        {
            if (!isInitialized)
                throw new InvalidOperationException("CosmosDbService is not initialized.");

            List<string> databases = new List<string>();
            var iterator = cosmosClient.GetDatabaseQueryIterator<DatabaseProperties>();
            while (iterator.HasMoreResults)
            {
                foreach (var db in await iterator.ReadNextAsync())
                {
                    databases.Add(db.Id);
                }
            }
            return databases;
        }

        public async Task<List<string>> GetContainersAsync(string databaseId)
        {
            if (!isInitialized)
                throw new InvalidOperationException("CosmosDbService is not initialized.");

            var database = cosmosClient.GetDatabase(databaseId);
            List<string> containers = new List<string>();
            var iterator = database.GetContainerQueryIterator<ContainerProperties>();
            while (iterator.HasMoreResults)
            {
                foreach (var container in await iterator.ReadNextAsync())
                {
                    containers.Add(container.Id);
                }
            }
            return containers;
        }

        public async Task<string> GetPartitionKeyPathAsync(string databaseId, string containerId)
        {
            if (!isInitialized)
                throw new InvalidOperationException("CosmosDbService is not initialized.");

            var container = cosmosClient.GetContainer(databaseId, containerId);
            var response = await container.ReadContainerAsync();
            var containerProperties = response.Resource;

            if (containerProperties.PartitionKeyPaths != null && containerProperties.PartitionKeyPaths.Count > 0)
            {
                return containerProperties.PartitionKeyPaths[0];
            }

            throw new InvalidOperationException("Container does not have a defined partition key.");
        }

        public async Task UploadDocumentsAsync(string databaseId, string containerId, List<JObject> documents)
        {
            if (!isInitialized)
                throw new InvalidOperationException("CosmosDbService is not initialized.");

            var container = cosmosClient.GetContainer(databaseId, containerId);
            string partitionKeyPropertyName = (await GetPartitionKeyPathAsync(databaseId, containerId)).TrimStart('/');

            foreach (var document in documents)
            {
                var partitionKeyValue = document[partitionKeyPropertyName]?.ToString();
                var id = document["id"]?.ToString();

                if (string.IsNullOrEmpty(partitionKeyValue))
                {
                    throw new Exception($"Partition key property '{partitionKeyPropertyName}' cannot be null or empty.");
                }

                if (string.IsNullOrEmpty(id))
                {
                    document["id"] = Guid.NewGuid().ToString();
                }

                await container.CreateItemAsync(document, new PartitionKey(partitionKeyValue));
            }
        }

        public async Task DeleteAllItemsAsync(string databaseId, string containerId)
        {
            if (!isInitialized)
                throw new InvalidOperationException("CosmosDbService is not initialized.");

            var container = cosmosClient.GetContainer(databaseId, containerId);
            string partitionKeyPropertyName = (await GetPartitionKeyPathAsync(databaseId, containerId)).TrimStart('/');

            var query = $"SELECT c.id, c.{partitionKeyPropertyName} FROM c";
            var iterator = container.GetItemQueryIterator<JObject>(query);

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    var id = item["id"]?.ToString();
                    var partitionKeyValue = item[partitionKeyPropertyName]?.ToString();

                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(partitionKeyValue))
                    {
                        await container.DeleteItemAsync<JObject>(id, new PartitionKey(partitionKeyValue));
                    }
                }
            }
        }

        public void Dispose()
        {
            cosmosClient?.Dispose();
        }
    }
}
