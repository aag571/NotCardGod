using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

    /// <summary>
    /// 控制战斗卡牌界面的文本、图标、冷却、锁定、选中和可用状态显示。
    /// </summary>
    public sealed class BattleCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private static readonly List<RaycastResult> RaycastResults = new List<RaycastResult>();

        private RectTransform rectTransform;
        private Image image;
        private Canvas canvas;
        private Transform homeParent;
        private int homeIndex;
        private Vector2 baseAnchoredPosition;
        private float baseRotation;
        private bool initialized;
        private bool hovered;
        private bool pressed;
        private bool dragging;
        private bool selected;
        private bool dragSelected;
        private bool playable;
        private bool opponentCard;
        private bool skillCard;

        /// <summary>
        /// 配置卡牌视图的显示数据。
        /// </summary>
        public void Configure(Vector2 anchoredPosition, float rotation, bool isSelected, bool isDragSelected, bool isPlayable, bool isOpponentCard, bool isSkillCard)
        {
            EnsureReferences();
            baseAnchoredPosition = anchoredPosition;
            baseRotation = rotation;
            selected = isSelected;
            dragSelected = isDragSelected;
            playable = isPlayable;
            opponentCard = isOpponentCard;
            skillCard = isSkillCard;

            if (!initialized)
            {
                rectTransform.anchoredPosition = baseAnchoredPosition;
                rectTransform.localRotation = Quaternion.Euler(0f, 0f, baseRotation);
                rectTransform.localScale = Vector3.one;
                initialized = true;
            }

            if (image != null)
            {
                image.raycastTarget = true;
            }

            if (dragSelected)
            {
                transform.SetAsLastSibling();
            }
        }

        /// <summary>
        /// 更新卡牌自然排列时的基准位置和角度，避免布局动画和悬停动画互相拉扯。
        /// </summary>
        public void SetBaseLayout(Vector2 anchoredPosition, float rotation)
        {
            baseAnchoredPosition = anchoredPosition;
            baseRotation = rotation;
        }

        /// <summary>
        /// 鼠标进入卡牌区域的回调。
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
        }

        /// <summary>
        /// 鼠标离开卡牌区域的回调。
        /// </summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            pressed = false;
        }

        /// <summary>
        /// 鼠标按下卡牌的回调。
        /// </summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            pressed = true;
        }

        /// <summary>
        /// 鼠标释放卡牌的回调。
        /// </summary>
        public void OnPointerUp(PointerEventData eventData)
        {
            pressed = false;
        }

        /// <summary>
        /// 开始拖拽卡牌的回调。
        /// </summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!CanDrag())
            {
                return;
            }

            EnsureReferences();
            dragging = true;
            hovered = false;
            pressed = false;
            homeParent = transform.parent;
            homeIndex = transform.GetSiblingIndex();
            var skillSlot = FindSlotIndex(transform, "SkillCardSlot");
            if (skillSlot >= 0)
            {
                var controller = FindObjectOfType<BattleSceneController>();
                controller?.BeginSkillDrag(skillSlot);
            }

            SetCardRay(false);

            var dragLayer = EnsureDragLayer();
            if (dragLayer != null)
            {
                transform.SetParent(dragLayer, true);
            }

            transform.SetAsLastSibling();
        }

        /// <summary>
        /// 拖拽卡牌中的回调。
        /// </summary>
        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging)
            {
                return;
            }

            EnsureReferences();
            var scale = canvas != null ? canvas.scaleFactor : 1f;
            rectTransform.anchoredPosition += eventData.delta / Mathf.Max(0.01f, scale);
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one * 1.1f;
            NotifyDragBase(eventData);
        }

        /// <summary>
        /// 结束拖拽卡牌的回调。
        /// </summary>
        public void OnEndDrag(PointerEventData eventData)
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;
            pressed = false;
            var dropped = TryDropOnBaseCard(eventData);
            RestoreParent();
            if (dropped)
            {
                var controller = FindObjectOfType<BattleSceneController>();
                controller?.RefreshCards();
            }
            else
            {
                var controller = FindObjectOfType<BattleSceneController>();
                controller?.EndSkillDrag();
            }

            SetCardRay(true);
        }

        /// <summary>
        /// 组件唤醒时执行的初始化逻辑。
        /// </summary>
        private void Awake()
        {
            EnsureReferences();
        }

        /// <summary>
        /// 每帧更新逻辑。
        /// </summary>
        private void Update()
        {
            EnsureReferences();

            if (dragging)
            {
                return;
            }

            var liftDirection = opponentCard ? -1f : 1f;
            var targetPosition = baseAnchoredPosition;
            if (hovered)
            {
                targetPosition += new Vector2(0f, (skillCard ? 28f : 38f) * liftDirection);
            }

            if (dragSelected)
            {
                targetPosition += new Vector2(0f, (skillCard ? 42f : 64f) * liftDirection);
            }
            else if (selected)
            {
                targetPosition += new Vector2(0f, (skillCard ? 86f : 64f) * liftDirection);
            }

            var targetScale = dragSelected ? 1.06f : selected ? 1.12f : hovered ? 1.04f : 1f;
            if (pressed)
            {
                targetScale -= 0.04f;
            }

            var targetRotation = hovered || selected || dragSelected ? 0f : baseRotation;
            rectTransform.anchoredPosition = Vector2.Lerp(rectTransform.anchoredPosition, targetPosition, Time.unscaledDeltaTime * 14f);
            rectTransform.localRotation = Quaternion.Lerp(rectTransform.localRotation, Quaternion.Euler(0f, 0f, targetRotation), Time.unscaledDeltaTime * 14f);
            rectTransform.localScale = Vector3.Lerp(rectTransform.localScale, Vector3.one * targetScale, Time.unscaledDeltaTime * 14f);

            if (image != null)
            {
                var color = image.color;
                color.a = 1f;
                image.color = color;
            }
        }

        /// <summary>
        /// 确保所有 UI 引用已绑定。
        /// </summary>
        private void EnsureReferences()
        {
            if (rectTransform == null)
            {
                rectTransform = GetComponent<RectTransform>();
            }

            if (image == null)
            {
                image = GetComponent<Image>();
            }

            if (canvas == null)
            {
                canvas = GetComponentInParent<Canvas>();
            }
        }

        private void SetCardRay(bool enabled)
        {
            EnsureReferences();
            var graphics = GetComponentsInChildren<Graphic>(true);
            for (var i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] != null)
                {
                    graphics[i].raycastTarget = enabled;
                }
            }
            for (var i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child != null && child.name.Contains("_PrefabView") && child.TryGetComponent<Image>(out var childImage))
                {
                    childImage.raycastTarget = enabled;
                }
            }
        }

        /// <summary>
        /// 确保拖拽层级设置正确。
        /// </summary>
        private Transform EnsureDragLayer()
        {
            if (canvas == null)
            {
                return null;
            }

            var canvasTransform = canvas.transform;
            var found = canvasTransform.Find("BattleDragLayer");
            if (found != null)
            {
                found.SetAsLastSibling();
                return found;
            }

            var layer = new GameObject("BattleDragLayer", typeof(RectTransform));
            layer.layer = gameObject.layer;
            layer.transform.SetParent(canvasTransform, false);
            var rect = layer.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            layer.transform.SetAsLastSibling();
            return layer.transform;
        }

        /// <summary>
        /// 恢复卡牌到原始父级对象。
        /// </summary>
        private void RestoreParent()
        {
            if (homeParent == null)
            {
                return;
            }

            transform.SetParent(homeParent, true);
            var lastIndex = Mathf.Max(0, homeParent.childCount - 1);
            transform.SetSiblingIndex(Mathf.Clamp(homeIndex, 0, lastIndex));
            homeParent = null;
            homeIndex = 0;
        }

        /// <summary>
        /// 判断当前卡牌是否可拖拽。
        /// </summary>
        private bool CanDrag()
        {
            return skillCard && !opponentCard && FindSlotIndex(transform, "SkillCardSlot") >= 0;
        }

        /// <summary>
        /// 尝试将拖拽卡牌放置到目标基础牌。
        /// </summary>
        private bool TryDropOnBaseCard(PointerEventData eventData)
        {
            var resolvedBaseSlot = FindBaseSlot(eventData);
            if (resolvedBaseSlot >= 0)
            {
                var resolvedSkillSlot = FindSlotIndex(transform, "SkillCardSlot");
                if (resolvedSkillSlot < 0)
                {
                    return false;
                }

                var resolvedController = FindObjectOfType<BattleSceneController>();
                return resolvedController != null && resolvedController.DropSkillCardOnBase(resolvedSkillSlot, resolvedBaseSlot);
            }

            if (EventSystem.current == null)
            {
                return false;
            }

            RaycastResults.Clear();
            EventSystem.current.RaycastAll(eventData, RaycastResults);
            for (var i = 0; i < RaycastResults.Count; i++)
            {
                var target = RaycastResults[i].gameObject;
                while (target != null)
                {
                    if (target.name.StartsWith("BaseCardSlot", System.StringComparison.Ordinal) &&
                        ParseSlotIndex(target.name, "BaseCardSlot") < 0)
                    {
                        target = target.transform.parent != null ? target.transform.parent.gameObject : null;
                        continue;
                    }

                    if (target.name.StartsWith("BaseCardSlot", System.StringComparison.Ordinal))
                    {
                        var skillSlot = FindSlotIndex(transform, "SkillCardSlot");
                        var baseSlot = ParseSlotIndex(target.name, "BaseCardSlot");
                        if (skillSlot >= 0 && baseSlot >= 0)
                        {
                            var controller = FindObjectOfType<BattleSceneController>();
                            return controller != null && controller.DropSkillCardOnBase(skillSlot, baseSlot);
                        }
                        return false;
                    }

                    target = target.transform.parent != null ? target.transform.parent.gameObject : null;
                }
            }
            return false;
        }

        /// <summary>
        /// 通知基础牌当前有拖拽悬停。
        /// </summary>
        private void NotifyDragBase(PointerEventData eventData)
        {
            var controller = FindObjectOfType<BattleSceneController>();
            if (controller == null || EventSystem.current == null)
            {
                return;
            }

            controller.HoverBaseDuringDrag(FindBaseSlot(eventData));
        }

        /// <summary>
        /// 查找悬停位置对应的基础牌槽位。
        /// </summary>
        private static int FindBaseSlot(PointerEventData eventData)
        {
            if (EventSystem.current == null)
            {
                return -1;
            }

            RaycastResults.Clear();
            EventSystem.current.RaycastAll(eventData, RaycastResults);
            for (var i = 0; i < RaycastResults.Count; i++)
            {
                var target = RaycastResults[i].gameObject;
                while (target != null)
                {
                    if (target.name.StartsWith("BaseCardSlot", System.StringComparison.Ordinal) &&
                        ParseSlotIndex(target.name, "BaseCardSlot") < 0)
                    {
                        target = target.transform.parent != null ? target.transform.parent.gameObject : null;
                        continue;
                    }

                    if (target.name.StartsWith("BaseCardSlot", System.StringComparison.Ordinal))
                    {
                        /// <summary>
                        /// 从字符串中解析卡牌强化槽位的索引。
                        /// </summary>
                        return ParseSlotIndex(target.name, "BaseCardSlot");
                    }

                    target = target.transform.parent != null ? target.transform.parent.gameObject : null;
                }
            }

            var controller = FindObjectOfType<BattleSceneController>();
            return controller != null
                ? controller.FindPlayerBaseSlotAtScreenPosition(eventData.position, eventData.pressEventCamera)
                : -1;
        }

        /// <summary>
        /// 从字符串中解析槽位索引。
        /// </summary>
        private static int FindSlotIndex(Transform start, string prefix)
        {
            var current = start;
            while (current != null)
            {
                var index = ParseSlotIndex(current.name, prefix);
                if (index >= 0)
                {
                    return index;
                }

                current = current.parent;
            }

            return -1;
        }

        private static int ParseSlotIndex(string objectName, string prefix)
        {
            if (!objectName.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                return -1;
            }

            var suffix = objectName.Substring(prefix.Length);
            return int.TryParse(suffix, out var oneBasedIndex) ? oneBasedIndex - 1 : -1;
        }
    }
