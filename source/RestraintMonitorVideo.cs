using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Spine;
using Spine.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Full rig is submitted ONLY to an offscreen camera command buffer, never a world Renderer.
    internal sealed class RestraintMonitorVideo : IDisposable
    {
        private readonly ManagersScript managers;
        private readonly SexPlace usage;
        private readonly Transform parent;
        /*WHFDPerfOnlyBegin*/
        private MeshRenderer performanceRenderer;
        private MonitorRenderGate renderGate;internal bool ForceRender;
        internal bool PerformanceVisible {get {return performanceRenderer!=null&&performanceRenderer.isVisible;}}
        /*WHFDPerfOnlyEnd*/
        private readonly int layer, order;
        private readonly int timingBuildId,timingInstanceId;
        private int timingSequence;
        private readonly ManualLogSource log;
        // These delegates read only this MOD's BepInEx settings, never native gameplay state.
        private readonly Func<int, string> poseByMode, faceByMode;
        private readonly Func<float> blendSeconds;
        private readonly Vector3[] screenVertices;
        private CommonStates npc;
        private Skeleton skeleton;
        private Spine.AnimationState state;
        private SkeletonAnimation template;
        private Camera camera;
        private RenderTexture texture;
        private CommandBuffer commands;
        private GameObject screenObject, cameraObject;
        private Mesh modelMesh, screenMesh;
        private Material screenMaterial;
        private readonly Dictionary<Material, Material> clones = new Dictionary<Material, Material>();
        private readonly Dictionary<Material, int[]> passes = new Dictionary<Material, int[]>();
        private readonly MeshGenerator generator = new MeshGenerator();
        private readonly SkeletonRendererInstruction instruction = new SkeletonRendererInstruction();
        private Rect envelope;
        private HeadFramingReference faceReference;
        private int framingAuditModes,appearanceAuditModes;
        private bool hasEnvelope, ready;
        private float zSpacing;
        private string phase;
        // 0=assigned/standby, 1=normal, 2=fast, 3=finish (native-state-scoped loop).
        private int selectedMode = -1;
        private int poseMode;
        internal int PoseMode {get {return poseMode;}}
        private bool finishing;
        private string lastNativeState = "Unknown";
        internal Func<int, MonitorFramingValue> FramingProvider;
        internal Func<MonitorArmValues> ArmProvider;
        internal Func<MonitorHeadValues> HeadProvider;
        internal Func<MonitorTorsoValues> TorsoProvider;
        private MonitorTorsoOffsets torsoOffsets;
        private string torsoBindingReason="Private video unavailable";
        internal bool TorsoAvailable {get {return ready && npc!=null && npc.npcID==15 && torsoOffsets!=null;}}
        internal string TorsoStatus {get {return torsoOffsets==null?torsoBindingReason:"Ready: spine_02_pos Rotation / degrees";}}

        private MonitorHeadOffsets headOffsets;
        private string headBindingReason="Private video unavailable";
        internal bool HeadAvailable {get {return ready && npc!=null && npc.npcID==15 && headOffsets!=null;}}
        internal string HeadStatus {get {return headOffsets==null?headBindingReason:"Ready: head_pos Rotation / degrees";}}

        private MonitorArmOffsets armOffsets;
        private string armBindingReason="Private video unavailable";
        internal bool ArmAvailable {get {return ready && npc!=null && npc.npcID==15 && armOffsets!=null;}}
        internal string ArmStatus {get {return armOffsets==null?armBindingReason:
            "Ready: root-local reach L="+armOffsets.LeftUnit.ToString("G5",System.Globalization.CultureInfo.InvariantCulture)+
            " / R="+armOffsets.RightUnit.ToString("G5",System.Globalization.CultureInfo.InvariantCulture);}}
        internal int? NumericNpcId { get { return npc == null ? (int?)null : npc.npcID; } }
        internal bool VideoReady { get { return ready; } }
        internal void SetDisplayVisible(bool visible){if(screenObject!=null)screenObject.SetActive(visible);}
        internal Texture VideoTexture { get { return ready ? texture : null; } }
        // Populate UI from this video's actual SkeletonData, not guessed animation names.
        internal string[] PoseNames { get { return skeleton == null ? new string[0] : skeleton.Data.Animations
            .Select(a => a.Name).Where(n => n != null && n.StartsWith("P_restrain", StringComparison.Ordinal)).ToArray(); } }
        internal string[] FaceNames { get { return skeleton == null ? new string[0] : skeleton.Data.Animations
            .Select(a => a.Name).Where(n => n != null && n.StartsWith("Face_", StringComparison.Ordinal)).ToArray(); } }
        // A saved configuration reselects on the next validated native snapshot.
        // Do not restart an active mode-3 playback.
        internal void RefreshPlaybackSettings() { if (ready && !finishing) selectedMode = -1; }
        // Bounded per decoration instance, not per frame or repeated failed start.
        private readonly HashSet<string> traced = new HashSet<string>();
        private bool awaitingFirstTick;
        private int publishedFrame;
        private void Trace(string key, string details)
        {
            if (MonitorDebugSettings.Enabled && traced.Add(key)) if(MonitorDebugSettings.Enabled)log.LogInfo("[MonitorVideoTrace] " + details);
        }
        private void TraceGate()
        {
            Trace("gate", "stage=start-gate; npcId=" + (npc == null ? "none" : npc.npcID.ToString()) +
                "; usageComponent=" + (usage != null) + "; inUse=" + (usage != null && usage.user != null));
            if (npc == null) Trace("wait-npc", "stage=start-gate; waiting=npc");
            else if (npc.npcID != 15) Trace("unsupported", "stage=start-gate; npcId=" + npc.npcID + "; supported=false");
            else Trace("npc-assigned", "stage=start-gate; npcId=15; startCondition=npc-assigned; inUse=" +
                (usage != null && usage.user != null));
        }

        internal RestraintMonitorVideo(ItemInfo owner, ManagersScript managers, Transform parent, Camera mainCamera,
            Rect screen, WoodPanelSurface surface, int layer, int order, ManualLogSource log,
            Func<int, string> poseByMode, Func<int, string> faceByMode, Func<float> blendSeconds,MonitorScreenGeometry screenGeometry)
        {
            this.managers = managers; this.parent = parent; this.layer = layer; this.order = order; this.log = log;
            this.poseByMode = poseByMode; this.faceByMode = faceByMode; this.blendSeconds = blendSeconds;
            usage = owner.GetComponent<SexPlace>();
            timingBuildId=owner.buildID;timingInstanceId=owner.GetInstanceID();
            if(screenGeometry==null)throw new MonitorVideoConditionException("Monitor aperture unavailable");
            screenVertices=screenGeometry.CopyFor(parent);
        }

        internal void SetNpc(CommonStates value)
        {
            Stop("npc-change"); npc = value;
            TraceGate();
            if (npc != null && npc.npcID == 15) Start();
        }
        internal void SetUseState(bool value)
        {
            Trace("use-" + value, "stage=use-event; inUse=" + value);
            // Usage changes must not tear down or restart a running private video.
            // If an earlier startup failed because the NPC rig was not ready, retry on use.
            if (value && !ready && npc != null && npc.npcID == 15) Start();
        }
        internal void TickAnimation(float deltaTime, MonitorStateSnapshot observed)
        {
            using(MonitorPerformance.Measure(MonitorPerfStage.NativeTick)) { /*WHFDPerfScope*/
            if (!ready) return;
            if (awaitingFirstTick && Time.frameCount > publishedFrame) {
                awaitingFirstTick = false;
                Trace("lifetime-next-tick", "stage=10; nextTick=true; elapsedFrames=" + (Time.frameCount - publishedFrame) +
                    "; ready=" + ready + "; screenActive=" + (screenObject != null && screenObject.activeInHierarchy) +
                    "; textureCreated=" + (texture != null && texture.IsCreated()));
            }
            if (npc == null || npc.npcID != 15) { Stop("npc-lost"); return; }
            try
            {
                phase = "private-animation";
                ObservePlaybackState(observed);
                AdvanceFixedPose(deltaTime);
                // Loop mode 3 while the owner-matched native state stays active.
                // Unknown snapshots are not evidence of an exit.
                int after = ModeFromSnapshot(observed);
                if (finishing && after >= 0 && after != 3) {
                    var timing = CaptureTiming(); // snapshot before replacing the active track
                    finishing = false;
                    SelectMode(after);
                    LogTiming("mode3-exit", () => (timing == null ? "sample=unavailable" : timing.Fields) +
                        "; reason=ConfirmedNativeTransition" +
                        "; requestedMode=" + after + "; selectedMode=" + selectedMode +
                        "; nativeState=" + (observed == null ? "Unknown" : observed.State) +
                        "; nativeAnimation=" + (observed == null ? "Unknown" : observed.Animation) +
                        "; source=" + (observed == null ? "Unknown" : observed.Reason) +
                        "; inUse=" + (observed == null || !observed.InUse.HasValue ? "Unknown" : observed.InUse.Value.ToString()));
                }
                Render();
            }
            catch (Exception error) { Fail(error); }
        
            } /*WHFDPerfEnd*/}

        private void Start()
        {
            using(MonitorPerformance.Measure(MonitorPerfStage.NativeStart)) { /*WHFDPerfScope*/
            Stop("start-reset");
            Trace("start", "stage=start; npcId=15; entered=true");
            try
            {
                phase = "recipe-source";
                if (GraphicsSettings.currentRenderPipeline != null) throw new MonitorVideoConditionException("Built-in camera path required");
                var facility = managers.itemMN.FindItem("gen_restrain_01");
                if (facility == null || facility.itemObj == null || facility.itemObj.GetComponent<InventorySlot>() == null)
                    throw new MonitorVideoConditionException("Reference facility unavailable");
                template = MonitorFaceSource.Resolve(managers);
                var data = template.skeletonDataAsset.GetSkeletonData(false);
                Trace("facility", "stage=2; gen_restrain_01=resolved; restraintData=resolved");
                // Candidate names are logged once for editing state mappings. No names
                // are invented, and unavailable names are rejected at playback time.
                var names = data.Animations.Select(a => a.Name).Where(n => !string.IsNullOrEmpty(n)).ToArray();
                var faces = names.Where(n => n.StartsWith("Face_", StringComparison.Ordinal)).Take(80).ToArray();
                var poses = names.Where(n => n.StartsWith("P_restrain", StringComparison.Ordinal)).Take(80).ToArray();
                Trace("candidate-names", "stage=state-config; faceOptions=" + string.Join(",", faces) +
                    "; poseOptions=" + string.Join(",", poses) + "; listingLimit=80-each");
                skeleton = new Skeleton(data) { ScaleX = template.initialFlipX ? -1 : 1, ScaleY = template.initialFlipY ? -1 : 1 };
                MonitorPerformance.Count(MonitorPerfEvent.NativeSkeleton); /*WHFDPerfEvent*/
                if (!string.IsNullOrEmpty(template.initialSkinName) && template.initialSkinName != "default") skeleton.SetSkin(template.initialSkinName);
                skeleton.SetToSetupPose();
                var original = MonitorFaceSource.ResolveInstance(npc);
                phase = "appearance";
                RestraintVideoRecipe.ApplyAppearance(skeleton, original.Skeleton, npc.parameters, npc.colors);
                phase = "fixed-pose";
                state = RestraintVideoRecipe.Configure(skeleton);
                MonitorPerformance.Count(MonitorPerfEvent.NativeState); /*WHFDPerfEvent*/
                MonitorRigMapDiagnostic.WriteOnce(skeleton,npc.npcID,log);
                if(!MonitorArmOffsets.TryCreate(skeleton,out armOffsets,out armBindingReason))
                    Trace("arm-binding", "[MonitorArms] npcId=15; disabled="+armBindingReason);
                else if(ApplyArmOverlay()) skeleton.UpdateWorldTransform();
                Trace("accessory", "stage=2; att_vib_01=resolved; VibAttached=" + (skeleton.FindSlot("Vib").Attachment != null));
                Trace("private", "stage=3; privateSkeleton=true; privateAnimationState=true; renderer=command-buffer-mesh; worldModelRenderer=none");
                Trace("pose", "stage=4; track=0; animation=P_restrain_attach_01; assigned=" + (state.GetCurrent(0) != null && state.GetCurrent(0).Animation.Name == RestraintVideoRecipe.Pose));
                Trace("face", "stage=5; track=2; animation=Face_01; assigned=" + (state.GetCurrent(2) != null && state.GetCurrent(2).Animation.Name == RestraintVideoRecipe.Expression));
                // Proven instance renderer spacing preserves negative-Z-front ordering.
                zSpacing = original.zSpacing;
                if (zSpacing >= 0) throw new MonitorVideoConditionException("Verified negative instance depth spacing unavailable");
                // Resolve/measure the actual rig before allocating or rendering any video target.
                phase = "mesh-and-crop";
                envelope = RestraintBustFraming.Measure(skeleton); hasEnvelope = true;
                RestraintBustFraming.TryCaptureFace(skeleton,envelope,out faceReference);
                try{if(MonitorDebugSettings.Enabled)log.LogInfo("[MonitorFramingAudit] route=native-initial; npcId=15; monitorId="+timingInstanceId+"; buildId="+timingBuildId+"; npcInstanceId="+npc.GetInstanceID()+"; asset="+template.skeletonDataAsset.name+"; templateScale="+template.transform.lossyScale+"; referencePose="+MonitorTemplatePlayback.AuditPose(skeleton)+"; slots="+RestraintBustFraming.AuditAttachments(skeleton));}catch(Exception e){log.LogWarning("[MonitorFramingAudit] native initial unavailable: "+e.GetType().Name);}
                // Bind and apply ONLY after the uncorrected framing reference is frozen.
                if(faceReference==null) headBindingReason="Uncorrected face framing unavailable";
                else if(!MonitorHeadOffsets.TryCreate(skeleton,out headOffsets,out headBindingReason))
                    Trace("head-binding", "[MonitorHead] npcId=15; disabled="+headBindingReason);
                else {Trace("head-binding", "[MonitorHead] npcId=15; verified=head_pos; attachments=weighted-descendants");
                    ApplyHeadOverlay();skeleton.UpdateWorldTransform();}
                // Capture framing before any torso correction, including persisted values.
                if(faceReference==null)torsoBindingReason="Uncorrected face framing unavailable";
                else if(!MonitorTorsoOffsets.TryCreate(skeleton,out torsoOffsets,out torsoBindingReason))
                    Trace("torso-binding","[MonitorTorso] npcId=15; disabled="+torsoBindingReason);
                else {Trace("torso-binding","[MonitorTorso] npcId=15; verified=spine_02_pos; native=PositionEditing/Override");
                    ApplyTorsoOverlay();skeleton.UpdateWorldTransform();}
                if(MonitorDebugSettings.Enabled)log.LogInfo("[MonitorVideoCrop] npcId=15; basis=measured-Head-attachment; neck-slot-required=false; fullRig=true; cameraWindow=" + envelope);
                phase = "offscreen-resources";
                texture = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default) {
                    name = "WHFD_Video15", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 1, useMipMap = false, autoGenerateMips = false
                };
                if (!texture.Create()) throw new MonitorVideoConditionException("Video texture creation failed");
                MonitorPerformance.Count(MonitorPerfEvent.NativeRTCreate); /*WHFDPerfEvent*/
                cameraObject = new GameObject("WHFD_Offscreen15"); cameraObject.hideFlags = HideFlags.HideAndDontSave;
                // Keep this instance's camera in the same scene as its owning attachment.
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,parent.gameObject.scene);
                camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 0;
                camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.targetTexture = texture; camera.aspect = texture.width / (float)texture.height; camera.renderingPath = RenderingPath.Forward; camera.allowHDR = false; camera.allowMSAA = false; camera.useOcclusionCulling = false;
                commands = new CommandBuffer { name = "WHFD_PrivateRig15" };
                camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, commands);
                modelMesh = new Mesh { name = "WHFD_OffscreenFullRig15" };
                Trace("resources", "stage=6; camera=true; renderTextureCreated=" + texture.IsCreated() + "; size=512x512; cullingMask=0; submission=CommandBuffer");
                // First render is the requested exact pose at time zero. Playback starts only after success.
                Render(); InspectTextureOnce(); CreateScreen(); ready = true;
                awaitingFirstTick = true; publishedFrame = Time.frameCount;
                Trace("published", "stage=10; published=true; screenActive=" + screenObject.activeInHierarchy + "; ready=true");
                if(MonitorDebugSettings.Enabled)log.LogInfo("[MonitorVideo] npcId=15; source=gen_restrain_01; accessory=att_vib_01; tracks=0:P_restrain_attach_01,2:Face_01; mode=RenderTexture; crop=camera-bust-window; full-rig=true; worldModelRenderer=none");
            }
            catch (Exception error) { Fail(error); }
        
            } /*WHFDPerfEnd*/}

        // Reads only the verified, owner-matched native snapshot; never writes native Spine.
        private static int ModeFromSnapshot(MonitorStateSnapshot observed)
        {return MonitorPlaybackMode.FromSnapshot(observed,15); }

        private void ObservePlaybackState(MonitorStateSnapshot observed)
        {
            int requested = ModeFromSnapshot(observed);
            string native = observed != null && requested >= 0 ? observed.State : "Unknown";
            // Trigger finish only on a confirmed transition into the native finish animation.
            if (requested == 3) {
                if (lastNativeState != "A_Finish") {
                    SelectMode(3);
                    finishing = true;
                }
            } else if (!finishing && requested >= 0) SelectMode(requested);
            // Unknown is not evidence of leaving an animation. Avoid retriggering on
            // transient shared-reference gaps during the same active use session.
            if (requested == 0 && observed != null && observed.InUse == false)
                lastNativeState = "NotInUse";
            else if (native != "Unknown") lastNativeState = native;
        }

        private void SelectMode(int mode)
        {
            if (mode < 0 || mode > 3 || skeleton == null || state == null) return;
            if (selectedMode == mode && mode != 3) return;
            string pose = poseByMode == null ? null : poseByMode(mode);
            string face = faceByMode == null ? null : faceByMode(mode);
            pose = AvailableOrDefault(pose, RestraintVideoRecipe.Pose, "pose", mode);
            face = AvailableOrDefault(face, RestraintVideoRecipe.Expression, "face", mode);
            float blend = blendSeconds == null ? 0.2f : blendSeconds();
            if (float.IsNaN(blend) || float.IsInfinity(blend)) blend = 0.2f;
            blend = Mathf.Clamp(blend, 0, 1);
            bool loopPose = true; // Mode 3 repeats until a verified native state transition.
            ApplyTrack(0, pose, loopPose, blend, mode == 3);
            ApplyTrack(2, face, true, blend, mode == 3);
            poseMode=mode; // settings input only; playback/finish state machine is unchanged
            if(torsoOffsets!=null) torsoOffsets.RestoreBeforeAnimation();
            if(headOffsets!=null) headOffsets.RestoreBeforeAnimation();
            if(armOffsets!=null) armOffsets.RestoreBeforeAnimation();
            state.Apply(skeleton); ApplyArmOverlay(); ApplyTorsoOverlay(); ApplyHeadOverlay(); skeleton.UpdateWorldTransform();
            selectedMode = mode;
            if(mode==3){timingSequence++;LogTiming("mode3-enter",()=>{var sample=CaptureTiming();return sample==null?"sample=unavailable":sample.Fields;});}
            if(MonitorDebugSettings.Enabled)log.LogInfo("[MonitorVideoState] npcId=15; monitorMode=" + mode +
                "; pose=" + pose + "; face=" + face + "; loopPose=" + loopPose + "; blend=" + blend+TimingContext);
        }

        private string AvailableOrDefault(string requested, string fallback, string track, int mode)
        {
            if (!string.IsNullOrEmpty(requested) && skeleton.Data.FindAnimation(requested) != null) return requested;
            if (!string.IsNullOrEmpty(requested) && requested != fallback)
                Trace("invalid-" + track + "-" + mode,
                    "stage=state-config; mode=" + mode + "; invalid=" + requested + "; fallback=" + fallback);
            return fallback;
        }

        private void ApplyTrack(int track, string animation, bool loop, float blend, bool restart)
        {
            var current = state.GetCurrent(track);
            if (!restart && current != null && current.Animation != null && current.Animation.Name == animation && current.Loop == loop)
                return;
            var next = state.SetAnimation(track, animation, loop);
            next.MixDuration = blend;
        }

        private string TimingContext {get {return "; buildID="+timingBuildId+"; facilityInstance="+timingInstanceId+"; sequence="+timingSequence;}}
        private MonitorPlaybackTimingSample CaptureTiming()
        {
            if(!MonitorDebugSettings.Enabled)return null;
            try{return MonitorPlaybackTimingSample.Capture(state==null?null:state.GetCurrent(0),template==null?float.NaN:template.timeScale,state==null?float.NaN:state.TimeScale);}
            catch(Exception error){try{log.LogWarning("[MonitorPlaybackTiming]"+TimingContext+"; event=sample-unavailable; error="+error.GetType().Name);}catch{}return null;}
        }
        private void LogTiming(string name,Func<string> details)
        {
            if(!MonitorDebugSettings.Enabled)return;
            try{if(MonitorDebugSettings.Enabled)log.LogInfo("[MonitorPlaybackTiming] npcId=15"+TimingContext+"; event="+name+"; frame="+Time.frameCount+"; "+details());}
            catch{} // diagnostics must never change playback or resource lifetime
        }


        // Existing private Skeleton/AnimationState remain sole owners of the rendered frame.
        private void AdvanceFixedPose(float deltaTime)
        {
            if(torsoOffsets!=null) torsoOffsets.RestoreBeforeAnimation();
            if(headOffsets!=null) headOffsets.RestoreBeforeAnimation();
            if(armOffsets!=null) armOffsets.RestoreBeforeAnimation();
            state.Update(deltaTime * template.timeScale); state.Apply(skeleton);
            ApplyArmOverlay(); ApplyTorsoOverlay(); ApplyHeadOverlay(); skeleton.UpdateWorldTransform();
        }
        private void ApplyTorsoOverlay()
        {
            if(torsoOffsets==null)return;
            try {
                string reason;
                if(!torsoOffsets.ApplyAfterAnimation(TorsoProvider==null?null:TorsoProvider(),out reason))
                    Trace("torso-values","[MonitorTorso] npcId=15; rejected="+reason);
            } catch(Exception error) {
                torsoOffsets.RestoreBeforeAnimation();torsoOffsets=null;
                torsoBindingReason="Torso overlay disabled: "+error.GetType().Name;
                Trace("torso-failure","[MonitorTorso] npcId=15; "+torsoBindingReason);
            }
        }
        private void ApplyHeadOverlay()
        {
            if(headOffsets==null)return;
            try {
                string reason;
                if(!headOffsets.ApplyAfterAnimation(HeadProvider==null?null:HeadProvider(),out reason))
                    Trace("head-values","[MonitorHead] npcId=15; rejected="+reason);
            } catch(Exception error) {
                headOffsets.RestoreBeforeAnimation();headOffsets=null;
                headBindingReason="Head overlay disabled: "+error.GetType().Name;
                Trace("head-failure","[MonitorHead] npcId=15; "+headBindingReason);
            }
        }
        private bool ApplyArmOverlay()
        {
            if(armOffsets==null)return false;
            try {
                string reason;
                if(!armOffsets.ApplyAfterAnimation(ArmProvider==null?null:ArmProvider(),out reason))
                    Trace("arm-values", "[MonitorArms] npcId=15; rejected="+reason);
                return armOffsets.HasApplied;
            } catch(Exception error) {
                armOffsets.RestoreBeforeAnimation();armOffsets=null;
                armBindingReason="Arm overlay disabled: "+error.GetType().Name;
                Trace("arm-failure", "[MonitorArms] npcId=15; "+armBindingReason);return false;
            }
        }
        private void Render()
        {
            using(MonitorPerformance.Measure(MonitorPerfStage.NativeRender)) { /*WHFDPerfScope*/
            var framing = FramingProvider == null ? null : FramingProvider(npc.npcID);
            if(framing==null || !framing.Valid || framing.NpcId!=npc.npcID)framing=new MonitorFramingValue {NpcId=npc.npcID};
            RenderPose(skeleton,framing);
        
            } /*WHFDPerfEnd*/}
        // Read-only drawing boundary. Does not own, advance, apply or dispose the pose.
        // A future nonsexual adapter must provide compatible rendering settings/resources.
        private void RenderPose(Skeleton renderPose,MonitorFramingValue framing)
        {
            phase = "mesh-and-crop";
            using(MonitorPerformance.Measure(MonitorPerfStage.NativeMeshAndSubmit)) { /*WHFDPerfScope*/
            MeshGenerator.GenerateSkeletonRendererInstruction(instruction, renderPose, null, null, false, false);
            generator.settings = new MeshGenerator.Settings { useClipping = true, pmaVertexColors = template.pmaVertexColors,
                tintBlack = template.tintBlack, zSpacing = zSpacing, addNormals = template.addNormals, calculateTangents = template.calculateTangents };
            generator.Begin(); generator.BuildMesh(instruction, true); modelMesh.Clear();
            generator.FillVertexData(modelMesh); generator.FillTriangles(modelMesh); generator.FillLateVertexData(modelMesh);
            if (generator.VertexCount == 0) throw new MonitorVideoConditionException("Private pose has no mesh");
            Vector2 center;float half;
            if(faceReference!=null && faceReference.Matches(renderPose)){
                // Keep the initial head center/view span. Animation moves inside the frame,
                // instead of making a history-dependent camera pan/zoom every frame.
                center=faceReference.Center;half=faceReference.Half;
            }else{
                var crop = RestraintBustFraming.Measure(renderPose);
                envelope = hasEnvelope ? RestraintBustFraming.Union(envelope, crop) : crop; hasEnvelope = true;
                center=envelope.center;half=Mathf.Max(envelope.width,envelope.height)*.54f;
            }
            var vertices = MonitorPerformance.ReuseVertexBuffer?generator.Buffers.vertexBuffer:generator.Buffers.vertexBuffer.Take(generator.VertexCount).ToArray();
            float minZ,maxZ;MonitorVertexRange.ZBounds(vertices,generator.VertexCount,out minZ,out maxZ);
            if (framing == null || !framing.Valid) framing = new MonitorFramingValue();
            camera.transform.position = new Vector3(center.x + framing.X * half * 2f,
                center.y + framing.Y * half * 2f, minZ - 1f);
            camera.transform.rotation = Quaternion.identity; camera.orthographicSize = half / framing.Zoom;
            camera.nearClipPlane = 0.01f; camera.farClipPlane = maxZ - minZ + 2f;
            commands.Clear();
            // The offscreen camera frustum and texture rectangle are the only crop.
            // Do not cut the model at an inferred neck boundary or hide individual slots.
            int drawCalls = 0;
            for (int i = 0; i < instruction.submeshInstructions.Count; i++)
            {
                var material = GetMaterial(instruction.submeshInstructions.Items[i].material);
                foreach (int pass in passes[material]) { commands.DrawMesh(modelMesh, Matrix4x4.identity, material, i, pass); drawCalls++; }
            }
            if (!traced.Contains("visibility")) {
                int visible=0;for(int vi=0;vi<generator.VertexCount;vi++){var v=camera.WorldToViewportPoint(vertices[vi]);if(v.z>=camera.nearClipPlane&&v.z<=camera.farClipPlane&&v.x>=0&&v.x<=1&&v.y>=0&&v.y<=1)visible++;}
                Trace("visibility", "stage=7; worldModelRenderer=none; commandDraws=" + drawCalls +
                    "; submeshes=" + instruction.submeshInstructions.Count + "; vertices=" + generator.VertexCount + "; verticesInFrustum=" + visible);
            }
            } /*WHFDPerfEnd*/
            if(MonitorDebugSettings.Enabled&&selectedMode>=0&&(framingAuditModes&(1<<poseMode))==0){
                framingAuditModes|=1<<poseMode;
                var position=camera.transform.position;var head=skeleton.FindBone("head_pos");var body=state.GetCurrent(0);
                if(MonitorDebugSettings.Enabled)log.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[MonitorFramingAudit] npcId=15; monitorId={0}; buildId={1}; mode={2}; X={3:R}; Y={4:R}; Zoom={5:R}; cameraX={6:R}; cameraY={7:R}; half={8:R}; refX={9:R}; refY={10:R}; headX={11:R}; headY={12:R}; body={13}; provider={14}",
                    timingInstanceId,timingBuildId,poseMode,framing.X,framing.Y,framing.Zoom,position.x,position.y,camera.orthographicSize,
                    faceReference==null?float.NaN:faceReference.Center.x,faceReference==null?float.NaN:faceReference.Center.y,
                    head==null?float.NaN:head.WorldX,head==null?float.NaN:head.WorldY,body==null?"none":body.Animation.Name,FramingProvider!=null));
            }
            if(MonitorDebugSettings.Enabled && selectedMode>=0 && (appearanceAuditModes&(1<<poseMode))==0){
                appearanceAuditModes|=1<<poseMode;
                try{if(MonitorDebugSettings.Enabled)log.LogInfo("[MonitorFramingAudit] route=native-details; npcId=15; monitorId="+timingInstanceId+"; buildId="+timingBuildId+"; npcInstanceId="+(npc==null?0:npc.GetInstanceID())+"; mode="+poseMode+"; providerPresent="+(FramingProvider!=null)+"; preview=unavailable; referencePath="+(faceReference!=null&&faceReference.Matches(renderPose)?"head-attachment-span":"envelope")+"; referenceHalf="+(faceReference==null?"unavailable":faceReference.Half.ToString("R",System.Globalization.CultureInfo.InvariantCulture))+"; skeleton="+MonitorTemplatePlayback.AuditPose(renderPose)+"; slots="+RestraintBustFraming.AuditAttachments(renderPose));}catch(Exception e){log.LogWarning("[MonitorFramingAudit] native diagnostic unavailable: "+e.GetType().Name);}
            }
            phase = "offscreen-render"; if(renderGate==null)renderGate=new MonitorRenderGate(camera,MonitorPerfStage.NativeCamera,MonitorPerfEvent.NativeCameraRender);
            renderGate.Request(performanceRenderer,ForceRender,npc==null?-1:npc.npcID,timingInstanceId);
            Trace("rendered", "stage=7; cameraRenderReturned=true");
        }

        // Diagnostic targets only: never reads a source game texture.
        private int ReadTarget(string label)
        {
            var previous = RenderTexture.active;
            Texture2D sample = null;
            try {
                RenderTexture.active = texture;
                sample = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                sample.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
                var pixels = sample.GetPixels32();
                int rgb = pixels.Count(c => c.r != 0 || c.g != 0 || c.b != 0);
                int alpha = pixels.Count(c => c.a != 0);
                if(MonitorDebugSettings.Enabled)log.LogInfo("[MonitorVideoProbe] npcId=15; test=" + label + "; rgbNonblack=" + rgb +
                    "; alphaNonzero=" + alpha + "; alphaMin=" + pixels.Min(c => c.a) +
                    "; alphaMax=" + pixels.Max(c => c.a) + "; pixels=" + pixels.Length);
                return rgb;
            }
            finally { RenderTexture.active = previous; if (sample != null) Object.Destroy(sample); }
        }

        private void InspectTextureOnce()
        {
            if (!MonitorDebugSettings.Enabled || !traced.Add("texture-readback")) return;
            try {
                int rgb = ReadTarget("production-unlit-frame");
                if(MonitorDebugSettings.Enabled)log.LogInfo("[MonitorVideoTrace] stage=8; productionUnlit=true; fullyBlack=" + (rgb == 0));
            }
            catch (Exception error) {
                log.LogWarning("[MonitorVideoProbe] test=diagnostic; unavailable=true; error=" + error.GetType().Name);
            }
        }

        private Material GetMaterial(Material source)
        {
            Material material;
            if (source == null || source.mainTexture == null)
                throw new MonitorVideoConditionException("Pose atlas texture unavailable");
            if (clones.TryGetValue(source, out material)) return material;
            // This exact unlit shader/pass rendered the original mesh/UV/vertex RGBA in
            // v0.2.27. Never replace the source material or inherit Lit/stencil keywords.
            var shader = Shader.Find("Sprites/Default");
            if (shader == null || !shader.isSupported)
                throw new MonitorVideoConditionException("Verified video unlit shader unavailable");
            material = new Material(shader);
            clones.Add(source, material);
            material.mainTexture = source.mainTexture;
            material.color = Color.white;
            if (material.HasProperty("_RendererColor")) material.SetColor("_RendererColor", Color.white);
            if (material.passCount < 1) throw new MonitorVideoConditionException("Verified video unlit pass unavailable");
            passes.Add(material, new[] { 0 });
            Trace("material-" + source.GetInstanceID(), "stage=material; shader=Sprites/Default; selectedPasses=0; videoOnly=true; atlasReused=true; vertexRGBA=unchanged");
            return material;
        }

        private void CreateScreen()
        {
            phase = "screen-publication";
            var shader = Shader.Find("Sprites/Default"); if (shader == null) throw new MonitorVideoConditionException("Video display shader unavailable");
            screenMaterial = new Material(shader); screenMaterial.mainTexture = texture; screenMaterial.color = Color.white;
            if (screenMaterial.HasProperty("_RendererColor")) screenMaterial.SetColor("_RendererColor", Color.white);
            screenMesh = new Mesh { name = "WHFD_VideoSurface15" }; screenMesh.vertices = screenVertices;
            screenMesh.uv = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(0,1), new Vector2(1,1) };
            screenMesh.colors = Enumerable.Repeat(Color.white, 4).ToArray(); screenMesh.triangles = new[] { 0,2,1,1,2,3 }; screenMesh.RecalculateBounds();
            screenObject = new GameObject("RestraintVideo15"); screenObject.layer = parent.gameObject.layer; screenObject.transform.SetParent(parent, false);
            screenObject.AddComponent<MeshFilter>().sharedMesh = screenMesh;
            var renderer = screenObject.AddComponent<MeshRenderer>();
            performanceRenderer=renderer; /*WHFDPerfEvent*/ renderer.sharedMaterial = screenMaterial;
            renderer.sortingLayerID = layer; renderer.sortingOrder = order; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            Trace("screen", "stage=9; textureAssigned=" + (screenMaterial.mainTexture == texture) + "; rendererEnabled=" + renderer.enabled + "; screenActive=" + screenObject.activeInHierarchy);
        }

        private void Fail(Exception error)
        {
            log.LogWarning("[MonitorVideo] npcId=15; display=standby; phase=" + phase + "; error=" + error.GetType().Name + "; reason=" + (error is MonitorVideoConditionException ? error.Message : "runtime-api-failure"));
            Stop("failure");
        }
        private void Stop(string reason)
        {
            using(MonitorPerformance.Measure(MonitorPerfStage.NativeStop)) { /*WHFDPerfScope*/
            if(renderGate!=null){renderGate.Dispose();renderGate=null;}performanceRenderer=null; /*WHFDPerfEvent*/
            poseMode=0;framingAuditModes=0;appearanceAuditModes=0;
            if(torsoOffsets!=null){torsoOffsets.RestoreBeforeAnimation();torsoOffsets=null;}
            torsoBindingReason="Private video unavailable";
            if(headOffsets!=null){headOffsets.RestoreBeforeAnimation();headOffsets=null;}
            headBindingReason="Private video unavailable";
            if(armOffsets!=null){armOffsets.RestoreBeforeAnimation();armOffsets=null;}
            armBindingReason="Private video unavailable";
            if (screenObject != null || awaitingFirstTick)
                Trace("stop-" + reason, "stage=10; teardown=true; reason=" + reason + "; afterFrames=" + (Time.frameCount - publishedFrame) + "; beforeFirstTick=" + awaitingFirstTick);
            awaitingFirstTick = false;
            ready = false;
            if (screenObject != null) { screenObject.SetActive(false); Object.Destroy(screenObject); }
            if (camera != null) { camera.RemoveAllCommandBuffers(); camera.targetTexture = null; }
            if (commands != null) { commands.Release(); commands = null; }
            if (cameraObject != null) Object.Destroy(cameraObject);
            if (texture != null) { MonitorPerformance.Count(MonitorPerfEvent.NativeRTRelease); /*WHFDPerfEvent*/ texture.Release(); Object.Destroy(texture); }
            foreach (var material in clones.Values) Object.Destroy(material); clones.Clear(); passes.Clear();
            if (screenMaterial != null) Object.Destroy(screenMaterial);
            if (modelMesh != null) Object.Destroy(modelMesh); if (screenMesh != null) Object.Destroy(screenMesh);
            screenObject = null; cameraObject = null; camera = null; texture = null; modelMesh = null; screenMesh = null; screenMaterial = null;
            skeleton = null; state = null; template = null; hasEnvelope = false; faceReference=null;
            selectedMode = -1; finishing = false; lastNativeState = "Unknown";
            // Instruction buffers are capacity-sized structs containing skeleton refs.
            Array.Clear(instruction.submeshInstructions.Items, 0, instruction.submeshInstructions.Items.Length);
            instruction.Clear();
        
            } /*WHFDPerfEnd*/}
        public void Dispose() { Stop("dispose"); npc = null; }
    }
}
