using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Spine;
using UnityEngine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Bounded read-only comparison for the one portrait being generated. No UV changes.
    internal static class MonitorFaceAudit
    {
        internal static void Report(Skeleton original, Skeleton snapshot, float spacing, Vector3[] input,
            int[] triangles, int activeVertices, Vector3[] output, Plane plane, Material[] materials, ManualLogSource log)
        {
            if(!MonitorDebugSettings.Enabled)return;
            try
            {
                int attachments = 0, order = 0, bones = 0, colors = 0, deform = 0;
                float maxDelta = 0;
                foreach (var slot in snapshot.Slots)
                {
                    var source = original.Slots.Items[slot.Data.Index];
                    if (slot.Attachment == null) continue; // Masked attachments are reported separately.
                    if (!ReferenceEquals(source.Attachment, slot.Attachment)) attachments++;
                    if (source.R != slot.R || source.G != slot.G || source.B != slot.B || source.A != slot.A ||
                        source.R2 != slot.R2 || source.G2 != slot.G2 || source.B2 != slot.B2) colors++;
                    if (!source.Deform.SequenceEqual(slot.Deform)) deform++;
                    var a = Positions(source); var b = Positions(slot);
                    if (a.Length != b.Length) maxDelta = float.PositiveInfinity;
                    else for (int i = 0; i < a.Length; i++) maxDelta = Math.Max(maxDelta, Math.Abs(a[i] - b[i]));
                }
                for (int i = 0; i < snapshot.DrawOrder.Count; i++)
                    if (original.DrawOrder.Items[i].Data.Index != snapshot.DrawOrder.Items[i].Data.Index) order++;
                var fields = typeof(Bone).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .Where(field => field.FieldType == typeof(float) || field.FieldType == typeof(bool) || field.FieldType == typeof(int)).ToArray();
                for (int i = 0; i < snapshot.Bones.Count; i++)
                    if (fields.Any(field => !Equals(field.GetValue(original.Bones.Items[i]), field.GetValue(snapshot.Bones.Items[i])))) bones++;
                var head = original.FindSlot("Head").Bone;
                var excluded = original.Slots.Where(slot => snapshot.Slots.Items[slot.Data.Index].Attachment == null &&
                    (slot.Attachment is RegionAttachment || slot.Attachment is MeshAttachment) && Under(slot.Bone, head))
                    .Select(slot => slot.Data.Index.ToString()).ToArray();
                log.LogInfo("[MonitorFaceAudit] npcId=15; attachmentMismatch=" + attachments + "; drawOrderMismatch=" + order +
                    "; boneMismatch=" + bones + "; colorMismatch=" + colors + "; deformMismatch=" + deform +
                    "; maxWorldVertexDelta=" + maxDelta + "; excludedRenderableUnderHeadSlots=" + string.Join(",", excluded) +
                    "; clipping=" + snapshot.Slots.Count(slot => slot.Attachment is ClippingAttachment));
                var used = triangles.Distinct().ToArray();
                int invalid = used.Count(index => index < 0 || index >= activeVertices);
                var valid = used.Where(index => index >= 0 && index < activeVertices).ToArray();
                var depth = materials.Distinct().Take(3).Select(material => "shader=" + material.shader.name +
                    ",ZWrite=" + Property(material, "_ZWrite") + ",ZTest=" + Property(material, "_ZTest") +
                    ",Cull=" + Property(material, "_Cull"));
                log.LogInfo("[MonitorFaceDepth] npcId=15; zSpacing=" + spacing + "; activeVertices=" + activeVertices +
                    "; capacity=" + input.Length + "; invalidReferencedIndices=" + invalid +
                    "; inputZ=" + valid.Min(index => input[index].z) + ".." + valid.Max(index => input[index].z) +
                    "; outputPlaneDistance=" + valid.Min(index => plane.GetDistanceToPoint(output[index])) + ".." +
                    valid.Max(index => plane.GetDistanceToPoint(output[index])) + "; submeshes=" + materials.Length +
                    "; depthState=" + string.Join("|", depth.ToArray()));
            }
            catch (Exception error) { log.LogWarning("[MonitorFaceAudit] npcId=15; unavailable=" + error.GetType().Name); }
        }

        private static string Property(Material material, string property)
        {
            return material.HasProperty(property) ? material.GetFloat(property).ToString() : "not-exposed";
        }
        private static bool Under(Bone bone, Bone ancestor)
        {
            for (; bone != null; bone = bone.Parent) if (ReferenceEquals(bone, ancestor)) return true;
            return false;
        }
        private static float[] Positions(Slot slot)
        {
            var region = slot.Attachment as RegionAttachment;
            if (region != null)
            {
                var result = new float[8]; region.ComputeWorldVertices(slot.Bone, result, 0, 2); return result;
            }
            var mesh = slot.Attachment as VertexAttachment;
            if (mesh != null)
            {
                var result = new float[mesh.WorldVerticesLength];
                mesh.ComputeWorldVertices(slot, 0, result.Length, result, 0, 2); return result;
            }
            return new float[0];
        }
    }
}
