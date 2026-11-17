using System;
using UnityEngine;
using UnityEngine.UI;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Keep release protected too, even outside the window. Does not consume gameplay keys.
    internal sealed class MonitorFramingPointerCapture
    {
        private bool captured;
        internal bool Step(bool visible, bool inside, bool held, bool down)
        {
            if (!visible) { captured=false; return false; }
            // Also capture a held button entering the panel; legacy sliders can take it.
            if (inside && (held || down)) captured=true;
            bool protect=captured;
            if (!held) captured=false;
            return protect;
        }
    }

    // Native OnUICheck reads EventSystem.IsPointerOverGameObject. Match the established
    // local UI-shield pattern, without writing game flags or patching input methods.
    [DefaultExecutionOrder(-10000)]
    internal sealed class MonitorFramingInputShield : MonoBehaviour
    {
        private Func<bool> visible;
        private Func<Rect> bounds;
        private RectTransform area;
        private int captureFrame=-1;
        private bool protectedThisFrame, lastVisible;
        private readonly MonitorFramingPointerCapture capture=new MonitorFramingPointerCapture();
        internal void Initialize(Func<bool> visible, Func<Rect> bounds)
        {
            this.visible=visible; this.bounds=bounds;
            var canvas=gameObject.AddComponent<Canvas>();
            canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=32759;
            gameObject.AddComponent<GraphicRaycaster>();
            var panel=new GameObject("FramingPointerArea",typeof(RectTransform),typeof(Image));
            panel.transform.SetParent(transform,false);
            var image=panel.GetComponent<Image>();image.color=Color.clear;image.raycastTarget=true;
            area=panel.GetComponent<RectTransform>();area.anchorMin=area.anchorMax=area.pivot=new Vector2(0,1);
            area.gameObject.SetActive(false);
            DontDestroyOnLoad(gameObject);
        }
        private void Update() { Refresh(); }
        internal void Refresh()
        {
            if (area==null || visible==null) return;
            bool show=visible();
            var rect=bounds();
            var mouse=Input.mousePosition;
            bool inside=rect.Contains(new Vector2(mouse.x,Screen.height-mouse.y));
            // Update and IMGUI may both refresh in one frame. Sample capture only once,
            // otherwise the second call would drop the release-frame shield.
            if(captureFrame!=Time.frameCount || show!=lastVisible) {
                protectedThisFrame=capture.Step(show,inside,Input.GetMouseButton(0),Input.GetMouseButtonDown(0));
                captureFrame=Time.frameCount;lastVisible=show;
            }
            bool protect=protectedThisFrame;
            area.gameObject.SetActive(show);
            if (!show) return;
            // Cover the screen only for a UI-originated pointer gesture, including release.
            if (protect) rect=new Rect(0,0,Screen.width,Screen.height);
            area.anchoredPosition=new Vector2(rect.x,-rect.y);
            area.sizeDelta=new Vector2(rect.width,rect.height);
        }
        internal void Hide()
        {
            capture.Step(false,false,false,false);
            captureFrame=-1;protectedThisFrame=false;lastVisible=false;
            if(area!=null) area.gameObject.SetActive(false);
        }
        private void OnDisable() { Hide(); }
    }
}
