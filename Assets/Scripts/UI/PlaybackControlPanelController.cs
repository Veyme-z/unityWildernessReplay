using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 底部面板：竖向三段式 — 双队数据（相邻）→ 时间轴 → 控制按钮。
/// 独立 Canvas，与 HudController/EventLogPanelController 模式一致。
/// </summary>
public class PlaybackControlPanelController : MonoBehaviour
{
    [SerializeField] Text _redName, _redHp, _redGold, _redScore, _redTower, _redWall, _redMember, _redBag;
    [SerializeField] Text _blueName, _blueHp, _blueGold, _blueScore, _blueTower, _blueWall, _blueMember, _blueBag;
    [SerializeField] Slider _slider;
    [SerializeField] Text _roundText;
    [SerializeField] Button _playBtn; [SerializeField] Text _playLabel;
    [SerializeField] Button _speed1Btn, _speed2Btn;
    // 智能导演模式
    [SerializeField] Button _btnManual, _btnAuto;
    [SerializeField] Text _directorStatus;
    int _totalRounds;
    ReplayPlayer _player;

    static Font Fn()
    {
        return UiFonts.Get();
    }

    public static PlaybackControlPanelController Create(ReplayPlayer player)
    {
        // 优先使用 prefab（如果有配置），否则退回纯代码创建
        var prefab = PrefabRefs.Instance.GetPlaybackControlPrefab();
        PlaybackControlPanelController ctrl;
        if (prefab != null)
        {
            var go = Object.Instantiate(prefab);
            UiFonts.Apply(go.transform);   // 覆盖 prefab 里烘焙的旧字体
            ctrl = go.GetComponentInChildren<PlaybackControlPanelController>();
            if (ctrl == null) ctrl = go.AddComponent<PlaybackControlPanelController>();
            ctrl._player = player;
            ctrl.AddDirectorUI(go.transform);  // 先创建导演模式 UI
            ctrl.WireCallbacks(player);        // 再连线回调
            ctrl.ResolveTextRefs();           // 按名字解析文本引用（避免序列化引用失效）
            ctrl._totalRounds = player.TotalRounds;
            ctrl.Sync(player);
            return ctrl;
        }
        Debug.LogWarning("[PlaybackControlPanelController] PlaybackControlPanel prefab 缺失，回退到代码创建 UI（请检查场景 PrefabRefs 或 Resources/Prefabs/UI/PlaybackControlPanel）。");
        ctrl = CreateFromCode(player);
        return ctrl;
    }

    /// <summary>动态创建导演模式按钮 + 状态指示灯（prefab 路径和代码路径共用）</summary>
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

    static PlaybackControlPanelController CreateFromCode(ReplayPlayer player)
    {
        var f = Fn();
        Color bg = new Color(0.102f, 0.102f, 0.118f, 0.85f);
        Color blue = new Color(0, 0.478f, 1f);
        Color grey = new Color(0.35f, 0.35f, 0.40f);

        // ── 独立 Canvas ──
        var canvasGo = new GameObject("BottomCanvas");
        var c = canvasGo.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 220;
        var s = canvasGo.AddComponent<CanvasScaler>(); s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920, 1080); s.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var ctrl = canvasGo.AddComponent<PlaybackControlPanelController>();
        ctrl._player = player;

        // ══════ 自上而下 y 坐标 ══════
        // y=110: 双队面板（两队相邻在同一栏）
        // y=55:  时间轴
        // y=0:   控制按钮

        // ── 双队面板（一条 bar，两队相邻，三行数据） ──
        var teamBar = Bar(canvasGo.transform, "TeamBar", 0, 108, 700, 88, bg);
        // 红队（左半）— 第一行
        ctrl._redName  = Lbl(teamBar.transform, "RN", "🔴 ---", 14, 8, -6, 160, 22, new Color(0.94f,0.34f,0.28f), f, TextAnchor.MiddleLeft);
        ctrl._redHp    = Lbl(teamBar.transform, "RH", "❤ ---", 13, 8, -28, 80, 20, new Color(0.94f,0.42f,0.38f), f, TextAnchor.MiddleLeft);
        ctrl._redGold  = Lbl(teamBar.transform, "RG", "💰 --- 金币", 13, 100, -28, 120, 20, new Color(0.96f,0.78f,0.22f), f, TextAnchor.MiddleLeft);
        ctrl._redScore = Lbl(teamBar.transform, "RS", "🏆 --- 积分", 13, 230, -28, 120, 20, Color.white, f, TextAnchor.MiddleLeft);
        // 红队 — 第二行（塔/墙）
        ctrl._redTower = Lbl(teamBar.transform, "RTw", "🗼 ---", 12, 8, -50, 80, 20, new Color(0.75f,0.73f,0.68f), f, TextAnchor.MiddleLeft);
        ctrl._redWall  = Lbl(teamBar.transform, "RWl", "🧱 ---", 12, 100, -50, 80, 20, new Color(0.75f,0.73f,0.68f), f, TextAnchor.MiddleLeft);
        // 红队 — 第三行（背包，宽展）
        ctrl._redBag   = Lbl(teamBar.transform, "RBg", "🎒 ---", 12, 8, -72, 340, 20, new Color(0.75f,0.73f,0.68f), f, TextAnchor.MiddleLeft);
        // 分隔线
        var div = new GameObject("Div"); div.transform.SetParent(teamBar.transform, false);
        var drt = div.AddComponent<RectTransform>(); drt.anchorMin=new Vector2(0.5f,0.1f); drt.anchorMax=new Vector2(0.5f,0.9f);
        drt.sizeDelta=new Vector2(1,0); drt.anchoredPosition=Vector2.zero;
        div.AddComponent<Image>().color=new Color(0.3f,0.3f,0.35f);
        // 蓝队（右半）— 第一行
        ctrl._blueName  = Lbl(teamBar.transform, "BN", "🔵 ---", 14, 362, -6, 160, 22, new Color(0.28f,0.62f,0.96f), f, TextAnchor.MiddleLeft);
        ctrl._blueHp    = Lbl(teamBar.transform, "BH", "❤ ---", 13, 362, -28, 80, 20, new Color(0.94f,0.42f,0.38f), f, TextAnchor.MiddleLeft);
        ctrl._blueGold  = Lbl(teamBar.transform, "BG", "💰 --- 金币", 13, 454, -28, 120, 20, new Color(0.96f,0.78f,0.22f), f, TextAnchor.MiddleLeft);
        ctrl._blueScore = Lbl(teamBar.transform, "BS", "🏆 --- 积分", 13, 584, -28, 120, 20, Color.white, f, TextAnchor.MiddleLeft);
        // 蓝队 — 第二行
        ctrl._blueTower = Lbl(teamBar.transform, "BTw", "🗼 ---", 12, 362, -50, 80, 20, new Color(0.75f,0.73f,0.68f), f, TextAnchor.MiddleLeft);
        ctrl._blueWall  = Lbl(teamBar.transform, "BWl", "🧱 ---", 12, 454, -50, 80, 20, new Color(0.75f,0.73f,0.68f), f, TextAnchor.MiddleLeft);
        // 蓝队 — 第三行（背包，宽展）
        ctrl._blueBag   = Lbl(teamBar.transform, "BBg", "🎒 ---", 12, 362, -72, 340, 20, new Color(0.75f,0.73f,0.68f), f, TextAnchor.MiddleLeft);

        // ── 时间轴 ──
        var tlBar = Bar(canvasGo.transform, "TimelineBar", 0, 55, 0, 50, bg);
        tlBar.GetComponent<RectTransform>().anchorMin = new Vector2(0.1f, 0);
        tlBar.GetComponent<RectTransform>().anchorMax = new Vector2(0.9f, 0);
        tlBar.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 50);
        ctrl._roundText = LblA(tlBar.transform, "RT", "0 / 0 回合", 0.5f, 1, 0.5f, 1, 13, 0, -4, 200, 20,
                                new Color(0.75f,0.73f,0.68f), f, TextAnchor.MiddleCenter);
        var slGo = new GameObject("Slider"); slGo.transform.SetParent(tlBar.transform, false);
        var slRt = slGo.AddComponent<RectTransform>();
        slRt.anchorMin=new Vector2(0,0.5f); slRt.anchorMax=new Vector2(1,0.5f);
        slRt.pivot=new Vector2(0.5f,0.5f); slRt.anchoredPosition=Vector2.zero; slRt.sizeDelta=new Vector2(-40,20);
        ctrl._slider = slGo.AddComponent<Slider>();
        ctrl._slider.minValue=1; ctrl._slider.maxValue=1; ctrl._slider.value=1;
        ctrl._slider.onValueChanged.AddListener(ctrl.OnDrag);
        // 轨道
        var trk = Ch(slGo.transform,"T"); trk.AddComponent<Image>().color=new Color(0.2f,0.2f,0.22f);
        var tr=trk.GetComponent<RectTransform>(); tr.anchorMin=new Vector2(0,0.3f); tr.anchorMax=new Vector2(1,0.7f);
        tr.offsetMin=Vector2.zero; tr.offsetMax=Vector2.zero;
        // 填充
        var fl = Ch(slGo.transform,"F"); fl.AddComponent<Image>().color=blue;
        var fr=fl.GetComponent<RectTransform>(); fr.anchorMin=new Vector2(0,0.3f); fr.anchorMax=new Vector2(1,0.7f);
        fr.offsetMin=Vector2.zero; fr.offsetMax=Vector2.zero;
        // 手柄
        var hd = Ch(slGo.transform,"H"); hd.AddComponent<Image>().color=new Color(1,1,1,0.5f);
        var hr=hd.GetComponent<RectTransform>(); hr.anchorMin=new Vector2(0,0); hr.anchorMax=new Vector2(0,1);
        hr.pivot=new Vector2(0.5f,0.5f); hr.sizeDelta=new Vector2(14,14);
        ctrl._slider.fillRect=fr; ctrl._slider.handleRect=hr; ctrl._slider.targetGraphic=hd.GetComponent<Image>();

        // ── 控制按钮 ──
        var btnBar = Bar(canvasGo.transform, "ControlBar", 0, 0, 284, 50, bg);
        btnBar.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0);
        btnBar.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0);
        btnBar.GetComponent<RectTransform>().sizeDelta = new Vector2(420, 50);
        float bx = -120;
        var pb = Btn(btnBar.transform,"Play","▶",f,bx,64,38,blue, ()=>player.TogglePlay(),22);
        ctrl._playBtn=pb.btn; ctrl._playLabel=pb.label; bx+=72;
        Btn(btnBar.transform,"Restart","↺",f,bx,52,34,grey, ()=>{player.Restart(); ctrl._totalRounds=player.TotalRounds;}); bx+=60;
        var s1=Btni(btnBar.transform,"Sp1","1x",f,bx,52,34,blue, ()=>player.SetSpeed(2));
        ctrl._speed1Btn=s1.btn; bx+=60;
        var s2=Btni(btnBar.transform,"Sp2","2x",f,bx,52,34,grey, ()=>player.SetSpeed(3));
        ctrl._speed2Btn=s2.btn; bx+=60;

        // 镜头按钮：🌐全局  🔴A队  🔵B队
        var camRig = Camera.main != null ? Camera.main.GetComponent<ReplayCameraRig>() : null;
        Color camBtnBg = new Color(0.22f, 0.22f, 0.28f);
        Btn(btnBar.transform,"CamGlobal","🌐",f,bx,24,24,camBtnBg, ()=>{ camRig?.SetCameraMode("global"); }, 12); bx+=30;
        Btn(btnBar.transform,"CamA","🔴",f,bx,24,24,camBtnBg, ()=>{ camRig?.SetCameraMode("teamA"); }, 12); bx+=30;
        Btn(btnBar.transform,"CamB","🔵",f,bx,24,24,camBtnBg, ()=>{ camRig?.SetCameraMode("teamB"); }, 12); bx+=40;

        // ── 智能导播模式按钮 ──
        var manBtn = Btn(btnBar.transform,"Btn_ModeManual","🎥M",f,bx,32,24,new Color(0.3f,0.55f,0.3f), ()=>{
            CameraManager.Instance?.SetSpectatorMode(CameraManager.CameraSpectatorMode.Manual);
        }, 12); bx+=38;
        var autoBtn = Btn(btnBar.transform,"Btn_ModeAuto","🤖A",f,bx,32,24,new Color(0.55f,0.25f,0.2f), ()=>{
            CameraManager.Instance?.SetSpectatorMode(CameraManager.CameraSpectatorMode.Auto);
        }, 12); bx+=38;
        Btn(btnBar.transform,"CamFree","🆓",f,bx,28,24,camBtnBg, ()=>{ camRig?.SetCameraMode("free"); }, 12); bx+=40;
        ctrl._btnManual = manBtn.btn;
        ctrl._btnAuto = autoBtn.btn;

        // ── 导演模式状态指示灯 ──
        var statusGo = new GameObject("DirectorStatus"); statusGo.transform.SetParent(canvasGo.transform, false);
        var srt = statusGo.AddComponent<RectTransform>();
        srt.anchorMin = new Vector2(0.5f, 1); srt.anchorMax = new Vector2(0.5f, 1);
        srt.pivot = new Vector2(0.5f, 1); srt.anchoredPosition = new Vector2(0, -72);
        srt.sizeDelta = new Vector2(320, 28);
        var st = statusGo.AddComponent<Text>();
        st.text = "智能导演进行中"; st.font = f; st.fontSize = 16;
        st.alignment = TextAnchor.MiddleCenter; st.color = new Color(1f, 0.25f, 0.2f);
        st.raycastTarget = false;
        statusGo.SetActive(false);
        ctrl._directorStatus = st;

        ctrl._totalRounds = player.TotalRounds;
        ctrl.Sync(player);
        return ctrl;
    }

    /// <summary>按名字解析文本引用（prefab 与代码创建通用，避免序列化引用失效）</summary>
    void ResolveTextRefs()
    {
        var red  = transform.Find("TeamBar/RedCard");
        var blue = transform.Find("TeamBar/BlueCard");
        _redName   = FindText(red,  "RN");  _redHp     = FindText(red,  "RH");
        _redGold   = FindText(red,  "RG");  _redScore  = FindText(red,  "RS");
        _redTower  = FindText(red,  "RTw"); _redWall   = FindText(red,  "RWl");
        _redMember = FindText(red,  "RMm"); _redBag    = FindText(red,  "RBg");
        _blueName   = FindText(blue, "BN");  _blueHp     = FindText(blue, "BH");
        _blueGold   = FindText(blue, "BG");  _blueScore  = FindText(blue, "BS");
        _blueTower  = FindText(blue, "BTw"); _blueWall   = FindText(blue, "BWl");
        _blueMember = FindText(blue, "BMm"); _blueBag    = FindText(blue, "BBg");
    }

    static Text FindText(Transform parent, string name)
    {
        if (parent == null) return null;
        var t = parent.Find(name);
        return t != null ? t.GetComponent<Text>() : null;
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
            foreach(var item in agg) bag.Append(item.Key).Append("x").Append(item.Value).Append(" ");
            string bagStr=bag.Length>0?bag.ToString().Trim():"空";

            // 队伍类型→卡片：defender=红方，challenger=蓝方（与 TeamColorApplicator 一致）
            bool isRed = st.type=="defender";
            var name=isRed?_redName:_blueName;     var hpT=isRed?_redHp:_blueHp;
            var goldT=isRed?_redGold:_blueGold;    var scoreT=isRed?_redScore:_blueScore;
            var wallT=isRed?_redWall:_blueWall;    var towerT=isRed?_redTower:_blueTower;
            var memberT=isRed?_redMember:_blueMember; var bagT=isRed?_redBag:_blueBag;

            if(name!=null){ name.text=isRed?"红方":"蓝方"; name.color=isRed?colRed:colBlue; }
            if(hpT!=null) hpT.text="基地 "+hp;
            if(goldT!=null) goldT.text="金币 "+st.gold;
            if(scoreT!=null) scoreT.text="积分 "+st.score;
            if(wallT!=null) wallT.text="围墙 "+walls+"/"+MAX_WALL;
            if(towerT!=null) towerT.text="防御塔 "+towers+"/"+MAX_TOWER;
            if(memberT!=null) memberT.text="人数 "+members+"/"+MAX_MEMBER;
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
    }

    // ── helpers ──
    static GameObject Bar(Transform p,string n,float x,float y,float w,float h,Color bg){var g=new GameObject(n);g.transform.SetParent(p,false);var r=g.AddComponent<RectTransform>();r.anchorMin=new Vector2(0.5f,0);r.anchorMax=new Vector2(0.5f,0);r.pivot=new Vector2(0.5f,0);r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);g.AddComponent<Image>().color=bg;return g;}
    static Text Lbl(Transform p,string n,string t,int sz,float x,float y,float w,float h,Color c,Font f,TextAnchor a){return LblA(p,n,t,0,1,0,1,sz,x,y,w,h,c,f,a);}
    static Text LblA(Transform p,string n,string t,float ax,float ay,float px,float py,int sz,float x,float y,float w,float h,Color c,Font f,TextAnchor a){var g=new GameObject(n);g.transform.SetParent(p,false);var r=g.AddComponent<RectTransform>();r.anchorMin=new Vector2(ax,ay);r.anchorMax=new Vector2(ax,ay);r.pivot=new Vector2(px,py);r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);var tt=g.AddComponent<Text>();tt.text=t;tt.font=f;tt.fontSize=sz;tt.alignment=a;tt.color=c;tt.raycastTarget=false;return tt;}
    static GameObject Ch(Transform p,string n){var g=new GameObject(n);g.transform.SetParent(p,false);return g;}
    static (Button btn,Text label) Btn(Transform p,string n,string l,Font f,float x,float w,float h,Color bg,UnityEngine.Events.UnityAction cb,int fsz=16){var g=new GameObject(n);g.transform.SetParent(p,false);var r=g.AddComponent<RectTransform>();r.anchorMin=new Vector2(0.5f,0.5f);r.anchorMax=new Vector2(0.5f,0.5f);r.pivot=new Vector2(0.5f,0.5f);r.anchoredPosition=new Vector2(x,0);r.sizeDelta=new Vector2(w,h);var i=g.AddComponent<Image>();i.color=bg;var b=g.AddComponent<Button>();b.onClick.AddListener(cb);var lg=new GameObject("L");lg.transform.SetParent(g.transform,false);var lr=lg.AddComponent<RectTransform>();lr.anchorMin=Vector2.zero;lr.anchorMax=Vector2.one;lr.offsetMin=Vector2.zero;lr.offsetMax=Vector2.zero;var lt=lg.AddComponent<Text>();lt.text=l;lt.font=f;lt.fontSize=fsz;lt.alignment=TextAnchor.MiddleCenter;lt.color=Color.white;lt.raycastTarget=false;return(b,lt);}
    static (Button btn,Text label) Btni(Transform p,string n,string l,Font f,float x,float w,float h,Color bg,UnityEngine.Events.UnityAction cb){return Btn(p,n,l,f,x,w,h,bg,cb);}
}
