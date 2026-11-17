using System;
using System.Collections.Generic;
using System.Linq;
using Spine;
using Spine.Unity;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Reuse only the measured static wall_00 surface and its existing normalized atlas UVs.
    internal sealed class WoodPanelSurface
    {
        // Measured from the supplied fixed-view wall images: the top cap/rim ends
        // at ~3% of the original wall height. This is wall_00 Region-V, not world Y.
        private const float FrontFaceJoinV = 0.03f;
        internal Vector3 MonitorCenter;
        internal Vector3 BaseCenter, UpDirection, RightDirection;
        internal float Width, Height;

        internal static WoodPanelSurface Create(Renderer source, Transform parent, float ratio,
            int sortingLayer, int sortingOrder, List<Object> owned)
        {
            var animation = source.GetComponent<SkeletonAnimation>();
            var skeleton = animation == null ? null : animation.skeleton;
            if (skeleton == null || skeleton.Slots.Count == 0) throw new InvalidOperationException("Static wall skeleton unavailable");
            var slot = skeleton.Slots.Items[0];
            var attachment = slot.Attachment as MeshAttachment;
            if (slot.Data.Name != "Wall_00" || attachment == null || attachment.Name != "wall_00" ||
                attachment.WorldVerticesLength != 16 || attachment.UVs == null || attachment.UVs.Length != 16 ||
                attachment.RegionUVs == null || attachment.RegionUVs.Length != 16)
                throw new InvalidOperationException("Measured eight-vertex wall attachment differs");
            var region = attachment.RendererObject as AtlasRegion;
            var atlasMaterial = region == null || region.page == null ? null : region.page.rendererObject as Material;
            if (atlasMaterial == null) throw new InvalidOperationException("Wall atlas material unavailable");
            var matches = source.sharedMaterials.Where(m => m != null &&
                (m == atlasMaterial || (m.mainTexture == atlasMaterial.mainTexture && m.shader == atlasMaterial.shader))).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Wall material match is ambiguous");

            var positions = new float[16];
            attachment.ComputeWorldVertices(slot, positions);
            // Region UV corners are mapped through actual attachment triangles, not atlas pixel dimensions.
            var bottomLeft = WorldAt(source, attachment, positions, new Vector2(0, 1));
            var bottomRight = WorldAt(source, attachment, positions, new Vector2(1, 1));
            var topLeft = WorldAt(source, attachment, positions, new Vector2(0, 0));
            var topRight = WorldAt(source, attachment, positions, new Vector2(1, 0));
            var upLeft = topLeft - bottomLeft;
            var upRight = topRight - bottomRight;
            float wallHeight = (upLeft.magnitude + upRight.magnitude) * 0.5f;
            if (wallHeight <= 0 || ratio <= 0 || float.IsNaN(ratio) || float.IsInfinity(ratio))
                throw new InvalidOperationException("Invalid measured wall height");

            // Log/image evidence: upper band is region V=0..~1/3, above the hole.
            // Reuse its middle half, excluding the top cap and lower band edge.
            var rows = attachment.RegionUVs.Where((v, i) => i % 2 == 1).Distinct().OrderBy(v => v).ToArray();
            if (rows.Length != 4 || Mathf.Abs(rows[0]) > 0.0001f || Mathf.Abs(rows[1] - 1f / 3f) > 0.001f ||
                Mathf.Abs(rows[3] - 1f) > 0.0001f)
                throw new InvalidOperationException("Measured wall UV bands differ; refusing an unverified wood crop");
            float vTop = Mathf.Lerp(rows[0], rows[1], 0.25f);
            float vBottom = Mathf.Lerp(rows[0], rows[1], 0.75f);
            float bandFraction = vBottom - vTop;
            float seamStep = RegionTexelStep(attachment, region, vTop, vBottom);
            float joinFraction = FrontFaceJoinV + seamStep;
            // Map the known front-face wood once, continuously, over the existing geometry.
            // Keep the verified lower-edge UV and avoid the cap/hole regions.
            if (joinFraction >= vBottom || ratio + joinFraction <= 0)
                throw new InvalidOperationException("Measured front join exceeds verified upper wood region");
            var normal = Vector3.Cross(topRight - topLeft, upLeft).normalized;
            var camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("Camera unavailable for front join");
            if (Vector3.Dot(normal, camera.transform.position - (topLeft + topRight) * 0.5f) < 0) normal = -normal;
            // Only the contacting lower edge sits half an atlas texel in front,
            // avoiding coplanar depth fighting while joining back to the existing panel plane.
            var contactOffset = normal * (wallHeight * seamStep * 0.5f);
            int strips = Mathf.CeilToInt(ratio / bandFraction);
            if (strips < 1 || strips > 64) throw new InvalidOperationException("Wood strip count outside bounds");
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            var colors = new List<Color32>();
            var filter = source.GetComponent<MeshFilter>();
            var originalColors = filter == null || filter.sharedMesh == null ? null : filter.sharedMesh.colors32;
            Color32 color = originalColors == null || originalColors.Length == 0 ? (Color32)Color.white : originalColors[0];
            for (int strip = 0; strip < strips; strip++)
            {
                float start = strip * bandFraction;
                float end = Mathf.Min(ratio, start + bandFraction);
                int n = vertices.Count;
                // Connect to measured front-face wood below the original decorative top cap.
                // This same continuous quad casts the connecting shadow; no duplicate caster.
                float lower = strip == 0 ? -joinFraction : start;
                float lowerV = ContinuousWoodV(lower, ratio, joinFraction, vBottom);
                float sampleTop = ContinuousWoodV(end, ratio, joinFraction, vBottom);
                var offset = strip == 0 ? contactOffset : Vector3.zero;
                vertices.Add(parent.InverseTransformPoint(topLeft + upLeft * lower + offset));
                vertices.Add(parent.InverseTransformPoint(topRight + upRight * lower + offset));
                vertices.Add(parent.InverseTransformPoint(topLeft + upLeft * end));
                vertices.Add(parent.InverseTransformPoint(topRight + upRight * end));
                uv.Add(UVAt(attachment, new Vector2(0, lowerV)));
                uv.Add(UVAt(attachment, new Vector2(1, lowerV)));
                uv.Add(UVAt(attachment, new Vector2(0, sampleTop)));
                uv.Add(UVAt(attachment, new Vector2(1, sampleTop)));
                triangles.AddRange(new[] { n, n + 2, n + 1, n + 1, n + 2, n + 3 });
                for (int i = 0; i < 4; i++) colors.Add(color);
            }
            var mesh = new Mesh { name = "WHFD_OriginalWoodExtension" };
            owned.Add(mesh);
            mesh.vertices = vertices.ToArray(); mesh.uv = uv.ToArray();
            mesh.triangles = triangles.ToArray(); mesh.colors32 = colors.ToArray();
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            var panel = new GameObject("WoodExtension");
            panel.layer = source.gameObject.layer; panel.transform.SetParent(parent, false);
            panel.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = panel.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = matches[0]; // Shared read-only reference; never destroy or edit it.
            renderer.sortingLayerID = sortingLayer; renderer.sortingOrder = sortingOrder;
            var properties = new MaterialPropertyBlock(); source.GetPropertyBlock(properties); renderer.SetPropertyBlock(properties);
            return new WoodPanelSurface {
                Width = Vector3.Distance(topLeft, topRight), Height = wallHeight * ratio,
                BaseCenter = (topLeft + topRight) * 0.5f,
                UpDirection = (upLeft + upRight).normalized, RightDirection = (topRight - topLeft).normalized,
                MonitorCenter = (topLeft + topRight) * 0.5f + (upLeft + upRight) * (ratio * 0.39f)
            };
        }

        // Prototype-only visual cap: append one quad above the existing top. No old vertex moves.
        internal static void ExtendPrototypeTop(WoodPanelSurface surface,Renderer panel,float extra)
        {
            if(extra<=0||float.IsNaN(extra)||float.IsInfinity(extra))throw new InvalidOperationException("Invalid visual extension");
            var mesh=panel.GetComponent<MeshFilter>().sharedMesh;var old=mesh.vertices;var oldUv=mesh.uv;var colors=mesh.colors32;
            var v=new List<Vector3>(old);var uv=new List<Vector2>(oldUv);var c=new List<Color32>(colors);var triangles=new List<int>(mesh.triangles);int n=v.Count;
            var top=surface.BaseCenter+surface.UpDirection*surface.Height;var half=surface.RightDirection*surface.Width*.5f;
            v.Add(panel.transform.InverseTransformPoint(top-half));v.Add(panel.transform.InverseTransformPoint(top+half));v.Add(panel.transform.InverseTransformPoint(top-half+surface.UpDirection*extra));v.Add(panel.transform.InverseTransformPoint(top+half+surface.UpDirection*extra));
            // Replaced with the continuous physical projection before assigning the concrete material.
            for(int i=0;i<4;i++){uv.Add(oldUv[oldUv.Length-2+i%2]);c.Add(colors.Length==0?(Color32)Color.white:colors[0]);}
            triangles.AddRange(new[]{n,n+2,n+1,n+1,n+2,n+3});mesh.vertices=v.ToArray();mesh.uv=uv.ToArray();mesh.colors32=c.ToArray();mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();surface.Height+=extra;
        }

        private static float ContinuousWoodV(float position, float ratio, float join, float upperUV)
        {
            float progress = (position + join) / (ratio + join);
            if (float.IsNaN(progress) || float.IsInfinity(progress) || progress < -0.00001f || progress > 1.00001f || upperUV <= join)
                throw new InvalidOperationException("Wood UV position outside continuous panel range");
            return join + (upperUV - join) * Mathf.Clamp01(progress);
        }

        private static float RegionTexelStep(MeshAttachment mesh, AtlasRegion region, float top, float bottom)
        {
            if (region == null || region.page == null || region.page.width <= 0 || region.page.height <= 0)
                throw new InvalidOperationException("Atlas page dimensions unavailable for seam inset");
            var direction = UVAt(mesh, new Vector2(0.5f, bottom)) - UVAt(mesh, new Vector2(0.5f, top));
            // Measure in atlas-page texels using the actual UV mapping, including rotation.
            // Do not reinterpret atlas pixel XY using the differently sized GPU texture.
            float texelSpan = new Vector2(direction.x * region.page.width, direction.y * region.page.height).magnitude;
            if (float.IsNaN(texelSpan) || float.IsInfinity(texelSpan) || texelSpan <= 2f || bottom <= top)
                throw new InvalidOperationException("Wood band too small for a one-texel seam inset");
            return (bottom - top) / texelSpan;
        }

        internal static Vector3 WorldAt(Renderer source, MeshAttachment mesh, float[] positions, Vector2 point)
        {
            int a, b, c; Vector3 weights; FindTriangle(mesh, point, out a, out b, out c, out weights);
            var value = new Vector3(positions[a * 2], positions[a * 2 + 1], 0) * weights.x +
                new Vector3(positions[b * 2], positions[b * 2 + 1], 0) * weights.y +
                new Vector3(positions[c * 2], positions[c * 2 + 1], 0) * weights.z;
            return source.transform.TransformPoint(value);
        }
        private static Vector2 UVAt(MeshAttachment mesh, Vector2 point)
        {
            int a, b, c; Vector3 weights; FindTriangle(mesh, point, out a, out b, out c, out weights);
            return new Vector2(mesh.UVs[a * 2], mesh.UVs[a * 2 + 1]) * weights.x +
                new Vector2(mesh.UVs[b * 2], mesh.UVs[b * 2 + 1]) * weights.y +
                new Vector2(mesh.UVs[c * 2], mesh.UVs[c * 2 + 1]) * weights.z;
        }
        private static void FindTriangle(MeshAttachment mesh, Vector2 point, out int a, out int b, out int c, out Vector3 weights)
        {
            if (mesh.Triangles == null || mesh.Triangles.Length % 3 != 0) throw new InvalidOperationException("Invalid wall triangles");
            for (int i = 0; i < mesh.Triangles.Length; i += 3)
            {
                a = mesh.Triangles[i]; b = mesh.Triangles[i + 1]; c = mesh.Triangles[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= 8 || b >= 8 || c >= 8) throw new InvalidOperationException("Invalid wall triangle index");
                var p = new Vector2(mesh.RegionUVs[a * 2], mesh.RegionUVs[a * 2 + 1]);
                var u = new Vector2(mesh.RegionUVs[b * 2], mesh.RegionUVs[b * 2 + 1]) - p;
                var v = new Vector2(mesh.RegionUVs[c * 2], mesh.RegionUVs[c * 2 + 1]) - p;
                var q = point - p;
                float determinant = u.x * v.y - v.x * u.y;
                if (Mathf.Abs(determinant) < 0.000001f) continue;
                float y = (q.x * v.y - v.x * q.y) / determinant;
                float z = (u.x * q.y - q.x * u.y) / determinant;
                weights = new Vector3(1 - y - z, y, z);
                if (weights.x >= -0.00001f && y >= -0.00001f && z >= -0.00001f) return;
            }
            a = b = c = 0; weights = Vector3.zero;
            throw new InvalidOperationException("Requested wood region lies outside measured wall triangles");
        }
    }
}
