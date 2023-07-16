using EnfusionDatabaseFramework.Drivers.WebProxy.Core;
using EnfusionDatabaseFramework.Drivers.WebProxy.MongoDB;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Configure proxy webserver listen urls
builder.WebHost.UseUrls($"http://{builder.Configuration.GetValue("BindHost", "*")}:{builder.Configuration.GetValue("BindPort", "8008")}");

// Setup connection to the target MongoDB server
builder.Services.AddSingleton<IMongoClient>(new MongoClient(
    $"mongodb://{builder.Configuration.GetValue("DbHost", "localhost")}:{builder.Configuration.GetValue("DbPort", "27017")}"));

// Add mongodb proxy service implementation
builder.Services.AddSingleton<IDbWebProxyService, MongoDbWebProxyService>();

var app = builder.Build();

// Inject service api routes
app.AddDbProxy();

app.Start();

Console.WriteLine($"MongoDB EnfusionDatabaseFramework proxy is now listening on {string.Join(", ", app.Urls)}.");

app.WaitForShutdown();
