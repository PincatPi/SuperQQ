using System;
using System.Collections.Generic;
using UnityEngine;

namespace SuperQQ.Player
{
    /// <summary>
    /// 玩家会话管理器 — 跨场景持久化的玩家身份档案中心
    /// 只持有纯数据 PlayerProfile，不持有任何 MonoBehaviour 引用
    /// 关卡场景加载时由 LevelPlayerRegistry 读取 Profile 列表实例化 PlayerController
    /// 在准备阶段由组队 UI 调用 RegisterProfile 注册玩家档案
    /// 持久化层：与 PlayerScoreManager 同层，随 DontDestroyOnLoad 跨场景保留
    /// </summary>
    public class PlayerSessionManager : MonoBehaviour
    {
        // 单例实例
        private static PlayerSessionManager _instance;

        // 玩家档案列表，按注册顺序保留（Player1 → Player2 → ...）
        // 此顺序即结算轨道的固定展示顺序
        private readonly List<PlayerProfile> _profiles = new();

        // ==================== 公开事件 ====================

        /// <summary>
        /// 新玩家档案注册事件
        /// 参数为新注册的 PlayerProfile
        /// PlayerScoreManager 订阅此事件为新玩家初始化得分记录
        /// </summary>
        public event Action<PlayerProfile> OnProfileRegistered;

        // ==================== 单例访问 ====================

        /// <summary>
        /// 全局唯一实例，供外部访问
        /// </summary>
        public static PlayerSessionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<PlayerSessionManager>();
                }
                return _instance;
            }
        }

        // ==================== 公开查询 ====================

        /// <summary>
        /// 所有已注册玩家档案（按注册顺序，只读视图）
        /// </summary>
        public IReadOnlyList<PlayerProfile> Profiles => _profiles;

        /// <summary>
        /// 已注册玩家数量
        /// </summary>
        public int PlayerCount => _profiles.Count;

        // ==================== 生命周期 ====================

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        // ==================== 档案注册 ====================

        /// <summary>
        /// 注册一个玩家档案
        /// 按身份主键（PlayerId 优先，回退 PlayerName）去重，相同主键不会重复注册
        /// 注册成功后发出 OnProfileRegistered 事件
        /// </summary>
        /// <param name="profile">待注册的玩家档案</param>
        public void RegisterProfile(PlayerProfile profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.IdentityKey))
            {
                return;
            }

            if (HasPlayerByIdentity(profile.IdentityKey))
            {
                return;
            }

            _profiles.Add(profile);
            OnProfileRegistered?.Invoke(profile);
        }

        /// <summary>
        /// 移除一个玩家档案
        /// 通常在玩家退出房间时调用
        /// </summary>
        /// <param name="playerName">玩家名称</param>
        public void UnregisterProfile(string playerName)
        {
            if (string.IsNullOrEmpty(playerName))
            {
                return;
            }

            for (int i = 0; i < _profiles.Count; i++)
            {
                if (_profiles[i].PlayerName == playerName)
                {
                    _profiles.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// 清空所有玩家档案
        /// 通常在退出整场游戏返回主菜单时调用
        /// </summary>
        public void ClearAllProfiles()
        {
            _profiles.Clear();
        }

        // ==================== 查询接口 ====================

        /// <summary>
        /// 判断是否已注册指定名称的玩家
        /// </summary>
        /// <param name="playerName">玩家名称</param>
        public bool HasPlayer(string playerName)
        {
            if (string.IsNullOrEmpty(playerName))
            {
                return false;
            }

            for (int i = 0; i < _profiles.Count; i++)
            {
                if (_profiles[i].PlayerName == playerName)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 获取指定名称的玩家档案
        /// </summary>
        /// <param name="playerName">玩家名称</param>
        /// <returns>玩家档案，未找到时返回 null</returns>
        public PlayerProfile GetProfile(string playerName)
        {
            if (string.IsNullOrEmpty(playerName))
            {
                return null;
            }

            for (int i = 0; i < _profiles.Count; i++)
            {
                if (_profiles[i].PlayerName == playerName)
                {
                    return _profiles[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 按身份主键（PlayerId 或 PlayerName）判断是否已注册
        /// </summary>
        public bool HasPlayerByIdentity(string identityKey)
        {
            return GetProfileByIdentity(identityKey) != null;
        }

        /// <summary>
        /// 按身份主键（PlayerId 或 PlayerName）获取玩家档案
        /// 联机模式传 PlayerId，单机模式传 PlayerName 均可命中
        /// </summary>
        /// <returns>玩家档案，未找到时返回 null</returns>
        public PlayerProfile GetProfileByIdentity(string identityKey)
        {
            if (string.IsNullOrEmpty(identityKey))
            {
                return null;
            }

            for (int i = 0; i < _profiles.Count; i++)
            {
                if (_profiles[i].IdentityKey == identityKey)
                {
                    return _profiles[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 获取所有玩家名称（按注册顺序）
        /// 用于结算页固定展示顺序，与得分排名无关
        /// </summary>
        public List<string> GetOrderedPlayerNames()
        {
            List<string> names = new List<string>();
            for (int i = 0; i < _profiles.Count; i++)
            {
                if (!string.IsNullOrEmpty(_profiles[i].PlayerName))
                {
                    names.Add(_profiles[i].PlayerName);
                }
            }
            return names;
        }
    }
}
