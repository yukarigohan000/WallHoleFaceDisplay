using Spine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Private evaluator; selection can later be supplied by a manual policy without UI coupling.
    internal sealed class MonitorFacePlayback
    {
        private readonly Skeleton baseline;
        private readonly AnimationState state;
        private TrackEntry selectedSource, playing;
        internal Skeleton Pose { get; private set; }
        internal bool IsPlaying { get { return playing != null; } }

        internal MonitorFacePlayback(Skeleton baseline)
        {
            this.baseline = baseline;
            Pose = MonitorFacePose.CaptureFull(baseline);
            state = new AnimationState(new AnimationStateData(baseline.Data));
        }

        internal bool Advance(TrackEntry selection, float deltaTime, float sourceStateScale)
        {
            bool changed = !ReferenceEquals(selectedSource, selection);
            if (changed)
            {
                selectedSource = selection;
                // This class never mutates the source entry, animation, state or mix table.
                playing = state.SetAnimation(MonitorExpressionSelection.FacialTrack, selection.Animation, selection.Loop);
                playing.AnimationStart = selection.AnimationStart; playing.AnimationEnd = selection.AnimationEnd;
                playing.TrackTime = selection.TrackTime;
            }
            playing.TimeScale = selection.TimeScale; playing.Alpha = selection.Alpha;
            state.TimeScale = sourceStateScale;
            MonitorFacePose.RestoreForAnimation(baseline, Pose);
            state.Update(changed ? 0 : deltaTime);
            state.Apply(Pose);
            Pose.UpdateWorldTransform(); // Private constraints, with captured runtime settings.
            MonitorFacePose.Mask(Pose);
            return changed;
        }
    }
}
