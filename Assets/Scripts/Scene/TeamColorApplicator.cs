using UnityEngine;

public class TeamColorApplicator : MonoBehaviour
{
    public UnitView unitView;

    void Start()
    {
        if (unitView == null)
            unitView = GetComponentInParent<UnitView>();
        ApplyTeamColor();
    }

    public void ApplyTeamColor()
    {
        if (unitView == null || unitView.state == null) return;

        Color ringColor;
        if (unitView.state.teamType == "defender")
            ringColor = new Color(1f, 0.176f, 0.333f, 0.8f);   // #FF2D55 霓虹红
        else if (unitView.state.teamType == "challenger")
            ringColor = new Color(0f, 0.478f, 1f, 0.8f);       // #007AFF 霓虹蓝
        else
            return;

        var selRing = unitView.transform.Find("SelRing");
        if (selRing != null)
        {
            selRing.gameObject.SetActive(true);
            var sr = selRing.GetComponent<MeshRenderer>();
            if (sr != null)
            {
                // 颜色直接烘焙到贴图像素中，不依赖 shader _Color
                var coloredTex = MatLib.CreateRingTex(ringColor, 128);
                // Sprites/Default 在这个项目中已验证可用
                sr.sharedMaterial = new Material(MatLib.Shader2D);
                sr.sharedMaterial.mainTexture = coloredTex;
                sr.sharedMaterial.color = Color.white; // 避免 Sprites/Default 二次染色
                sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sr.receiveShadows = false;
            }
        }
    }
}
