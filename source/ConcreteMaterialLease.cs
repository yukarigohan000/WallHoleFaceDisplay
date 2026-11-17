using System;using System.IO;using System.Reflection;using System.Security.Cryptography;using System.Collections.Generic;using System.Linq;using Spine;using Spine.Unity;using UnityEngine;using Object=UnityEngine.Object;
namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal sealed class ConcreteMaterialLease:IDisposable
    {
        private sealed class Entry{internal Material Source,Material,PanelMaterial;internal Texture2D Texture,PanelTexture;internal int Users;}
        private static readonly Dictionary<string,Entry> cache=new Dictionary<string,Entry>();
        private Entry entry;private string key;
        internal Material Material{get{return entry.Material;}}
        internal Material PanelMaterial{get{return entry.PanelMaterial;}}
        internal const string PngResource="WHFD.Concrete.v0267.png";
        internal const string PngSha256="19bed4f044d967930459219941d76366c3fce0385d6bb23b4e7c550bbab67762";
        internal static ConcreteMaterialLease Acquire(Material source,MeshAttachment attachment,Renderer wall,Renderer panel,WoodPanelSurface surface,ConcreteInkStamp[] ink)
        {
            var texture=source.mainTexture as Texture2D;
            if(texture==null||attachment.UVs==null||attachment.UVs.Length!=16)throw new InvalidOperationException("Known wall atlas unavailable");
            float minU=attachment.UVs.Where((v,i)=>i%2==0).Min(),maxU=attachment.UVs.Where((v,i)=>i%2==0).Max();
            float minV=attachment.UVs.Where((v,i)=>i%2==1).Min(),maxV=attachment.UVs.Where((v,i)=>i%2==1).Max();
            int x0=Mathf.CeilToInt(minU*texture.width),x1=Mathf.FloorToInt(maxU*texture.width),y0=Mathf.CeilToInt(minV*texture.height),y1=Mathf.FloorToInt(maxV*texture.height);
            if(x0<0||x1>texture.width||y0<0||y1>texture.height||x1<=x0||y1<=y0)throw new InvalidOperationException("Wall atlas bounds differ");
            var animation=wall.GetComponent<SkeletonAnimation>();var slot=animation.skeleton.Slots.Items[0];var positions=new float[attachment.WorldVerticesLength];attachment.ComputeWorldVertices(slot,positions);
            float originalHeight=Vector3.Distance(WoodPanelSurface.WorldAt(wall,attachment,positions,new Vector2(0,0)),WoodPanelSurface.WorldAt(wall,attachment,positions,new Vector2(0,1)));
            string key=source.GetInstanceID()+"|"+x0+"|"+x1+"|"+y0+"|"+y1+"|"+(surface.Width/originalHeight).ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+"|"+(surface.Height/originalHeight).ToString("F4",System.Globalization.CultureInfo.InvariantCulture);
            foreach(var stamp in ink){key+="|"+stamp.Identity;foreach(var point in new[]{stamp.A,stamp.B,stamp.D}){var relative=point-surface.BaseCenter;key+="|"+(Vector3.Dot(relative,surface.RightDirection)/surface.Width).ToString("F5",System.Globalization.CultureInfo.InvariantCulture)+","+(Vector3.Dot(relative,surface.UpDirection)/originalHeight).ToString("F5",System.Globalization.CultureInfo.InvariantCulture);}}
            Entry existing;
            if(cache.TryGetValue(key,out existing)){SetPanelUv(panel,surface,originalHeight,existing.PanelTexture.width,existing.PanelTexture.height);existing.Users++;return new ConcreteMaterialLease{key=key,entry=existing};}
            Texture2D copy=null,png=null;Material material=null,panelMaterial=null;var previous=RenderTexture.active;RenderTexture temporary=null;
            try{
                // One GPU readback per shared wall atlas, never per frame. Alpha is copied exactly.
                temporary=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
                Graphics.Blit(texture,temporary);RenderTexture.active=temporary;
                copy=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false,false){name="WHFD_ConcreteAtlas",filterMode=texture.filterMode,wrapMode=texture.wrapMode,anisoLevel=texture.anisoLevel};
                copy.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0,false);var pixels=copy.GetPixels32();
                byte[] bytes;
                using(var stream=typeof(ConcreteMaterialLease).Assembly.GetManifestResourceStream(PngResource)){
                    if(stream==null)throw new InvalidOperationException("Embedded concrete PNG unavailable");using(var memory=new MemoryStream()){stream.CopyTo(memory);bytes=memory.ToArray();}}
                using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()!=PngSha256)throw new InvalidOperationException("Concrete PNG hash mismatch");
                png=new Texture2D(2,2,TextureFormat.RGBA32,false,false);if(!ImageConversion.LoadImage(png,bytes,false))throw new InvalidOperationException("Concrete PNG decoding failed");
                var concretePixels=png.GetPixels32();
                foreach(var stamp in ink)ConcreteInkBake.Composite(concretePixels,png.width,png.height,stamp.Pixels,stamp.Width,stamp.Height,Map(stamp.A,surface,originalHeight,png.width,png.height),Map(stamp.B,surface,originalHeight,png.width,png.height),Map(stamp.D,surface,originalHeight,png.width,png.height));
                png.SetPixels32(concretePixels);png.Apply(false,false);
                bool premultiplied=source.HasProperty("_StraightAlphaInput")?source.GetFloat("_StraightAlphaInput")==0:source.HasProperty("_SrcBlend")&&source.GetFloat("_SrcBlend")==1;
                var mapped=new float[positions.Length];for(int i=0;i<positions.Length;i+=2){var p=Map(wall.transform.TransformPoint(new Vector3(positions[i],positions[i+1],0)),surface,originalHeight,png.width,png.height);mapped[i]=p.x;mapped[i+1]=1-p.y;}
                ConcreteAtlasPixels.Paint(pixels,texture.width,texture.height,attachment.UVs,mapped,concretePixels,png.width,png.height,premultiplied,attachment.Triangles);
                SetPanelUv(panel,surface,originalHeight,png.width,png.height);
                copy.SetPixels32(pixels);copy.Apply(false,true);
                material=new Material(source){name="WHFD_ConcreteMaterial",mainTexture=copy};
                // Panel uses the same physical sample coordinates, not the old compressed wood UV band.
                png.wrapMode=TextureWrapMode.Clamp;png.filterMode=FilterMode.Bilinear;
                // Atlas can be PMA; the standalone concrete PNG is opaque, so both paths agree.
                panelMaterial=new Material(source){name="WHFD_ConcretePanel",mainTexture=png};
                existing=new Entry{Source=source,Material=material,Texture=copy,PanelMaterial=panelMaterial,PanelTexture=png,Users=1};png=null;cache.Add(key,existing);
                return new ConcreteMaterialLease{key=key,entry=existing};
            }catch{if(panelMaterial!=null)Object.Destroy(panelMaterial);if(material!=null)Object.Destroy(material);if(copy!=null)Object.Destroy(copy);throw;}
            finally{if(png!=null)Object.Destroy(png);RenderTexture.active=previous;if(temporary!=null)RenderTexture.ReleaseTemporary(temporary);}
        }
        private static Vector2 Map(Vector3 point,WoodPanelSurface surface,float originalHeight,int width,int height)
        {
            float total=originalHeight+surface.Height;float density=Mathf.Min(width/surface.Width,height/total);
            var relative=point-surface.BaseCenter;
            return new Vector2(.5f+Vector3.Dot(relative,surface.RightDirection)*density/width,.5f+(Vector3.Dot(relative,surface.UpDirection)+originalHeight-total*.5f)*density/height);
        }
        private static void SetPanelUv(Renderer panel,WoodPanelSurface surface,float originalHeight,int width,int height)
        {var mesh=panel.GetComponent<MeshFilter>().sharedMesh;mesh.uv=mesh.vertices.Select(v=>Map(panel.transform.TransformPoint(v),surface,originalHeight,width,height)).ToArray();}
        public void Dispose(){if(entry==null)return;if(--entry.Users==0){cache.Remove(key);Object.Destroy(entry.Material);Object.Destroy(entry.Texture);Object.Destroy(entry.PanelMaterial);Object.Destroy(entry.PanelTexture);}entry=null;}
    }
}
