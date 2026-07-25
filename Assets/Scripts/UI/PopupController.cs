using System;
using UnityEngine;

namespace SuperQQ.UI
{
    /// <summary>
    /// 弹窗控制器 — 挂载到弹窗 Prefab 根节点上
    /// 负责自动关闭倒计时、关闭回调和弹窗生命周期
    /// 由 PopupManager 创建时自动添加并初始化
    /// </summary>
    public class PopupController : MonoBehaviour
    {
        // 自动关闭倒计时（秒），0 表示不自动关闭
        private float _autoCloseDuration;

        // 已经过的时间
        private float _elapsedTime;

        // 是否已初始化
        private bool _bIsInitialized;

        // 关闭回调
        private Action<PopupController> _onCloseCallback;

        // ==================== 公开查询 ====================

        /// <summary>
        /// 剩余自动关闭时间，不自动关闭时返回 -1
        /// </summary>
        public float RemainingTime
        {
            get
            {
                if (_autoCloseDuration <= 0f)
                {
                    return -1f;
                }
                return Mathf.Max(0f, _autoCloseDuration - _elapsedTime);
            }
        }

        /// <summary>
        /// 是否已初始化
        /// </summary>
        public bool BIsInitialized => _bIsInitialized;

        // ==================== 生命周期 ====================

        private void Update()
        {
            if (!_bIsInitialized)
            {
                return;
            }

            if (_autoCloseDuration <= 0f)
            {
                return;
            }

            _elapsedTime += Time.deltaTime;
            if (_elapsedTime >= _autoCloseDuration)
            {
                Close();
            }
        }

        // ==================== 初始化 ====================

        /// <summary>
        /// 初始化弹窗控制器，由 PopupManager 调用
        /// </summary>
        /// <param name="autoCloseDuration">自动关闭时长，0 表示不自动关闭</param>
        /// <param name="onCloseCallback">关闭时的回调函数</param>
        public void Initialize(float autoCloseDuration, Action<PopupController> onCloseCallback)
        {
            _autoCloseDuration = autoCloseDuration;
            _elapsedTime = 0f;
            _onCloseCallback = onCloseCallback;
            _bIsInitialized = true;
        }

        // ==================== 关闭 ====================

        /// <summary>
        /// 关闭弹窗：触发回调、重置状态、禁用 GameObject
        /// 由自动关闭倒计时或 PopupManager.ClosePopup 调用
        /// </summary>
        public void Close()
        {
            if (!_bIsInitialized)
            {
                return;
            }

            // 触发关闭回调
            _onCloseCallback?.Invoke(this);

            // 重置状态
            _bIsInitialized = false;
            _autoCloseDuration = 0f;
            _elapsedTime = 0f;
            _onCloseCallback = null;

            // 禁用对象，由 PopupManager 对象池回收
            gameObject.SetActive(false);
        }

        // ==================== 重置（对象池复用前调用） ====================

        /// <summary>
        /// 重置弹窗状态，对象池取出复用前由 PopupManager 调用
        /// </summary>
        public void ResetState()
        {
            _bIsInitialized = false;
            _autoCloseDuration = 0f;
            _elapsedTime = 0f;
            _onCloseCallback = null;
        }
    }
}
