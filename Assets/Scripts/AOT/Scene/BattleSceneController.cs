using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using DG.Tweening;
using TTSDK.UNBridgeLib.LitJson;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Muninn.Model;

    /// <summary>
    /// 管理战斗场景的人机对战界面、手牌交互、状态刷新、出牌动画和结算面板。
    /// </summary>
    public sealed class BattleSceneController : MonoBehaviour
    {
        
        #region 基础配置信息

        private string LocalPlayerId => localPlayerId;
        private string AiPlayerId => opponentPlayerId;
        private string OpponentPlayerId => opponentPlayerId;
        // 基础手牌可见槽位数量；弃牌阶段允许临时超过正式上限
        private const int BaseSlotCount = 10;
        // 强化手牌可见槽位数量；弃牌阶段允许临时超过正式上限
        private const int SkillSlotCount = 12;
        // 玩家和对手手牌槽位统一尺寸
        private static readonly Vector2 HandCardSlotSize = new Vector2(150f, 210f);
        // 基础牌双击出牌判定窗口
        private const float BasePlayDoubleClickSeconds = 0.35f;

        #endregion


        #region 行为检测

        // UI 射线检测复用列表，用于判断是否点击了空白区域。
        private static readonly List<RaycastResult> Rays = new List<RaycastResult>();
        
        // 上一次刷新时我方手牌实例 id，用于识别新发牌。
        private readonly HashSet<string> myHandIds = new HashSet<string>(StringComparer.Ordinal);
        // 上一次刷新时敌方手牌实例 id，用于识别新发牌。
        private readonly HashSet<string> enemyHandIds = new HashSet<string>(StringComparer.Ordinal);
        // 本次刷新收集到的我方手牌实例 id。
        private readonly HashSet<string> nextMyHandIds = new HashSet<string>(StringComparer.Ordinal);
        // 本次刷新收集到的敌方手牌实例 id。
        private readonly HashSet<string> nextEnemyHandIds = new HashSet<string>(StringComparer.Ordinal);
        
        
        #endregion

        #region 手牌与 UI 数据
        
        // 我方基础手牌槽位到手牌列表索引的映射。
        private readonly int[] playerBaseSlotHandIndices = new int[BaseSlotCount];
        // 我方强化手牌槽位到手牌列表索引的映射。
        private readonly int[] playerSkillSlotHandIndices = new int[SkillSlotCount];

        // ResultPanel 中显示的强化出牌记录。
        private readonly List<string> playLogs = new List<string>();
        // 图鉴当前筛选后的强化卡列表。
        private readonly List<SkillCardBase> guideCards = new List<SkillCardBase>();
        // 图鉴列表复用的按钮集合。
        private readonly List<Button> guideItems = new List<Button>();
        // 卡牌预制体根节点精灵缓存，减少重复查组件。
        private readonly Dictionary<GameObject, Sprite> cardSpriteCache = new Dictionary<GameObject, Sprite>();
        // 手牌 UI 对象池，按预制体 key 分组复用。
        private readonly Dictionary<string, Stack<GameObject>> cardViewPool = new Dictionary<string, Stack<GameObject>>(StringComparer.Ordinal);
        // 每个手牌槽当前挂载的卡牌预制体实例。
        private readonly Dictionary<string, GameObject> slotViews = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        // 每个手牌槽当前显示的预制体类别，用于切换卡牌类型时回收旧实例。
        private readonly Dictionary<string, string> slotViewKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        // 基础卡强化槽使用的品质 Tag 图片。
        private readonly Dictionary<CardQuality, Sprite> enhancementTagSprites = new Dictionary<CardQuality, Sprite>();
        // 正在播放发牌动画、尚未显示到手牌槽的卡牌实例。
        private readonly HashSet<string> pendingDealIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> slotDealIds = new Dictionary<string, string>(StringComparer.Ordinal);
        // 玩家和对手状态节点上的护盾特效，不依附于任何卡牌视图。
        private ShieldFx playerShieldFx;
        private ShieldFx opponentShieldFx;

        #endregion

        #region 编辑器配置

        [Header("Config")]
        // 数据仓库没有配置时使用的默认战斗平衡配置。
        private BalanceConfig defaultBalanceConfig;
        // 本地人机对战使用的默认隐藏牌库配置。
        private MatchDeckConfig defaultMatchDeckConfig;
        // 编辑器或离线模式使用的默认卡牌目录。
        private CardCatalog defaultCardCatalog;
        // 战斗卡牌视图使用的默认美术配置。
        private CardArtConfig defaultCardArtConfig;


        #endregion

        #region 预制体

        // 基础攻击卡牌 UI 预制体。
        private GameObject baseAttackPrefab;
        // 基础防御卡牌 UI 预制体。
        private GameObject baseDefensePrefab;
        // 基础治疗卡牌 UI 预制体。
        private GameObject baseHealPrefab;
        // 白银强化卡牌 UI 预制体。
        private GameObject skillSilverPrefab;
        // 黄金强化卡牌 UI 预制体。
        private GameObject skillGoldPrefab;
        // 白金强化卡牌 UI 预制体。
        private GameObject skillPlatinumPrefab;
        // 棱彩强化卡牌 UI 预制体。
        private GameObject skillPrismaticPrefab;

        #endregion

        #region 场景引用

        [Header("手牌区域")]
        [SerializeField] private GameObject baseHandPanel;
        [SerializeField] private GameObject skillHandPanel;
        [SerializeField] private GameObject opponentBaseHandPanel;
        [SerializeField] private GameObject opponentSkillHandPanel;

        [Header("卡槽")]
        [SerializeField] private GameObject[] baseCardSlots;
        [SerializeField] private GameObject[] opponentBaseCardSlots;
        [SerializeField] private GameObject[] skillCardSlots;
        [SerializeField] private GameObject[] opponentSkillCardSlots;

        [Header("战场信息")]
        [SerializeField] private GameObject battlefieldPanel;
        [SerializeField] private GameObject battlefieldHintText;
        [SerializeField] private GameObject phaseText;
        [SerializeField] private GameObject timerText;

        [Header("战斗特效")]
        private Shader battleFxShader;
        private Shader shieldFxShader;
        [SerializeField] private CardMaven cardMaven;
        [SerializeField] private Desk desk;
        [SerializeField] private GameObject cardPoolRoot;

        [Header("生命 / 护盾")]
        [SerializeField] private GameObject playerHpText;
        [SerializeField] private GameObject playerShieldText;
        [SerializeField] private GameObject opponentHpText;
        [SerializeField] private GameObject opponentShieldText;

        [Header("功能按钮")]
        [SerializeField] private GameObject mainBack;
        [SerializeField] private GameObject discardButton;
        [SerializeField] private GameObject preparation;
        [SerializeField] private GameObject fieldGuide;
        [SerializeField] private GameObject mainBackClose;
        [SerializeField] private GameObject fieldGuideClose;

        [Header("结果面板")]
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private GameObject resultLogScroll;
        [SerializeField] private GameObject resultLogViewport;
        [SerializeField] private GameObject resultLogContent;
        [SerializeField] private GameObject resultLogText;
        [SerializeField] private GameObject resultLogTitleText;

        [Header("设置面板")]
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private GameObject settingsButton;
        [SerializeField] private Slider bgmSlider;
        [SerializeField] private Slider sfxSlider;

        [Header("图鉴面板")]
        private GameObject fieldGuidePanel;
        private GameObject guideFilterAll;
        private GameObject guideFilterAtk;
        private GameObject guideFilterDef;
        private GameObject guideFilterHeal;
        private GameObject guideFilterSilver;
        private GameObject guideFilterGold;
        private GameObject guideFilterPlatinum;
        private GameObject guideFilterPrismatic;
        private GameObject fieldGuideCardPreview;
        private GameObject fieldGuideList;
        private GameObject fieldGuideListViewport;
        private GameObject fieldGuideListContent;
        private GameObject fieldGuideCardPreviewText;
        private Color fieldGuideCardPreviewTextDefaultColor = Color.white;
        private GameObject fieldGuideDetailTitle;
        private GameObject fieldGuideDetailBody;

        [Header("技能说明面板")]
        [SerializeField] private GameObject explainPanel;
        [SerializeField] private GameObject skillNameText;
        [SerializeField] private GameObject skillExplainText;

        [Header("结算面板")]
        [SerializeField] private GameObject endSettlePanel;
        [SerializeField] private GameObject endSettleTitleText;
        [SerializeField] private GameObject endSettleBodyText;
        [SerializeField] private GameObject endSettleReturnButton;

        #endregion

        #region 运行时状态

        // 当前实际使用的战斗平衡配置。
        private BalanceConfig balanceConfig;
        // 当前实际使用的隐藏牌库配置。
        private MatchDeckConfig matchDeckConfig;
        // 当前实际使用的卡牌目录。
        private CardCatalog cardCatalog;
        // 当前实际使用的卡牌美术配置。
        private CardArtConfig cardArtConfig;
        // 当前战斗的卡牌管理器。
        private ICardManager cardManager;
        // 当前战斗的行动结算器。
        private ICombatResolver combatResolver;
        // 当前战斗的状态机。
        private GameController gameController;
        // 当前人机对手控制器。
        private IAIController aiController;
        // 我方玩家战斗状态。
        private PlayerCombatStateData localPlayer;
        // AI 玩家战斗状态。
        private PlayerCombatStateData aiPlayer;
        // 本地 PvE 进度服务和本局是否为 Boss 战。
        private LocalAccountService accountService;
        private bool bossBattle;
        private int bossIndex;
        
        
        // 当前行动动画的源卡牌位置。
        private RectTransform actRt;
        // 当前行动动画显示的卡牌名称。
        private string actLabel;
        // 当前行动写入 ResultPanel 的日志文本。
        private string actLog;
        // 当前行动动画和日志使用的颜色。
        private Color actColor = Color.white;
        // 当前行动动画卡牌的显示尺寸。
        private Vector2 actSize = new Vector2(164f, 226f);
        // AI 下次尝试行动前的剩余等待时间。
        private float aiActionTimer;
        // 本局战斗开始时的未缩放时间。
        private float battleStartTime;
        // 打开暂停面板前缓存的 Time.timeScale。
        private float pauseScale = 1f;
        // 当前选中的强化手牌索引，-1 表示未选中。
        private int selectedSkillHandIndex = -1;
        private string selectedSkillInstanceId;
        // 当前选中的基础牌槽位索引，-1 表示未选中。
        private int selectedBaseSlot = -1;
        private string selectedBaseInstanceId;
        // 强化牌拖拽时悬停命中的基础牌槽位。
        private int dragBaseSlot = -1;
        // 上一次基础牌点击槽位，用于双击出牌判定。
        private int lastBaseClickSlot = -1;
        // 上一次基础牌点击的未缩放时间。
        private float lastBaseClickTime = -1f;
        // 上一次基础牌点击的实例 id，避免发牌或重排后误触发双击。
        private string lastBaseClickInstanceId;
        private int lastSkillClickSlot = -1;
        private float lastSkillClickTime = -1f;
        private string lastSkillClickInstanceId;
        // 当前是否处于弃牌模式。
        private bool discardMode;
        private bool tutorialActive;
        private bool tutorialFinished;
        private bool tutorialAwaitingStart;
        private TutorialStep tutorialStep;
        private GameObject tutorialPanel;
        private Button tutorialSkipBattleButton;
        private GameObject tutorialTitle;
        private GameObject tutorialBody;
        private Button tutorialCompleteButton;
        private GameObject battleLoadingCurtain;
        // 设置面板是否打开。
        private bool settingOpen;
        // 图鉴面板是否打开。
        private bool guideOpen;
        // 当前场景是否持有暂停状态。
        private bool pauseHeld;
        // 发牌动画系统是否准备完成。
        private bool dealFxReady;
        // 战斗启动流程是否完成。
        private bool booted;
        private bool onlineBattle;
        private bool onlineHost;
        private string localPlayerId = "player";
        private string opponentPlayerId = "ai";
        private uint opponentSenderId;
        private UosOnlineService onlineService;
        private float nextNetworkSyncTime;
        private float nextHeartbeatTime;
        private float lastOpponentMessageTime;
        private float opponentLeftSuspectedAt = -1f;
        private string opponentDisplayName = "玩家";
        private bool onlineGameStarted;
        private readonly BattleNetworkSession networkSession = new BattleNetworkSession();
        private BattleSnapshotSanitizer snapshotSanitizer;
        private const float NetworkHeartbeatInterval = 1f;
        private const float OpponentTimeoutSeconds = 8f;
        private const float PlayerLeftGraceSeconds = 2f;
        // 是否已经处理过本局结算。
        private bool gameOverHandled;
        // 图鉴当前基础类型筛选条件。
        private BaseCardType? guideType;
        // 图鉴当前品质筛选条件。
        private CardQuality? guideQuality;
        // 图鉴当前正在展示详情的强化牌。
        private SkillCardBase guideSelected;
        // 弃牌按钮文本
        private string discardButtonTextName;
        private Text preparationButtonLabel;
        private Coroutine resultScrollCoroutine;

        private enum TutorialStep
        {
            None,
            SelectShieldSkill,
            AttachShieldSkill,
            SelectSecondDefenseSkill,
            AttachSecondDefenseSkill,
            PlayDefense,
            EndPreparation,
            PlayAttack,
            ObserveShieldAndTimer,
            DiscardCards,
            SynthesizeAttack,
            EndPreparationAfterAttackSynthesis,
            PlayAttackAfterAttackSynthesis,
            InspectGrownAttack,
            Complete
        }

        #endregion
        
        
        /// <summary>
        /// 在对象创建时建立组件引用并补齐运行所需状态。
        /// </summary>
        private void Awake()
        {
            InitAllPanel();
            battleLoadingCurtain = SceneObject("BattleLoadingCurtain");
            if (battleLoadingCurtain != null)
            {
                battleLoadingCurtain.SetActive(true);
                battleLoadingCurtain.transform.SetAsLastSibling();
            }
            ConfigureBattleUi();
            if (battlefieldHintText != null && battlefieldHintText.TryGetComponent<Text>(out var hintText))
                hintText.fontSize = Mathf.Max(hintText.fontSize, 28);
            InitializeAudioSettings();
            BindButtons();
        }

        private void InitAllPanel()
        {
            if (resultPanel != null)
                resultPanel.SetActive(true);
            if (settingsPanel != null)
                settingsPanel.SetActive(false);
            if (fieldGuidePanel != null)
                fieldGuidePanel.SetActive(false);
            if (endSettlePanel != null)
                endSettlePanel.SetActive(false);
            if (explainPanel != null)
                explainPanel.SetActive(false);
        }

        private void InitializeAudioSettings()
        {
            var audio = GameAudioService.Ensure();
            bgmSlider.SetValueWithoutNotify(audio.BgmVolume);
            sfxSlider.SetValueWithoutNotify(audio.SfxVolume);
            bgmSlider.onValueChanged.RemoveAllListeners();
            sfxSlider.onValueChanged.RemoveAllListeners();
            bgmSlider.onValueChanged.AddListener(audio.SetBgmVolume);
            sfxSlider.onValueChanged.AddListener(audio.SetSfxVolume);
        }
        
        /// <summary>
        /// 启动初始化流程并进入对应场景逻辑。
        /// </summary>
        private IEnumerator Start()
        {
            var loadingCurtainStart = Time.unscaledTime;
            tutorialActive = PlayerPrefs.GetInt("NotCardGod.Tutorial.Active", 0) == 1;
            tutorialAwaitingStart = false;
            PlayerPrefs.DeleteKey("NotCardGod.Tutorial.Active");
            discardButtonTextName = discardButton.gameObject.GetComponentInChildren<Text>().name;
            preparationButtonLabel = preparation.GetComponentInChildren<Text>(true);
            GameAudioService.Ensure();
            yield return LoadBattleAssets();
            InitializeCardFx();
            var remainingCurtainTime = 0.5f - (Time.unscaledTime - loadingCurtainStart);
            if (remainingCurtainTime > 0f)
                yield return new WaitForSecondsRealtime(remainingCurtainTime);
            if (battleLoadingCurtain != null)
                battleLoadingCurtain.SetActive(false);
            onlineBattle = PlayerPrefs.GetInt("NotCardGod.Battle.Online", 0) == 1;
            if (onlineBattle)
                BootstrapOnlineBattle();
            else
                BootstrapPveBattle();
            if (endSettlePanel != null)
                endSettlePanel.SetActive(false);
            booted = true;
        }

        /// <summary>
        /// 对象销毁时释放临时资源并恢复运行状态。
        /// </summary>
        private void OnDestroy()
        {
            if (onlineService != null)
            {
                onlineService.EventReceivedWithSender -= OnBattleNetworkEvent;
                onlineService.PlayerLeft -= OnOpponentLeft;
                onlineService.Disconnected -= OnLocalNetworkDisconnected;
            }
            if (pauseHeld)
            {
                Time.timeScale = pauseScale;
            }

            CardFx.ClearPool();
            ClearCardViewPool();
        }

        private void ReturnToMain()
        {
            if (onlineBattle)
            {
                if (!gameOverHandled && accountService != null)
                    accountService.RecordPvpResult(false);
                onlineService?.LeaveCurrentRoom();
                PlayerPrefs.SetInt("NotCardGod.Battle.Online", 0);
                PlayerPrefs.Save();
            }

            SceneManager.LoadScene("Main");
        }

        /// <summary>
        /// 每帧推进计时、输入检测和界面刷新。
        /// </summary>
        private void Update()
        {
            if (!booted || gameController == null)
            {
                return;
            }

            var tutorialAllowsClock = tutorialActive && gameController.State == GameState.Battle;
            if (!onlineBattle || (onlineHost && onlineGameStarted))
                gameController.Tick(!tutorialActive || tutorialAllowsClock ? Time.deltaTime : 0f);
            if (!onlineBattle && !tutorialActive)
                TickAi(Time.deltaTime);
            if (onlineBattle && onlineHost && onlineGameStarted && !gameOverHandled &&
                Time.unscaledTime >= nextNetworkSyncTime)
            {
                nextNetworkSyncTime = Time.unscaledTime + 0.25f;
                BroadcastBattleSnapshot();
            }
            if (onlineBattle && !gameOverHandled)
                TickOnlineConnection();
            RefreshTutorialSkipBattleButton();
            ClearSelOnBlank();
            RefreshStaticUi();
        }
        
        

        /// <summary>
        /// 配置战斗界面和手牌槽位。
        /// </summary>
        private void ConfigureBattleUi()
        {
            var baseCount = Mathf.Min(baseCardSlots?.Length ?? 0, opponentBaseCardSlots?.Length ?? 0);
            var skillCount = Mathf.Min(skillCardSlots?.Length ?? 0, opponentSkillCardSlots?.Length ?? 0);
            for (var i = 0; i < baseCount; i++)
            {
                ConfigureCardSlot(baseCardSlots[i], true);
                ConfigureCardSlot(opponentBaseCardSlots[i], false);
            }

            for (var i = 0; i < skillCount; i++)
            {
                ConfigureCardSlot(skillCardSlots[i], true);
                ConfigureCardSlot(opponentSkillCardSlots[i], false);
            }

            battlefieldHintText.GetComponent<Text>().text = "备战阶段";
        }

        /// <summary>
        /// 初始化战斗特效组件。
        /// </summary>
        private void InitializeCardFx()
        {
            if (battleFxShader == null)
            {
                battleFxShader = Shader.Find("NotCardGod/UI/EnhancementGlow");
            }

            if (battleFxShader == null)
            {
                Debug.LogError("战斗特效 Shader 加载失败：NotCardGod/UI/EnhancementGlow，WebGL 构建可能裁剪了该 Shader。");
            }

            CardFx.Configure(battleFxShader);
            cardMaven.Init(cardMaven.transform);
            desk.Init(desk.transform);
            dealFxReady = true;
        }

        private static GameObject SceneObject(string objectName)
        {
            return GameObject.Find(objectName);
        }

        /// <summary>
        /// 配置一个卡槽，用来存放可以点击的基础卡
        /// </summary>
        private void ConfigureCardSlot(GameObject slot, bool interactive)
        {
            var layoutElement = slot.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = slot.AddComponent<LayoutElement>();
            }

            layoutElement.ignoreLayout = true;

            var slotRect = slot.GetComponent<RectTransform>();
            if (slotRect != null)
            {
                slotRect.sizeDelta = HandCardSlotSize;
            }
            

            if (interactive && slot.GetComponent<Button>() == null)
            {
                var button = slot.AddComponent<Button>();
                button.transition = Selectable.Transition.ColorTint;
                var colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
                colors.pressedColor = new Color(0.88f, 0.92f, 1f, 1f);
                colors.disabledColor = new Color(1f, 1f, 1f, 0.45f);
                button.colors = colors;
            }

            var slotButton = slot.GetComponent<Button>();
            if (slotButton != null)
            {
                slotButton.transition = Selectable.Transition.None;
                if (slotButton.targetGraphic != null && slotButton.targetGraphic.gameObject == slot)
                {
                    slotButton.targetGraphic = null;
                }
            }

            if (slot.GetComponent<BattleCardView>() == null)
            {
                slot.AddComponent<BattleCardView>();
            }

        }
        

        /// <summary>
        /// 执行 ResolveHandLaneParent 对应的运行时流程。
        /// </summary>
        private Transform ResolveHandLaneParent(string slotName)
        {
            if (slotName.StartsWith("OpponentBaseCardSlot", StringComparison.Ordinal))
            {
                return opponentBaseHandPanel.transform;
            }

            if (slotName.StartsWith("OpponentSkillCardSlot", StringComparison.Ordinal))
            {
                return opponentSkillHandPanel.transform;
            }

            if (slotName.StartsWith("SkillCardSlot", StringComparison.Ordinal))
            {
                return skillHandPanel.transform;
            }

            if (slotName.StartsWith("BaseCardSlot", StringComparison.Ordinal))
            {
                return baseHandPanel.transform;
            }

            return baseHandPanel.transform;
        }

        /// <summary>
        /// 执行 BindButtons 对应的运行时流程。
        /// </summary>
        private void BindButtons()
        {
            Array.Fill(playerBaseSlotHandIndices, -1);
            Array.Fill(playerSkillSlotHandIndices, -1);

            var baseCount = Mathf.Min(baseCardSlots?.Length ?? 0, opponentBaseCardSlots?.Length ?? 0);
            var skillCount = Mathf.Min(skillCardSlots?.Length ?? 0, opponentSkillCardSlots?.Length ?? 0);
            for (var i = 0; i < baseCount; i++)
            {
                var slot = i;
                BindButton(baseCardSlots[i], () => OnBaseSlotClicked(slot));
            }

            for (var i = 0; i < skillCount; i++)
            {
                var slot = i;
                BindButton(skillCardSlots[i], () => OnSkillSlotClicked(slot));
            }
            // 弃牌按钮绑定
            BindButton(discardButton, ToggleDiscardMode);
            // 设置按钮绑定
            BindButton(settingsButton, ToggleSettings);
            // 准备按钮绑定
            BindButton(preparation, OnendBattleClicked);
            // 主菜单按钮绑定
            BindButton(mainBack, ReturnToMain);
            // 关闭主菜单 
            BindButton(mainBackClose, ToggleSettings);
            
            // 打开图鉴面板
            BindButton(fieldGuide, ToggleGuide);
            // 关闭图鉴面板
            // 结算面板
            BindButton(endSettleReturnButton, ReturnToMain);
        }

        private void BindFieldGuideButtons()
        {
            BindButton(fieldGuideClose, ToggleGuide);
            BindButton(guideFilterAll, () => SetGuideFilter(null, null));
            BindButton(guideFilterAtk, () => SetGuideFilter(BaseCardType.Attack, guideQuality));
            BindButton(guideFilterDef, () => SetGuideFilter(BaseCardType.Defense, guideQuality));
            BindButton(guideFilterHeal, () => SetGuideFilter(BaseCardType.Heal, guideQuality));
            BindButton(guideFilterSilver, () => SetGuideFilter(guideType, CardQuality.Silver));
            BindButton(guideFilterGold, () => SetGuideFilter(guideType, CardQuality.Gold));
            BindButton(guideFilterPlatinum, () => SetGuideFilter(guideType, CardQuality.Platinum));
            BindButton(guideFilterPrismatic, () => SetGuideFilter(guideType, CardQuality.Prismatic));
        }

        // 按钮事件绑定（先清空后绑定）
        private static void BindButton(GameObject target, UnityEngine.Events.UnityAction action)
        {
            if (target == null)
            {
                return;
            }

            var button = target.GetComponent<Button>();
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        /// <summary>
        /// 设置面板的开关
        /// </summary>
        private void ToggleSettings()
        {
            settingOpen = !settingOpen;
            settingsPanel.SetActive(settingOpen);
            if (gameController.Mode == GameMode.PvE)
            {
                ApplyPause();
            }
        }

        /// <summary>
        /// 图鉴面板的开关
        /// </summary>
        private void ToggleGuide()
        {
            if (tutorialActive && !tutorialFinished)
            {
                ShowTutorialBlocked("教程进行中暂不打开图鉴，请按顶部提示操作。");
                return;
            }
            if (fieldGuidePanel == null)
            {
                SetText(battlefieldHintText, "图鉴资源尚未加载完成");
                return;
            }

            guideOpen = !guideOpen;
            fieldGuidePanel.SetActive(guideOpen);
            if (guideOpen)
            {
                PopulateGuide();
            }

            ApplyPause();
        }

        /// <summary>
        /// 游戏是否能够暂停的逻辑
        /// </summary>
        private void ApplyPause()
        {
            var shouldPause = gameController != null &&
                              gameController.Mode == GameMode.PvE &&
                              (settingOpen || guideOpen);
            if (shouldPause && !pauseHeld)
            {
                pauseScale = Time.timeScale;
                Time.timeScale = 0f;
                pauseHeld = true;
                return;
            }

            if (!shouldPause && pauseHeld)
            {
                Time.timeScale = pauseScale;
                pauseHeld = false;
            }
        }

        /// <summary>
        /// 填充图鉴
        /// </summary>
        private void PopulateGuide()
        {
            var content = EnsureGuideListContent();
            if (content == null)
            {
                return;
            }

            if (cardCatalog == null || cardCatalog.cards == null)
            {
                guideCards.Clear();
                EnsureGuideItems(content, 0);
                ShowGuideCard(null);
                return;
            }

            // 清空当前图鉴的卡牌（为筛选做铺垫）
            guideCards.Clear();
            
            // 将符合要求的卡牌插入到 图鉴清单
            for (var i = 0; i < cardCatalog.cards.Count; i++)
            {
                if (cardCatalog.cards[i] is SkillCardBase skill && PassGuideFilter(skill))
                {
                    InsertGuideCard(skill);
                }
            }

            EnsureGuideItems(content, guideCards.Count);
            for (var i = 0; i < guideItems.Count; i++)
            {
                var active = i < guideCards.Count;
                guideItems[i].gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                var skill = guideCards[i];
                var unlocked = IsGuideCardUnlocked(skill);
                var rect = guideItems[i].GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2(0f, -54f - i * 62f);
                rect.offsetMax = new Vector2(0f, -i * 62f);

                var label = guideItems[i].GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    SetQualityText(label, unlocked
                        ? $"{BaseTag(skill.SkillCardType)} {skill.DisplayName}"
                        : $"{BaseTag(skill.SkillCardType)} 未解锁", skill.Quality, unlocked);
                }
                if (guideItems[i].TryGetComponent<Image>(out var img))
                {
                    SetImage(img, null, GuideItemColor(skill == guideSelected, skill.Quality));
                }

                var captured = skill;
                guideItems[i].onClick.RemoveAllListeners();
                guideItems[i].onClick.AddListener(() => ShowGuideCard(captured));
            }

            if (content.TryGetComponent<RectTransform>(out var contentRt))
            {
                Canvas.ForceUpdateCanvases();
                var viewportRt = fieldGuideListViewport != null
                    ? fieldGuideListViewport.GetComponent<RectTransform>()
                    : null;
                var viewportHeight = viewportRt != null ? viewportRt.rect.height : 0f;
                var itemHeight = 62f * guideCards.Count + 8f;
                contentRt.anchorMin = new Vector2(0f, 1f);
                contentRt.anchorMax = new Vector2(1f, 1f);
                contentRt.pivot = new Vector2(0.5f, 1f);
                contentRt.anchoredPosition = Vector2.zero;
                contentRt.sizeDelta = new Vector2(0f, Mathf.Max(viewportHeight, itemHeight));

                if (fieldGuideList != null && fieldGuideList.TryGetComponent<ScrollRect>(out var scrollRect))
                {
                    scrollRect.StopMovement();
                    scrollRect.movementType = ScrollRect.MovementType.Clamped;
                    scrollRect.elasticity = 0f;
                    scrollRect.inertia = false;
                    scrollRect.vertical = itemHeight > viewportHeight + 0.5f;
                    scrollRect.verticalNormalizedPosition = 1f;
                }
            }

            ShowGuideCard(guideCards.Contains(guideSelected) ? guideSelected : guideCards.Count > 0 ? guideCards[0] : null);
            RefreshGuideFilterButtons();
        }

        /// <summary>
        /// 获取图鉴列表内容节点。场景未绑定列表 Content 时在现有图鉴面板下补建，
        /// 保证图鉴仍然可以显示动态生成的技能卡条目。
        /// </summary>
        private Transform EnsureGuideListContent()
        {
            if (fieldGuideListContent != null)
            {
                return fieldGuideListContent.transform;
            }

            Debug.LogError("Battle 场景缺少静态 FieldGuideListContent，图鉴不会动态创建 UI。", this);
            return null;
        }

        /// <summary>
        /// 展示卡牌图鉴
        /// </summary>
        private void ShowGuideCard(SkillCardBase skill)
        {
            guideSelected = skill;
            if (skill == null)
            {
                SetText("FieldGuideDetailTitle", "无");
                SetText("FieldGuideDetailBody", "当前筛选条件下没有卡牌");
                SetFieldGuidePreviewText(string.Empty);
                SetGuidePreview(null, Color.white);
                return;
            }

            if (!IsGuideCardUnlocked(skill))
            {
                SetText("FieldGuideDetailTitle", "未解锁");
                SetText("FieldGuideDetailBody", "击败对应首领后解锁");
                SetFieldGuidePreviewText("未解锁");
                SetGuidePreview(GuideSprite(skill), Color.gray);
                return;
            }

            SetQualityText(fieldGuideDetailTitle != null ? fieldGuideDetailTitle.GetComponent<Text>() : null,
                skill.DisplayName, skill.Quality, true);
            SetFieldGuidePreviewText(skill.DisplayName);
            SetText("FieldGuideDetailBody", GuideBody(skill));
            SetGuidePreview(GuideSprite(skill), Color.white);

            SetTextColor("FieldGuideDetailTitle", QualityColor(skill.Quality));
            PopulateGuideSelectionOnly();
        }

        private bool IsGuideCardUnlocked(SkillCardBase skill)
        {
            return skill != null && PveCampaignService.IsSkillUnlocked(
                accountService != null ? accountService.UnlockProgress : null,
                skill.CardId);
        }

        /// <summary>
        /// 执行 GuideBody 对应的运行时流程。
        /// </summary>
        private string GuideBody(SkillCardBase skill)
        {
            var desc = string.IsNullOrWhiteSpace(skill.Description) ? "暂无详细介绍" : skill.Description;
            var pre = skill.CanUseOnPre ? "可在备战期使用" : "仅战斗期使用";
            return
                $"品级：{QualityName(skill.Quality)}\n" +
                $"类型：{BaseTypeName(skill.SkillCardType)}强化\n" +
                $"数值：{FormatNumber(skill.SkillPower)}\n" +
                $"阶段：{pre}\n" +
                $"效果：{EffectNames(skill)}\n\n" +
                desc;
        }

        /// <summary>
        /// 筛选器：设置筛选条件
        /// </summary>
        private void SetGuideFilter(BaseCardType? type, CardQuality? quality)
        {
            guideType = type;
            guideQuality = quality;
            PopulateGuide();
            RefreshGuideFilterButtons();
        }

        /// <summary>
        /// 图鉴筛选器
        /// </summary>
        private bool PassGuideFilter(SkillCardBase skill)
        {
            if (skill == null)
            {
                return false;
            }

            // 当前 图鉴基础类型筛选 枚举对象有值并且 该强化卡的基础卡牌类型不等于该值
            if (guideType.HasValue && skill.SkillCardType != guideType.Value)
            {
                return false;
            }

            // 图鉴品质筛选 枚举对象没有值 或者 图鉴品质筛选 对象的值和 该强化卡的品质枚举 一致
            return !guideQuality.HasValue || skill.Quality == guideQuality.Value;
        }

        /// <summary>
        /// 插入目标牌到图鉴里，并且根据 品质*类型的 优先级进行排序
        /// </summary>
        private void InsertGuideCard(SkillCardBase skill)
        {
            var order = GuideOrder(skill);
            for (var i = 0; i < guideCards.Count; i++)
            {
                var other = guideCards[i];
                var otherOrder = GuideOrder(other);
                if (order < otherOrder ||
                    order == otherOrder && string.CompareOrdinal(skill.DisplayName, other.DisplayName) < 0)
                {
                    guideCards.Insert(i, skill);
                    return;
                }
            }

            guideCards.Add(skill);
        }

        /// <summary>
        /// 卡牌在图鉴里的优先级编号：每个编号对应 （一种品质 * 一种类型） 
        /// </summary>
        private static int GuideOrder(SkillCardBase skill)
        {
            if (skill == null)
            {
                return int.MaxValue;
            }

            return (int)skill.Quality * 10 + (int)skill.SkillCardType;
        }

        /// <summary>
        /// 执行 EnsureGuideItems 对应的运行时流程。
        /// </summary>
        private void EnsureGuideItems(Transform content, int count)
        {
            var template = guideFilterAll != null ? guideFilterAll.GetComponent<Button>() : null;
            if (template == null)
            {
                Debug.LogError("远端图鉴预制体缺少 GuideFilterAll 按钮，无法创建图鉴条目。", this);
                return;
            }

            while (guideItems.Count < count)
            {
                var index = guideItems.Count + 1;
                var item = Instantiate(template, content, false);
                item.name = $"GuideSkillItem{index}";
                var label = item.GetComponentInChildren<Text>(true);
                if (label != null)
                    label.name = $"{item.name}_Label";
                guideItems.Add(item);
            }
        }

        /// <summary>
        /// 执行 PopulateGuideSelectionOnly 对应的运行时流程。
        /// </summary>
        private void PopulateGuideSelectionOnly()
        {
            for (var i = 0; i < guideItems.Count && i < guideCards.Count; i++)
            {
                if (guideItems[i].TryGetComponent<Image>(out var img))
                {
                    SetImage(img, null, GuideItemColor(guideCards[i] == guideSelected, guideCards[i].Quality));
                }
            }
        }

        /// <summary>
        /// 执行 RefreshGuideFilterButtons 对应的运行时流程。
        /// </summary>
        private void RefreshGuideFilterButtons()
        {
            SetGuideButton(guideFilterAll, !guideType.HasValue && !guideQuality.HasValue);
            SetGuideButton(guideFilterAtk, guideType == BaseCardType.Attack);
            SetGuideButton(guideFilterDef, guideType == BaseCardType.Defense);
            SetGuideButton(guideFilterHeal, guideType == BaseCardType.Heal);
            SetGuideButton(guideFilterSilver, guideQuality == CardQuality.Silver);
            SetGuideButton(guideFilterGold, guideQuality == CardQuality.Gold);
            SetGuideButton(guideFilterPlatinum, guideQuality == CardQuality.Platinum);
            SetGuideButton(guideFilterPrismatic, guideQuality == CardQuality.Prismatic);
        }

        /// <summary>
        /// 执行 SetGuideButton 对应的运行时流程。
        /// </summary>
        private static void SetGuideButton(GameObject target, bool active)
        {
            if (target == null)
            {
                return;
            }

            var img = target.GetComponent<Image>();
            SetImage(img, null, active ? new Color32(0x4E, 0x67, 0x8E, 0xFF) : new Color32(0x2E, 0x38, 0x48, 0xFF));
        }

        /// <summary>
        /// 执行 GuideItemColor 对应的运行时流程。
        /// </summary>
        private static Color GuideItemColor(bool selected, CardQuality quality)
        {
            if (quality == CardQuality.Prismatic)
                return selected ? new Color32(125, 63, 178, 255) : new Color32(84, 42, 120, 255);
            var color = QualityColor(quality);
            return selected
                ? Color.Lerp(new Color32(0x18, 0x1D, 0x26, 0xFF), color, 0.55f)
                : Color.Lerp(new Color32(0x18, 0x1D, 0x26, 0xFF), color, 0.20f);
        }

        /// <summary>
        /// 执行 GuideSprite 对应的运行时流程。
        /// </summary>
        private Sprite GuideSprite(SkillCardBase skill)
        {
            return skill == null ? null : CardPrefabSprite(skill);
        }

        /// <summary>
        /// 执行 SetGuidePreview 对应的运行时流程。
        /// </summary>
        private void SetGuidePreview(Sprite sprite, Color color)
        {
            var obj = fieldGuideCardPreview;
            if (!obj.TryGetComponent<Image>(out var img))
            {
                return;
            }

            SetImage(img, sprite, color);
            img.preserveAspect = sprite != null;
        }

        /// <summary>
        /// 执行 EffectNames 对应的运行时流程。
        /// </summary>
        private static string EffectNames(SkillCardBase skill)
        {
            if (skill == null || skill.Effects == null || skill.Effects.Count == 0)
            {
                return skill != null && skill.SkillPower != 0 ? "基础数值强化" : "无额外效果";
            }

            var parts = new List<string>();
            for (var i = 0; i < skill.Effects.Count; i++)
            {
                var fx = skill.Effects[i];
                if (fx == null)
                {
                    continue;
                }

                parts.Add(EffectName(fx.effectKind));
            }

            return parts.Count > 0 ? string.Join(", ", parts) : "无额外效果";
        }

        /// <summary>
        /// 执行 EffectName 对应的运行时流程。
        /// </summary>
        private static string EffectName(CardEffectKind kind)
        {
            switch (kind)
            {
                case CardEffectKind.SetPower:
                    return "独立数值";
                case CardEffectKind.DamageBoost:
                case CardEffectKind.DamageMultiplier:
                case CardEffectKind.CopyDamage:
                    return "基础伤害强化";
                case CardEffectKind.HealBoost:
                    return "治疗强化";
                case CardEffectKind.ShieldBoost:
                case CardEffectKind.HolyShield:
                    return "护盾";
                case CardEffectKind.DefensePierce:
                case CardEffectKind.DefensePierceValue:
                case CardEffectKind.IgnoreShield:
                    return "穿透防御/护盾";
                case CardEffectKind.TrueDamage:
                case CardEffectKind.TrueDamageMultiplier:
                    return "真实伤害";
                case CardEffectKind.Burn:
                case CardEffectKind.MultiStrikeBurn:
                    return "灼烧伤害";
                case CardEffectKind.LifeSteal:
                case CardEffectKind.LifeStealRatio:
                    return "生命偷取";
                case CardEffectKind.HealOverTime:
                case CardEffectKind.RegenTurnStart:
                    return "持续恢复";
                case CardEffectKind.CleanseDebuff:
                    return "净化";
                case CardEffectKind.ReduceNextDamage:
                case CardEffectKind.DoubleDefense:
                case CardEffectKind.FatalGuard:
                    return "增强防御";
                case CardEffectKind.Thorn:
                case CardEffectKind.ReflectBlockedDamage:
                case CardEffectKind.RevengeShield:
                case CardEffectKind.MirrorDamage:
                    return "计时";
                case CardEffectKind.DisableCards:
                case CardEffectKind.Stun:
                    return "控制";
                case CardEffectKind.ExtraTurn:
                    return "互换";
                default:
                    return "";
            }
        }

        /// <summary>
        /// 加载战斗资源
        /// </summary>
        private IEnumerator LoadBattleAssets()
        {
            if (!GameApp.TryGet<IGameAssetService>(out var assets))
            {
                yield break;
            }

            yield return LoadAsset<BalanceConfig>("BalanceConfig", value => defaultBalanceConfig = value);
            yield return LoadAsset<MatchDeckConfig>("MatchDeckConfig", value => defaultMatchDeckConfig = value);
            yield return LoadAsset<CardCatalog>("CardCatalog", value => defaultCardCatalog = value);
            yield return LoadAsset<CardArtConfig>("CardArtConfig", value => defaultCardArtConfig = value);
            yield return LoadAsset<GameObject>("Card_Base_Attack", value => baseAttackPrefab = value);
            yield return LoadAsset<GameObject>("Card_Base_Defense", value => baseDefensePrefab = value);
            yield return LoadAsset<GameObject>("Card_Base_Heal", value => baseHealPrefab = value);
            yield return LoadAsset<GameObject>("Card_Skill_Silver", value => skillSilverPrefab = value);
            yield return LoadAsset<GameObject>("Card_Skill_Gold", value => skillGoldPrefab = value);
            yield return LoadAsset<GameObject>("Card_Skill_Platinum", value => skillPlatinumPrefab = value);
            yield return LoadAsset<GameObject>("Card_Skill_Prismatic", value => skillPrismaticPrefab = value);
            yield return LoadAsset<Shader>("UIEnhancementGlow", value => battleFxShader = value);
            yield return LoadAsset<Shader>("ShieldCapsule", value => shieldFxShader = value);
            yield return LoadAsset<GameObject>("FieldGuidePanel", BindRemoteFieldGuide);
            yield return LoadAsset<Sprite>("GameBG", value => BindSceneSprite("BattleBackground", value));
            yield return LoadAsset<Sprite>("Show", value => BindSceneSprite("ExplainPanel", value));
            yield return LoadSubAsset<Sprite>("UIS", "UIS_1", value => BindSceneSprite("GameSettings", value));
            yield return LoadSubAsset<Sprite>("UIS", "UIS_0", value => BindSceneSprite("FieldGuide", value));
            yield return LoadAsset<Sprite>("Tag_Silver", value => enhancementTagSprites[CardQuality.Silver] = value);
            yield return LoadAsset<Sprite>("Tag_Gold", value => enhancementTagSprites[CardQuality.Gold] = value);
            yield return LoadAsset<Sprite>("Tag_Platinum", value => enhancementTagSprites[CardQuality.Platinum] = value);
            yield return LoadAsset<Sprite>("Tag_Prismatic", value => enhancementTagSprites[CardQuality.Prismatic] = value);
        }

        private void BindRemoteFieldGuide(GameObject prefab)
        {
            if (prefab == null || battlefieldPanel == null)
                return;

            var battleCanvas = SceneObject("BattleCanvas");
            var parent = battleCanvas != null ? battleCanvas.transform : battlefieldPanel.transform.parent;
            fieldGuidePanel = Instantiate(prefab, parent, false);
            fieldGuidePanel.name = "FieldGuidePanel";
            fieldGuidePanel.SetActive(false);
            var rect = fieldGuidePanel.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one;
            }

            guideFilterAll = FindDescendant(fieldGuidePanel.transform, "GuideFilterAll");
            guideFilterAtk = FindDescendant(fieldGuidePanel.transform, "GuideFilterAtk");
            guideFilterDef = FindDescendant(fieldGuidePanel.transform, "GuideFilterDef");
            guideFilterHeal = FindDescendant(fieldGuidePanel.transform, "GuideFilterHeal");
            guideFilterSilver = FindDescendant(fieldGuidePanel.transform, "GuideFilterSilver");
            guideFilterGold = FindDescendant(fieldGuidePanel.transform, "GuideFilterGold");
            guideFilterPlatinum = FindDescendant(fieldGuidePanel.transform, "GuideFilterPlatinum");
            guideFilterPrismatic = FindDescendant(fieldGuidePanel.transform, "GuideFilterPrismatic");
            fieldGuideClose = FindDescendant(fieldGuidePanel.transform, "FieldGuideClose");
            fieldGuideCardPreview = FindDescendant(fieldGuidePanel.transform, "FieldGuideCardPreview");
            fieldGuideList = FindDescendant(fieldGuidePanel.transform, "FieldGuideList");
            fieldGuideListViewport = FindDescendant(fieldGuidePanel.transform, "FieldGuideListViewport");
            fieldGuideListContent = FindDescendant(fieldGuidePanel.transform, "FieldGuideListContent");
            fieldGuideCardPreviewText = FindDescendant(fieldGuidePanel.transform, "FieldGuideCardPreviewText");
            if (fieldGuideCardPreviewText != null && fieldGuideCardPreviewText.TryGetComponent<Text>(out var previewText))
                fieldGuideCardPreviewTextDefaultColor = previewText.color;
            fieldGuideDetailTitle = FindDescendant(fieldGuidePanel.transform, "FieldGuideDetailTitle");
            fieldGuideDetailBody = FindDescendant(fieldGuidePanel.transform, "FieldGuideDetailBody");
            BindFieldGuideButtons();
            if (tutorialActive)
                BindTutorialOverlay(prefab);
        }

        private void BindTutorialOverlay(GameObject prefab)
        {
            var battleCanvas = SceneObject("BattleCanvas");
            var parent = battleCanvas != null ? battleCanvas.transform : battlefieldPanel.transform.parent;
            tutorialPanel = Instantiate(prefab, parent, false);
            tutorialPanel.name = "TutorialOverlayPanel";
            tutorialPanel.SetActive(true);
            tutorialPanel.transform.SetAsLastSibling();

            var keep = new HashSet<string>(StringComparer.Ordinal)
            {
                "FieldGuideDetailTitle", "FieldGuideDetailBody", "FieldGuideClose"
            };
            for (var index = tutorialPanel.transform.childCount - 1; index >= 0; index--)
            {
                var child = tutorialPanel.transform.GetChild(index).gameObject;
                child.SetActive(keep.Contains(child.name));
            }

            var panelRect = tutorialPanel.GetComponent<RectTransform>();
            if (panelRect != null)
            {
                panelRect.anchorMin = new Vector2(0.5f, 1f);
                panelRect.anchorMax = new Vector2(0.5f, 1f);
                panelRect.pivot = new Vector2(0.5f, 1f);
                panelRect.anchoredPosition = new Vector2(0f, -18f);
                panelRect.sizeDelta = new Vector2(1120f, 205f);
                panelRect.localScale = Vector3.one;
            }

            tutorialTitle = FindDescendant(tutorialPanel.transform, "FieldGuideDetailTitle");
            tutorialBody = FindDescendant(tutorialPanel.transform, "FieldGuideDetailBody");
            var closeObject = FindDescendant(tutorialPanel.transform, "FieldGuideClose");
            ConfigureTutorialText(tutorialTitle, new Vector2(0f, -18f), new Vector2(1040f, 48f), 30);
            ConfigureTutorialText(tutorialBody, new Vector2(0f, -78f), new Vector2(1040f, 105f), 25);

            foreach (var graphic in tutorialPanel.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            tutorialCompleteButton = closeObject != null ? closeObject.GetComponent<Button>() : null;
            if (tutorialCompleteButton != null)
            {
                tutorialCompleteButton.onClick.RemoveAllListeners();
                tutorialCompleteButton.gameObject.SetActive(false);
                if (tutorialCompleteButton.targetGraphic != null)
                    tutorialCompleteButton.targetGraphic.raycastTarget = true;
            }
            CreateTutorialSkipBattleButton();
        }

        private void CreateTutorialSkipBattleButton()
        {
            if (tutorialSkipBattleButton != null)
                return;

            if (tutorialCompleteButton == null || tutorialPanel == null)
                return;

            // The skip action replaces the tutorial close action during battle, so it
            // deliberately shares the same parent, position and visual layout.
            var buttonObject = Instantiate(tutorialCompleteButton.gameObject,
                tutorialCompleteButton.transform.parent, false);
            buttonObject.name = "TutorialSkipBattleButton";
            var button = buttonObject.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(SkipTutorialBattlePhase);

            var label = buttonObject.GetComponentInChildren<Text>(true);
            if (label != null)
                label.text = "跳过本轮战斗";
            tutorialSkipBattleButton = button;
            buttonObject.SetActive(false);
        }

        private void RefreshTutorialSkipBattleButton()
        {
            if (tutorialSkipBattleButton == null)
                return;

            var visible = tutorialActive && !tutorialFinished &&
                          gameController != null && gameController.State == GameState.Battle &&
                          tutorialStep == TutorialStep.ObserveShieldAndTimer;
            tutorialSkipBattleButton.gameObject.SetActive(visible);
        }

        private void SkipTutorialBattlePhase()
        {
            if (gameController == null || gameController.State != GameState.Battle)
                return;

            gameController.EndBattleAction(LocalPlayerId, out _);
            gameController.EndBattleAction(AiPlayerId, out _);
            RefreshStaticUi();
        }

        private static void ConfigureTutorialText(GameObject target, Vector2 position, Vector2 size, int fontSize)
        {
            if (target == null)
                return;
            var rect = target.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
            }
            if (target.TryGetComponent<Text>(out var label))
            {
                label.fontSize = fontSize;
                label.resizeTextForBestFit = true;
                label.alignment = TextAnchor.UpperCenter;
                label.color = Color.white;
            }
        }

        private static GameObject FindDescendant(Transform root, string objectName)
        {
            if (root == null)
                return null;

            var children = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < children.Length; index++)
            {
                if (string.Equals(children[index].name, objectName, StringComparison.Ordinal))
                    return children[index].gameObject;
            }

            return null;
        }

        private void BindSceneSprite(string objectName, Sprite sprite)
        {
            var target = SceneObject(objectName);
            if (target != null && target.TryGetComponent<Image>(out var image))
                image.sprite = sprite;
        }

        /// <summary>
        /// 通过资源服务异步加载战斗资源，失败时在编辑器下回退到本地路径。
        /// </summary>
        private IEnumerator LoadAsset<T>(string key, Action<T> apply) where T : UnityEngine.Object
        {
            if (BattleAssetPreloader.TryGet<T>(key, out var cached))
            {
                apply?.Invoke(cached);
                yield break;
            }

            if (!GameApp.TryGet<IGameAssetService>(out var assets))
            {
                yield break;
            }

            Task<T> task = null;
            try
            {
                task = assets.LoadAssetAsync<T>(key, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"资源加载请求失败：{key}，{ex.Message}");
                yield break;
            }

            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
            {
                apply?.Invoke(task.Result);
            }
            else if (task.Exception != null)
            {
                Debug.LogWarning($"战斗资源加载失败：{key}，{task.Exception.GetBaseException().Message}");
            }
            else
            {
                Debug.LogWarning($"战斗资源加载失败：{key}，返回资源为空");
            }
        }
        

        private CardCatalog BuildRuntimeCatalog(CardCatalog source)
        {
            var catalog = ScriptableObject.CreateInstance<CardCatalog>();
            if (source != null && source.cards != null)
            {
                for (var i = 0; i < source.cards.Count; i++)
                {
                    AddCatalogCardUnique(catalog, source.cards[i]);
                }
            }

            if (!HasBaseCards(catalog))
            {
                AddCatalogCardUnique(catalog, CreateBasicCard("base_attack", "攻击", "造成基础伤害", BaseCardType.Attack, 1, false));
                AddCatalogCardUnique(catalog, CreateBasicCard("base_defense", "防御", "获得防御", BaseCardType.Defense, 1, false));
                AddCatalogCardUnique(catalog, CreateBasicCard("base_heal", "治疗", "回复生命", BaseCardType.Heal, 1, true));
            }

            SkillHot.AddTo(catalog);
            if (!HasSkillCards(catalog))
            {
                AddCatalogCardUnique(catalog, CreateSkillCard("skill_attack_silver", "强攻", "提高攻击牌伤害", BaseCardType.Attack, 3, CardQuality.Silver, false));
                AddCatalogCardUnique(catalog, CreateSkillCard("skill_defense_silver", "坚守", "提高防御牌防御属性", BaseCardType.Defense, 4, CardQuality.Silver, true));
                AddCatalogCardUnique(catalog, CreateSkillCard("skill_heal_silver", "急救", "提高治疗牌回复", BaseCardType.Heal, 3, CardQuality.Silver, true));
            }

            return catalog;
        }

        private static void AddCatalogCardUnique(CardCatalog catalog, CardBase card)
        {
            if (catalog == null || catalog.cards == null || card == null || string.IsNullOrEmpty(card.CardId))
            {
                return;
            }

            for (var i = 0; i < catalog.cards.Count; i++)
            {
                if (catalog.cards[i] != null && string.Equals(catalog.cards[i].CardId, card.CardId, StringComparison.Ordinal))
                {
                    return;
                }
            }

            catalog.cards.Add(card);
        }

        private static bool HasBaseCards(CardCatalog catalog)
        {
            if (catalog == null || catalog.cards == null)
            {
                return false;
            }

            for (var i = 0; i < catalog.cards.Count; i++)
            {
                if (catalog.cards[i] is HandCardBase)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSkillCards(CardCatalog catalog)
        {
            if (catalog == null || catalog.cards == null)
            {
                return false;
            }

            for (var i = 0; i < catalog.cards.Count; i++)
            {
                if (catalog.cards[i] is SkillCardBase)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 创建运行时基础手牌定义。
        /// </summary>
        private static BasicHandCard CreateBasicCard(string id, string name, string desc, BaseCardType type, int power, bool canUseOnPre)
        {
            var card = ScriptableObject.CreateInstance<BasicHandCard>();
            card.ConfigureRuntime(id, name, desc, type, power, canUseOnPre);
            return card;
        }

        /// <summary>
        /// 创建运行时强化卡牌定义。
        /// </summary>
        private static StrengthenedSkillCard CreateSkillCard(string id, string name, string desc, BaseCardType type, int power, CardQuality quality, bool canUseOnPre)
        {
            var card = ScriptableObject.CreateInstance<StrengthenedSkillCard>();
            card.ConfigureRuntime(id, name, desc, type, power, quality, canUseOnPre);
            return card;
        }

        /// <summary>
        /// 启动本地人机战斗，建立玩家、AI、卡牌管理器和战斗状态机。
        /// </summary>
        private void BootstrapPveBattle()
        {
            onlineBattle = false;
            localPlayerId = "player";
            opponentPlayerId = "ai";
            balanceConfig = defaultBalanceConfig != null ? defaultBalanceConfig : ScriptableObject.CreateInstance<BalanceConfig>();
            matchDeckConfig = defaultMatchDeckConfig;
            cardCatalog = BuildRuntimeCatalog(defaultCardCatalog);
            cardArtConfig = defaultCardArtConfig;
            accountService = new LocalAccountService(balanceConfig);
            var progress = accountService.UnlockProgress;
            bossBattle = PveCampaignService.IsBossBattle(progress);
            bossIndex = PveCampaignService.GetBossWinsCompleted(progress);
            var availableCardIds = PveCampaignService.GetAvailableCardIds(progress, cardCatalog);
            cardManager = GameplayHot.CreateCardManager(balanceConfig, matchDeckConfig, cardCatalog, Environment.TickCount);
            combatResolver = CombatHot.Create(balanceConfig);
            var repository = GameApp.TryGet<GameDataRepository>(out var dataRepository) ? dataRepository : null;
            aiController = GameplayHot.CreateAi(BuildPveAiConfig(repository != null ? repository.AiBrainConfig : null, bossBattle, bossIndex), cardManager, Environment.TickCount);
            gameController = new GameController(balanceConfig, cardManager, combatResolver);
            gameController.Configure(GameMode.PvE, PlayerRole.Host, LocalPlayerId, AiPlayerId);
            gameController.StateChanged += OnStateChanged;
            gameController.ActionResolved += OnActionResolved;
            gameController.HandUpdated += _ => RefreshCards();
            gameController.DiscardRequested += OnDiscardRequested;
            gameController.GameEnded += OnGameEnded;
            localPlayer = new PlayerCombatStateData { playerId = LocalPlayerId };
            aiPlayer = new PlayerCombatStateData { playerId = AiPlayerId };
            gameController.RegisterPlayer(localPlayer);
            gameController.RegisterPlayer(aiPlayer);
            cardManager.InitializeDeck(localPlayer, availableCardIds);
            cardManager.InitializeDeck(aiPlayer, availableCardIds);
            if (tutorialActive)
            {
                cardManager.ConfigureTutorialMode(LocalPlayerId, AiPlayerId);
                tutorialStep = TutorialStep.SelectShieldSkill;
            }
            battleStartTime = Time.unscaledTime;
            // Bosses should begin making decisions promptly after the deal animation.
            aiActionTimer = bossBattle ? 1.5f : 2.0f;
            SetResultLog();
            gameController.StartGame();
            RefreshCards();
            RefreshStaticUi();
            if (tutorialActive)
                ShowTutorialStep();
        }

        private void BootstrapOnlineBattle()
        {
            onlineService = UosOnlineService.Instance;
            if (onlineService == null || !onlineService.TryGetBattleParticipants(
                    out localPlayerId, out opponentPlayerId, out onlineHost, out opponentSenderId))
            {
                Debug.LogError("联机 Battle 初始化失败：房间不是两名真人玩家，拒绝回退到人机对战。", this);
                SetText(battlefieldHintText, "联机房间人数异常，正在返回主界面");
                SceneManager.LoadScene("Main");
                return;
            }

            balanceConfig = defaultBalanceConfig != null ? defaultBalanceConfig : ScriptableObject.CreateInstance<BalanceConfig>();
            matchDeckConfig = defaultMatchDeckConfig;
            cardCatalog = BuildRuntimeCatalog(defaultCardCatalog);
            cardArtConfig = defaultCardArtConfig;
            accountService = new LocalAccountService(balanceConfig);
            var availableCardIds = PveCampaignService.GetAvailableCardIds(accountService.UnlockProgress, cardCatalog);
            cardManager = GameplayHot.CreateCardManager(balanceConfig, matchDeckConfig, cardCatalog, Environment.TickCount);
            snapshotSanitizer = new BattleSnapshotSanitizer(cardManager, cardCatalog);
            combatResolver = CombatHot.Create(balanceConfig);
            aiController = null;
            gameController = new GameController(balanceConfig, cardManager, combatResolver);
            gameController.Configure(GameMode.P2P, onlineHost ? PlayerRole.Host : PlayerRole.Guest,
                LocalPlayerId, AiPlayerId);
            gameController.StateChanged += OnStateChanged;
            gameController.ActionResolved += OnActionResolved;
            gameController.HandUpdated += _ =>
            {
                if (onlineGameStarted)
                    RefreshCards();
            };
            gameController.DiscardRequested += OnDiscardRequested;
            gameController.GameEnded += OnGameEnded;
            localPlayer = new PlayerCombatStateData { playerId = LocalPlayerId };
            aiPlayer = new PlayerCombatStateData { playerId = AiPlayerId };
            gameController.RegisterPlayer(localPlayer);
            gameController.RegisterPlayer(aiPlayer);
            cardManager.InitializeDeck(localPlayer, availableCardIds);
            cardManager.InitializeDeck(aiPlayer, availableCardIds);

            onlineService.EventReceivedWithSender -= OnBattleNetworkEvent;
            onlineService.EventReceivedWithSender += OnBattleNetworkEvent;
            onlineService.PlayerLeft -= OnOpponentLeft;
            onlineService.PlayerLeft += OnOpponentLeft;
            onlineService.Disconnected -= OnLocalNetworkDisconnected;
            onlineService.Disconnected += OnLocalNetworkDisconnected;
            battleStartTime = Time.unscaledTime;
            nextNetworkSyncTime = Time.unscaledTime + 0.25f;
            nextHeartbeatTime = Time.unscaledTime + NetworkHeartbeatInterval;
            lastOpponentMessageTime = Time.unscaledTime;
            opponentLeftSuspectedAt = -1f;
            opponentDisplayName = ResolveOpponentDisplayName();
            onlineGameStarted = false;
            networkSession.Reset();
            SetResultLog();
            RefreshCards();
            RefreshStaticUi();

            if (!onlineHost)
                SendBattleMessage(new BattleNetworkMessageData { type = "ready" }, true);

            SetText(battlefieldHintText, string.Empty);
            Debug.Log(onlineHost
                ? "[UOS] Battle 已加载，后台等待客户端 ready"
                : "[UOS] Battle 已加载，客户端 ready 已发送，后台等待 start");
            Debug.Log($"[UOS] Battle 使用真人对战：local={LocalPlayerId}, opponent={OpponentPlayerId}, host={onlineHost}");
        }

        private void OnOpponentLeft(MuninnPlayer player)
        {
            if (!onlineBattle || gameOverHandled || gameController == null || player == null)
                return;

            if (player.SenderId != opponentSenderId && !string.Equals(player.Id, OpponentPlayerId, StringComparison.Ordinal))
                return;

            opponentLeftSuspectedAt = Time.unscaledTime;
            Debug.LogWarning($"[UOS] 检测到对手离房事件，后台等待连接宽限期：opponent={OpponentPlayerId}");
        }

        private void TickOnlineConnection()
        {
            var now = Time.unscaledTime;
            if (onlineService == null || !onlineService.IsInRoom)
            {
                OnLocalNetworkDisconnected();
                return;
            }

            if (now >= nextHeartbeatTime)
            {
                nextHeartbeatTime = now + NetworkHeartbeatInterval;
                var messageType = !onlineHost && !onlineGameStarted ? "ready" : "heartbeat";
                SendBattleMessage(new BattleNetworkMessageData { type = messageType }, !onlineHost);
            }

            if (!onlineGameStarted)
                return;

            var leftGraceExpired = opponentLeftSuspectedAt >= 0f &&
                                   now - opponentLeftSuspectedAt >= PlayerLeftGraceSeconds;
            var heartbeatExpired = now - lastOpponentMessageTime >= OpponentTimeoutSeconds;
            if (leftGraceExpired || heartbeatExpired)
                EndOnlineBattleForConnectionLoss(true, leftGraceExpired ? "opponent_left" : "opponent_timeout");
        }

        private void OnLocalNetworkDisconnected()
        {
            if (!onlineBattle || gameOverHandled || gameController == null)
                return;

            EndOnlineBattleForConnectionLoss(false, "local_disconnected");
        }

        private void EndOnlineBattleForConnectionLoss(bool localWins, string reason)
        {
            OnGameEnded(new GameOverMessageData
            {
                winnerId = localWins ? LocalPlayerId : OpponentPlayerId,
                loserId = localWins ? OpponentPlayerId : LocalPlayerId,
                winnerHp = localWins
                    ? (localPlayer != null ? Mathf.RoundToInt(localPlayer.hp) : 0)
                    : (aiPlayer != null ? Mathf.RoundToInt(aiPlayer.hp) : 0),
                reason = reason
            });
        }

        private AiBrainConfigData BuildPveAiConfig(AiBrainConfigData source, bool isBoss, int completedBosses)
        {
            var progress = Mathf.Clamp(completedBosses, 0, PveCampaignService.BossCount);
            var config = new AiBrainConfigData
            {
                aiStyle = isBoss ? AiStyle.Boss : AiStyle.Normal,
                minActionDelaySeconds = isBoss ? Mathf.Max(1.75f, 1.95f - progress * 0.03f) : Mathf.Max(1.25f, 1.65f - progress * 0.04f),
                maxActionDelaySeconds = isBoss ? Mathf.Max(2.25f, 2.25f - progress * 0.03f) : Mathf.Max(1.95f, 2.15f - progress * 0.04f),
                skillPlayChance = isBoss
                    ? Mathf.Min(0.95f, 0.58f + progress * 0.04f)
                    : progress >= PveCampaignService.BossCount
                        ? UnityEngine.Random.Range(0.50f, 0.90f)
                        : Mathf.Min(0.65f, 0.16f + progress * 0.04f)
            };

            if (source != null && source.actionWeights != null)
            {
                config.actionWeights.AddRange(source.actionWeights);
            }

            if (cardCatalog != null && cardCatalog.cards != null)
            {
                for (var i = 0; i < cardCatalog.cards.Count; i++)
                {
                    if (cardCatalog.cards[i] is SkillCardBase skill)
                    {
                        config.bossPreferences.Add(new BossCardPreferenceData
                        {
                            cardId = skill.CardId,
                            preferenceWeight = 1f + (int)skill.Quality * (1f + progress * 0.2f)
                        });
                    }
                }
            }

            return config;
        }
        

        /// <summary>
        /// 清空手牌对象池，场景销毁时释放复用对象。
        /// </summary>
        private void ClearCardViewPool()
        {
            foreach (var pair in cardViewPool)
            {
                while (pair.Value.Count > 0)
                {
                    var obj = pair.Value.Pop();
                    if (obj != null)
                    {
                        Destroy(obj);
                    }
                }
            }

            cardViewPool.Clear();
        }

        /// <summary>
        /// 推进 AI 行为，优先处理弃牌，其次使用 AI 控制器选择行动。
        /// </summary>
        private void TickAi(float deltaTime)
        {
            if (gameController == null || aiPlayer == null || aiController == null)
            {
                return;
            }

            aiActionTimer -= deltaTime;
            if (aiActionTimer > 0f)
            {
                return;
            }

            if (pendingDealIds.Count > 0)
            {
                aiActionTimer = 0.25f;
                return;
            }

            if (gameController.State == GameState.Discarding)
            {
                aiActionTimer = bossBattle ? 0.25f : 0.5f;
                SubmitAiDiscardOverflow();

                RefreshCards();
                return;
            }

            if (gameController.State != GameState.Battle && gameController.State != GameState.Preparation)
            {
                return;
            }

            var decision = aiController.Decide(aiPlayer, localPlayer, gameController.State);
            aiActionTimer = Mathf.Max(0.1f, decision.delaySeconds);
            if (decision.hasAction)
            {
                gameController.SubmitPlayerAction(decision.action, out _, out _);
            }
            else if (gameController.State == GameState.Battle)
            {
                gameController.EndBattleAction(AiPlayerId, out _);
                RefreshStaticUi();
            }
        }

        private void SendBattleMessage(BattleNetworkMessageData message, bool toMaster)
        {
            if (!onlineBattle || onlineService == null || message == null)
                return;

            try
            {
                message.senderDisplayName = onlineService.LocalDisplayName;
                message.protocolVersion = BattleNetworkMessageData.CurrentProtocolVersion;
                if (message.type == "action")
                {
                    message.action.turnIndex = gameController.TurnIndex;
                    networkSession.StampAction(message);
                }
                var data = Encoding.UTF8.GetBytes(JsonMapper.ToJson(message));
                if (toMaster)
                    onlineService.SendEventToMaster(data);
                else
                    onlineService.SendEvent(data);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[UOS] Battle 消息发送失败：{exception.Message}");
            }
        }

        private void BroadcastBattleSnapshot(ActionResultData actionResult = null,
            GameOverMessageData gameOver = null, bool actionResponse = false, string actionReason = null)
        {
            if (!onlineBattle || !onlineHost || gameController == null)
                return;

            var message = new BattleNetworkMessageData
            {
                type = "snapshot",
                snapshot = snapshotSanitizer.CreateForOpponent(gameController.CreateSnapshot(), LocalPlayerId),
                actionResult = actionResult,
                gameOver = gameOver,
                actionResponse = actionResponse,
                actionReason = actionReason
            };
            networkSession.StampSnapshot(message);
            SendBattleMessage(message, false);
        }

        private void OnBattleNetworkEvent(byte[] data, uint senderId)
        {
            if (!onlineBattle || data == null || data.Length == 0 || onlineService == null)
                return;

            try
            {
                var message = JsonMapper.ToObject<BattleNetworkMessageData>(Encoding.UTF8.GetString(data));
                if (message == null || string.IsNullOrEmpty(message.type))
                    return;
                if (message.protocolVersion != BattleNetworkMessageData.CurrentProtocolVersion)
                {
                    Debug.LogWarning($"[UOS] Battle 协议版本不匹配：received={message.protocolVersion}");
                    return;
                }
                if (senderId != opponentSenderId)
                {
                    Debug.LogWarning($"[UOS] 忽略非对手 Battle 消息：type={message.type}, sender={senderId}, expected={opponentSenderId}");
                    return;
                }

                if (message.type != "snapshot" && message.type != "heartbeat")
                    Debug.Log($"[UOS] 收到 Battle 消息：type={message.type}, sender={senderId}, host={onlineHost}, started={onlineGameStarted}");

                lastOpponentMessageTime = Time.unscaledTime;
                opponentLeftSuspectedAt = -1f;
                if (!string.IsNullOrWhiteSpace(message.senderDisplayName) &&
                    !string.Equals(message.senderDisplayName, OpponentPlayerId, StringComparison.Ordinal))
                {
                    opponentDisplayName = message.senderDisplayName.Trim();
                }

                if (onlineHost)
                {
                    if (message.type == "ready")
                    {
                        StartAuthoritativeOnlineGame();
                        return;
                    }

                    if (message.type == "heartbeat")
                    {
                        if (onlineGameStarted)
                            BroadcastBattleSnapshot();
                        return;
                    }

                    if (onlineGameStarted && !gameOverHandled && message.type == "action" && message.action != null &&
                        string.Equals(message.action.playerId, OpponentPlayerId, StringComparison.Ordinal))
                    {
                        if (!networkSession.TryAcceptGuestAction(message))
                            return;
                        if (message.action.turnIndex != gameController.TurnIndex)
                        {
                            BroadcastBattleSnapshot(null, null, true, "操作所属回合已结束，请重新选择。");
                            return;
                        }
                        gameController.SubmitPlayerAction(message.action, out var result, out var reason);
                        if (!string.IsNullOrEmpty(reason))
                            Debug.LogWarning($"[UOS] Guest action rejected: {reason}");
                        BroadcastBattleSnapshot(result, null, true, reason);
                    }

                    return;
                }

                if (message.type == "heartbeat")
                    return;
                if ((message.type != "start" && message.type != "snapshot") || message.snapshot == null)
                    return;
                if (!networkSession.TryAcceptSnapshot(message))
                    return;

                if (!onlineGameStarted)
                {
                    onlineGameStarted = true;
                    battleStartTime = Time.unscaledTime;
                    Debug.Log($"[UOS] 客户端收到开局同步：sequence={networkSession.SessionId}");
                }

                gameController.ApplySnapshot(message.snapshot);
                RefreshSynchronizedPlayers();
                if (message.actionResult != null)
                    OnActionResolved(message.actionResult);
                if (message.gameOver != null)
                    OnGameEnded(message.gameOver);
                if (message.actionResponse)
                {
                    if (string.IsNullOrEmpty(message.actionReason))
                        Debug.Log("[UOS] 客户端操作已由房主确认");
                    else
                    {
                        Debug.LogWarning($"[UOS] 客户端操作被房主拒绝：{message.actionReason}");
                        SetText(battlefieldHintText, message.actionReason);
                    }
                }
                RefreshCards();
                RefreshStaticUi();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UOS] Battle 消息解析失败：{exception.Message}");
            }
        }

        private void StartAuthoritativeOnlineGame()
        {
            if (!onlineHost || onlineGameStarted || gameController == null)
                return;

            networkSession.BeginHost(DateTime.UtcNow.Ticks);
            battleStartTime = Time.unscaledTime;
            gameController.StartGame();
            var message = new BattleNetworkMessageData
            {
                type = "start",
                snapshot = snapshotSanitizer.CreateForOpponent(gameController.CreateSnapshot(), LocalPlayerId)
            };
            networkSession.StampSnapshot(message);
            SendBattleMessage(message, false);
            onlineGameStarted = true;
            RefreshCards();
            RefreshStaticUi();
            Debug.Log($"[UOS] 双方已就绪，房主开始对局：sequence={networkSession.SessionId}");
        }

        private void RefreshSynchronizedPlayers()
        {
            if (gameController == null)
                return;
            gameController.Players.TryGetValue(LocalPlayerId, out localPlayer);
            gameController.Players.TryGetValue(OpponentPlayerId, out aiPlayer);
        }

        private bool SubmitBattleAction(PlayerActionMessageData action, out string reason)
        {
            reason = string.Empty;
            if (gameController == null || action == null)
            {
                reason = "战斗尚未准备完成";
                return false;
            }
            if (onlineBattle && !onlineGameStarted)
            {
                reason = "等待双方进入对局";
                return false;
            }
            if (onlineBattle && gameOverHandled)
            {
                reason = "对局已经结束";
                return false;
            }

            if (!onlineBattle || onlineHost)
            {
                var accepted = gameController.SubmitPlayerAction(action, out var result, out reason);
                if (accepted && onlineBattle)
                    BroadcastBattleSnapshot(result);
                return accepted;
            }

            if (action.actionType == ActionType.Synthesize || action.actionType == ActionType.Discard)
            {
                var predicted = gameController.SubmitPlayerAction(action, out _, out reason, true);
                if (!predicted)
                    return false;

                SendBattleMessage(new BattleNetworkMessageData
                {
                    type = "action",
                    action = action
                }, true);
                Debug.Log($"[UOS] 客户端预测操作已发送：action={action.actionType}, instance={action.cardInstanceId}");
                return true;
            }

            SendBattleMessage(new BattleNetworkMessageData
            {
                type = "action",
                action = action
            }, true);
            Debug.Log($"[UOS] 客户端操作已发送：action={action.actionType}, instance={action.cardInstanceId}");
            return true;
        }

        /// <summary>
        /// 点击空白区域时取消当前选择。
        /// </summary>
        private void ClearSelOnBlank()
        {
            if (!Input.GetMouseButtonDown(0) || EventSystem.current == null)
            {
                return;
            }

            var data = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            Rays.Clear();
            EventSystem.current.RaycastAll(data, Rays);
            for (var i = 0; i < Rays.Count; i++)
            {
                if (IsCardSlot(Rays[i].gameObject))
                {
                    return;
                }
            }

            selectedBaseSlot = -1;
            selectedSkillHandIndex = -1;
            selectedBaseInstanceId = null;
            selectedSkillInstanceId = null;
            dragBaseSlot = -1;
            ResetBaseDoubleClick();
            HideSkillExplain();
            RefreshCards();
        }

        /// <summary>
        /// 判断对象是否属于手牌槽或手牌视图。
        /// </summary>
        private static bool IsCardSlot(GameObject obj)
        {
            while (obj != null)
            {
                if (obj.name.StartsWith("BaseCardSlot", StringComparison.Ordinal) ||
                    obj.name.StartsWith("SkillCardSlot", StringComparison.Ordinal))
                {
                    return true;
                }

                obj = obj.transform.parent != null ? obj.transform.parent.gameObject : null;
            }

            return false;
        }

        /// <summary>
        /// 隐藏卡牌说明面板。
        /// </summary>
        private void HideSkillExplain()
        {
            explainPanel.SetActive(false);
        }

        /// <summary>
        /// 刷新阶段、倒计时、生命、防御和按钮状态。
        /// </summary>
        private void RefreshStaticUi()
        {
            if (gameController == null)
            {
                return;
            }

            RefreshOpponentName();
            SetText(phaseText, ResolvePhaseText(gameController.State));
            SetText(timerText, Mathf.CeilToInt(gameController.StateSecondsLeft).ToString());
            if (tutorialActive && tutorialStep == TutorialStep.ObserveShieldAndTimer)
            {
                SetText(tutorialBody,
                    $"你已经体验了实时战斗；本阶段剩余 {Mathf.CeilToInt(gameController.StateSecondsLeft)} 秒。在本教程点击右上角按钮可以跳过战斗阶段。");
            }
            if (preparationButtonLabel != null)
                preparationButtonLabel.color = gameController.State == GameState.Preparation ? Color.white : Color.gray;
            if (localPlayer != null)
            {
                SetVitalsText("PlayerHpText", "PlayerShieldText", "我方", localPlayer);
            }

            if (aiPlayer != null)
            {
                SetVitalsText("OpponentHpText", "OpponentShieldText", "对手", aiPlayer);
                SetVitalsText("EnemyHpText", "EnemyShieldText", "对手", aiPlayer);
            }

            var canClickEndButton = gameController.State == GameState.Preparation;
            preparation.GetComponent<Button>().interactable = canClickEndButton;
            var endButtonText = endBattleText();
            if (preparationButtonLabel != null)
                preparationButtonLabel.text = endButtonText;
        }

        private void RefreshOpponentName()
        {
            if (!onlineBattle)
            {
                SetText("OpponentNameText", bossBattle ? $"Boss{bossIndex + 1}" : "普通人机");
                return;
            }

            SetText("OpponentNameText", opponentDisplayName);
        }

        private string ResolveOpponentDisplayName()
        {
            var room = onlineService != null ? onlineService.CurrentRoom : null;
            if (room?.Players != null)
            {
                for (var index = 0; index < room.Players.Count; index++)
                {
                    var player = room.Players[index];
                    if (player == null ||
                        (player.SenderId != opponentSenderId &&
                         !string.Equals(player.Id, OpponentPlayerId, StringComparison.Ordinal)))
                        continue;

                    if (!string.IsNullOrWhiteSpace(player.Name))
                    {
                        var candidate = player.Name.Trim();
                        if (!string.Equals(candidate, player.Id, StringComparison.Ordinal) &&
                            !string.Equals(candidate, OpponentPlayerId, StringComparison.Ordinal))
                            return candidate;
                    }
                    return "玩家";
                }
            }

            return "玩家";
        }

        private string endBattleText()
        {
            if (gameController == null)
            {
                return "战斗";
            }

            if (gameController.State == GameState.Preparation)
            {
                return "准备";
            }

            if (gameController.State == GameState.Battle)
            {
                return "战斗进行中";
            }

            return "战斗";
        }

        /// <summary>
        /// 对外刷新入口，供卡牌拖拽组件在拖拽结束后调用。
        /// </summary>
        public void RefreshCards()
        {
            RefreshHandArea(localPlayer, true);
            RefreshHandArea(aiPlayer, false);
            // shield 是防御属性；只有 absorbShield 或一次性 HolyShield 才显示护盾特效。
            UpdateShieldFx(true, localPlayer);
            UpdateShieldFx(false, aiPlayer);
            SwapHandIds();
            Canvas.ForceUpdateCanvases();
        }

        private static bool HasShieldBuff(PlayerCombatStateData player)
        {
            if (player == null || player.buffs == null)
            {
                return false;
            }

            if (player.absorbShield > 0f)
            {
                return true;
            }

            for (var i = 0; i < player.buffs.Count; i++)
            {
                var buff = player.buffs[i];
                if (buff == null)
                {
                    continue;
                }

                if (buff.kind == CombatBuffKind.HolyShield)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasHolyShield(PlayerCombatStateData player)
        {
            if (player?.buffs == null)
                return false;

            for (var index = 0; index < player.buffs.Count; index++)
            {
                var buff = player.buffs[index];
                if (buff != null && buff.kind == CombatBuffKind.HolyShield)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 刷新指定玩家的基础牌和强化牌显示。
        /// </summary>
        private void RefreshHandArea(PlayerCombatStateData player, bool mine)
        {
            if (player == null || player.deck == null)
            {
                return;
            }

            var baseSlot = 0;
            var skillSlot = 0;
            var baseCapacity = SlotCapacity(false, mine);
            var skillCapacity = SlotCapacity(true, mine);
            if (mine)
            {
                ReconcileSelectedCards(player);
                Array.Fill(playerBaseSlotHandIndices, -1);
                Array.Fill(playerSkillSlotHandIndices, -1);
            }

            for (var i = 0; i < player.deck.hand.Count; i++)
            {
                var inst = player.deck.hand[i];
                if (!cardManager.TryGetCard(inst.cardId, out var card))
                {
                    continue;
                }

                if (card.Category == CardCategory.Skill)
                {
                    if (skillSlot < skillCapacity)
                    {
                        var name = mine ? $"SkillCardSlot{skillSlot + 1}" : $"OpponentSkillCardSlot{skillSlot + 1}";
                        SetSlot(mine ? skillCardSlots[skillSlot] : opponentSkillCardSlots[skillSlot], name, card, inst, mine,
                            mine && string.Equals(selectedSkillInstanceId, inst.instanceId, StringComparison.Ordinal), false);
                        if (mine)
                        {
                            playerSkillSlotHandIndices[skillSlot] = i;
                        }
                    }

                    skillSlot++;
                }
                else
                {
                    if (baseSlot < baseCapacity)
                    {
                        var name = mine ? $"BaseCardSlot{baseSlot + 1}" : $"OpponentBaseCardSlot{baseSlot + 1}";
                        SetSlot(mine ? baseCardSlots[baseSlot] : opponentBaseCardSlots[baseSlot], name, card, inst, mine,
                            mine && string.Equals(selectedBaseInstanceId, inst.instanceId, StringComparison.Ordinal), mine && dragBaseSlot == baseSlot);
                        if (mine)
                        {
                            playerBaseSlotHandIndices[baseSlot] = i;
                        }
                    }

                    baseSlot++;
                }
            }
            ClearUnusedSlots(mine, Mathf.Min(baseSlot, baseCapacity), Mathf.Min(skillSlot, skillCapacity));
            LayoutHandSlots(mine, false, Mathf.Min(baseSlot, baseCapacity));
            LayoutHandSlots(mine, true, Mathf.Min(skillSlot, skillCapacity));
        }

        private void ReconcileSelectedCards(PlayerCombatStateData player)
        {
            var resolvedBaseSlot = -1;
            var resolvedSkillHandIndex = -1;
            var baseSlot = 0;
            for (var handIndex = 0; handIndex < player.deck.hand.Count; handIndex++)
            {
                var instance = player.deck.hand[handIndex];
                if (instance == null || !cardManager.TryGetCard(instance.cardId, out var card))
                    continue;

                if (card.Category == CardCategory.Skill)
                {
                    if (string.Equals(selectedSkillInstanceId, instance.instanceId, StringComparison.Ordinal))
                        resolvedSkillHandIndex = handIndex;
                }
                else
                {
                    if (string.Equals(selectedBaseInstanceId, instance.instanceId, StringComparison.Ordinal))
                        resolvedBaseSlot = baseSlot;
                    baseSlot++;
                }
            }

            selectedBaseSlot = resolvedBaseSlot;
            selectedSkillHandIndex = resolvedSkillHandIndex;
            if (resolvedBaseSlot < 0)
                selectedBaseInstanceId = null;
            if (resolvedSkillHandIndex < 0)
                selectedSkillInstanceId = null;
        }

        
        /// <summary>
        /// 清理没有对应手牌的槽位。
        /// </summary>
        private void ClearUnusedSlots(bool mine, int baseUsed, int skillUsed)
        {
            var baseCapacity = SlotCapacity(false, mine);
            var skillCapacity = SlotCapacity(true, mine);
            for (var i = Mathf.Clamp(baseUsed, 0, baseCapacity); i < baseCapacity; i++)
            {
                SetSlot(mine ? baseCardSlots[i] : opponentBaseCardSlots[i], mine ? $"BaseCardSlot{i + 1}" : $"OpponentBaseCardSlot{i + 1}", null, null, mine, false, false);
            }

            for (var i = Mathf.Clamp(skillUsed, 0, skillCapacity); i < skillCapacity; i++)
            {
                SetSlot(mine ? skillCardSlots[i] : opponentSkillCardSlots[i], mine ? $"SkillCardSlot{i + 1}" : $"OpponentSkillCardSlot{i + 1}", null, null, mine, false, false);
            }
        }

        private int SlotCapacity(bool skill, bool mine)
        {
            if (skill)
            {
                return Mathf.Min(SkillSlotCount, mine ? skillCardSlots?.Length ?? 0 : opponentSkillCardSlots?.Length ?? 0);
            }

            return Mathf.Min(BaseSlotCount, mine ? baseCardSlots?.Length ?? 0 : opponentBaseCardSlots?.Length ?? 0);
        }

        /// <summary>
        /// 把单张卡牌数据显示到指定槽位。
        /// </summary>
        private void SetSlot(GameObject slot, string slotName, CardBase card, CardInstanceData inst, bool mine, bool selected, bool dragSelected)
        {
            slot.SetActive(true);
            if (card == null)
            {
                slotDealIds.Remove(slotName);
                SetSlotButtonGraphic(slot, null);
                SetSlotVisible(slot, false);
                ReleaseSlotView(slotName);
                return;
            }

            slotDealIds[slotName] = inst != null ? inst.instanceId : string.Empty;

            var viewObj = EnsureSlotView(slotName, slot.transform, card);
            SetSlotButtonGraphic(slot, viewObj);
            if (mine)
            {
                SetPlayerFront(viewObj, card);
                RefreshEnhancementSlots(viewObj, card, inst);
            }
            else
            {
                SetEnemyBack(viewObj != null ? viewObj : slot, card);
            }

            var labels = slot.GetComponentsInChildren<Text>(true);
            for (var i = 0; i < labels.Length; i++)
            {
                labels[i].text = mine && card.Category == CardCategory.Skill ? card.DisplayName : string.Empty;
                labels[i].color = Color.black;
                labels[i].enabled = mine && card.Category == CardCategory.Skill;
            }

            var view = slot.GetComponent<BattleCardView>();
            if (view != null)
            {
                view.Configure(((RectTransform)slot.transform).anchoredPosition, 0f, selected, dragSelected, true, !mine, card.Category == CardCategory.Skill);
            }

            var isPendingDeal = inst != null && pendingDealIds.Contains(inst.instanceId);
            var queuedDeal = TryDealFx(slotName, slot, card, inst, mine);
            if (mine && (isPendingDeal || queuedDeal))
            {
                // 发牌动画期间隐藏强化槽，发牌完成回调会由 RefreshEnhancementSlots 恢复。
                HideEnhancementSlots(viewObj);
            }
            SetSlotVisible(slot, !isPendingDeal && !queuedDeal);
            if (mine && !isPendingDeal && !queuedDeal)
            {
                // 发牌显隐只控制整张卡牌，不能让 Slot1/Slot2 的强化图标状态丢失。
                RefreshEnhancementSlots(viewObj, card, inst);
            }
        }

        /// <summary>
        /// 控制槽位内部可视组件显隐，保留槽位对象本身用于点击和布局。
        /// </summary>
        private static void SetSlotVisible(GameObject slot, bool visible)
        {
            var graphics = slot.GetComponentsInChildren<Graphic>(true);
            for (var i = 0; i < graphics.Length; i++)
            {
                if (graphics[i].gameObject == slot)
                {
                    continue;
                }

                // Slot1/Slot2 的显示由 RefreshEnhancementSlots 独立管理，不能被发牌显隐覆盖。
                if (IsEnhancementSlotGraphic(graphics[i].transform))
                {
                    continue;
                }

                graphics[i].enabled = visible;
            }
        }

        private static bool IsEnhancementSlotGraphic(Transform current)
        {
            while (current != null)
            {
                if (string.Equals(current.name, "Slot1", StringComparison.Ordinal) ||
                    string.Equals(current.name, "Slot2", StringComparison.Ordinal))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static void SetSlotButtonGraphic(GameObject slot, GameObject viewObj)
        {
            if (slot == null || !slot.TryGetComponent<Button>(out var button))
            {
                return;
            }

            Graphic graphic = null;
            if (viewObj != null)
            {
                graphic = CardBodyImage(viewObj);
                if (graphic == null)
                {
                    graphic = viewObj.GetComponentInChildren<Graphic>(true);
                }
            }

            if (graphic != null)
            {
                graphic.enabled = true;
                graphic.raycastTarget = true;
            }

            button.targetGraphic = graphic;
        }

        /// <summary>
        /// 设置槽位内主要图片颜色，并保留已有 sprite 不被运行时代码清空。
        /// </summary>
        private static void SetSlotImageColor(GameObject slot, Color color)
        {
            var images = slot.GetComponentsInChildren<Image>(true);
            for (var i = 0; i < images.Length; i++)
            {
                var image = images[i];
                if (image == null || image.name.StartsWith("Tag_", StringComparison.Ordinal))
                {
                    continue;
                }

                image.enabled = true;
                image.color = color;
                return;
            }
        }

        /// <summary>
        /// 敌方手牌只显示背面，不暴露卡牌正面内容。
        /// </summary>
        private void SetEnemyBack(GameObject view, CardBase card)
        {
            if (view == null)
            {
                return;
            }

            // 对手牌不显示基础牌预制体中的强化槽，避免通过槽位暴露对手信息。
            HideEnhancementSlots(view);

            var image = CardBodyImage(view);
            if (image == null)
            {
                return;
            }

            // 卡牌视图会在我方正面、敌方背面之间复用。清理所有可能由特效或
            // 上一次显示状态留下的材质和透明度，避免敌方背面被渲染成白板。
            image.material = null;
            image.canvasRenderer.SetColor(Color.white);

            var back = card is HandCardBase
                ? cardArtConfig != null ? cardArtConfig.BaseBack() : null
                : cardArtConfig != null ? cardArtConfig.Back(card != null ? card.Quality : CardQuality.Silver) : null;
            if (back != null)
            {
                image.sprite = back;
                image.preserveAspect = true;
                image.color = Color.white;
                image.enabled = true;
            }
            else
            {
                // 没有背面资源时也不能保留预制体默认正面图。
                image.sprite = null;
                image.enabled = false;
            }

        }

        private static void HideEnhancementSlots(GameObject view)
        {
            if (view == null)
            {
                return;
            }

            for (var i = 1; i <= 2; i++)
            {
                var slot = view.transform.Find($"Slot{i}");
                if (slot != null)
                {
                    slot.gameObject.SetActive(false);
                }
            }
        }

        private void SetPlayerFront(GameObject view, CardBase card)
        {
            if (view == null || card == null)
            {
                return;
            }

            var image = CardBodyImage(view);
            if (image == null)
            {
                return;
            }

            var front = CardPrefabSprite(card);
            if (front != null)
            {
                image.material = null;
                image.sprite = front;
                image.preserveAspect = true;
            }

            image.color = Color.white;
        }

        private static Image CardBodyImage(GameObject view)
        {
            if (view == null)
            {
                return null;
            }

            // 卡牌主体 Image 位于预制体根节点。优先取根节点，避免把动态 ShieldFx 当成卡牌图片。
            var rootImage = view.GetComponent<Image>();
            if (rootImage != null)
            {
                return rootImage;
            }

            var images = view.GetComponentsInChildren<Image>(true);
            for (var i = 0; i < images.Length; i++)
            {
                if (images[i] != null &&
                    !string.Equals(images[i].gameObject.name, "ShieldFx", StringComparison.Ordinal) &&
                    !string.Equals(images[i].gameObject.name, "sheild", StringComparison.Ordinal))
                {
                    return images[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 确保指定槽位中挂载了正确类型的卡牌预制体实例。
        /// </summary>
        private GameObject EnsureSlotView(string slotName, Transform parent, CardBase card)
        {
            var prefab = CardPrefab(card);
            var key = CardPrefabKey(card);
            if (prefab == null)
            {
                return null;
            }

            if (slotViews.TryGetValue(slotName, out var current) &&
                current != null &&
                slotViewKeys.TryGetValue(slotName, out var currentKey) &&
                string.Equals(currentKey, key, StringComparison.Ordinal))
            {
                current.SetActive(true);
                current.transform.SetParent(parent, false);
                current.transform.SetAsFirstSibling();
                SetSlotViewGraphicsVisible(current, true);
                return current;
            }

            ReleaseSlotView(slotName);
            var obj = TakeCardView(key, prefab, parent);
            obj.name = $"{slotName}_Card";
            obj.transform.SetParent(parent, false);
            obj.transform.SetAsFirstSibling();
            obj.SetActive(true);
            SetSlotViewGraphicsVisible(obj, true);

            var rt = obj.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.anchoredPosition = Vector2.zero;
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;
            }

            slotViews[slotName] = obj;
            slotViewKeys[slotName] = key;
            return obj;
        }

        private static void SetSlotViewGraphicsVisible(GameObject viewObj, bool visible)
        {
            if (viewObj == null)
            {
                return;
            }

            var graphics = viewObj.GetComponentsInChildren<Graphic>(true);
            for (var i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] != null)
                {
                    graphics[i].enabled = visible;
                }
            }
        }

        private void RefreshEnhancementSlots(GameObject baseCardView, CardBase card, CardInstanceData instance)
        {
            if (baseCardView == null || !(card is HandCardBase))
            {
                return;
            }

            var attached = instance != null ? instance.attachedSkillCards : null;
            for (var i = 0; i < 2; i++)
            {
                var slot = baseCardView.transform.Find($"Slot{i + 1}");
                if (slot == null)
                {
                    continue;
                }

                // 强化槽只由当前挂载数量决定。没有对应强化卡时，先隐藏并清理旧图片。
                slot.gameObject.SetActive(false);
                var image = slot.GetComponent<Image>();
                var rawImage = slot.GetComponent<RawImage>();
                if (image != null)
                {
                    image.sprite = null;
                    image.color = SetAlpha(image.color, 0f);
                }
                if (rawImage != null)
                {
                    rawImage.texture = null;
                    rawImage.color = SetAlpha(rawImage.color, 0f);
                }

                if (instance == null || attached == null || i >= attached.Count)
                {
                    continue;
                }

                var skill = attached[i];
                if (skill == null)
                {
                    continue;
                }

                // 合成后的实例已经保存了强化品质。UI 不应再依赖卡牌定义查找，
                // 否则定义尚未热更新完成或 cardId 不在当前目录时，合法强化会被静默隐藏。
                enhancementTagSprites.TryGetValue(skill.quality, out var sprite);
                if (sprite == null)
                {
                    Debug.LogWarning($"[BattleUI] 强化槽 Tag 资源未加载: instance={instance.instanceId}, " +
                                     $"slot={i + 1}, quality={skill.quality}, cardId={skill.cardId}");
                    continue;
                }

                slot.gameObject.SetActive(true);
                if (image != null)
                {
                    image.sprite = sprite;
                    image.preserveAspect = true;
                    image.color = SetAlpha(image.color, 1f);
                    image.enabled = true;
                }
                else if (rawImage != null && sprite.texture != null)
                {
                    rawImage.texture = sprite.texture;
                    // Tag_* 是独立图片资源，完整纹理才是稳定的显示范围；
                    // 使用 Sprite.rect 会在导入设置变化时把图标裁成空白区域。
                    rawImage.uvRect = new Rect(0f, 0f, 1f, 1f);
                    rawImage.color = SetAlpha(rawImage.color, 1f);
                    rawImage.enabled = true;
                }
            }
        }

        private static Color SetAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private void UpdateShieldFx(bool mine, PlayerCombatStateData player)
        {
            var owner = SceneObject(mine ? "Player" : "Opponent");
            if (owner == null)
            {
                return;
            }

            var fx = mine ? playerShieldFx : opponentShieldFx;
            if (fx == null)
            {
                fx = ShieldFx.Create(owner.transform, shieldFxShader ?? battleFxShader);
                if (mine)
                {
                    playerShieldFx = fx;
                }
                else
                {
                    opponentShieldFx = fx;
                }
            }

            if (fx == null)
            {
                return;
            }

            if (HasShieldBuff(player))
            {
                fx.Show(HasHolyShield(player));
            }
            else
            {
                fx.Hide();
            }
        }

        private void PlayShieldBreak(string targetId)
        {
            var mine = string.Equals(targetId, LocalPlayerId, StringComparison.Ordinal);
            var fx = mine ? playerShieldFx : opponentShieldFx;
            if (fx == null)
            {
                return;
            }

            fx.Break();
        }

        /// <summary>
        /// 回收指定槽位中的卡牌预制体实例。
        /// </summary>
        private void ReleaseSlotView(string slotName)
        {
            if (!slotViews.TryGetValue(slotName, out var obj) || obj == null)
            {
                slotViews.Remove(slotName);
                slotViewKeys.Remove(slotName);
                return;
            }

            var key = slotViewKeys.TryGetValue(slotName, out var savedKey) ? savedKey : obj.name;
            obj.SetActive(false);
            obj.transform.SetParent(CardPoolRoot(), false);
            if (!cardViewPool.TryGetValue(key, out var stack))
            {
                stack = new Stack<GameObject>();
                cardViewPool[key] = stack;
            }

            stack.Push(obj);
            slotViews.Remove(slotName);
            slotViewKeys.Remove(slotName);
        }

        /// <summary>
        /// 从对象池取出卡牌视图，池为空时实例化对应预制体。
        /// </summary>
        private GameObject TakeCardView(string key, GameObject prefab, Transform parent)
        {
            if (cardViewPool.TryGetValue(key, out var stack))
            {
                while (stack.Count > 0)
                {
                    var obj = stack.Pop();
                    if (obj != null)
                    {
                        obj.transform.SetParent(parent, false);
                        return obj;
                    }
                }
            }

            return Instantiate(prefab, parent, false);
        }

        /// <summary>
        /// 返回手牌视图对象池根节点。
        /// </summary>
        private Transform CardPoolRoot()
        {
            return cardPoolRoot != null ? cardPoolRoot.transform : baseHandPanel.transform;
        }

        
        /// <summary>
        /// 按杀戮尖塔风格重排手牌槽位。
        /// </summary>
        private void LayoutHandSlots(bool mine, bool skill, int count)
        {
            var prefix = mine
                ? skill ? "SkillCardSlot" : "BaseCardSlot"
                : skill ? "OpponentSkillCardSlot" : "OpponentBaseCardSlot";
            var max = SlotCapacity(skill, mine);
            var width = mine
                ? skill ? 96f : 125f
                : skill ? 46f : 58f;
            var arc = mine
                ? skill ? 15f : 24f
                : skill ? -9f : -15f;
            var baseY = mine ? skill ? 34f : 0f : skill ? -18f : 0f;
            var active = Mathf.Max(1, count);

            for (var i = 0; i < max; i++)
            {
                var slot = CardSlot(mine, skill, i);
                if (i >= count)
                {
                    continue;
                }

                var rt = slot.GetComponent<RectTransform>();
                if (rt == null)
                {
                    continue;
                }

                var siblingIndex = Mathf.Min(i, Mathf.Max(0, slot.transform.parent.childCount - 1));
                slot.transform.SetSiblingIndex(siblingIndex);

                var mid = (active - 1) * 0.5f;
                var t = active <= 1 ? 0f : (i - mid) / Mathf.Max(0.01f, mid);
                var fan = Mathf.Clamp01((active - 1f) / (skill ? 8f : 7f));
                var curve = Mathf.Abs(t) * (mine ? skill ? 6f : 18f : 7f) * fan;
                var target = new Vector2((i - mid) * width, baseY - curve);
                var rot = -t * arc * fan;
                var view = slot.GetComponent<BattleCardView>();
                if (view != null)
                {
                    view.SetBaseLayout(target, rot);
                }

                var lifted = mine && (skill
                    ? playerSkillSlotHandIndices[i] == selectedSkillHandIndex
                    : selectedBaseSlot == i || dragBaseSlot == i);
                if (lifted)
                {
                    slot.transform.SetAsLastSibling();
                    continue;
                }

                if (view == null)
                {
                    rt.anchoredPosition = target;
                    rt.localRotation = Quaternion.Euler(0f, 0f, rot);
                    rt.localScale = Vector3.one;
                }
            }
        }
        
        /// <summary>
        /// 获得卡牌槽位名
        /// </summary>
        private GameObject CardSlot(bool mine, bool skill, int index)
        {
            return mine
                ? skill ? skillCardSlots[index] : baseCardSlots[index]
                : skill ? opponentSkillCardSlots[index] : opponentBaseCardSlots[index];
        }

        /// <summary>
        /// 新手牌进入槽位时播放发牌动画。
        /// </summary>
        private bool TryDealFx(string slotName, GameObject slot, CardBase card, CardInstanceData inst, bool mine)
        {
            if (!dealFxReady || cardMaven == null || inst == null || slot == null)
            {
                return false;
            }

            var idSet = mine ? myHandIds : enemyHandIds;
            if (idSet.Contains(inst.instanceId))
            {
                return false;
            }

            if (pendingDealIds.Contains(inst.instanceId))
            {
                return true;
            }

            var rt = slot.GetComponent<RectTransform>();
            if (rt == null)
            {
                return false;
            }

            var sprite = mine ? CardPrefabSprite(card) : null;
            Color color = mine && card is HandCardBase hand ? BaseColor(hand.HandCardType) : (Color)new Color32(0x24, 0x24, 0x29, 0xFF);
            pendingDealIds.Add(inst.instanceId);
            cardMaven.Deal(rt, mine ? card.DisplayName : string.Empty, color, rt.rect.size, sprite, () =>
            {
                pendingDealIds.Remove(inst.instanceId);
                aiActionTimer = Mathf.Max(aiActionTimer, 0.5f);
                if (slot == null ||
                    !slotDealIds.TryGetValue(slotName, out var currentId) ||
                    !string.Equals(currentId, inst.instanceId, StringComparison.Ordinal) ||
                    !slotViews.TryGetValue(slotName, out var currentView) ||
                    currentView == null)
                {
                    return;
                }

                SetSlotVisible(slot, true);
                if (mine)
                {
                    RefreshEnhancementSlots(currentView, card, inst);
                }
                SetSlotButtonGraphic(slot, currentView);
                Canvas.ForceUpdateCanvases();
            });
            return true;
        }

        /// <summary>
        /// 在一帧刷新结束后记录当前手牌 id，用于下一轮识别新发牌。
        /// </summary>
        private void SwapHandIds()
        {
            myHandIds.Clear();
            enemyHandIds.Clear();
            CopyHandIds(localPlayer, myHandIds);
            CopyHandIds(aiPlayer, enemyHandIds);
        }

        /// <summary>
        /// 拷贝指定玩家当前手牌实例 id。
        /// </summary>
        private static void CopyHandIds(PlayerCombatStateData player, HashSet<string> target)
        {
            if (player?.deck?.hand == null)
            {
                return;
            }

            for (var i = 0; i < player.deck.hand.Count; i++)
            {
                if (!string.IsNullOrEmpty(player.deck.hand[i].instanceId))
                {
                    target.Add(player.deck.hand[i].instanceId);
                }
            }
        }

        /// <summary>
        /// 处理基础牌槽位点击，弃牌阶段弃牌，否则双击打出。
        /// </summary>
        private void OnBaseSlotClicked(int slot)
        {
            if (!TutorialInputEnabled())
                return;
            if (slot < 0 || slot >= playerBaseSlotHandIndices.Length)
            {
                return;
            }

            var handIndex = playerBaseSlotHandIndices[slot];
            if (handIndex < 0)
            {
                return;
            }

            if (tutorialActive && !TutorialAllowsBaseCard(handIndex))
                return;

            if (discardMode)
            {
                var discardInstanceId = localPlayer.deck.hand[handIndex]?.instanceId;
                var discardDoubleClick = lastBaseClickSlot == slot &&
                                         string.Equals(lastBaseClickInstanceId, discardInstanceId, StringComparison.Ordinal) &&
                                         Time.unscaledTime - lastBaseClickTime <= BasePlayDoubleClickSeconds;
                if (discardDoubleClick)
                {
                    SubmitDiscardAction(handIndex);
                    ResetBaseDoubleClick();
                }
                else
                {
                    lastBaseClickSlot = slot;
                    lastBaseClickTime = Time.unscaledTime;
                    lastBaseClickInstanceId = discardInstanceId;
                    ShowHandCardExplain(handIndex);
                    RefreshCards();
                }
                return;
            }
            GameAudioService.Instance?.PlaySfx(GameSfx.Card);
            var instanceId = localPlayer?.deck?.hand != null && handIndex < localPlayer.deck.hand.Count
                ? localPlayer.deck.hand[handIndex]?.instanceId
                : null;
            var isDoubleClick = selectedBaseSlot == slot &&
                                lastBaseClickSlot == slot &&
                                string.Equals(lastBaseClickInstanceId, instanceId, StringComparison.Ordinal) &&
                                Time.unscaledTime - lastBaseClickTime <= BasePlayDoubleClickSeconds;
            if (isDoubleClick)
            {
                ResetBaseDoubleClick();
                SubmitLocalAction(handIndex);
                return;
            }

            if (selectedBaseSlot != slot)
            {
                selectedBaseSlot = slot;
                selectedSkillHandIndex = -1;
            }
            selectedBaseInstanceId = instanceId;
            selectedSkillInstanceId = null;

            lastBaseClickSlot = slot;
            lastBaseClickTime = Time.unscaledTime;
            lastBaseClickInstanceId = instanceId;
            RefreshCards();
            ShowHandCardExplain(handIndex);
            if (tutorialActive && tutorialStep == TutorialStep.InspectGrownAttack)
                FinishTutorial();
        }

        private void ResetBaseDoubleClick()
        {
            lastBaseClickSlot = -1;
            lastBaseClickTime = -1f;
            lastBaseClickInstanceId = null;
        }

        /// <summary>
        /// 处理强化牌槽位点击，负责选中和显示说明。
        /// </summary>
        private void OnSkillSlotClicked(int slot)
        {
            if (!TutorialInputEnabled())
                return;
            if (slot < 0 || slot >= playerSkillSlotHandIndices.Length)
            {
                return;
            }
            GameAudioService.Instance?.PlaySfx(GameSfx.Card);
            var handIndex = playerSkillSlotHandIndices[slot];
            if (handIndex < 0)
            {
                return;
            }

            if (tutorialActive && !TutorialAllowsSkillCard(handIndex))
                return;

            if (discardMode)
            {
                var instanceId = localPlayer.deck.hand[handIndex]?.instanceId;
                var isDoubleClick = lastSkillClickSlot == slot &&
                                    string.Equals(lastSkillClickInstanceId, instanceId, StringComparison.Ordinal) &&
                                    Time.unscaledTime - lastSkillClickTime <= BasePlayDoubleClickSeconds;
                if (isDoubleClick)
                {
                    SubmitDiscardAction(handIndex);
                    ResetSkillDoubleClick();
                }
                else
                {
                    lastSkillClickSlot = slot;
                    lastSkillClickTime = Time.unscaledTime;
                    lastSkillClickInstanceId = instanceId;
                    ShowHandCardExplain(handIndex);
                    RefreshCards();
                }
                return;
            }

            selectedSkillHandIndex = handIndex;
            selectedBaseSlot = -1;
            selectedSkillInstanceId = localPlayer.deck.hand[handIndex]?.instanceId;
            selectedBaseInstanceId = null;
            ResetBaseDoubleClick();
            RefreshCards();
            ShowHandCardExplain(handIndex);
            if (tutorialActive && tutorialStep == TutorialStep.SelectShieldSkill)
            {
                tutorialStep = TutorialStep.AttachShieldSkill;
                ShowTutorialStep();
            }
            else if (tutorialActive && tutorialStep == TutorialStep.SelectSecondDefenseSkill)
            {
                tutorialStep = TutorialStep.AttachSecondDefenseSkill;
                ShowTutorialStep();
            }
        }

        private IEnumerator LoadSubAsset<T>(string key, string subAssetName, Action<T> apply)
            where T : UnityEngine.Object
        {
            if (!GameApp.TryGet<IGameAssetService>(out var assets))
                yield break;
            Task<T> task;
            try
            {
                task = assets.LoadSubAssetAsync<T>(key, subAssetName, CancellationToken.None);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"图集子资源加载请求失败：{key}/{subAssetName}，{exception.Message}");
                yield break;
            }
            while (!task.IsCompleted)
                yield return null;
            if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
                apply?.Invoke(task.Result);
            else
                Debug.LogWarning($"图集子资源加载失败：{key}/{subAssetName}，{task.Exception?.GetBaseException().Message}");
        }

        private void ResetSkillDoubleClick()
        {
            lastSkillClickSlot = -1;
            lastSkillClickTime = -1f;
            lastSkillClickInstanceId = null;
        }

        private void SubmitDiscardAction(int handIndex)
        {
            if (gameController == null || localPlayer == null)
                return;

            var instance = localPlayer.deck?.hand != null && handIndex >= 0 && handIndex < localPlayer.deck.hand.Count
                ? localPlayer.deck.hand[handIndex]
                : null;

            if (!SubmitBattleAction(new PlayerActionMessageData
                {
                    actionType = ActionType.Discard,
                    playerId = LocalPlayerId,
                    handIndex = handIndex,
                    cardId = instance?.cardId,
                    cardInstanceId = instance?.instanceId
                }, out var reason))
            {
                SetText(battlefieldHintText, string.IsNullOrEmpty(reason) ? "弃牌失败" : reason);
                return;
            }

            SetText(battlefieldHintText, "已弃掉所选卡牌");
            RefreshCards();
            RefreshStaticUi();
        }

        private void ShowHandCardExplain(int handIndex)
        {
            if (localPlayer?.deck?.hand == null ||
                handIndex < 0 ||
                handIndex >= localPlayer.deck.hand.Count ||
                cardManager == null)
            {
                HideSkillExplain();
                return;
            }

            var instance = localPlayer.deck.hand[handIndex];
            if (instance == null || !cardManager.TryGetCard(instance.cardId, out var card) || card == null)
            {
                HideSkillExplain();
                return;
            }

            explainPanel.SetActive(true);
            SetText("Name", card.DisplayName);
            SetText("Explain", BuildCardExplain(card, instance));
        }

        private string BuildCardExplain(CardBase card, CardInstanceData instance)
        {
            if (card is HandCardBase hand)
            {
                var currentBasePower = instance != null && instance.currentBasePower > 0
                    ? instance.currentBasePower
                    : hand.BasePower;
                var attachedCount = instance?.attachedSkillCards != null ? instance.attachedSkillCards.Count : 0;
                var slotMax = cardManager != null ? cardManager.EnhSlotMax : 1;
                var lines = new List<string>
                {
                    $"类型：{BaseTypeName(hand.HandCardType)}",
                    $"基础数值：{FormatNumber(currentBasePower)}",
                    "成长数值：1",
                    $"强化槽：{attachedCount}/{slotMax}"
                };

                if (attachedCount > 0)
                {
                    lines.Add("已强化：");
                    for (var i = 0; i < instance.attachedSkillCards.Count; i++)
                    {
                        var skillInstance = instance.attachedSkillCards[i];
                        if (skillInstance != null &&
                            cardManager != null &&
                            cardManager.TryGetCard(skillInstance.cardId, out var attachedCard) &&
                            attachedCard is SkillCardBase attachedSkill)
                        {
                            lines.Add($"- {attachedSkill.DisplayName} +{FormatNumber(attachedSkill.SkillPower)}");
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(card.Description))
                {
                    lines.Add(string.Empty);
                    lines.Add(card.Description);
                }

                return string.Join("\n", lines);
            }

            if (card is SkillCardBase skill)
            {
                var pre = skill.CanUseOnPre ? "备战期可用" : "仅战斗期";
                var lines = new List<string>
                {
                    $"品级：{QualityName(skill.Quality)}",
                    $"目标：{BaseTypeName(skill.SkillCardType)}基础牌",
                    $"强化数值：{FormatNumber(skill.SkillPower)}",
                    $"阶段：{pre}",
                    $"效果：{EffectNames(skill)}"
                };

                if (!string.IsNullOrWhiteSpace(card.Description))
                {
                    lines.Add(string.Empty);
                    lines.Add(card.Description);
                }

                return string.Join("\n", lines);
            }

            return card.Description;
        }

        /// <summary>
        /// 记录开始拖拽的强化牌索引。
        /// </summary>
        public void BeginSkillDrag(int skillSlot)
        {
            if (skillSlot >= 0 && skillSlot < playerSkillSlotHandIndices.Length)
            {
                selectedSkillHandIndex = playerSkillSlotHandIndices[skillSlot];
                selectedSkillInstanceId = selectedSkillHandIndex >= 0 && selectedSkillHandIndex < localPlayer.deck.hand.Count
                    ? localPlayer.deck.hand[selectedSkillHandIndex]?.instanceId
                    : null;
            }
        }

        /// <summary>
        /// 结束强化牌拖拽并清理悬停状态。
        /// </summary>
        public void EndSkillDrag()
        {
            dragBaseSlot = -1;
            RefreshCards();
        }

        /// <summary>
        /// 拖拽强化牌经过基础牌时标记基础牌强化选中状态。
        /// </summary>
        public void HoverBaseDuringDrag(int baseSlot)
        {
            dragBaseSlot = baseSlot;
            RefreshCards();
        }

        public int FindPlayerBaseSlotAtScreenPosition(Vector2 screenPosition, Camera eventCamera)
        {
            for (var i = 0; i < playerBaseSlotHandIndices.Length; i++)
            {
                if (playerBaseSlotHandIndices[i] < 0)
                {
                    continue;
                }

                if (i >= baseCardSlots.Length)
                {
                    continue;
                }

                var slot = baseCardSlots[i];
                if (!slot.activeInHierarchy)
                {
                    continue;
                }

                var rect = slot.GetComponent<RectTransform>();
                if (rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPosition, eventCamera))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 把当前拖拽的强化牌合成到目标基础牌上。
        /// </summary>
        public bool DropSkillCardOnBase(int baseSlot)
        {
            if (!TutorialInputEnabled())
                return false;
            if (selectedSkillHandIndex < 0 || baseSlot < 0 || baseSlot >= playerBaseSlotHandIndices.Length)
            {
                return false;
            }

            var baseHandIndex = playerBaseSlotHandIndices[baseSlot];
            if (baseHandIndex < 0)
            {
            SetText(battlefieldHintText, "目标基础牌不存在");
                dragBaseSlot = -1;
                RefreshCards();
                return false;
            }

            if (tutorialActive && !TutorialAllowsSynthesis(selectedSkillHandIndex, baseHandIndex))
                return false;

            var skillInstance = localPlayer?.deck?.hand != null &&
                                selectedSkillHandIndex < localPlayer.deck.hand.Count
                ? localPlayer.deck.hand[selectedSkillHandIndex]
                : null;
            var baseInstance = localPlayer?.deck?.hand != null && baseHandIndex < localPlayer.deck.hand.Count
                ? localPlayer.deck.hand[baseHandIndex]
                : null;

            var action = new PlayerActionMessageData
            {
                playerId = LocalPlayerId,
                actionType = ActionType.Synthesize,
                handIndex = selectedSkillHandIndex,
                targetIndex = baseHandIndex,
                cardId = skillInstance?.cardId,
                cardInstanceId = skillInstance?.instanceId,
                targetPlayerId = OpponentPlayerId,
                targetCardId = baseInstance?.cardId,
                targetInstanceId = baseInstance?.instanceId
            };

            var reason = string.Empty;
            var ok = SubmitBattleAction(action, out reason);
            SetText(battlefieldHintText,
                !ok ? reason : "强化牌已放入基础牌强化槽");
            ResetBaseDoubleClick();
            selectedSkillHandIndex = -1;
            selectedSkillInstanceId = null;
            selectedBaseSlot = -1;
            selectedBaseInstanceId = null;
            dragBaseSlot = -1;
            RefreshCards();
            if (ok)
            {
                GameAudioService.Instance?.PlaySfx(GameSfx.Intensify);
            }
            if (ok && baseSlot >= 0 && baseSlot < playerBaseSlotHandIndices.Length)
            {
                ShowHandCardExplain(playerBaseSlotHandIndices[baseSlot]);
            }

            if (ok && tutorialActive && tutorialStep == TutorialStep.AttachShieldSkill)
            {
                tutorialStep = TutorialStep.SelectSecondDefenseSkill;
                ShowTutorialStep();
            }
            else if (ok && tutorialActive && tutorialStep == TutorialStep.AttachSecondDefenseSkill)
            {
                tutorialStep = TutorialStep.PlayDefense;
                ShowTutorialStep();
            }
            else if (ok && tutorialActive && tutorialStep == TutorialStep.SynthesizeAttack)
            {
                tutorialStep = TutorialStep.PlayAttackAfterAttackSynthesis;
                ShowTutorialStep();
            }

            return ok;
        }

        /// <summary>
        /// 根据拖拽组件传入的强化牌槽和基础牌槽完成合成。
        /// </summary>
        public bool DropSkillCardOnBase(int skillSlot, int baseSlot)
        {
            if (skillSlot >= 0 && skillSlot < playerSkillSlotHandIndices.Length)
            {
                selectedSkillHandIndex = playerSkillSlotHandIndices[skillSlot];
                selectedSkillInstanceId = selectedSkillHandIndex >= 0 && selectedSkillHandIndex < localPlayer.deck.hand.Count
                    ? localPlayer.deck.hand[selectedSkillHandIndex]?.instanceId
                    : null;
            }

            return DropSkillCardOnBase(baseSlot);
        }

        /// <summary>
        /// 切换弃牌模式，只有弃牌阶段允许开启。
        /// </summary>
        private void ToggleDiscardMode()
        {
            if (!TutorialInputEnabled())
                return;
            if (tutorialActive && tutorialStep != TutorialStep.DiscardCards)
            {
                ShowTutorialBlocked("当前教程步骤不需要弃牌，请按顶部提示操作。");
                return;
            }
            if (gameController == null || gameController.State != GameState.Discarding)
            {
            SetText(battlefieldHintText, "只有弃牌阶段可以进入弃牌模式");
                return;
            }

            
            discardMode = !discardMode;
            ResetBaseDoubleClick();
            ResetSkillDoubleClick();
            if (discardMode)
            {
                SetText(discardButtonTextName,"弃牌中...");
            }
            else
            {
                SetText(discardButtonTextName,"弃牌");
            }
            SetText(battlefieldHintText, discardMode ? "弃牌模式：点击手牌弃掉" : "已退出弃牌模式");
        }

        private void OnendBattleClicked()
        {
            if (!TutorialInputEnabled())
                return;
            if (gameController == null)
            {
                return;
            }

            if (gameController.State == GameState.Preparation)
            {
                if (tutorialActive && tutorialStep != TutorialStep.EndPreparation &&
                    tutorialStep != TutorialStep.EndPreparationAfterAttackSynthesis)
                {
                    ShowTutorialBlocked("请先完成防御强化并打出防御牌。");
                    return;
                }
                if (tutorialActive)
                    tutorialStep = tutorialStep == TutorialStep.EndPreparationAfterAttackSynthesis
                        ? TutorialStep.SynthesizeAttack
                        : TutorialStep.PlayAttack;
                var prepareAction = new PlayerActionMessageData
                {
                    playerId = LocalPlayerId,
                    actionType = ActionType.EndPreparation,
                    targetPlayerId = OpponentPlayerId
                };
                if (!SubmitBattleAction(prepareAction, out var reason))
                {
                    SetText(battlefieldHintText, string.IsNullOrEmpty(reason) ? "无法结束备战" : reason);
                    return;
                }
                if (tutorialActive)
                {
                    RunTutorialAiAction();
                    ShowTutorialStep();
                }
                RefreshStaticUi();
                return;
            }

            if (gameController.State != GameState.Battle)
            {
                return;
            }
            SetText(battlefieldHintText, "战斗阶段固定持续 30 秒，只有强制终止回合的卡牌可以提前结束。\n持续型卡牌会在完整时限内公平生效。");
        }

        /// <summary>
        /// 提交本地玩家出牌行动。
        /// </summary>
        private void SubmitLocalAction(int handIndex)
        {
            CardBase playedCard = null;
            if (localPlayer?.deck?.hand != null && handIndex >= 0 && handIndex < localPlayer.deck.hand.Count)
                cardManager.TryGetCard(localPlayer.deck.hand[handIndex]?.cardId, out playedCard);
            var action = new PlayerActionMessageData
            {
                playerId = LocalPlayerId,
                targetPlayerId = AiPlayerId,
                actionType = ActionType.PlayBase,
                handIndex = handIndex,
                cardId = playedCard?.CardId,
                cardInstanceId = localPlayer?.deck?.hand != null && handIndex >= 0 &&
                                  handIndex < localPlayer.deck.hand.Count
                    ? localPlayer.deck.hand[handIndex]?.instanceId
                    : null
            };

            string reason = string.Empty;
            if (SubmitBattleAction(action, out reason))
            {
                GameAudioService.Instance?.PlaySfx(GameSfx.Send);
                RefreshStaticUi();
                if (tutorialActive && playedCard is HandCardBase tutorialCard)
                {
                    if (tutorialStep == TutorialStep.PlayDefense && tutorialCard.HandCardType == BaseCardType.Defense)
                    {
                        tutorialStep = TutorialStep.EndPreparation;
                        ShowTutorialStep();
                    }
                    else if ((tutorialStep == TutorialStep.PlayAttack ||
                              tutorialStep == TutorialStep.PlayAttackAfterAttackSynthesis) &&
                             tutorialCard.HandCardType == BaseCardType.Attack)
                    {
                        if (tutorialStep == TutorialStep.PlayAttackAfterAttackSynthesis)
                        {
                            FinishTutorial();
                            return;
                        }

                        tutorialStep = TutorialStep.ObserveShieldAndTimer;
                        ShowTutorialStep();
                    }
                }
            }
            else
            {
                SetText("BattlefieldHintText", reason);
            }

            selectedBaseSlot = -1;
            selectedBaseInstanceId = null;
            ResetBaseDoubleClick();
            RefreshCards();
        }

        /// <summary>
        /// 响应战斗阶段变化并刷新提示。
        /// </summary>
        private void OnStateChanged(GameState state)
        {
            if (state != GameState.Init && state != GameState.GameOver)
                GameAudioService.Instance?.PlaySfx(GameSfx.Next);
            discardMode = false;
            ResetBaseDoubleClick();
            ResetSkillDoubleClick();
            SetText(discardButtonTextName,"弃牌");
            if (state == GameState.Discarding)
            {
                SetTextColor(discardButtonTextName,Color.white);
            }
            else
            {
                SetTextColor(discardButtonTextName,Color.grey);
            }
            SetText(battlefieldHintText, tutorialActive ? TutorialPhaseText(state) : ResolvePhaseText(state));
            if (tutorialActive && state == GameState.Discarding && gameController.TurnIndex >= 2 &&
                tutorialStep == TutorialStep.ObserveShieldAndTimer)
            {
                tutorialStep = TutorialStep.DiscardCards;
                ShowTutorialStep();
            }
            else if (tutorialActive && state == GameState.Preparation && gameController.TurnIndex >= 2 &&
                     tutorialStep == TutorialStep.DiscardCards)
            {
                tutorialStep = TutorialStep.EndPreparationAfterAttackSynthesis;
                ShowTutorialStep();
            }
            RefreshCards();
            RefreshStaticUi();
        }

        /// <summary>
        /// 接收行动结算结果，写入出牌记录并播放简单出牌动画。
        /// </summary>
        private void OnActionResolved(ActionResultData result)
        {
            if (result == null)
            {
                return;
            }

            PlayActionSound(result);
            AddPlayLog(result);
            PlayActFx(result);
            SetText(battlefieldHintText, ActTxt(result));
            RefreshCards();
            RefreshStaticUi();
            if (IsShieldBroken(result))
            {
                PlayShieldBreak(result.targetId);
            }
        }

        private void PlayActionSound(ActionResultData result)
        {
            if (result == null)
            {
                return;
            }

            var audio = GameAudioService.Instance;
            if (audio == null)
                return;

            // 护盾破裂是受击结果，不属于“我方出牌音效”。无论攻击者是我方还是敌方，
            // 只要吸收护盾被击破，都必须播放 po。
            if (IsShieldBroken(result))
            {
                audio.PlaySfx(GameSfx.ShieldBreak);
            }

            // send/intensify 由成功的我方操作入口单独播放，这里不重复播放。
        }

        private bool IsShieldBroken(ActionResultData result)
        {
            if (result == null)
                return false;

            if (result.effects != null)
            {
                for (var i = 0; i < result.effects.Count; i++)
                {
                    var kind = result.effects[i].effectKind;
                    if (kind == CardEffectKind.HolyShield ||
                        (kind == CardEffectKind.DestroyShield && result.absorbShieldDelta < 0f) ||
                        (kind == CardEffectKind.AbsorbShield && result.absorbShieldDelta < 0f))
                        return true;
                }
            }

            if (result.absorbShieldDelta >= 0f)
            {
                return false;
            }

            if (gameController != null && !string.IsNullOrEmpty(result.targetId) &&
                gameController.Players.TryGetValue(result.targetId, out var target))
                return target.absorbShield <= 0f;

            return false;
        }

        /// <summary>
        /// 弃牌阶段提示玩家需要保留到指定数量。
        /// </summary>
        private void OnDiscardRequested(DiscardRequestMessageData message)
        {
            if (message != null && message.playerId == LocalPlayerId)
            {
                var baseLimit = balanceConfig != null ? balanceConfig.baseHandLimit : 7;
                var skillLimit = balanceConfig != null ? balanceConfig.strengthenedHandLimit : 7;
                SetText(battlefieldHintText,
                    baseLimit == skillLimit
                        ? $"手牌超过上限，基础手牌和强化卡上限均为 {baseLimit} 张，请弃牌。"
                        : $"手牌超过上限，基础手牌上限为 {baseLimit} 张，强化卡上限为 {skillLimit} 张，请弃牌。");
                if (!discardMode)
                    ToggleDiscardMode();
            }

            if (!onlineBattle)
                SubmitAiDiscardOverflow();
        }

        private void SubmitAiDiscardOverflow()
        {
            if (gameController == null || aiPlayer?.deck?.hand == null)
                return;

            var guard = aiPlayer.deck.hand.Count + 1;
            while (cardManager.CountOverflowCards(aiPlayer) > 0 && guard-- > 0)
            {
                var overflowBefore = cardManager.CountOverflowCards(aiPlayer);
                var submitted = false;
                for (var index = aiPlayer.deck.hand.Count - 1; index >= 0; index--)
                {
                    if (gameController.SubmitPlayerAction(new PlayerActionMessageData
                    {
                        playerId = AiPlayerId,
                        actionType = ActionType.Discard,
                        handIndex = index
                    }, out _, out _))
                    {
                        submitted = true;
                        if (cardManager.CountOverflowCards(aiPlayer) < overflowBefore)
                            break;
                    }
                }

                if (!submitted || cardManager.CountOverflowCards(aiPlayer) >= overflowBefore)
                    break;
            }
        }

        /// <summary>
        /// 战斗结束后显示结算面板。
        /// </summary>
        private void OnGameEnded(GameOverMessageData message)
        {
            if (gameOverHandled)
            {
                return;
            }

            gameOverHandled = true;
            GameAudioService.Instance?.PlaySfx(GameSfx.Pass);
            if (onlineBattle && onlineHost && message != null)
                BroadcastBattleSnapshot(null, message);
            var win = message != null && message.winnerId == LocalPlayerId;
            var rewardId = string.Empty;
            if (onlineBattle && accountService != null && message != null &&
                !string.IsNullOrEmpty(message.winnerId) && !string.IsNullOrEmpty(LocalPlayerId))
            {
                accountService.RecordPvpResult(win);
            }
            else if (!onlineBattle && accountService != null)
            {
                rewardId = bossBattle ? accountService.RecordPveBossResult(win) : RecordNormalBattleResult(win);
            }
            endSettlePanel.SetActive(true);
            SetText(endSettleTitleText, win ? "胜利" : "失败");
            SetTextColor(endSettleTitleText, win
                ? new Color32(54, 184, 92, 255)
                : new Color32(220, 62, 62, 255));
            if (win && !string.IsNullOrEmpty(rewardId) && cardManager != null && cardManager.TryGetCard(rewardId, out var rewardCard))
            {
                SetText(endSettleBodyText, $"击败首领，解锁强化卡：{rewardCard.DisplayName}");
            }
            else
            {
                var body = onlineBattle
                    ? (win
                        ? "恭喜你击败了对手，取得成功\n段位分+10"
                        : "很遗憾，你输掉了战斗\n段位分-10")
                    : (win ? "人机对战胜利奖励待领取" : "本局已结束");
                SetText(endSettleBodyText, body);
            }
        }

        private static string BuildOnlineSettlementMessage(bool win, string reason)
        {
            if (reason == "local_disconnected")
                return "你的连接已断开，本局判负";
            if (reason == "opponent_left" || reason == "opponent_timeout")
                return win ? "对手断开连接，判定胜利" : "本局连接已结束";
            return win ? "真人对战胜利" : "真人对战失败";
        }

        private string RecordNormalBattleResult(bool win)
        {
            accountService.RecordPveResult(win, false);
            return string.Empty;
        }

        /// <summary>
        /// 追加左侧出牌记录。
        /// </summary>
        private void AddPlayLog(ActionResultData result)
        {
            if (result == null)
            {
                return;
            }

            var who = result.actorId == LocalPlayerId ? "我方" : "对手";
            var tag = result.baseType.HasValue ? BaseTag(result.baseType.Value) : "[牌]";
            var elapsed = Mathf.Max(0f, Time.unscaledTime - battleStartTime);
            var timestamp = TimeSpan.FromSeconds(elapsed).ToString(@"mm\:ss");
            var skillNames = new List<string>();

            if (result.skillIds != null)
            {
                for (var i = 0; i < result.skillIds.Count; i++)
                {
                    var skillId = result.skillIds[i];
                    if (string.IsNullOrEmpty(skillId))
                    {
                        continue;
                    }

                    if (cardManager != null && cardManager.TryGetCard(skillId, out var skill))
                    {
                        skillNames.Add(FormatSkillLogName(skill.DisplayName, skill.Quality));
                    }
                    else
                    {
                        // 目录尚未完成热更新时仍保留可读日志，不显示空白或抛异常。
                        skillNames.Add(skillId);
                    }
                }
            }

            var suffix = skillNames.Count > 0 ? $" {string.Join(" + ", skillNames)}" : string.Empty;
            playLogs.Add($"{timestamp} {who} {tag}{suffix}");
            SetResultLog();
        }

        private static string FormatSkillLogName(string displayName, CardQuality quality)
        {
            var safeName = EscapeRichText(string.IsNullOrWhiteSpace(displayName) ? "未知强化" : displayName);
            switch (quality)
            {
                case CardQuality.Silver:
                    return $"<color=#C9D2DC>{safeName}</color>";
                case CardQuality.Gold:
                    return $"<color=#FFD34E>{safeName}</color>";
                case CardQuality.Platinum:
                    return $"<color=#B7E3A1>{safeName}</color>";
                case CardQuality.Prismatic:
                    return FormatPrismaticName(string.IsNullOrWhiteSpace(displayName) ? "未知强化" : displayName);
                default:
                    return safeName;
            }
        }

        private static string FormatPrismaticName(string displayName)
        {
            return $"<color=#FF00FF>{EscapeRichText(displayName)}</color>";
        }

        private static void SetQualityText(Text text, string value, CardQuality quality, bool unlocked)
        {
            if (text == null)
                return;

            text.supportRichText = true;
            if (!unlocked)
            {
                text.text = value ?? string.Empty;
                text.color = Color.gray;
                return;
            }

            text.color = quality == CardQuality.Prismatic ? Color.white : QualityColor(quality);
            text.text = quality == CardQuality.Prismatic
                ? FormatPrismaticName(value ?? string.Empty)
                : value ?? string.Empty;
            if (quality == CardQuality.Prismatic && text.GetComponent<PrismaticTextGradientEffect>() == null)
                text.gameObject.AddComponent<PrismaticTextGradientEffect>();
        }

        private void SetFieldGuidePreviewText(string value)
        {
            if (fieldGuideCardPreviewText == null ||
                !fieldGuideCardPreviewText.TryGetComponent<Text>(out var previewText))
                return;

            previewText.text = value ?? string.Empty;
            previewText.color = fieldGuideCardPreviewTextDefaultColor;
        }

        private static string EscapeRichText(string value)
        {
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        /// <summary>
        /// 刷新 ResultPanel 文本。
        /// </summary>
        private void SetResultLog()
        {
            SetText(resultLogText, playLogs.Count == 0 ? "本局暂无出牌记录" : string.Join("\n", playLogs));
            Canvas.ForceUpdateCanvases();
            if (resultLogText == null || resultLogContent == null || resultLogViewport == null ||
                !resultLogText.TryGetComponent<Text>(out var logText))
            {
                return;
            }

            var textRect = logText.rectTransform;
            var contentRect = resultLogContent.GetComponent<RectTransform>();
            var viewportRect = resultLogViewport.GetComponent<RectTransform>();
            if (contentRect == null || viewportRect == null)
                return;

            logText.alignment = TextAnchor.UpperLeft;
            logText.supportRichText = true;
            if (resultLogText.GetComponent<PrismaticTextGradientEffect>() == null)
                resultLogText.AddComponent<PrismaticTextGradientEffect>();
            logText.horizontalOverflow = HorizontalWrapMode.Wrap;
            logText.verticalOverflow = VerticalWrapMode.Overflow;
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);

            var textHeight = Mathf.Max(1f, logText.preferredHeight + 12f);
            var contentHeight = Mathf.Max(viewportRect.rect.height, textHeight + 8f);
            contentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);
            textRect.offsetMin = new Vector2(8f, -textHeight);
            textRect.offsetMax = new Vector2(-8f, 0f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            Canvas.ForceUpdateCanvases();

            if (resultLogScroll != null && resultLogScroll.TryGetComponent<ScrollRect>(out var scrollRect))
            {
                scrollRect.content = contentRect;
                scrollRect.viewport = viewportRect;
                scrollRect.StopMovement();
                scrollRect.verticalNormalizedPosition = 0f;
                if (resultScrollCoroutine != null)
                    StopCoroutine(resultScrollCoroutine);
                resultScrollCoroutine = StartCoroutine(ScrollResultLogToBottom(scrollRect));
            }
        }

        private bool TutorialAllowsSkillCard(int handIndex)
        {
            var synthesizingAttack = tutorialStep == TutorialStep.SynthesizeAttack;
            var selectingShield = tutorialStep == TutorialStep.SelectShieldSkill ||
                                  tutorialStep == TutorialStep.AttachShieldSkill;
            var selectingSecond = tutorialStep == TutorialStep.SelectSecondDefenseSkill ||
                                  tutorialStep == TutorialStep.AttachSecondDefenseSkill;
            if (tutorialStep == TutorialStep.DiscardCards)
                return true;
            if (!synthesizingAttack && !selectingShield && !selectingSecond)
            {
                ShowTutorialBlocked("当前步骤不需要强化牌，请按顶部提示操作。");
                return false;
            }

            if (!TryGetHandCardDefinition(localPlayer, handIndex, out var definition) ||
                definition is not SkillCardBase skill ||
                (!synthesizingAttack && (skill.SkillCardType != BaseCardType.Defense || !skill.CanUseOnPre)))
            {
                ShowTutorialBlocked("请选择标有“可在备战期使用”的防御强化牌。");
                return false;
            }
            if (synthesizingAttack && skill.SkillCardType != BaseCardType.Attack)
            {
                ShowTutorialBlocked("请选择攻击强化牌，用它合成攻击基础牌。");
                return false;
            }
            if (selectingShield && !string.Equals(skill.CardId, "sk_s_def_shield", StringComparison.Ordinal))
            {
                ShowTutorialBlocked("第一张请选择“护盾”强化牌，它会生成肉眼可见的吸收护盾。");
                return false;
            }
            if (selectingSecond && string.Equals(skill.CardId, "sk_s_def_shield", StringComparison.Ordinal))
            {
                ShowTutorialBlocked("第二张请选择另一张防御强化牌，用来观察双强化叠加。");
                return false;
            }
            return true;
        }

        private bool TutorialAllowsBaseCard(int handIndex)
        {
            if (!TryGetHandCardDefinition(localPlayer, handIndex, out var definition) ||
                definition is not HandCardBase handCard)
                return false;

            if (tutorialStep == TutorialStep.DiscardCards)
                return true;
            if (tutorialStep == TutorialStep.PlayDefense && handCard.HandCardType == BaseCardType.Defense)
                return true;
            if ((tutorialStep == TutorialStep.SynthesizeAttack || tutorialStep == TutorialStep.PlayAttack ||
                 tutorialStep == TutorialStep.PlayAttackAfterAttackSynthesis) &&
                handCard.HandCardType == BaseCardType.Attack)
                return true;
            if (tutorialStep == TutorialStep.InspectGrownAttack && handCard.HandCardType == BaseCardType.Attack)
                return true;

            ShowTutorialBlocked(tutorialStep == TutorialStep.AttachShieldSkill ||
                                tutorialStep == TutorialStep.AttachSecondDefenseSkill
                ? "请把已选中的防御强化牌拖到防御基础牌上。"
                : "这不是当前步骤要求操作的基础牌。");
            return false;
        }

        private bool TutorialAllowsSynthesis(int skillHandIndex, int baseHandIndex)
        {
            var synthesizingAttack = tutorialStep == TutorialStep.SynthesizeAttack;
            if ((!synthesizingAttack && tutorialStep != TutorialStep.AttachShieldSkill &&
                 tutorialStep != TutorialStep.AttachSecondDefenseSkill) ||
                !TryGetHandCardDefinition(localPlayer, skillHandIndex, out var skillDefinition) ||
                skillDefinition is not SkillCardBase skill ||
                synthesizingAttack && skill.SkillCardType != BaseCardType.Attack ||
                !synthesizingAttack && (skill.SkillCardType != BaseCardType.Defense || !skill.CanUseOnPre) ||
                !TryGetHandCardDefinition(localPlayer, baseHandIndex, out var baseDefinition) ||
                baseDefinition is not HandCardBase handCard ||
                handCard.HandCardType != (synthesizingAttack ? BaseCardType.Attack : BaseCardType.Defense))
            {
                ShowTutorialBlocked(synthesizingAttack
                    ? "请把攻击强化牌合成到攻击基础牌上。"
                    : "本步骤只能把备战可用的防御强化牌合成到防御基础牌。");
                return false;
            }
            if (!synthesizingAttack && ((tutorialStep == TutorialStep.AttachShieldSkill &&
                 !string.Equals(skill.CardId, "sk_s_def_shield", StringComparison.Ordinal)) ||
                (tutorialStep == TutorialStep.AttachSecondDefenseSkill &&
                 string.Equals(skill.CardId, "sk_s_def_shield", StringComparison.Ordinal))))
            {
                ShowTutorialBlocked("请使用当前步骤指定的防御强化牌完成合成。");
                return false;
            }
            return true;
        }

        private bool TryGetHandCardDefinition(PlayerCombatStateData player, int handIndex, out CardBase definition)
        {
            definition = null;
            return player?.deck?.hand != null && handIndex >= 0 && handIndex < player.deck.hand.Count &&
                   cardManager != null && cardManager.TryGetCard(player.deck.hand[handIndex]?.cardId, out definition);
        }

        private void RunTutorialAiAction()
        {
            if (aiPlayer?.deck?.hand != null)
            {
                var attackBaseIndex = -1;
                var attackSkillIndex = -1;
                for (var index = 0; index < aiPlayer.deck.hand.Count; index++)
                {
                    if (!TryGetHandCardDefinition(aiPlayer, index, out var definition))
                        continue;
                    if (attackBaseIndex < 0 && definition is HandCardBase handCard &&
                        handCard.HandCardType == BaseCardType.Attack)
                        attackBaseIndex = index;
                    if (attackSkillIndex < 0 && definition is SkillCardBase skillCard &&
                        skillCard.SkillCardType == BaseCardType.Attack)
                        attackSkillIndex = index;
                }

                if (attackBaseIndex >= 0 && attackSkillIndex >= 0)
                {
                    gameController.SubmitPlayerAction(new PlayerActionMessageData
                    {
                        playerId = AiPlayerId,
                        targetPlayerId = LocalPlayerId,
                        actionType = ActionType.Synthesize,
                        handIndex = attackSkillIndex,
                        targetIndex = attackBaseIndex
                    }, out _, out _);
                }

                for (var index = 0; index < aiPlayer.deck.hand.Count; index++)
                {
                    if (!TryGetHandCardDefinition(aiPlayer, index, out var definition) ||
                        definition is not HandCardBase handCard || handCard.HandCardType != BaseCardType.Attack)
                        continue;
                    gameController.SubmitPlayerAction(new PlayerActionMessageData
                    {
                        playerId = AiPlayerId,
                        targetPlayerId = LocalPlayerId,
                        actionType = ActionType.PlayBase,
                        handIndex = index
                    }, out _, out _);
                    break;
                }
            }
        }

        private void ShowTutorialStep()
        {
            if (!tutorialActive)
                return;
            if (tutorialPanel != null)
            {
                tutorialPanel.SetActive(true);
                tutorialPanel.transform.SetAsLastSibling();
            }
            SetText(tutorialTitle, $"新手教程 · 第 {Mathf.Clamp((int)tutorialStep, 1, 14)}/14 步");
            string body;
            switch (tutorialStep)
            {
                case TutorialStep.DiscardCards:
                    body = "第二回合进入弃牌阶段。点击“弃牌”进入弃牌模式，再双击一张不需要的手牌；弃够超出上限的牌后，继续进行攻击合成。";
                    break;
                case TutorialStep.SelectShieldSkill:
                    body = "先单击“护盾”防御强化牌。它可以生成吸收护盾，白银护盾只抵消攻击伤害，不会清除攻击附带的灼烧等特殊效果。";
                    break;
                case TutorialStep.AttachShieldSkill:
                    body = "把“护盾”强化牌拖到防御基础牌上。基础牌最多可以挂载多张强化，强化效果会共同结算。";
                    break;
                case TutorialStep.SelectSecondDefenseSkill:
                    body = "再选择“反震”防御强化牌。下一步把它叠加到同一张防御基础牌，观察两个独立效果共同生效。";
                    break;
                case TutorialStep.AttachSecondDefenseSkill:
                    body = "把第二张防御强化牌拖到刚才的防御基础牌。卡面详情会同时列出两张强化及合计效果。";
                    break;
                case TutorialStep.PlayDefense:
                    body = "双击这张双强化防御基础牌。备战阶段最多打出一张防御基础牌，护盾和防御增益会同时生效。";
                    break;
                case TutorialStep.EndPreparation:
                    body = gameController != null && gameController.TurnIndex >= 2
                        ? "攻击强化已经合成。点击“准备”按钮结束备战，进入战斗阶段。"
                        : "防御已经生效。点击“准备”按钮结束备战，进入战斗阶段。";
                    break;
                case TutorialStep.SynthesizeAttack:
                    body = "现在是战斗阶段。先选择“蓄力”攻击强化牌，再把它拖到攻击基础牌上完成攻击合成。";
                    break;
                case TutorialStep.EndPreparationAfterAttackSynthesis:
                    body = "第二回合准备完成。点击“准备”按钮进入战斗阶段，进入战斗后再合成攻击强化牌。";
                    break;
                case TutorialStep.PlayAttack:
                    body = "人机已经开始攻击。吸收护盾可以抵挡一次敌方攻击；现在你也可以打出攻击卡牌，请双击一张攻击基础牌开始出牌。";
                    break;
                case TutorialStep.PlayAttackAfterAttackSynthesis:
                    body = "攻击强化已经生效。双击攻击基础牌完成第二回合出牌，教程就结束了。";
                    break;
                case TutorialStep.ObserveShieldAndTimer:
                    body = "对面的攻击会击碎你的吸收护盾。战斗阶段必须完整持续 30 秒，持续灼烧、治疗和全局效果才能公平结算；只有强制终止卡可以提前结束。";
                    break;
                case TutorialStep.InspectGrownAttack:
                    body = "第二回合开始。单击上一回合保留在手中的攻击基础牌查看详情：成长数值始终为 1，只有基础数值从 1 增加到了 2。";
                    break;
                case TutorialStep.Complete:
                    body = "教程完成！你已体验双强化叠加、备战防御、护盾防御、玩家与人机即时对战、 每阶段轮换和回合，以及攻击基础牌留手成长。";
                    break;
                default:
                    body = "请按提示完成当前操作。";
                    break;
            }
            SetText(tutorialBody, body);
            SetText(battlefieldHintText, body);
            if (tutorialCompleteButton != null && tutorialStep == TutorialStep.SelectShieldSkill &&
                tutorialAwaitingStart)
            {
                var label = tutorialCompleteButton.GetComponentInChildren<Text>(true);
                if (label != null)
                    label.text = "开始第二步";
                tutorialCompleteButton.gameObject.SetActive(true);
            }
        }

        private void ShowTutorialBlocked(string message)
        {
            SetText(battlefieldHintText, message);
        }

        private bool TutorialInputEnabled()
        {
            if (!tutorialActive || !tutorialAwaitingStart)
                return true;

            ShowTutorialBlocked("请点击“开始第二步”继续教程。");
            return false;
        }

        private void AdvanceTutorialStart()
        {
            if (!tutorialActive || !tutorialAwaitingStart)
                return;

            tutorialAwaitingStart = false;
            if (tutorialCompleteButton != null)
                tutorialCompleteButton.gameObject.SetActive(false);
            ShowTutorialStep();
        }

        private void FinishTutorial()
        {
            tutorialFinished = true;
            tutorialStep = TutorialStep.Complete;
            PlayerPrefs.SetInt("NotCardGod.Tutorial.Completed", 1);
            PlayerPrefs.Save();
            if (tutorialCompleteButton != null)
            {
                tutorialCompleteButton.onClick.RemoveAllListeners();
                tutorialCompleteButton.onClick.AddListener(CompleteTutorialAndReturn);
                tutorialCompleteButton.gameObject.SetActive(true);
                var label = tutorialCompleteButton.GetComponentInChildren<Text>(true);
                if (label != null)
                    label.text = "完成并返回主菜单";
            }
            ShowTutorialStep();
        }

        private void CompleteTutorialAndReturn()
        {
            if (tutorialFinished)
                SceneManager.LoadScene("Main");
        }

        private string TutorialPhaseText(GameState state)
        {
            switch (state)
            {
                case GameState.Draw:
                    return "教程：回合开始会抽取基础卡和强化卡；基础攻击卡的基础数值会成长，成长数值始终为 1。";
                case GameState.Discarding:
                    return "教程：单击卡牌查看 ExplainPanel 详情，确认后双击任意基础卡或技能卡弃牌。";
                case GameState.Preparation:
                    return "教程：拖动或点击强化卡，将它附加到同类型基础卡；本阶段最多打出一张防御基础卡。";
                case GameState.Battle:
                    return "教程：单击基础卡查看详情，双击打出。攻击对手，防御保护自己，治疗恢复生命。";
                case GameState.TurnEnd:
                    return "教程：本回合结算完成。强化效果会叠加在基础数值上，下一回合继续抽牌。";
                default:
                    return ResolvePhaseText(state);
            }
        }

        private static IEnumerator ScrollResultLogToBottom(ScrollRect scrollRect)
        {
            yield return new WaitForEndOfFrame();
            Canvas.ForceUpdateCanvases();
            if (scrollRect == null)
                yield break;
            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = 0f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (scrollRect == null)
                yield break;
            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = 0f;
        }

        /// <summary>
        /// 播放出牌进入战场的简化动画。
        /// </summary>
        private void PlayActFx(ActionResultData result)
        {
            if (desk != null)
            {
                desk.Put(actRt, result != null ? result.cardName : string.Empty, actColor, actSize);
            }
        }

        /// <summary>
        /// 返回基础牌类型的日志标签。
        /// </summary>
        private static string BaseTag(BaseCardType type)
        {
            switch (type)
            {
                case BaseCardType.Attack:
                    return "[攻]";
                case BaseCardType.Defense:
                    return "[防]";
                case BaseCardType.Heal:
                    return "[疗]";
                default:
                    return "[牌]";
            }
        }

        /// <summary>
        /// 返回基础牌类型中文名称。
        /// </summary>
        private static string BaseTypeName(BaseCardType type)
        {
            switch (type)
            {
                case BaseCardType.Attack:
                    return "攻击";
                case BaseCardType.Defense:
                    return "防御";
                case BaseCardType.Heal:
                    return "治疗";
                default:
                    return "未知";
            }
        }

        /// <summary>
        /// 返回基础牌使用的主色。
        /// </summary>
        private static Color BaseColor(BaseCardType type)
        {
            switch (type)
            {
                case BaseCardType.Attack:
                    return new Color32(0xEC, 0x54, 0x44, 0xFF);
                case BaseCardType.Defense:
                    return new Color32(0x57, 0x9B, 0xFF, 0xFF);
                case BaseCardType.Heal:
                    return new Color32(0x00, 0xFC, 0x88, 0xFF);
                default:
                    return Color.white;
            }
        }

        /// <summary>
        /// 返回强化牌品级中文名称。
        /// </summary>
        private static string QualityName(CardQuality quality)
        {
            switch (quality)
            {
                case CardQuality.Gold:
                    return "黄金强化卡牌";
                case CardQuality.Platinum:
                    return "白金强化卡牌";
                case CardQuality.Prismatic:
                    return "棱彩强化卡牌";
                default:
                    return "白银强化卡牌";
            }
        }

        /// <summary>
        /// 返回强化牌品级显示颜色。
        /// </summary>
        private static Color QualityColor(CardQuality quality)
        {
            switch (quality)
            {
                case CardQuality.Gold:
                    return new Color32(0xF4, 0xC4, 0x30, 0xFF);
                case CardQuality.Platinum:
                    return new Color32(0xD8, 0xF0, 0xFF, 0xFF);
                case CardQuality.Prismatic:
                    return new Color32(0xFF, 0x6F, 0xD8, 0xFF);
                default:
                    return new Color32(0xC8, 0xC8, 0xD0, 0xFF);
            }
        }

        /// <summary>
        /// 从卡牌预制体中读取第一张可用图片作为图鉴预览。
        /// </summary>
        /// <summary>
        /// 返回指定卡牌应该使用的预制体。
        /// </summary>
        private GameObject CardPrefab(CardBase card)
        {
            if (card is HandCardBase hand)
            {
                switch (hand.HandCardType)
                {
                    case BaseCardType.Attack:
                        return baseAttackPrefab;
                    case BaseCardType.Defense:
                        return baseDefensePrefab;
                    case BaseCardType.Heal:
                        return baseHealPrefab;
                }
            }

            if (card is SkillCardBase skill)
            {
                switch (skill.Quality)
                {
                    case CardQuality.Gold:
                        return skillGoldPrefab;
                    case CardQuality.Platinum:
                        return skillPlatinumPrefab;
                    case CardQuality.Prismatic:
                        return skillPrismaticPrefab;
                    default:
                        return skillSilverPrefab;
                }
            }

            return null;
        }

        /// <summary>
        /// 返回卡牌预制体对象池使用的分组键。
        /// </summary>
        private static string CardPrefabKey(CardBase card)
        {
            if (card is HandCardBase hand)
            {
                return $"base_{hand.HandCardType}";
            }

            if (card is SkillCardBase skill)
            {
                return $"skill_{skill.Quality}";
            }

            return "unknown";
        }

        private Sprite CardPrefabSprite(CardBase card)
        {
            var prefab = CardPrefab(card);
            if (prefab == null)
            {
                return null;
            }

            if (cardSpriteCache.TryGetValue(prefab, out var cached))
            {
                return cached;
            }

            var image = prefab.GetComponentInChildren<Image>(true);
            var sprite = image != null ? image.sprite : null;
            cardSpriteCache[prefab] = sprite;
            return sprite;
        }

        private string ResolvePhaseText(GameState state)
        {
            switch (state)
            {
                case GameState.Draw:
                    return "发牌";
                case GameState.Preparation:
                    return "备战";
                case GameState.Battle:
                    return "战斗";
                case GameState.Discarding:
                    return "弃牌";
                case GameState.GameOver:
                    return "结束";
                default:
                    return state.ToString();
            }
        }

        /// <summary>
        /// 把行动结果转换为战场提示文本。
        /// </summary>
        private string ActTxt(ActionResultData result)
        {
            var actorName = result.actorId == LocalPlayerId ? "我方" : "对手";
            
            if (result.hpDelta < 0)
            {
                return $"{actorName} 造成 {FormatNumber(-result.hpDelta)} 点伤害";
            }

            if (result.hpDelta > 0)
            {
                return $"{actorName} 回复 {FormatNumber(result.hpDelta)} 点生命";
            }

            if (result.absorbShieldDelta > 0)
            {
                return $"{actorName} 获得 {FormatNumber(result.absorbShieldDelta)} 点护盾";
            }

            if (result.shieldDelta > 0)
            {
                return $"{actorName} 获得 {FormatNumber(result.shieldDelta)} 点防御";
            }

            return $"{actorName} 使用卡牌";
        }

        /// <summary>
        /// 设置指定文本节点的显示内容。
        /// </summary>
        private void SetText(string objectName, string value)
        {
            var target = SceneObject(objectName);
            if (target != null && target.TryGetComponent<Text>(out var text))
            {
                if (!string.Equals(text.text, value, StringComparison.Ordinal))
                {
                    text.text = value;
                }
            }
        }

        private static void SetText(GameObject target, string value)
        {
            if (target != null && target.TryGetComponent<Text>(out var text))
            {
                text.text = value;
            }
        }

        private static void SetTextColor(GameObject target, Color color)
        {
            if (target != null && target.TryGetComponent<Text>(out var text))
                text.color = color;
        }

        private void SetVitalsText(string hpObjectName, string shieldObjectName, string label, PlayerCombatStateData player)
        {
            var hpTarget = SceneObject(hpObjectName);
            if (player == null || hpTarget == null || !hpTarget.TryGetComponent<Text>(out var hpText))
            {
                return;
            }

            var hp = FormatNumber(player.hp);
            var shield = FormatNumber(player.shield);
            var shieldTarget = SceneObject(shieldObjectName);
            if (shieldTarget != null && shieldTarget.TryGetComponent<Text>(out var shieldText))
            {
                var hpValue = $"{label} {hp}";
                if (!string.Equals(hpText.text, hpValue, StringComparison.Ordinal))
                {
                    hpText.text = hpValue;
                }

                var shieldValue = $"防御 {shield}";
                if (!string.Equals(shieldText.text, shieldValue, StringComparison.Ordinal))
                {
                    shieldText.text = shieldValue;
                }

                return;
            }

            var combined = $"{label}  HP {hp}  防御 {shield}";
            if (!string.Equals(hpText.text, combined, StringComparison.Ordinal))
            {
                hpText.text = combined;
            }
        }

        /// <summary>
        /// 执行 SetTextColor 对应的运行时流程。
        /// </summary>
        private void SetTextColor(string objectName, Color color)
        {
            var target = SceneObject(objectName);
            if (target != null && target.TryGetComponent<Text>(out var text))
            {
                if (text.color != color)
                {
                    text.color = color;
                }
            }
        }

        /// <summary>
        /// 设置指定图片节点的精灵和颜色。
        /// </summary>
        private static void SetImage(Image image, Sprite sprite, Color color)
        {
            if (image == null)
            {
                return;
            }

            if (image.sprite != sprite)
            {
                image.sprite = sprite;
            }

            if (image.color != color)
            {
                image.color = color;
            }
        }

        /// <summary>
        /// 执行 SetActive 对应的运行时流程。
        /// </summary>
        private void SetActive(string objectName, bool active)
        {
            var target = SceneObject(objectName);
            if (target != null)
            {
                target.SetActive(active);
            }
        }

        /// <summary>
        /// 执行 SetInteractable 对应的运行时流程。
        /// </summary>
        private void SetInteractable(string objectName, bool interactable)
        {
            var target = SceneObject(objectName);
            if (target != null && target.TryGetComponent<Button>(out var button))
            {
                button.interactable = interactable;
            }
        }

        /// <summary>
        /// 执行 CanUseInstanceOnPre 对应的运行时流程。
        /// </summary>
        private bool CanUseInstanceOnPre(CardInstanceData instance, CardBase card, out string reason)
        {
            reason = string.Empty;
            if (gameController.State != GameState.Preparation)
            {
                return true;
            }

            if (!card.CanUseOnPre &&
                (!(card is HandCardBase handCard) || handCard.HandCardType != BaseCardType.Defense))
            {
                reason = "这张牌不能在备战阶段使用";
                return false;
            }

            if (instance?.attachedSkillCards == null)
            {
                return true;
            }

            for (var i = 0; i < instance.attachedSkillCards.Count; i++)
            {
                var attached = instance.attachedSkillCards[i];
                if (attached != null && cardManager.TryGetCard(attached.cardId, out var attachedCard) && !attachedCard.CanUseOnPre)
                {
                    reason = "这张基础牌上有不能在备战阶段使用的强化牌";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 执行 ResolveTargetBaseInstance 对应的运行时流程。
        /// </summary>
        private CardInstanceData ResolveTargetBaseInstance(CardBase targetBaseCard)
        {
            if (localPlayer?.deck?.hand == null || targetBaseCard == null)
            {
                return null;
            }

            if (selectedBaseSlot >= 0 && selectedBaseSlot < playerBaseSlotHandIndices.Length)
            {
                var selectedHandIndex = playerBaseSlotHandIndices[selectedBaseSlot];
                if (selectedHandIndex >= 0 && selectedHandIndex < localPlayer.deck.hand.Count)
                {
                    return localPlayer.deck.hand[selectedHandIndex];
                }
            }

            for (var i = 0; i < localPlayer.deck.hand.Count; i++)
            {
                var instance = localPlayer.deck.hand[i];
                if (instance != null && instance.cardId == targetBaseCard.CardId)
                {
                    return instance;
                }
            }

            return null;
        }

        /// <summary>
        /// 执行 FormatNumber 对应的运行时流程。
        /// </summary>
        private static string FormatNumber(float value)
        {
            return (Mathf.Floor(value * 10f) / 10f).ToString("0.#");
        }
    }

    internal sealed class PrismaticTextGradientEffect : BaseMeshEffect
    {
        private static readonly Color32 TopLeft = new Color32(71, 224, 255, 255);
        private static readonly Color32 TopRight = new Color32(255, 240, 96, 255);
        private static readonly Color32 BottomRight = new Color32(255, 76, 174, 255);
        private static readonly Color32 BottomLeft = new Color32(151, 91, 255, 255);

        public override void ModifyMesh(VertexHelper vertexHelper)
        {
            if (!IsActive() || vertexHelper == null)
                return;

            var vertexCount = vertexHelper.currentVertCount;
            for (var vertexIndex = 0; vertexIndex + 3 < vertexCount; vertexIndex += 4)
            {
                var markerCount = 0;
                for (var corner = 0; corner < 4; corner++)
                {
                    UIVertex markerVertex = default;
                    vertexHelper.PopulateUIVertex(ref markerVertex, vertexIndex + corner);
                    if (IsPrismaticMarker(markerVertex.color))
                        markerCount++;
                }

                if (markerCount < 3)
                    continue;

                SetCornerColor(vertexHelper, vertexIndex, TopLeft);
                SetCornerColor(vertexHelper, vertexIndex + 1, TopRight);
                SetCornerColor(vertexHelper, vertexIndex + 2, BottomRight);
                SetCornerColor(vertexHelper, vertexIndex + 3, BottomLeft);
            }
        }

        private static bool IsPrismaticMarker(Color32 color)
        {
            return color.r >= 220 && color.g <= 40 && color.b >= 220;
        }

        private static void SetCornerColor(VertexHelper vertexHelper, int vertexIndex, Color32 color)
        {
            UIVertex vertex = default;
            vertexHelper.PopulateUIVertex(ref vertex, vertexIndex);
            vertex.color = color;
            vertexHelper.SetUIVertex(vertex, vertexIndex);
        }
    }

    internal sealed class ShieldFx
    {
        private readonly GameObject obj;
        private readonly RectTransform rt;
        private readonly Image image;
        private readonly CanvasGroup group;
        private readonly Material material;

        private ShieldFx(GameObject obj, RectTransform rt, Image image, CanvasGroup group, Material material)
        {
            this.obj = obj;
            this.rt = rt;
            this.image = image;
            this.group = group;
            this.material = material;
        }

        public static ShieldFx Create(Transform parent, Shader shader)
        {
            if (parent == null)
            {
                return null;
            }

            // 护盾对象由 Battle 场景预先创建。这里只绑定并补齐运行时组件，
            // 不再创建第二个同名对象，避免护盾显示在错误层级或覆盖卡牌。
            var shieldTransform = parent.Find("sheild");
            if (shieldTransform == null)
            {
                Debug.LogWarning($"[BattleUI] 未找到场景护盾对象: {parent.name}/sheild");
                return null;
            }

            var obj = shieldTransform.gameObject;
            obj.layer = 5;
            var rt = obj.GetComponent<RectTransform>();
            if (rt == null)
            {
                rt = obj.AddComponent<RectTransform>();
            }

            var canvasRenderer = obj.GetComponent<CanvasRenderer>();
            if (canvasRenderer == null)
            {
                obj.AddComponent<CanvasRenderer>();
            }

            var image = obj.GetComponent<Image>();
            if (image == null)
            {
                image = obj.AddComponent<Image>();
            }

            var group = obj.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = obj.AddComponent<CanvasGroup>();
            }

            obj.transform.SetAsLastSibling();

            // 只统一护盾绘制区域，不修改场景文件本身。
            rt.sizeDelta = new Vector2(430f, 150f);
            image.raycastTarget = false;
            image.sprite = null;
            var effectShader = Shader.Find("NotCardGod/UI/ShieldCapsule") ?? shader;
            var material = effectShader != null ? new Material(effectShader) : null;
            image.material = material;
            image.color = Color.white;

            group.blocksRaycasts = false;
            group.interactable = false;
            group.alpha = 0f;

            var fx = new ShieldFx(obj, rt, image, group, material);
            fx.Hide();
            return fx;
        }

        public void Show(bool holy)
        {
            if (obj == null)
            {
                return;
            }

            obj.SetActive(true);
            obj.transform.SetAsLastSibling();
            rt.DOKill();
            group.DOKill();
            rt.localScale = Vector3.one;
            image.color = holy
                ? new Color(1f, 0.82f, 0.24f, 1f)
                : new Color(0.2f, 0.72f, 1f, 1f);
            group.alpha = 1f;
            SetBreak(0f);
            rt.DOScale(holy ? 1.08f : 1.045f, holy ? 0.42f : 0.62f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }

        public void Hide()
        {
            if (obj == null)
            {
                return;
            }

            rt.DOKill();
            group.DOKill();
            group.alpha = 0f;
            obj.SetActive(false);
        }

        public void Break()
        {
            if (obj == null)
            {
                return;
            }

            obj.SetActive(true);
            obj.transform.SetAsLastSibling();
            rt.DOKill();
            group.DOKill();
            rt.localScale = Vector3.one;
            group.alpha = 1f;
            SetBreak(0f);
            var breakValue = 0f;
            var sequence = DOTween.Sequence();
            sequence.Append(DOTween.To(() => breakValue, value =>
            {
                breakValue = value;
                SetBreak(value);
            }, 0.55f, 0.34f).SetEase(Ease.OutQuad));
            sequence.Append(DOTween.To(() => breakValue, value =>
            {
                breakValue = value;
                SetBreak(value);
            }, 1f, 0.38f).SetEase(Ease.InQuad));
            sequence.Join(rt.DOScale(new Vector3(1.2f, 1.14f, 1f), 0.38f).SetEase(Ease.OutQuad));
            sequence.AppendInterval(0.16f);
            sequence.Append(group.DOFade(0f, 0.24f).SetEase(Ease.InQuad));
            sequence.OnComplete(() => obj.SetActive(false));
        }

        private void SetBreak(float value)
        {
            if (material != null && material.HasProperty("_Break"))
            {
                material.SetFloat("_Break", value);
            }
        }
    }

    internal sealed class CardFx
    {
        private static readonly Stack<CardFx> Pool = new Stack<CardFx>();
        private static Shader effectShader;

        public GameObject obj;
        public RectTransform rt;
        private Image img;
        private Text txt;
        private CanvasGroup group;

        public static void Configure(Shader shader)
        {
            effectShader = shader;
        }
        

        /// <summary>
        /// 执行 Release 对应的运行时流程。
        /// </summary>
        public static void Release(CardFx fx)
        {
            if (fx == null || fx.obj == null)
            {
                return;
            }

            fx.rt.DOKill();
            fx.SetAlpha(1f);
            fx.obj.SetActive(false);
            Pool.Push(fx);
        }

        /// <summary>
        /// 执行 ClearPool 对应的运行时流程。
        /// </summary>
        public static void ClearPool()
        {
            while (Pool.Count > 0)
            {
                var fx = Pool.Pop();
                if (fx != null && fx.obj != null)
                {
                    UnityEngine.Object.Destroy(fx.obj);
                }
            }
        }

        /// <summary>
        /// 执行 Fade 对应的运行时流程。
        /// </summary>
        public Tween Fade(float alpha, float seconds)
        {
            return group != null ? group.DOFade(alpha, seconds) : null;
        }

        /// <summary>
        /// 执行 SetAlpha 对应的运行时流程。
        /// </summary>
        public void SetAlpha(float alpha)
        {
            if (group != null)
            {
                group.alpha = alpha;
            }
        }
}
