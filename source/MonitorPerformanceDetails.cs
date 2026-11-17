using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Globalization;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal struct MonitorPerfOwnerScope : IDisposable
    {
        private readonly int npc,monitor;
        internal MonitorPerfOwnerScope(int npc,int monitor){this.npc=npc;this.monitor=monitor;}
        public void Dispose(){MonitorPerformance.SetOwner(npc,monitor);}
    }
    internal struct MonitorSlowEvent
    {
        internal int Frame,Npc,Monitor,Gc0,Gc1,Gc2;
        internal MonitorPerfStage Stage;
        internal double WallMs,CpuMs;
        internal long Bytes;
    }
    internal sealed class MonitorSlowEvents
    {
        internal const int Capacity=64;
        private readonly MonitorSlowEvent[] values=new MonitorSlowEvent[Capacity];
        private int next,used;internal int Total {get;private set;}
        internal void Add(MonitorSlowEvent value){values[next]=value;next=(next+1)%Capacity;if(used<Capacity)used++;Total++;}
        internal void Append(StringBuilder text)
        {
            text.Append("\n slowEventTotal=").Append(Total).Append(" retained=").Append(used).Append(" dropped=").Append(Total-used);
            for(int i=0;i<used;i++){var e=values[(next-used+i+Capacity)%Capacity];text.Append("\n slow frame=").Append(e.Frame).Append(" stage=").Append(e.Stage)
                .Append(" npcId=").Append(e.Npc).Append(" monitorId=").Append(e.Monitor).Append(" wallMs=").Append(e.WallMs.ToString("F3",CultureInfo.InvariantCulture))
                .Append(" threadCpuMs=").Append(e.CpuMs.ToString("F3",CultureInfo.InvariantCulture)).Append(" gc0/1/2=").Append(e.Gc0).Append('/').Append(e.Gc1).Append('/').Append(e.Gc2).Append(" allocatedBytes=").Append(e.Bytes);}
        }
        internal void Clear(){next=used=0;Total=0;}
    }
    internal static class MonitorThreadCpu
    {
        private static bool supported=Environment.OSVersion.Platform==PlatformID.Win32NT;
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetThreadTimes(IntPtr thread,out long created,out long exited,out long kernel,out long user);
        internal static double Read()
        {if(!supported)return -1;try{long c,e,k,u;if(GetThreadTimes(GetCurrentThread(),out c,out e,out k,out u))return (k+u)/10000d;}catch{}supported=false;return -1;}
    }
    internal static partial class MonitorPerformance
    {
        internal static bool CullOffscreen {get;private set;}=true;
        internal static bool ReuseVertexBuffer {get;private set;}=true;
        internal static bool TypedPoseCopy {get;private set;}=true;
        private static int currentNpc=-1,currentMonitor=-1;
        private static MonitorSlowEvents slowEvents;
        private static bool allocationApiPresent,allocationProbePassed;
        private static long allocationProbeBytes;
        internal static void SetOwner(int npc,int monitor){currentNpc=npc;currentMonitor=monitor;}
        internal static MonitorPerfOwnerScope Owner(int npc,int monitor){var old=new MonitorPerfOwnerScope(currentNpc,currentMonitor);SetOwner(npc,monitor);return old;}
        internal static bool ProbeAllocationCounter(Func<long> counter,out long delta)
        {
            delta=-1;if(counter==null)return false;
            try{long before=counter();var probe=new byte[8192];probe[0]=1;long after=counter();GC.KeepAlive(probe);delta=after-before;return delta>=8192;}catch{return false;}
        }
        private static void ResetDetails()
        {slowEvents=new MonitorSlowEvents();currentNpc=currentMonitor=-1;}
        private static void AppendDetails(StringBuilder text)
        {
            text.Append(" allocationApiPresent=").Append(allocationApiPresent).Append(" allocationProbePassed=").Append(allocationProbePassed).Append(" allocationProbeBytes=").Append(allocationProbeBytes)
                .Append(" CullOffscreen=").Append(CullOffscreen).Append(" ReuseVertexBuffer=").Append(ReuseVertexBuffer).Append(" TypedPoseCopy=").Append(TypedPoseCopy);
            if(slowEvents!=null){slowEvents.Append(text);slowEvents.Clear();}
        }
    }
}
