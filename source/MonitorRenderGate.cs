using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Defers ONLY offscreen Camera.Render, not Skeleton/AnimationState advancement.
    // Tests the live main camera in onPreCull, after camera movement/LateUpdate.
    internal sealed class MonitorRenderGate : IDisposable
    {
        private static readonly List<MonitorRenderGate> entries=new List<MonitorRenderGate>();
        private static readonly Plane[] planes=new Plane[6];
        private static int active;private static bool flushing;
        private readonly Camera camera;private readonly MonitorPerfStage stage;private readonly MonitorPerfEvent counter;
        private MeshRenderer screen;private bool pending,disposed,reportedFailure;private int requestedFrame=-1,skippedFrame=-1,npc=-1,monitor=-1;
        internal MonitorRenderGate(Camera camera,MonitorPerfStage stage,MonitorPerfEvent counter)
        {
            this.camera=camera;this.stage=stage;this.counter=counter;
            int index=entries.IndexOf(null);if(index<0)entries.Add(this);else entries[index]=this;
            if(active++==0){Camera.onPreCull+=BeforeMain;Camera.onPostRender+=AfterMain;}
        }
        internal static bool CompletesOnMainCamera
        {get {var main=Camera.main;return MonitorPerformance.CullOffscreen&&active>0&&main!=null&&main.enabled&&main.gameObject.activeInHierarchy;}}
        internal void Request(MeshRenderer renderer,bool force,int npcId,int monitorId)
        {
            if(disposed)return;screen=renderer;npc=npcId;monitor=monitorId;pending=true;requestedFrame=Time.frameCount;
            MonitorPerformance.Count(MonitorPerfEvent.RenderRequested);
            if(!MonitorPerformance.CullOffscreen||force||renderer==null||!CompletesOnMainCamera)Draw();
        }
        private void Draw()
        {
            if(!pending||disposed||camera==null)return;pending=false;
            using(MonitorPerformance.Owner(npc,monitor))using(MonitorPerformance.Measure(stage)){MonitorPerformance.Count(counter);camera.Render();}
        }
        private void DrawDeferred()
        {try{Draw();}catch(Exception e){MonitorPerformance.Count(MonitorPerfEvent.RenderFailure);if(!reportedFailure){reportedFailure=true;Debug.LogWarning("[WallHoleFaceDisplay] deferred render failed; npcId="+npc+"; monitorId="+monitor+"; "+e.GetType().Name);}}}
        private static void BeforeMain(Camera rendering)
        {
            if(flushing||!MonitorPerformance.CullOffscreen||rendering==null||rendering!=Camera.main)return;
            using(MonitorPerformance.Measure(MonitorPerfStage.MainCameraFlush)){
                flushing=true;
                try{GeometryUtility.CalculateFrustumPlanes(rendering,planes);
                    for(int i=0;i<entries.Count;i++){var item=entries[i];if(item==null||!item.pending||item.disposed||item.requestedFrame!=Time.frameCount)continue;
                        var screen=item.screen;
                        if(screen!=null&&screen.enabled&&screen.gameObject.activeInHierarchy&&(rendering.cullingMask&(1<<screen.gameObject.layer))!=0&&GeometryUtility.TestPlanesAABB(planes,screen.bounds))item.DrawDeferred();
                        else if(item.skippedFrame!=Time.frameCount){item.skippedFrame=Time.frameCount;MonitorPerformance.Count(MonitorPerfEvent.RenderSkipped);}
                    }
                }catch(Exception){MonitorPerformance.Count(MonitorPerfEvent.RenderGateFallback);
                    // Conservative fallback: keep independent output up to date.
                    for(int i=0;i<entries.Count;i++){var item=entries[i];if(item!=null&&item.pending&&item.requestedFrame==Time.frameCount)item.DrawDeferred();}}
                finally{flushing=false;}
            }
        }
        private static void AfterMain(Camera rendering)
        {if(!flushing&&MonitorPerformance.CullOffscreen&&rendering!=null&&rendering==Camera.main)MonitorPerformance.EndFrame(Time.frameCount);}
        public void Dispose()
        {
            if(disposed)return;disposed=true;pending=false;screen=null;int index=entries.IndexOf(this);if(index>=0)entries[index]=null;
            if(--active==0){Camera.onPreCull-=BeforeMain;Camera.onPostRender-=AfterMain;entries.Clear();}
        }
    }
}
