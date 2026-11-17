using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    [DataContract]
    internal sealed class MonitorFramingValue
    {
        [DataMember(Order=0)] public int NpcId = 15;
        [DataMember(Order=1)] public float X;
        [DataMember(Order=2)] public float Y;
        [DataMember(Order=3)] public float Zoom = 1f;
        internal MonitorFramingValue Copy() { return new MonitorFramingValue { NpcId=NpcId, X=X, Y=Y, Zoom=Zoom }; }
        internal bool Valid { get { return NpcId >= 0 && Finite(X) && Finite(Y) && Finite(Zoom) && Math.Abs(X)<=2 && Math.Abs(Y)<=2 && Zoom>=.25f && Zoom<=4f; } }
        private static bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
    }
    [DataContract]
    internal sealed class MonitorFramingDocument
    {
        [DataMember(Order=0)] public int FormatVersion = 1;
        [DataMember(Order=1)] public MonitorFramingValue[] Npcs = { new MonitorFramingValue() };
    }
    // Own file only; unsupported or corrupt files are preserved and disable Save.
    internal sealed class MonitorFramingSettings
    {
        private MonitorFramingDocument saved = new MonitorFramingDocument();
        private readonly string path;
        internal string Error { get; private set; }
        internal string FilePath { get { return path; } }
        internal MonitorFramingSettings(string path)
        {
            this.path = path;
            if (!MonitorStandardDefaults.Exists(path)) return;
            try {
                using (var input=MonitorStandardDefaults.OpenRead(path)) saved=(MonitorFramingDocument)Serializer().ReadObject(input);
                Validate(saved);
            } catch (Exception error) { saved=MonitorStandardDefaults.Fallback<MonitorFramingDocument>(path,()=>new MonitorFramingDocument()); Error="Settings preserved; save disabled: " + error.GetType().Name; }
            if(Error==null)Error=MonitorStandardDefaults.Warning(path);
        }
        private static DataContractJsonSerializer Serializer() { return new DataContractJsonSerializer(typeof(MonitorFramingDocument)); }
        private static void Validate(MonitorFramingDocument value)
        {
            if (value==null || value.FormatVersion!=1 || value.Npcs==null || value.Npcs.Any(p=>p==null || !p.Valid) || value.Npcs.Select(p=>p.NpcId).Distinct().Count()!=value.Npcs.Length)
                throw new InvalidDataException("Unsupported version or invalid framing data");
        }
        internal MonitorFramingValue Get(int id)
        {
            var value=saved.Npcs.FirstOrDefault(p=>p.NpcId==id);
            return value==null ? new MonitorFramingValue { NpcId=id } : value.Copy();
        }
        // Preview and persistence are keyed only by numeric NPC ID, never a monitor.
        internal MonitorFramingValue Resolve(int id,int? previewId,MonitorFramingValue preview)
        {
            return previewId==id && preview!=null && preview.NpcId==id && preview.Valid ? preview.Copy() : Get(id);
        }
        internal void Save(MonitorFramingValue value)
        {
            if (Error!=null) throw new InvalidOperationException(Error);
            if (value==null || !value.Valid) throw new InvalidDataException("Invalid framing value");
            var next=new MonitorFramingDocument { Npcs=saved.Npcs.Where(p=>p.NpcId!=value.NpcId).Select(p=>p.Copy()).Concat(new[]{value.Copy()}).OrderBy(p=>p.NpcId).ToArray() };
            Validate(next);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                    Serializer().WriteObject(output,next); output.Flush(true);
                }
                if(File.Exists(path)) File.Replace(temp,path,path+".bak");
                else File.Move(temp,path);
                saved=next;
            } finally { if(File.Exists(temp)) File.Delete(temp); }
        }
    }
}
