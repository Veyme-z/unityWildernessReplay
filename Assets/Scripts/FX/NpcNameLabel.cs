using UnityEngine;

/// <summary>
/// NPC 头顶常驻名牌：黑色底板 + 白色名字（如"小贩"）。
/// Billboard 面向相机、永不消失；由 SceneBuilder.BuildNeutralNpc 挂载到小贩等中立单位。
/// 复用 TradeBadge 的字形/底板参数（NotoSansSC 中文 + 全宽/半宽自适应）。
/// </summary>
public class NpcNameLabel : MonoBehaviour
{
    /// <summary>在 NPC 头顶挂常驻名牌。label=显示文字，yPos=相对父节点的世界高度。</summary>
    public static NpcNameLabel Attach(Transform parent, string label, float yPos, float bgScale = 1f)
    {
        var go = new GameObject("NameLabel");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0, yPos, 0);

        // 黑色底板（半透明黑，Sprites/Default 无需光照）
        var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "Bg";
        bg.transform.SetParent(go.transform, false);
        var bgRend = bg.GetComponent<MeshRenderer>();
        bgRend.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        bgRend.sharedMaterial.color = new Color(0f, 0f, 0f, 0.6f);
        bgRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bgRend.receiveShadows = false;
        var bgCol = bg.GetComponent<Collider>();
        if (bgCol != null) Destroy(bgCol);

        // 名字文字（与 TradeBadge 同款参数，NotoSansSC 支持中文）
        var txtGo = new GameObject("Txt");
        txtGo.transform.SetParent(go.transform, false);
        txtGo.transform.localPosition = new Vector3(0, 0, -0.01f);
        var tm = txtGo.AddComponent<TextMesh>();
        tm.font = FxFactory.BuiltinFont();
        tm.fontSize = Mathf.RoundToInt(60 * bgScale);
        tm.characterSize = 0.04f * bgScale;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(1f, 1f, 1f, 1f);
        tm.text = label;
        // 防隐形（与 TradeBadge.ApplyText 一致）：legacy TextMesh 赋值动态字体后，
        // MeshRenderer 的材质可能残留旧贴图导致文字看不见；显式请求字形并重新绑定 font.material。
        if (tm.font != null)
        {
            tm.font.RequestCharactersInTexture(label, tm.fontSize, tm.fontStyle);
            var mr = tm.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.sharedMaterial = tm.font.material;
        }

        // 底板宽度按全宽/半宽自适应（中文=全宽，ASCII/数字=半宽）
        float full = 0f, half = 0f;
        foreach (char ch in label)
        {
            if (ch > 0x7F) full++;
            else half++;
        }
        float charWidth = 0.35f;   // 全宽字符世界宽度
        float padding = 0.3f;      // 左右总留白
        float minWidth = 0.6f;
        float w = Mathf.Max(minWidth, full * charWidth + half * charWidth * 0.5f + padding) * bgScale;
        float h = 0.3f * bgScale;
        bg.transform.localScale = new Vector3(w, h, 1f);

        go.AddComponent<Billboard>();

        return go.AddComponent<NpcNameLabel>();
    }
}
