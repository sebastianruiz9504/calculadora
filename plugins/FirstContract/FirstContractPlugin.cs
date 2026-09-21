using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace DigitalTech.Puntajes
{
    public sealed class FirstContractPlugin : IPlugin
    {
        public const string Table = "cr07a_contractrecord1";
        public const string Client = "cr07a_cliente";
        public const string Flag = "cr07a_esprimercontratoconelcliente";
        public const string Start = "cr07a_contractstartdate";
        public const string LockTable = "cr07a_scoreclientlock";
        private readonly bool enabled;

        public FirstContractPlugin() { enabled = true; }
        public FirstContractPlugin(string configuration, string secureConfiguration)
        {
            enabled = string.Equals(configuration, "enabled", StringComparison.Ordinal);
        }

        public void Execute(IServiceProvider provider)
        {
            if (!enabled) return;
            var context = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
            if (context.PrimaryEntityName != Table || (context.Stage != 20 && context.Stage != 40)) return;
            if (context.MessageName != "Create" && context.MessageName != "Update" && context.MessageName != "Delete") return;
            if (!context.IsInTransaction) throw new InvalidPluginExecutionException("La regla de primer contrato requiere una transacción.");

            var factory = (IOrganizationServiceFactory)provider.GetService(typeof(IOrganizationServiceFactory));
            // Full history must be visible regardless of the submitting user's row-level access.
            var service = factory.CreateOrganizationService(null);
            var before = context.PreEntityImages.Contains("Before") ? context.PreEntityImages["Before"] : null;
            var target = context.InputParameters.Contains("Target") ? context.InputParameters["Target"] as Entity : null;
            var oldClient = before?.GetAttributeValue<EntityReference>(Client);
            var newClient = context.MessageName == "Delete" ? null
                : target != null && target.Contains(Client) ? target.GetAttributeValue<EntityReference>(Client) : oldClient;
            var clients = new[] { oldClient, newClient }.Where(x => x != null)
                .Select(x => x.Id).Distinct().OrderBy(x => x.ToString("D"), StringComparer.Ordinal).ToArray();

            if (context.Stage == 20)
            {
                // Acquire locks BEFORE the score row write. The SQL locks last through PostOperation.
                // A dedicated technical row avoids changing any customer/business fields.
                foreach (var id in clients)
                {
                    var row = new Entity(LockTable, id);
                    row["cr07a_name"] = id.ToString("D");
                    row["cr07a_nonce"] = Guid.NewGuid().ToString("D");
                    service.Execute(new UpsertRequest { Target = row });
                }
                if (context.MessageName != "Create")
                {
                    var current = service.Retrieve(Table, context.PrimaryEntityId, new ColumnSet(Client));
                    if (current.GetAttributeValue<EntityReference>(Client)?.Id != oldClient?.Id)
                        throw new InvalidPluginExecutionException("El cliente del registro cambió durante la operación. Actualiza la página e intenta de nuevo.");
                }
                // An explicit unlink must not leave an unassigned Yes. Existing unlinked rows are untouched.
                if (oldClient != null && newClient == null && target != null) target[Flag] = new OptionSetValue(2);
                if (context.MessageName == "Update" && target != null && target.Contains(Flag)
                    && !target.Contains(Client) && !target.Contains(Start) && !target.Contains("createdon") && newClient != null)
                {
                    var first = Ordered(ReadClient(service, newClient.Id)).FirstOrDefault();
                    target[Flag] = new OptionSetValue(first != null && first.Id == target.Id ? 1 : 2);
                }
                return;
            }
            // Flag-only updates are corrected in PreOperation, directly in Target.
            // They must never start another reconciliation pipeline.
            if (context.MessageName == "Update" && target != null
                && !target.Contains(Client) && !target.Contains(Start) && !target.Contains("createdon")) return;
            foreach (var client in clients)
                foreach (var change in Plan(ReadClient(service, client))) service.Update(change);
        }

        private static List<Entity> ReadClient(IOrganizationService service, Guid client)
        {
            var query = new QueryExpression(Table)
            {
                ColumnSet = new ColumnSet("createdon", Start, Flag),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            query.Criteria.AddCondition(Client, ConditionOperator.Equal, client);
            query.AddOrder("cr07a_contractrecord1id", OrderType.Ascending);
            var rows = new List<Entity>();
            while (true)
            {
                var page = service.RetrieveMultiple(query);
                rows.AddRange(page.Entities);
                if (!page.MoreRecords) break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
            return rows;
        }

        public static IEnumerable<Entity> Plan(IEnumerable<Entity> source)
        {
            var rows = Ordered(source).ToArray();
            // Demote first, then promote. All writes commit together in the triggering transaction.
            return rows.Select((row, index) => new { Row = row, Value = index == 0 ? 1 : 2 })
                .Where(x => x.Row.GetAttributeValue<OptionSetValue>(Flag)?.Value != x.Value)
                .OrderByDescending(x => x.Value)
                .Select(x => new Entity(Table, x.Row.Id) { [Flag] = new OptionSetValue(x.Value) });
        }

        private static IOrderedEnumerable<Entity> Ordered(IEnumerable<Entity> source) =>
            source.OrderBy(x => x.GetAttributeValue<DateTime?>("createdon") ?? DateTime.MaxValue)
                .ThenBy(x => x.GetAttributeValue<DateTime?>(Start) ?? DateTime.MaxValue)
                .ThenBy(x => x.Id.ToString("D"), StringComparer.Ordinal);
    }
}
