using System;
namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Deterministic, code-native surface; never reads or changes geometry.
    internal static class ConcretePattern
    {
        private static uint Hash(int x,int y){unchecked{uint n=(uint)x*374761393u+(uint)y*668265263u+9137u;n=(n^(n>>13))*1274126177u;return n^(n>>16);}}
        private static double Cloud(int x,int y)
        {
            const int cell=73;int gx=x/cell,gy=y/cell;double a=(x%cell)/(double)cell,b=(y%cell)/(double)cell;a=a*a*(3-2*a);b=b*b*(3-2*b);
            double p=(Hash(gx,gy)%256)/255d,q=(Hash(gx+1,gy)%256)/255d,r=(Hash(gx,gy+1)%256)/255d,s=(Hash(gx+1,gy+1)%256)/255d;
            return (p+(q-p)*a)*(1-b)+(r+(s-r)*a)*b;
        }
        internal static int Rgb(int x,int y,int width,int height)
        {
            uint h=Hash(x,y);double coarse=Cloud(x,y);
            int noise=(int)(h%17)-8;double stain=Math.Max(0,Math.Sin(x*.043+Math.Sin(y*.009)*.6)-.83)*55;
            double damp=Math.Max(0,1-y/(double)Math.Max(1,height)*9)*16;
            int gray=(int)(153+coarse*17+noise-stain-damp);
            if(h%1091<3)gray-=31; // small pores
            if(Math.Abs(Math.Sin(y*.017+x*.039+Math.Sin(y*.007)*1.2))<.007)gray-=17;
            int r=Math.Max(70,Math.Min(190,gray+3));int g=Math.Max(65,Math.Min(188,gray));int b=Math.Max(60,Math.Min(184,gray-5-(int)(stain*.15)));
            return (r<<16)|(g<<8)|b;
        }
        internal static bool Recolorable(byte r,byte g,byte b,byte alpha)
        {return alpha==255&&(int)r+g+b>=150;} // Preserve transparent/AA pixels and dark opening/outline texels verbatim.
        internal static string NameText(int? id,string name)
        {return (id==15||id==16||id==17||id==44||id==90)?name??string.Empty:string.Empty;}
    }
    internal struct VisualRect
    {
        internal float X,Y,W,H;
        internal float Right{get{return X+W;}}internal float Top{get{return Y+H;}}
    }
    internal static class VisualDecorationLayout
    {
        // Input is the measured safe upper wall band, clipped below the existing monitor.
        internal static bool TryCreate(float left,float right,float bottom,float top,float monitorBottom,out VisualRect plate,out VisualRect warning)
        {
            plate=warning=new VisualRect();float width=right-left;
            if(!Finite(left)||!Finite(right)||!Finite(bottom)||!Finite(top)||!Finite(monitorBottom)||width<=0)return false;
            float margin=width*.035f;top=Math.Min(top,monitorBottom-margin)-margin;
            float h=Math.Min(width*.14f,(top-bottom)*.7f);
            if(h<width*.055f)return false;
            plate=new VisualRect{X=left+width*.08f,Y=top-h,W=width*.60f,H=h};
            warning=new VisualRect{X=left+width*.73f,Y=top-h,W=width*.17f,H=h};return true;
        }
        internal static bool TryCenterUnderMonitor(VisualRect monitor,float minimumBottom,VisualRect originalPlate,VisualRect warning,out VisualRect plate)
        {
            plate=new VisualRect();
            if(!Finite(monitor.X)||!Finite(monitor.Y)||!Finite(monitor.W)||!Finite(monitor.H)||!Finite(minimumBottom)||monitor.W<=0||monitor.H<=0||originalPlate.W<=0||originalPlate.H<=0)return false;
            float gap=monitor.H*.03f;
            plate=new VisualRect{X=monitor.X+monitor.W*.5f-originalPlate.W*.5f,Y=monitor.Y-gap-originalPlate.H,W=originalPlate.W,H=originalPlate.H};
            if(plate.Y<minimumBottom)return false;
            if(plate.X<warning.Right&&plate.Right>warning.X&&plate.Y<warning.Top&&plate.Top>warning.Y)return false;
            return true;
        }
        private static bool Finite(float v){return !float.IsNaN(v)&&!float.IsInfinity(v);}
    }
}
