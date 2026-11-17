namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // All three providers use the same scope. No gameplay state detection here.
    internal static class MonitorPoseSelection
    {
        internal static MonitorPoseValues Resolve(MonitorPoseSettings settings,MonitorPoseValues common,
            object requester,object selected,object draftOwner,int playbackMode,int editScope,bool preview,
            MonitorArmValues arms,MonitorHeadValues head,MonitorTorsoValues torso)
        {
            bool ownsPreview=preview && requester!=null && ReferenceEquals(requester,selected) && ReferenceEquals(requester,draftOwner);
            var value=settings.Resolve(ownsPreview?editScope:playbackMode,common);
            if(ownsPreview){if(arms!=null)value.Arms=arms.Copy();if(head!=null)value.Head=head.Copy();if(torso!=null)value.Torso=torso.Copy();}
            return value;
        }
    }
}
