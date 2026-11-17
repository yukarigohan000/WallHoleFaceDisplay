using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal enum MonitorPerfStage { Update,LateUpdate,FrameGap,MonitorWork,RestoreScan,NearestSelection,UiReset,Poll,StateObservation,AutoSync,LoadNpc,Attach,NativeStart,NativeTick,TemplateTick,TemplateAnimation,TemplateSurfaceInit,TemplateRender,NativeRender,TemplateCamera,NativeCamera,TemplateMeshAndSubmit,NativeMeshAndSubmit,FacilityAssetLoad,NormalAssetLoad,TemplateDispose,NativeStop,MainCameraFlush,RestorePose,StateUpdateApply,OffsetsWorld,Count }
    internal enum MonitorPerfEvent { RestoreTargets,RestoreAttempt,SelectionChanged,BindingChanged,AutoLoadAttempt,LoadSuccess,LoadFailure,TemplateSkeleton,NativeSkeleton,TemplateState,NativeState,TemplateRTCreate,NativeRTCreate,TemplateRTRelease,NativeRTRelease,TemplateCameraRender,NativeCameraRender,AttachmentAdded,AttachmentDetached,SourceSeedSkeleton,RenderRequested,RenderSkipped,RenderGateFallback,RenderFailure,TypedCopyFallback,Count }
    [DataContract] internal sealed class MonitorPerfOptions
    {
        [DataMember] public int FormatVersion=1;
        [DataMember] public bool Enabled=false;
        [DataMember] public double IntervalSeconds=10;
        [DataMember] public double SpikeMilliseconds=50;
        [DataMember] public bool MeasureAllocations=true;
        [DataMember] public bool CullOffscreen=true;
        [DataMember] public bool ReuseVertexBuffer=true;
        [DataMember] public bool TypedPoseCopy=true;
        [DataMember] public bool MeasureThreadCpu=true;
        [OnDeserializing] private void Defaults(StreamingContext context){FormatVersion=1;IntervalSeconds=10;SpikeMilliseconds=50;MeasureAllocations=true;CullOffscreen=ReuseVertexBuffer=TypedPoseCopy=MeasureThreadCpu=true;}
        internal bool Valid {get {return FormatVersion==1&&IntervalSeconds>=10&&IntervalSeconds<=120&&SpikeMilliseconds>=16&&SpikeMilliseconds<=1000;}}
    }
    internal struct MonitorPerfContext
    {
        internal int Monitors,Near,Visible,Assigned,Ready,N15,N16,N17;
        internal int Motion; // -1 unknown, 0 still, 1 moving (player root >0.001 units/frame)
        internal int Bucket {get {int n=Monitors==0?0:Monitors==1?1:2;return ((((n*3+(Motion<0?0:Motion==0?1:2))*2+(Near>0?1:0))*2+(Visible>0?1:0))*2+(Assigned>0?1:0));}}
    }
    // Preallocated ring for exact recent-sample p95/p99, full-window count/mean/max.
    // No per-observation allocation, logging, file access or sorting.
    internal sealed class MonitorPerfSamples
    {
        internal const int Capacity=2048;
        private readonly double[] values=new double[Capacity];
        private int write,used;
        internal long Count,Bytes;
        internal double Sum,Max;
        internal int SampleCount {get {return used;}}
        internal void Add(double ms,long bytes)
        {Count++;Sum+=ms;if(ms>Max)Max=ms;if(bytes>0)Bytes+=bytes;values[write]=ms;write=(write+1)%Capacity;if(used<Capacity)used++;}
        internal double Percentile(double fraction,double[] scratch)
        {if(used==0)return 0;Array.Copy(values,scratch,used);Array.Sort(scratch,0,used);return scratch[Math.Max(0,(int)Math.Ceiling(used*fraction)-1)];}
        internal void Append(StringBuilder text,string name,double[] scratch,bool allocationMeasured=true)
        {
            if(Count==0)return;text.Append("\n ").Append(name).Append(" n=").Append(Count).Append(" avgMs=").Append((Sum/Count).ToString("F3",CultureInfo.InvariantCulture))
                .Append(" maxMs=").Append(Max.ToString("F3",CultureInfo.InvariantCulture)).Append(" p95Ms=").Append(Percentile(.95,scratch).ToString("F3",CultureInfo.InvariantCulture))
                .Append(" p99Ms=").Append(Percentile(.99,scratch).ToString("F3",CultureInfo.InvariantCulture)).Append(" recentSamples=").Append(used).Append(" inclusiveThreadBytes=").Append(allocationMeasured?Bytes:-1);
        }
        internal void Clear(){Count=Bytes=0;Sum=Max=0;write=used=0;}
    }
    internal struct MonitorPerfScope : IDisposable
    {
        private readonly MonitorPerfStage stage;private readonly long start,bytes;private readonly int g0,g1,g2,npc,monitor;private readonly double cpu;
        internal MonitorPerfScope(MonitorPerfStage stage,long start,long bytes,int npc,int monitor,bool measureCpu)
        {this.stage=stage;this.start=start;this.bytes=bytes;this.npc=npc;this.monitor=monitor;g0=GC.CollectionCount(0);g1=GC.CollectionCount(1);g2=GC.CollectionCount(2);cpu=measureCpu?MonitorThreadCpu.Read():-1;}
        public void Dispose(){if(start!=0)MonitorPerformance.End(stage,start,bytes,g0,g1,g2,cpu,npc,monitor);}
    }
    // Main-thread diagnostic only. Disabled by default, controlled by a NEW sidecar.
    // Does not own or modify any gameplay object, settings, rendering or timing.
    internal static partial class MonitorPerformance
    {
        internal static bool Enabled {get;private set;}
        private static MonitorPerfOptions options;
        private static Action<string> sink;
        private static MonitorPerfSamples[] stages,contexts;
        private static readonly string[] stageNames=Enum.GetNames(typeof(MonitorPerfStage)),eventNames=Enum.GetNames(typeof(MonitorPerfEvent));
        private static long[] events;
        private static double[] scratch;
        private static Func<long> allocationCounter;
        private static long windowStart,allocatedStart;
        private static int gc0,gc1,gc2,deltaGc0,deltaGc1,deltaGc2,frames,spikes,gcSpikeFrames,frameRenders,maxRenders,minMonitors,maxMonitors;
        private static int lastFrame=-1,observedFrame=-1;
        private static double workMs,previousReportMs;
        private static MonitorPerfContext context;
        internal static void Initialize(string folder,Action<string> output)
        {
            string path=Path.Combine(folder,"WallHoleFaceDisplay.performance.json");
            if(!File.Exists(path))return;
            try{MonitorPerfOptions value;using(var input=File.OpenRead(path))value=(MonitorPerfOptions)new DataContractJsonSerializer(typeof(MonitorPerfOptions)).ReadObject(input);
                if(value==null||!value.Valid)throw new InvalidDataException("Invalid performance options");Configure(value,output);}
            catch(Exception e){try{output("[WHFDPerf] disabled; sidecar preserved; "+e.GetType().Name);}catch{}}
        }
        internal static void Configure(MonitorPerfOptions value,Action<string> output)
        {
            if(value==null||!value.Valid)throw new ArgumentException("Invalid diagnostic options");
            Enabled=false;options=value;sink=output;CullOffscreen=value.CullOffscreen;ReuseVertexBuffer=value.ReuseVertexBuffer;TypedPoseCopy=value.TypedPoseCopy;if(!value.Enabled||!MonitorDebugSettings.Enabled)return;
            stages=new MonitorPerfSamples[(int)MonitorPerfStage.Count];contexts=new MonitorPerfSamples[72];events=new long[(int)MonitorPerfEvent.Count];scratch=new double[MonitorPerfSamples.Capacity];
            for(int i=0;i<stages.Length;i++)stages[i]=new MonitorPerfSamples();for(int i=0;i<contexts.Length;i++)contexts[i]=new MonitorPerfSamples();
            allocationCounter=null;
            if(value.MeasureAllocations){try{var method=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",BindingFlags.Public|BindingFlags.Static,null,Type.EmptyTypes,null);
                    if(method!=null)allocationCounter=(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),method);}catch{}}
            allocationApiPresent=allocationCounter!=null;allocationProbePassed=ProbeAllocationCounter(allocationCounter,out allocationProbeBytes);if(!allocationProbePassed)allocationCounter=null;ResetDetails();
            windowStart=Stopwatch.GetTimestamp();allocatedStart=Allocated();gc0=GC.CollectionCount(0);gc1=GC.CollectionCount(1);gc2=GC.CollectionCount(2);
            frames=spikes=gcSpikeFrames=frameRenders=maxRenders=deltaGc0=deltaGc1=deltaGc2=0;minMonitors=int.MaxValue;maxMonitors=0;lastFrame=observedFrame=-1;workMs=previousReportMs=0;Enabled=true;
            SafeLog("[WHFDPerf] enabled; source=v0.2.49; positionFix48=true; intervalSeconds="+value.IntervalSeconds.ToString(CultureInfo.InvariantCulture)+"; allocationApiPresent="+allocationApiPresent+"; allocationProbePassed="+allocationProbePassed+"; allocationProbeBytes="+allocationProbeBytes+"; allocationCounter="+(allocationCounter!=null)+"; p95/p99=last2048Samples; timingsInclusive=true; GPU/nativeAllocations=unmeasured");
        }
        private static long Allocated(){try{return allocationCounter==null?-1:allocationCounter();}catch{allocationCounter=null;return -1;}}
        internal static MonitorPerfScope Measure(MonitorPerfStage stage)
        {return Enabled?new MonitorPerfScope(stage,Stopwatch.GetTimestamp(),Allocated(),currentNpc,currentMonitor,options.MeasureThreadCpu&&stage==MonitorPerfStage.TemplateAnimation):default(MonitorPerfScope);}
        internal static void End(MonitorPerfStage stage,long start,long before,int g0,int g1,int g2,double cpu,int npc,int monitor)
        {
            if(!Enabled)return;double ms=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;long now=before<0?-1:Allocated();
            stages[(int)stage].Add(ms,now<0||before<0?0:Math.Max(0,now-before));
            if(stage==MonitorPerfStage.Update||stage==MonitorPerfStage.LateUpdate||stage==MonitorPerfStage.MainCameraFlush)workMs+=ms;
            if(ms>=options.SpikeMilliseconds){double cpuEnd=cpu<0?-1:MonitorThreadCpu.Read();slowEvents.Add(new MonitorSlowEvent{Frame=observedFrame,Stage=stage,Npc=npc,Monitor=monitor,Gc0=Math.Max(0,GC.CollectionCount(0)-g0),Gc1=Math.Max(0,GC.CollectionCount(1)-g1),Gc2=Math.Max(0,GC.CollectionCount(2)-g2),WallMs=ms,CpuMs=cpuEnd<0||cpu<0?-1:Math.Max(0,cpuEnd-cpu),Bytes=now<0||before<0?-1:Math.Max(0,now-before)});}
        }
        internal static void Count(MonitorPerfEvent kind,int amount=1)
        {if(!Enabled)return;events[(int)kind]+=amount;if(kind==MonitorPerfEvent.TemplateCameraRender||kind==MonitorPerfEvent.NativeCameraRender)frameRenders+=amount;}
        internal static void BeginFrame(int frame,MonitorPerfContext value,double frameMs)
        {
            if(!Enabled||frame==observedFrame)return;if(observedFrame>=0&&lastFrame!=observedFrame)EndFrame(observedFrame);observedFrame=frame;context=value;frames++;workMs=0;frameRenders=0;
            minMonitors=Math.Min(minMonitors,value.Monitors);maxMonitors=Math.Max(maxMonitors,value.Monitors);
            int a=GC.CollectionCount(0),b=GC.CollectionCount(1),c=GC.CollectionCount(2);bool collected=a!=gc0||b!=gc1||c!=gc2;deltaGc0+=Math.Max(0,a-gc0);deltaGc1+=Math.Max(0,b-gc1);deltaGc2+=Math.Max(0,c-gc2);gc0=a;gc1=b;gc2=c;
            if(frameMs>=0&&!double.IsNaN(frameMs)&&!double.IsInfinity(frameMs)){stages[(int)MonitorPerfStage.FrameGap].Add(frameMs,0);contexts[value.Bucket].Add(frameMs,0);if(frameMs>=options.SpikeMilliseconds){spikes++;if(collected)gcSpikeFrames++;}}
        }
        internal static void EndFrame(int frame)
        {
            if(!Enabled||frame!=observedFrame||lastFrame==frame)return;lastFrame=frame;
            stages[(int)MonitorPerfStage.MonitorWork].Add(workMs,0);maxRenders=Math.Max(maxRenders,frameRenders);
            if((Stopwatch.GetTimestamp()-windowStart)/(double)Stopwatch.Frequency>=options.IntervalSeconds)Flush();
        }
        internal static void Flush()
        {
            if(!Enabled)return;long started=Stopwatch.GetTimestamp();double seconds=(started-windowStart)/(double)Stopwatch.Frequency;long bytes=Allocated();
            var text=new StringBuilder(8192);text.Append("[WHFDPerf] seconds=").Append(seconds.ToString("F2",CultureInfo.InvariantCulture)).Append(" frames=").Append(frames)
                .Append(" monitorMin/Max=").Append(frames==0?0:minMonitors).Append('/').Append(maxMonitors).Append(" currentNear/Visible/Assigned/Ready=").Append(context.Near).Append('/').Append(context.Visible).Append('/').Append(context.Assigned).Append('/').Append(context.Ready)
                .Append(" currentID15/16/17=").Append(context.N15).Append('/').Append(context.N16).Append('/').Append(context.N17).Append(" maxCameraCallsPerFrame=").Append(maxRenders)
                .Append(" spikeFrames=").Append(spikes).Append(" gcCoincidentSpikeFrames=").Append(gcSpikeFrames).Append(" gc0/1/2=").Append(deltaGc0).Append('/').Append(deltaGc1).Append('/').Append(deltaGc2)
                .Append(" managedHeapBytes=").Append(GC.GetTotalMemory(false)).Append(" mainThreadAllocatedBytes=").Append(bytes<0||allocatedStart<0?-1:Math.Max(0,bytes-allocatedStart))
                .Append(" allocationCounterAvailable=").Append(allocationCounter!=null).Append(" previousReportMs=").Append(previousReportMs.ToString("F3",CultureInfo.InvariantCulture));
            AppendDetails(text);
            for(int i=0;i<stages.Length;i++)stages[i].Append(text,stageNames[i],scratch,allocationCounter!=null&&i!=(int)MonitorPerfStage.FrameGap&&i!=(int)MonitorPerfStage.MonitorWork);
            for(int i=0;i<events.Length;i++)if(events[i]!=0)text.Append("\n event ").Append(eventNames[i]).Append('=').Append(events[i]);
            for(int i=0;i<contexts.Length;i++)if(contexts[i].Count>0){int bits=i;int assigned=bits%2;bits/=2;int visible=bits%2;bits/=2;int near=bits%2;bits/=2;int motion=bits%3;int count=bits/3;
                contexts[i].Append(text,"FrameGapContext monitors="+(count==2?"2+":count.ToString())+" motion="+(motion==0?"unknown":motion==1?"still":"moving")+" near="+near+" visible="+visible+" assigned="+assigned,scratch,false);}
            for(int i=0;i<stages.Length;i++)stages[i].Clear();for(int i=0;i<contexts.Length;i++)contexts[i].Clear();Array.Clear(events,0,events.Length);
            frames=spikes=gcSpikeFrames=maxRenders=deltaGc0=deltaGc1=deltaGc2=0;minMonitors=int.MaxValue;maxMonitors=0;windowStart=Stopwatch.GetTimestamp();
            SafeLog(text.ToString());previousReportMs=(Stopwatch.GetTimestamp()-started)*1000d/Stopwatch.Frequency;allocatedStart=Allocated();
        }
        private static void SafeLog(string message){try{if(sink!=null)sink(message);}catch{Enabled=false;}}
        internal static void Shutdown()
        {if(Enabled)Flush();slowEvents=null;Enabled=false;sink=null;stages=null;contexts=null;events=null;scratch=null;allocationCounter=null;}
    }
}
