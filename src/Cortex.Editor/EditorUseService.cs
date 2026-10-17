using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
namespace Cortex.Editor;

public sealed class EditorUseService : IDisposable
{
    private readonly JsonElement _template;
    private readonly string _entry;
    private readonly IReadOnlyList<NodeDefinition> _definitions;
    private readonly Func<IReadOnlyList<FlowNodeHandler>> _bindHandlers;
    private readonly ConcurrentQueue<string> _diagnostic;
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly EditorUseTools _tools;
    private readonly object _sync=new();
    private TypedFlowExecution? _execution;
    private CancellationTokenSource? _cancellation;
    private Task _task=Task.CompletedTask;
    private EditorRunSnapshot _snapshot;
    private bool _busy;
    public IReadOnlyDictionary<string,string[]> Fields {get;}
    public IReadOnlyDictionary<string,string[]> Models {get;}
    public IReadOnlyDictionary<string,string[]> ReviewOutputs {get;}
    public EditorUseService(JsonElement template,string entry,IReadOnlyList<NodeDefinition> definitions,Func<IReadOnlyList<FlowNodeHandler>> bindHandlers,ConcurrentQueue<string>? diagnostic=null)
    {
        _template=template.Clone();_entry=entry;_definitions=definitions;_bindHandlers=bindHandlers;_diagnostic=diagnostic??new();_tools=new(template,definitions,bindHandlers);
        var fields=new Dictionary<string,string[]>(StringComparer.Ordinal);var models=new Dictionary<string,string[]>(StringComparer.Ordinal);
        if(template.TryGetProperty("usage",out var usage))
        {
            if(usage.TryGetProperty("fields",out var allowed)) foreach(var item in allowed.EnumerateObject())
            {
                var node=template.GetProperty("nodes").EnumerateArray().SingleOrDefault(n=>n.GetProperty("id").GetString()==item.Name);
                if(node.ValueKind!=JsonValueKind.Object) throw new InvalidDataException("Unknown usage node: "+item.Name);
                var definition=definitions.SingleOrDefault(d=>d.Kind==node.GetProperty("kind").GetString());
                var resolved=definition?.Resolve(Settings(node));
                var names=item.Value.EnumerateArray().Select(v=>v.GetString()!).ToArray();
                if(names.Any(n=>definition?.ConfigFields.Any(f=>f.Name==n)!=true)) throw new InvalidDataException("Unknown usage field on "+item.Name);
                fields.Add(item.Name,names);
            }
            if(usage.TryGetProperty("models",out var choices)) foreach(var item in choices.EnumerateObject())
            {
                if(!fields.TryGetValue(item.Name,out var exposed)||!exposed.Contains("model")) throw new InvalidDataException("Model selection must expose the model field.");
                var values=item.Value.EnumerateArray().Select(v=>v.GetString()!).ToArray();
                if(values.Length==0||values.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Provide allowed model choices.");
                models.Add(item.Name,values);
            }
        }
        var review=new Dictionary<string,string[]>(StringComparer.Ordinal);
        if(template.TryGetProperty("usage",out var reviewUsage)&&reviewUsage.TryGetProperty("reviewOutputs",out var configuredReview))foreach(var item in configuredReview.EnumerateObject())
        {
            var node=template.GetProperty("nodes").EnumerateArray().SingleOrDefault(n=>n.GetProperty("id").GetString()==item.Name);
            if(node.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Unknown review node.");
            var definition=definitions.Single(d=>d.Kind==node.GetProperty("kind").GetString());
            var resolved=definition.Resolve(Settings(node));
            var ports=item.Value.EnumerateArray().Select(v=>v.GetString()!).ToArray();
            if(ports.Any(p=>!resolved.DataOutputs.Any(o=>o.Name==p)))throw new InvalidDataException("Unknown review output.");
            review.Add(item.Name,ports);
        }
        Fields=fields;Models=models;ReviewOutputs=review;_snapshot=new(null,"idle",null,null,[],entry);
    }
    public EditorUseService(JsonElement template,string entry,IReadOnlyList<FlowNodeHandler> handlers,ConcurrentQueue<string>? diagnostic=null)
        : this(template,entry,handlers.Select(handler=>handler.Definition).ToArray(),()=>handlers,diagnostic) { }
    private static JsonElement Settings(JsonElement node) => node.TryGetProperty("settings",out var settings)&&settings.ValueKind==JsonValueKind.Object
        ? settings : JsonSerializer.SerializeToElement(new { });
    public string EntryId => _entry;
    public bool Busy {get{lock(_sync)return _busy||_tools.Busy;}}
    public IReadOnlyDictionary<string,UseToolDefinition> Tools => _tools.Definitions;
    public IReadOnlyDictionary<string,EditorRunSnapshot> ToolRuns => _tools.Snapshots;
    public EditorRunSnapshot ToolSnapshot(string id) => _tools.Snapshot(id);
    public Task WaitToolAsync(string id,CancellationToken token) => _tools.WaitAsync(id,token);
    public EditorArtifact? ToolArtifact(string id) => _tools.Artifact(id);
    public async Task RunToolAsync(string id,Dictionary<string,JsonElement> parameters,CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if(Busy||_snapshot.State!="paused"||_snapshot.Result is null)throw new InvalidOperationException("Run a tool while reviewing a completed step.");
            var node=_snapshot.Result.Value.GetProperty("steps").EnumerateArray().Last().GetProperty("nodeId").GetString()!;
            await _tools.StartAsync(id,node,parameters,token);
        }
        finally{_gate.Release();}
    }
    public EditorRunSnapshot Snapshot(){lock(_sync)return _snapshot with{Diagnostics=_diagnostic.ToArray()};}
    public async Task AdvanceAsync(Dictionary<string,Dictionary<string,JsonElement>> parameters,CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if(Busy) throw new InvalidOperationException("A step is already running.");
            if(_execution is not null&&parameters.Count>0) throw new InvalidOperationException("Inputs are fixed for this run. Start over to change them.");
            if(_snapshot.State is "failed" or "cancelled" or "completed") throw new InvalidOperationException("Start over before creating another run.");
            if(_execution is null)
            {
                var graph=JsonNode.Parse(_template.GetRawText())!;
                foreach(var(nodeId,values) in parameters)
                {
                    if(values is null) throw new InvalidDataException("Parameters must be an object of field values.");
                    if(!Fields.TryGetValue(nodeId,out var exposed)) throw new InvalidDataException("This node exposes no user parameters.");
                    var node=graph["nodes"]!.AsArray().Single(n=>n!["id"]!.GetValue<string>()==nodeId)!;
                    var definition=_definitions.Single(d=>d.Kind==node["kind"]!.GetValue<string>());
                    node["settings"]??=new JsonObject();
                    foreach(var(name,value) in values)
                    {
                        if(!exposed.Contains(name)) throw new InvalidDataException("This parameter is locked: "+name);
                        var field=definition.ConfigFields.Single(f=>f.Name==name);
                        if(value.ValueKind==JsonValueKind.Null&&!field.Required){node["settings"]!.AsObject().Remove(name);continue;}
                        FlowDomain.Check(value,field.Type);
                        if(name=="model"&&Models.TryGetValue(nodeId,out var options)&&!options.Contains(value.GetString()))throw new InvalidDataException("Choose an allowed model.");
                        node["settings"]![name]=JsonNode.Parse(value.GetRawText());
                    }
                }
                var candidate=JsonSerializer.SerializeToElement(graph);
                _ = TypedFlowCompiler.Validate(candidate,_entry,_definitions);
                var handlers=_bindHandlers();
                EditorExecutionService.EnsureResolvedContractParity(candidate,_definitions,handlers);
                _execution=new(TypedFlowCompiler.Compile(candidate,_entry,handlers));
                _diagnostic.Clear();_snapshot=new(Guid.NewGuid().ToString("N"),"idle",null,null,[],_entry);
            }
            lock(_sync)
            {
                _cancellation?.Dispose();_cancellation=new();_busy=true;
                _snapshot=_snapshot with{State="running",Error=null};
                var runToken=_cancellation.Token;
                _task=Task.Run(()=>StepAsync(runToken),CancellationToken.None);
            }
        }
        finally{_gate.Release();}
    }
    private async Task StepAsync(CancellationToken token)
    {
        try
        {
            var result=await _execution!.AdvanceAsync(token);
            var output=EditorExecutionService.SerializeResult(result);
            lock(_sync)_snapshot=_snapshot with{State=!result.Success?"failed":_execution.Completed?"completed":"paused",Result=output,NextNodeId=_execution.NextNodeId};
        }
        catch(OperationCanceledException){lock(_sync)_snapshot=_snapshot with{State="cancelled",Error="Step cancelled.",NextNodeId=null};}
        catch(Exception error)when(error is not OutOfMemoryException){lock(_sync)_snapshot=_snapshot with{State="failed",Error=error.Message,NextNodeId=null};}
        finally{lock(_sync)_busy=false;}
    }
    public async Task ReviewAsync(Dictionary<string,JsonElement> outputs,CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if(Busy||_execution is null||_snapshot.State!="paused")throw new InvalidOperationException("Review a draft after its step has completed.");
            var result=_snapshot.Result!.Value;var id=result.GetProperty("steps").EnumerateArray().Last().GetProperty("nodeId").GetString()!;
            if(!ReviewOutputs.TryGetValue(id,out var allowed)||outputs.Keys.Any(p=>!allowed.Contains(p)))throw new InvalidDataException("This output is not exposed for human review.");
            var values=result.GetProperty("outputs").GetProperty(id).EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetProperty("value").Clone());
            foreach(var(key,value)in outputs)values[key]=value.Clone();
            var edited=_execution.AmendLastOutputs(values);
            lock(_sync)_snapshot=_snapshot with{Result=EditorExecutionService.SerializeResult(edited)};
        }
        finally{_gate.Release();}
    }
    public async Task ResetAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try{if(Busy)throw new InvalidOperationException("Cancel the running step before starting over.");_execution=null;_tools.Reset();_diagnostic.Clear();lock(_sync)_snapshot=new(null,"idle",null,null,[],_entry);}
        finally{_gate.Release();}
    }
    public void Cancel(){lock(_sync)_cancellation?.Cancel();_tools.Cancel();}
    public Task WaitAsync(CancellationToken token){lock(_sync)return _task.WaitAsync(token);}
    public void Dispose(){Cancel();_cancellation?.Dispose();_tools.Dispose();}
}
