using System.Text.Json;
namespace Cortex.Video.Editing;
public sealed record EditorialCatalogPin(string Schema,string Fingerprint);
public sealed record EditorialCompositionEdit {
    public double VoiceGain {get;init;}=1;
    public bool VoiceMuted {get;init;}
    public IReadOnlyList<EditorialAudioEnvelope> VoiceEnvelopes {get;init;}=[];
    public EditorialTransform? Transform {get;init;}
    public IReadOnlyList<EditorialPropertyAnimation> Animations {get;init;}=[];
    public IReadOnlyList<EditorialLayerEffect> Effects {get;init;}=[];
    public EditorialMask? Mask {get;init;}
    public EditorialSceneTransition? Transition {get;init;}
    public IReadOnlyList<EditorialAudioEdit> Audio {get;init;}=[];
}
public sealed record EditorialTransform(double PositionX=0,double PositionY=0,double ScaleX=1,double ScaleY=1,double RotationDeg=0,double Opacity=1);
public sealed record EditorialPropertyAnimation(string Property,IReadOnlyList<EditorialKeyframe> Keyframes);
public sealed record EditorialKeyframe(double TimeSec,double Value,string Curve="linear.v1");
public sealed record EditorialLayerEffect(string Id,int Version,IReadOnlyDictionary<string,JsonElement> Parameters,IReadOnlyDictionary<string,string> Inputs);
public sealed record EditorialMask(string MediaId,string Channel="alpha",bool Inverted=false);
public sealed record EditorialSceneTransition {
    public string EffectId {get;init;}="crossfade";
    public int EffectVersion {get;init;}=1;
    public double DurationSec {get;init;}=.5;
    public IReadOnlyDictionary<string,JsonElement> Parameters {get;init;}=new Dictionary<string,JsonElement>();
}
public sealed record EditorialAudioEdit {
    public required string MediaId {get;init;}
    public double StartSec {get;init;}
    public required double DurationSec {get;init;}
    public double SourceInSec {get;init;}
    public double Gain {get;init;}=1;
    public bool Muted {get;init;}
    public IReadOnlyList<EditorialAudioEnvelope> Envelopes {get;init;}=[];
}
public sealed record EditorialAudioEnvelope {
    public string Type {get;init;}="linear";
    public double StartSec {get;init;}
    public required double DurationSec {get;init;}
    public double From {get;init;}
    public double To {get;init;}=1;
}
