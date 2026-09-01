using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>某一天的小贩矿石回收价快照（按天聚合，130 回合 = 1 天）。</summary>
[System.Serializable]
public class DailyPrice
{
    public int day;
    public int stonePrice;
    public int copperPrice;
    public int ironPrice;
}

/// <summary>资源价格折线图数据：由回放 rounds 里的 vendorShopList 按天聚合得到。</summary>
public class PriceChartData
{
    public List<DailyPrice> dailyPrices = new List<DailyPrice>();
    public int windowStartDay = -1;   // start.vendorShopPriceChange.date.startDay（-1 = 无）
    public int windowEndDay = -1;     // start.vendorShopPriceChange.date.stopDay（-1 = 无）

    /// <summary>
    /// 遍历 rounds，按天聚合出每日价格。
    /// 规则：价格逐回合随 vendorShopList 更新；某天结束时（进入下一天的首轮 / 最后一轮）把当天结束时的价格快照一次。
    /// 因此每天的价格 = 当天最后一轮执行到的价格（受世界新闻波动），carry-forward 到后续天。
    /// 若回放含 vendorShopPriceChange.date.stopDay，则只保留 day ≤ stopDay 的天（价格波动期，X 轴标签显示到 stopDay 为止）。
    /// </summary>
    public static PriceChartData FromReplay(ReplayData data)
    {
        var chart = new PriceChartData();
        if (data == null || data.rounds == null || data.rounds.Count == 0) return chart;
        if (data.start != null)
        {
            chart.windowStartDay = data.start.priceChangeStartDay;
            chart.windowEndDay = data.start.priceChangeEndDay;
        }

        int stone = 0, copper = 0, iron = 0;
        int lastDay = -1;

        foreach (var rd in data.rounds)
        {
            int day = StateEngine.DayOf(rd.round);

            // 新的一天开始 → 先把上一天结束时的价格快照下来（此刻 stone/copper/iron 还是上一天最后一轮的值）
            if (day != lastDay && lastDay >= 1)
                chart.dailyPrices.Add(new DailyPrice
                {
                    day = lastDay,
                    stonePrice = stone,
                    copperPrice = copper,
                    ironPrice = iron
                });

            // 更新当天价格（vendorShopList 固定 3 项：stone / iron / copper；缺失的项 carry-forward）
            if (rd.vendorShopList != null)
                foreach (var item in rd.vendorShopList)
                {
                    string n = (item.name ?? "").ToLowerInvariant();
                    if (n == "stone") stone = item.price;
                    else if (n == "iron") iron = item.price;
                    else if (n == "copper") copper = item.price;
                }

            lastDay = day;
        }

        // 最后一天（可能未走满 130 回合，也补一份当天结束快照）
        if (lastDay >= 1)
            chart.dailyPrices.Add(new DailyPrice
            {
                day = lastDay,
                stonePrice = stone,
                copperPrice = copper,
                ironPrice = iron
            });

        // 若存在 stopDay 波动窗口 → 只保留 day ≤ stopDay 的天（X 轴显示到波动结束为止）
        if (chart.windowEndDay > 0)
        {
            var filtered = new List<DailyPrice>();
            foreach (var dp in chart.dailyPrices)
                if (dp.day <= chart.windowEndDay) filtered.Add(dp);
            chart.dailyPrices = filtered;
        }

        return chart;
    }
}

/// <summary>
/// 资源价格折线图卡片：右上角（任务面板下方）展示 石头/铜/铁 的每日小贩回收价走势。
/// 与 TaskPanelController 一致：纯代码搭建 UGUI（Canvas + RawImage 展示程序化生成的折线 Texture2D），无需 prefab。
/// 颜色：石头=灰、铜=橙、铁=红。
/// </summary>
public class PriceChartCard : MonoBehaviour
{
    [Header("UI 引用（代码创建时赋值）")]
    [SerializeField] Text _title;
    [SerializeField] RawImage _chartImage;
    [SerializeField] Text _emptyLabel;  // 无价格数据提示

    // 三条折线颜色（与图例一致）：石头用紫色（原灰色易与白色坐标轴混淆）
    static readonly Color STONE  = new Color(0.72f, 0.38f, 0.95f);
    static readonly Color COPPER = new Color(0.92f, 0.60f, 0.22f);
    static readonly Color IRON   = new Color(0.95f, 0.35f, 0.30f);

    // 图表纹理尺寸（2 倍渲染 → RawImage 缩小显示时双线性平滑抗锯齿）
    const int TEX_W = 560;
    const int TEX_H = 320;
    // 绘图区边距（纹理像素）：左/右留白给 Y 轴、顶部留白、底部留白给 X 轴天数标签
    const int PAD_LEFT = 24;
    const int PAD_RIGHT = 24;
    const int PAD_TOP = 18;
    const int PAD_BOTTOM = 46;

    /// <summary>创建资源价格卡片。返回 null 表示回放无 round 数据（不创建）。</summary>
    public static PriceChartCard Create(ReplayPlayer player)
    {
        if (player == null || player.data == null || player.data.rounds == null || player.data.rounds.Count == 0)
            return null;

        var chartData = PriceChartData.FromReplay(player.data);
        if (chartData.dailyPrices.Count == 0) return null;

        var font = UiFonts.Get();

        // ── 独立 Canvas（左上角任务面板 215/216 之上无重叠，此处仅需高于事件日志） ──
        var canvasGo = new GameObject("PriceChartCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 211;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // ── 面板（右上角；旧任务面板已移至左上角，此处不再有上方任务面板） ──
        var panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        var rt = panelGo.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.anchoredPosition = new Vector2(-10, -256);
        rt.sizeDelta = new Vector2(340, 240);   // 加宽留出 Y 轴数值标签
        var panelImg = panelGo.AddComponent<Image>();
        panelImg.color = new Color(0.102f, 0.102f, 0.118f, 0.85f);
        panelImg.raycastTarget = false;

        // ── 标题 ──
        var title = MakeText(panelGo.transform, "Title", "资源价格走势", font, 15,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -6), new Vector2(280, 22),
            Color.white, TextAnchor.MiddleCenter);

        // ── 图表区（RawImage + Y 轴标签） ──
        var chartGo = new GameObject("Chart");
        chartGo.transform.SetParent(panelGo.transform, false);
        var crt = chartGo.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.5f, 0.5f);
        crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = new Vector2(0, 8);
        crt.sizeDelta = new Vector2(280, 168);
        var raw = chartGo.AddComponent<RawImage>();
        raw.raycastTarget = false;

        // ── 无数据提示（默认隐藏） ──
        var emptyLabel = MakeText(panelGo.transform, "Empty", "本回放不含小贩回收价数据", font, 13,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(280, 80),
            new Color(0.7f, 0.68f, 0.62f), TextAnchor.MiddleCenter);
        emptyLabel.gameObject.SetActive(false);

        // ── 图例（底部：色块 + 名称） ──
        MakeLegendItem(panelGo.transform, "石头", STONE, -90, font);
        MakeLegendItem(panelGo.transform, "铜", COPPER, 0, font);
        MakeLegendItem(panelGo.transform, "铁", IRON, 90, font);

        var ctrl = panelGo.AddComponent<PriceChartCard>();
        ctrl._title = title;
        ctrl._chartImage = raw;
        ctrl._emptyLabel = emptyLabel;
        ctrl.Render(chartData);
        return ctrl;
    }

    /// <summary>渲染折线图：无价格数据时显示提示文字。有数据时叠加 X 轴天数 + Y 轴数值标签。</summary>
    void Render(PriceChartData data)
    {
        if (_chartImage == null) return;

        int maxPrice, yStep, dayStep;
        var tex = RenderChart(data.dailyPrices, TEX_W, TEX_H, out maxPrice, out yStep, out dayStep);
        if (maxPrice <= 0)
        {
            if (_chartImage != null) _chartImage.gameObject.SetActive(false);
            if (_emptyLabel != null) _emptyLabel.gameObject.SetActive(true);
            return;
        }

        _chartImage.texture = tex;
        AddAxisLabels(data.dailyPrices, maxPrice, yStep, dayStep);
    }

    /// <summary>在图表上叠加 Y 轴数值标签（每条网格线）+ X 轴天数标签（步进 dayStep）。
    /// 位置按纹理像素 → RawImage 显示坐标换算（scaleX/scaleY = 显示尺寸 / 纹理尺寸）。</summary>
    void AddAxisLabels(List<DailyPrice> prices, int maxPrice, int yStep, int dayStep)
    {
        if (_chartImage == null || prices == null || prices.Count == 0) return;
        var rt = _chartImage.rectTransform;
        float sx = rt.rect.width / TEX_W;
        float sy = rt.rect.height / TEX_H;
        int plotW = TEX_W - PAD_LEFT - PAD_RIGHT;
        int plotH = TEX_H - PAD_TOP - PAD_BOTTOM;
        Font font = UiFonts.Get();
        // 与标题"资源价格走势"一致：NotoSansSC / 15 / 白色（用户可见性要求）
        const int labelFontSize = 15;
        var labelColor = Color.white;

        // Y 轴数值标签（含 0 和 max），贴在绘图区左侧。
        // 关键：锚点用图表中心 (0.5,0.5)，anchoredPosition 才按中心坐标算（与 lx/ly 一致），
        // 否则锚在右缘/上缘会把标签放到图表内部压住折线 → 看不见。
        for (int v = 0; v <= maxPrice; v += yStep)
        {
            float py = PAD_BOTTOM + (float)v / maxPrice * plotH;
            float ly = (py - TEX_H * 0.5f) * sy;
            float lx = (PAD_LEFT - TEX_W * 0.5f) * sx - 6f;
            MakeText(rt, "Y_" + v, v.ToString(), font, labelFontSize,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(lx, ly), new Vector2(40, 22), labelColor, TextAnchor.MiddleRight);
        }

        // X 轴天数标签（步进 dayStep；最后一个数据点也补一个），锚点同为图表中心
        int n = prices.Count;
        for (int d = 0; d < n; d += dayStep)
            AddXDayLabel(rt, prices[d], d, n, plotW, sx, sy, font, labelColor);
        if (n > 1 && (n - 1) % dayStep != 0)
            AddXDayLabel(rt, prices[n - 1], n - 1, n, plotW, sx, sy, font, labelColor);
    }

    void AddXDayLabel(RectTransform rt, DailyPrice p, int index, int count, int plotW, float sx, float sy, Font font, Color color)
    {
        float px = PAD_LEFT + (count > 1 ? (float)index / (count - 1) : 0.5f) * plotW;
        float lx = (px - TEX_W * 0.5f) * sx;
        float ly = (PAD_BOTTOM - TEX_H * 0.5f) * sy - 13f;   // 绘图区底边之下
        MakeText(rt, "X_" + p.day, p.day.ToString(), font, 15,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f),
            new Vector2(lx, ly), new Vector2(52, 22), color, TextAnchor.MiddleCenter);
    }

    // ---------- 图表渲染（Texture2D 程序化绘制） ----------

    /// <summary>把每日价格画成三条折线。maxPrice = Y 轴最大值（向上取整到 5/10 的整数倍）；
    /// yStep/dayStep = Y 轴数值网格步进 / X 轴天数标签步进（供外层放坐标轴标签）；无价格数据时返回 null 且 maxPrice=0。</summary>
    static Texture2D RenderChart(List<DailyPrice> prices, int texW, int texH, out int maxPrice, out int yStep, out int dayStep)
    {
        int maxVal = 0;
        foreach (var p in prices)
        {
            if (p.stonePrice > maxVal) maxVal = p.stonePrice;
            if (p.copperPrice > maxVal) maxVal = p.copperPrice;
            if (p.ironPrice > maxVal) maxVal = p.ironPrice;
        }
        if (maxVal <= 0)
        {
            maxPrice = 0; yStep = 1; dayStep = 1;
            return null;
        }
        maxPrice = NiceCeil(maxVal);
        yStep = NiceYStep(maxPrice);
        dayStep = NiceDayStep(prices.Count);

        var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        // 清空为透明
        var clear = new Color(0f, 0f, 0f, 0f);
        var px = tex.GetPixels();
        for (int i = 0; i < px.Length; i++) px[i] = clear;
        tex.SetPixels(px);

        int plotW = texW - PAD_LEFT - PAD_RIGHT;
        int plotH = texH - PAD_TOP - PAD_BOTTOM;

        var grid = new Color(1f, 1f, 1f, 0.14f);
        var axis = new Color(1f, 1f, 1f, 0.8f);    // 坐标轴主线：亮、明显
        var tick = new Color(1f, 1f, 1f, 0.55f);   // 刻度线
        int n = prices.Count;

        // 横向网格线（每 yStep 一条，淡）
        for (int v = yStep; v < maxPrice; v += yStep)
        {
            int y = PAD_BOTTOM + Mathf.RoundToInt((float)v / maxPrice * plotH);
            DrawHLine(tex, y, PAD_LEFT, PAD_LEFT + plotW, grid);
        }
        // 纵向网格线（每天 dayStep 一条，淡）；跳过最后一天（与右边框位置重合，避免看似右边线）
        if (n > 1)
            for (int d = 0; d < n; d += dayStep)
            {
                if (d == n - 1) continue;
                int x = PAD_LEFT + Mathf.RoundToInt((float)d / (n - 1) * plotW);
                DrawVLine(tex, x, PAD_BOTTOM, PAD_BOTTOM + plotH, grid);
            }

        // 坐标轴主线：X 轴（底横线）+ Y 轴（左竖线）。
        // 纹理 560px 缩到屏幕 ~168px（总缩放 ~0.3），2px 细线缩放后亚像素被过滤看不见，
        // 因此轴线加粗 8px（→ 屏幕 ~2.4px）且伸向留白区，不遮挡折线。
        // 坐标轴仅保留：X 轴（底部横线）+ Y 轴（左侧竖线），无顶/右边框
        for (int k = 0; k < 8; k++)
            DrawHLine(tex, PAD_BOTTOM - k, PAD_LEFT, PAD_LEFT + plotW, axis);   // X 轴向下 8px
        for (int k = 0; k < 8; k++)
            DrawVLine(tex, PAD_LEFT - k, PAD_BOTTOM, PAD_BOTTOM + plotH, axis); // Y 轴向左 8px

        // Y 轴刻度：从轴线向绘图区内伸 6px 短横线（标记数值位置）
        for (int v = 0; v <= maxPrice; v += yStep)
        {
            int y = PAD_BOTTOM + Mathf.RoundToInt((float)v / maxPrice * plotH);
            DrawHLine(tex, y, PAD_LEFT, PAD_LEFT + 6, tick);
        }
        // X 轴刻度：从轴线向绘图区内伸 6px 短竖线（标记天数位置）；跳过最后一天（避免右边竖线感）
        if (n > 1)
            for (int d = 0; d < n; d += dayStep)
            {
                if (d == n - 1) continue;
                int x = PAD_LEFT + Mathf.RoundToInt((float)d / (n - 1) * plotW);
                DrawVLine(tex, x, PAD_BOTTOM, PAD_BOTTOM + 6, tick);
            }

        // 三条折线（粗 4px，缩放到显示尺寸后约 2px）
        DrawSeries(tex, prices, p => p.stonePrice, STONE, PAD_LEFT, PAD_BOTTOM, plotW, plotH, maxPrice, 4);
        DrawSeries(tex, prices, p => p.copperPrice, COPPER, PAD_LEFT, PAD_BOTTOM, plotW, plotH, maxPrice, 4);
        DrawSeries(tex, prices, p => p.ironPrice, IRON, PAD_LEFT, PAD_BOTTOM, plotW, plotH, maxPrice, 4);

        tex.Apply();
        return tex;
    }

    /// <summary>Y 轴数值网格步进：让标签数量保持在 4~6 条。</summary>
    static int NiceYStep(int maxPrice)
    {
        if (maxPrice <= 5) return 1;
        if (maxPrice <= 10) return 2;
        if (maxPrice <= 20) return 5;
        if (maxPrice <= 50) return 10;
        return 20;
    }

    /// <summary>X 轴天数标签步进：让标签数量保持在 5~10 个。</summary>
    static int NiceDayStep(int dayCount)
    {
        if (dayCount <= 8) return 1;
        if (dayCount <= 16) return 2;
        if (dayCount <= 40) return 5;
        if (dayCount <= 80) return 10;
        return 20;
    }

    /// <summary>Y 轴最大值取整：≤5→5，≤10→10，≤20→20，其余向上取整到 10。</summary>
    static int NiceCeil(int v)
    {
        if (v <= 0) return 1;
        if (v <= 5) return 5;
        if (v <= 10) return 10;
        if (v <= 20) return 20;
        int step = 10;
        return ((v + step - 1) / step) * step;
    }

    static void DrawSeries(Texture2D t, List<DailyPrice> prices, Func<DailyPrice, int> get,
        Color c, int padX, int padY, int plotW, int plotH, int maxPrice, int thick)
    {
        int n = prices.Count;
        if (n == 0) return;

        int X(int i)
        {
            if (n == 1) return padX + plotW / 2;
            return padX + Mathf.RoundToInt((float)i / (n - 1) * plotW);
        }
        int Y(int v)
        {
            v = Mathf.Clamp(v, 0, maxPrice);
            return padY + Mathf.RoundToInt((float)v / maxPrice * plotH);
        }

        int prevX = X(0), prevY = Y(get(prices[0]));
        if (n == 1) { FillDisk(t, prevX, prevY, thick * 0.5f, c); return; }
        for (int i = 1; i < n; i++)
        {
            int x = X(i), y = Y(get(prices[i]));
            DrawLine(t, prevX, prevY, x, y, c, thick);
            prevX = x;
            prevY = y;
        }
    }

    /// <summary>沿两点间直线逐像素画圆盘，形成粗线段（端点也覆盖，等价数据点标记）。</summary>
    static void DrawLine(Texture2D t, int x0, int y0, int x1, int y1, Color c, int thick)
    {
        int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
        float r = thick * 0.5f;
        if (steps <= 0) { FillDisk(t, x0, y0, r, c); return; }
        for (int s = 0; s <= steps; s++)
        {
            float k = (float)s / steps;
            FillDisk(t, Mathf.RoundToInt(Mathf.Lerp(x0, x1, k)),
                        Mathf.RoundToInt(Mathf.Lerp(y0, y1, k)), r, c);
        }
    }

    static void FillDisk(Texture2D t, int cx, int cy, float r, Color c)
    {
        int ri = Mathf.CeilToInt(r);
        for (int dy = -ri; dy <= ri; dy++)
            for (int dx = -ri; dx <= ri; dx++)
                if (dx * dx + dy * dy <= r * r)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x >= 0 && x < t.width && y >= 0 && y < t.height)
                        t.SetPixel(x, y, c);
                }
    }

    static void DrawHLine(Texture2D t, int y, int x0, int x1, Color c)
    {
        if (y < 0 || y >= t.height) return;
        for (int x = Mathf.Max(0, x0); x <= Mathf.Min(t.width - 1, x1); x++)
            t.SetPixel(x, y, c);
    }

    static void DrawVLine(Texture2D t, int x, int y0, int y1, Color c)
    {
        if (x < 0 || x >= t.width) return;
        for (int y = Mathf.Max(0, y0); y <= Mathf.Min(t.height - 1, y1); y++)
            t.SetPixel(x, y, c);
    }

    // ---------- UGUI 元素 ----------

    static void MakeLegendItem(Transform parent, string label, Color color, float x, Font font)
    {
        var go = new GameObject("Legend_" + label);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 10);
        rt.sizeDelta = new Vector2(80, 18);

        var dot = new GameObject("Dot");
        dot.transform.SetParent(go.transform, false);
        var drt = dot.AddComponent<RectTransform>();
        drt.anchorMin = new Vector2(0f, 0.5f);
        drt.anchorMax = new Vector2(0f, 0.5f);
        drt.pivot = new Vector2(0f, 0.5f);
        drt.anchoredPosition = new Vector2(0, 0);
        drt.sizeDelta = new Vector2(10, 10);
        var img = dot.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;

        MakeText(go.transform, "L", label, font, 12,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14, 0), new Vector2(60, 18),
            new Color(0.85f, 0.85f, 0.85f), TextAnchor.MiddleLeft);
    }

    static Text MakeText(Transform parent, string name, string text, Font font, int fontSize,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size, Color color, TextAnchor align)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        var t = go.AddComponent<Text>();
        t.text = text;
        t.font = font;
        t.fontSize = fontSize;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        return t;
    }
}
