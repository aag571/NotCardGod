using System;
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;
using UnityEngine;

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
    public string LoginCode { get; private set; }
    public string AnonymousCode { get; private set; }
    public TTUserInfo UserInfo { get; private set; }

    public event Action<bool, string> InitializeCompleted;
    public event Action<string, string, bool> LoginCompleted;
    public event Action<TTUserInfo> UserInfoUpdated;
    public event Action<string> LoginFailed;
    public event Action<bool> ApplicationVisibilityChanged;

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

        TT.Login(
            (code, anonymousCode, isLogin) =>
            {
                LoginCode = code;
                AnonymousCode = anonymousCode;
                IsLoggedIn = isLogin;
                LoginCompleted?.Invoke(code, anonymousCode, isLogin);
                callback?.Invoke(isLogin, code, anonymousCode);
            },
            error =>
            {
                IsLoggedIn = false;
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
                UserInfoUpdated?.Invoke(userInfo);
                onSuccess?.Invoke(userInfo);
            },
            error => onFailure?.Invoke(error));
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
        TT.GetAppLifeCycle().OnShow += data => OnApplicationShow();
        TT.GetAppLifeCycle().OnHide += () => OnApplicationHide();
    }

    private void OnApplicationShow() => ApplicationVisibilityChanged?.Invoke(true);
    private void OnApplicationHide() => ApplicationVisibilityChanged?.Invoke(false);
}
