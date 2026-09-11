using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CopiersEquipmentOperationsTests
{
    private const string Depot="00000000-0000-0000-0000-000000000001",Office="00000000-0000-0000-0000-000000000002",
        A="00000000-0000-0000-0000-000000000003",B="00000000-0000-0000-0000-000000000004",
        Equipment="00000000-0000-0000-0000-000000000005",Replacement="00000000-0000-0000-0000-000000000006",
        Report="00000000-0000-0000-0000-000000000007",Actor="00000000-0000-0000-0000-000000000008";
    private static readonly CopiersEquipmentOperationsOptions Settings=new(){Enabled=true,InternalClientIds=[Depot,Office]};
    private static CopiersActivityV2BusinessCommand Command(string kind)=>new(){ReportRecordId=Report,SubmissionKey=Report,FinalizationFingerprint=new string('a',64),ServiceReference="ACT-000321",
        ActivityKind="movement",ClientId=kind=="internal"?Office:A,EquipmentId=Equipment,EquipmentSerial="SERIAL-A",TechnicianSystemUserId=Actor,OccurredAtUtc=DateTimeOffset.Parse("2026-09-11T16:00:00Z"),
        Operation=new(){Kind=kind,ClientId=kind=="internal"?Office:A,DestinationId=kind=="delivery"?A:kind=="internal"?Office:B,DestinationName="Destino",Internal=kind=="internal",Reason="Operación física de prueba",
            Equipment=new(){Id=Equipment,Serial="SERIAL-A",Reference="Modelo",OriginId=kind is "delivery" or "internal"?Depot:A,OriginName="Origen",Condition="Operativo",Accessories="Cable de energía"}}};
    private static CopiersEquipmentOperationsLedger Service(Transport transport)=>new(transport,Options.Create(Settings),new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"CopiersMtoV2:DataverseApp:BaseUrl","https://example.test/"}}).Build());

    [Theory]
    [InlineData("withdrawal")]
    [InlineData("internal")]
    public async Task DepartureRemovesAssignmentAndRecordsCustodyNotDestinationAvailability(string kind)
    {
        var command=Command(kind); var transport=new Transport(command); var changes=await Service(transport).PlanAsync(command,default);
        Assert.Equal(2,changes.Count); Assert.True(changes[0].Insert); Assert.False(changes[1].Insert);
        Assert.Null(changes[1].Payload["cr07a_Cliente@odata.bind"]);
        var transit=JsonSerializer.Deserialize<CopiersEquipmentTransit>((string)changes[1].Payload[CopiersEquipmentOperationsLedger.TransitField]!)!;
        Assert.Equal(Report,transit.OperationKey); Assert.Equal(command.Operation!.DestinationId,transit.DestinationId); Assert.Equal(Actor,transit.CustodianId);
        Assert.Equal("W/\"1\"",changes[1].Version); Assert.Equal(Report,changes[0].Payload["dtc_signedreportkey"]);
    }
    [Fact]
    public async Task DeliveryConfirmsCustomerAssignmentWithoutInventingWarehouseIntermediateMove()
    {
        var command=Command("delivery");var transport=new Transport(command);var changes=await Service(transport).PlanAsync(command,default);
        Assert.Equal(2,changes.Count); Assert.Equal($"/cr07a_clientes({A})",changes[1].Payload["cr07a_Cliente@odata.bind"]); Assert.Null(changes[1].Payload[CopiersEquipmentOperationsLedger.TransitField]);
    }
    [Fact]
    public async Task ReplacementHasTwoMovementsAndTwoVersionedAssignmentsInOneAtomicChangeSet()
    {
        var command=ReplacementCommand(); var transport=new Transport(command);var service=Service(transport);var changes=await service.PlanAsync(command,default);
        Assert.Equal(4,changes.Count);Assert.Equal(2,changes.Count(x=>x.Insert));Assert.Null(changes[1].Payload["cr07a_Cliente@odata.bind"]);
        Assert.Equal($"/cr07a_clientes({A})",changes[3].Payload["cr07a_Cliente@odata.bind"]);
        await service.BatchAsync(changes,default);
        Assert.Equal(4,transport.Rows.Count(x=>x.Key.StartsWith("cr07a_")));
        Assert.Equal(1,transport.BatchCount);
    }
    [Fact]
    public async Task AssetRaceRejectsEntireReplacementWithoutPartialMovements()
    {
        var command=ReplacementCommand();var transport=new Transport(command);var service=Service(transport);var changes=await service.PlanAsync(command,default);
        transport.Rows[$"cr07a_equipos({Replacement})"]["@odata.etag"]="W/\"2\"";
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(()=>service.BatchAsync(changes,default));
        Assert.DoesNotContain(transport.Rows.Keys,x=>x.StartsWith(CopiersEquipmentOperationsLedger.Movements));
        Assert.Equal(A,transport.Rows[$"cr07a_equipos({Equipment})"]["_cr07a_cliente_value"]!.GetValue<string>());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReceiptRequiresExactDepartureAndDestination(bool internalReceipt)
    {
        var dest=internalReceipt?Office:B;
        var departure=Command("withdrawal");var command=departure with {ReportRecordId=Guid.NewGuid().ToString(),ClientId=dest,
            Operation=departure.Operation! with {Kind="receipt",PendingKey=Report,ClientId=dest,DestinationId=dest,Internal=internalReceipt}};
        var transport=new Transport(command);transport.SetTransit(dest);
        var changes=await Service(transport).PlanAsync(command,default);
        Assert.Equal($"/cr07a_clientes({dest})",changes[1].Payload["cr07a_Cliente@odata.bind"]);Assert.Null(changes[1].Payload[CopiersEquipmentOperationsLedger.TransitField]);
        var wrong=command with {Operation=command.Operation! with{PendingKey=Guid.NewGuid().ToString()}};
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(()=>Service(transport).PlanAsync(wrong,default));
        transport.SetTransit(A);
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(()=>Service(transport).PlanAsync(command,default));
    }
    [Fact]
    public async Task TransitAssetCannotBeDeliveredAgain()
    {
        var command=Command("delivery");var transport=new Transport(command);transport.SetTransit(B);
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(()=>Service(transport).PlanAsync(command,default));
    }
    [Fact]
    public async Task ExactInternalReplayDoesNotDuplicateMovementAfterAssetChangesAgain()
    {
        var command=Command("internal");var transport=new Transport(command);var service=Service(transport);
        Assert.False((await service.CommitAsync(command,null,default)).ReusedExisting);
        transport.Rows[$"cr07a_equipos({Equipment})"]["_cr07a_cliente_value"]=B;
        Assert.True((await service.CommitAsync(command,null,default)).ReusedExisting);Assert.Equal(1,transport.BatchCount);
        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(()=>service.CommitAsync(command with{FinalizationFingerprint=new string('b',64)},null,default));
    }
    [Fact]
    public async Task CustomerOperationCannotCommitWithoutCertificate()
    {
        var command=Command("delivery");var transport=new Transport(command);
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(()=>Service(transport).CommitAsync(command,null,default));Assert.Equal(0,transport.BatchCount);
    }
    [Theory]
    [InlineData("delivery","Certificado de entrega")]
    [InlineData("withdrawal","Certificado de retiro")]
    [InlineData("replacement","Certificado de cambio")]
    [InlineData("receipt","Certificado de entrega")]
    public async Task CustomerCertificateIsOnePageAndNeverDisclosesOtherClientOrInternalMovement(string kind,string title)
    {
        var model=CopiersMtoV2CompactPdfTests.Model();model.FormVersion=CopiersActivityV2Bindings.FormVersion;model.ServiceReference="ACT-000321";
        model.WorkPerformed="Prueba local de entrega, retiro o cambio";model.CustomerObservations="Información revisada.";
        var op=Command(kind=="replacement"?"withdrawal":kind).Operation! with{Kind=kind,Replacement=kind=="replacement"?new(){Id=Replacement,Serial="SERIAL-B",Reference="Modelo nuevo",Condition="Operativo",Accessories="Cable"}:null};
        model.Answers=model.Answers.Where(x=>x.Key!="recommendations").Concat(CopiersActivityV2Capture.OperationAnswers(op)).Concat(new[]{
            Answer("activity_kind","movement"),Answer("origin_client_name","CLIENTE-ORIGEN-PRIVADO"),Answer("destination_client_name","DEPOSITO-DESTINO-PRIVADO")}).ToArray();
        var builder=new CopiersMtoV2ProfessionalPdfBuilder();var result=await builder.BuildAsync(model);
        using var doc=PdfDocument.Open(result.Content);var page=Assert.Single(doc.GetPages());var text=ContentOrderTextExtractor.GetText(page);
        Assert.Contains(title,text);Assert.Contains("Ana Cliente",text);Assert.Equal(3,page.GetImages().Count());
        Assert.DoesNotContain("CLIENTE-ORIGEN-PRIVADO",text);Assert.DoesNotContain("DEPOSITO-DESTINO-PRIVADO",text);Assert.DoesNotContain("PendingKey",text);
        if(kind=="replacement"){Assert.Contains("Retirado",text);Assert.Contains("Entregado",text);Assert.Contains("SERIAL-B",text);}
        Assert.Equal(result.Content,(await builder.BuildAsync(model)).Content);
        var directory=Environment.GetEnvironmentVariable("COPIERS_OPERATIONS_PDF_SAMPLES");if(!string.IsNullOrEmpty(directory))await File.WriteAllBytesAsync(Path.Combine(directory,kind+".pdf"),result.Content);
    }
    private static CopiersMaintenanceV2FormAnswerSnapshot Answer(string key,string value)=>new(){Key=key,Label=key,Value=value};
    private static CopiersActivityV2BusinessCommand ReplacementCommand()
    {var command=Command("replacement");return command with{Operation=command.Operation! with{DestinationId=Depot,Replacement=new(){Id=Replacement,Serial="SERIAL-B",Reference="Modelo",OriginId=Office,OriginName="Oficina",Condition="Operativo"}}};}
    private sealed class Transport : ICopiersMtoV2ApplicationDataverseClient
    {
        public Dictionary<string,JsonObject> Rows {get;}=[];public int BatchCount {get;private set;}
        public Transport(CopiersActivityV2BusinessCommand command)
        {
            var op=command.Operation!;Rows[$"cr07a_equipos({Equipment})"]=Asset(op.Equipment);
            if(op.Replacement is not null)Rows[$"cr07a_equipos({Replacement})"]=Asset(op.Replacement);
        }
        private static JsonObject Asset(CopiersOperationEquipment asset)=>new(){["@odata.etag"]="W/\"1\"",["cr07a_equipoid"]=asset.Id,["cr07a_nombredelequipo"]=asset.Serial,["cr07a_referencia"]=asset.Reference,["_cr07a_cliente_value"]=asset.OriginId};
        public void SetTransit(string destination)=>Rows[$"cr07a_equipos({Equipment})"][CopiersEquipmentOperationsLedger.TransitField]=JsonSerializer.Serialize(new CopiersEquipmentTransit{OperationKey=Report,OriginId=A,OriginName="Origen",DestinationId=destination,DestinationName="Destino",CustodianId=Actor});
        public async Task<HttpResponseMessage> SendAsync(string relativeUrl,HttpMethod method,HttpContent? content,Action<HttpRequestMessage>? customizeRequest,CancellationToken ct=default)
        {
            var path=relativeUrl["/api/data/v9.2/".Length..].Split('?')[0];
            if(method==HttpMethod.Get){if(path.StartsWith("cr07a_clientes("))return Json(new{cr07a_nombre="Destino",statecode=0});return Rows.TryGetValue(path,out var row)?Json(row):new(HttpStatusCode.NotFound);}
            Assert.Equal("$batch",path);BatchCount++;
            var body=await content!.ReadAsStringAsync(ct);var matches=Regex.Matches(body,@"PATCH https://example.test/api/data/v9.2/(?<path>\S+) HTTP/1.1\r\nContent-Type: application/json\r\n(?<condition>If-None-Match|If-Match): (?<version>[^\r]+)\r\n\r\n(?<json>[^\r]+)");
            Assert.NotEmpty(matches);
            foreach(Match m in matches)
            {
                Rows.TryGetValue(m.Groups["path"].Value,out var row);
                if(m.Groups["condition"].Value=="If-None-Match"?row is not null:row?["@odata.etag"]?.GetValue<string>()!=m.Groups["version"].Value)
                    return new(HttpStatusCode.OK){Content=new StringContent("HTTP/1.1 412 Precondition Failed\r\n")};
            }
            foreach(Match m in matches)
            {
                var target=m.Groups["path"].Value;var update=JsonNode.Parse(m.Groups["json"].Value)!.AsObject();
                if(!Rows.TryGetValue(target,out var row)){row=new JsonObject();Rows[target]=row;}
                foreach(var pair in update)row[pair.Key]=pair.Value?.DeepClone();
                row["@odata.etag"]="W/\"2\"";
            }
            return new(HttpStatusCode.OK){Content=new StringContent(string.Concat(Enumerable.Repeat("HTTP/1.1 204 No Content\r\n",matches.Count)))};
        }
        private static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
    }
}
