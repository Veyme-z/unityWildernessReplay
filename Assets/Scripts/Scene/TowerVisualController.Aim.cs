// TowerVisualController 攻击/瞄准主循环（Partial Class）
// 职责：Fire/开火表现（炮塔转向 + 后坐力 + 各塔原生特效分派）、ResetAttack（Seek/复位清状态）、
//       LateUpdate 每帧调度器（瞄准/后坐力/枪口粒子逐帧 + 调用各特效文件的逐帧更新方法）。
using UnityEngine;

public partial class TowerVisualController : MonoBehaviour
{
    /// <summary>触发一次攻击表现：单目标（只由真实 Replay attack 事件调用）。</summary>
    public void Fire(Vector3 targetWorldPos) { Fire(new Vector3[] { targetWorldPos }); }

    /// <summary>
    /// 触发一次攻击表现（多目标）：转向主目标 + 后坐力 + 枪口特效 + 按塔类型分派特效。
    /// 30 加特林(Minigun)=原生粒子 + N 条弹道 + 落点火花；31 电磁狙击炮(Laser)=多束激光延伸；32 火箭(Rocket)=导弹直飞。
    /// </summary>
    public void Fire(Vector3[] targetWorldPositions)
    {
        if (!_setup || _turret == null) return;
        if (targetWorldPositions == null || targetWorldPositions.Length == 0) return;
        Vector3 primary = targetWorldPositions[0];

        // 炮塔转向 + 后坐力 + 播放原生枪口粒子
        FireMuzzleOnly(primary);

        // SciFi 塔原生特效（替代旧程序化 Tracer/命中环/电击）
        if (_towerType == "Laser") { ShowLaserBeam(primary); return; }
        if (_towerType == "Rocket") { LaunchRockets(primary); return; }
        if (_towerType == "AntiAir") return;  // 仅原生枪口粒子
        if (_towerType == "Minigun")
        {
            // 加特林：每发子弹到落点画粗弹道线 + 命中火花，直观显示打到哪些机器人
            foreach (var wp in targetWorldPositions)
            {
                SpawnTracer(wp);
                SpawnGatlingHit(wp);
            }
            return;
        }

        // 旧 CubeTowerDefense 塔类型兜底（当前不再加载）
        if (_towerType == "Flamethrower") return;
        if (_towerType == "RPG") { HitAt(primary); return; }
        foreach (var wp in targetWorldPositions) { SpawnTracer(wp); SpawnHitRing(wp); }
    }

    /// <summary>塔开火：炮塔转向目标 + 后坐力 + 枪口粒子/闪光，但**不下发目标命中效果**（留给飞行弹体到达时）。</summary>
    public void FireMuzzleOnly(Vector3 targetWorldPos)
    {
        if (!_setup || _turret == null) return;

        Vector3 fullDir = targetWorldPos - _turret.position;
        Vector3 dir = fullDir;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = _turret.forward;
        _aimWorldDir = dir.normalized;
        // 完整 3D 方向：保留高度差，供炮塔上下俯仰跟随目标
        _aimWorldDir3D = fullDir.sqrMagnitude < 0.0001f ? _aimWorldDir : fullDir.normalized;
        _hasAim = true;
        _aimT = aimHoldDuration;

        // 两阶段后坐力：从当前状态自然重新触发（位置恒为 base + offset，不累计漂移）
        _recoilKicking = true;
        _recoilT = 0f;

        // 播放一次枪口粒子（电磁炮 RPG 除外：枪口特效由电球/原生承担，避免叠加）
        if (_towerType != "RPG")
        {
            foreach (var ps in _muzzleParticles)
                if (ps != null) ps.Play();
            if (_muzzleParticles.Length > 0)
            {
                _particlesFired = true;
                _fireTime = Time.time;
            }
            // 旧塔用程序化枪口点光；SciFi 塔用素材包原生粒子，不再加
            if (_towerType != "Minigun" && _towerType != "AntiAir" && _towerType != "Laser" && _towerType != "Rocket")
                SpawnMuzzleFlash();
        }
    }

    /// <summary>清除攻击状态（Seek 跳转后调用）：清空转向/后坐力/粒子/闪光/弹道/命中闪光/激光/火箭，复位待机 180°。</summary>
    public void ResetAttack()
    {
        _hasAim = false;
        _aimT = 0f;
        _aimWorldDir = Vector3.forward;
        _aimWorldDir3D = Vector3.forward;
        _recoilKicking = false;
        _recoilT = 0f;
        _particlesFired = false;
        ApplyIdle();
        foreach (var ps in _muzzleParticles)
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (_flashLight != null)
        {
            _flashLight.intensity = 0f;
            _flashLight.gameObject.SetActive(false);
        }
        _flashT = 0f;
        ClearTracer();
        ClearHitRing();
        // 关闭 SciFi 塔原生特效
        HideLaserBeam();
        _rocketFlying = false;
        ResetRocketMissiles();
    }

    /// <summary>每帧调度器：瞄准/后坐力/枪口粒子逐帧 + 各特效文件逐帧更新。暂停时全部冻结。</summary>
    void LateUpdate()
    {
        if (!_setup || _turret == null) return;

        // Seek 检测：大幅跳转 → 清除旧攻击状态
        if (_player != null && _player.cur != _lastRound)
        {
            bool seeked = Mathf.Abs(_player.cur - _lastRound) > 1;
            _lastRound = _player.cur;
            if (seeked) ResetAttack();
        }

        // 暂停冻结：炮塔/后坐力/粒子/特效全部静止
        bool playing = _player == null || _player.playing;
        if (!playing)
        {
            if (!_particleFrozen) { FreezeParticles(true); _particleFrozen = true; }
            return;
        }
        if (_particleFrozen) { FreezeParticles(false); _particleFrozen = false; }

        // ── 完全空闲快速退出：无瞄准/后坐/闪光/粒子/弹道/命中/激光/火箭时只做待机对齐 ──
        bool hasActive = _hasAim || _recoilKicking || _recoilT < 1f || _flashT > 0f
                         || _activeTracers.Count > 0 || _particlesFired
                         || _activeHitRings.Count > 0
                         || _laserActiveT > 0f || _rocketFlying;
        if (!hasActive)
        {
            _recoilT = 1f;
            if (_turret.localPosition != _turretBaseLocalPos)
                _turret.localPosition = _turretBaseLocalPos;
            // 待机旋转：已到位则跳过，避免空闲塔每帧重复四元数计算
            Vector3 idleFwd = Quaternion.Euler(0f, idleYawOffset, 0f) * transform.forward;
            Quaternion idle = Quaternion.LookRotation(idleFwd, Vector3.up);
            if (Quaternion.Angle(_turret.rotation, idle) > 0.1f)
                _turret.rotation = Quaternion.RotateTowards(_turret.rotation, idle, turnSpeed * Time.deltaTime);
            return;
        }

        // 攻击瞄准保持计时：到期回到待机朝向（连续攻击时 Fire 会刷新 _aimT）
        if (_hasAim)
        {
            _aimT -= Time.deltaTime;
            if (_aimT <= 0f) _hasAim = false;
        }

        // 炮塔转向：水平 yaw 指向目标 + 上下俯仰 pitch 跟随目标高度；否则回到待机 180°
        Quaternion desired;
        if (_hasAim)
        {
            Vector3 flat = new Vector3(_aimWorldDir3D.x, 0f, _aimWorldDir3D.z);
            if (flat.sqrMagnitude < 0.0001f) flat = transform.forward;
            flat.Normalize();
            Quaternion yaw = Quaternion.LookRotation(flat, Vector3.up);
            // 高度差转俯仰角（正=向下，负=向上），绕炮塔自身 X 轴
            float pitchDeg = Mathf.Asin(Mathf.Clamp(-_aimWorldDir3D.y, -1f, 1f)) * Mathf.Rad2Deg;
            pitchDeg = Mathf.Clamp(pitchDeg, -pitchLimit, pitchLimit);
            desired = yaw * Quaternion.Euler(pitchDeg, 0f, 0f);
        }
        else
        {
            Vector3 idleFwd = Quaternion.Euler(0f, idleYawOffset, 0f) * transform.forward;
            desired = Quaternion.LookRotation(idleFwd, Vector3.up);
        }
        _turret.rotation = Quaternion.RotateTowards(_turret.rotation, desired, turnSpeed * Time.deltaTime);

        // 后坐力两阶段：快速后退（EaseOutCubic）+ 平滑恢复（Smooth01），位置恒为 base+offset 不漂移
        if (_recoilKicking)
        {
            _recoilT += Time.deltaTime / recoilKickDuration;
            if (_recoilT >= 1f) { _recoilT = 0f; _recoilKicking = false; }
            _turret.localPosition = _turretBaseLocalPos + new Vector3(0f, 0f, -recoilDistance * EaseOutCubic(_recoilT));
        }
        else if (_recoilT < 1f)
        {
            _recoilT += Time.deltaTime / recoilRecoverDuration;
            _turret.localPosition = _turretBaseLocalPos + new Vector3(0f, 0f, -recoilDistance * (1f - Smooth01(_recoilT)));
        }
        else if (_turret.localPosition != _turretBaseLocalPos)
        {
            _turret.localPosition = _turretBaseLocalPos;
        }

        // 枪口粒子：发射后短暂停止发射，防止循环开火
        if (_particlesFired && Time.time > _fireTime + particleDuration)
        {
            _particlesFired = false;
            foreach (var ps in _muzzleParticles)
                if (ps != null && ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // 枪口闪光衰减：到期禁用（不销毁，下次攻击重新 SetActive）
        if (_flashT > 0f && _flashLight != null)
        {
            _flashT -= Time.deltaTime / muzzleLightDuration;
            _flashLight.intensity = _flashT > 0f ? 3f * _flashT : 0f;
            if (_flashT <= 0f) { _flashT = 0f; _flashLight.gameObject.SetActive(false); }
        }

        // SciFi 塔原生特效逐帧（激光显示计时/火箭推进）
        UpdateLaserFx();
        UpdateRocketFx();

        // 攻击目标可视化逐帧（弹道淡出/命中环三阶段）
        UpdateTracersFx();
        UpdateHitRingsFx();
    }
}
