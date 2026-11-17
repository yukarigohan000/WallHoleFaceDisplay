using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization.Json;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Read-only startup fallback. Existing saves, validation and write paths remain authoritative.
    internal static class MonitorStandardDefaults
    {
        private static readonly Dictionary<string,string> warnings=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        internal static string Warning(string path){string value;return warnings.TryGetValue(path,out value)?value:null;}
        private static void Warn(string path,string reason){warnings[path]="Settings preserved; embedded fallback active; save disabled: "+reason;}
        internal static T Fallback<T>(string path,Func<T> empty)
        {
            using(var input=Resource(Path.GetFileName(path))) {
                if(input==null)return empty();
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(input);
            }
        }
        private static Stream Resource(string name)
        { return typeof(MonitorStandardDefaults).Assembly.GetManifestResourceStream("WHFD.Standard."+name); }
        internal static bool Exists(string path)
        { if(File.Exists(path))return true;using(var s=Resource(Path.GetFileName(path)))return s!=null; }
        private static XElement Read(Stream stream)
        { using(var reader=JsonReaderWriterFactory.CreateJsonReader(stream,System.Xml.XmlDictionaryReaderQuotas.Max))return XElement.Load(reader); }
        internal static Stream OpenRead(string path)
        {
            using(var standard=Resource(Path.GetFileName(path))) {
                if(!File.Exists(path)) {
                    if(standard==null)throw new FileNotFoundException("No standard profile",path);
                    var root=Read(standard);
                    RespectUserCommon(path,root);
                    return Encode(root);
                }
                // Nullable mode members intentionally mean 'inherit common', including after Clear.
                // Never resurrect a cleared mode override from the distribution profile.
                if(standard==null)return File.OpenRead(path);
                var baseline=Read(standard);
                try {
                    XElement user;using(var input=File.OpenRead(path))user=Read(input);
                    if((string)user.Attribute("type")!="object")throw new InvalidDataException("Expected settings object");
                    foreach(string name in new[]{"FormatVersion","NpcId"}) {
                        var required=baseline.Element(name);if(required!=null&&(user.Element(name)==null||user.Element(name).Value!=required.Value))throw new InvalidDataException("Unsupported profile identity/version");
                    }
                    Repair(user,baseline,path);
                    Supplement(user,baseline);
                    return Encode(user);
                }catch(Exception error){Warn(path,error.GetType().Name);return Encode(baseline);}
            }
        }
        // Only malformed/out-of-range fields are replaced; valid user fields and explicit inheritance stay intact.
        private static void Repair(XElement user,XElement baseline,string path)
        {
            foreach(var member in user.Elements().ToArray()) {
                string name=member.Name.LocalName;var fallback=baseline==null?null:baseline.Element(member.Name);
                string kind=(string)member.Attribute("type");
                if(name=="Common"||name.StartsWith("Mode",StringComparison.Ordinal)) {
                    if(kind=="object")Repair(member,fallback,path);
                    else if(kind!="null")Replace(member,fallback,path);
                    continue;
                }
                if(name=="Npcs") {
                    if(kind!="array"){Replace(member,fallback,path);continue;}
                    var seen=new HashSet<int>();
                    foreach(var entry in member.Elements().ToArray()) {
                        int id;if((string)entry.Attribute("type")!="object"||!int.TryParse(entry.Element("NpcId")?.Value,out id)||id<0||!seen.Add(id)){entry.Remove();Warn(path,"invalid framing entry");continue;}
                        var original=fallback?.Elements().FirstOrDefault(v=>v.Element("NpcId")?.Value==id.ToString(CultureInfo.InvariantCulture));
                        Repair(entry,original,path);
                    }
                    continue;
                }
                if(name=="Body"||name=="Face") {if(kind!="string"&&kind!="null")Replace(member,fallback,path);continue;}
                float limit=0,min=0,max=0;
                if(name=="LeftX"||name=="LeftY"||name=="RightX"||name=="RightY")limit=1;
                else if(name=="HeadRotation"||name=="TorsoRotation"||name=="Rotation")limit=180;
                else if(name=="X"||name=="Y")limit=2;
                else if(name=="Zoom"){min=.25f;max=4;}
                else continue;
                if(limit>0){min=-limit;max=limit;}
                // Null mode/common fields intentionally inherit and are valid.
                if(kind=="null"){if(user.Name.LocalName=="Common"||user.Name.LocalName.StartsWith("Mode",StringComparison.Ordinal))continue;Replace(member,fallback,path);continue;}
                float value;
                if(kind!="number"||!float.TryParse(member.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||float.IsNaN(value)||float.IsInfinity(value)||value<min||value>max)Replace(member,fallback,path);
            }
        }
        private static void Replace(XElement member,XElement fallback,string path)
        {
            Warn(path,"invalid field "+member.Name.LocalName);
            if(fallback==null)member.Remove();else member.ReplaceWith(new XElement(fallback));
        }
        private static Stream Encode(XElement value)
        {
            var output=new MemoryStream();
            using(var writer=JsonReaderWriterFactory.CreateJsonWriter(output,Encoding.UTF8,false))value.WriteTo(writer);
            output.Position=0;return output;
        }
        private static void RespectUserCommon(string path,XElement standard)
        {
            // A user's explicit common setting wins over a bundled per-mode fallback.
            var file=Path.GetFileName(path);var folder=Path.GetDirectoryName(path);
            string[] inputs=file=="WallHoleFaceDisplay.framing-modes-15.json"?new[]{"framing.json"}:
                file=="WallHoleFaceDisplay.pose-15.json"?new[]{"arms-15.json","head-15.json","torso-15.json"}:new string[0];
            foreach(var name in inputs) {
                var inputPath=Path.Combine(folder,"WallHoleFaceDisplay."+name);if(!File.Exists(inputPath))continue;
                XElement common;
                try{using(var input=OpenRead(inputPath))common=Read(input);if(Warning(inputPath)!=null)continue;}
                catch(Exception error){Warn(inputPath,error.GetType().Name);continue;}
                if(name=="framing.json")common=common.Element("Npcs")?.Elements().FirstOrDefault(v=>v.Element("NpcId")?.Value=="15");
                if(common==null)continue;
                foreach(var member in common.Elements()) {
                    string key=member.Name.LocalName;
                    if(key=="Rotation")key=name=="head-15.json"?"HeadRotation":"TorsoRotation";
                    if(key=="NpcId"||key=="FormatVersion")continue;
                    foreach(var mode in standard.Elements().Where(v=>v.Name.LocalName.StartsWith("Mode",StringComparison.Ordinal)))mode.Element(key)?.Remove();
                }
            }
        }
        private static void Supplement(XElement user,XElement baseline)
        {
            foreach(var value in baseline.Elements()) {
                var name=value.Name.LocalName;
                // Required identity/version must still be supplied and validated by each existing reader.
                if(name=="FormatVersion"||name=="NpcId"||name.StartsWith("Mode",StringComparison.Ordinal))continue;
                var target=user.Element(value.Name);
                if(target==null)user.Add(new XElement(value));
                else if((string)target.Attribute("type")=="object"&&(string)value.Attribute("type")=="object")Supplement(target,value);
                else if(name=="Npcs"&&(string)target.Attribute("type")=="array") {
                    foreach(var entry in value.Elements()) {
                        var existing=target.Elements().FirstOrDefault(v=>v.Element("NpcId")?.Value==entry.Element("NpcId")?.Value);
                        if(existing==null)target.Add(new XElement(entry));else Supplement(existing,entry);
                    }
                }
            }
        }
        internal static float PlaybackBlend(float fallback)
        {
            float value;return float.TryParse(Playback("BlendSeconds",fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value)&&value>=0&&value<=1?value:fallback;
        }
        internal static string Playback(string key,string fallback)
        {
            using(var stream=Resource("MonitorStatePlayback.json")) {
                if(stream==null)return fallback;
                var value=Read(stream).Element(key);return value==null?fallback:value.Value;
            }
        }
    }
}
