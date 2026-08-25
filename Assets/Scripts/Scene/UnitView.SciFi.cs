// UnitView 的 SciFiHeroPBR 野兽视觉子模块（Partial Class）
// 职责：把野兽(11-14)的旧模型(骷髅/可爱机器人)运行时替换为 SciFiHeroPBR 模块化角色，
//       并按野兽类型组合不同"型号"（身体/头/手臂/腿/武器）+ 染色（红/蓝/绿/白），
//       动画用专用 SciFiBeast_AnimatorController（isMoving/onAttack/onDeath 参数）。
// 仅在 Resources/SciFiHeroPBR 资源存在时生效；加载失败自动保留原外观。

using System.Collections.Generic;
using UnityEngine;

public partial class UnitView
{
    /// <summary>野兽型号：各部件节点名 + 三色染色（_Color01 主色 / _Color02 副色 / _Color03 金属）。</summary>
    struct BeastVariant
    {
        public string body, head, arm, leg, weapon;
        public Color c1, c2, c3;
    }

    static readonly Dictionary<int, BeastVariant> BEAST_VARIANTS = new Dictionary<int, BeastVariant>
    {
        { 11, new BeastVariant { body = "Body1",    head = "head1", arm = "Arm1", leg = "Leg1", weapon = "AssaultRifle",
                                 c1 = new Color(0.85f, 0.20f, 0.18f), c2 = new Color(0.95f, 0.55f, 0.20f), c3 = new Color(0.45f, 0.45f, 0.45f) } },
        { 12, new BeastVariant { body = "Body2",    head = "Head2", arm = "Arm2", leg = "Leg2", weapon = "Shotgun",
                                 c1 = new Color(0.16f, 0.48f, 0.95f), c2 = new Color(0.25f, 0.85f, 0.92f), c3 = new Color(0.45f, 0.45f, 0.45f) } },
        { 13, new BeastVariant { body = "Body3",    head = "Head3", arm = "Arm3", leg = "Leg3", weapon = "SniperRifle",
                                 c1 = new Color(0.20f, 0.72f, 0.30f), c2 = new Color(0.62f, 0.90f, 0.30f), c3 = new Color(0.45f, 0.45f, 0.45f) } },
        { 14, new BeastVariant { body = "Body2",    head = "Head4", arm = "Arm1", leg = "Leg1", weapon = "AssaultRifle",
                                 c1 = new Color(0.92f, 0.92f, 0.92f), c2 = new Color(0.75f, 0.78f, 0.82f), c3 = new Color(0.40f, 0.40f, 0.42f) } },
    };

    static GameObject s_scifiBeastPrefab;   // 模块化角色（含武器/Animator/Avatar）
    static Material s_scifiBaseMat;         // PBRMaskTint 基础材质
    static RuntimeAnimatorController s_beastCtrl; // SciFiBeast_AnimatorController

    /// <summary>野兽体型（目标宽度，11→14 依次增大）：11 最小，14 最大。</summary>
    static readonly Dictionary<int, float> BEAST_SIZE = new Dictionary<int, float>
    {
        { 11, 1.30f },
        { 12, 1.45f },
        { 13, 1.65f },
        { 14, 1.85f },
    };

    /// <summary>把野兽外观替换为 SciFi 模块化角色：隐藏旧模型、按型号组合部件、配动画、染色。资源缺失时静默保留原外观。</summary>
    void SetupSciFiBeastVisual()
    {
        var visual = transform.Find("Visual");
        if (visual == null) return;

        if (s_scifiBeastPrefab == null)
            s_scifiBeastPrefab = Resources.Load<GameObject>("SciFiHeroPBR/Prefabs/AssaultRifle01");
        if (s_scifiBeastPrefab == null)
        {
            Debug.LogWarning("[UnitView] 未找到 Resources/SciFiHeroPBR 角色，野兽保持原外观");
            return;
        }

        // 隐藏旧模型（KayKit 骷髅 / 可爱机器人）
        for (int i = 0; i < visual.childCount; i++)
            visual.GetChild(i).gameObject.SetActive(false);

        // 实例化模块化角色（自带武器网格 + Animator + Humanoid Avatar）
        var inst = Object.Instantiate(s_scifiBeastPrefab, visual);
        inst.name = "SciFiBeast";
        var instT = inst.transform;
        instT.localPosition = Vector3.zero;
        instT.localRotation = Quaternion.identity;
        instT.localScale = Vector3.one;
        _body = instT;
        _sciFiVisual = true;

        // 朝向修正：实测模型正面朝 +Z（加 180° 会倒着走），这里保持 0° 对齐移动方向。
        // 保留 FacingFix 节点便于后续微调；不放根节点上，避免被 UnitView 静止冻结逻辑覆盖。
        var fix = new GameObject("FacingFix");
        fix.transform.SetParent(instT, false);
        fix.transform.localRotation = Quaternion.identity;
        // 把角色全部子节点移到 fix 下（保留 fix 自身），保持各自世界变换
        while (instT.childCount > 1)
            instT.GetChild(0).SetParent(fix.transform, true);

        // 按野兽类型组合不同型号（身体/头/手臂/腿/武器）
        BeastVariant v;
        if (BEAST_VARIANTS.TryGetValue(state.type, out v))
            ApplyBeastVariant(instT, v);

        // Animator：复用 prefab 自带 Animator（avatar 已配置），换为专用控制器
        _animator = inst.GetComponentInChildren<Animator>();
        if (_animator != null)
        {
            _animator.applyRootMotion = false;
            if (s_beastCtrl == null)
                s_beastCtrl = Resources.Load<RuntimeAnimatorController>("Animations/SciFiBeast_AnimatorController");
            if (s_beastCtrl == null)
                s_beastCtrl = Resources.Load<RuntimeAnimatorController>("Animations/Skeleton_AnimatorController");
            if (s_beastCtrl != null) _animator.runtimeAnimatorController = s_beastCtrl;
            _hasParams = true;
        }
        else
        {
            Debug.LogWarning("[UnitView.SciFi] 野兽 " + state.type + " 角色无 Animator，动画不可用");
        }

        // 蒙皮（供距离 LOD 烘焙：取首个活跃部件蒙皮）
        _skinned = inst.GetComponentInChildren<SkinnedMeshRenderer>(false);

        // 染色（所有活跃部件）
        ApplySciFiTint(inst, v);
    }

    /// <summary>按型号显式激活/隐藏各槽位部件（身体/头/手臂/腿/武器/背包）。</summary>
    static void ApplyBeastVariant(Transform root, BeastVariant v)
    {
        foreach (var sr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            string slot = SlotOf(sr.gameObject.name);
            if (slot == null) continue; // 非可换部件（肩垫等）保持默认
            bool active;
            switch (slot)
            {
                case "body":   active = sr.gameObject.name == v.body; break;
                case "head":   active = sr.gameObject.name == v.head; break;
                case "arm":    active = sr.gameObject.name == v.arm; break;
                case "leg":    active = sr.gameObject.name == v.leg; break;
                case "weapon": active = sr.gameObject.name == v.weapon; break;
                default:       active = false; break; // 背包等隐藏
            }
            sr.gameObject.SetActive(active);
        }
    }

    static string SlotOf(string name)
    {
        if (name.StartsWith("Body")) return "body";
        if (name.StartsWith("head") || name.StartsWith("Head")) return "head";
        if (name.StartsWith("Arm")) return "arm";
        if (name.StartsWith("Leg")) return "leg";
        if (name == "AssaultRifle" || name == "Pistol" || name == "Shotgun" || name == "SniperRifle") return "weapon";
        if (name.StartsWith("Backpack") || name == "BackPack") return "pack";
        return null;
    }

    /// <summary>给角色所有活跃部件替换为按型号配色的 PBRMaskTint 材质实例。</summary>
    void ApplySciFiTint(GameObject inst, BeastVariant v)
    {
        if (s_scifiBaseMat == null)
            s_scifiBaseMat = Resources.Load<Material>("SciFiHeroPBR/Materials/PBRMaskTint");
        if (s_scifiBaseMat == null) return;

        var mat = new Material(s_scifiBaseMat);
        mat.SetColor("_Color01", v.c1);
        mat.SetColor("_Color02", v.c2);
        mat.SetColor("_Color03", v.c3);
        foreach (var sr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!sr.gameObject.activeSelf) continue;
            sr.sharedMaterial = mat;
            // 与野兽渲染优化一致：关闭阴影（上百只野兽不产生额外渲染负担）
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
        }
    }

    /// <summary>获取枪口世界位置：优先取右手持枪骨骼，其次手部骨骼；失败返回 false。</summary>
    public bool TryGetMuzzle(out Vector3 worldPos)
    {
        if (_body != null)
        {
            var t = FindBoneInHierarchy(_body, "ArmPosition_Right");
            if (t == null) t = FindBoneInHierarchy(_body, "Hand_Right");
            if (t == null) t = FindBoneInHierarchy(_body, "AssaultRifle");
            if (t != null) { worldPos = t.position; return true; }
        }
        worldPos = Vector3.zero;
        return false;
    }

    static Transform FindBoneInHierarchy(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindBoneInHierarchy(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
