using Dana.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace Dana.Data;

public class MongoContext
{
    private static bool _configured;

    public MongoContext(IOptions<MongoSettings> options)
    {
        ConfigureSerialization();
        var client = new MongoClient(options.Value.ConnectionString);
        Database = client.GetDatabase(options.Value.Database);
    }

    public IMongoDatabase Database { get; }

    public IMongoCollection<T> Collection<T>() => Database.GetCollection<T>(CollectionName<T>());

    public static string CollectionName<T>() => typeof(T).Name switch
    {
        nameof(Lead) => "leads",
        nameof(Contact) => "contacts",
        nameof(Company) => "companies",
        nameof(Deal) => "deals",
        nameof(Pipeline) => "pipelines",
        nameof(User) => "users",
        nameof(Activity) => "activities",
        nameof(BoardConfig) => "boards",
        nameof(TaskItem) => "tasks",
        nameof(Notification) => "notifications",
        nameof(Attachment) => "attachments",
        nameof(EmailTemplate) => "emailtemplates",
        nameof(Automation) => "automations",
        nameof(LeadForm) => "leadforms",
        nameof(ApiChannel) => "apichannels",
        nameof(ReportDashboard) => "reportdashboards",
        nameof(AuditEntry) => "audit",
        nameof(AppSettings) => "settings",
        nameof(Team) => "teams",
        nameof(Product) => "products",
        nameof(ProductCategory) => "productcategories",
        nameof(Quote) => "quotes",
        nameof(ApiToken) => "apitokens",
        nameof(AiAgent) => "aiagents",
        nameof(AgentConversation) => "agentconversations",
        nameof(AgentMemory) => "agentmemories",
        nameof(CrmDocument) => "documents",
        nameof(DocumentTemplateEntity) => "documenttemplates",
        nameof(ElectronicInvoice) => "invoices",
        nameof(TelegramLink) => "telegramlinks",
        nameof(ProjectBoard) => "projectboards",
        nameof(ProjectCard) => "projectcards",
        nameof(Account) => "accounts",
        nameof(JournalEntry) => "journalentries",
        nameof(PaymentPlan) => "paymentplans",
        var n => n.ToLowerInvariant() + "s"
    };

    public static void ConfigureSerialization()
    {
        if (_configured) return;
        _configured = true;

        var pack = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new IgnoreExtraElementsConvention(true),
            new IgnoreIfNullConvention(false),
            new EnumRepresentationConvention(BsonType.String),
        };
        ConventionRegistry.Register("dana", pack, _ => true);

        BsonSerializer.TryRegisterSerializer(new DecimalSerializer(BsonType.Decimal128));
        BsonSerializer.TryRegisterSerializer(new NullableSerializer<decimal>(new DecimalSerializer(BsonType.Decimal128)));
    }
}
