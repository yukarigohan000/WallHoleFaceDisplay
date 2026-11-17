using System;using System.IO;using System.Linq;using System.Security.Cryptography;using UnityEngine;
namespace GameModding.MadIsland.WallHoleFaceDisplay {
 internal static class ReferenceDecorationCatalog {
  internal sealed class Info {internal readonly string Name,Resource,Hash;internal readonly float CenterX,CenterY,Width,Height;internal Info(string n,string r,string h,float x,float y,float w,float z){Name=n;Resource=r;Hash=h;CenterX=x;CenterY=y;Width=w;Height=z;}}
  internal static readonly Info Plate=new Info("NamePlateBackground","WHFD.NamePlate.v0272.png","6917342ea2c1a9caae8bb91b95a3a8e91ab87796ee2d701c889242c3c39f53c2",0,0,600,140);
  internal static readonly Info[] Items=new[]{new Info("RedWriting","WHFD.Visual.v0269.red_handwriting.png","e5358434086e8cf8a6a05f04d2533d9e8ff4d77ab0200ff1ae6b14b5325961f7",264f,496f,100f,31.75074183976261f),new Info("FreeWriting","WHFD.Visual.v0267.free_handwriting.png","577a0afec5d88c44516c0282d48b29c2719c266549efd976248d6843088892d2",66.0f,360.0f,60.0f,31.867852604828464f),new Info("TallyMarks","WHFD.Visual.v0267.tally_handwriting.png","48cb9247f07bd66385ca2ea4b734eca3a5ffadd06b68d5cb09ba2fac98b8c6ab",66.0f,403.5f,50.0f,102.41157556270096f),new Info("PinkWriting","WHFD.Visual.v0270.pink_handwriting.png","4ad333b5ca98a549d97451fd66d896956f7100e658ae4ba34e58dc33305752e0",264f,496f,100f,62.32394366197183f),new Info("BalloonPink","WHFD.Visual.v0267.BalloonPink.png","22ff4c2738d67e0ebe6a788b898b297491c99924027676939102fbfc0f89efb2",36.0f,574.0f,17.3310962f,61.0f),new Info("BalloonGreen","WHFD.Visual.v0267.BalloonGreen.png","b94c2ef35209525e75689d48701f70f379cd42ef3ba9a657b76cd1303bc46dba",57.0f,574.0f,17.4089888f,61.0f),new Info("BalloonCyan","WHFD.Visual.v0267.BalloonCyan.png","09bf58e073204d338e97fd9e4f6483f0322d81bb3b111b38b318182d6cf0b7e5",296.0f,574.0f,17.1946309f,61.0f),new Info("BalloonYellow","WHFD.Visual.v0267.BalloonYellow.png","6e7afafac72c967268e6d37a65a8d4e2859128e5b7bbdb36c49a12c2a54f7862",312.0f,574.0f,17.5067265f,61.0f)};
  internal static byte[] Bytes(Info info){using(var stream=typeof(ReferenceDecorationCatalog).Assembly.GetManifestResourceStream(info.Resource)){if(stream==null)throw new InvalidOperationException("Missing reference decoration");using(var data=new MemoryStream()){stream.CopyTo(data);var bytes=data.ToArray();using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()!=info.Hash)throw new InvalidOperationException("Decoration PNG hash mismatch");return bytes;}}}
  internal static VisualRect Placement(Info info,VisualRect hole){if(hole.W<=0||hole.H<=0||float.IsNaN(hole.X+hole.Y+hole.W+hole.H)||float.IsInfinity(hole.X+hole.Y+hole.W+hole.H))throw new ArgumentException("Invalid hole bounds");float sx=hole.W/110f,sy=hole.H/78f,scale=Math.Min(sx,sy);float w=info.Width*scale,h=info.Height*scale;return new VisualRect{X=hole.X+hole.W*.5f+(info.CenterX-169)*sx-w*.5f,Y=hole.Y+hole.H*.5f+(565-info.CenterY)*sy-h*.5f,W=w,H=h};}
  internal static VisualRect Placement(Info info,VisualRect hole,VisualRect wall,VisualRect plate,float bodyTop){
   float cx=hole.X+hole.W*.5f,cy=hole.Y+hole.H*.5f;
   if(info.Name=="RedWriting"){
    float gap=wall.W*.025f,available=plate.Y-gap-bodyTop-gap;if(plate.W<=0||available<=0)return new VisualRect();
    float w=Math.Min(plate.W*.92f,wall.W*.56f),h=w*info.Height/info.Width;if(h>available){h=available;w=h*info.Width/info.Height;}
    if(h<wall.W*.012f)return new VisualRect();return new VisualRect{X=plate.X+plate.W*.5f-w*.5f,Y=plate.Y-gap-h,W=w,H=h};
   }
   bool left=info.Name=="TallyMarks"||info.Name=="FreeWriting"||info.Name=="BalloonPink"||info.Name=="BalloonGreen";
   float outer=left?wall.X+wall.W*.025f:cx+hole.W*.72f,inner=left?cx-hole.W*.72f:wall.Right-wall.W*.025f;
   if(inner<=outer)return new VisualRect();float band=inner-outer;
   if(info.Name=="PinkWriting"){
    // Latest user PNG includes its own arrow. Align the unchanged handwritten tip above/right of the opening.
    float tipU=0.01232394366197183f,tipV=0.08003766478342744f,tipX=cx+hole.W*.67f,tipY=cy+hole.H*.65f;
    float inkWidth=Math.Min(band*.90f,(inner-tipX)/(1-tipU));if(inkWidth<=0)return new VisualRect();float inkHeight=inkWidth*info.Height/info.Width;
    return new VisualRect{X=tipX-inkWidth*tipU,Y=tipY-inkHeight*tipV,W=inkWidth,H=inkHeight};
   }
   if(info.Name.StartsWith("Balloon")){
    var original=Placement(info,hole);float h=original.H*.88f,w=h*info.Width/info.Height;
    float maxWidth=Math.Min(h*(17.5067265f/61f),band*.38f);if(w>maxWidth){h*=maxWidth/w;w=maxWidth;}
    float gap=band*.045f;int index=info.Name=="BalloonPink"||info.Name=="BalloonCyan"?0:1;
    float start=left?outer:inner-(2*maxWidth+gap);float center=start+maxWidth*.5f+index*(maxWidth+gap);
    return new VisualRect{X=center-w*.5f,Y=cy-hole.H*.10f-h*.5f,W=w,H=h};
   }
   float width=band*.90f,height=width*info.Height/info.Width,centerY;
   if(info.Name=="FreeWriting"){var tally=Items.First(i=>i.Name=="TallyMarks");float tallyTop=cy+hole.H*.36f+band*.90f*tally.Height/tally.Width;centerY=Math.Max(hole.Top+hole.H*1.85f,tallyTop+hole.H*.20f)+height*.5f;}
   else if(info.Name=="TallyMarks")centerY=cy+hole.H*.36f+height*.5f;
   else return new VisualRect();
   return new VisualRect{X=(outer+inner)*.5f-width*.5f,Y=centerY-height*.5f,W=width,H=height};
  }
  internal static bool PixelTouchesHole(float px,float py,VisualRect hole){float x=(px-hole.X-hole.W*.5f)/(hole.W*.53f),y=(py-hole.Y-hole.H*.5f)/(hole.H*.53f);return x*x+y*y<1;}
 }
}
