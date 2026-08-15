using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 金币跟随锚点翻转器 — 挂在玩家子物体 CoinsFollowPoint 上
    /// 玩家面朝方向（PlayerController.FacingDir）改变时，本物体相对玩家的本地 X 坐标
    /// 在 flipDuration 内平滑取反（如 -0.7 ↔ +0.7），使锚点始终位于玩家身后；
    /// 本地 Y/Z 保持不变（锚点高度在 Inspector 中通过 localPosition.y 配置）
    ///
    /// 翻转途中再次转身：以当前位置为起点重新插值，方向抖动不会跳变
    /// </summary>
    public class CoinsFollowPointFlip : MonoBehaviour
    {
        [Tooltip("锚点与玩家的水平距离（米，取正值即可）；朝右时锚点在 -X 侧，朝左时在 +X 侧")]
        [SerializeField, Min(0f)] private float behindDistance = 0.7f;
        [Tooltip("换侧平滑时长（秒）")]
        [SerializeField, Min(0.01f)] private float flipDuration = 0.25f;

        private PlayerController player;
        private float lastFacingSign = 1f;
        private float flipFromX;        // 本次翻转起始的本地 X
        private float flipTimer;        // >0 表示正在翻转插值中

        private void Awake()
        {
            player = GetComponentInParent<PlayerController>();

            // 初始就位：直接落到当前朝向的身后侧，不做插值
            lastFacingSign = CurrentFacingSign();
            Vector3 local = transform.localPosition;
            local.x = TargetLocalX();
            transform.localPosition = local;
        }

        private void Update()
        {
            float sign = CurrentFacingSign();
            if (sign != lastFacingSign)
            {
                lastFacingSign = sign;
                flipFromX = transform.localPosition.x;
                flipTimer = flipDuration;
            }
            if (flipTimer > 0f)
            {
                flipTimer -= Time.deltaTime;
                float t = 1f - Mathf.Max(flipTimer, 0f) / flipDuration;
                Vector3 local = transform.localPosition;
                local.x = Mathf.Lerp(flipFromX, TargetLocalX(), Mathf.SmoothStep(0f, 1f, t));
                transform.localPosition = local;
            }
        }

        /// <summary>当前面朝符号（+1 朝右 / -1 朝左；无玩家组件时默认朝右）</summary>
        private float CurrentFacingSign()
        {
            return player == null || player.FacingDir >= 0f ? 1f : -1f;
        }

        /// <summary>目标本地 X：始终落在玩家面朝的反方向（身后）</summary>
        private float TargetLocalX()
        {
            return -lastFacingSign * behindDistance;
        }
    }
}
