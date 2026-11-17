using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Null means inherit that individual value from the existing common files.
    [DataContract]
    internal sealed class MonitorPoseOverride
    {
        [DataMember(Order=0,EmitDefaultValue=false)] public float? LeftX;
        [DataMember(Order=1,EmitDefaultValue=false)] public float? LeftY;
        [DataMember(Order=2,EmitDefaultValue=false)] public float? RightX;
        [DataMember(Order=3,EmitDefaultValue=false)] public float? RightY;
        [DataMember(Order=4,EmitDefaultValue=false)] public float? HeadRotation;
        [DataMember(Order=5,EmitDefaultValue=false)] public float? TorsoRotation;
        internal MonitorPoseOverride Copy(){return (MonitorPoseOverride)MemberwiseClone();}
        private static bool Check(float? v,float max){return !v.HasValue || (!float.IsNaN(v.Value)&&!float.IsInfinity(v.Value)&&Math.Abs(v.Value)<=max);}
        internal bool Valid {get {return Check(LeftX,1)&&Check(LeftY,1)&&Check(RightX,1)&&Check(RightY,1)&&Check(HeadRotation,180)&&Check(TorsoRotation,180);}}
    }
    [DataContract]
    internal sealed class MonitorPoseDocument
    {
        [DataMember(Order=0,IsRequired=true)] public int FormatVersion=1;
        [DataMember(Order=1,IsRequired=true)] public int NpcId=15;
        [DataMember(Order=2,EmitDefaultValue=false)] public MonitorPoseOverride Mode0;
        [DataMember(Order=3,EmitDefaultValue=false)] public MonitorPoseOverride Mode1;
        [DataMember(Order=4,EmitDefaultValue=false)] public MonitorPoseOverride Mode2;
        [DataMember(Order=5,EmitDefaultValue=false)] public MonitorPoseOverride Mode3;
        internal MonitorPoseOverride Mode(int mode)
        {switch(mode){case 0:return Mode0;case 1:return Mode1;case 2:return Mode2;case 3:return Mode3;default:return null;}}
        internal MonitorPoseOverride Edit(int mode)
        {
            switch(mode){case 0:return Mode0??(Mode0=new MonitorPoseOverride());case 1:return Mode1??(Mode1=new MonitorPoseOverride());
                case 2:return Mode2??(Mode2=new MonitorPoseOverride());case 3:return Mode3??(Mode3=new MonitorPoseOverride());default:throw new ArgumentOutOfRangeException("mode");}
        }
        internal MonitorPoseDocument Copy(){return new MonitorPoseDocument {FormatVersion=FormatVersion,NpcId=NpcId,
            Mode0=Mode0==null?null:Mode0.Copy(),Mode1=Mode1==null?null:Mode1.Copy(),Mode2=Mode2==null?null:Mode2.Copy(),Mode3=Mode3==null?null:Mode3.Copy()};}
        internal bool Valid {get {return FormatVersion==1 && NpcId==15 &&
            (Mode0==null||Mode0.Valid)&&(Mode1==null||Mode1.Valid)&&(Mode2==null||Mode2.Valid)&&(Mode3==null||Mode3.Valid);}}
    }
    internal sealed class MonitorPoseValues
    {
        internal MonitorArmValues Arms;
        internal MonitorHeadValues Head;
        internal MonitorTorsoValues Torso;
        internal MonitorPoseValues Copy(){return new MonitorPoseValues {Arms=Arms.Copy(),Head=Head.Copy(),Torso=Torso.Copy()};}
    }
    internal sealed class MonitorPoseSettings
    {
        private readonly string path;
        private MonitorPoseDocument saved=new MonitorPoseDocument();
        internal string Error {get;private set;}
        internal MonitorPoseSettings(string path)
        {
            this.path=path;if(!MonitorStandardDefaults.Exists(path))return;
            try{using(var input=MonitorStandardDefaults.OpenRead(path))saved=(MonitorPoseDocument)Serializer().ReadObject(input);
                if(saved==null||!saved.Valid)throw new InvalidDataException();
            }catch(Exception error){saved=MonitorStandardDefaults.Fallback<MonitorPoseDocument>(path,()=>new MonitorPoseDocument());Error="Pose settings preserved; common fallback; save disabled: "+error.GetType().Name;}
            if(Error==null)Error=MonitorStandardDefaults.Warning(path);
        }
        private static DataContractJsonSerializer Serializer(){return new DataContractJsonSerializer(typeof(MonitorPoseDocument));}
        internal MonitorPoseValues Resolve(int mode,MonitorPoseValues common)
        {
            var result=common.Copy();var value=saved.Mode(mode);if(value==null)return result;
            result.Arms.LeftX=value.LeftX??result.Arms.LeftX;result.Arms.LeftY=value.LeftY??result.Arms.LeftY;
            result.Arms.RightX=value.RightX??result.Arms.RightX;result.Arms.RightY=value.RightY??result.Arms.RightY;
            result.Head.Rotation=value.HeadRotation??result.Head.Rotation;result.Torso.Rotation=value.TorsoRotation??result.Torso.Rotation;
            return result;
        }
        internal void SaveArms(int mode,MonitorArmValues value)
        {
            if(value==null||!value.Valid)throw new InvalidDataException("Invalid arms");
            var next=saved.Copy();var edit=next.Edit(mode);edit.LeftX=value.LeftX;edit.LeftY=value.LeftY;edit.RightX=value.RightX;edit.RightY=value.RightY;Save(next);
        }
        internal void SaveHead(int mode,MonitorHeadValues value)
        {if(value==null||!value.Valid)throw new InvalidDataException("Invalid head");var next=saved.Copy();next.Edit(mode).HeadRotation=value.Rotation;Save(next);}
        internal void SaveTorso(int mode,MonitorTorsoValues value)
        {if(value==null||!value.Valid)throw new InvalidDataException("Invalid torso");var next=saved.Copy();next.Edit(mode).TorsoRotation=value.Rotation;Save(next);}
        internal void ClearArms(int mode)
        {var next=saved.Copy();var edit=next.Edit(mode);edit.LeftX=edit.LeftY=edit.RightX=edit.RightY=null;Save(next);}
        internal void ClearHead(int mode){var next=saved.Copy();next.Edit(mode).HeadRotation=null;Save(next);}
        internal void ClearTorso(int mode){var next=saved.Copy();next.Edit(mode).TorsoRotation=null;Save(next);}
        private void Save(MonitorPoseDocument next)
        {
            if(Error!=null)throw new InvalidOperationException(Error);
            if(!next.Valid)throw new InvalidDataException("Invalid mode pose");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){Serializer().WriteObject(output,next);output.Flush(true);}
                if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);
                saved=next;
            }finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
