using UnityEngine;

/// <summary>
/// 队伍颜色应用器：运行时根据 UnitView.state.teamType 自动染色 3D 模型和脚底光环。
/// 挂载在角色 Prefab 的根节点（UnitView 同级或子节点均可）。
/// </summary>
public class TeamColorApplicator : MonoBehaviour
{
    static readonly int ColorProp = Shader.PropertyToID("_Color");

    public UnitView unitView;

    void Start()
    {
        if (unitView == null)
            unitView = GetComponentInParent<UnitView>();
        ApplyTeamColor();
    }

    /// <summary>由 UnitView 在 Configure 阶段显式调用（可能早于 Start）</summary>
    public void ApplyTeamColor()
    {
        if (unitView == null || unitView.state == null) return;

        Color teamTint;
        Color ringColor;

        if (unitView.state.teamType == "defender")
        {
            teamTint = new Color(1f, 0.55f, 0.55f, 1f);   // 浅红染色（保留纹理细节）
            ringColor = new Color(1f, 0.15f, 0.15f, 0.45f); // 红色光环
        }
        else if (unitView.state.teamType == "challenger")
        {
            teamTint = new Color(0.55f, 0.65f, 1f, 1f);     // 浅蓝染色
            ringColor = new Color(0.15f, 0.25f, 1f, 0.45f); // 蓝色光环
        }
        else
        {
            return; // 中立 NPC 不染色
        }

        // 1. 对所有 Renderer 染色（SkinnedMeshRenderer + MeshRenderer）
        var mpb = new MaterialPropertyBlock();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            r.GetPropertyBlock(mpb);
            mpb.SetColor(ColorProp, teamTint);
            r.SetPropertyBlock(mpb);
        }

        // 2. 染色脚底 SelRing
        var root = unitView.transform;
        var selRing = root.Find("SelRing");
        if (selRing != null)
        {
            var sr = selRing.GetComponent<MeshRenderer>();
            if (sr != null && sr.sharedMaterial != null)
            {
                // 创建实例材质避免污染 prefab
                sr.material.color = ringColor;
            }
        }
    }
}
