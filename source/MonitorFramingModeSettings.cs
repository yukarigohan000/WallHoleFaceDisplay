using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    [DataContract]
    internal sealed class MonitorFramingModeOverride
    {
        [DataMember(Order=0,EmitDefaultValue=false)] public float? X;
        [DataMember(Order=1,EmitDefaultValue=false)] public float? Y;
        [DataMember(Order=2,EmitDefaultValue=false)] public float? Zoom;
        internal MonitorFramingModeOverride Copy(){return (MonitorFramingModeOverride)MemberwiseClone();}
        private static bool Check(float? v){return !v.HasValue || (!float.IsNaN(v.Value)&&!float.IsInfinity(v.Value)&&Math.Abs(v.Value)<=2);}
        private static bool CheckZoom(float? v){return !v.HasValue || (!float.IsNaN(v.Value)&&!float.IsInfinity(v.Value)&&v.Value>=.25f&&v.Value<=4);}
        internal bool Valid {get {return Check(X)&&Check(Y)&&CheckZoom(Zoom);}}
    }
    [DataContract]
    internal sealed class MonitorFramingModeDocument
    {
        [DataMember(Order=0,IsRequired=true)] public int FormatVersion=1;
        [DataMember(Order=1,IsRequired=true)] public int NpcId=15;
        [DataMember(Order=2,EmitDefaultValue=false)] public MonitorFramingModeOverride Mode0;
        [DataMember(Order=3,EmitDefaultValue=false)] public MonitorFramingModeOverride Mode1;
        [DataMember(Order=4,EmitDefaultValue=false)] public MonitorFramingModeOverride Mode2;
        [DataMember(Order=5,EmitDefaultValue=false)] public MonitorFramingModeOverride Mode3;
        internal MonitorFramingModeOverride Mode(int mode)
        {switch(mode){case 0:return Mode0;case 1:return Mode1;case 2:return Mode2;case 3:return Mode3;default:return null;}}
        internal MonitorFramingModeOverride Edit(int mode)
        {switch(mode){case 0:return Mode0??(Mode0=new MonitorFramingModeOverride());case 1:return Mode1??(Mode1=new MonitorFramingModeOverride());
            case 2:return Mode2??(Mode2=new MonitorFramingModeOverride());case 3:return Mode3??(Mode3=new MonitorFramingModeOverride());default:throw new ArgumentOutOfRangeException("mode");}}
        internal MonitorFramingModeDocument Copy(){return new MonitorFramingModeDocument {FormatVersion=FormatVersion,NpcId=NpcId,
            Mode0=Mode0==null?null:Mode0.Copy(),Mode1=Mode1==null?null:Mode1.Copy(),Mode2=Mode2==null?null:Mode2.Copy(),Mode3=Mode3==null?null:Mode3.Copy()};}
        internal bool Valid {get {return FormatVersion==1 && NpcId==15 && (Mode0==null||Mode0.Valid)&&(Mode1==null||Mode1.Valid)&&(Mode2==null||Mode2.Valid)&&(Mode3==null||Mode3.Valid);}}
    }
    // Same nullable overrides and atomic persistence as mode poses. Never writes common framing.
    internal sealed class MonitorFramingModeSettings
    {
        private readonly string path;
        private MonitorFramingModeDocument saved=new MonitorFramingModeDocument();
        internal string Error {get;private set;}
        internal MonitorFramingModeSettings(string path)
        {
            this.path=path;if(!MonitorStandardDefaults.Exists(path))return;
            try{using(var input=MonitorStandardDefaults.OpenRead(path))saved=(MonitorFramingModeDocument)Serializer().ReadObject(input);
                if(saved==null||!saved.Valid)throw new InvalidDataException();
            }catch(Exception error){saved=MonitorStandardDefaults.Fallback<MonitorFramingModeDocument>(path,()=>new MonitorFramingModeDocument());Error="Mode framing preserved; common fallback; save disabled: "+error.GetType().Name;}
            if(Error==null)Error=MonitorStandardDefaults.Warning(path);
        }
        private static DataContractJsonSerializer Serializer(){return new DataContractJsonSerializer(typeof(MonitorFramingModeDocument));}
        internal MonitorFramingValue Resolve(int mode,MonitorFramingValue common)
        {
            var result=common.Copy();if(common.NpcId!=15)return result;
            var value=saved.Mode(mode);if(value!=null){result.X=value.X??result.X;result.Y=value.Y??result.Y;result.Zoom=value.Zoom??result.Zoom;}
            return result; // Missing axes or Zoom inherit independently from common.
        }
        internal void SaveXY(int mode,float x,float y)
        {var next=saved.Copy();var value=next.Edit(mode);value.X=x;value.Y=y;Save(next);}
        internal void SaveZoom(int mode,float zoom)
        {var next=saved.Copy();next.Edit(mode).Zoom=zoom;Save(next);}
        // Apply only edited groups in one atomic write; preserve absent/high-precision
        // values in the untouched group rather than materializing resolved fallbacks.
        internal void SaveEdited(int mode,MonitorFramingValue value,bool xy,bool zoom)
        {
            if(value==null||!value.Valid||value.NpcId!=15)throw new InvalidDataException("Invalid framing edits");
            if(!xy&&!zoom)return;
            var next=saved.Copy();var edit=next.Edit(mode);
            if(xy){edit.X=value.X;edit.Y=value.Y;}if(zoom)edit.Zoom=value.Zoom;Save(next);
        }
        internal void ClearZoom(int mode){var next=saved.Copy();next.Edit(mode).Zoom=null;Save(next);}
        internal void Clear(int mode,bool x,bool y)
        {var next=saved.Copy();var value=next.Edit(mode);if(x)value.X=null;if(y)value.Y=null;Save(next);}
        private void Save(MonitorFramingModeDocument next)
        {
            if(Error!=null)throw new InvalidOperationException(Error);
            if(!next.Valid)throw new InvalidDataException("Invalid mode framing");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){Serializer().WriteObject(output,next);output.Flush(true);}
                if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);saved=next;
            }finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
    internal static class MonitorFramingModeSelection
    {
        internal static MonitorFramingValue Resolve(MonitorFramingModeSettings settings,MonitorFramingValue common,
            object requester,object selected,object draftOwner,int playbackMode,int editScope,bool preview,MonitorFramingValue draft)
        {
            bool owns=common.NpcId==15 && preview && requester!=null && ReferenceEquals(requester,selected) && ReferenceEquals(requester,draftOwner);
            var result=settings.Resolve(owns?editScope:playbackMode,common);
            if(owns && draft!=null && draft.NpcId==common.NpcId && draft.Valid){result.X=draft.X;result.Y=draft.Y;
                result.Zoom=draft.Zoom;
            }
            return result;
        }
    }
}
