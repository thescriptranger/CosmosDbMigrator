
var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.CosmosDbMigrator_Web>("cosmosdbmigrator-web");

builder.Build().Run();
