using FulfillmentHub.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddFulfillmentHubWorker();

var host = builder.Build();
host.Run();
