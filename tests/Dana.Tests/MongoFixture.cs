using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Dana.Tests;

/// <summary>
/// Contenedor de servicios contra una base MongoDB de pruebas (DANA_TEST_MONGO, por defecto mongodb://localhost:27017, base "dana_test").
/// La base se vacía al iniciar la colección de pruebas. Si Mongo no está disponible, las pruebas de integración se omiten.
/// </summary>
public class MongoFixture : IDisposable
{
    public ServiceProvider Services { get; }
    public bool Available { get; }
    public string Reason { get; } = "";

    public MongoFixture()
    {
        var conn = Environment.GetEnvironmentVariable("DANA_TEST_MONGO") ?? "mongodb://localhost:27017";
        var dbName = Environment.GetEnvironmentVariable("DANA_TEST_DB") ?? "dana_test";
        try
        {
            var client = new MongoClient(MongoClientSettings.FromConnectionString(conn) is var s ? Tap(s) : null);
            client.GetDatabase("admin").RunCommand<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));
            client.DropDatabase(dbName);
            Available = true;
        }
        catch (Exception ex) { Reason = "MongoDB no disponible: " + ex.Message; }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(new MongoSettings { ConnectionString = conn, Database = dbName }));
        services.AddSingleton<MongoContext>();
        services.AddSingleton<ChangeNotifier>();
        services.AddSingleton(typeof(IRepository<>), typeof(MongoRepository<>));
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ActivityService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<UserService>();
        services.AddSingleton<PermissionService>();
        services.AddSingleton<PipelineService>();
        services.AddSingleton<AutomationService>();
        services.AddSingleton<DealService>();
        services.AddSingleton<LeadService>();
        services.AddSingleton<TaskService>();
        services.AddSingleton<DashboardService>();
        services.AddSingleton<DemoDataService>();
        services.AddSingleton<BoardConfigService>();
        services.AddSingleton<IHostEnvironment>(new TestEnv());
        services.AddSingleton<AttachmentService>();
        services.AddSingleton(new EmailOptions());
        services.AddSingleton<SecretProtector>();
        services.AddSingleton<EmailService>();
        services.AddSingleton<InboundLeadService>();
        services.AddSingleton<LeadFormService>();
        services.AddSingleton<ReportService>();
        services.AddSingleton<ExportImportService>();
        services.AddSingleton<QuoteService>();
        services.AddSingleton<ReminderWorker>();
        services.AddHttpClient("automations");
        services.AddHttpClient("openai");
        services.AddHttpClient("websearch");
        services.AddSingleton<Dana.Services.Ai.WebSearchService>();
        services.AddSingleton(new Dana.Services.Messaging.TwilioOptions());
        services.AddSingleton<Dana.Services.Messaging.TwilioMessagingService>();
        services.AddDataProtection();
        services.AddSingleton<DocumentService>();
        services.AddSingleton<Dana.Services.Ai.AgentTools>();
        services.AddSingleton<Dana.Services.Ai.IChatProvider, Dana.Services.Ai.AnthropicProvider>();
        services.AddSingleton<Dana.Services.Ai.IChatProvider, Dana.Services.Ai.OpenAiProvider>();
        services.AddSingleton<Dana.Services.Ai.AgentService>();
        Services = services.BuildServiceProvider();
    }

    private static MongoClientSettings Tap(MongoClientSettings s) { s.ServerSelectionTimeout = TimeSpan.FromSeconds(3); return s; }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();
    public void Dispose() => Services.Dispose();

    private sealed class TestEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Dana.Tests";
        public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "dana-tests");
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}

[CollectionDefinition("mongo")]
public class MongoCollection : ICollectionFixture<MongoFixture> { }
