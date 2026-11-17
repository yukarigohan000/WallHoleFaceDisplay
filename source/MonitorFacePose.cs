using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Spine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Pose selection is independent of mesh rendering. No inferred facial animation/track.
    internal static class MonitorFacePose
    {
        // Base slots verified by v0.2.18; additional head/hair variants are explicitly
        // grouped by RandomCharacter.RemoveHead / SetCharacter / LoadColors.
        private static readonly HashSet<string> FaceSlots = new HashSet<string>(StringComparer.Ordinal)
        { "Head", "Head2", "Ear", "EarBack", "Cheek", "face_blue", "face_red", "Eye", "EyeBrow", "EyeWhite",
          "Face", "HairBack", "HairBack2", "HairBack_M", "HairBack_H", "HairBack_L", "HairFront", "HighLight",
          "Mouth", "Nose", "Tatoo", "Tears", "Droll", "Brain", "Mouth_up", "Mouth_low", "Mouth_Inside",
          "L_Antler", "R_Antler", "Drool", "EyeShadow", "CheekShadow", "Nose_sweat", "Hat", "HatBack", "Hat_L", "Hat_H" };

        // This pinned Spine runtime exposes world matrices/Active as read-only properties.
        // Copy only scalar INSTANCE fields into private bones/slots; never structural refs,
        // shared SlotData flags or source fields. Static capture keeps final matrices;
        // animation restores these private constraints before reevaluating its own pose.
        private static readonly FieldInfo[] BoneValues = Scalars(typeof(Bone));
        private static readonly FieldInfo[] SlotValues = Scalars(typeof(Slot));
        private static readonly FieldInfo[] IkValues = Scalars(typeof(IkConstraint));
        private static readonly FieldInfo[] TransformValues = Scalars(typeof(TransformConstraint));
        private static readonly FieldInfo[] PathValues = Scalars(typeof(PathConstraint));
        private static readonly FieldInfo AttachmentMarker = typeof(Slot).GetField("attachmentState", BindingFlags.Instance | BindingFlags.NonPublic);
        private static FieldInfo[] Scalars(Type type)
        {
            return type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => field.FieldType == typeof(float) || field.FieldType == typeof(bool) || field.FieldType == typeof(int)).ToArray();
        }

        internal static Skeleton Capture(Skeleton source)
        {
            var snapshot = CaptureFull(source); Mask(snapshot); ValidateHead(snapshot); return snapshot;
        }

        internal static Skeleton CaptureFull(Skeleton source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var snapshot = new Skeleton(source.Data); CopyRuntime(source, snapshot); return snapshot;
        }

        internal static void RestoreForAnimation(Skeleton baseline, Skeleton target)
        {
            CopyRuntime(baseline, target);
            // Source AnimationState's bookkeeping markers must not enter a different state.
            foreach (var slot in target.Slots) MonitorScalarCopy<Slot>.Zero(slot,AttachmentMarker);
        }

        private static void CopyRuntime(Skeleton source, Skeleton snapshot)
        {
            if (!ReferenceEquals(source.Data, snapshot.Data)) throw new InvalidOperationException("Different skeleton data layouts");
            snapshot.X = source.X; snapshot.Y = source.Y;
            snapshot.ScaleX = source.ScaleX; snapshot.ScaleY = source.ScaleY;
            snapshot.R = source.R; snapshot.G = source.G; snapshot.B = source.B; snapshot.A = source.A; snapshot.Time = source.Time;
            if (!ReferenceEquals(snapshot.Skin, source.Skin)) snapshot.SetSkin(source.Skin);
            bool bindingsChanged = false;
            for (int i = 0; i < source.IkConstraints.Count; i++)
            {
                var from = source.IkConstraints.Items[i]; var to = snapshot.IkConstraints.Items[i];
                var target = snapshot.Bones.Items[from.Target.Data.Index];
                bindingsChanged |= !ReferenceEquals(to.Target, target); to.Target = target;
                bindingsChanged |= MapBones(from.Bones, to.Bones, snapshot);
            }
            for (int i = 0; i < source.TransformConstraints.Count; i++)
            {
                var from = source.TransformConstraints.Items[i]; var to = snapshot.TransformConstraints.Items[i];
                var target = snapshot.Bones.Items[from.Target.Data.Index];
                bindingsChanged |= !ReferenceEquals(to.Target, target); to.Target = target;
                bindingsChanged |= MapBones(from.Bones, to.Bones, snapshot);
            }
            for (int i = 0; i < source.PathConstraints.Count; i++)
            {
                var from = source.PathConstraints.Items[i]; var to = snapshot.PathConstraints.Items[i];
                var target = snapshot.Slots.Items[from.Target.Data.Index];
                bindingsChanged |= !ReferenceEquals(to.Target, target); to.Target = target;
                bindingsChanged |= MapBones(from.Bones, to.Bones, snapshot);
            }
            if (bindingsChanged) snapshot.UpdateCache();
            for (int i = 0; i < source.Bones.Count; i++)
                MonitorScalarCopy<Bone>.Copy(source.Bones.Items[i],snapshot.Bones.Items[i],BoneValues);
            for (int i = 0; i < source.Slots.Count; i++)
            {
                var original = source.Slots.Items[i]; var copy = snapshot.Slots.Items[i];
                copy.Attachment = original.Attachment; // Keep UV/weighted bone indices in the SAME SkeletonData.
                MonitorScalarCopy<Slot>.Copy(original,copy,SlotValues);
                copy.Deform.Clear(); copy.Deform.AddRange(original.Deform); // Independent mutable deform buffer.
            }
            for (int i = 0; i < source.DrawOrder.Count; i++)
                snapshot.DrawOrder.Items[i] = snapshot.Slots.Items[source.DrawOrder.Items[i].Data.Index];
            for (int i = 0; i < source.IkConstraints.Count; i++)
                MonitorScalarCopy<IkConstraint>.Copy(source.IkConstraints.Items[i],snapshot.IkConstraints.Items[i],IkValues);
            for (int i = 0; i < source.TransformConstraints.Count; i++)
                MonitorScalarCopy<TransformConstraint>.Copy(source.TransformConstraints.Items[i],snapshot.TransformConstraints.Items[i],TransformValues);
            for (int i = 0; i < source.PathConstraints.Count; i++)
                MonitorScalarCopy<PathConstraint>.Copy(source.PathConstraints.Items[i],snapshot.PathConstraints.Items[i],PathValues);
        }

        private static bool MapBones(ExposedList<Bone> source, ExposedList<Bone> target, Skeleton owner)
        {
            bool changed = source.Count != target.Count;
            if (!changed) for (int i = 0; i < source.Count; i++)
                if (!ReferenceEquals(target.Items[i], owner.Bones.Items[source.Items[i].Data.Index])) { changed = true; break; }
            if (changed)
            {
                target.Clear();
                foreach (var bone in source) target.Add(owner.Bones.Items[bone.Data.Index]);
            }
            return changed;
        }

        internal static void Mask(Skeleton skeleton)
        {
            foreach (var slot in skeleton.Slots)
                if (!FaceSlots.Contains(slot.Data.Name) && !(slot.Attachment is ClippingAttachment)) slot.Attachment = null;
        }

        private static void ValidateHead(Skeleton snapshot)
        {
            var head = snapshot.FindSlot("Head");
            if (head == null || !(head.Attachment is RegionAttachment || head.Attachment is MeshAttachment))
                throw new InvalidOperationException("Target instance has no renderable Head");
        }
    }
}
