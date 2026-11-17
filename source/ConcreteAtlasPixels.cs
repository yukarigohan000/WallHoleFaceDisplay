using System;using UnityEngine;
namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal static class ConcreteAtlasPixels
    {
        // Convert unchanged atlas UVs back to the attachment's own region coordinates.
        // This preserves rotated/trimmed atlas addressing without rewriting a single UV.
        internal static void Paint(Color32[] atlas,int width,int height,float[] uv,float[] region,Color32[] concrete,int concreteWidth,int concreteHeight,bool premultiplied,int[] triangles=null)
        {
            if(atlas==null||atlas.Length!=width*height||uv==null||region==null||uv.Length!=16||region.Length!=16||concrete==null||concrete.Length!=concreteWidth*concreteHeight)throw new ArgumentException("Invalid concrete atlas input");
            int a=0,b=-1,c=-1;float determinant=0;
            for(int j=1;j<8&&c<0;j++)for(int k=j+1;k<8;k++){
                float dx1=uv[j*2]-uv[0],dy1=uv[j*2+1]-uv[1],dx2=uv[k*2]-uv[0],dy2=uv[k*2+1]-uv[1];float det=dx1*dy2-dx2*dy1;
                if(Math.Abs(det)>1e-9f){b=j;c=k;determinant=det;break;}}
            if(c<0)throw new ArgumentException("Degenerate wall UVs");
            float minU=uv[0],maxU=uv[0],minV=uv[1],maxV=uv[1];for(int i=1;i<8;i++){minU=Math.Min(minU,uv[i*2]);maxU=Math.Max(maxU,uv[i*2]);minV=Math.Min(minV,uv[i*2+1]);maxV=Math.Max(maxV,uv[i*2+1]);}
            int x0=(int)Math.Floor(minU*width),x1=(int)Math.Ceiling(maxU*width),y0=(int)Math.Floor(minV*height),y1=(int)Math.Ceiling(maxV*height);
            if(x0<0||y0<0||x1>width||y1>height)throw new ArgumentException("Wall UVs exceed atlas");
            float ux=uv[b*2]-uv[a*2],uy=uv[b*2+1]-uv[a*2+1],vx=uv[c*2]-uv[a*2],vy=uv[c*2+1]-uv[a*2+1];
            for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++){
                float px=(x+.5f)/width-uv[a*2],py=(y+.5f)/height-uv[a*2+1];float wb=(px*vy-vx*py)/determinant,wc=(ux*py-px*uy)/determinant,wa=1-wb-wc;
                float u=wa*region[a*2]+wb*region[b*2]+wc*region[c*2],v=wa*region[a*2+1]+wb*region[b*2+1]+wc*region[c*2+1];
                // Physical projection is piecewise affine on the actual wall triangles.
                if(triangles!=null){for(int ti=0;ti<triangles.Length;ti+=3){int ia=triangles[ti]*2,ib=triangles[ti+1]*2,ic=triangles[ti+2]*2;float ax=uv[ia],ay=uv[ia+1],bx=uv[ib]-ax,by=uv[ib+1]-ay,cx=uv[ic]-ax,cy=uv[ic+1]-ay,det=bx*cy-cx*by;if(Math.Abs(det)<1e-10f)continue;float dx=(x+.5f)/width-ax,dy=(y+.5f)/height-ay,bb=(dx*cy-cx*dy)/det,cc=(bx*dy-dx*by)/det,aa=1-bb-cc;if(aa<-.00001f||bb<-.00001f||cc<-.00001f)continue;u=aa*region[ia]+bb*region[ib]+cc*region[ic];v=aa*region[ia+1]+bb*region[ib+1]+cc*region[ic+1];break;}/* Outside triangle centers in the one-texel bounding rim retain the affine edge sample, preventing old RGB bleed under bilinear filtering. */}
                int sx=Math.Max(0,Math.Min(concreteWidth-1,(int)(u*concreteWidth))),sy=Math.Max(0,Math.Min(concreteHeight-1,(int)((1-v)*concreteHeight)));
                var fresh=concrete[sy*concreteWidth+sx];byte alpha=atlas[y*width+x].a;
                // All RGB comes from the new PNG, including dark grain and AA texels.
                // Only the original coverage (alpha) is retained; original RGB is never mixed in.
                if(premultiplied){fresh.r=(byte)((fresh.r*alpha+127)/255);fresh.g=(byte)((fresh.g*alpha+127)/255);fresh.b=(byte)((fresh.b*alpha+127)/255);}
                atlas[y*width+x]=new Color32(fresh.r,fresh.g,fresh.b,alpha);
            }
        }
    }
}


