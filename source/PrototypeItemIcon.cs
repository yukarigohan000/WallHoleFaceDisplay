using System;using System.IO;using System.Security.Cryptography;using UnityEngine;using Object=UnityEngine.Object;
namespace GameModding.MadIsland.WallHoleFaceDisplay {
 internal sealed class PrototypeItemIcon:IDisposable {
  internal const string Resource="WHFD.ItemIcon.v0271.png";
  internal const string Sha256="f33f0bebdd3b60b7c1e13a71fabe5a99ada876a2838c5deb6285b85518e77bf8";
  private Texture2D texture;internal Sprite Sprite{get;private set;}
  internal PrototypeItemIcon(){
   try{byte[] bytes;using(var stream=typeof(PrototypeItemIcon).Assembly.GetManifestResourceStream(Resource)){
    if(stream==null)throw new InvalidOperationException("Facility icon resource unavailable");using(var memory=new MemoryStream()){stream.CopyTo(memory);bytes=memory.ToArray();}}
    using(var hash=SHA256.Create())if(BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()!=Sha256)throw new InvalidOperationException("Facility icon hash mismatch");
    texture=new Texture2D(2,2,TextureFormat.RGBA32,false,false){name="WHFD_ItemIcon",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
    if(!ImageConversion.LoadImage(texture,bytes,false)||texture.width!=512||texture.height!=512)throw new InvalidOperationException("Facility icon decode failed");
    Sprite=UnityEngine.Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100f,0,SpriteMeshType.FullRect);Sprite.name="WHFD_ItemIcon";texture.Apply(false,true);
   }catch{Dispose();throw;}
  }
  public void Dispose(){if(Sprite!=null)Object.Destroy(Sprite);Sprite=null;if(texture!=null)Object.Destroy(texture);texture=null;}
 }
}
