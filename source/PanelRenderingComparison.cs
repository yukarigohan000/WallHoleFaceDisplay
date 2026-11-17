using System;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // One-variable comparisons of the owned extension only. No source wall writes.
    internal sealed class PanelRenderingComparison : IDisposable
    {
        private readonly MeshRenderer panel;
        private readonly Mesh mesh;
        private readonly Vector3[] normals;
        private readonly bool enabled;
        private readonly int sortingOrder, behindOrder, buildId;
        private readonly ShadowCastingMode casting;
        private readonly ManualLogSource log;
        private int state;
        private static readonly string[] States = {
            "0 baseline", "1 panel hidden", "2 panel sorted behind source",
            "3 panel normals absent", "4 panel shadow off", "5 panel shadow two-sided"
        };

        internal PanelRenderingComparison(Renderer source, MeshRenderer ownedPanel, int ownerId, ManualLogSource logger)
        {
            if (source == null || ownedPanel == null) throw new InvalidOperationException("Comparison renderers unavailable");
            var filter = ownedPanel.GetComponent<MeshFilter>();
            var original = source.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable ||
                (original != null && original.sharedMesh == filter.sharedMesh))
                throw new InvalidOperationException("Comparison requires a separate owned readable mesh");
            if (source.sortingOrder <= -32768) throw new InvalidOperationException("No lower sorting order is available");
            panel = ownedPanel; mesh = filter.sharedMesh; normals = mesh.normals;
            enabled = panel.enabled; sortingOrder = panel.sortingOrder; casting = panel.shadowCastingMode;
            behindOrder = source.sortingOrder - 1; buildId = ownerId; log = logger;
        }

        internal void Next()
        {
            if (panel == null || mesh == null) return;
            Restore();
            int next = (state + 1) % States.Length;
            try
            {
                switch (next)
                {
                    case 1: panel.enabled = false; break;
                    case 2: panel.sortingOrder = behindOrder; break;
                    case 3: mesh.normals = new Vector3[0]; break;
                    case 4: panel.shadowCastingMode = ShadowCastingMode.Off; break;
                    case 5: panel.shadowCastingMode = ShadowCastingMode.TwoSided; break;
                }
                state = next;
                log.LogInfo("[WallCompare] buildID=" + buildId + "; " + States[state] +
                    "; enabled=" + panel.enabled + "; sorting=" + panel.sortingOrder +
                    "; casting=" + panel.shadowCastingMode + "; normals=" + mesh.normals.Length);
            }
            catch { Restore(); state = 0; throw; }
        }
        private void Restore()
        {
            if (panel != null) { panel.enabled = enabled; panel.sortingOrder = sortingOrder; panel.shadowCastingMode = casting; }
            if (mesh != null) mesh.normals = normals;
        }
        public void Dispose() { Restore(); }
    }
}
