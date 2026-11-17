using System;using System.Collections.Generic;using System.Linq;using TMPro;using UnityEngine;using Object=UnityEngine.Object;
namespace GameModding.MadIsland.WallHoleFaceDisplay {
 internal sealed class RuntimeNameFont:IDisposable {
  private sealed class Entry{internal TMP_FontAsset Asset;internal Font Source;internal int Users;}
  private static readonly Dictionary<int,Entry> cache=new Dictionary<int,Entry>();private Entry entry;private int key;
  internal TMP_FontAsset Asset{get;private set;}
  internal static RuntimeNameFont Acquire(TMP_FontAsset fallback){
   var supplied=Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f=>f!=null&&!f.name.StartsWith("WHFD_")&&(f.name.IndexOf("hand",StringComparison.OrdinalIgnoreCase)>=0||f.name.IndexOf("tegaki",StringComparison.OrdinalIgnoreCase)>=0||f.name.IndexOf("Kyokasho",StringComparison.OrdinalIgnoreCase)>=0));
   if(supplied!=null&&supplied.HasCharacter('\u65e5',false,true))return new RuntimeNameFont{Asset=supplied};
   Entry existing;int key=fallback.GetInstanceID();if(cache.TryGetValue(key,out existing)){existing.Users++;return new RuntimeNameFont{key=key,entry=existing,Asset=existing.Asset};}
   Font source=null;TMP_FontAsset asset=null;
   try{var names=Font.GetOSInstalledFontNames();string family=names.FirstOrDefault(n=>n.Equals("UD Digi Kyokasho N-R",StringComparison.OrdinalIgnoreCase))??names.FirstOrDefault(n=>n.IndexOf("\u6559\u79d1\u66f8\u4f53",StringComparison.Ordinal)>=0&&n.EndsWith(" N",StringComparison.Ordinal))??names.FirstOrDefault(n=>n.IndexOf("Kyokasho",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("\u6559\u79d1\u66f8\u4f53",StringComparison.Ordinal)>=0);
    if(family==null)return new RuntimeNameFont{Asset=fallback};source=Font.CreateDynamicFontFromOSFont(family,90);asset=TMP_FontAsset.CreateFontAsset(source);if(asset==null)throw new InvalidOperationException("OS handwriting font unavailable");asset.name="WHFD_RuntimeHandwriting";asset.fallbackFontAssetTable=new List<TMP_FontAsset>{fallback};
    if(!asset.HasCharacter('\u65e5',false,true))throw new InvalidOperationException("Japanese glyph unavailable");existing=new Entry{Asset=asset,Source=source,Users=1};cache.Add(key,existing);return new RuntimeNameFont{key=key,entry=existing,Asset=asset};
   }catch{Destroy(asset,source);return new RuntimeNameFont{Asset=fallback};}}
  // Per-text mesh styling, not font-outline modification. Called only when TMP rebuilds this name label.
  internal static void Wobble(TMP_TextInfo info){for(int i=0;i<info.characterCount;i++){var c=info.characterInfo[i];if(!c.isVisible)continue;var vertices=info.meshInfo[c.materialReferenceIndex].vertices;int start=c.vertexIndex;var center=(vertices[start]+vertices[start+2])*.5f;float height=Vector3.Distance(vertices[start],vertices[start+1]);float angle=Mathf.Sin(i*2.17f)*.009f,cos=Mathf.Cos(angle),sin=Mathf.Sin(angle),rise=Mathf.Sin(i*1.31f)*height*.012f;for(int j=0;j<4;j++){var p=vertices[start+j]-center;vertices[start+j]=center+new Vector3(p.x*cos-p.y*sin,p.x*sin+p.y*cos+rise,p.z);}}}
  private static void Destroy(TMP_FontAsset asset,Font source){if(asset!=null){if(asset.material!=null)Object.Destroy(asset.material);if(asset.atlasTextures!=null)foreach(var texture in asset.atlasTextures.Distinct())if(texture!=null)Object.Destroy(texture);Object.Destroy(asset);}if(source!=null)Object.Destroy(source);}
  public void Dispose(){if(entry==null)return;if(--entry.Users==0){cache.Remove(key);Destroy(entry.Asset,entry.Source);}entry=null;Asset=null;}
 }
}
