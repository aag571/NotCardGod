using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;
using Unity.Muninn;
using Unity.Muninn.Model;
using Unity.Muninn.MuninnLobby;
using Unity.Muninn.Transport;
using Unity.UOS.Auth;
using Unity.UOS.Common;
using Unity.UOS.Matchmaking;
using Unity.UOS.Matchmaking.Model;
using UnityEngine;
using MatchmakingPlayer = Unity.UOS.Matchmaking.Model.Player;

public enum UosMatchState
{
    Idle,
    Creating,
    Waiting,
    Matched,
    Failed,
    Cancelled
}

public sealed class UosOnlineService : MuninnBehaviour
{
    public static UosOnlineService Instance { get; private set; }

    public bool IsReady { get; private set; }
    public bool IsInRoom { get; private set; }
    public string CurrentRoomId { get; private set; }
    public string CurrentJoinCode { get; private set; }
    public MuninnRoomView CurrentRoom => IsInRoom ? GetMuninnRoomView() : null;
    public string CurrentTicketId { get; private set; }
    public UosMatchState MatchState { get; private set; } = UosMatchState.Idle;

    public event Action<UosMatchState> MatchStateChanged;
    public event Action<MuninnRoomView> RoomJoined;
    public event Action<MuninnPlayer> PlayerJoined;
    public event Action<MuninnPlayer> PlayerLeft;
    public event Action<byte[]> EventReceived;
    public event Action<byte[], uint> EventReceivedWithSender;
    public event Action Disconnected;
    public event Action<string> Error;

    private string _userId;
    private int matchmakingGeneration;
    private bool isJoiningRoom;
    private string joiningRoomId;
    private bool joinFailureReported;
    public string MatchMap { get; set; } = "default";
    public string MatchRole { get; set; }
    public string LocalDisplayName { get; private set; }
    public string LocalAvatarUrl { get; private set; }

    public override void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        base.Awake();
    }

    public async Task<bool> InitializeAsync(string userId)
    {
        _userId = UosCrudSaveService.NormalizeUosUserId(userId);
        if (string.IsNullOrEmpty(_userId) || Application.isEditor)
        {
            Debug.Log("[UOS] 联机服务在编辑器中保持禁用，请使用抖音小游戏构建测试。");
            return false;
        }

        var appId = Unity.UOS.Common.Settings.AppID;
        var appSecret = Unity.UOS.Common.Settings.AppSecret;
        var profileId = Unity.UOS.Common.Settings.RoomProfileUUID;
        if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(appSecret) || string.IsNullOrEmpty(profileId))
        {
            ReportError("UOS AppID、AppSecret 或 RoomProfileUUID 尚未配置");
            return false;
        }

        try
        {
            AuthTokenManager.Initialize(appId, appSecret);
            MatchmakingSDK.Initialize(appId, appSecret);
            await AuthTokenManager.GenerateAccessTokenWithUosAppId(appId, _userId);
            MuninnSettings.UosAppId = appId;
            MuninnSettings.UosAppSecret = appSecret;
            MuninnSettings.RoomProfileUUID = profileId;
            MuninnSettings.TransportType = MuninnTransportType.WebSocketSecure;
            var sdk = DouyinTTSDKService.Instance;
            LocalDisplayName = string.IsNullOrWhiteSpace(sdk?.DisplayName) ? _userId : sdk.DisplayName;
            LocalAvatarUrl = sdk?.AvatarUrl ?? string.Empty;
            MuninnNetwork.PlayerInfo = new MuninnPlayerInfo
            {
                Id = _userId,
                Name = LocalDisplayName,
                Properties = new Dictionary<string, string>
                {
                    { "avatarUrl", LocalAvatarUrl }
                }
            };
            IsReady = true;
            Debug.Log($"[UOS] 联机服务初始化成功：userId={_userId}, roomProfile={profileId}, matchmakingConfig={Unity.UOS.Common.Settings.MatchmakingConfigID}");
            return true;
        }
        catch (Exception exception)
        {
            ReportError($"UOS 联机认证失败：{exception.Message}");
            return false;
        }
    }

    public void StartMatchmaking(string configId = null, GameMode mode = GameMode.P2P)
    {
        if (!IsReady)
        {
            ReportError("UOS 联机服务尚未准备完成");
            return;
        }

        var selectedConfigId = string.IsNullOrEmpty(configId)
            ? Unity.UOS.Common.Settings.MatchmakingConfigID
            : configId;
        if (string.IsNullOrEmpty(selectedConfigId))
        {
            ReportError("UOS MatchmakingConfigID 尚未配置");
            return;
        }

        if (mode != GameMode.P2P)
        {
            ReportError("Matchmaking 只接受 P2P 模式，FP2P 请使用私人房间邀请");
            return;
        }

        if (MatchState == UosMatchState.Creating || MatchState == UosMatchState.Waiting || isJoiningRoom)
        {
            Debug.Log($"[UOS] 忽略重复匹配请求：state={MatchState}, ticketId={CurrentTicketId}, joiningRoomId={joiningRoomId}");
            return;
        }

        if (IsInRoom)
        {
            Debug.Log($"[UOS] 已在房间中，忽略匹配请求：roomId={CurrentRoomId}");
            return;
        }

        matchmakingGeneration++;
        joinFailureReported = false;
        StartCoroutine(MatchmakingRoutine(selectedConfigId, mode, matchmakingGeneration));
    }

    public void RefreshLocalPlayerInfo()
    {
        var sdk = DouyinTTSDKService.Instance;
        if (sdk == null || MuninnNetwork.PlayerInfo == null)
            return;

        LocalDisplayName = string.IsNullOrWhiteSpace(sdk.DisplayName) ? _userId : sdk.DisplayName;
        LocalAvatarUrl = sdk.AvatarUrl ?? string.Empty;
        MuninnNetwork.PlayerInfo.Name = LocalDisplayName;
        MuninnNetwork.PlayerInfo.Properties["avatarUrl"] = LocalAvatarUrl;
        if (IsInRoom)
        {
            var self = CurrentRoom?.Self();
            if (self != null)
            {
                self.Name = LocalDisplayName;
                self.Properties["avatarUrl"] = LocalAvatarUrl;
                UpdatePlayerInfo(self, _ => { });
            }
        }
    }

    public void StopMatchmaking()
    {
        matchmakingGeneration++;
        var ticketId = CurrentTicketId;
        CurrentTicketId = null;
        if (!string.IsNullOrEmpty(ticketId))
            StartCoroutine(DeleteTicketRoutine(ticketId));
        SetMatchState(UosMatchState.Cancelled);
    }

    public void ResetMatchmakingSession()
    {
        matchmakingGeneration++;
        var ticketId = CurrentTicketId;
        CurrentTicketId = null;
        if (!string.IsNullOrEmpty(ticketId))
            StartCoroutine(DeleteTicketRoutine(ticketId));

        if (IsInRoom)
            LeaveRoom();
        IsInRoom = false;
        isJoiningRoom = false;
        joiningRoomId = null;
        joinFailureReported = false;
        CurrentRoomId = null;
        CurrentJoinCode = null;
        SetMatchState(UosMatchState.Idle);
    }

    public void CreatePrivateRoom(string joinCode)
    {
        if (!IsReady)
        {
            ReportError("UOS 联机服务尚未准备完成");
            return;
        }

        if (string.IsNullOrWhiteSpace(joinCode))
            joinCode = UnityEngine.Random.Range(100000, 999999).ToString();

        MuninnNetwork.CreateOrJoinRoom(new CreateOrJoinRoomRequest
        {
            Namespace = "PvP",
            RoomUUID = string.Empty,
            Visibility = MuninnRoomVisibility.Private,
            JoinCode = joinCode,
            PlayerId = _userId,
            MaxPlayers = 2
        });
    }

    public void JoinPrivateRoom(string roomId, string joinCode)
    {
        if (!IsReady || string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(joinCode))
        {
            ReportError("加入房间需要有效的 RoomId 和 JoinCode");
            return;
        }

        JoinRoomOnce(roomId, joinCode);
    }

    public void JoinRoomFromQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return;

        var values = new Dictionary<string, string>();
        foreach (var pair in query.Split('&'))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
                continue;
            var key = Uri.UnescapeDataString(pair.Substring(0, separator));
            var value = Uri.UnescapeDataString(pair.Substring(separator + 1));
            values[key] = value;
        }

        values.TryGetValue("roomId", out var roomId);
        values.TryGetValue("joinCode", out var joinCode);
        if (!string.IsNullOrEmpty(roomId) && !string.IsNullOrEmpty(joinCode))
            JoinPrivateRoom(roomId, joinCode);
    }

    public void SendEvent(byte[] data)
    {
        if (!IsInRoom || data == null)
            return;
        MuninnNetwork.RaiseEvent(data, new RaiseEventOptions { Target = RaiseEventTarget.TO_ALL_BUT_ME });
    }

    public void SendEventToMaster(byte[] data)
    {
        if (!IsInRoom || data == null)
            return;
        MuninnNetwork.ServerCall(data);
    }

    public bool TryGetBattleParticipants(out string localId, out string opponentId,
        out bool isMasterClient, out uint opponentSenderId)
    {
        localId = null;
        opponentId = null;
        isMasterClient = false;
        opponentSenderId = 0;
        var room = CurrentRoom;
        if (room == null || room.Players == null || room.Players.Count != 2)
            return false;

        var self = room.Self();
        if (self == null)
            return false;

        localId = self.Id;
        isMasterClient = room.IsMasterClient();
        for (var index = 0; index < room.Players.Count; index++)
        {
            var player = room.Players[index];
            if (player != null && player.SenderId != self.SenderId)
            {
                opponentId = player.Id;
                opponentSenderId = player.SenderId;
                break;
            }
        }

        return !string.IsNullOrEmpty(localId) && !string.IsNullOrEmpty(opponentId);
    }

    public void LeaveCurrentRoom()
    {
        if (IsInRoom)
            LeaveRoom();
        IsInRoom = false;
        isJoiningRoom = false;
        joiningRoomId = null;
        CurrentRoomId = null;
        CurrentJoinCode = null;
        if (MatchState == UosMatchState.Matched)
            SetMatchState(UosMatchState.Idle);
    }

    public void InviteFriend()
    {
        if (!IsInRoom || string.IsNullOrEmpty(CurrentRoomId) || string.IsNullOrEmpty(CurrentJoinCode))
        {
            ReportError("请先创建私人房间，再邀请好友");
            return;
        }

        DouyinTTSDKService.Instance?.ShareRoomToFriend(CurrentRoomId, CurrentJoinCode,
            (success, message) =>
            {
                if (!success)
                    ReportError($"好友邀请失败：{message}");
            });
    }

    public override void BeforeConnectRoom(JoinRoomResponse response)
    {
        CurrentRoomId = response.RoomUuid;
        CurrentJoinCode = response.JoinCode;
    }

    public override void OnJoinedRoom(MuninnRoomView roomView)
    {
        IsInRoom = true;
        isJoiningRoom = false;
        joiningRoomId = null;
        joinFailureReported = false;
        CurrentRoomId = roomView.Room.Id;
        if (string.IsNullOrEmpty(CurrentJoinCode))
            CurrentJoinCode = roomView.Room.RoomCode;
        RoomJoined?.Invoke(roomView);
    }

    public override void OnJoinRoomFailed(MuninnError error)
    {
        IsInRoom = false;
        isJoiningRoom = false;
        joiningRoomId = null;
        if (joinFailureReported)
            return;

        joinFailureReported = true;
        ReportError($"加入 UOS 房间失败：{error.Description}");
    }

    public override void OnPlayerEnteredRoom(MuninnPlayer player) => PlayerJoined?.Invoke(player);

    public override void OnPlayerInfoUpdated(MuninnPlayer player) => PlayerJoined?.Invoke(player);

    public override void OnPlayerLeftRoom(MuninnPlayer player) => PlayerLeft?.Invoke(player);

    public override void OnLeftRoom(LeaveRoomEvent leaveRoomEvent)
    {
        IsInRoom = false;
        isJoiningRoom = false;
        joiningRoomId = null;
        CurrentRoomId = null;
        CurrentJoinCode = null;
    }

    public override void OnDisconnected()
    {
        var wasInRoom = IsInRoom;
        var wasJoiningRoom = isJoiningRoom;
        IsInRoom = false;
        isJoiningRoom = false;
        joiningRoomId = null;
        CurrentRoomId = null;
        CurrentJoinCode = null;
        if (wasInRoom)
            Disconnected?.Invoke();
        if ((wasInRoom || wasJoiningRoom) && !joinFailureReported)
        {
            joinFailureReported = true;
            ReportError("UOS 实时连接已断开");
        }
    }

    public override void OnEvent(MuninnEvent muninnEvent)
    {
        DispatchReceivedEvent(muninnEvent);
    }

    public override void OnServerCall(MuninnEvent muninnEvent)
    {
        DispatchReceivedEvent(muninnEvent);
    }

    private void DispatchReceivedEvent(MuninnEvent muninnEvent)
    {
        if (muninnEvent?.Data == null)
            return;

        EventReceived?.Invoke(muninnEvent.Data);
        EventReceivedWithSender?.Invoke(muninnEvent.Data, muninnEvent.SenderId);
    }

    private IEnumerator MatchmakingRoutine(string configId, GameMode mode, int generation)
    {
        while (generation == matchmakingGeneration && MatchState != UosMatchState.Matched)
        {
            yield return MatchmakingTicketRoutine(configId, mode, generation);
            if (generation != matchmakingGeneration || MatchState == UosMatchState.Matched)
                yield break;

            if (!string.IsNullOrEmpty(CurrentTicketId))
            {
                yield return DeleteTicketRoutine(CurrentTicketId);
                CurrentTicketId = null;
            }

            Debug.Log("[UOS] 匹配请求已结束，正在重新创建 Ticket");
            yield return new WaitForSeconds(1f);
        }
    }

    private IEnumerator MatchmakingTicketRoutine(string configId, GameMode mode, int generation)
    {
        if (generation != matchmakingGeneration)
            yield break;
        SetMatchState(UosMatchState.Creating);
        var createTask = MatchmakingSDK.Instance.CreateTicketAsync(configId,
            new List<MatchmakingPlayer>
            {
                new MatchmakingPlayer
                {
                    id = _userId,
                     attributes = new Dictionary<string, string>
                     {
                         { "mode", "pk" },
                         { "map", string.IsNullOrWhiteSpace(MatchMap) ? "default" : MatchMap },
                         { "role", string.IsNullOrWhiteSpace(MatchRole) ? _userId : MatchRole }
                     }
                }
        });
        yield return WaitForTask(createTask);
        if (generation != matchmakingGeneration)
        {
            if (createTask.Status == TaskStatus.RanToCompletion && !string.IsNullOrEmpty(createTask.Result))
                yield return DeleteTicketRoutine(createTask.Result);
            yield break;
        }

        if (createTask.IsFaulted)
        {
            ReportError($"创建匹配请求失败：{createTask.Exception?.GetBaseException().Message}");
            yield break;
        }

        if (createTask.IsCanceled)
        {
            ReportError("创建匹配请求已取消");
            yield break;
        }

        var ticketId = createTask.Result;
        CurrentTicketId = ticketId;
        Debug.Log($"[UOS] Matchmaking Ticket 创建成功：ticketId={ticketId}, configId={configId}, userId={_userId}, generation={generation}");
        SetMatchState(UosMatchState.Waiting);
        while (generation == matchmakingGeneration && CurrentTicketId == ticketId)
        {
            var ticketTask = MatchmakingSDK.Instance.GetTicketAsync(ticketId);
            yield return WaitForTask(ticketTask);
            if (generation != matchmakingGeneration || CurrentTicketId != ticketId)
                yield break;

            if (ticketTask.IsFaulted)
            {
                ReportError($"查询匹配状态失败：{ticketTask.Exception?.GetBaseException().Message}");
                yield break;
            }

            if (ticketTask.IsCanceled)
            {
                ReportError("查询匹配状态已取消");
                yield break;
            }

            var ticket = ticketTask.Result;
            Debug.Log($"[UOS] Matchmaking Ticket 状态：ticketId={ticketId}, status={ticket.status}");
            if (ticket.status == MatchmakingSDK.TicketStatusMatched && ticket.assignment != null)
            {
                CurrentRoomId = ticket.assignment.roomId;
                Debug.Log($"[UOS] 匹配成功：ticketId={ticketId}, roomId={CurrentRoomId}, generation={generation}");
                CurrentTicketId = null;
                SetMatchState(UosMatchState.Matched);
                JoinRoomOnce(CurrentRoomId);
                yield break;
            }

            if (ticket.status == MatchmakingSDK.TicketStatusError)
            {
                ReportError($"匹配失败：{ticket.assignment?.msg}");
                yield break;
            }

            yield return new WaitForSeconds(1f);
        }
    }

    private IEnumerator DeleteTicketRoutine(string ticketId)
    {
        var task = MatchmakingSDK.Instance.DeleteTicketAsync(ticketId);
        yield return WaitForTask(task);
        if (task.IsFaulted)
            Debug.LogWarning($"[UOS] 删除 Matchmaking Ticket 失败：ticketId={ticketId}, error={task.Exception?.GetBaseException().Message}");
    }

    private void JoinRoomOnce(string roomId, string joinCode = "")
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            ReportError("加入房间失败：RoomId 为空");
            return;
        }

        if (IsInRoom && string.Equals(CurrentRoomId, roomId, StringComparison.Ordinal))
        {
            Debug.Log($"[UOS] 已在目标房间中，忽略重复入房：roomId={roomId}");
            return;
        }

        if (isJoiningRoom)
        {
            Debug.Log($"[UOS] 正在进入房间，忽略重复入房：requestedRoomId={roomId}, joiningRoomId={joiningRoomId}");
            return;
        }

        isJoiningRoom = true;
        joiningRoomId = roomId;
        joinFailureReported = false;
        Debug.Log($"[UOS] 开始进入房间：roomId={roomId}");
        MuninnNetwork.JoinRoomByUUID(roomId, joinCode);
    }

    private static IEnumerator WaitForTask(Task task)
    {
        while (!task.IsCompleted)
            yield return null;
    }

    private void SetMatchState(UosMatchState state)
    {
        MatchState = state;
        MatchStateChanged?.Invoke(state);
    }

    private void ReportError(string message)
    {
        Debug.LogWarning($"[UOS] {message}");
        Error?.Invoke(message);
        SetMatchState(UosMatchState.Failed);
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (Instance == this)
            Instance = null;
    }
}
