using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 底部播放控制面板：双队数据栏 + 时间轴 + 播放/镜头/导播按钮。独立 Canvas（sortingOrder 220）。
///
/// ═══ 架构：Prefab 是真源，代码只做运行时接线（后续改面板先看这） ═══
/// 1. 结构/布局/静态颜色/字号/标签文案 全在 Assets/Prefabs/UI/PlaybackControlPanel.prefab。
///    场景 unknow.unity 的 PrefabRefs.playbackControlPanelPrefab 按 GUID 引用它；prefab 缺失时 Create()
///    直接 LogError 并返回 null（旧的 CreateFromCode 纯代码兜底已删除）。
/// 2. Create(player) 实例化 prefab 后依次执行（顺序有依赖）：
///      UiFonts.Apply     —— 统一替换所有 Text 字体为 NotoSansSC（覆盖 prefab 烘焙字体）
///      AddDirectorUI()   —— 动态追加「手动/自动」按钮 + DirectorStatus 指示灯（prefab 里没有）
///      WireCallbacks()   —— 按名字查找接线：Slider/Play/Restart/Sp1/Sp2/CamGlobal/CamA/CamB/CamFree/手动/自动
///      ResolveTextRefs() —— 按名字重解析队伍文本（TeamBar/RedCard|BlueCard/*），防序列化引用失效
///      Sync(player)      —— 立即填充一次数据
/// 3. Update() 每帧调 Sync()（轮询式直读 engine 现场，非事件驱动）；拖时间轴会先 SetPlaying(false) 再 JumpTo。
/// 4. 代码运行时覆盖点：全部文本内容、Play/Speed/Manual/Auto 按钮底色、字体。
///    其余（布局、字号、静态颜色、按钮标签）来自 prefab，改样式直接改 prefab。
/// 5. 当前状态（2026-08）：镜头按钮 = 全局/CamGlobal · 蓝方/CamA · 红方/CamB · 自由/CamFree，
///    与键盘 1/2/3/4 对应（见 ReplayCameraRig）。ControlBar 680 宽 + HorizontalLayoutGroup 自动排布；
///    TeamBar 用 HorizontalLayoutGroup 排 RedCard/BlueCard 两张队伍卡。已按需求移除全部 emoji。
/// </summary>
public class PlaybackControlPanelController : MonoBehaviour
{
    [SerializeField] Text _redName, _redHp, _redGold, _redScore, _redTower, _redWall, _redMember, _redTask, _redBag;
    [SerializeField] Text _blueName, _blueHp, _blueGold, _blueScore, _blueTower, _blueWall, _blueMember, _blueTask, _blueBag;
    [SerializeField] Slider _slider;
    [SerializeField] Text _roundText;
    [SerializeField] Button _playBtn; [SerializeField] Text _playLabel;
    [SerializeField] Button _speed1Btn, _speed2Btn;
    // 智能导演模式
    [SerializeField] Button _btnManual, _btnAuto;
    [SerializeField] Text _directorStatus;
    int _totalRounds;
    ReplayPlayer _player;

    /// <summary>全局调试开关：单位头顶实时显示 ID/坐标/HP/攻击力（默认关闭，点击「显示」按钮取反）。</summary>
    public static bool ShowUnitStats = false;
    [SerializeField] Button _showStatsBtn;   // 「显示」调试切换按钮（prefab ControlBar 内，WireCallbacks 按名接线）
    [SerializeField] Button _volumeBtn;      // 「音量」循环按钮（prefab 无则动态克隆 CamFree 同款，WireCallbacks 接线）

    static Font Fn()
    {
        return UiFonts.Get();
    }

    public static PlaybackControlPanelController Create(ReplayPlayer player)
    {
        // prefab 是真源：场景 PrefabRefs 按 GUID 引用，缺失即报错（不再有纯代码兜底）
        var prefab = PrefabRefs.Instance.GetPlaybackControlPrefab();
        if (prefab == null)
        {
            Debug.LogError("[PlaybackControlPanelController] 缺少 PlaybackControlPanel prefab（请检查场景 PrefabRefs.playbackControlPanelPrefab），面板未创建。");
            return null;
        }
        var go = Object.Instantiate(prefab);
        UiFonts.Apply(go.transform);   // 覆盖 prefab 里烘焙的旧字体
        var ctrl = go.GetComponentInChildren<PlaybackControlPanelController>();
        if (ctrl == null) ctrl = go.AddComponent<PlaybackControlPanelController>();
        ctrl._player = player;
        ctrl.AddDirectorUI(go.transform);  // 先创建导演模式 UI
        ctrl.WireCallbacks(player);        // 再连线回调
        ctrl.ResolveTextRefs();           // 按名字解析文本引用（避免序列化引用失效）
        ctrl._totalRounds = player.TotalRounds;
        ctrl.Sync(player);
        return ctrl;
    }

    /// <summary>动态创建导演模式按钮 + 状态指示灯（「手动/自动」按钮由代码动态创建，prefab 里没有）</summary>
    void AddDirectorUI(Transform canvasRoot)
    {
        var f = Fn();
        // 在 ControlBar 中添加 Manual/Auto 按钮
        var btnBar = transform.Find("ControlBar");
        if (btnBar == null) return;

        // 找到最右侧按钮的位置
        float bx = 200f; // 右侧偏移
        var manBtn = MakeBtn(btnBar, "Btn_ModeManual", "手动", bx, 48, 24,
            new Color(0.3f, 0.55f, 0.3f), f, 12,
            () => CameraManager.Instance?.SetSpectatorMode(CameraManager.CameraSpectatorMode.Manual));
        var autoBtn = MakeBtn(btnBar, "Btn_ModeAuto", "自动", bx + 54, 48, 24,
            new Color(0.55f, 0.25f, 0.2f), f, 12,
            () => CameraManager.Instance?.SetSpectatorMode(CameraManager.CameraSpectatorMode.Auto));
        _btnManual = manBtn;
        _btnAuto = autoBtn;

        // 状态指示灯
        var statusGo = new GameObject("DirectorStatus");
        statusGo.transform.SetParent(canvasRoot, false);
        var srt = statusGo.AddComponent<RectTransform>();
        srt.anchorMin = new Vector2(0.5f, 1); srt.anchorMax = new Vector2(0.5f, 1);
        srt.pivot = new Vector2(0.5f, 1); srt.anchoredPosition = new Vector2(0, -72);
        srt.sizeDelta = new Vector2(320, 28);
        var st = statusGo.AddComponent<Text>();
        st.text = "智能导演进行中"; st.font = f; st.fontSize = 16;
        st.alignment = TextAnchor.MiddleCenter; st.color = new Color(1f, 0.25f, 0.2f);
        st.raycastTarget = false;
        statusGo.SetActive(false);
        _directorStatus = st;
    }

    Button MakeBtn(Transform p, string n, string l, float x, float w, float h, Color c, Font f, int fsz, UnityEngine.Events.UnityAction cb)
    {
        var g = new GameObject(n); g.transform.SetParent(p, false);
        var r = g.AddComponent<RectTransform>();
        r.anchorMin = new Vector2(0.5f, 0.5f); r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition = new Vector2(x, 0);
        r.sizeDelta = new Vector2(w, h);
        g.AddComponent<Image>().color = c;
        var b = g.AddComponent<Button>(); b.onClick.AddListener(cb);
        var lg = new GameObject("L"); lg.transform.SetParent(g.transform, false);
        var lr = lg.AddComponent<RectTransform>();
        lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
        lr.offsetMin = Vector2.zero; lr.offsetMax = Vector2.zero;
        var lt = lg.AddComponent<Text>();
        lt.text = l; lt.font = f; lt.fontSize = fsz;
        lt.alignment = TextAnchor.MiddleCenter; lt.color = Color.white; lt.raycastTarget = false;
        return b;
    }

    /// <summary>Prefab 模式：连接按钮/滑块回调（无法在 prefab 中序列化 UnityAction）</summary>
    void WireCallbacks(ReplayPlayer player)
    {
        if (_slider != null) _slider.onValueChanged.AddListener(OnDrag);
        if (_playBtn != null) _playBtn.onClick.AddListener(() => player.TogglePlay());
        // Restart / Speed / Camera 按钮通过名称查找（prefab 中已命名）
        var btnBar = transform.Find("ControlBar");
        if (btnBar != null)
        {
            var restartBtn = btnBar.Find("Restart")?.GetComponent<Button>();
            if (restartBtn != null) restartBtn.onClick.AddListener(() => { player.Restart(); _totalRounds = player.TotalRounds; });

            var sp1Btn = btnBar.Find("Sp1")?.GetComponent<Button>();
            if (sp1Btn != null) { sp1Btn.onClick.AddListener(() => player.SetSpeed(2)); _speed1Btn = sp1Btn; }

            var sp2Btn = btnBar.Find("Sp2")?.GetComponent<Button>();
            if (sp2Btn != null) { sp2Btn.onClick.AddListener(() => player.SetSpeed(3)); _speed2Btn = sp2Btn; }

            var camRig = Camera.main != null ? Camera.main.GetComponent<ReplayCameraRig>() : null;
            var camGlobalBtn = btnBar.Find("CamGlobal")?.GetComponent<Button>();
            if (camGlobalBtn != null) camGlobalBtn.onClick.AddListener(() => camRig?.SetCameraMode("global"));
            var camABtn = btnBar.Find("CamA")?.GetComponent<Button>();
            if (camABtn != null) camABtn.onClick.AddListener(() => camRig?.SetCameraMode("teamA"));
            var camBBtn = btnBar.Find("CamB")?.GetComponent<Button>();
            if (camBBtn != null) camBBtn.onClick.AddListener(() => camRig?.SetCameraMode("teamB"));
            var camFreeBtn = btnBar.Find("CamFree")?.GetComponent<Button>();
            if (camFreeBtn != null) camFreeBtn.onClick.AddListener(() => camRig?.SetCameraMode("free"));

            // 「显示」调试切换按钮（点击取反全局开关，Update 里高亮）
            var showStatsBtn = btnBar.Find("Btn_ShowStats")?.GetComponent<Button>();
            if (showStatsBtn != null) { showStatsBtn.onClick.AddListener(() => ShowUnitStats = !ShowUnitStats); _showStatsBtn = showStatsBtn; }

            // 「音量」循环按钮：prefab 没有 Btn_Volume 节点则动态克隆 CamFree 同款按钮 → 加到 ControlBar 末尾
            var volBtn = btnBar.Find("Btn_Volume")?.GetComponent<Button>();
            if (volBtn == null)
            {
                var camFreeGo = btnBar.Find("CamFree")?.gameObject;
                if (camFreeGo != null)
                {
                    var volGo = Instantiate(camFreeGo, btnBar);
                    volGo.name = "Btn_Volume";
                    volBtn = volGo.GetComponent<Button>();
                }
            }
            if (volBtn != null)
            {
                _volumeBtn = volBtn;
                volBtn.onClick.AddListener(() =>
                {
                    BgmController.CycleVolume();
                    RefreshVolumeLabel();
                });
                // 克隆发生在 UiFonts.Apply 之后，需手动补 NotoSansSC 字体 + 初始文字
                var volText = volBtn.transform.Find("L")?.GetComponent<Text>();
                if (volText != null) { volText.font = Fn(); volText.text = BgmController.CurrentVolumeLabel(); }
                // ControlBar 680 宽装多按钮，新增音量按钮后可能溢出 → 动态加宽到 740
                var barRT = btnBar.GetComponent<RectTransform>();
                if (barRT != null && barRT.sizeDelta.x < 740f)
                    barRT.sizeDelta = new Vector2(740f, barRT.sizeDelta.y);
            }

            // 智能导播模式按钮
            var manBtn = btnBar.Find("Btn_ModeManual")?.GetComponent<Button>();
            if (manBtn != null) { manBtn.onClick.AddListener(() => CameraManager.Instance?.SetSpectatorMode(CameraManager.CameraSpectatorMode.Manual)); _btnManual = manBtn; }
            var autoBtn = btnBar.Find("Btn_ModeAuto")?.GetComponent<Button>();
            if (autoBtn != null) { autoBtn.onClick.AddListener(() => CameraManager.Instance?.SetSpectatorMode(CameraManager.CameraSpectatorMode.Auto)); _btnAuto = autoBtn; }
        }
        // 导演状态指示灯
        var statusGo = transform.Find("DirectorStatus");
        if (statusGo != null) { _directorStatus = statusGo.GetComponent<Text>(); statusGo.gameObject.SetActive(false); }
    }



    /// <summary>按名字解析文本引用（prefab 实例化后重解析，避免序列化引用失效）</summary>
    void ResolveTextRefs()
    {
        var red  = transform.Find("TeamBar/RedCard");
        var blue = transform.Find("TeamBar/BlueCard");
        _redName   = FindText(red,  "RN");  _redHp     = FindText(red,  "RH");
        _redGold   = FindText(red,  "RG");  _redScore  = FindText(red,  "RS");
        _redTower  = FindText(red,  "RTw"); _redWall   = FindText(red,  "RWl");
        _redMember = FindText(red,  "RMm"); _redTask   = FindText(red,  "RTk");
        _redBag    = FindText(red,  "RBg");
        _blueName   = FindText(blue, "BN");  _blueHp     = FindText(blue, "BH");
        _blueGold   = FindText(blue, "BG");  _blueScore  = FindText(blue, "BS");
        _blueTower  = FindText(blue, "BTw"); _blueWall   = FindText(blue, "BWl");
        _blueMember = FindText(blue, "BMm"); _blueTask   = FindText(blue, "BTk");
        _blueBag    = FindText(blue, "BBg");
    }

    static Text FindText(Transform parent, string name)
    {
        if (parent == null) return null;
        var t = parent.Find(name);
        return t != null ? t.GetComponent<Text>() : null;
    }

    /// <summary>刷新音量按钮文字为当前档位（每帧轮询，不引入事件订阅）。</summary>
    void RefreshVolumeLabel()
    {
        if (_volumeBtn == null) return;
        var t = _volumeBtn.transform.Find("L")?.GetComponent<Text>();
        if (t != null) t.text = BgmController.CurrentVolumeLabel();
    }

    void OnDrag(float v) {
        if (_player==null) return;
        int t=Mathf.RoundToInt(v);
        if (Mathf.Abs(t-_player.cur)>1.5f) { _player.SetPlaying(false); _player.JumpTo(t,false); Sync(_player); }
    }

    public void Sync(ReplayPlayer p) {
        if (p==null||_totalRounds<=0) return;
        if (_slider!=null&&_slider.maxValue!=_totalRounds) { _slider.minValue=1; _slider.maxValue=_totalRounds; }
        if (_slider!=null) _slider.SetValueWithoutNotify(p.cur);
        if (_roundText!=null) _roundText.text=p.cur+" / "+_totalRounds+" 回合";
        // 游戏上限（任务书）：围墙≤28、防御塔≤3、每队队员≤3
        const int MAX_WALL = 28, MAX_TOWER = 3, MAX_MEMBER = 3;
        Color colRed  = new Color(1f, 0.176f, 0.333f);   // defender = 红方
        Color colBlue = new Color(0f, 0.478f, 1f);       // challenger = 蓝方

        foreach(var kv in p.engine.teams) {
            var st=kv.Value; int hp=0, towers=0, walls=0, members=0;
            var agg = new System.Collections.Generic.Dictionary<string, int>();
            foreach(var u in p.engine.units.Values) {
                if(u.teamId!=st.teamId) continue;
                if(u.dying||u.dead) continue;
                if(u.type==4) hp=u.hp;
                else if(u.type==3) towers++;
                else if(u.type==5) walls++;
                else if(u.type==6||u.type==7) members++;
                if(u.backpacks!=null)
                    foreach(var b in u.backpacks) { int n; agg.TryGetValue(b.name, out n); agg[b.name]=n+b.num; }
            }
            var bag=new System.Text.StringBuilder();
            foreach(var item in agg) bag.Append(ItemNameCn.Cn(item.Key)).Append("x").Append(item.Value).Append(" ");
            string bagStr=bag.Length>0?bag.ToString().Trim():"空";

            // 队伍类型→卡片：defender=红方，challenger=蓝方（与 TeamColorApplicator 一致）
            bool isRed = st.type=="defender";
            var name=isRed?_redName:_blueName;     var hpT=isRed?_redHp:_blueHp;
            var goldT=isRed?_redGold:_blueGold;    var scoreT=isRed?_redScore:_blueScore;
            var wallT=isRed?_redWall:_blueWall;    var towerT=isRed?_redTower:_blueTower;
            var memberT=isRed?_redMember:_blueMember; var taskT=isRed?_redTask:_blueTask;
            var bagT=isRed?_redBag:_blueBag;

            if(name!=null){ name.text=isRed?"红方":"蓝方"; name.color=isRed?colRed:colBlue; }
            if(hpT!=null) hpT.text="基地 "+hp;
            if(goldT!=null) goldT.text="金币 "+st.gold;
            if(scoreT!=null) scoreT.text="积分 "+st.score;
            if(wallT!=null) wallT.text="围墙 "+walls+"/"+MAX_WALL;
            if(towerT!=null) towerT.text="防御塔 "+towers+"/"+MAX_TOWER;
            if(memberT!=null) memberT.text="人数 "+members+"/"+MAX_MEMBER;
            if(taskT!=null)
            {
                // 已接取/已作答 → 显示对错；否则显示"尚未接受任务"
                // 注意：不加 emoji 前缀，避免回退字体行高不同导致与围墙行对不齐
                if (st.taskCorrect > 0 || st.taskWrong > 0 || st.hasActiveTask)
                    taskT.text="任务 对"+st.taskCorrect+" 错"+st.taskWrong;
                else
                    taskT.text="尚未接受任务";
            }
            if(bagT!=null) bagT.text="背包 "+bagStr;
        }
        if(_playLabel!=null){ _playLabel.text=p.playing?"暂停":"播放"; _playBtn.GetComponent<Image>().color=p.playing?new Color(0.96f,0.78f,0.22f):new Color(0,0.478f,1f); }
        if(_speed1Btn!=null)_speed1Btn.GetComponent<Image>().color=p.speedIndex==2?new Color(0,0.478f,1f):new Color(0.35f,0.35f,0.40f);
        if(_speed2Btn!=null)_speed2Btn.GetComponent<Image>().color=p.speedIndex==3?new Color(0,0.478f,1f):new Color(0.35f,0.35f,0.40f);
    }
    void Update() {
        if(_player!=null) Sync(_player);

        // 导演模式指示灯：Auto 模式时红色呼吸闪烁，Manual 时隐藏
        if (_directorStatus != null)
        {
            var mgr = CameraManager.Instance;
            bool isAuto = mgr != null && mgr.IsAuto;
            _directorStatus.gameObject.SetActive(isAuto);
            if (isAuto)
            {
                // 呼吸灯：alpha 在 0.4~1.0 之间正弦波动
                float alpha = 0.4f + 0.6f * (Mathf.Sin(Time.time * 3f) * 0.5f + 0.5f);
                var c = _directorStatus.color;
                _directorStatus.color = new Color(1f, 0.2f, 0.15f, alpha);
            }
        }

        // Auto 模式按钮高亮
        if (_btnManual != null && _btnAuto != null && CameraManager.Instance != null)
        {
            bool auto = CameraManager.Instance.IsAuto;
            _btnManual.GetComponent<Image>().color = auto ? new Color(0.35f, 0.35f, 0.40f) : new Color(0.3f, 0.55f, 0.3f);
            _btnAuto.GetComponent<Image>().color = auto ? new Color(0.75f, 0.3f, 0.2f) : new Color(0.35f, 0.35f, 0.40f);
        }

        // 「显示」调试开关高亮（开启=琥珀色，关闭=默认暗底）
        if (_showStatsBtn != null)
            _showStatsBtn.GetComponent<Image>().color = ShowUnitStats ? new Color(0.85f, 0.6f, 0.1f) : new Color(0.22f, 0.22f, 0.28f);

        // 音量按钮文字每帧同步为当前档位
        if (_volumeBtn != null) RefreshVolumeLabel();
    }

}
