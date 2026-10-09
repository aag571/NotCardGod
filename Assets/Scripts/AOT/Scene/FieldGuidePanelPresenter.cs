using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class FieldGuidePanelPresenter : MonoBehaviour
{
    private readonly List<SkillCardBase> cards = new List<SkillCardBase>();
    private readonly List<Button> items = new List<Button>();

    private CardCatalog catalog;
    private UnlockProgressData progress;
    private RectTransform listContent;
    private RectTransform listViewport;
    private ScrollRect listScrollRect;
    private Button itemTemplate;
    private Text detailTitle;
    private Text detailBody;
    private Text previewText;
    private Color previewTextDefaultColor;
    private Image previewImage;
    private BaseCardType? typeFilter;
    private CardQuality? qualityFilter;
    private SkillCardBase selected;

    public void Initialize(CardCatalog sourceCatalog, UnlockProgressData unlockProgress)
    {
        catalog = sourceCatalog;
        progress = unlockProgress;
        listContent = FindComponent<RectTransform>("FieldGuideListContent");
        listViewport = FindComponent<RectTransform>("FieldGuideListViewport");
        listScrollRect = FindComponent<ScrollRect>("FieldGuideList");
        if (listScrollRect != null)
        {
            listScrollRect.movementType = ScrollRect.MovementType.Clamped;
            listScrollRect.elasticity = 0f;
            listScrollRect.inertia = false;
        }
        itemTemplate = FindComponent<Button>("GuideFilterAll");
        detailTitle = FindComponent<Text>("FieldGuideDetailTitle");
        detailBody = FindComponent<Text>("FieldGuideDetailBody");
        previewText = FindComponent<Text>("FieldGuideCardPreviewText");
        if (previewText != null)
            previewTextDefaultColor = previewText.color;
        previewImage = FindComponent<Image>("FieldGuideCardPreview");

        Bind("FieldGuideClose", Close);
        Bind("GuideFilterAll", () => SetFilter(null, null));
        Bind("GuideFilterAtk", () => SetFilter(BaseCardType.Attack, null));
        Bind("GuideFilterDef", () => SetFilter(BaseCardType.Defense, null));
        Bind("GuideFilterHeal", () => SetFilter(BaseCardType.Heal, null));
        Bind("GuideFilterSilver", () => SetFilter(null, CardQuality.Silver));
        Bind("GuideFilterGold", () => SetFilter(null, CardQuality.Gold));
        Bind("GuideFilterPlatinum", () => SetFilter(null, CardQuality.Platinum));
        Bind("GuideFilterPrismatic", () => SetFilter(null, CardQuality.Prismatic));
        Refresh();
    }

    public void Open(UnlockProgressData unlockProgress)
    {
        progress = unlockProgress;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        Refresh();
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    public void SetProgress(UnlockProgressData unlockProgress)
    {
        progress = unlockProgress;
        if (gameObject.activeInHierarchy)
            Refresh();
    }

    private void SetFilter(BaseCardType? type, CardQuality? quality)
    {
        typeFilter = type;
        qualityFilter = quality;
        selected = null;
        Refresh();
    }

    private void Refresh()
    {
        cards.Clear();
        if (catalog != null && catalog.cards != null)
        {
            for (var index = 0; index < catalog.cards.Count; index++)
            {
                if (!(catalog.cards[index] is SkillCardBase skill))
                    continue;
                if (typeFilter.HasValue && skill.SkillCardType != typeFilter.Value)
                    continue;
                if (qualityFilter.HasValue && skill.Quality != qualityFilter.Value)
                    continue;
                cards.Add(skill);
            }
        }

        cards.Sort((left, right) =>
        {
            var quality = left.Quality.CompareTo(right.Quality);
            if (quality != 0)
                return quality;
            var type = left.SkillCardType.CompareTo(right.SkillCardType);
            return type != 0 ? type : string.CompareOrdinal(left.DisplayName, right.DisplayName);
        });

        EnsureItems(cards.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var active = index < cards.Count;
            items[index].gameObject.SetActive(active);
            if (!active)
                continue;

            var skill = cards[index];
            var unlocked = IsUnlocked(skill);
            var itemRect = items[index].GetComponent<RectTransform>();
            itemRect.anchorMin = new Vector2(0f, 1f);
            itemRect.anchorMax = new Vector2(1f, 1f);
            itemRect.pivot = new Vector2(0.5f, 1f);
            itemRect.offsetMin = new Vector2(0f, -54f - index * 62f);
            itemRect.offsetMax = new Vector2(0f, -index * 62f);

            var label = items[index].GetComponentInChildren<Text>(true);
            SetQualityText(label,
                unlocked ? $"{TypeName(skill.SkillCardType)} · {skill.DisplayName}" : $"{TypeName(skill.SkillCardType)} · 未解锁",
                skill.Quality,
                unlocked);
            var image = items[index].GetComponent<Image>();
            if (image != null)
                image.color = GuideItemColor(skill == selected, skill.Quality);

            var captured = skill;
            items[index].onClick.RemoveAllListeners();
            items[index].onClick.AddListener(() => ShowCard(captured));
        }

        ResizeListContent();

        ShowCard(selected != null && cards.Contains(selected) ? selected : cards.Count > 0 ? cards[0] : null);
        RefreshFilterButtons();
    }

    private void ResizeListContent()
    {
        if (listContent == null)
            return;

        Canvas.ForceUpdateCanvases();
        var viewportHeight = listViewport != null ? listViewport.rect.height : 0f;
        var itemHeight = cards.Count * 62f + 8f;
        var contentHeight = Mathf.Max(viewportHeight, itemHeight);
        listContent.anchorMin = new Vector2(0f, 1f);
        listContent.anchorMax = new Vector2(1f, 1f);
        listContent.pivot = new Vector2(0.5f, 1f);
        listContent.anchoredPosition = Vector2.zero;
        listContent.sizeDelta = new Vector2(0f, contentHeight);

        if (listScrollRect == null)
            return;
        listScrollRect.StopMovement();
        listScrollRect.vertical = itemHeight > viewportHeight + 0.5f;
        listScrollRect.verticalNormalizedPosition = 1f;
    }

    private void EnsureItems(int count)
    {
        if (listContent == null || itemTemplate == null)
            return;
        while (items.Count < count)
        {
            var item = Instantiate(itemTemplate, listContent, false);
            item.name = $"MainGuideSkillItem{items.Count + 1}";
            items.Add(item);
        }
    }

    private void ShowCard(SkillCardBase skill)
    {
        selected = skill;
        RefreshSelectionColors();
        if (skill == null)
        {
            SetPlainText(detailTitle, "无可显示卡牌", Color.white);
            SetPlainText(previewText, string.Empty, previewTextDefaultColor);
            if (detailBody != null)
                detailBody.text = "当前筛选条件下没有卡牌";
            return;
        }

        if (!IsUnlocked(skill))
        {
            SetPlainText(detailTitle, "未解锁", Color.gray);
            SetPlainText(previewText, "未解锁", previewTextDefaultColor);
            if (detailBody != null)
                detailBody.text = "击败对应首领后解锁";
            if (previewImage != null)
                previewImage.color = Color.gray;
            return;
        }

        SetQualityText(detailTitle, skill.DisplayName, skill.Quality, true);
        SetPlainText(previewText, skill.DisplayName, previewTextDefaultColor);
        if (detailBody != null)
        {
            var stage = skill.CanUseOnPre ? "可在备战阶段使用" : "仅可在战斗阶段使用";
            var description = string.IsNullOrWhiteSpace(skill.Description) ? "暂无详细介绍" : skill.Description;
            detailBody.text = $"品质：{QualityName(skill.Quality)}\n类型：{TypeName(skill.SkillCardType)}强化\n强化数值：{skill.SkillPower:0.#}\n使用阶段：{stage}\n\n{description}";
        }
        if (previewImage != null)
            previewImage.color = Color.Lerp(Color.white, QualityColor(skill.Quality), 0.25f);
    }

    private void RefreshSelectionColors()
    {
        for (var index = 0; index < items.Count && index < cards.Count; index++)
        {
            if (!items[index].gameObject.activeSelf)
                continue;
            var image = items[index].GetComponent<Image>();
            if (image != null)
                image.color = GuideItemColor(cards[index] == selected, cards[index].Quality);
        }
    }

    private bool IsUnlocked(SkillCardBase skill)
    {
        return skill != null && PveCampaignService.IsSkillUnlocked(progress, skill.CardId);
    }

    private void RefreshFilterButtons()
    {
        SetFilterButton("GuideFilterAll", !typeFilter.HasValue && !qualityFilter.HasValue);
        SetFilterButton("GuideFilterAtk", typeFilter == BaseCardType.Attack);
        SetFilterButton("GuideFilterDef", typeFilter == BaseCardType.Defense);
        SetFilterButton("GuideFilterHeal", typeFilter == BaseCardType.Heal);
        SetFilterButton("GuideFilterSilver", qualityFilter == CardQuality.Silver);
        SetFilterButton("GuideFilterGold", qualityFilter == CardQuality.Gold);
        SetFilterButton("GuideFilterPlatinum", qualityFilter == CardQuality.Platinum);
        SetFilterButton("GuideFilterPrismatic", qualityFilter == CardQuality.Prismatic);
    }

    private void SetFilterButton(string objectName, bool active)
    {
        var image = FindComponent<Image>(objectName);
        if (image != null)
            image.color = active ? new Color32(78, 103, 142, 255) : new Color32(46, 56, 72, 255);
    }

    private void Bind(string objectName, UnityEngine.Events.UnityAction action)
    {
        var button = FindComponent<Button>(objectName);
        if (button == null)
            return;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    private T FindComponent<T>(string objectName) where T : Component
    {
        var transforms = GetComponentsInChildren<Transform>(true);
        for (var index = 0; index < transforms.Length; index++)
        {
            if (transforms[index].name == objectName)
                return transforms[index].GetComponent<T>();
        }
        return null;
    }

    private static void SetQualityText(Text text, string value, CardQuality quality, bool unlocked)
    {
        if (text == null)
            return;
        text.supportRichText = true;
        if (!unlocked)
        {
            SetPlainText(text, value, Color.gray);
            return;
        }

        text.color = quality == CardQuality.Prismatic ? Color.white : QualityColor(quality);
        text.text = quality == CardQuality.Prismatic
            ? $"<color=#FF00FF>{EscapeRichText(value)}</color>"
            : value;
        if (quality == CardQuality.Prismatic && text.GetComponent<PrismaticTextGradientEffect>() == null)
            text.gameObject.AddComponent<PrismaticTextGradientEffect>();
    }

    private static void SetPlainText(Text text, string value, Color color)
    {
        if (text == null)
            return;
        text.text = value ?? string.Empty;
        text.color = color;
    }

    private static string EscapeRichText(string value)
    {
        return (value ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static Color QualityColor(CardQuality quality)
    {
        switch (quality)
        {
            case CardQuality.Silver: return new Color32(201, 210, 220, 255);
            case CardQuality.Gold: return new Color32(255, 211, 78, 255);
            case CardQuality.Platinum: return new Color32(183, 227, 161, 255);
            case CardQuality.Prismatic: return Color.white;
            default: return Color.white;
        }
    }

    private static Color GuideItemColor(bool selected, CardQuality quality)
    {
        if (quality == CardQuality.Prismatic)
            return selected ? new Color32(125, 63, 178, 255) : new Color32(84, 42, 120, 255);
        return Color.Lerp(new Color32(24, 29, 38, 255), QualityColor(quality), selected ? 0.55f : 0.2f);
    }

    private static string QualityName(CardQuality quality)
    {
        switch (quality)
        {
            case CardQuality.Silver: return "白银";
            case CardQuality.Gold: return "黄金";
            case CardQuality.Platinum: return "白金";
            case CardQuality.Prismatic: return "棱彩";
            default: return "普通";
        }
    }

    private static string TypeName(BaseCardType type)
    {
        switch (type)
        {
            case BaseCardType.Attack: return "攻击";
            case BaseCardType.Defense: return "防御";
            case BaseCardType.Heal: return "治疗";
            default: return "卡牌";
        }
    }
}
