using EnfusionDatabaseFramework.Drivers.WebProxy.Core;
using EnfusionDatabaseFramework.Drivers.WebProxy.MongoDB;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Configure proxy webserver listen urls
builder.WebHost.UseUrls($"http://{builder.Configuration.GetValue("BindHost", "*")}:{builder.Configuration.GetValue("BindPort", "8008")}");

// Setup connection to the target MongoDB server
var connectionString = builder.Configuration["DbConnectionString"];
if (string.IsNullOrWhiteSpace(connectionString))
{
    var host = builder.Configuration.GetValue("DbHost", "localhost");
    var port = builder.Configuration.GetValue("DbPort", "27017");
    var user = builder.Configuration["DbUser"];
    var password = builder.Configuration["DbPassword"];
    string authString = string.IsNullOrWhiteSpace(user) ? string.Empty : $"{user}:{password}@";
    connectionString = $"mongodb://{authString}{host}:{port}";
}

builder.Services.AddSingleton<IMongoClient>(new MongoClient(connectionString));

// Add mongodb proxy service implementation
builder.Services.AddSingleton<IDbWebProxyService, MongoDbWebProxyService>();

var app = builder.Build();

// Inject service api routes
app.AddDbProxy();

app.Start();

Console.WriteLine($"MongoDB EnfusionDatabaseFramework proxy is now listening on {string.Join(", ", app.Urls)}.");

app.WaitForShutdown();
