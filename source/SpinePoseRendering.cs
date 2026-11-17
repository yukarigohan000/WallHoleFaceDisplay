using System;
using System.Runtime.CompilerServices;
using Spine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Source owns its private Skeleton/state; renderer borrows only the evaluated pose.
    // Transfer exclusive ticking/disposal responsibility to the session: do not also
    // tick the source directly or give the same source to another session.
    internal interface ISpinePoseSource : IDisposable
    {
        Skeleton Pose { get; }
        bool TryAdvance(float seconds,out string reason);
    }
    internal interface ISpinePoseRenderer : IDisposable
    {
        // Must not animate, mutate or dispose the borrowed Skeleton/state.
        // Clear any retained pose/instruction references in Dispose, before source disposal.
        bool TryRender(Skeleton borrowedPose,out string reason);
    }

    // Unconnected nonsexual test/session coordinator. Owns no second Skeleton or state.
    internal sealed class SpinePoseRenderSession : IDisposable
    {
        private static readonly object OwnershipLock=new object();
        private static readonly ConditionalWeakTable<ISpinePoseSource,SpinePoseRenderSession> Sources=new ConditionalWeakTable<ISpinePoseSource,SpinePoseRenderSession>();
        private static readonly ConditionalWeakTable<ISpinePoseRenderer,SpinePoseRenderSession> Renderers=new ConditionalWeakTable<ISpinePoseRenderer,SpinePoseRenderSession>();
        private ISpinePoseSource source;
        private ISpinePoseRenderer renderer;
        private long lastFrame=-1;
        private bool disposed, faulted, busy, disposeRequested;
        internal string DisposalError {get;private set;}
        internal SpinePoseRenderSession(ISpinePoseSource source,ISpinePoseRenderer renderer)
        {
            if(source==null) throw new ArgumentNullException(nameof(source));
            if(renderer==null) throw new ArgumentNullException(nameof(renderer));
            lock(OwnershipLock) {
                SpinePoseRenderSession existing;
                if(Sources.TryGetValue(source,out existing) || Renderers.TryGetValue(renderer,out existing))
                    throw new InvalidOperationException("Pose source or renderer already owned");
                Sources.Add(source,this);Renderers.Add(renderer,this);
            }
            this.source=source;this.renderer=renderer;
        }
        internal bool TryTick(long frame,float seconds,out string reason)
        {
            reason=null;
            if(disposed) {reason="Disposed";return false;}
            if(faulted) {reason="SessionFaulted";return false;}
            if(busy) {reason="ReentrantTick";return false;}
            if(frame<0 || frame<=lastFrame) {reason="FrameAlreadyProcessedOrInvalid";return false;}
            if(float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds<0 || seconds>IndependentSpinePlayback.MaximumStepSeconds) {
                reason="InvalidStepSeconds";return false;
            }
            busy=true;
            try {
                var pose=source.Pose;
                if(pose==null) {faulted=true;reason="PoseUnavailable";return false;}
                // Consume once even if an update/render fails after partial work.
                lastFrame=frame;
                if(!source.TryAdvance(seconds,out reason)) {faulted=true;return false;}
                if(!ReferenceEquals(pose,source.Pose)) {faulted=true;reason="PoseIdentityChanged";return false;}
                if(!renderer.TryRender(pose,out reason)) {faulted=true;return false;}
                return true;
            } catch(Exception error) {faulted=true;reason="SessionFailure:"+error.GetType().Name;return false;}
            finally {busy=false;if(disposeRequested) Dispose();}
        }
        public void Dispose()
        {
            if(disposed) return;
            if(busy) {disposeRequested=true;return;}
            disposed=true;
            var sink=renderer;var owner=source;
            renderer=null;source=null;
            try {sink.Dispose();}
            catch(Exception error) {DisposalError="RendererDisposeFailure:"+error.GetType().Name;}
            finally {
                try {owner.Dispose();}
                catch(Exception error) {DisposalError=(DisposalError==null?"":DisposalError+";")+"SourceDisposeFailure:"+error.GetType().Name;}
                finally { lock(OwnershipLock) {Sources.Remove(owner);Renderers.Remove(sink);} }
            }
        }
    }
}
