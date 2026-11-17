using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    [DataContract]
    internal sealed class MonitorArmValues
    {
        [DataMember(Order=0)] public int FormatVersion=1;
        [DataMember(Order=1)] public int NpcId=15;
        [DataMember(Order=2)] public float LeftX;
        [DataMember(Order=3)] public float LeftY;
        [DataMember(Order=4)] public float RightX;
        [DataMember(Order=5)] public float RightY;
        internal MonitorArmValues Copy(){return new MonitorArmValues {FormatVersion=FormatVersion,NpcId=NpcId,LeftX=LeftX,LeftY=LeftY,RightX=RightX,RightY=RightY};}
        internal bool Valid {get {return FormatVersion==1 && NpcId==15 && Check(LeftX)&&Check(LeftY)&&Check(RightX)&&Check(RightY);}}
        private static bool Check(float value){return !float.IsNaN(value)&&!float.IsInfinity(value)&&Math.Abs(value)<=1;}
    }
    internal sealed class MonitorArmSettings
    {
        private MonitorArmValues saved=new MonitorArmValues();
        private readonly string path;
        internal string Error {get;private set;}
        internal MonitorArmSettings(string path)
        {
            this.path=path;if(!MonitorStandardDefaults.Exists(path))return;
            try{using(var input=MonitorStandardDefaults.OpenRead(path))saved=(MonitorArmValues)Serializer().ReadObject(input);
                if(saved==null||!saved.Valid)throw new InvalidDataException();
            }catch(Exception error){saved=MonitorStandardDefaults.Fallback<MonitorArmValues>(path,()=>new MonitorArmValues());Error="Arm settings preserved; save disabled: "+error.GetType().Name;}
            if(Error==null)Error=MonitorStandardDefaults.Warning(path);
        }
        private static DataContractJsonSerializer Serializer(){return new DataContractJsonSerializer(typeof(MonitorArmValues));}
        internal MonitorArmValues Get(){return saved.Copy();}
        internal void Save(MonitorArmValues value)
        {
            if(Error!=null)throw new InvalidOperationException(Error);
            if(value==null||!value.Valid)throw new InvalidDataException("Invalid arm settings");
            var next=value.Copy();Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){Serializer().WriteObject(output,next);output.Flush(true);}
                if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);
                saved=next;
            }finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
