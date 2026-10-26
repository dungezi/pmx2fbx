using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
                { HumanBodyBones.Jaw, new[] { "顎", "jaw", "head jaw" } }
            };
            foreach (bool left in new[] { true, false })
            {
                string jp = left ? "左" : "右", en = left ? "Left" : "Right", shortSide = left ? "l" : "r";
                Add(a, en + "Shoulder", jp + "肩", en + "Shoulder", "shoulder" + shortSide, "arm " + en + " clavicle", "arm " + en + " shoulder 1");
                Add(a, en + "UpperArm", jp + "腕", en + "UpperArm", en + "Arm", "arm" + shortSide, "arm " + en + " shoulder", "arm " + en + " shoulder 2");
                Add(a, en + "LowerArm", jp + "ひじ", jp + "肘", en + "LowerArm", en + "Elbow", "elbow" + shortSide, "arm " + en + " elbow", "arm " + en + " elbow 1", en + "ForeArm");
                Add(a, en + "Hand", jp + "手首", en + "Hand", en + "Wrist", "wrist" + shortSide, "arm " + en + " wrist");
                Add(a, en + "UpperLeg", jp + "足", jp + "足D", en + "UpperLeg", en + "Leg", en + "Thigh", "leg" + shortSide, "leg " + en + " thigh", "leg " + en + " thigh 1", en + "UpLeg");
                Add(a, en + "LowerLeg", jp + "ひざ", jp + "膝", jp + "ひざD", en + "LowerLeg", en + "Knee", "knee" + shortSide, "leg " + en + " knee", en + "Leg");
                Add(a, en + "Foot", jp + "足首", jp + "足首D", en + "Foot", en + "Ankle", "ankle" + shortSide, "leg " + en + " ankle");
                Add(a, en + "Toes", jp + "つま先", jp + "足先EX", en + "Toes", en + "Toe", "toe" + shortSide, "leg " + en + " toes", en + "ToeBase");
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
                        var slot = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), en + fingers[f] + segments[s]);
                        a[slot] = a[slot].Concat(new[] { en + "Hand" + fingers[f] + (s + 1), jp + (f == 1 ? "人差指" : japanese[f]) + number,
                            fingers[f] + (s + 1) + shortSide, shortSide + fingers[f] + (s + 1),
                            fingers[f] + "0" + (s + 1) + shortSide, "f" + fingers[f].ToLowerInvariant() + "0" + (s + 1) + shortSide,
                            shortSide + "Finger" + f + (s == 0 ? "" : s.ToString()) }).ToArray();
                    }
                foreach (string part in new[] { "Shoulder", "UpperArm", "LowerArm", "Hand", "UpperLeg", "LowerLeg", "Foot", "Toes", "Eye" })
                {
                    var slot = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), en + part);
                    var extra = new List<string> { part + shortSide, shortSide + part };
                    if (part == "UpperArm") extra.AddRange(new[] { "upper_arm" + shortSide });
                    if (part == "LowerArm") extra.AddRange(new[] { "forearm" + shortSide, shortSide + "forearm" });
                    if (part == "UpperLeg") extra.AddRange(new[] { "thigh" + shortSide, shortSide + "thigh" });
                    if (part == "LowerLeg") extra.AddRange(new[] { "calf" + shortSide, shortSide + "calf", "shin" + shortSide });
                    if (part == "Shoulder") extra.AddRange(new[] { "clavicle" + shortSide, shortSide + "clavicle" });
                    a[slot] = a[slot].Concat(extra).ToArray();
                }
            }
            return a;
        }
        private static void Add(Dictionary<HumanBodyBones, string[]> a, string human, params string[] names)
            => a[(HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), human)] = names;
        private static string Normalize(string name)
        {
            name = (name ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            int separator = Math.Max(name.LastIndexOf(':'), name.LastIndexOf('|'));
            if (separator >= 0) name = name.Substring(separator + 1);
            name = new string(name.Where(char.IsLetterOrDigit).ToArray());
            foreach (string prefix in new[] { "valvebipedbip001", "valvebipedbip01", "mixamorig", "bip001", "bip01", "jbipc", "jbip", "def" })
                if (name.StartsWith(prefix, StringComparison.Ordinal)) { name = name.Substring(prefix.Length); break; }
            return name;
        }

        public static Dictionary<HumanBodyBones, int> AutoMap(PmxModel model)
            => AutoMap(model, out _);
        private static Dictionary<HumanBodyBones, int> AutoMap(PmxModel model, out List<HumanBodyBones> inferred)
        {
            var result = new Dictionary<HumanBodyBones, int>();
            var used = new HashSet<int>();
            // VRM's declared humanoid is authoritative. Do not fill optional slots by guessing names.
            if (model.HumanBones.Count > 0)
            {
                foreach (var pair in model.HumanBones)
                {
                    if (!Enum.TryParse(pair.Key, true, out HumanBodyBones slot) || slot == HumanBodyBones.LastBone) continue;
                    if (pair.Value < 0 || pair.Value >= model.Bones.Length || !used.Add(pair.Value))
                        throw new InvalidDataException("文件内的人形骨骼定义重复或越界。");
                    result.Add(slot, pair.Value);
                }
                inferred = new List<HumanBodyBones>(); return result;
            }
            var candidates = new Dictionary<HumanBodyBones, Dictionary<int, int>>();
            var primaryNames = model.Bones.Select(b => Normalize(b.Name)).ToArray();
            var englishNames = model.Bones.Select(b => Normalize(b.EnglishName)).ToArray();
            foreach (var pair in Aliases)
            {
                var choices = new Dictionary<int, int>(); candidates[pair.Key] = choices;
                var names = pair.Value;
                // MMD thumbs use 0/1/2, while many exported rigs use 1/2/3.
                if (pair.Key.ToString().Contains("Thumb"))
                {
                    string jp = pair.Key.ToString().StartsWith("Left") ? "左親指" : "右親指";
                    bool zero = primaryNames.Contains(Normalize(jp + "0")) || englishNames.Contains(Normalize(jp + "0"));
                    if (!zero)
                    {
                        int segment = pair.Key.ToString().EndsWith("Proximal") ? 1 : pair.Key.ToString().EndsWith("Intermediate") ? 2 : 3;
                        names = names.Where(n => !n.StartsWith(jp, StringComparison.Ordinal)).Concat(new[] { jp + segment }).ToArray();
                    }
                }
                var normalized = names.Select(Normalize).ToArray();
                for (int i = 0; i < model.Bones.Length; i++)
                    for (int a = 0; a < names.Length; a++)
                        if (primaryNames[i] == normalized[a] || englishNames[i] == normalized[a])
                        { choices[i] = 100 - Math.Min(a, 30); break; }
            }
            // Resolve in rounds: an established ancestor/descendant chain disambiguates duplicate names.
            // Equal candidates remain unmapped rather than depending on source array ordering.
            for (int round = 0; round < 4; round++)
            foreach (var pair in candidates.OrderBy(p => (int)p.Key))
            {
                if (result.ContainsKey(pair.Key)) continue;
                var scored = pair.Value.Where(p => !used.Contains(p.Key)).Select(p => new { Index=p.Key,
                    Score=p.Value + HierarchyScore(model, pair.Key, p.Key, result, candidates) }).OrderByDescending(p => p.Score).ToArray();
                if (scored.Length == 0 || scored[0].Score < 0 || scored.Length > 1 && scored[0].Score == scored[1].Score) continue;
                result[pair.Key] = scored[0].Index; used.Add(scored[0].Index);
            }
            inferred = new List<HumanBodyBones>();
            if (model.Format == "XPS") InferXpsTorso(model, result, used, inferred);
            return result;
        }
        private static int HierarchyScore(PmxModel model, HumanBodyBones slot, int index,
            Dictionary<HumanBodyBones,int> map, Dictionary<HumanBodyBones, Dictionary<int,int>> choices)
        {
            int score = 0;
            foreach (var edge in Edges())
            {
                if (edge.Key != slot && edge.Value != slot) continue;
                bool child = edge.Key == slot; var other = child ? edge.Value : edge.Key;
                // MMD upper/lower body are intentionally split; Repair connects those roots.
                if (edge.Key == HumanBodyBones.Spine && edge.Value == HumanBodyBones.Hips && model.Format != "VRM") continue;
                if (map.TryGetValue(other, out int known))
                    score += (child ? Ancestor(model, known, index) : Ancestor(model, index, known)) && known != index ? 80 : -300;
                else if (choices.TryGetValue(other, out var list) && list.Count > 0)
                    score += list.Keys.Any(i => i != index && (child ? Ancestor(model, i, index) : Ancestor(model, index, i))) ? 35 : -130;
            }
            return score;
        }
        private static IEnumerable<KeyValuePair<HumanBodyBones, HumanBodyBones>> Edges()
        {
            yield return Edge(HumanBodyBones.Spine, HumanBodyBones.Hips);
            yield return Edge(HumanBodyBones.Chest, HumanBodyBones.Spine);
            yield return Edge(HumanBodyBones.UpperChest, HumanBodyBones.Chest);
            yield return Edge(HumanBodyBones.Neck, HumanBodyBones.Chest);
            yield return Edge(HumanBodyBones.Head, HumanBodyBones.Neck);
            yield return Edge(HumanBodyBones.Jaw, HumanBodyBones.Head);
            foreach (string side in new[] { "Left", "Right" })
            {
                Func<string, HumanBodyBones> s = n => (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side+n);
                yield return Edge(s("Eye"), HumanBodyBones.Head);
                yield return Edge(s("Shoulder"), HumanBodyBones.Chest);
                yield return Edge(s("UpperArm"), s("Shoulder")); yield return Edge(s("LowerArm"), s("UpperArm")); yield return Edge(s("Hand"), s("LowerArm"));
                yield return Edge(s("UpperLeg"), HumanBodyBones.Hips); yield return Edge(s("LowerLeg"), s("UpperLeg")); yield return Edge(s("Foot"), s("LowerLeg")); yield return Edge(s("Toes"), s("Foot"));
                foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                {
                    yield return Edge(s(finger+"Proximal"), s("Hand")); yield return Edge(s(finger+"Intermediate"), s(finger+"Proximal")); yield return Edge(s(finger+"Distal"), s(finger+"Intermediate"));
                }
            }
        }
        private static KeyValuePair<HumanBodyBones,HumanBodyBones> Edge(HumanBodyBones child, HumanBodyBones parent)
            => new KeyValuePair<HumanBodyBones,HumanBodyBones>(child,parent);
        private static bool Ancestor(PmxModel model, int ancestor, int child)
        {
            for (int steps = 0; child >= 0 && child < model.Bones.Length && steps < model.Bones.Length; steps++)
            { if (child == ancestor) return true; child = model.Bones[child].Parent; }
            return false;
        }
        private static void InferXpsTorso(PmxModel model, Dictionary<HumanBodyBones, int> map, HashSet<int> used, List<HumanBodyBones> inferred)
        {
            // Infer only along the shared torso chain of named head and bilateral limbs.
            // Exact names remain authoritative; accessory/duplicate rigs cannot win by suffix.
            if (!map.TryGetValue(HumanBodyBones.Hips, out int hips) || !map.TryGetValue(HumanBodyBones.Head, out int head)
                || !map.TryGetValue(HumanBodyBones.LeftUpperArm, out int left) || !map.TryGetValue(HumanBodyBones.RightUpperArm, out int right)
                || !map.TryGetValue(HumanBodyBones.LeftUpperLeg, out int leftLeg) || !map.TryGetValue(HumanBodyBones.RightUpperLeg, out int rightLeg)
                || !Ancestor(model, hips, head) || !Ancestor(model, hips, leftLeg) || !Ancestor(model, hips, rightLeg)) return;
            if (map.TryGetValue(HumanBodyBones.LeftShoulder, out int ls)) left = ls;
            if (map.TryGetValue(HumanBodyBones.RightShoulder, out int rs)) right = rs;
            int shared = -1;
            for (int node = model.Bones[head].Parent, steps = 0; node >= 0 && steps < model.Bones.Length; node = model.Bones[node].Parent, steps++)
                if (Ancestor(model, node, left) && Ancestor(model, node, right)) { shared = node; break; }
            if (shared < 0 || shared == hips || !Ancestor(model, hips, shared)) return;
            Action<HumanBodyBones, int> assign = (slot, index) => {
                if (!map.ContainsKey(slot) && index >= 0 && !used.Contains(index))
                { map[slot] = index; used.Add(index); inferred.Add(slot); }
            };
            assign(HumanBodyBones.Chest, shared);
            if (!map.TryGetValue(HumanBodyBones.Chest, out int chest) || !Ancestor(model, hips, chest) || !Ancestor(model, chest, head)) return;
            var chain = new List<int>();
            for (int node = model.Bones[chest].Parent, steps = 0; node >= 0 && node != hips && steps < model.Bones.Length; node = model.Bones[node].Parent, steps++) chain.Add(node);
            chain.Reverse();
            // A unique unused, non-coincident joint avoids choosing arbitrary helper chains.
            var spine = chain.Where(i => !used.Contains(i) && Separated(model.Bones[i].Position, model.Bones[hips].Position)
                && Separated(model.Bones[i].Position, model.Bones[chest].Position)).ToArray();
            if (spine.Length == 1) assign(HumanBodyBones.Spine, spine[0]);
            int neck = model.Bones[head].Parent;
            if (neck != chest && Ancestor(model, chest, neck) && Separated(model.Bones[neck].Position, model.Bones[head].Position))
                assign(HumanBodyBones.Neck, neck);
        }
        private static bool Separated(Float3 a, Float3 b)
            => (double)(a.X - b.X) * (a.X - b.X) + (double)(a.Y - b.Y) * (a.Y - b.Y) + (double)(a.Z - b.Z) * (a.Z - b.Z) > 1e-12;
        public static Dictionary<HumanBodyBones, Transform> Map(PmxModel model, Transform[] bones, ConversionOptions options, ConversionReport report = null)
        {
            var indices = AutoMap(model, out var inferred);
            foreach (var custom in options.BoneOverrides)
            {
                if (!Enum.IsDefined(typeof(HumanBodyBones), custom.Human) || custom.Human == HumanBodyBones.LastBone)
                    throw new InvalidDataException("手动骨骼插槽无效。");
                if (custom.PmxIndex == -1) { indices.Remove(custom.Human); continue; }
                if (custom.PmxIndex < 0 || custom.PmxIndex >= bones.Length) throw new InvalidDataException("手动骨骼映射索引越界。");
                indices[custom.Human] = custom.PmxIndex;
            }
            if (indices.Values.Distinct().Count() != indices.Count) throw new InvalidDataException("一个源骨骼不能映射到多个 Humanoid 插槽。");
            inferred.RemoveAll(h => options.BoneOverrides.Any(o => o.Human == h));
            if (inferred.Count > 0 && report != null)
                report.Warnings.Add("XPS 部分躯干名称未识别，已按头部、双臂和髋部的共同父子层级补全映射：" + string.Join(", ", inferred) + "。请检查关节位置和动作。");
            var result = indices.ToDictionary(x => x.Key, x => bones[x.Value]);
            foreach (var pair in indices)
                report?.Infos.Add("骨骼 " + pair.Key + " = [" + pair.Value + "] " + model.Bones[pair.Value].Name);
            foreach (var slot in Aliases.Keys.Where(h => !indices.ContainsKey(h)))
                if (Required.Contains(slot)) report?.Warnings.Add("骨骼 " + slot + " 未能唯一、可靠地识别；请检查映射。");
            ValidateMapping(model, indices);
            foreach (var pair in result) pair.Value.name = pair.Key.ToString();
            return result;
        }
        public static void Repair(GameObject root, Dictionary<HumanBodyBones, Transform> map, ConversionReport report)
        {
            var unmapped = new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Head }.Where(b => !map.ContainsKey(b)).ToArray();
            if (unmapped.Length > 0)
                throw new InvalidDataException("VRChat 骨骼尚未映射：" + string.Join(", ", unmapped) + "。请在骨骼映射中手动指定；仅转换 FBX 时可取消 VRChat PC 适配。");
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

            // Retain valid helper/twist chains; only repair missing semantic ancestry.
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
                FirstBranch(map[slot("UpperArm")], map[slot("LowerArm")]); FirstBranch(map[slot("LowerArm")], map[slot("Hand")]);
                FirstBranch(map[slot("UpperLeg")], map[slot("LowerLeg")]); FirstBranch(map[slot("LowerLeg")], map[slot("Foot")]);
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
            if (map.TryGetValue(child, out var c) && map.TryGetValue(parent, out var p) && !c.IsChildOf(p))
            {
                if (p.IsChildOf(c)) throw new InvalidDataException("骨骼映射形成反向父子链：" + parent + " / " + child);
                c.SetParent(p, true);
            }
        }
        private static void ValidateMapping(PmxModel model, Dictionary<HumanBodyBones,int> map)
        {
            var parents = Edges().ToDictionary(e => e.Key, e => e.Value);
            foreach (var edge in Edges())
            {
                if (!map.TryGetValue(edge.Key, out int child)) continue;
                var parentSlot = edge.Value;
                while (!map.ContainsKey(parentSlot) && parents.TryGetValue(parentSlot, out var next)) parentSlot = next;
                if (!map.TryGetValue(parentSlot, out int parent)) continue;
                if (Ancestor(model, child, parent)) throw new InvalidDataException("骨骼映射父子方向错误：" + edge.Value + " → " + edge.Key);
                if (edge.Key == HumanBodyBones.Spine && model.Format != "VRM") continue;
                if (!Ancestor(model, parent, child)) throw new InvalidDataException("骨骼映射不属于同一肢体链：" + edge.Value + " → " + edge.Key + "。请修正映射。");
                if (!Separated(model.Bones[parent].Position, model.Bones[child].Position))
                    throw new InvalidDataException("骨骼关节重叠：" + edge.Value + " / " + edge.Key);
            }
            foreach (string side in new[] { "Left", "Right" })
            {
                var fingerSlots = map.Keys.Where(h => h.ToString().StartsWith(side) && new[] { "Thumb", "Index", "Middle", "Ring", "Little" }.Any(f => h.ToString().Contains(f))).ToArray();
                foreach (var h in fingerSlots)
                {
                    string name = h.ToString();
                    if (!name.EndsWith("Proximal") && !map.ContainsKey((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), name.Replace(name.EndsWith("Distal") ? "Distal" : "Intermediate", "Proximal"))))
                        throw new InvalidDataException("指骨链不完整：" + h + "。请补全或取消该指骨映射。");
                }
            }
        }
        private static void FirstBranch(Transform parent, Transform descendant)
        {
            for (var current = descendant; current && current != parent; current = current.parent)
                current.SetSiblingIndex(0);
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
