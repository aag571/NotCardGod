using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Newtonsoft.Json;
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;
using Unity.UOS.Auth;
using Unity.UOS.CloudSave;
using Unity.UOS.CloudSave.Model.Files;
using UnityEngine;
using FileOptions = Unity.UOS.CloudSave.Model.Files.FileOptions;

public sealed class DouyinTTSDKService : MonoBehaviour
{
    [Serializable]
    public sealed class Settings
    {
        public string rewardedVideoAdId;
        public bool forceLogin = true;
        public bool autoLoginOnInitialize = true;
        public string sidebarScene = "sidebar";
    }

    public static DouyinTTSDKService Instance { get; private set; }
    public Settings Configuration = new Settings();
    public bool IsInitialized { get; private set; }
    public bool IsLoggedIn { get; private set; }
    public bool IsGuest { get; private set; }
    public bool CanUsePlatformFeatures => IsInitialized && IsLoggedIn && !IsGuest;
    public string UserId { get; private set; }
    public string LoginCode { get; private set; }
    public string AnonymousCode { get; private set; }
    public TTUserInfo UserInfo { get; private set; }
    public string DisplayName { get; private set; }
    public string AvatarUrl { get; private set; }

    public event Action<bool, string> InitializeCompleted;
    public event Action<string, string, bool> LoginCompleted;
    public event Action<TTUserInfo> UserInfoUpdated;
    public event Action<string> LoginFailed;
    public event Action<bool> ApplicationVisibilityChanged;
    private object latestApplicationShowData;
    private bool sidebarLaunchObserved;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Initialize(Action<bool, string> callback = null)
    {
        if (IsInitialized)
        {
            callback?.Invoke(true, string.Empty);
            return;
        }

        if (Application.isEditor)
        {
            IsInitialized = true;
            InitializeCompleted?.Invoke(true, string.Empty);
            callback?.Invoke(true, string.Empty);
            if (Configuration.autoLoginOnInitialize)
                Login(Configuration.forceLogin);
            return;
        }

        TT.InitSDK((code, environment) =>
        {
            IsInitialized = code == 0;
            string message = IsInitialized ? string.Empty : "TTSDK 初始化失败，错误码：" + code;
            InitializeCompleted?.Invoke(IsInitialized, message);
            callback?.Invoke(IsInitialized, message);
            if (IsInitialized && Configuration.autoLoginOnInitialize)
                Login(Configuration.forceLogin);
        });
    }

    public void Login(bool forceLogin, Action<bool, string, string> callback = null)
    {
        if (!IsInitialized)
        {
            callback?.Invoke(false, string.Empty, "TTSDK 尚未初始化");
            return;
        }

        if (Application.isEditor)
        {
            IsGuest = true;
            IsLoggedIn = true;
            LoginCode = "guest";
            AnonymousCode = SystemInfo.deviceUniqueIdentifier;
            UserId = "guest_" + (string.IsNullOrEmpty(AnonymousCode) ? "editor" : AnonymousCode);
            LoginCompleted?.Invoke(LoginCode, AnonymousCode, true);
            callback?.Invoke(true, LoginCode, AnonymousCode);
            return;
        }

        TT.Login(
            (code, anonymousCode, isLogin) =>
            {
                LoginCode = code;
                AnonymousCode = anonymousCode;
                IsLoggedIn = isLogin;
                IsGuest = false;
                UserId = isLogin ? code : anonymousCode;
                LoginCompleted?.Invoke(code, anonymousCode, isLogin);
                callback?.Invoke(isLogin, code, anonymousCode);
            },
            error =>
            {
                IsLoggedIn = false;
                IsGuest = false;
                UserId = null;
                LoginFailed?.Invoke(error);
                callback?.Invoke(false, string.Empty, error);
            },
            forceLogin);
    }

    public void GetUserInfo(Action<TTUserInfo> onSuccess = null, Action<string> onFailure = null)
    {
        TT.GetUserInfo(
            (ref TTUserInfo userInfo) =>
            {
                UserInfo = userInfo;
                DisplayName = ReadUserInfoString(userInfo, "nickName", "nickname", "name");
                AvatarUrl = ReadUserInfoString(userInfo, "avatarUrl", "avatar", "avatarUrl100", "avatarUrl200");
                UserInfoUpdated?.Invoke(userInfo);
                onSuccess?.Invoke(userInfo);
            },
            error => onFailure?.Invoke(error));
    }

    public void ApplyFallbackProfile()
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
            DisplayName = IsGuest ? "游客" : (UserId ?? "玩家");
    }

    private static string ReadUserInfoString(TTUserInfo userInfo, params string[] names)
    {
        object boxed = userInfo;
        var type = boxed.GetType();
        for (var index = 0; index < names.Length; index++)
        {
            var field = type.GetField(names[index], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (field != null)
            {
                var value = field.GetValue(boxed)?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            var property = type.GetProperty(names[index], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (property != null && property.CanRead)
            {
                var value = property.GetValue(boxed, null)?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return string.Empty;
    }

    public bool Save<T>(T data, string saveName = null)
    {
        return string.IsNullOrEmpty(saveName) ? TT.Save(data) : TT.Save(data, saveName);
    }

    public T Load<T>(string saveName = null)
    {
        return string.IsNullOrEmpty(saveName) ? TT.LoadSaving<T>() : TT.LoadSaving<T>(saveName);
    }

    public void DeleteSave<T>(string saveName = null)
    {
        if (string.IsNullOrEmpty(saveName))
            TT.DeleteSaving<T>();
        else
            TT.DeleteSaving<T>(saveName);
    }

    public TTRewardedVideoAd CreateRewardedVideoAd(Action<bool, int> onClose, Action<int, string> onError = null)
    {
        if (string.IsNullOrEmpty(Configuration.rewardedVideoAdId))
        {
            onError?.Invoke(-1, "未配置激励视频广告位 ID");
            return null;
        }

        return TT.CreateRewardedVideoAd(Configuration.rewardedVideoAdId, onClose, onError, false, null, 0, false);
    }

    public void ShowRewardedVideo(Action<bool, int> onClose, Action<int, string> onError = null)
    {
        CreateRewardedVideoAd(onClose, onError)?.Show();
    }

    public void CheckSidebar(Action<bool> onSupported, Action onComplete = null, Action<int, string> onError = null)
    {
        TT.CheckScene((TTSideBar.SceneEnum)0, onSupported, onComplete, onError);
    }

    public void EnterSidebar(Action onSuccess = null, Action onComplete = null, Action<int, string> onError = null)
    {
        var data = new JsonData();
        data["scene"] = Configuration.sidebarScene;
        TT.NavigateToScene(data, onSuccess, onComplete, onError);
    }

    /// <summary>
    /// 先校验当前宿主是否支持侧边栏场景，校验成功后才执行跳转。
    /// </summary>
    public void CheckAndEnterSidebar(Action onSuccess = null, Action onComplete = null, Action<int, string> onError = null)
    {
        if (!CanUsePlatformFeatures)
        {
            onError?.Invoke(-1, "TTSDK 尚未完成真实用户登录");
            return;
        }

        TT.CheckScene(
            (TTSideBar.SceneEnum)0,
            supported =>
            {
                if (supported)
                {
                    EnterSidebar(onSuccess, onComplete, onError);
                    return;
                }

                onError?.Invoke(-2, "当前宿主版本不支持侧边栏入口场景");
            },
            onComplete,
            onError);
    }

    /// <summary>
    /// 判断本次小游戏是否由抖音侧边栏启动。
    /// TTSDK 文档约定：侧边栏启动时 GetLaunchFrom 返回 homepage。
    /// </summary>
    public bool IsLaunchedFromSidebar()
    {
        if (sidebarLaunchObserved)
            return true;

        if (IsSidebarShowData(latestApplicationShowData))
            return true;

        var options = TT.GetLaunchOptionsSync();
        if (options == null)
            return false;

        if (options.Query != null)
        {
            var queryKeys = new[] { "scene", "from", "enter_from", "entry", "launch_from" };
            foreach (var key in queryKeys)
            {
                if (options.Query.TryGetValue(key, out var value) &&
                    string.Equals(value, Configuration.sidebarScene, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        var optionType = options.GetType();
        var launchMember = optionType.GetProperty("LaunchFrom") as MemberInfo ?? optionType.GetField("LaunchFrom");
        var launchValue = launchMember is PropertyInfo launchProperty
            ? launchProperty.GetValue(options, null)
            : launchMember is FieldInfo launchField ? launchField.GetValue(options) : null;
        if (string.Equals(launchValue?.ToString(), "homepage", StringComparison.OrdinalIgnoreCase))
            return true;

        var locationMember = optionType.GetProperty("Location") as MemberInfo ?? optionType.GetField("Location");
        var locationValue = locationMember is PropertyInfo locationProperty
            ? locationProperty.GetValue(options, null)
            : locationMember is FieldInfo locationField ? locationField.GetValue(options) : null;
        if (string.Equals(locationValue?.ToString(), "homepage", StringComparison.OrdinalIgnoreCase))
            return true;

        var sceneMember = optionType.GetProperty("Scene") as MemberInfo ?? optionType.GetField("Scene");
        var sceneValue = sceneMember is PropertyInfo sceneProperty
            ? sceneProperty.GetValue(options, null)
            : sceneMember is FieldInfo sceneField ? sceneField.GetValue(options) : null;
        return string.Equals(sceneValue?.ToString(), "homepage", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSidebarShowData(object data)
    {
        if (data == null)
            return false;

        if (data is IDictionary<string, object> values)
        {
            values.TryGetValue("launch_from", out var launchFrom);
            values.TryGetValue("location", out var location);
            return string.Equals(launchFrom?.ToString(), "homepage", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(location?.ToString(), "sidebar_card", StringComparison.OrdinalIgnoreCase);
        }

        var json = JsonConvert.SerializeObject(data);
        return json.IndexOf("\"launch_from\":\"homepage\"", StringComparison.OrdinalIgnoreCase) >= 0 &&
               json.IndexOf("\"location\":\"sidebar_card\"", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public void ShareRoomToFriend(string roomId, string joinCode, Action<bool, string> callback = null)
    {
        if (!CanUsePlatformFeatures)
        {
            callback?.Invoke(false, "TTSDK 尚未完成真实用户登录");
            return;
        }

        var data = new JsonData();
        data["title"] = "邀请你加入对战";
        data["desc"] = "点击进入游戏加入我的房间";
        data["query"] = $"roomId={Uri.EscapeDataString(roomId ?? string.Empty)}&joinCode={Uri.EscapeDataString(joinCode ?? string.Empty)}";
        TT.ShareMessageToFriend(
            data,
            _ => callback?.Invoke(true, string.Empty),
            error => callback?.Invoke(false, error),
            () => callback?.Invoke(false, "用户取消分享"));
    }

    public string GetLaunchQuery()
    {
        var options = TT.GetLaunchOptionsSync();
        if (options != null && options.Query != null && options.Query.TryGetValue("roomId", out var roomId))
        {
            options.Query.TryGetValue("joinCode", out var joinCode);
            return $"roomId={Uri.EscapeDataString(roomId ?? string.Empty)}&joinCode={Uri.EscapeDataString(joinCode ?? string.Empty)}";
        }
        return string.Empty;
    }

    public void RequestPromotionActivity(string activityId, Action<JsonData> onSuccess, Action onComplete = null, Action<int, string> onError = null)
    {
        var data = new JsonData();
        if (!string.IsNullOrEmpty(activityId)) data["activityId"] = activityId;
        TT.RequestPromotionActivity(data, onSuccess, onComplete, onError);
    }

    public void ReceiveCoupon(string activityId, Action<JsonData> onSuccess, Action onComplete = null, Action<int, string> onError = null)
    {
        var data = new JsonData();
        if (!string.IsNullOrEmpty(activityId)) data["activityId"] = activityId;
        TT.ReceiveCoupon(data, onSuccess, onComplete, onError);
    }

    private void OnEnable()
    {
        TT.GetAppLifeCycle().OnShow += data =>
        {
            latestApplicationShowData = data;
            var payload = data == null ? string.Empty : data.ToString();
            var serialized = IsSidebarShowData(data);
            sidebarLaunchObserved = serialized ||
                (payload.IndexOf("homepage", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 payload.IndexOf("sidebar_card", StringComparison.OrdinalIgnoreCase) >= 0);
            Debug.Log($"[Sidebar] OnShow launch observed={sidebarLaunchObserved}, payload={payload}");
            OnApplicationShow();
        };
        TT.GetAppLifeCycle().OnHide += () => OnApplicationHide();
    }

    private void OnApplicationShow() => ApplicationVisibilityChanged?.Invoke(true);
    private void OnApplicationHide() => ApplicationVisibilityChanged?.Invoke(false);
}

public sealed class UosCrudSaveService : MonoBehaviour
{
    public static UosCrudSaveService Instance { get; private set; }
    public bool IsReady { get; private set; }
    public string UserId { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public async Task<bool> InitializeAsync(string userId)
    {
        if (Application.isEditor || string.IsNullOrEmpty(userId))
            return false;

        try
        {
            userId = NormalizeUosUserId(userId);
            await CloudSaveSDK.InitializeAsync();
            var appId = Unity.UOS.Common.Settings.AppID;
            var appSecret = Unity.UOS.Common.Settings.AppSecret;
            AuthTokenManager.Initialize(appId, appSecret);
            await AuthTokenManager.GenerateAccessTokenWithUosAppId(appId, userId);
            UserId = userId;
            IsReady = true;
            return true;
        }
        catch (Exception exception)
        {
            IsReady = false;
            Debug.LogWarning($"UOS CRUD Save 初始化失败，将使用 TTSDK/本地存档：{exception.Message}");
            return false;
        }
    }

    public static string NormalizeUosUserId(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return userId;

        if (userId.Length <= 64)
            return userId;

        using (var sha256 = SHA256.Create())
        {
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(userId));
            var hash = BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
            return "tt_" + hash.Substring(0, 60);
        }
    }

    public async Task<string> CreateAsync<T>(string name, T value)
    {
        EnsureReady();
        return await CloudSaveSDK.Instance.Files.CreateAsync(name, Serialize(value));
    }

    public async Task<T> ReadAsync<T>(string name)
    {
        EnsureReady();
        var item = await FindByNameAsync(name);
        if (item == null)
            return default;

        var bytes = await CloudSaveSDK.Instance.Files.LoadBytesAsync(item.SaveId);
        return Deserialize<T>(bytes);
    }

    public async Task<string> UpdateAsync<T>(string name, T value)
    {
        EnsureReady();
        var item = await FindByNameAsync(name);
        if (item == null)
            return await CreateAsync(name, value);

        await CloudSaveSDK.Instance.Files.UpdateAsync(item.SaveId, new UpdateOptions
        {
            File = new FileOptions
            {
                UpdateFileWay = UpdateFileWay.ByFileBytes,
                FileBytes = Serialize(value)
            }
        });
        return item.SaveId;
    }

    public async Task DeleteAsync(string name, bool hardDelete = false)
    {
        EnsureReady();
        var item = await FindByNameAsync(name);
        if (item != null)
            await CloudSaveSDK.Instance.Files.DeleteAsync(item.SaveId, hardDelete);
    }

    public Task<LocalAccountState> ReadAccountAsync()
    {
        return ReadAsync<LocalAccountState>("NCG.LocalAccount");
    }

    public Task<string> UpdateAccountAsync(LocalAccountState state)
    {
        return UpdateAsync("NCG.LocalAccount", state);
    }

    private static async Task<SaveItem> FindByNameAsync(string name)
    {
        var saves = await CloudSaveSDK.Instance.Files.ListAllAsync();
        for (var i = 0; i < saves.Count; i++)
        {
            if (string.Equals(saves[i].Name, name, StringComparison.Ordinal))
                return saves[i];
        }

        return null;
    }

    private static byte[] Serialize<T>(T value)
    {
        return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));
    }

    private static T Deserialize<T>(byte[] bytes)
    {
        return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(bytes));
    }

    private void EnsureReady()
    {
        if (!IsReady)
            throw new InvalidOperationException("UOS CRUD Save 尚未初始化");
    }
}
