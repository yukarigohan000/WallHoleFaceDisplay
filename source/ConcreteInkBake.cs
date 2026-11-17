using System;using UnityEngine;
namespace GameModding.MadIsland.WallHoleFaceDisplay {
 internal sealed class ConcreteInkStamp {
  internal string Identity;internal Color32[] Pixels;internal int Width,Height;internal Vector3 A,B,D;
 }
 internal static class ConcreteInkBake {
  // Initialization only. Composite premultiplied samples into wall RGB; never change wall alpha.
  internal static int Composite(Color32[] wall,int width,int height,Color32[] ink,int inkWidth,int inkHeight,Vector2 a,Vector2 b,Vector2 d){
   if(wall==null||wall.Length!=width*height||ink==null||ink.Length!=inkWidth*inkHeight)throw new ArgumentException("Invalid wall ink data");
   float ux=b.x-a.x,uy=b.y-a.y,vx=d.x-a.x,vy=d.y-a.y,det=ux*vy-vx*uy;
   if(float.IsNaN(det)||float.IsInfinity(det)||Math.Abs(det)<1e-12f)throw new ArgumentException("Degenerate wall ink projection");
   var c=new Vector2(b.x+d.x-a.x,b.y+d.y-a.y);
   int x0=Math.Max(0,(int)Math.Floor(Math.Min(Math.Min(a.x,b.x),Math.Min(c.x,d.x))*width)),x1=Math.Min(width,(int)Math.Ceiling(Math.Max(Math.Max(a.x,b.x),Math.Max(c.x,d.x))*width));
   int y0=Math.Max(0,(int)Math.Floor(Math.Min(Math.Min(a.y,b.y),Math.Min(c.y,d.y))*height)),y1=Math.Min(height,(int)Math.Ceiling(Math.Max(Math.Max(a.y,b.y),Math.Max(c.y,d.y))*height)),changed=0;
   for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++){
    float px=(x+.5f)/width-a.x,py=(y+.5f)/height-a.y,s=(px*vy-vx*py)/det,t=(ux*py-px*uy)/det;if(s<0||s>1||t<0||t>1)continue;
    float sx=s*inkWidth-.5f,sy=t*inkHeight-.5f;int ix=(int)Math.Floor(sx),iy=(int)Math.Floor(sy);float fx=sx-ix,fy=sy-iy,rr=0,gg=0,bb=0,alpha=0;
    Accumulate(ink,inkWidth,inkHeight,ix,iy,(1-fx)*(1-fy),ref rr,ref gg,ref bb,ref alpha);Accumulate(ink,inkWidth,inkHeight,ix+1,iy,fx*(1-fy),ref rr,ref gg,ref bb,ref alpha);
    Accumulate(ink,inkWidth,inkHeight,ix,iy+1,(1-fx)*fy,ref rr,ref gg,ref bb,ref alpha);Accumulate(ink,inkWidth,inkHeight,ix+1,iy+1,fx*fy,ref rr,ref gg,ref bb,ref alpha);
    if(alpha<=0)continue;int index=y*width+x;var old=wall[index];var fresh=new Color32(Byte(rr+old.r*(1-alpha)),Byte(gg+old.g*(1-alpha)),Byte(bb+old.b*(1-alpha)),old.a);
    if(fresh.r!=old.r||fresh.g!=old.g||fresh.b!=old.b)changed++;wall[index]=fresh;
   }
   return changed;
  }
  private static byte Byte(float value){return(byte)Math.Max(0,Math.Min(255,(int)Math.Round(value)));}
  private static void Accumulate(Color32[] pixels,int width,int height,int x,int y,float weight,ref float r,ref float g,ref float b,ref float alpha){
   x=Math.Max(0,Math.Min(width-1,x));y=Math.Max(0,Math.Min(height-1,y));var p=pixels[y*width+x];float amount=p.a/255f*weight;r+=p.r*amount;g+=p.g*amount;b+=p.b*amount;alpha+=amount;
  }
 }
}
