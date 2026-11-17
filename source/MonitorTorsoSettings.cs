using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    [DataContract]
    internal sealed class MonitorTorsoValues
    {
        [DataMember(Order=0)] public int FormatVersion=1;
        [DataMember(Order=1)] public int NpcId=15;
        [DataMember(Order=2)] public float Rotation;
        internal MonitorTorsoValues Copy(){return new MonitorTorsoValues {FormatVersion=FormatVersion,NpcId=NpcId,Rotation=Rotation};}
        internal bool Valid {get {return FormatVersion==1 && NpcId==15 && Check(Rotation,180);}}
        private static bool Check(float value,float limit){return !float.IsNaN(value)&&!float.IsInfinity(value)&&Math.Abs(value)<=limit;}
    }
    internal sealed class MonitorTorsoSettings
    {
        private MonitorTorsoValues saved=new MonitorTorsoValues();
        private readonly string path;
        internal string Error {get;private set;}
        internal MonitorTorsoSettings(string path)
        {
            this.path=path;if(!MonitorStandardDefaults.Exists(path))return;
            try{using(var input=MonitorStandardDefaults.OpenRead(path))saved=(MonitorTorsoValues)Serializer().ReadObject(input);
                if(saved==null||!saved.Valid)throw new InvalidDataException();
            }catch(Exception error){saved=MonitorStandardDefaults.Fallback<MonitorTorsoValues>(path,()=>new MonitorTorsoValues());Error="Torso settings preserved; save disabled: "+error.GetType().Name;}
            if(Error==null)Error=MonitorStandardDefaults.Warning(path);
        }
        private static DataContractJsonSerializer Serializer(){return new DataContractJsonSerializer(typeof(MonitorTorsoValues));}
        internal MonitorTorsoValues Get(){return saved.Copy();}
        internal void Save(MonitorTorsoValues value)
        {
            if(Error!=null)throw new InvalidOperationException(Error);
            if(value==null||!value.Valid)throw new InvalidDataException("Invalid torso settings");
            var next=value.Copy();Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){Serializer().WriteObject(output,next);output.Flush(true);}
                if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);
                saved=next;
            }finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
