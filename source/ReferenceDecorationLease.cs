using System;using System.Collections.Generic;using UnityEngine;using Object=UnityEngine.Object;
namespace GameModding.MadIsland.WallHoleFaceDisplay {
 internal sealed class ReferenceDecorationLease:IDisposable {
  private sealed class Entry{internal Texture2D Texture;internal Sprite Sprite;internal Material Material;internal Color32[] Pixels;internal int Users;}
  private static readonly Dictionary<string,Entry> cache=new Dictionary<string,Entry>();private Entry entry;private string key;
  internal Color32[] Pixels{get{return entry.Pixels;}}internal int Width{get{return entry.Texture.width;}}internal int Height{get{return entry.Texture.height;}}
  internal Sprite Sprite{get{return entry.Sprite;}}internal Material Material{get{return entry.Material;}}
  internal static ReferenceDecorationLease Acquire(ReferenceDecorationCatalog.Info info){Entry item;if(cache.TryGetValue(info.Resource,out item)){item.Users++;return new ReferenceDecorationLease{key=info.Resource,entry=item};}
   var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,false);Sprite sprite=null;Material material=null;
   try{if(!ImageConversion.LoadImage(texture,ReferenceDecorationCatalog.Bytes(info),false))throw new InvalidOperationException("Reference PNG decoding failed");texture.name=info.Name;texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100f,0,SpriteMeshType.FullRect);sprite.name=info.Name;
    var shader=Shader.Find("Sprites/Default");if(shader==null)throw new InvalidOperationException("Sprite shader missing");material=new Material(shader){name="WHFD_"+info.Name,mainTexture=texture,color=Color.white};item=new Entry{Texture=texture,Sprite=sprite,Material=material,Pixels=texture.GetPixels32(),Users=1};texture.Apply(false,true);cache.Add(info.Resource,item);return new ReferenceDecorationLease{key=info.Resource,entry=item};
   }catch{if(sprite!=null)Object.Destroy(sprite);if(material!=null)Object.Destroy(material);Object.Destroy(texture);throw;}}
  internal bool ClearOfHole(VisualRect target,VisualRect hole){var pixels=entry.Pixels;for(int y=0;y<entry.Texture.height;y++)for(int x=0;x<entry.Texture.width;x++){if(pixels[y*entry.Texture.width+x].a<1)continue;float px=target.X+(x+.5f)*target.W/entry.Texture.width,py=target.Y+(y+.5f)*target.H/entry.Texture.height;if(ReferenceDecorationCatalog.PixelTouchesHole(px,py,hole))return false;}return true;}
  internal bool ClearOfRect(VisualRect target,VisualRect obstacle){if(obstacle.W<=0||obstacle.H<=0)return true;var pixels=entry.Pixels;for(int y=0;y<entry.Texture.height;y++)for(int x=0;x<entry.Texture.width;x++){if(pixels[y*entry.Texture.width+x].a==0)continue;float px=target.X+(x+.5f)*target.W/entry.Texture.width,py=target.Y+(y+.5f)*target.H/entry.Texture.height;if(px>=obstacle.X&&px<=obstacle.Right&&py>=obstacle.Y&&py<=obstacle.Top)return false;}return true;}
  public void Dispose(){if(entry==null)return;if(--entry.Users==0){cache.Remove(key);Object.Destroy(entry.Sprite);Object.Destroy(entry.Material);Object.Destroy(entry.Texture);}entry=null;}
 }
}
