using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pmx2Fbx.Core;
using UnityEngine;

namespace Pmx2Fbx
{
    public static class HumanoidRig
    {
        private static readonly Dictionary<HumanBodyBones, string[]> Aliases = CreateAliases();
        public static readonly HumanBodyBones[] Required = {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot
        };
        private static Dictionary<HumanBodyBones, string[]> CreateAliases()
        {
            var a = new Dictionary<HumanBodyBones, string[]> {
                { HumanBodyBones.Hips, new[] { "下半身", "腰", "pelvis", "hips", "hip", "root hips" } },
                { HumanBodyBones.Spine, new[] { "上半身", "spine", "upperbody", "spine lower" } },
                { HumanBodyBones.Chest, new[] { "上半身2", "上半身２", "chest", "spine1", "upperbody2", "spine upper" } },
                { HumanBodyBones.UpperChest, new[] { "上半身3", "上半身３", "upperchest", "spine2" } },
                { HumanBodyBones.Neck, new[] { "首", "neck", "head neck lower" } },
                { HumanBodyBones.Head, new[] { "頭", "head", "head neck upper" } },
                { HumanBodyBones.Jaw, new[] { "顎", "jaw" } }
            };
            foreach (bool left in new[] { true, false })
            {
                string jp = left ? "左" : "右", en = left ? "Left" : "Right", shortSide = left ? "l" : "r";
                Add(a, en + "Shoulder", jp + "肩", en + "Shoulder", "shoulder" + shortSide, "arm " + en + " clavicle");
                Add(a, en + "UpperArm", jp + "腕", en + "UpperArm", en + "Arm", "arm" + shortSide, "arm " + en + " shoulder");
                Add(a, en + "LowerArm", jp + "ひじ", jp + "肘", en + "LowerArm", en + "Elbow", "elbow" + shortSide, "arm " + en + " elbow");
                Add(a, en + "Hand", jp + "手首", en + "Hand", en + "Wrist", "wrist" + shortSide, "arm " + en + " wrist");
                Add(a, en + "UpperLeg", jp + "足", jp + "足D", en + "UpperLeg", en + "Leg", en + "Thigh", "leg" + shortSide, "leg " + en + " thigh");
                Add(a, en + "LowerLeg", jp + "ひざ", jp + "膝", jp + "ひざD", en + "LowerLeg", en + "Knee", "knee" + shortSide, "leg " + en + " knee");
                Add(a, en + "Foot", jp + "足首", jp + "足首D", en + "Foot", en + "Ankle", "ankle" + shortSide, "leg " + en + " ankle");
                Add(a, en + "Toes", jp + "つま先", jp + "足先EX", en + "Toes", en + "Toe", "toe" + shortSide);
                Add(a, en + "Eye", jp + "目", en + "Eye", "eye" + shortSide);
                string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
                string[] japanese = { "親指", "人指", "中指", "薬指", "小指" };
                string[] segments = { "Proximal", "Intermediate", "Distal" };
                for (int f = 0; f < fingers.Length; f++)
                    for (int s = 0; s < segments.Length; s++)
                    {
                        int number = f == 0 ? s : s + 1;
                        Add(a, en + fingers[f] + segments[s], jp + japanese[f] + number,
                            jp + japanese[f] + (char)('０' + number), en + fingers[f] + segments[s], en + fingers[f] + (s + 1));
                    }
            }
            return a;
        }
        private static void Add(Dictionary<HumanBodyBones, string[]> a, string human, params string[] names)
            => a[(HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), human)] = names;
        private static string Normalize(string name) => new string((name ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        public static Dictionary<HumanBodyBones, int> AutoMap(PmxModel model)
        {
            var result = new Dictionary<HumanBodyBones, int>();
            var used = new HashSet<int>();
            foreach (var pair in Aliases)
            {
                int found = -1;
                foreach (string alias in pair.Value)
                {
                    string key = Normalize(alias);
                    found = Array.FindIndex(model.Bones, b => Normalize(b.Name) == key || Normalize(b.EnglishName) == key);
                    if (found >= 0 && !used.Contains(found)) break;
                    found = -1;
                }
                if (found >= 0) { result[pair.Key] = found; used.Add(found); }
            }
            return result;
        }
        public static Dictionary<HumanBodyBones, Transform> Map(PmxModel model, Transform[] bones, ConversionOptions options)
        {
            var indices = AutoMap(model);
            foreach (var custom in options.BoneOverrides)
            {
                if (custom.PmxIndex == -1) { indices.Remove(custom.Human); continue; }
                if (custom.PmxIndex < 0 || custom.PmxIndex >= bones.Length) throw new InvalidDataException("手动骨骼映射索引越界。");
                indices[custom.Human] = custom.PmxIndex;
            }
            if (indices.Values.Distinct().Count() != indices.Count) throw new InvalidDataException("一个源骨骼不能映射到多个 Humanoid 插槽。");
            var result = indices.ToDictionary(x => x.Key, x => bones[x.Value]);
            foreach (var pair in result) pair.Value.name = pair.Key.ToString();
            return result;
        }
        public static void Repair(GameObject root, Dictionary<HumanBodyBones, Transform> map, ConversionReport report)
        {
            if (!map.ContainsKey(HumanBodyBones.Spine) || !map.ContainsKey(HumanBodyBones.Hips) || !map.ContainsKey(HumanBodyBones.Head))
                throw new InvalidDataException("无法找到 Hips / Spine / Head。请在骨骼映射中手动指定。");
            var spine = map[HumanBodyBones.Spine]; var head = map[HumanBodyBones.Head];
            if (!map.ContainsKey(HumanBodyBones.Chest)) Create(map, HumanBodyBones.Chest, Vector3.Lerp(spine.position, head.position, .45f), root, report);
            if (!map.ContainsKey(HumanBodyBones.Neck)) Create(map, HumanBodyBones.Neck, Vector3.Lerp(map[HumanBodyBones.Chest].position, head.position, .75f), root, report);
            foreach (bool left in new[] { true, false })
            {
                var shoulder = left ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder;
                var upperArm = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
                if (!map.ContainsKey(shoulder) && map.ContainsKey(upperArm))
                    Create(map, shoulder, Vector3.Lerp(map[HumanBodyBones.Chest].position, map[upperArm].position, .35f), root, report);
            }
            var missing = Required.Where(x => !map.ContainsKey(x)).ToArray();
            if (missing.Length > 0) throw new InvalidDataException("VRChat 必要骨骼缺失：" + string.Join(", ", missing));

            // Detach mapped bones first so split MMD chains cannot cause a reparenting cycle.
            foreach (var t in map.Values) t.SetParent(root.transform, true);
            Parent(map, HumanBodyBones.Spine, HumanBodyBones.Hips);
            Parent(map, HumanBodyBones.Chest, HumanBodyBones.Spine);
            Parent(map, HumanBodyBones.UpperChest, HumanBodyBones.Chest);
            var chest = map.ContainsKey(HumanBodyBones.UpperChest) ? HumanBodyBones.UpperChest : HumanBodyBones.Chest;
            Parent(map, HumanBodyBones.Neck, chest); Parent(map, HumanBodyBones.Head, HumanBodyBones.Neck);
            Parent(map, HumanBodyBones.Jaw, HumanBodyBones.Head);
            Parent(map, HumanBodyBones.LeftEye, HumanBodyBones.Head); Parent(map, HumanBodyBones.RightEye, HumanBodyBones.Head);
            foreach (string side in new[] { "Left", "Right" })
            {
                Func<string, HumanBodyBones> slot = n => (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + n);
                Parent(map, slot("Shoulder"), chest);
                Parent(map, slot("UpperArm"), slot("Shoulder")); Parent(map, slot("LowerArm"), slot("UpperArm")); Parent(map, slot("Hand"), slot("LowerArm"));
                Parent(map, slot("UpperLeg"), HumanBodyBones.Hips); Parent(map, slot("LowerLeg"), slot("UpperLeg")); Parent(map, slot("Foot"), slot("LowerLeg")); Parent(map, slot("Toes"), slot("Foot"));
                foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                {
                    Parent(map, slot(finger + "Proximal"), slot("Hand"));
                    Parent(map, slot(finger + "Intermediate"), slot(finger + "Proximal"));
                    Parent(map, slot(finger + "Distal"), slot(finger + "Intermediate"));
                }
                map[slot("LowerArm")].SetSiblingIndex(0); map[slot("Hand")].SetSiblingIndex(0);
                map[slot("LowerLeg")].SetSiblingIndex(0); map[slot("Foot")].SetSiblingIndex(0);
            }
            foreach (var pair in map)
                if (pair.Key != HumanBodyBones.Hips && !pair.Value.IsChildOf(map[HumanBodyBones.Hips]))
                    throw new InvalidDataException("骨骼 " + pair.Key + " 不在 Hips 子层级中。请检查指骨映射。");
            report.Infos.Add("已整理 Hips → Spine → Chest → Neck / Shoulders 层级和四肢顺序，保留辅助骨骼。");
        }
        private static void Create(Dictionary<HumanBodyBones, Transform> map, HumanBodyBones slot, Vector3 position, GameObject root, ConversionReport report)
        {
            var t = new GameObject(slot.ToString()).transform;
            t.SetParent(root.transform); t.position = position; map[slot] = t;
            report.Warnings.Add("补建 " + slot + " 骨骼，请检查实际关节位置。");
        }
        private static void Parent(Dictionary<HumanBodyBones, Transform> map, HumanBodyBones child, HumanBodyBones parent)
        {
            if (map.TryGetValue(child, out var c) && map.TryGetValue(parent, out var p)) c.SetParent(p, true);
        }
        public static void SetTPose(Dictionary<HumanBodyBones, Transform> map)
        {
            foreach (bool left in new[] { true, false })
            {
                var upper = map[left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm];
                var lower = map[left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm];
                var hand = map[left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand];
                var direction = left ? Vector3.left : Vector3.right;
                if ((lower.position - upper.position).sqrMagnitude < 1e-8f || (hand.position - lower.position).sqrMagnitude < 1e-8f)
                    throw new InvalidDataException("手臂关节位置重叠，无法构建 T Pose。");
                upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, direction) * upper.rotation;
                lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, direction) * lower.rotation;
            }
        }
        public static HumanDescription Describe(GameObject root, Dictionary<HumanBodyBones, Transform> map)
        {
            return new HumanDescription {
                human = map.Select(x => new HumanBone { boneName = x.Value.name, humanName = HumanTrait.BoneName[(int)x.Key], limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
                skeleton = root.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray(),
                upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f,
                armStretch = .05f, legStretch = .05f, feetSpacing = 0, hasTranslationDoF = false
            };
        }
    }
}
