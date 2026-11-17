using System;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Camera framing, read-only diagnostics and private-monitor playback settings only.
    internal sealed class MonitorFramingDebugUI : IDisposable
    {
        private readonly ConfigEntry<KeyboardShortcut> key;
        private bool uiReady;
        private readonly Func<MonitorDisplayRouter> current;
        private readonly Func<string> targetLabel;
        internal bool IsOpen {get {return open;}}
        private readonly Func<MonitorStateDiagnostics> diagnostics;
        private readonly ConfigFile config;
        private readonly ConfigEntry<string>[] playbackPoses, playbackFaces;
        private readonly Action refreshPlayback;
        private readonly string[] draftPoses = new string[4], draftFaces = new string[4];
        private static readonly string[] StateLabels = { "モード0：待機", "モード1：通常", "モード2：高速", "モード3：最終状態" };
        private bool diagnosticTab, playbackTab, armTab, headTab, torsoTab;
        private int observedLegacyTab=-1;
        private readonly MonitorNpcProfiles npcProfiles=new MonitorNpcProfiles(Paths.ConfigPath);
        private string npcInput="15",npcLoadMessage="";
        private object templateDraftOwner;
        private MonitorTemplateDisplay templateDraftDisplay;
        private int templateDraftId=-1,templateScope=-1,templateTab;
        private bool templatePreview,templateDragging,posePreview,legacyPlaybackPreview;
        private MonitorDisplayRouter playbackPreviewOwner;
        private string playbackPreviewSignature;
        private bool OwnsPlaybackPreview(MonitorDisplayRouter requester)
        {return open&&playbackTab&&legacyPlaybackPreview&&requester!=null&&ReferenceEquals(requester,current())&&ReferenceEquals(requester,playbackPreviewOwner)&&!requester.IsTemplate&&requester.NumericNpcId==15;}
        internal string PlaybackPoseFor(MonitorDisplayRouter requester,int mode,string saved)
        {return OwnsPlaybackPreview(requester)&&Contains(requester.PoseNames,draftPoses[editMode])?draftPoses[editMode]:saved;}
        internal string PlaybackFaceFor(MonitorDisplayRouter requester,int mode,string saved)
        {return OwnsPlaybackPreview(requester)&&Contains(requester.FaceNames,draftFaces[editMode])?draftFaces[editMode]:saved;}
        private void StopPlaybackPreview()
        {legacyPlaybackPreview=false;var owner=playbackPreviewOwner;playbackPreviewOwner=null;playbackPreviewSignature=null;if(owner!=null)owner.RefreshPlaybackSettings();}

        private MonitorNpcValues templateDraft;
        private string templateLX="0",templateLY="0",templateRX="0",templateRY="0",templateHead="0",templateTorso="0",templateX="0",templateY="0",templateZoom="1";
        private Vector2 templateScroll,legacyScroll;
        private bool templateDetails;
        private readonly MonitorPoseSettings poseSettings;
        private int poseEditScope=-1;
        private object poseDraftOwner;
        private readonly MonitorTorsoSettings torsoSettings;
        private MonitorTorsoValues torsoDraft;
        private string torsoRotation="0",torsoMessage="";
        private readonly MonitorHeadSettings headSettings;
        private MonitorHeadValues headDraft;
        private string headRotation="0",headMessage="";
        private readonly MonitorArmSettings armSettings;
        private MonitorArmValues armDraft;
        private string armLX="0",armLY="0",armRX="0",armRY="0",armMessage="";
        private int editMode;
        private string playbackMessage = "";
        private readonly MonitorFramingSettings settings;
        private readonly MonitorFramingModeSettings framingModes;
        private object framingDraftOwner;
        private Vector2 framingScroll;
        private bool IsFramingTab {get {return !diagnosticTab&&!playbackTab&&!armTab&&!headTab&&!torsoTab;}}
        private string FramingError {get {return poseEditScope<0?settings.Error:framingModes.Error;}}
        private readonly MonitorFramingInputShield inputShield;
        private MonitorDisplayRouter observed;
        private MonitorFramingValue draft;
        private int? framingNpcId;
        private bool open, preview, dragging;
        private Rect window=new Rect(30,60,450,640);
        private string x="0", y="0", zoom="1", message="";
        private string savedXText,savedYText,savedZoomText;
        private readonly int windowId=unchecked((int)0x57484644);
        internal MonitorFramingDebugUI(ConfigFile config, Func<MonitorDisplayRouter> current,
            Func<MonitorStateDiagnostics> diagnostics, ConfigEntry<string>[] playbackPoses,
            ConfigEntry<string>[] playbackFaces, Action refreshPlayback, Func<string> targetLabel=null, Action<string> warning=null)
        {
            this.current=current;this.diagnostics=diagnostics;this.targetLabel=targetLabel;
            this.config=config;this.playbackPoses=playbackPoses;this.playbackFaces=playbackFaces;
            this.refreshPlayback=refreshPlayback;
            ResetPlaybackDraft();
            poseSettings=new MonitorPoseSettings(Path.Combine(Paths.ConfigPath,"WallHoleFaceDisplay.pose-15.json"));
            armSettings=new MonitorArmSettings(Path.Combine(Paths.ConfigPath,"WallHoleFaceDisplay.arms-15.json"));
            ResetArmDraft();
            headSettings=new MonitorHeadSettings(Path.Combine(Paths.ConfigPath,"WallHoleFaceDisplay.head-15.json"));
            ResetHeadDraft();
            torsoSettings=new MonitorTorsoSettings(Path.Combine(Paths.ConfigPath,"WallHoleFaceDisplay.torso-15.json"));
            ResetTorsoDraft();
            ResetPoseDrafts();
            settings=new MonitorFramingSettings(Path.Combine(Paths.ConfigPath,"WallHoleFaceDisplay.framing.json"));
            framingModes=new MonitorFramingModeSettings(Path.Combine(Paths.ConfigPath,"WallHoleFaceDisplay.framing-modes-15.json"));
            if(warning!=null){
                string[] names={"framing.json","framing-modes-15.json","arms-15.json","head-15.json","torso-15.json","pose-15.json"};
                string[] errors={settings.Error,framingModes.Error,armSettings.Error,headSettings.Error,torsoSettings.Error,poseSettings.Error};
                for(int i=0;i<errors.Length;i++)if(errors[i]!=null)warning("[MonitorSettings] WallHoleFaceDisplay."+names[i]+": "+errors[i]);
                foreach(int id in new[]{16,17,44,90}){var profile=npcProfiles.Get(id);if(profile.Error!=null)warning("[MonitorSettings] npcId="+id+"; profile: "+profile.Error);if(profile.FacilityAnimations.Error!=null)warning("[MonitorSettings] npcId="+id+"; facility animations: "+profile.FacilityAnimations.Error);}
            }
            ResetDraft();
            if(!MonitorDebugSettings.Enabled)return;
            GameObject host=null;
            try {
            key=config.Bind("MonitorDebug", "ToggleFramingUI", new KeyboardShortcut(KeyCode.F6,KeyCode.LeftControl,KeyCode.LeftAlt), "Toggle camera framing editor; no playback controls.");
            host=new GameObject("WHFD.FramingInputShield",typeof(RectTransform));
            inputShield=host.AddComponent<MonitorFramingInputShield>();
            inputShield.Initialize(()=>open,()=>window);
                uiReady=true;
            }catch(Exception error){if(host!=null)UnityEngine.Object.Destroy(host);inputShield=null;warning?.Invoke("[MonitorDebug] optional UI unavailable; normal settings/playback retained: "+error.GetType().Name);}

        }
        internal MonitorFramingValue FramingFor(MonitorDisplayRouter requester,int id)
        {
            var active=current();var common=settings.Get(id);
            bool editing=open && IsFramingTab && preview && active!=null && active.NumericNpcId==id && active.VideoReady;
            return MonitorFramingModeSelection.Resolve(framingModes,common,requester,active,framingDraftOwner,
                requester!=null && requester.NumericNpcId==id?requester.PoseMode:-1,poseEditScope,editing,draft);
        }
        internal MonitorArmValues ArmsFor(MonitorDisplayRouter requester)
        {return PoseFor(requester).Arms;}
        internal MonitorFramingValue Framing(int id)
        {
            return FramingFor(current(),id);
        }
        internal void Tick()
        {
            if(!MonitorDebugSettings.Enabled||!uiReady)return;
            if (key.Value.IsDown()) { if(open) Close(); else {ResetDraft();ResetPlaybackDraft();ResetPoseDrafts();open=true;} }
            var active=current();
            if(!ReferenceEquals(active,observed)){templatePreview=false;templateDraft=null;templateDraftOwner=null;}
            if (!ReferenceEquals(active,observed) || active==null || active.NumericNpcId!=15 || !active.VideoReady) {
                preview=false;dragging=false;
            }
            if(framingNpcId!=(active==null?(int?)null:active.NumericNpcId)){preview=false;dragging=false;ResetDraft();}
            if(!ReferenceEquals(active,framingDraftOwner)){preview=false;dragging=false;ResetDraft();}
            if(!ReferenceEquals(active,poseDraftOwner))ResetPoseDrafts();
            if(observed!=null&&!ReferenceEquals(active,observed))observed.SetPreviewRendering(false);
            if(active!=null)active.SetPreviewRendering(open&&(active.IsTemplate?templatePreview:(IsFramingTab&&preview)||((armTab||headTab||torsoTab)&&posePreview)||(playbackTab&&legacyPlaybackPreview)));
            observed=active;
            if(legacyPlaybackPreview){
                if(!OwnsPlaybackPreview(active))StopPlaybackPreview();
                else{string signature=editMode+"|"+draftPoses[editMode]+"|"+draftFaces[editMode];if(signature!=playbackPreviewSignature){playbackPreviewSignature=signature;active.RefreshPlaybackSettings();}}
            }
            inputShield.Refresh();
        }
        private void ResetDraft()
        {
            var active=current();framingDraftOwner=active;framingNpcId=active==null?(int?)null:active.NumericNpcId;
            draft=framingNpcId.HasValue?framingModes.Resolve(poseEditScope,settings.Get(framingNpcId.Value)):new MonitorFramingValue {NpcId=-1};
            SyncText();savedXText=x;savedYText=y;savedZoomText=zoom;message=FramingError ?? (framingNpcId.HasValue?"":"NPC ID Unknown; saving disabled.");
        }
        private void SyncText() { x=Text(draft.X); y=Text(draft.Y); zoom=Text(draft.Zoom); }
        private static string Text(float value) { return value.ToString("0.####",CultureInfo.InvariantCulture); }
        private bool ParseDraft()
        {
            var active=current();
            if(!framingNpcId.HasValue || active==null || active.NumericNpcId!=framingNpcId)return false;
            float a,b,c;
            if(!float.TryParse(x,NumberStyles.Float,CultureInfo.InvariantCulture,out a) || !float.TryParse(y,NumberStyles.Float,CultureInfo.InvariantCulture,out b) || !float.TryParse(zoom,NumberStyles.Float,CultureInfo.InvariantCulture,out c)) return false;
            var next=new MonitorFramingValue { NpcId=framingNpcId.Value,X=a,Y=b,Zoom=c };
            if(!next.Valid) return false;
            draft=next;return true;
        }
        private void Close() {
            using(MonitorPerformance.Measure(MonitorPerfStage.UiReset)) { /*WHFDPerfScope*/if(current()!=null)current().SetPreviewRendering(false);StopPlaybackPreview();posePreview=false;templateDragging=false;templatePreview=false;templateDraft=null;templateDraftOwner=null; var diag=diagnostics();if(diag!=null) diag.Stop("ui-closed");preview=false;dragging=false;open=false;if(inputShield!=null) inputShield.Hide();ResetDraft();ResetPlaybackDraft();ResetPoseDrafts(); 
            } /*WHFDPerfEnd*/}
        internal void Draw()
        {
            if(!MonitorDebugSettings.Enabled || !uiReady || !open) return;
            window.x=Mathf.Clamp(window.x,0,Mathf.Max(0,Screen.width-100));
            window.y=Mathf.Clamp(window.y,0,Mathf.Max(0,Screen.height-40));
            window=GUI.Window(windowId,window,Content,targetLabel==null?"WHFD monitor / NPC ID 15":targetLabel());
            inputShield.Refresh();
        }
        private void Content(int id)
        {
            DrawNpcInput();
            if(current()!=null && current().IsTemplate){DrawTemplate();GUI.DragWindow(new Rect(0,0,450,20));return;}
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("画角")) {diagnosticTab=false;playbackTab=false;armTab=false;headTab=false;torsoTab=false;var diag=diagnostics();if(diag!=null)diag.Stop("tab-changed");}
            if(GUILayout.Button("状態診断")) {diagnosticTab=true;playbackTab=false;armTab=false;headTab=false;torsoTab=false;var diag=diagnostics();if(diag!=null)diag.Refresh();}
            if(GUILayout.Button("再生設定")) {diagnosticTab=false;playbackTab=true;armTab=false;headTab=false;torsoTab=false;var diag=diagnostics();if(diag!=null)diag.Stop("tab-changed");}
            if(GUILayout.Button("\u8155\u8abf\u6574")) {diagnosticTab=false;playbackTab=false;armTab=true;headTab=false;torsoTab=false;var diag=diagnostics();if(diag!=null)diag.Stop("tab-changed");}
            GUILayout.EndHorizontal();
            // A second row keeps existing tabs and window width intact.
            if(GUILayout.Button("頭の調整")) {diagnosticTab=false;playbackTab=false;armTab=false;headTab=true;torsoTab=false;var diag=diagnostics();if(diag!=null)diag.Stop("tab-changed");}
            if(GUILayout.Button("胴体の調整")) {diagnosticTab=false;playbackTab=false;armTab=false;headTab=false;torsoTab=true;var diag=diagnostics();if(diag!=null)diag.Stop("tab-changed");}
            int legacyTab=diagnosticTab?5:playbackTab?4:armTab?1:headTab?2:torsoTab?3:0;
            if(legacyTab!=observedLegacyTab){posePreview=false;preview=false;dragging=false;legacyScroll=Vector2.zero;ResetPoseDrafts();ResetDraft();observedLegacyTab=legacyTab;}
            if(!diagnosticTab&&!playbackTab)DrawPoseScope(IsFramingTab);
            if(armTab||headTab||torsoTab){
                legacyScroll=GUILayout.BeginScrollView(legacyScroll,GUILayout.Height(380));
                GUILayout.BeginHorizontal();if(GUILayout.Button("プレビュー開始"))posePreview=true;if(GUILayout.Button("プレビュー終了"))posePreview=false;GUILayout.EndHorizontal();
                GUILayout.Label(posePreview?"プレビュー中：選択モニターの編集中項目のみ":"プレビュー停止中：保存値を表示");
                var selected=current();if(posePreview&&selected!=null&&selected.VideoTexture!=null){var poseArea=GUILayoutUtility.GetRect(210,210,GUILayout.ExpandWidth(false));GUI.DrawTexture(poseArea,selected.VideoTexture,ScaleMode.ScaleToFit,false);}
            }
            if(torsoTab) {DrawTorso();GUILayout.EndScrollView();GUI.DragWindow(new Rect(0,0,450,20));return;}
            if(headTab) {DrawHead();GUILayout.EndScrollView();GUI.DragWindow(new Rect(0,0,450,20));return;}
            if(armTab) {DrawArms();GUILayout.EndScrollView();GUI.DragWindow(new Rect(0,0,450,20));return;}
            if(diagnosticTab) {DrawDiagnostics();GUI.DragWindow(new Rect(0,0,450,20));return;}
            if(playbackTab) {legacyScroll=GUILayout.BeginScrollView(legacyScroll,GUILayout.Height(380));DrawPlaybackSettings();GUILayout.EndScrollView();GUI.DragWindow(new Rect(0,0,450,20));return;}
            framingScroll=GUILayout.BeginScrollView(framingScroll,GUILayout.Height(380));
            var active=current();
            bool available=active!=null && active.NumericNpcId==framingNpcId && framingNpcId==15 && active.VideoReady;
            GUILayout.Label("画角調整：ゲームはそのまま進行します");
            GUILayout.Label("NPC ID: "+(framingNpcId.HasValue?framingNpcId.Value.ToString():"Unknown")+(available?" / video available":" / compatible video required"));
            GUILayout.BeginHorizontal();
            GUI.enabled=available;
            if(GUILayout.Button("プレビュー開始")) { if(ParseDraft()) { preview=true;if(observed!=null&&!ReferenceEquals(active,observed))observed.SetPreviewRendering(false);
            if(active!=null)active.SetPreviewRendering(open&&(templatePreview||preview||posePreview||legacyPlaybackPreview));
            observed=active;framingDraftOwner=active;message=""; } else message="有効な画角値を入力してください。"; }
            GUI.enabled=true;
            if(GUILayout.Button("プレビュー終了")) {preview=false;dragging=false;}
            GUILayout.EndHorizontal();
            GUILayout.Label(preview ? "プレビュー中：選択モニター・編集対象のみ" : "プレビュー停止中：再生モードの保存値を表示");
            var area=GUILayoutUtility.GetRect(230,230,GUILayout.ExpandWidth(false));
            GUI.Box(area,GUIContent.none);
            if(preview && active!=null && active.VideoTexture!=null) GUI.DrawTexture(area,active.VideoTexture,ScaleMode.ScaleToFit,false);
            var ev=Event.current;
            if(preview && ev.type==EventType.MouseDown && ev.button==0 && area.Contains(ev.mousePosition)) {dragging=true;ev.Use();}
            if(ev.type==EventType.MouseUp && dragging) {dragging=false;ev.Use();}
            if(preview && dragging && ev.type==EventType.MouseDrag) {
                draft.X=Mathf.Clamp(draft.X-ev.delta.x/area.width/draft.Zoom,-2,2);
                draft.Y=Mathf.Clamp(draft.Y+ev.delta.y/area.height/draft.Zoom,-2,2);SyncText();ev.Use();
            }
            if(preview && area.Contains(ev.mousePosition) && ev.type==EventType.ScrollWheel) {
                draft.Zoom=Mathf.Clamp(draft.Zoom*Mathf.Exp(-ev.delta.y*.1f),.25f,4);SyncText();ev.Use();
            }
            GUILayout.Label("画像をドラッグして移動 / ホイールで倍率変更");
            GUI.enabled=available;
            draft.X=SliderField("X",ref x,draft.X,-2f,2f);
            draft.Y=SliderField("Y",ref y,draft.Y,-2f,2f);
            draft.Zoom=SliderField("倍率",ref zoom,draft.Zoom,.25f,4f);
            GUI.enabled=true;
            bool valid=ParseDraft();
            GUILayout.Label("X/Y：画面幅の -2～2 / 倍率：0.25～4（小数点は .）");
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("保存値へ戻す")) {ResetDraft();dragging=false;}
            GUI.enabled=available && valid && FramingError==null;
            if(GUILayout.Button("保存")) {
                try {if(poseEditScope<0)settings.Save(draft);else framingModes.SaveEdited(poseEditScope,draft,x!=savedXText||y!=savedYText,zoom!=savedZoomText);ResetDraft();message="保存しました。";} catch(Exception error) {message="保存失敗："+error.GetType().Name;}
            }
            GUI.enabled=true;
            if(GUILayout.Button("閉じる（未保存変更を破棄）")) Close();
            GUILayout.EndHorizontal();
            if(poseEditScope>=0){GUI.enabled=available && framingModes.Error==null;
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("Xを共通に戻す（保存）"))ClearFramingOverride(true,false);
                if(GUILayout.Button("Yを共通に戻す（保存）"))ClearFramingOverride(false,true);
                if(GUILayout.Button("X/Yを共通に戻す（保存）"))ClearFramingOverride(true,true);
                GUILayout.EndHorizontal();
                if(GUILayout.Button("倍率を共通に戻す（保存）")){try{framingModes.ClearZoom(poseEditScope);ResetDraft();message="Common Zoom fallback saved for this mode.";}catch(Exception error){message="保存失敗："+error.GetType().Name;}}
                GUI.enabled=true;}
            if(!valid) GUILayout.Label("入力値が不正です。直前の有効な値を表示します。");
            if(!string.IsNullOrEmpty(message)) GUILayout.Label(message);
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0,0,450,20));
        }
        internal MonitorHeadValues HeadFor(MonitorDisplayRouter requester) {return PoseFor(requester).Head;}
        private void ResetHeadDraft(){headDraft=EditSavedPose().Head;SyncHeadText();headMessage=PoseError(headSettings==null?null:headSettings.Error)??"";}
        private void SyncHeadText(){headRotation=Text(headDraft.Rotation);}
        private bool ParseHead()
        {
            float r;
            if(!float.TryParse(headRotation,NumberStyles.Float,CultureInfo.InvariantCulture,out r))return false;
            var next=new MonitorHeadValues {Rotation=r};if(!next.Valid)return false;headDraft=next;return true;
        }
        private void DrawHead()
        {
            var active=current();bool available=active!=null && active.NumericNpcId==15 && active.HeadAvailable;
            GUILayout.Label("頭の角度調整（モニター専用 head_pos）");
            GUILayout.Label(active==null?"施設にNPC ID 15をセットしてください。":active.HeadStatus);
            GUILayout.Label("角度：-180～180度 / 0＝補正なし。プレビュー開始で反映します。中央合わせ基準は固定です。");
            GUI.enabled=available;
            headDraft.Rotation=SliderField("角度",ref headRotation,headDraft.Rotation,-180,180);
            GUI.enabled=true;bool valid=ParseHead();
            GUILayout.BeginHorizontal();GUI.enabled=available && valid && PoseError(headSettings.Error)==null;
            if(GUILayout.Button("頭を保存")){try{if(poseEditScope<0)headSettings.Save(headDraft);else poseSettings.SaveHead(poseEditScope,headDraft);headMessage="保存しました。";}catch(Exception error){headMessage="保存失敗："+error.GetType().Name;}}
            GUI.enabled=true;
            if(GUILayout.Button("保存値へ戻す")){posePreview=false;ResetHeadDraft();}
            if(GUILayout.Button("0に初期化")){headDraft=new MonitorHeadValues();SyncHeadText();headMessage="0に初期化しました。保存するまで未保存です。";}
            GUILayout.EndHorizontal();
            if(!valid)GUILayout.Label("入力値が不正です。直前の有効な値を表示します。");
            if(poseEditScope>=0){GUI.enabled=available && poseSettings.Error==null;
                if(GUILayout.Button("頭を共通設定に戻す（保存）")){try{poseSettings.ClearHead(poseEditScope);ResetHeadDraft();headMessage="Common fallback saved for this group/mode.";}catch(Exception error){headMessage="保存失敗："+error.GetType().Name;}}GUI.enabled=true;}
            if(!string.IsNullOrEmpty(headMessage))GUILayout.Label(headMessage);
            if(GUILayout.Button("閉じる（未保存変更を破棄）"))Close();
        }
        internal MonitorTorsoValues TorsoFor(MonitorDisplayRouter requester) {return PoseFor(requester).Torso;}
        private void ResetTorsoDraft(){torsoDraft=EditSavedPose().Torso;SyncTorsoText();torsoMessage=PoseError(torsoSettings==null?null:torsoSettings.Error)??"";}
        private void SyncTorsoText(){torsoRotation=Text(torsoDraft.Rotation);}
        private bool ParseTorso()
        {
            float r;
            if(!float.TryParse(torsoRotation,NumberStyles.Float,CultureInfo.InvariantCulture,out r))return false;
            var next=new MonitorTorsoValues {Rotation=r};if(!next.Valid)return false;torsoDraft=next;return true;
        }
        private void DrawTorso()
        {
            var active=current();bool available=active!=null && active.NumericNpcId==15 && active.TorsoAvailable;
            GUILayout.Label("胴体の角度調整（モニター専用 spine_02_pos）");
            GUILayout.Label(active==null?"施設にNPC ID 15をセットしてください。":active.TorsoStatus);
            GUILayout.Label("角度：-180～180度 / 0＝補正なし。プレビュー開始で反映します。中央合わせ基準は固定です。");
            GUI.enabled=available;
            torsoDraft.Rotation=SliderField("角度",ref torsoRotation,torsoDraft.Rotation,-180,180);
            GUI.enabled=true;bool valid=ParseTorso();
            GUILayout.BeginHorizontal();GUI.enabled=available && valid && PoseError(torsoSettings.Error)==null;
            if(GUILayout.Button("胴体を保存")){try{if(poseEditScope<0)torsoSettings.Save(torsoDraft);else poseSettings.SaveTorso(poseEditScope,torsoDraft);torsoMessage="保存しました。";}catch(Exception error){torsoMessage="保存失敗："+error.GetType().Name;}}
            GUI.enabled=true;
            if(GUILayout.Button("保存値へ戻す")){posePreview=false;ResetTorsoDraft();}
            if(GUILayout.Button("0に初期化")){torsoDraft=new MonitorTorsoValues();SyncTorsoText();torsoMessage="0に初期化しました。保存するまで未保存です。";}
            GUILayout.EndHorizontal();
            if(!valid)GUILayout.Label("入力値が不正です。直前の有効な値を表示します。");
            if(poseEditScope>=0){GUI.enabled=available && poseSettings.Error==null;
                if(GUILayout.Button("胴体を共通設定に戻す（保存）")){try{poseSettings.ClearTorso(poseEditScope);ResetTorsoDraft();torsoMessage="Common fallback saved for this group/mode.";}catch(Exception error){torsoMessage="保存失敗："+error.GetType().Name;}}GUI.enabled=true;}
            if(!string.IsNullOrEmpty(torsoMessage))GUILayout.Label(torsoMessage);
            if(GUILayout.Button("閉じる（未保存変更を破棄）"))Close();
        }
        internal MonitorArmValues Arms() {return PoseFor(current()).Arms;}
        private void ResetArmDraft(){armDraft=EditSavedPose().Arms;SyncArmText();armMessage=PoseError(armSettings==null?null:armSettings.Error)??"";}
        private void SyncArmText() {armLX=Text(armDraft.LeftX);armLY=Text(armDraft.LeftY);armRX=Text(armDraft.RightX);armRY=Text(armDraft.RightY);}
        private bool ParseArms()
        {
            float lx,ly,rx,ry;
            if(!float.TryParse(armLX,NumberStyles.Float,CultureInfo.InvariantCulture,out lx)||!float.TryParse(armLY,NumberStyles.Float,CultureInfo.InvariantCulture,out ly)||
                !float.TryParse(armRX,NumberStyles.Float,CultureInfo.InvariantCulture,out rx)||!float.TryParse(armRY,NumberStyles.Float,CultureInfo.InvariantCulture,out ry))return false;
            var next=new MonitorArmValues {LeftX=lx,LeftY=ly,RightX=rx,RightY=ry};if(!next.Valid)return false;armDraft=next;return true;
        }
        private static float ArmSlider(string label,ref string text,float value)
        {
            float next=SliderField(label,ref text,value,-1f,1f);
            if(next!=value){next=(float)Math.Round(next*100f)/100f;text=Text(next);}return next;
        }
        private void DrawArms()
        {
            var active=current();bool available=active!=null && active.NumericNpcId==15 && active.ArmAvailable;
            GUILayout.Label("左右腕の位置調整（モニター専用IK）");
            GUILayout.Label(active==null?"施設にNPC ID 15をセットしてください。":active.ArmStatus);
            GUILayout.Label("X/Y：各腕の長さに対する -1～1 / 刻み0.01。\nプレビュー開始で反映します。0＝補正なし。保存するまで未保存です。");
            GUI.enabled=available;
            armDraft.LeftX=ArmSlider("左 X",ref armLX,armDraft.LeftX);
            armDraft.LeftY=ArmSlider("左 Y",ref armLY,armDraft.LeftY);
            armDraft.RightX=ArmSlider("右 X",ref armRX,armDraft.RightX);
            armDraft.RightY=ArmSlider("右 Y",ref armRY,armDraft.RightY);
            GUI.enabled=true;
            bool valid=ParseArms();
            GUILayout.BeginHorizontal();
            GUI.enabled=available && valid && PoseError(armSettings.Error)==null;
            if(GUILayout.Button("腕を保存")){try{if(poseEditScope<0)armSettings.Save(armDraft);else poseSettings.SaveArms(poseEditScope,armDraft);armMessage="保存しました。";}catch(Exception error){armMessage="保存失敗："+error.GetType().Name;}}
            GUI.enabled=true;
            if(GUILayout.Button("保存値へ戻す")){posePreview=false;ResetArmDraft();}
            if(GUILayout.Button("0に初期化")){armDraft=new MonitorArmValues();SyncArmText();armMessage="0に初期化しました。保存するまで未保存です。";}
            GUILayout.EndHorizontal();
            if(!valid)GUILayout.Label("Invalid input: finite values -1..1 required.");
            if(poseEditScope>=0){GUI.enabled=available && poseSettings.Error==null;
                if(GUILayout.Button("腕を共通設定に戻す（保存）")){try{poseSettings.ClearArms(poseEditScope);ResetArmDraft();armMessage="Common fallback saved for this group/mode.";}catch(Exception error){armMessage="保存失敗："+error.GetType().Name;}}GUI.enabled=true;}
            if(!string.IsNullOrEmpty(armMessage))GUILayout.Label(armMessage);
            if(GUILayout.Button("閉じる（未保存変更を破棄）"))Close();
        }
        private MonitorPoseValues CommonPose()
        {
            return new MonitorPoseValues {Arms=armSettings==null?new MonitorArmValues():armSettings.Get(),
                Head=headSettings==null?new MonitorHeadValues():headSettings.Get(),Torso=torsoSettings==null?new MonitorTorsoValues():torsoSettings.Get()};
        }
        private MonitorPoseValues EditSavedPose() {return poseSettings.Resolve(poseEditScope,CommonPose());}
        private string PoseError(string commonError) {return poseEditScope<0?commonError:poseSettings.Error;}
        private void ResetPoseDrafts()
        {poseDraftOwner=current();ResetArmDraft();ResetHeadDraft();ResetTorsoDraft();}
        private MonitorPoseValues PoseFor(MonitorDisplayRouter requester)
        {
            var active=current();bool editing=open && posePreview && (armTab||headTab||torsoTab) && active!=null && active.NumericNpcId==15 && active.VideoReady;
            return MonitorPoseSelection.Resolve(poseSettings,CommonPose(),requester,active,poseDraftOwner,
                requester==null?-1:requester.PoseMode,poseEditScope,editing,
                editing && armTab && active.ArmAvailable?armDraft:null,
                editing && headTab && active.HeadAvailable?headDraft:null,
                editing && torsoTab && active.TorsoAvailable?torsoDraft:null);
        }
        private void ClearFramingOverride(bool x,bool y)
        {
            try{framingModes.Clear(poseEditScope,x,y);ResetDraft();message="Common fallback saved for selected axes/mode.";}
            catch(Exception error){message="保存失敗："+error.GetType().Name;}
        }
        private void DrawNpcInput()
        {
            var active=current();GUILayout.BeginHorizontal();GUILayout.Label("NPC ID",GUILayout.Width(55));npcInput=GUILayout.TextField(npcInput,10,GUILayout.Width(75));
            int id;bool valid=MonitorNpcInput.TryParse(npcInput,out id);GUI.enabled=active!=null && valid;
            if(GUILayout.Button("読込 / 適用")){
                try{var profile=id==15?null:npcProfiles.Get(id);
                    if(active.TryLoadNpc(id,profile)){templatePreview=false;templateDraft=null;templateDraftOwner=null;preview=false;dragging=false;ResetDraft();ResetPoseDrafts();}
                    npcLoadMessage=active.LoadStatus;
                }catch(Exception error){npcLoadMessage="Load failed; current display kept: "+error.Message;}
            }
            GUI.enabled=active!=null;
            if(GUILayout.Button("施設のNPCを自動表示")){active.FollowOccupant();templatePreview=false;templateDraft=null;templateDraftOwner=null;preview=false;dragging=false;ResetDraft();ResetPoseDrafts();npcLoadMessage=active.LoadStatus;}
            GUI.enabled=true;GUILayout.EndHorizontal();
            if(active!=null)GUILayout.Label(active.FollowsOccupant?"対象NPC：施設から自動取得":"対象NPC：手動指定（施設へ戻す場合は上の自動表示ボタン）");
            GUILayout.Label("表示対象 NPC ID："+(active!=null&&active.NumericNpcId.HasValue?active.NumericNpcId.Value.ToString():"none")+(active!=null&&active.VideoReady?" / 表示可能":" / 未表示"));
            GUI.enabled=active!=null&&valid&&id!=15;
            if(GUILayout.Button("通常モデルを読込（施設再生なし）")){
                if(active.TryLoadNpc(id,npcProfiles.Get(id),false)){templatePreview=false;templateDraft=null;templateDraftOwner=null;preview=false;dragging=false;ResetDraft();ResetPoseDrafts();}npcLoadMessage=active.LoadStatus;
            }
            GUI.enabled=true;
            if(!valid)GUILayout.Label("NPC IDは0以上の半角数字で入力してください。");
            if(!string.IsNullOrEmpty(npcLoadMessage))GUILayout.Label(npcLoadMessage);
            if(active!=null && active.IsTemplate)active.TemplateValuesProvider=()=>TemplateValuesFor(active);
        }
        internal void BindRouter(MonitorDisplayRouter router){router.ProfileProvider=npcProfiles.Get;router.TemplateValuesProvider=()=>TemplateValuesFor(router);}
        private string TemplateGroup {get {return new[]{"frame","arms","head","torso","animation"}[templateTab];}}
        private void ResetTemplateDraft(MonitorDisplayRouter active)
        {
            templateDraftOwner=active;templateDraftDisplay=active.Template;templateDraftId=active.Template.NpcId;templateDraft=active.Template.GetValues(templateScope);
            SyncTemplateText();
        }
        private void SyncTemplateText()
        {
            templateLX=Text(templateDraft.LeftX??0);templateLY=Text(templateDraft.LeftY??0);templateRX=Text(templateDraft.RightX??0);templateRY=Text(templateDraft.RightY??0);
            templateHead=Text(templateDraft.HeadRotation??0);templateTorso=Text(templateDraft.TorsoRotation??0);templateX=Text(templateDraft.X??0);templateY=Text(templateDraft.Y??0);templateZoom=Text(templateDraft.Zoom??1);
        }
        private MonitorNpcValues TemplateValuesFor(MonitorDisplayRouter requester)
        {
            requester.Template.AuditEditScope=templateScope;
            requester.Template.AuditPreview=open&&templatePreview&&ReferenceEquals(requester.Template,templateDraftDisplay)&&ReferenceEquals(requester,templateDraftOwner)&&ReferenceEquals(requester,current());
            return MonitorNpcPreview.Resolve(requester.Template.Settings,requester,current(),templateDraftOwner,templateDraftId,
                requester.Template.Mode,templateScope,open&&templatePreview&&ReferenceEquals(requester.Template,templateDraftDisplay),TemplateGroup,templateDraft,requester.Template.GetValues);
        }
        static string RigMessage(string feature,bool available,string reason)
        {if(available)return feature+"調整：対応";if(reason!=null&&reason.Contains("Drool"))return feature+"調整：非対応（Droolが頭部に追従しないため分離防止）";return feature+"調整：非対応（構造・制約・ウェイトを安全に確認できません）\n"+reason;}
        private static bool TemplateNumber(string text,float min,float max,out float value)
        {return float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&!float.IsNaN(value)&&!float.IsInfinity(value)&&value>=min&&value<=max;}
        private void DrawTemplate()
        {
            var active=current();var display=active.Template;
            if(templateDraft==null||!ReferenceEquals(templateDraftOwner,active)||templateDraftId!=display.NpcId||!ReferenceEquals(templateDraftDisplay,display)){templatePreview=false;ResetTemplateDraft(active);}
            GUILayout.Label("再生中：モード"+display.Mode+" / 編集対象："+(templateScope<0?"共通設定":"モード"+templateScope));
            GUILayout.Label("編集対象（保存先）");
            int scope=GUILayout.SelectionGrid(templateScope+1,new[]{"共通","モード0","モード1","モード2","モード3"},5)-1;
            if(scope!=templateScope){templateScope=scope;templateDragging=false;ResetTemplateDraft(active);}
            int tab=GUILayout.Toolbar(templateTab,new[]{"画角","腕","頭","胴体","再生"});if(tab!=templateTab){templateTab=tab;templateDragging=false;ResetTemplateDraft(active);}
            templateScroll=GUILayout.BeginScrollView(templateScroll,GUILayout.Height(400));
            GUILayout.Label(display.IsFacility?"施設モデルを表示中":"通常モデルを表示中（施設再生なし）");
            templateDetails=GUILayout.Toggle(templateDetails,"詳細診断を表示");if(templateDetails)GUILayout.Label(display.Status);
            GUILayout.Label("設定 NPC ID "+display.NpcId+" / 利用可能Animation数："+display.Playback.Animations.Length);
            if(display.Settings.Error!=null)GUILayout.Label(display.Settings.Error);
            if(display.IsFacility&&display.GroupError("animation")!=null)GUILayout.Label(display.GroupError("animation"));
            bool follow=GUILayout.Toggle(display.FollowFacility,"施設状態に追従（不明時は現在モードを維持）");display.FollowFacility=follow;
            GUILayout.Label("表示する再生モード（手動選択で施設追従を解除）");
            int play=GUILayout.SelectionGrid(display.Mode,new[]{"モード0","モード1","モード2","モード3"},4);
            if(play!=display.Mode){try{display.SetMode(play);display.FollowFacility=false;}catch(Exception e){npcLoadMessage=e.Message;}}
            GUILayout.BeginHorizontal();if(GUILayout.Button("プレビュー開始"))templatePreview=true;if(GUILayout.Button("プレビュー終了")){templatePreview=false;templateDragging=false;}GUILayout.EndHorizontal();
            GUILayout.Label(templatePreview?"プレビュー中：選択モニター・編集対象のみ":"プレビュー停止中：実際の再生モードの保存値を表示");
            bool supported=templateTab==0||(templateTab==4&&display.Playback.Animations.Length>0)||(templateTab==1&&display.Playback.ArmsAvailable)||(templateTab==2&&display.Playback.HeadAvailable)||(templateTab==3&&display.Playback.TorsoAvailable);
            if(templateTab==4&&display.Playback.Animations.Length==0)GUILayout.Label("Animationなし：静止表示のみ");
            if(templateTab==1)GUILayout.Label(RigMessage("腕",display.Playback.ArmsAvailable,display.Playback.ArmReason));
            if(templateTab==2)GUILayout.Label(RigMessage("頭",display.Playback.HeadAvailable,display.Playback.HeadReason));
            if(templateTab==3)GUILayout.Label(RigMessage("胴体",display.Playback.TorsoAvailable,display.Playback.TorsoReason));
            GUI.enabled=supported;float a,b,c,d;bool valid=true;
            if(templatePreview){
                var area=GUILayoutUtility.GetRect(210,210,GUILayout.ExpandWidth(false));GUI.Box(area,GUIContent.none);
                if(display.Texture!=null)GUI.DrawTexture(area,display.Texture,ScaleMode.ScaleToFit,false);
                var ev=Event.current;
                if(templateTab==0){
                    if(ev.type==EventType.MouseDown&&ev.button==0&&area.Contains(ev.mousePosition)){templateDragging=true;ev.Use();}
                    if(ev.type==EventType.MouseUp&&templateDragging){templateDragging=false;ev.Use();}
                    if(templateDragging&&ev.type==EventType.MouseDrag){templateDraft.X=Mathf.Clamp((templateDraft.X??0)-ev.delta.x/area.width/(templateDraft.Zoom??1),-2,2);templateDraft.Y=Mathf.Clamp((templateDraft.Y??0)+ev.delta.y/area.height/(templateDraft.Zoom??1),-2,2);SyncTemplateText();ev.Use();}
                    if(area.Contains(ev.mousePosition)&&ev.type==EventType.ScrollWheel){templateDraft.Zoom=Mathf.Clamp((templateDraft.Zoom??1)*Mathf.Exp(-ev.delta.y*.1f),.25f,4);SyncTemplateText();ev.Use();}
                    GUILayout.Label("画像をドラッグして移動 / ホイールで倍率変更");
                }
            }
            if(templateTab==0){
                templateDraft.X=SliderField("X",ref templateX,templateDraft.X??0,-2,2);templateDraft.Y=SliderField("Y",ref templateY,templateDraft.Y??0,-2,2);templateDraft.Zoom=SliderField("倍率",ref templateZoom,templateDraft.Zoom??1,.25f,4);
                valid=TemplateNumber(templateX,-2,2,out a)&&TemplateNumber(templateY,-2,2,out b)&&TemplateNumber(templateZoom,.25f,4,out c);
                if(valid){TemplateNumber(templateX,-2,2,out a);TemplateNumber(templateY,-2,2,out b);TemplateNumber(templateZoom,.25f,4,out c);templateDraft.X=a;templateDraft.Y=b;templateDraft.Zoom=c;}
            }else if(templateTab==1){
                templateDraft.LeftX=ArmSlider("左 X",ref templateLX,templateDraft.LeftX??0);templateDraft.LeftY=ArmSlider("左 Y",ref templateLY,templateDraft.LeftY??0);templateDraft.RightX=ArmSlider("右 X",ref templateRX,templateDraft.RightX??0);templateDraft.RightY=ArmSlider("右 Y",ref templateRY,templateDraft.RightY??0);
                valid=TemplateNumber(templateLX,-1,1,out a)&&TemplateNumber(templateLY,-1,1,out b)&&TemplateNumber(templateRX,-1,1,out c)&&TemplateNumber(templateRY,-1,1,out d);
                if(valid){TemplateNumber(templateLX,-1,1,out a);TemplateNumber(templateLY,-1,1,out b);TemplateNumber(templateRX,-1,1,out c);TemplateNumber(templateRY,-1,1,out d);templateDraft.LeftX=a;templateDraft.LeftY=b;templateDraft.RightX=c;templateDraft.RightY=d;}
            }else if(templateTab==2){templateDraft.HeadRotation=SliderField("角度",ref templateHead,templateDraft.HeadRotation??0,-180,180);valid=TemplateNumber(templateHead,-180,180,out a);if(valid)templateDraft.HeadRotation=a;}
            else if(templateTab==3){templateDraft.TorsoRotation=SliderField("角度",ref templateTorso,templateDraft.TorsoRotation??0,-180,180);valid=TemplateNumber(templateTorso,-180,180,out a);if(valid)templateDraft.TorsoRotation=a;}
            else{
                var options=new string[display.Playback.Animations.Length+1];options[0]="";Array.Copy(display.Playback.Animations,0,options,1,display.Playback.Animations.Length);
                templateDraft.Body=TemplateChoice("身体アニメーション（track 0）",options,templateDraft.Body);
                templateDraft.Face=TemplateChoice("表情アニメーション（track 2・任意）",options,templateDraft.Face);
                GUILayout.Label("空欄＝再生なし。このモデルで利用可能なAnimationだけを表示します。");
            }
            GUI.enabled=true;
            if(!valid)GUILayout.Label("入力値が不正です。保存できません。");
            GUILayout.BeginHorizontal();GUI.enabled=supported&&valid&&display.GroupError(TemplateGroup)==null;
            if(GUILayout.Button("保存")){try{display.SaveGroup(templateScope,TemplateGroup,templateDraft);ResetTemplateDraft(active);npcLoadMessage="Saved NPC ID "+display.NpcId+" / "+TemplateGroup;}catch(Exception e){npcLoadMessage=e.Message;}}
            GUI.enabled=true;if(GUILayout.Button("保存値へ戻す")){templatePreview=false;templateDragging=false;ResetTemplateDraft(active);}
            if(GUILayout.Button("初期化")){var defaults=MonitorNpcValues.Resolve(null,null);if(display.IsFacility&&TemplateGroup=="animation")defaults=MonitorFacilityAnimationSettings.Defaults(templateScope,defaults,display.NpcId);MonitorNpcValues.CopyGroup(templateDraft,defaults,TemplateGroup);
                SyncTemplateText();}
            GUILayout.EndHorizontal();
            if(templateScope>=0){GUI.enabled=display.GroupError(TemplateGroup)==null;if(GUILayout.Button("この項目を共通設定に戻す（保存）")){try{display.ClearGroup(templateScope,TemplateGroup);ResetTemplateDraft(active);}catch(Exception e){npcLoadMessage=e.Message;}}GUI.enabled=true;}
            GUILayout.EndScrollView();
            if(GUILayout.Button("閉じる（未保存変更を破棄）"))Close();
        }
        private string TemplateChoice(string label,string[] options,string value)
        {
            GUILayout.Label(label);GUILayout.BeginHorizontal();if(GUILayout.Button("<",GUILayout.Width(38)))value=Cycle(options,value,-1);
            GUILayout.Label(string.IsNullOrEmpty(value)?"（なし）":value,GUILayout.ExpandWidth(true));if(GUILayout.Button(">",GUILayout.Width(38)))value=Cycle(options,value,1);GUILayout.EndHorizontal();return value;
        }
        private void DrawPoseScope(bool framing=false)
        {
            var active=current();int mode=active==null?-1:active.PoseMode;
            GUILayout.Label("再生中："+(mode<0?"不明":"モード"+mode)+" / 編集対象："+(poseEditScope<0?"共通設定":"モード"+poseEditScope));
            int next=GUILayout.SelectionGrid(poseEditScope+1,new[]{"共通設定","モード0","モード1","モード2","モード3"},5)-1;
            if(next!=poseEditScope){bool continuePreview=framing&&preview;poseEditScope=next;ResetPoseDrafts();dragging=false;ResetDraft();preview=continuePreview;}
            if(!framing){GUILayout.Label("プレビューは選択中モニターのみ。編集対象やタブの切替で未保存値を破棄します。");
                if(poseSettings.Error!=null)GUILayout.Label(poseSettings.Error);}
            else if(framingModes.Error!=null)GUILayout.Label(framingModes.Error);
        }
        private void ResetPlaybackDraft()
        {
            for (int i = 0; i < 4; i++) {
                draftPoses[i] = playbackPoses[i].Value;
                draftFaces[i] = playbackFaces[i].Value;
            }
            playbackMessage = "";
        }
        private static string Cycle(string[] names, string currentName, int direction)
        {
            if (names == null || names.Length == 0) return currentName;
            int found = Array.IndexOf(names, currentName);
            if (found < 0) return names[0];
            return names[(found + direction + names.Length) % names.Length];
        }
        private static bool Contains(string[] names, string name)
        {
            return names != null && Array.IndexOf(names, name) >= 0;
        }
        private void DrawChoice(string title, string[] options, string[] values)
        {
            GUILayout.Label(title + "  (" + options.Length + " available)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(38))) values[editMode] = Cycle(options, values[editMode], -1);
            GUILayout.Label(values[editMode] ?? "（なし）", GUILayout.MinWidth(245), GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", GUILayout.Width(38))) values[editMode] = Cycle(options, values[editMode], 1);
            GUILayout.EndHorizontal();
        }
        private void SavePlaybackSettings()
        {
            string[] oldPoses = new string[4], oldFaces = new string[4];
            for (int i = 0; i < 4; i++) { oldPoses[i] = playbackPoses[i].Value; oldFaces[i] = playbackFaces[i].Value; }
            bool previousAutoSave = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try {
                for (int i = 0; i < 4; i++) { playbackPoses[i].Value = draftPoses[i]; playbackFaces[i].Value = draftFaces[i]; }
                config.Save();
                if (refreshPlayback != null) refreshPlayback();
                playbackMessage = "Saved: current monitor updates on its next stable state. Finish updates next time.";
            } catch (Exception error) {
                for (int i = 0; i < 4; i++) { playbackPoses[i].Value = oldPoses[i]; playbackFaces[i].Value = oldFaces[i]; }
                try { config.Save(); } catch { }
                playbackMessage = "保存失敗：" + error.GetType().Name;
            } finally { config.SaveOnConfigSet = previousAutoSave; }
        }
        private void DrawPlaybackSettings()
        {
            var active = current();
            bool available = active != null && active.NumericNpcId == 15 && active.VideoReady;
            string[] poses = available ? active.PoseNames : new string[0];
            string[] faces = available ? active.FaceNames : new string[0];
            GUILayout.Label("NPC ID 15：モニター専用の再生設定");
            GUILayout.Label(available ? "接続済み：Animationを選び、保存してください。" :
                "施設にNPC ID 15をセットして、モニターを表示してください。");
            editMode = GUILayout.SelectionGrid(editMode, StateLabels, 2);
            if (editMode < 0 || editMode > 3) editMode = 0;
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && available && poses.Length > 0 && faces.Length > 0;
            DrawChoice("身体アニメーション（track 0）", poses, draftPoses);
            DrawChoice("表情アニメーション（track 2）", faces, draftFaces);
            GUI.enabled = previousEnabled;
            GUILayout.Space(9);
            GUILayout.Label("編集中の値（保存するまでは未保存）");
            GUILayout.BeginHorizontal();GUI.enabled=available;
            if(GUILayout.Button("プレビュー開始")){legacyPlaybackPreview=true;playbackPreviewOwner=active;playbackPreviewSignature=null;}
            if(GUILayout.Button("プレビュー終了"))StopPlaybackPreview();GUI.enabled=previousEnabled;GUILayout.EndHorizontal();
            GUILayout.Label(legacyPlaybackPreview?"プレビュー中：選択モニターのみ。最終状態の再生中は復帰後に反映します。":"プレビュー停止中：保存済みの施設状態を表示");
            if(legacyPlaybackPreview&&active!=null&&active.VideoTexture!=null){var playbackArea=GUILayoutUtility.GetRect(210,210,GUILayout.ExpandWidth(false));GUI.DrawTexture(playbackArea,active.VideoTexture,ScaleMode.ScaleToFit,false);}
            for (int i = 0; i < 4; i++) {
                GUILayout.Label(StateLabels[i] + ": " + draftPoses[i] + " / " + draftFaces[i]);
            }
            bool valid = available;
            for (int i = 0; i < 4; i++) valid &= Contains(poses,draftPoses[i]) && Contains(faces,draftFaces[i]);
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("保存値へ戻す")){StopPlaybackPreview();ResetPlaybackDraft();}
            GUI.enabled = previousEnabled && valid;
            if (GUILayout.Button("4モードを保存")) SavePlaybackSettings();
            GUI.enabled = previousEnabled;
            GUILayout.EndHorizontal();
            GUILayout.Label("再生設定は従来の.cfgの[MonitorStatePlayback]に保存します。");
            GUILayout.Label("ID 15の再生設定は保存で反映します。最終状態の再生を途中で中断しません。");
            if (!valid && available) GUILayout.Label("このモデルに存在しないAnimationが含まれるため保存できません。");
            if (!string.IsNullOrEmpty(playbackMessage)) GUILayout.Label(playbackMessage);
            if (GUILayout.Button("閉じる（未保存変更を破棄）")) Close();
        }
        internal void TickDiagnostics()
        {
            if(!MonitorDebugSettings.Enabled||!uiReady)return;
            var diag=diagnostics();if(diag==null)return;
            if(open && diagnosticTab) diag.Tick();else diag.Stop("ui-inactive");
        }
        private static string Flag(bool? value) {return value.HasValue?value.Value.ToString():"Unknown";}
        private void DrawDiagnostics()
        {
            GUILayout.Label("Read-only / native facility track 0");
            var diag=diagnostics();
            if(diag==null) {GUILayout.Label("Unknown: attach one original facility first.");return;}
            var value=diag.Snapshot;
            GUILayout.Label("NPC ID: "+(value.NpcId.HasValue?value.NpcId.Value.ToString():value.NpcSet==false?"none":"Unknown"));
            GUILayout.Label("NPC set: "+Flag(value.NpcSet)+" / in use: "+Flag(value.InUse));
            GUILayout.Label("Shared ref present: "+Flag(value.SharedRefPresent));
            GUILayout.Label("Shared matches this facility: "+Flag(value.SharedMatchesOwner));
            GUILayout.Label("Animation: "+value.Animation);
            GUILayout.Label("Observed state: "+value.State);
            GUILayout.Label("Source: "+value.Reason);
            GUILayout.Label("Sample frame: "+value.Frame+" / capture: "+(diag.Running?diag.Remaining.ToString("F1")+"s":"stopped (snapshot)"));
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Capture 30 seconds"))diag.Begin();
            if(GUILayout.Button("Stop capture"))diag.Stop("manual");
            if(GUILayout.Button("Refresh once"))diag.Refresh();
            GUILayout.EndHorizontal();
            GUILayout.Label("Only transitions are logged. Unknown does not mean idle.\nNo normal/fast semantic mapping is assumed.\nClosing the UI stops continuous reading.");
            if(GUILayout.Button("Close diagnostics / cancel"))Close();
        }
        private static float SliderField(string label,ref string text,float value,float min,float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label,GUILayout.Width(42));
            float next=GUILayout.HorizontalSlider(value,min,max,GUILayout.MinWidth(240),GUILayout.ExpandWidth(true));
            if(next!=value) text=Text(next);
            text=GUILayout.TextField(text,24,GUILayout.Width(82));
            GUILayout.EndHorizontal();
            return next;
        }
        internal void Cancel() {Close();}
        public void Dispose() {Close();if(inputShield!=null) UnityEngine.Object.Destroy(inputShield.gameObject);}
    }
}
