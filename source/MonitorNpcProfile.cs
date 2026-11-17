using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    [DataContract]
    internal sealed class MonitorNpcValues
    {
        [DataMember(EmitDefaultValue=false)] public float? LeftX,LeftY,RightX,RightY,HeadRotation,TorsoRotation,X,Y,Zoom;
        // Null inherits; empty string explicitly selects no animation.
        [DataMember(EmitDefaultValue=false)] public string Body,Face;
        internal MonitorNpcValues Copy(){return (MonitorNpcValues)MemberwiseClone();}
        private static bool Check(float? v,float min,float max){return !v.HasValue||(!float.IsNaN(v.Value)&&!float.IsInfinity(v.Value)&&v.Value>=min&&v.Value<=max);}
        internal bool Valid {get {return Check(LeftX,-1,1)&&Check(LeftY,-1,1)&&Check(RightX,-1,1)&&Check(RightY,-1,1)&&Check(HeadRotation,-180,180)&&Check(TorsoRotation,-180,180)&&Check(X,-2,2)&&Check(Y,-2,2)&&Check(Zoom,.25f,4);}}
        internal static MonitorNpcValues Resolve(MonitorNpcValues common,MonitorNpcValues mode)
        {
            common=common??new MonitorNpcValues();mode=mode??new MonitorNpcValues();
            return new MonitorNpcValues {LeftX=mode.LeftX??common.LeftX??0,LeftY=mode.LeftY??common.LeftY??0,RightX=mode.RightX??common.RightX??0,RightY=mode.RightY??common.RightY??0,
                HeadRotation=mode.HeadRotation??common.HeadRotation??0,TorsoRotation=mode.TorsoRotation??common.TorsoRotation??0,
                X=mode.X??common.X??0,Y=mode.Y??common.Y??0,Zoom=mode.Zoom??common.Zoom??1,Body=mode.Body??common.Body??"",Face=mode.Face??common.Face??""};
        }
        internal static void CopyGroup(MonitorNpcValues target,MonitorNpcValues value,string group)
        {
            switch(group){case "arms":target.LeftX=value.LeftX;target.LeftY=value.LeftY;target.RightX=value.RightX;target.RightY=value.RightY;break;
                case "head":target.HeadRotation=value.HeadRotation;break;case "torso":target.TorsoRotation=value.TorsoRotation;break;
                case "frame":target.X=value.X;target.Y=value.Y;target.Zoom=value.Zoom;break;case "animation":target.Body=value.Body;target.Face=value.Face;break;default:throw new ArgumentException("group");}
        }
    }
    [DataContract]
    internal sealed class MonitorNpcProfile
    {
        [DataMember(IsRequired=true)] public int FormatVersion=1;
        [DataMember(IsRequired=true)] public int NpcId;
        [DataMember(EmitDefaultValue=false)] public MonitorNpcValues Common,Mode0,Mode1,Mode2,Mode3;
        internal MonitorNpcValues Mode(int mode){switch(mode){case -1:return Common;case 0:return Mode0;case 1:return Mode1;case 2:return Mode2;case 3:return Mode3;default:return null;}}
        internal MonitorNpcValues Edit(int mode){switch(mode){case -1:return Common??(Common=new MonitorNpcValues());case 0:return Mode0??(Mode0=new MonitorNpcValues());case 1:return Mode1??(Mode1=new MonitorNpcValues());case 2:return Mode2??(Mode2=new MonitorNpcValues());case 3:return Mode3??(Mode3=new MonitorNpcValues());default:throw new ArgumentOutOfRangeException("mode");}}
        internal MonitorNpcProfile Copy(){return new MonitorNpcProfile {FormatVersion=FormatVersion,NpcId=NpcId,Common=Common==null?null:Common.Copy(),Mode0=Mode0==null?null:Mode0.Copy(),Mode1=Mode1==null?null:Mode1.Copy(),Mode2=Mode2==null?null:Mode2.Copy(),Mode3=Mode3==null?null:Mode3.Copy()};}
        internal bool Valid(int id){return FormatVersion==1&&NpcId==id&&id>=0&&new[]{Common,Mode0,Mode1,Mode2,Mode3}.All(v=>v==null||v.Valid);}
    }
    internal sealed class MonitorNpcProfileSettings
    {
        private readonly string path;private MonitorNpcProfile saved;
        private MonitorFacilityAnimationSettings facilityAnimations;
        internal MonitorFacilityAnimationSettings FacilityAnimations {get {return facilityAnimations??(facilityAnimations=new MonitorFacilityAnimationSettings(Path.GetDirectoryName(path),NpcId));}}
        internal int NpcId {get;private set;}
        internal string Error {get;private set;}
        internal MonitorNpcProfileSettings(string folder,int id)
        {
            if(id<0||id==15)throw new ArgumentOutOfRangeException("id","ID 15 uses the unchanged compatibility settings");
            NpcId=id;path=Path.Combine(folder,"WallHoleFaceDisplay.profile-"+id.ToString(CultureInfo.InvariantCulture)+".json");saved=new MonitorNpcProfile {NpcId=id};
            if(!MonitorStandardDefaults.Exists(path))return;
            try{using(var input=MonitorStandardDefaults.OpenRead(path))saved=(MonitorNpcProfile)Serializer().ReadObject(input);if(saved==null||!saved.Valid(id))throw new InvalidDataException();}
            catch(Exception e){saved=MonitorStandardDefaults.Fallback<MonitorNpcProfile>(path,()=>new MonitorNpcProfile {NpcId=id});Error="Profile preserved; safe defaults; save disabled: "+e.GetType().Name;}
            if(Error==null)Error=MonitorStandardDefaults.Warning(path);
        }
        private static DataContractJsonSerializer Serializer(){return new DataContractJsonSerializer(typeof(MonitorNpcProfile));}
        internal string AuditFrameSource(int mode){var v=mode<0?null:saved.Mode(mode);return "X="+(v!=null&&v.X.HasValue?"mode":"common/default")+",Y="+(v!=null&&v.Y.HasValue?"mode":"common/default")+",Zoom="+(v!=null&&v.Zoom.HasValue?"mode":"common/default");}
        internal MonitorNpcValues Get(int mode){return MonitorNpcValues.Resolve(saved.Common,mode<0?null:saved.Mode(mode));}
        internal void Save(int mode,string group,MonitorNpcValues draft,string[] available)
        {
            if(Error!=null)throw new InvalidOperationException(Error);
            if(draft==null||!draft.Valid)throw new InvalidDataException("Invalid profile values");
            var next=saved.Copy();var v=next.Edit(mode);
            switch(group){
                case "arms":v.LeftX=draft.LeftX;v.LeftY=draft.LeftY;v.RightX=draft.RightX;v.RightY=draft.RightY;break;
                case "head":v.HeadRotation=draft.HeadRotation;break;
                case "torso":v.TorsoRotation=draft.TorsoRotation;break;
                case "frame":v.X=draft.X;v.Y=draft.Y;v.Zoom=draft.Zoom;break;
                case "animation":if(!AnimationValid(draft.Body,available)||!AnimationValid(draft.Face,available))throw new InvalidDataException("Animation unavailable in this SkeletonData");v.Body=draft.Body??"";v.Face=draft.Face??"";break;
                default:throw new ArgumentException("Unknown profile group");
            }
            Write(next);
        }
        internal static bool AnimationValid(string name,string[] available){return string.IsNullOrEmpty(name)||(available!=null&&Array.IndexOf(available,name)>=0);}
        internal void Clear(int mode,string group)
        {
            if(mode<0)throw new InvalidOperationException("Common values cannot inherit");var next=saved.Copy();var v=next.Edit(mode);
            switch(group){case "arms":v.LeftX=v.LeftY=v.RightX=v.RightY=null;break;case "head":v.HeadRotation=null;break;case "torso":v.TorsoRotation=null;break;case "frame":v.X=v.Y=v.Zoom=null;break;case "animation":v.Body=v.Face=null;break;default:throw new ArgumentException("group");}Write(next);
        }
        private void Write(MonitorNpcProfile next)
        {
            if(Error!=null)throw new InvalidOperationException(Error);if(!next.Valid(NpcId))throw new InvalidDataException();Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){Serializer().WriteObject(output,next);output.Flush(true);}if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);saved=next;}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
    internal sealed class MonitorNpcProfiles
    {
        private readonly string folder;private readonly Dictionary<int,MonitorNpcProfileSettings> profiles=new Dictionary<int,MonitorNpcProfileSettings>();
        internal MonitorNpcProfiles(string folder){this.folder=folder;}
        internal MonitorNpcProfileSettings Get(int id){MonitorNpcProfileSettings result;if(!profiles.TryGetValue(id,out result)){result=new MonitorNpcProfileSettings(folder,id);profiles.Add(id,result);}return result;}
    }
    internal static class MonitorNpcInput
    {
        internal static bool TryParse(string text,out int id){return int.TryParse(text,System.Globalization.NumberStyles.None,CultureInfo.InvariantCulture,out id)&&id>=0;}
    }
    internal static class MonitorNpcPreview
    {
        internal static MonitorNpcValues Resolve(MonitorNpcProfileSettings settings,object requester,object selected,object draftOwner,int draftId,int actualMode,int scope,bool preview,string group,MonitorNpcValues draft,Func<int,MonitorNpcValues> resolve=null)
        {
            bool owns=preview&&requester!=null&&ReferenceEquals(requester,selected)&&ReferenceEquals(requester,draftOwner)&&settings.NpcId==draftId;
            var result=resolve==null?settings.Get(owns?scope:actualMode):resolve(owns?scope:actualMode);if(owns&&draft!=null&&draft.Valid)MonitorNpcValues.CopyGroup(result,draft,group);return result;
        }
    }
}
