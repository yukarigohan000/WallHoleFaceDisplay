using System;
using Spine;
using UnityEngine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Per-private-Skeleton reference, never persisted under a monitor/instance key.
    internal sealed class HeadFramingReference
    {
        private readonly Skeleton source;
        internal Vector2 Center {get;private set;}
        internal float Half {get;private set;}
        internal HeadFramingReference(Skeleton source,Vector2 center,float half){this.source=source;Center=center;Half=half;}
        internal bool Matches(Skeleton pose){return ReferenceEquals(source,pose);}
    }
    // Camera composition only. Never filters slots, attachments, bones or generated triangles.
    internal static class RestraintBustFraming
    {
        internal static Rect Measure(Skeleton skeleton)
        {
            // Head is confirmed in this rig. Neck/shoulders may be part of another mesh.
            // Their topology or separate slot names are not prerequisites for filming the rig.
            Rect head = Bounds(skeleton.FindSlot("Head"));
            float left = head.xMin, right = head.xMax, top = head.yMax;
            foreach (var name in new[] { "HairFront", "HairBack", "HairBack2", "HairBack_H", "HairBack_L" })
            {
                var slot = skeleton.FindSlot(name);
                if (slot == null || !(slot.Attachment is RegionAttachment || slot.Attachment is MeshAttachment)) continue;
                var hair = Bounds(slot);
                left = Mathf.Min(left, hair.xMin); right = Mathf.Max(right, hair.xMax); top = Mathf.Max(top, hair.yMax);
            }
            // Two measured head extents is the initial portrait field of view, not an
            // assertion about where a nonexistent Neck slot or anatomical joint lies.
            // Keep the top of the head/hair in view and reserve the lower part for neck/shoulders.
            float span = Mathf.Max(head.width, head.height) * 2f;
            span = Mathf.Max(span, right - left);
            float viewTop = top + head.height * 0.1f;
            float centerX = (left + right) * 0.5f;
            return new Rect(centerX - span * 0.5f, viewTop - span, span, span);
        }

        internal static bool TryCaptureFace(Skeleton pose,Rect initialView,out HeadFramingReference reference)
        {
            reference=null;
            try {
                // Exclude hair from the center. Use the actual deformed Head attachment,
                // not its owner bone (weighted attachments may belong to a root slot).
                var head=Bounds(pose.FindSlot("Head"));var center=head.center;
                // Preserve the existing two-head portrait scale; wide hair may leave the frame.
                // Hair geometry must not change face magnification or normalized X/Y distance.
                float half=(Mathf.Max(head.width,head.height)*2f)*.54f;
                if(!Finite(center.x)||!Finite(center.y)||!Finite(half)||half<=0)return false;
                reference=new HeadFramingReference(pose,center,half);return true;
            }catch(Exception){return false;} // no new fatal prerequisite; retain legacy framing
        }
        internal static string AuditAttachments(Skeleton pose)
        {
            var text=new System.Text.StringBuilder();
            foreach(var slot in pose.Slots){
                text.Append("|").Append(slot.Data.Name).Append("[bone=").Append(slot.Bone.Data.Name).Append(";active=").Append(slot.Bone.Active).Append(";alpha=").Append(slot.A.ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append(";attachment=").Append(slot.Attachment==null?"none":slot.Attachment.Name);
                try{var v=Bounds(slot);text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,";bounds={0:R},{1:R},{2:R},{3:R}",v.xMin,v.yMin,v.xMax,v.yMax);}catch(Exception e){text.Append(";boundsUnavailable=").Append(e.GetType().Name);}
                text.Append("]");
            }
            return text.ToString();
        }
        private static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        internal static Rect Union(Rect a, Rect b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        }

        private static Rect Bounds(Slot slot)
        {
            if (slot == null) throw new MonitorVideoConditionException("Confirmed portrait Head slot unavailable");
            float[] points;
            var region = slot.Attachment as RegionAttachment;
            var mesh = slot.Attachment as MeshAttachment;
            if (region != null) { points = new float[8]; region.ComputeWorldVertices(slot.Bone, points, 0, 2); }
            else if (mesh != null) { points = new float[mesh.WorldVerticesLength]; mesh.ComputeWorldVertices(slot, 0, points.Length, points, 0, 2); }
            else throw new MonitorVideoConditionException("Portrait reference attachment unavailable");
            float left = float.PositiveInfinity, right = float.NegativeInfinity, bottom = left, top = right;
            for (int i = 0; i < points.Length; i += 2)
            {
                float x = points[i], y = points[i + 1];
                if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y))
                    throw new MonitorVideoConditionException("Nonfinite portrait reference vertices");
                left = Mathf.Min(left, x); right = Mathf.Max(right, x); bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y);
            }
            if (right <= left || top <= bottom) throw new MonitorVideoConditionException("Empty portrait reference bounds");
            return Rect.MinMaxRect(left, bottom, right, top);
        }
    }
}
