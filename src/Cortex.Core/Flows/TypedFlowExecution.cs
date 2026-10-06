using System.Text.Json;
using Cortex.Core.Flows.Durable;
using Cortex.Core.Flows.Execution;
namespace Cortex.Core.Flows;

/// <summary>An in-process, one-node-at-a-time view of the existing execution kernel.</summary>
public sealed class TypedFlowExecution
{
    private readonly TypedFlow _flow;
    private readonly FlowExecutionState _state;
    private readonly FlowExecutionServices _services;
    private readonly SemaphoreSlim _advance = new(1,1);
    public TypedFlowExecution(TypedFlow flow, IFlowCompletion? completion = null)
    {
        _flow=flow;
        var scope=new ArtifactScope("local-harness",new("local","local","local","local","local"),"local","local");
        var reference=new ArtifactRef(1,scope,"project",new string('0',64));
        _state=FlowExecutionKernel.Start(flow,JsonSerializer.SerializeToElement(new{}),new(scope,"local",reference,reference,"local",0,Sessions.SessionEntryKind.Dm),null);
        _services=new(completion??new NoCompletion());
    }
    public string? NextNodeId=>_state.Snapshot.Status==ExecutionStatus.Ready?_state.Snapshot.Cursor:null;
    public bool Completed=>_state.Snapshot.Status==ExecutionStatus.Completed;
    public async Task<TypedRunResult> AdvanceAsync(CancellationToken token)
    {
        await _advance.WaitAsync(token);
        try
        {
            var step=await FlowExecutionKernel.AdvanceAsync(_flow,_state,_services,token,oneNode:true);
            if(step.Status==ExecutionStatus.WaitingForHuman) throw new InvalidOperationException("This node requires the durable human feedback backend.");
            var result=step.Result;
            return new(result.Success,result.Steps.ToArray(),result.Outputs.ToDictionary(p=>p.Key,p=>new Dictionary<string,TypedValue>(p.Value)),result.TargetStateId,result.ConfirmedSend);
        }
        finally{_advance.Release();}
    }
    public TypedRunResult AmendLastOutputs(IReadOnlyDictionary<string,JsonElement> outputs)
    {
        if(!_advance.Wait(0))throw new InvalidOperationException("A node is running.");
        try
        {
            var snapshot=_state.Snapshot;
            if(snapshot.Status!=ExecutionStatus.Ready||snapshot.Steps.Count==0)throw new InvalidOperationException("Review outputs while paused between nodes.");
            var id=snapshot.Steps[^1].NodeId;var node=_flow.Nodes[id];
            if(FlowExecutionKernel.Effect(node) is not (StepEffect.Pure or StepEffect.Inference))throw new InvalidOperationException("Published side effects cannot be edited as draft outputs.");
            if(!outputs.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(node.Outputs.Select(p=>p.Id)))throw new InvalidDataException("Review must retain the declared output ports.");
            var updated=new Dictionary<string,TypedValue>(StringComparer.Ordinal);
            foreach(var port in node.Outputs){FlowDomain.Check(outputs[port.Id],port.Type);updated[port.Id]=new(port.Type,outputs[port.Id].Clone());}
            snapshot.Outputs[id]=updated;
            foreach(var(port,value)in updated)snapshot.Values[new(id,port)]=value;
            return new(true,snapshot.Steps.ToArray(),snapshot.Outputs.ToDictionary(p=>p.Key,p=>new Dictionary<string,TypedValue>(p.Value)),snapshot.TargetStateId,snapshot.ConfirmedSend);
        }
        finally{_advance.Release();}
    }
    private sealed class NoCompletion:IFlowCompletion
    {public Task<string> CompleteAsync(FlowCompletionRequest request,CancellationToken token)=>throw new InvalidDataException("Configure a model provider for built-in prompt nodes.");}
}
