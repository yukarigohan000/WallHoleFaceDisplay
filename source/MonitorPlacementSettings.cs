using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    [DataContract] internal sealed class MonitorPlacement
    {
        [DataMember] public string Context;
        [DataMember] public int BuildId;
        [DataMember] public float X,Y,Z;
        internal bool Valid {get {return !string.IsNullOrEmpty(Context)&&BuildId>=0&&Finite(X)&&Finite(Y)&&Finite(Z);}}
        private static bool Finite(float v){return !float.IsNaN(v)&&!float.IsInfinity(v);}
        internal bool Matches(string context,int build,float x,float y,float z)
        {return Context==context&&BuildId==build&&Math.Abs(X-x)<.001f&&Math.Abs(Y-y)<.001f&&Math.Abs(Z-z)<.001f;}
    }
    [DataContract] internal sealed class MonitorPlacementFile
    {
        [DataMember] public int FormatVersion=1;
        [DataMember] public List<MonitorPlacement> Entries=new List<MonitorPlacement>();
    }
    // Read-only game identity is supplied by the caller. This file is MOD config;
    // neither game saves nor facility components are modified.
    internal sealed class MonitorPlacementSettings
    {
        private readonly string path;
        private MonitorPlacementFile saved=new MonitorPlacementFile();
        internal string Error {get;private set;}
        internal MonitorPlacementSettings(string path)
        {
            this.path=path;if(!File.Exists(path))return;
            try{using(var input=File.OpenRead(path)){var value=(MonitorPlacementFile)Serializer().ReadObject(input);
                if(value==null||value.FormatVersion!=1||value.Entries==null||value.Entries.Count>4096||value.Entries.Any(e=>e==null||!e.Valid)||value.Entries.GroupBy(e=>e.Context+"|"+e.BuildId).Any(g=>g.Count()!=1))throw new InvalidDataException("Invalid placement registry");saved=value;}}
            catch(Exception e){Error="モニター設置記録を読めません。元ファイルを保護します："+e.GetType().Name;}
        }
        private static DataContractJsonSerializer Serializer(){return new DataContractJsonSerializer(typeof(MonitorPlacementFile));}
        internal bool Contains(string context,int build,float x,float y,float z)
        {return Error==null&&saved.Entries.Any(e=>e.Matches(context,build,x,y,z));}
        internal void Set(MonitorPlacement value,bool enabled)
        {
            if(Error!=null)throw new InvalidOperationException(Error);
            if(value==null||!value.Valid)throw new InvalidDataException("Placement identity unavailable");
            var next=new MonitorPlacementFile{Entries=saved.Entries.Where(e=>e.Context!=value.Context||e.BuildId!=value.BuildId).ToList()};
            if(enabled)next.Entries.Add(value);if(next.Entries.Count>4096)throw new InvalidDataException("Placement limit reached");
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){Serializer().WriteObject(output,next);output.Flush(true);}
                if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);saved=next;}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
