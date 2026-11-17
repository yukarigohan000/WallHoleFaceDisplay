using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Spine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal static class MonitorFacilityRecipe
    {
        // Data reviewed against RandomCharacter.CreateRestrain and actual sexList[5].
        // One common playback path; these are native model indices, not per-ID animation branches.
        private static readonly Dictionary<int,int> Reviewed=new Dictionary<int,int>{{15,0},{16,1},{17,3},{44,4},{90,2}};
        internal static bool TryIndex(int id,out int index){return Reviewed.TryGetValue(id,out index);}
        internal static readonly string[] Body={"P_restrain","P_restrain_attach_00","P_restrain_attach_01","P_restrain_attach_02"};
        internal const string Face="Face_01";
        internal static string DefaultFace(int npcId){return npcId==44?"":Face;}
        internal static void Validate(Skeleton pose,int npcId=-1)
        {
            if(pose==null)throw new InvalidOperationException("Facility Skeleton unavailable");
            foreach(var name in Body)if(pose.Data.FindAnimation(name)==null)throw new InvalidOperationException("Facility animation unavailable: "+name);
            if(DefaultFace(npcId)!=""&&pose.Data.FindAnimation(DefaultFace(npcId))==null)throw new InvalidOperationException("Facility face animation unavailable: "+Face);
        }
        internal static void Prepare(Skeleton pose,int npcId=-1)
        {
            Validate(pose,npcId);
            // Same private-slot accessory and chain setup as the successful ID15 recipe,
            // without applying an animation from a different SkeletonData.
            var vib=pose.FindSlot("Vib");var attachment=vib==null?null:pose.GetAttachment(vib.Data.Index,RestraintVideoRecipe.Accessory);
            if(vib==null||attachment==null)throw new InvalidOperationException("Facility accessory unavailable");vib.Attachment=attachment;
            foreach(var name in new[]{"R_foot_ring","L_foot_ring","L_hand_ring","R_hand_ring","L_hand_chain","L_foot_chain","R_hand_chain","R_foot_chain"}){
                var slot=pose.FindSlot(name);if(slot==null)throw new InvalidOperationException("Facility chain slot unavailable: "+name);
                var value=pose.GetAttachment(slot.Data.Index,name+"_01")??pose.GetAttachment(slot.Data.Index,name+"_00");if(value!=null)slot.Attachment=value;
            }
            pose.SetBonesToSetupPose();foreach(var slot in pose.Slots)slot.Deform.Clear();pose.DrawOrder.Clear();pose.DrawOrder.AddRange(pose.Slots);pose.UpdateWorldTransform();
        }
    }
    [DataContract]
    internal sealed class MonitorFacilityTracks
    {
        [DataMember(EmitDefaultValue=false)] public string Body,Face;
        internal MonitorFacilityTracks Copy(){return (MonitorFacilityTracks)MemberwiseClone();}
    }
    [DataContract]
    internal sealed class MonitorFacilityTrackDocument
    {
        [DataMember(IsRequired=true)] public int FormatVersion=1;
        [DataMember(IsRequired=true)] public int NpcId;
        [DataMember(EmitDefaultValue=false)] public MonitorFacilityTracks Common,Mode0,Mode1,Mode2,Mode3;
        internal MonitorFacilityTracks Mode(int mode){switch(mode){case -1:return Common;case 0:return Mode0;case 1:return Mode1;case 2:return Mode2;case 3:return Mode3;default:throw new ArgumentOutOfRangeException("mode");}}
        internal MonitorFacilityTracks Edit(int mode){switch(mode){case -1:return Common??(Common=new MonitorFacilityTracks());case 0:return Mode0??(Mode0=new MonitorFacilityTracks());case 1:return Mode1??(Mode1=new MonitorFacilityTracks());case 2:return Mode2??(Mode2=new MonitorFacilityTracks());case 3:return Mode3??(Mode3=new MonitorFacilityTracks());default:throw new ArgumentOutOfRangeException("mode");}}
        internal MonitorFacilityTrackDocument Copy(){return new MonitorFacilityTrackDocument{FormatVersion=FormatVersion,NpcId=NpcId,Common=Common==null?null:Common.Copy(),Mode0=Mode0==null?null:Mode0.Copy(),Mode1=Mode1==null?null:Mode1.Copy(),Mode2=Mode2==null?null:Mode2.Copy(),Mode3=Mode3==null?null:Mode3.Copy()};}
    }
    internal sealed class MonitorFacilityAnimationSettings
    {
        private readonly string path;private readonly int id;private MonitorFacilityTrackDocument saved;
        internal string Error {get;private set;}
        internal MonitorFacilityAnimationSettings(string folder,int id)
        {
            int index;if(!MonitorFacilityRecipe.TryIndex(id,out index)||id==15)throw new ArgumentOutOfRangeException("id");this.id=id;
            path=Path.Combine(folder,"WallHoleFaceDisplay.facility-animations-"+id+".json");saved=new MonitorFacilityTrackDocument{NpcId=id};if(!MonitorStandardDefaults.Exists(path))return;
            try{using(var input=MonitorStandardDefaults.OpenRead(path))saved=(MonitorFacilityTrackDocument)Serializer().ReadObject(input);if(saved==null||saved.FormatVersion!=1||saved.NpcId!=id)throw new InvalidDataException();}
            catch(Exception e){saved=MonitorStandardDefaults.Fallback<MonitorFacilityTrackDocument>(path,()=>new MonitorFacilityTrackDocument{NpcId=id});Error="Facility animation settings preserved; defaults active; save disabled: "+e.GetType().Name;}
            if(Error==null)Error=MonitorStandardDefaults.Warning(path);
        }
        private static DataContractJsonSerializer Serializer(){return new DataContractJsonSerializer(typeof(MonitorFacilityTrackDocument));}
        internal MonitorNpcValues Apply(int mode,MonitorNpcValues values)
        {
            var result=Defaults(mode,values,id);var selected=saved.Mode(mode);var common=saved.Common;
            result.Body=(selected==null?null:selected.Body)??(common==null?null:common.Body)??result.Body;
            result.Face=(selected==null?null:selected.Face)??(common==null?null:common.Face)??result.Face;
            return result;
        }
        internal static MonitorNpcValues Defaults(int mode,MonitorNpcValues values,int npcId=-1)
        {if(mode<-1||mode>3)throw new ArgumentOutOfRangeException("mode");var result=values.Copy();result.Body=MonitorFacilityRecipe.Body[mode<0?0:mode];result.Face=MonitorFacilityRecipe.DefaultFace(npcId);return result;}
        internal void Save(int mode,MonitorNpcValues value,string[] available)
        {
            if(!MonitorNpcProfileSettings.AnimationValid(value.Body,available)||!MonitorNpcProfileSettings.AnimationValid(value.Face,available))throw new InvalidDataException("Facility animation unavailable");
            var next=saved.Copy();var selected=next.Edit(mode);selected.Body=value.Body??"";selected.Face=value.Face??"";Write(next);
        }
        internal void Clear(int mode){if(mode<0)throw new ArgumentOutOfRangeException("mode");var next=saved.Copy();var selected=next.Edit(mode);selected.Body=selected.Face=null;Write(next);}
        private void Write(MonitorFacilityTrackDocument next)
        {
            if(Error!=null)throw new InvalidOperationException(Error);if(next.NpcId!=id||next.FormatVersion!=1)throw new InvalidDataException();Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){Serializer().WriteObject(output,next);output.Flush(true);}if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);saved=next;}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
