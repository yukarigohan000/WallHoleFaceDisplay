using System;
using System.Collections.Generic;
using Spine;
using UnityEngine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Fixed, verified recipe; no gameplay API calls and no original instance writes.
    internal static class RestraintVideoRecipe
    {
        internal const string Accessory = "att_vib_01", Expression = "Face_01", Pose = "P_restrain_attach_01";
        private static readonly string[] ParameterSlots = { "HairFront", "HairBack", "HairBack2", "HairBack_H", "HairBack_L",
            "Face", "Nose", "EyeBrow", "HighLight", "Tatoo", "Ear", "Oppai", "Nipple" };
        private static readonly int[] ParameterIndices = { 0, 1, 1, 1, 1, 2, 3, 4, 5, 7, 8, 6, 6 };
        private static readonly HashSet<string> KeepOnMissing = new HashSet<string>(StringComparer.Ordinal)
        { "EyeBrow", "Nose", "Ear", "EarBack", "Head", "Neck", "HighLight" };

        private static readonly HashSet<string> OptionalSlots = new HashSet<string>(StringComparer.Ordinal) {
            "HairFront", "HairBack", "HairBack2", "HairBack_H", "HairBack_L", "HairBack_M",
            "Body_Cloth", "Body_Cloth_L", "Body_ClothBack", "Body_R_Cloth", "Body_L_Cloth", "R_arm_Cloth", "L_arm_Cloth",
            "Hip_Cloth", "Hip_Cloth_H", "Hip_Cloth_L", "Hip_Cloth_L2", "Hip_L_Cloth", "Hip_R_Cloth", "Hip_M_Cloth",
            "Hat", "Hat_L", "Acce", "Acce_L", "L_shoe", "L_shoe_H", "L_shoe_L", "R_shoe", "R_shoe_H", "R_shoe_L"
        };

        internal static void ApplyAppearance(Skeleton target, Skeleton individual, int[] parameters, Color32[] palette)
        {
            if (parameters == null || parameters.Length < 9) throw new MonitorVideoConditionException("Appearance parameters unavailable");
            if (individual.Skin != null && !ReferenceEquals(target.Data, individual.Data))
                throw new MonitorVideoConditionException("Custom cross-data runtime Skin is not verified");
            if (individual.Skin != null) target.SetSkin(individual.Skin);
            for (int i = 0; i < ParameterSlots.Length; i++)
                SetVariant(target, ParameterSlots[i], parameters[ParameterIndices[i]]);
            if (palette == null || palette.Length < 10) throw new MonitorVideoConditionException("Appearance palette unavailable");
            // RandomCharacter.LoadColors / GetCommonSkinSlots: same palette indices, no source writes.
            foreach (var name in new[] { "Head", "Head_H", "Neck", "Face", "Nose", "L_arm", "L_forearm", "L_hand", "R_arm", "R_hand",
                "R_forearm", "L_upleg", "L_leg", "R_upleg", "R_leg", "Body", "BodyBase", "Body_up", "Body_preg", "Hip",
                "Hips", "L_hip", "R_hip", "L_Hip", "R_Hip", "L_finger", "R_finger", "Ear", "EarBack", "L_foot", "R_foot", "Mouth_up", "Jaw" })
                Tint(target.FindSlot(name), palette[3]);
            Tint(target.FindSlot("HairFront"), palette[0]);
            foreach (var name in new[] { "HairBack", "HairBack2", "HairBack_M", "HairBack_H", "HairBack_L" }) Tint(target.FindSlot(name), palette[1]);
            Tint(target.FindSlot("EyeBrow"), palette[2]); Tint(target.FindSlot("Eye"), palette[4]); Tint(target.FindSlot("EyeWhite"), palette[6]);
            // Resolve attachments IN THE TARGET DATA. Never reuse weighted attachments from another rig.
            foreach (var slot in target.Slots)
            {
                var from = individual.FindSlot(slot.Data.Name);
                if (from == null) continue;
                slot.R = from.R; slot.G = from.G; slot.B = from.B; slot.A = from.A;
                slot.R2 = from.R2; slot.G2 = from.G2; slot.B2 = from.B2;
                if (from.Attachment != null)
                {
                    var mapped = target.GetAttachment(slot.Data.Index, from.Attachment.Name);
                    if (mapped != null) slot.Attachment = mapped;
                }
                else if (OptionalSlots.Contains(slot.Data.Name) || slot.Data.Name == "Neck" || slot.Data.Name == "L_arm" || slot.Data.Name == "R_arm")
                    slot.Attachment = null;
            }
            target.R = individual.R; target.G = individual.G; target.B = individual.B; target.A = individual.A;
        }

        private static void Tint(Slot slot, Color32 color)
        {
            if (slot == null) return;
            slot.R = color.r / 255f; slot.G = color.g / 255f; slot.B = color.b / 255f; slot.A = color.a / 255f;
        }

        private static void SetVariant(Skeleton skeleton, string name, int id)
        {
            var slot = skeleton.FindSlot(name); if (slot == null) return;
            if (id == -1) { slot.Attachment = null; return; }
            // RandomCharacter.SetAttachment's exact naming/fallback policy.
            var attachment = skeleton.GetAttachment(slot.Data.Index, name + "_" + id.ToString("00")) ??
                skeleton.GetAttachment(slot.Data.Index, name + "_00");
            if (attachment != null || !KeepOnMissing.Contains(name)) slot.Attachment = attachment;
        }

        internal static AnimationState Configure(Skeleton skeleton)
        {
            var vib = skeleton.FindSlot("Vib");
            if (vib == null) throw new MonitorVideoConditionException("Vib slot missing");
            var attachment = skeleton.GetAttachment(vib.Data.Index, Accessory);
            if (attachment == null) throw new MonitorVideoConditionException("Vib/att_vib_01 attachment missing");
            vib.Attachment = attachment;
            if (skeleton.Data.FindAnimation(Expression) == null || skeleton.Data.FindAnimation(Pose) == null)
                throw new MonitorVideoConditionException("Exact requested expression/pose unavailable");
            foreach (var name in new[] { "R_foot_ring", "L_foot_ring", "L_hand_ring", "R_hand_ring",
                "L_hand_chain", "L_foot_chain", "R_hand_chain", "R_foot_chain" }) SetVariant(skeleton, name, 1);
            var state = new AnimationState(new AnimationStateData(skeleton.Data));
            state.SetAnimation(0, Pose, true); state.SetAnimation(2, Expression, true);
            state.Apply(skeleton); skeleton.UpdateWorldTransform();
            if (skeleton.FindSlot("Vib").Attachment == null) throw new MonitorVideoConditionException("Requested pose removed required attachment");
            return state;
        }
    }
}
