// UnitView 的动画子模块（Partial Class）
// 职责：Animator 运行时装配（Robot/Worker 覆盖）、动画状态同步、攻击/采集/死亡触发、播放倍速
// 字段声明与主流程见 UnitView.cs

using UnityEngine;

public partial class UnitView
{
    public static float AnimatorSpeed = 1f; // 由 ReplayPlayer 同步播放倍速

    /// <summary>每帧动画状态同步（LateUpdate 子模块：仅负责 Animator speed / isMoving 同步）。</summary>
    void UpdateAnimationState(bool isMovingNow, bool posChanged, Vector3 moveDir)
    {
        if (_animator != null)
        {
            try
            {
                // 暂停时冻结动画（ReplayPlayer 引用全局缓存，避免大量单位各自 FindObjectOfType）
                if (_player == null)
                {
                    if (s_cachedPlayer == null) s_cachedPlayer = FindObjectOfType<ReplayPlayer>();
                    _player = s_cachedPlayer;
                }
                bool replayPlaying = _player == null || _player.playing;
                float targetAnimSpeed;
                if (!replayPlaying)
                {
                    targetAnimSpeed = 0f;
                }
                else if (isMovingNow)
                {
                    float realSpeed = posChanged ? moveDir.magnitude / Time.deltaTime : 0f;
                    targetAnimSpeed = Mathf.Clamp(realSpeed * strideCoefficient, 0.15f, 4.5f) * AnimatorSpeed;
                }
                else
                {
                    targetAnimSpeed = AnimatorSpeed;
                }

                // 仅在目标速度变化时写入，静止单位不再每帧赋值 Animator.speed
                if (targetAnimSpeed != _animSpeed)
                {
                    _animSpeed = targetAnimSpeed;
                    _animator.speed = targetAnimSpeed;
                }

                if (_hasParams && isMovingNow != _wasMoving)
                {
                    _wasMoving = isMovingNow;
                    _animator.SetBool("isMoving", isMovingNow);
                }
            }
            catch (System.Exception) { }
        }
    }

    void SetupRobotAnimator()
    {
        if (_animator.runtimeAnimatorController == null) return;
        if (_animator.parameterCount > 0) { _hasParams = true; return; }

        var baseCtrl = Resources.Load<RuntimeAnimatorController>("Animations/Skeleton_AnimatorController");
        if (baseCtrl == null) return;
        var overrides = new AnimatorOverrideController(baseCtrl);
        var robotClips = _animator.runtimeAnimatorController.animationClips;
        if (robotClips != null && robotClips.Length > 0)
        {
            var idleClip  = FindClip(robotClips, "Idle");
            var walkClip  = FindClip(robotClips, "Walk", "Run", "Fly", "Dash");
            var atkClip   = FindClip(robotClips, "Attack", "Punch", "Slash", "Claw", "Projectile", "Slam");
            var deathClip = FindClip(robotClips, "Die", "Death");
            overrides["Idle_A"]    = idleClip ?? robotClips[0];
            overrides["Walking_A"] = walkClip ?? idleClip ?? robotClips[0];
            overrides["Hit_A"]     = atkClip  ?? idleClip ?? robotClips[0];
            overrides["Death_A"]   = deathClip ?? idleClip ?? robotClips[0];
        }
        _animator.runtimeAnimatorController = overrides;
        _hasParams = true;
    }

    /// <summary>worker(type=6)：用 AnimatorOverrideController 把砍劈动画 Hit_A 替换为调整过的 Hit_Worker。</summary>
    void ApplyWorkerHitOverride()
    {
        var hitClip = Resources.Load<AnimationClip>("Animations/Hit_Worker");
        if (_animator == null || hitClip == null) return;
        if (_animator.runtimeAnimatorController == null) return;
        if (_animator.runtimeAnimatorController is AnimatorOverrideController) return;

        var overrides = new AnimatorOverrideController(_animator.runtimeAnimatorController);
        overrides["Hit_A"] = hitClip;
        _animator.runtimeAnimatorController = overrides;
    }

    /// <summary>从 clips 中按优先级匹配第一个包含关键字的动画。</summary>
    static AnimationClip FindClip(AnimationClip[] clips, params string[] keywords)
    {
        foreach (var kw in keywords)
        {
            foreach (var c in clips)
            {
                if (c != null && c.name.IndexOf(kw, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return c;
            }
        }
        return null;
    }

    /// <summary>更新动画状态（外部调用 — 仅负责 Trigger，isMoving 由 LateUpdate 统一管理）</summary>
    public void UpdateAnimation(bool isMoving, bool isDead)
    {
        if (_animator == null) return;
        // 只在死亡状态发生变化时触发一次（ReplayPlayer 每帧调用，避免死亡期间重复 SetTrigger 空耗）
        if (isDead == _wasDead) return;
        _wasDead = isDead;
        try
        {
            if (isDead)
            {
                if (_hasParams) _animator.SetTrigger("onDeath");
                else _animator.Play("Die");
            }
        }
        catch (System.Exception) { }
    }

    /// <summary>触发攻击动画</summary>
    public void TriggerAttack()
    {
        // 远处静态野兽攻击时临时恢复骨骼动画（播放攻击动作，随后自动回静态）。
        // 冷却 2.5s + 窗口 1.0s（占空比 ~40%）：频繁攻击的野兽只在一部分攻击时动画，限制并发动画数，
        // 否则夜间上百只野兽同时攻击会全部进动画 → CPU 回升（实测跳转后 101/140 远处野兽动画）。
        if (_lodStatic && _skinned != null && Time.time - _lastTransientEnter > LodTransientCooldown)
        {
            _lastTransientEnter = Time.time;
            _transientAnimUntil = Time.time + LodTransientWindow;
            SetLodStatic(false);
        }
        if (_animator == null) return;
        try
        {
            if (_hasParams) _animator.SetTrigger("onAttack");
            else _animator.Play("Take Damage");
        }
        catch (System.Exception) { }
    }

    /// <summary>触发采集动作：挥臂砍劈（复用 onAttack → Hit 砍劈动画）。</summary>
    public void TriggerCollect()
    {
        TriggerAttack();
    }

    /// <summary>触发死亡动画</summary>
    public void TriggerDeath()
    {
        // 远处静态野兽死亡时临时恢复骨骼动画，播放死亡动作后再随视图销毁
        if (_lodStatic && _skinned != null)
        {
            _transientAnimUntil = Time.time + 1.2f;
            SetLodStatic(false);
        }
        if (_animator == null) return;
        try
        {
            if (_hasParams) _animator.SetTrigger("onDeath");
            else _animator.Play("Die");
        }
        catch (System.Exception) { }
    }

    public void SetAnimScale(float s) { state.animScale = s; }
}
