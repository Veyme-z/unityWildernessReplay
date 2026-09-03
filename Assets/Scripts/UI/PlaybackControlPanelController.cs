using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 底部播放控制面板：双队数据栏 + 时间轴 + 播放/镜头按钮。独立 Canvas（sortingOrder 220）。
///
/// ═══ 架构：Prefab 是真源，代码只做运行时接线（后续改面板先看这） ═══
/// 1. 结构/布局/静态颜色/字号/标签文案 全在 Assets/Prefabs/UI/PlaybackControlPanel.prefab。
///    场景 unknow.unity 的 PrefabRefs.playbackControlPanelPrefab 按 GUID 引用它；prefab 缺失时 Create()
///    直接 LogError 并返回 null（旧的 CreateFromCode 纯代码兜底已删除）。
/// 2. Create(player) 实例化 prefab 后依次执行（顺序有依赖）：
///      UiFonts.Apply     —— 统一替换所有 Text 字体为 NotoSansSC（覆盖 prefab 烘焙字体）
///      WireCallbacks()   —— 按名字查找接线：Slider/Play/Restart/Sp1/Sp2/CamGlobal/CamA/CamB/CamFree
///      ResolveTextRefs() —— 按名字重解析队伍文本（TeamBar/RedCard|BlueCard/*），防序列化引用失效
///      Sync(player)      —— 立即填充一次数据
/// 3. Update() 每帧调 Sync()（轮询式直读 engine 现场，非事件驱动）；拖时间轴会先 SetPlaying(false) 再 JumpTo。
/// 4. 代码运行时覆盖点：全部文本内容、Play/Speed 按钮底色、当前镜头模式按钮底色、字体。
///    其余（布局、字号、静态颜色、按钮标签）来自 prefab，改样式直接改 prefab。
/// 5. 当前状态（2026-09）：镜头按钮 = 全局/CamGlobal · 蓝方/CamA · 红方/CamB · 自由/CamFree，
///    与键盘 1/2/3/4 对应（见 ReplayCameraRig）；开局默认「自由」模式（ReplayCameraRig.Start→"free"），
///    Update 里按 CurrentModeName 高亮当前镜头按钮。已取消「手动/自动」智能导播按钮与 DirectorStatus 指示灯。
///    ControlBar 680 宽 + HorizontalLayoutGroup 自动排布；TeamBar 用 HorizontalLayoutGroup 排 RedCard/BlueCard。
/// 6. 倍速按钮（2026-09）：Sp1 点击在 1×↔0.5×、Sp2 点击在 2×↔5× 间循环切换，按钮文字随档位更新，
///    初始默认 1×（ReplayPlayer.SPEEDS={0.5,1,2,5}，speedIndex 默认 1）；高亮 = 当前激活的速度组。
///</summary>
public class PlaybackControlPanelController : MonoBehaviour
{
    [SerializeField] Text _redName, _redHp, _redGold, _redScore, _redTower, _redWall, _redMember, _redTask, _redBag;
    [SerializeField] Text _blueName, _blueHp, _blueGold, _blueScore, _blueTower, _blueWall, _blueMember, _blueTask, _blueBag;
    [SerializeField] Slider _slider;
    [SerializeField] Text _roundText;
    [SerializeField] Button _playBtn; [SerializeField] Text _playLabel;
    [SerializeField] Button _speed1Btn, _speed2Btn;
    // 倍速按钮档位：Sp1 在 1x↔0.5x、Sp2 在 2x↔5x 间循环（false=基础档 1x/2x，true=切换档 0.5x/5x）
    bool _sp1Half;
    bool _sp2Five;
    // 镜头模式按钮（高亮当前激活项；开局默认「自由」）
    [SerializeField] Button _camGlobalBtn, _camABtn, _camBBtn, _camFreeBtn;
    int _totalRounds;
    ReplayPlayer _player;

    /// <summary>全局调试开关：单位头顶实时显示 ID/坐标/HP/攻击力（默认关闭，点击「显示」按钮取反）。</summary>
    public static bool ShowUnitStats = false;
    [SerializeField] Button _showStatsBtn;   // 「显示」调试切换按钮（prefab ControlBar 内，WireCallbacks 按名接线）
    [SerializeField] Button _volumeBtn;      // 「音量」循环按钮（prefab 无则动态克隆 CamFree 同款，WireCallbacks 接线）
    [SerializeField] Button _cinematicBtn;   // 「动画」开关按钮（克隆音量按钮，默认关 = 不播全屏 ufo/plane）

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
        ctrl.WireCallbacks(player);        // 再连线回调
        ctrl.ResolveTextRefs();           // 按名字解析文本引用（避免序列化引用失效）
        ctrl._totalRounds = player.TotalRounds;
        ctrl.Sync(player);
        return ctrl;
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
            if (sp1Btn != null)
            {
                // 1x ↔ 0.5x 循环（SPEEDS[1]=1x / SPEEDS[0]=0.5x）
                sp1Btn.onClick.AddListener(() => { _sp1Half = !_sp1Half; player.SetSpeed(_sp1Half ? 0 : 1); });
                _speed1Btn = sp1Btn;
            }

            var sp2Btn = btnBar.Find("Sp2")?.GetComponent<Button>();
            if (sp2Btn != null)
            {
                // 2x ↔ 5x 循环（SPEEDS[2]=2x / SPEEDS[3]=5x）
                sp2Btn.onClick.AddListener(() => { _sp2Five = !_sp2Five; player.SetSpeed(_sp2Five ? 3 : 2); });
                _speed2Btn = sp2Btn;
            }

            var camRig = Camera.main != null ? Camera.main.GetComponent<ReplayCameraRig>() : null;
            var camGlobalBtn = btnBar.Find("CamGlobal")?.GetComponent<Button>();
            if (camGlobalBtn != null) { camGlobalBtn.onClick.AddListener(() => camRig?.SetCameraMode("global")); _camGlobalBtn = camGlobalBtn; }
            var camABtn = btnBar.Find("CamA")?.GetComponent<Button>();
            if (camABtn != null) { camABtn.onClick.AddListener(() => camRig?.SetCameraMode("teamA")); _camABtn = camABtn; }
            var camBBtn = btnBar.Find("CamB")?.GetComponent<Button>();
            if (camBBtn != null) { camBBtn.onClick.AddListener(() => camRig?.SetCameraMode("teamB")); _camBBtn = camBBtn; }
            var camFreeBtn = btnBar.Find("CamFree")?.GetComponent<Button>();
            if (camFreeBtn != null) { camFreeBtn.onClick.AddListener(() => camRig?.SetCameraMode("free")); _camFreeBtn = camFreeBtn; }

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

            // 「动画」开关：克隆音量按钮放到它旁边（音量右侧）。默认关 → 全屏剧情（ufo/plane）一律不播；
            // 选手点开后再下一次自然入夜/任务点1领取才进动画。状态由 Update 每帧高亮（开=琥珀，关=暗底）。
            if (_volumeBtn != null)
            {
                var cinGo = btnBar.Find("Btn_Cinematic")?.gameObject;
                if (cinGo == null) cinGo = Instantiate(_volumeBtn.gameObject, btnBar);
                cinGo.name = "Btn_Cinematic";
                var cinBtn = cinGo.GetComponent<Button>();
                if (cinBtn != null)
                {
                    _cinematicBtn = cinBtn;
                    cinBtn.onClick.AddListener(() => ReplayCinematic.CinematicEnabled = !ReplayCinematic.CinematicEnabled);
                    // 克隆发生在 UiFonts.Apply 之后，需手动补字体 + 标签文字
                    var cinText = cinGo.transform.Find("L")?.GetComponent<Text>();
                    if (cinText != null) { cinText.font = Fn(); cinText.text = "动画"; }
                }
                var cinBar = btnBar.GetComponent<RectTransform>();
                if (cinBar != null && cinBar.sizeDelta.x < 800f)
                    cinBar.sizeDelta = new Vector2(800f, cinBar.sizeDelta.y);
            }

        }
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

    /// <summary>设置倍速按钮标签（"0.5×"/"1×"/"2×"/"5×"），文字挂在按钮的 L 子节点。</summary>
    static void SetSpeedLabel(Button b, string label)
    {
        if (b == null) return;
        var t = b.transform.Find("L")?.GetComponent<Text>();
        if (t != null) t.text = label;
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
        // 游戏上限（任务书）：围墙≤20（6×6 边框格数）、防御塔≤3、每队队员≤3
        const int MAX_WALL = 20, MAX_TOWER = 3, MAX_MEMBER = 3;
        Color colRed  = new Color(1f, 0.176f, 0.333f);   // defender = 红方
        Color colBlue = new Color(0f, 0.478f, 1f);       // challenger = 蓝方

        foreach(var kv in p.engine.teams) {
            var st=kv.Value; int hp=0, towers=0, walls=0, members=0;
            var agg = new System.Collections.Generic.Dictionary<string, int>();
            foreach(var u in p.engine.units.Values) {
                if(u.teamId!=st.teamId) continue;
                if(u.dying||u.dead) continue;
                if(u.type==4) hp=u.hp;
                else if(u.IsTower) towers++;
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

            if(name!=null)
            {
                // 队名显示格式：红方：{队名} / 蓝方：{队名}（队名为空时只显示阵营）
                string label = isRed ? "红方" : "蓝方";
                name.text = string.IsNullOrEmpty(st.teamName) ? label : label + "：" + st.teamName;
                name.color = isRed ? colRed : colBlue;
            }
            if(hpT!=null) hpT.text="基地 "+hp;
            if(goldT!=null) goldT.text="金币 "+st.gold;
            if(scoreT!=null) scoreT.text="积分 "+st.score;
            if(wallT!=null) wallT.text="围墙 "+walls+"/"+MAX_WALL;
            if(towerT!=null) towerT.text="防御塔 "+towers+"/"+MAX_TOWER;
            if(memberT!=null) memberT.text="人数 "+members+"/"+MAX_MEMBER;
            if(taskT!=null)
            {
                // 显示 allTaskInfo：自进化类1/2 每类「完成X 失败Y 共Z」各一行（"失效"改名"失败"）。
                // 任一类总数>0 才显示对应行；两行都没有则"尚未接受任务"。
                // 注意：不加 emoji 前缀，避免回退字体行高不同导致与围墙行对不齐
                var tsb = new System.Text.StringBuilder();
                if (st.task1Total > 0)
                    tsb.Append("自进化1 完成").Append(st.task1Done).Append(" 失败").Append(st.task1Failed).Append(" 共").Append(st.task1Total).Append('\n');
                if (st.task2Total > 0)
                    tsb.Append("自进化2 完成").Append(st.task2Done).Append(" 失败").Append(st.task2Failed).Append(" 共").Append(st.task2Total);
                taskT.text = tsb.Length > 0 ? tsb.ToString().TrimEnd('\n') : "尚未接受任务";
            }
            if(bagT!=null) bagT.text="背包 "+bagStr;
        }
        if(_playLabel!=null){ _playLabel.text=p.playing?"暂停":"播放"; _playBtn.GetComponent<Image>().color=p.playing?new Color(0.96f,0.78f,0.22f):new Color(0,0.478f,1f); }
        // 倍速按钮：Sp1 组=0.5x/1x，Sp2 组=2x/5x；高亮当前激活的速度组，按钮文字随档位更新
        if(_speed1Btn!=null)
        {
            _speed1Btn.GetComponent<Image>().color=(p.speedIndex==0||p.speedIndex==1)?new Color(0,0.478f,1f):new Color(0.35f,0.35f,0.40f);
            SetSpeedLabel(_speed1Btn, p.speedIndex==0?"0.5×":"1×");
        }
        if(_speed2Btn!=null)
        {
            _speed2Btn.GetComponent<Image>().color=(p.speedIndex==2||p.speedIndex==3)?new Color(0,0.478f,1f):new Color(0.35f,0.35f,0.40f);
            SetSpeedLabel(_speed2Btn, p.speedIndex==3?"5×":"2×");
        }
    }
    void Update() {
        if(_player!=null) Sync(_player);

        // 当前镜头模式按钮高亮（读取 ReplayCameraRig 实际模式；开局默认「自由」）
        UpdateCameraModeHighlight();

        // 「显示」调试开关高亮（开启=琥珀色，关闭=默认暗底）
        if (_showStatsBtn != null)
            _showStatsBtn.GetComponent<Image>().color = ShowUnitStats ? new Color(0.85f, 0.6f, 0.1f) : new Color(0.22f, 0.22f, 0.28f);

        // 音量按钮文字每帧同步为当前档位
        if (_volumeBtn != null) RefreshVolumeLabel();

        // 「动画」开关高亮：开=琥珀色，关=暗底（默认关）
        if (_cinematicBtn != null)
        {
            var img = _cinematicBtn.GetComponent<Image>();
            if (img != null) img.color = ReplayCinematic.CinematicEnabled ? new Color(0.85f, 0.6f, 0.1f) : new Color(0.22f, 0.22f, 0.28f);
        }
    }

    /// <summary>高亮当前激活的镜头模式按钮：选中=蓝（与倍速按钮一致），其余恢复暗底。</summary>
    void UpdateCameraModeHighlight()
    {
        var rig = Camera.main != null ? Camera.main.GetComponent<ReplayCameraRig>() : null;
        string mode = rig != null ? rig.CurrentModeName : "global";
        Color active = new Color(0f, 0.478f, 1f);
        Color idle = new Color(0.22f, 0.22f, 0.28f);
        SetCamHighlight(_camGlobalBtn, mode == "global", active, idle);
        SetCamHighlight(_camABtn,     mode == "teamA",  active, idle);
        SetCamHighlight(_camBBtn,     mode == "teamB",  active, idle);
        SetCamHighlight(_camFreeBtn,  mode == "free",   active, idle);
    }

    static void SetCamHighlight(Button b, bool on, Color active, Color idle)
    {
        if (b == null) return;
        var img = b.GetComponent<Image>();
        if (img != null) img.color = on ? active : idle;
    }

}
