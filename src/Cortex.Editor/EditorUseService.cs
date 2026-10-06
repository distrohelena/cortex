using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
namespace Cortex.Editor;

public sealed class EditorUseService : IDisposable
{
    private readonly JsonElement _template;
    private readonly string _entry;
    private readonly IReadOnlyList<FlowNodeHandler> _handlers;
    private readonly ConcurrentQueue<string> _diagnostic;
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly object _sync=new();
    private TypedFlowExecution? _execution;
    private CancellationTokenSource? _cancellation;
    private Task _task=Task.CompletedTask;
    private EditorRunSnapshot _snapshot;
    private bool _busy;
    public IReadOnlyDictionary<string,string[]> Fields {get;}
    public IReadOnlyDictionary<string,string[]> Models {get;}
    public EditorUseService(JsonElement template,string entry,IReadOnlyList<FlowNodeHandler> handlers,ConcurrentQueue<string>? diagnostic=null)
    {
        _template=template.Clone();_entry=entry;_handlers=handlers;_diagnostic=diagnostic??new();
        var fields=new Dictionary<string,string[]>(StringComparer.Ordinal);var models=new Dictionary<string,string[]>(StringComparer.Ordinal);
        if(template.TryGetProperty("usage",out var usage))
        {
            if(usage.TryGetProperty("fields",out var allowed)) foreach(var item in allowed.EnumerateObject())
            {
                var node=template.GetProperty("nodes").EnumerateArray().SingleOrDefault(n=>n.GetProperty("id").GetString()==item.Name);
                if(node.ValueKind!=JsonValueKind.Object) throw new InvalidDataException("Unknown usage node: "+item.Name);
                var definition=handlers.SingleOrDefault(h=>h.Definition.Kind==node.GetProperty("kind").GetString())?.Definition;
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
        Fields=fields;Models=models;_snapshot=new(null,"idle",null,null,[],entry);
    }
    public string EntryId => _entry;
    public bool Busy {get{lock(_sync)return _busy;}}
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
                    var definition=_handlers.Single(h=>h.Definition.Kind==node["kind"]!.GetValue<string>()).Definition;
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
                _execution=new(TypedFlowCompiler.Compile(JsonSerializer.SerializeToElement(graph),_entry,_handlers));
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
    public async Task ResetAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try{if(Busy)throw new InvalidOperationException("Cancel the running step before starting over.");_execution=null;_diagnostic.Clear();lock(_sync)_snapshot=new(null,"idle",null,null,[],_entry);}
        finally{_gate.Release();}
    }
    public void Cancel(){lock(_sync)_cancellation?.Cancel();}
    public Task WaitAsync(CancellationToken token){lock(_sync)return _task.WaitAsync(token);}
    public void Dispose(){Cancel();_cancellation?.Dispose();}
}
