using System;
using System.Linq;
using Spine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal sealed class IndependentSpineTrackStatus
    {
        internal string AnimationName { get; private set; }
        internal bool Loop { get; private set; }
        internal float Time { get; private set; }
        internal float MixDuration { get; private set; }
        internal bool Complete { get; private set; }
        internal IndependentSpineTrackStatus(TrackEntry entry)
        {
            AnimationName=entry.Animation.Name; Loop=entry.Loop;
            Time=entry.TrackTime; MixDuration=entry.MixDuration;
            Complete=!entry.Loop && entry.TrackTime>=entry.Animation.Duration;
        }
    }

    // Standalone evaluation of caller-reviewed nonsexual animation data only.
    // No facility/NPC lookup, renderer, UI, asset name policy or gameplay integration.
    // The caller treats SkeletonData as immutable; never accepts a live Skeleton/state.
    internal sealed class IndependentSpinePlayback : ISpinePoseSource
    {
        internal const float MaximumBlendSeconds=10f, MaximumStepSeconds=60f;
        private AnimationState state;
        private Skeleton pose;
        private readonly string[] animationNames;
        private bool faulted, disposed;
        internal Skeleton Pose { get { return disposed || faulted ? null : pose; } }
        Skeleton ISpinePoseSource.Pose {get {return Pose;}}
        bool ISpinePoseSource.TryAdvance(float seconds,out string reason) {return TryAdvance(seconds,out reason);}
        private IndependentSpinePlayback(SkeletonData data)
        {
            pose=new Skeleton(data);
            pose.SetToSetupPose();pose.UpdateWorldTransform();
            state=new AnimationState(new AnimationStateData(data));
            animationNames=data.Animations.Select(a=>a.Name).Distinct(StringComparer.Ordinal).OrderBy(n=>n,StringComparer.Ordinal).ToArray();
        }
        internal static bool TryCreate(SkeletonData data,out IndependentSpinePlayback controller,out string reason)
        {
            controller=null;reason=null;
            if(data==null) {reason="SkeletonDataMissing";return false;}
            try {controller=new IndependentSpinePlayback(data);return true;}
            catch(Exception error) {reason="SpineCreateFailure:"+error.GetType().Name;return false;}
        }
        // Preserve the original convenience API; unavailable instances expose no list.
        internal string[] GetAnimationNames()
        {
            string[] names; string reason;
            return TryGetAnimationNames(out names,out reason) ? names : new string[0];
        }
        internal bool TryGetAnimationNames(out string[] names,out string reason)
        {
            names=new string[0];
            if(!Available(out reason)) return false;
            names=(string[])animationNames.Clone();return true;
        }
        // Explicit restart API, including when the same animation is already assigned.
        internal bool TryPlay(int track,string animationName,bool loop,float blendSeconds,out string reason)
        {
            Animation animation;
            if(!TryResolve(track,animationName,blendSeconds,out animation,out reason)) return false;
            return PlayResolved(track,animation,loop,blendSeconds,out reason);
        }
        // Poll-safe selection API. Same animation identity + loop is a no-op, even
        // after a one-shot completes. Blend changes apply only on a later transition.
        internal bool TryEnsurePlaying(int track,string animationName,bool loop,float blendSeconds,out string reason)
        {
            Animation animation;
            if(!TryResolve(track,animationName,blendSeconds,out animation,out reason)) return false;
            try {
                var entry=state.GetCurrent(track);
                if(entry!=null && ReferenceEquals(entry.Animation,animation) && entry.Loop==loop) return true;
            } catch(Exception error) {return Fail(error,out reason);}
            return PlayResolved(track,animation,loop,blendSeconds,out reason);
        }
        private bool TryResolve(int track,string name,float blendSeconds,out Animation animation,out string reason)
        {
            animation=null;
            if(!Available(out reason)) return false;
            if(!TrackValid(track)) {reason="TrackMustBe0Or2";return false;}
            if(!FiniteRange(blendSeconds,MaximumBlendSeconds)) {reason="InvalidBlendSeconds";return false;}
            if(string.IsNullOrEmpty(name) || !animationNames.Contains(name,StringComparer.Ordinal)) {
                reason="AnimationUnavailable";return false;
            }
            try {
                animation=pose.Data.FindAnimation(name);
                if(animation==null) {reason="AnimationUnavailable";return false;}
                return true;
            } catch(Exception error) {return Fail(error,out reason);}
        }
        private bool PlayResolved(int track,Animation animation,bool loop,float blendSeconds,out string reason)
        {
            reason=null;
            try {
                var entry=state.SetAnimation(track,animation,loop);
                entry.MixDuration=blendSeconds;
                state.Apply(pose);pose.UpdateWorldTransform();return true;
            } catch(Exception error) {return Fail(error,out reason);}
        }
        internal bool TryAdvance(float seconds,out string reason)
        {
            if(!Available(out reason)) return false;
            if(!FiniteRange(seconds,MaximumStepSeconds)) {reason="InvalidStepSeconds";return false;}
            try {state.Update(seconds);state.Apply(pose);pose.UpdateWorldTransform();return true;}
            catch(Exception error) {return Fail(error,out reason);}
        }
        internal bool TryGetTrack(int track,out IndependentSpineTrackStatus status,out string reason)
        {
            status=null;
            if(!Available(out reason)) return false;
            if(!TrackValid(track)) {reason="TrackMustBe0Or2";return false;}
            try {
                var entry=state.GetCurrent(track);
                if(entry!=null) status=new IndependentSpineTrackStatus(entry);
                return true; // null means that the valid track has not been assigned.
            } catch(Exception error) {return Fail(error,out reason);}
        }
        private bool Available(out string reason)
        {
            reason=disposed?"Disposed":faulted?"SpineFaulted":null;
            return reason==null;
        }
        private bool Fail(Exception error,out string reason)
        {
            faulted=true;reason="SpineRuntimeFailure:"+error.GetType().Name;return false;
        }
        private static bool TrackValid(int value) {return value==0 || value==2;}
        private static bool FiniteRange(float value,float maximum) {return !float.IsNaN(value) && !float.IsInfinity(value) && value>=0 && value<=maximum;}
        public void Dispose()
        {
            if(disposed) return;
            disposed=true;state=null;pose=null;
        }
    }
}
