using System;
using System.Globalization;
using Spine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Scalar snapshot only: no retained TrackEntry, state or Skeleton; safe across pooling.
    internal sealed class MonitorPlaybackTimingSample
    {
        internal readonly bool HasTrack,HasAnimation,Loop;
        internal readonly string Animation;
        internal readonly float Duration,TrackTime,AnimationTime,TrackScale,TemplateScale,StateScale;
        private MonitorPlaybackTimingSample(TrackEntry entry,float templateScale,float stateScale)
        {
            HasTrack=entry!=null;HasAnimation=HasTrack && entry.Animation!=null;
            Animation=HasAnimation?entry.Animation.Name:"<none>";
            Duration=HasAnimation?entry.Animation.Duration:float.NaN;
            TrackTime=HasTrack?entry.TrackTime:float.NaN;
            AnimationTime=HasAnimation?entry.AnimationTime:float.NaN;
            TrackScale=HasTrack?entry.TimeScale:float.NaN;
            Loop=HasTrack && entry.Loop;TemplateScale=templateScale;StateScale=stateScale;
        }
        internal static MonitorPlaybackTimingSample Capture(TrackEntry entry,float templateScale,float stateScale)
        {return new MonitorPlaybackTimingSample(entry,templateScale,stateScale);}
        private static string F(float value){return value.ToString("R",CultureInfo.InvariantCulture);}
        private static string Safe(string value){return (value??"<null>").Replace("\r","\\r").Replace("\n","\\n");}
        internal string Fields {get {return "animation="+Safe(Animation)+"; duration="+F(Duration)+"; trackTime="+F(TrackTime)+
            "; animationTime="+F(AnimationTime)+"; trackScale="+F(TrackScale)+"; templateScale="+F(TemplateScale)+
            "; stateScale="+F(StateScale)+"; loop="+Loop+"; hasTrack="+HasTrack+"; hasAnimation="+HasAnimation;}}
        internal string CompletionReason {get {return !HasTrack?"NoTrack":!HasAnimation?"NoAnimation":
            !Loop && TrackTime>=Duration?"DurationReached":"Other";}}
        internal string HoldReason(int requested,int selected)
        {
            if(selected!=3)return "None-mode-changed";
            if(!HasAnimation)return "NoPlayableTrack";
            if(!Loop && TrackTime>=Duration)return requested==3?"NativeStillMode3-nonloop-final-pose":
                requested<0?"NativeUnknown-nonloop-final-pose":"ModeSwitchNotApplied-nonloop-final-pose";
            return "Mode3-retained";
        }
    }
}
