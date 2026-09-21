using DigitalTech.Puntajes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System.Reflection;
using Xunit;

public sealed class FirstContractTests
{
    private static Entity Row(int id, string created, string start, int? flag) => new(FirstContractPlugin.Table, Guid.Parse($"00000000-0000-0000-0000-{id:D12}"))
    {
        ["createdon"] = DateTime.Parse(created),
        [FirstContractPlugin.Start] = DateTime.Parse(start),
        [FirstContractPlugin.Flag] = flag.HasValue ? new OptionSetValue(flag.Value) : null
    };

    private static void Apply(Entity[] rows)
    {
        foreach (var change in FirstContractPlugin.Plan(rows))
            rows.Single(r => r.Id == change.Id)[FirstContractPlugin.Flag] = change[FirstContractPlugin.Flag];
    }

    [Fact] public void UsesCreatedDateBeforeContractStart()
    {
        var first = Row(1, "2025-01-01", "2026-12-01", 2);
        var second = Row(2, "2025-01-02", "2025-01-01", 1);
        var changes = FirstContractPlugin.Plan(new[] { second, first }).ToArray();
        Assert.Equal(second.Id, changes[0].Id);
        Assert.Equal(2, changes[0].GetAttributeValue<OptionSetValue>(FirstContractPlugin.Flag).Value);
        Assert.Equal(first.Id, changes[1].Id);
        Assert.All(changes, c => Assert.Single(c.Attributes));
    }

    [Fact] public void ResolvesCreationTieByStartThenOrdinalGuid()
    {
        var rows = new[] { Row(9,"2025-01-01","2025-02-01",null), Row(2,"2025-01-01","2025-01-01",null), Row(1,"2025-01-01","2025-01-01",null) };
        Apply(rows);
        Assert.Equal(rows[2].Id, rows.Single(r => r.GetAttributeValue<OptionSetValue>(FirstContractPlugin.Flag).Value == 1).Id);
    }

    [Theory] [InlineData(645250000)] [InlineData(645250001)] [InlineData(null)]
    public void NormalizesLegacyValuesAndIsIdempotent(int? flag)
    {
        var rows = new[] { Row(1,"2025-01-01","2025-01-01",flag), Row(2,"2025-01-02","2025-01-02",flag) };
        Apply(rows);
        Assert.Equal(1, rows[0].GetAttributeValue<OptionSetValue>(FirstContractPlugin.Flag).Value);
        Assert.Equal(2, rows[1].GetAttributeValue<OptionSetValue>(FirstContractPlugin.Flag).Value);
        Assert.Empty(FirstContractPlugin.Plan(rows));
    }

    [Fact] public void DeletingFirstPromotesNext()
    {
        var remaining = new[] { Row(2,"2025-01-02","2025-01-02",2), Row(3,"2025-01-03","2025-01-03",2) };
        Apply(remaining);
        Assert.Equal(1, remaining[0].GetAttributeValue<OptionSetValue>(FirstContractPlugin.Flag).Value);
        Assert.Equal(2, remaining[1].GetAttributeValue<OptionSetValue>(FirstContractPlugin.Flag).Value);
    }

    [Fact] public void ImportedOlderRowBecomesFirstWithoutMonthOrStatusFilter()
    {
        var rows = new[] { Row(1,"2026-01-01","2026-01-01",1), Row(2,"2024-01-01","2024-01-01",2) };
        rows[1]["statecode"] = new OptionSetValue(1);
        Apply(rows);
        Assert.Equal(1, rows[1].GetAttributeValue<OptionSetValue>(FirstContractPlugin.Flag).Value);
        Assert.Equal(2, rows[0].GetAttributeValue<OptionSetValue>(FirstContractPlugin.Flag).Value);
    }

    [Fact] public void EmptyClientHasNoWrites() => Assert.Empty(FirstContractPlugin.Plan(Array.Empty<Entity>()));

    [Fact] public void DisabledConfigurationDoesNotExecute() => new FirstContractPlugin("disabled", null).Execute(null);

    [Fact] public void PreOperationLocksBothClientsInStableOrderBeforeAnyScoreWrite()
    {
        var oldId = Guid.Parse("00000000-0000-0000-0000-000000000009");
        var newId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var ctx = Context("Update", 20, oldId, newId);
        var service = new FakeService(ctx);
        new FirstContractPlugin().Execute(service);
        Assert.Equal(new[] { newId, oldId }, service.Locks);
        Assert.Empty(service.Updates);
        Assert.Empty(service.Queries);
    }

    [Theory] [InlineData("Update")] [InlineData("Delete")]
    public void PostOperationReconcilesAffectedClientUsingAllHistory(string message)
    {
        var client = Guid.NewGuid();
        var ctx = Context(message, 40, client, message == "Delete" ? null : client);
        var service = new FakeService(ctx) { Rows = new[] { Row(1,"2025-01-01","2025-01-01",2), Row(2,"2025-01-02","2025-01-02",1) } };
        new FirstContractPlugin().Execute(service);
        Assert.Equal(2, service.Updates.Count);
        var query = Assert.Single(service.Queries);
        Assert.Equal(client, Assert.Single(Assert.Single(query.Criteria.Conditions).Values));
        Assert.Empty(service.Locks);
    }

    [Fact] public void CreateUsesClientFromTargetAndPromotesOnlyFirst()
    {
        var ctx = Context("Create", 40, null, Guid.NewGuid());
        var service = new FakeService(ctx) { Rows = new[] { Row(1,"2025-01-01","2025-01-01",2) } };
        new FirstContractPlugin().Execute(service);
        Assert.Equal(1, Assert.Single(service.Updates).GetAttributeValue<OptionSetValue>(FirstContractPlugin.Flag).Value);
    }

    [Fact] public void ExistingUnlinkedRowsRemainUntouched()
    {
        var service = new FakeService(Context("Update", 40, null, null));
        new FirstContractPlugin().Execute(service);
        Assert.Empty(service.Queries);
        Assert.Empty(service.Updates);
    }

    [Fact] public void FlagOnlyUpdateDoesNotReenterPostOperation()
    {
        var context = Context("Update", 40, Guid.NewGuid(), null);
        ((Entity)context.InputParameters["Target"])[FirstContractPlugin.Flag] = new OptionSetValue(1);
        var service = new FakeService(context);
        new FirstContractPlugin().Execute(service);
        Assert.Empty(service.Locks);
        Assert.Empty(service.Queries);
        Assert.Empty(service.Updates);
    }

    [Fact] public void ManualFlagIsCorrectedInTargetWithoutAnotherWrite()
    {
        var context = Context("Update", 20, Guid.NewGuid(), null);
        var target = (Entity)context.InputParameters["Target"];
        target[FirstContractPlugin.Flag] = new OptionSetValue(1);
        var first = Row(1,"2025-01-01","2025-01-01",1);
        var service = new FakeService(context) { Rows = new[] { first } };
        new FirstContractPlugin().Execute(service);
        Assert.Equal(2, target.GetAttributeValue<OptionSetValue>(FirstContractPlugin.Flag).Value);
        Assert.Empty(service.Updates);
    }

    private static IPluginExecutionContext Context(string message, int stage, Guid? before, Guid? after)
    {
        var ctx = DispatchProxy.Create<IPluginExecutionContext, ContextProxy>();
        var proxy = (ContextProxy)(object)ctx;
        var target = new Entity(FirstContractPlugin.Table, Guid.NewGuid());
        if (after.HasValue) target[FirstContractPlugin.Client] = new EntityReference("account", after.Value);
        var images = new EntityImageCollection();
        if (before.HasValue) images["Before"] = new Entity(FirstContractPlugin.Table) { [FirstContractPlugin.Client] = new EntityReference("account", before.Value) };
        proxy.Values = new() { ["PrimaryEntityName"] = FirstContractPlugin.Table, ["PrimaryEntityId"] = target.Id, ["Stage"] = stage,
            ["MessageName"] = message, ["IsInTransaction"] = true, ["PreEntityImages"] = images,
            ["InputParameters"] = new ParameterCollection { ["Target"] = message == "Delete" ? target.ToEntityReference() : target },
            ["SharedVariables"] = new ParameterCollection() };
        return ctx;
    }

    public class ContextProxy : DispatchProxy
    {
        public Dictionary<string, object> Values = new();
        protected override object Invoke(MethodInfo method, object[] args) => Values.GetValueOrDefault(method.Name.Substring(4));
    }

    private sealed class FakeService(IPluginExecutionContext context) : IOrganizationService, IOrganizationServiceFactory, IServiceProvider
    {
        public List<Guid> Locks = new();
        public List<Entity> Updates = new();
        public List<QueryExpression> Queries = new();
        public Entity[] Rows = Array.Empty<Entity>();
        public object GetService(Type type) => type == typeof(IPluginExecutionContext) ? context : this;
        public IOrganizationService CreateOrganizationService(Guid? user) { Assert.Null(user); return this; }
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            var upsert = Assert.IsType<UpsertRequest>(request);
            Assert.Equal(FirstContractPlugin.LockTable, upsert.Target.LogicalName);
            Locks.Add(upsert.Target.Id);
            return new UpsertResponse();
        }
        public void Update(Entity entity) { Updates.Add(entity); }
        public EntityCollection RetrieveMultiple(QueryBase query) { Queries.Add(Assert.IsType<QueryExpression>(query)); return new EntityCollection(Rows.ToList()); }
        public Guid Create(Entity e) => throw new NotSupportedException();
        public void Delete(string name, Guid id) => throw new NotSupportedException();
        public Entity Retrieve(string name, Guid id, ColumnSet columns) => context.PreEntityImages.Contains("Before") ? context.PreEntityImages["Before"] : new Entity(name, id);
        public void Associate(string name, Guid id, Relationship relation, EntityReferenceCollection refs) => throw new NotSupportedException();
        public void Disassociate(string name, Guid id, Relationship relation, EntityReferenceCollection refs) => throw new NotSupportedException();
    }
}
