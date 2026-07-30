using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class BuildBattleFieldGuidePanel
{
    private static readonly Color PanelColor = new Color(0.04f, 0.05f, 0.07f, 0.97f);
    private static readonly Color ListColor = new Color(0.08f, 0.10f, 0.13f, 0.95f);

    [MenuItem("Tools/Battle/Build Field Guide Panel")]
    public static void Build()
    {
        if (EditorSceneManager.GetActiveScene().name != "Battle")
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity");
        }

        var panel = FindSceneObject("FieldGuidePanel");
        var controller = FindSceneObject("BattleSceneController");
        if (panel == null || controller == null)
        {
            Debug.LogError("FieldGuidePanel or BattleSceneController was not found in the open scene.");
            return;
        }

        ClearChildren(panel.transform);
        SetRect(panel.GetComponent<RectTransform>(), new Vector2(0.12f, 0.08f), new Vector2(0.88f, 0.92f));
        panel.GetComponent<Image>().color = PanelColor;

        var title = CreateText(panel.transform, "FieldGuideTitleText", "强化卡牌图鉴", new Vector2(0.04f, 0.91f), new Vector2(0.78f, 0.98f), 26, TextAnchor.MiddleLeft, Color.white);
        var close = CreateButton(panel.transform, "fieldGuideClose", "关闭", new Vector2(0.84f, 0.92f), new Vector2(0.97f, 0.98f));

        var filters = new[]
        {
            new Filter("GuideFilterAll", "全部", new Vector2(0.04f, 0.84f), new Vector2(0.13f, 0.89f)),
            new Filter("GuideFilterAtk", "攻击", new Vector2(0.14f, 0.84f), new Vector2(0.23f, 0.89f)),
            new Filter("GuideFilterDef", "防御", new Vector2(0.24f, 0.84f), new Vector2(0.33f, 0.89f)),
            new Filter("GuideFilterHeal", "治疗", new Vector2(0.34f, 0.84f), new Vector2(0.43f, 0.89f)),
            new Filter("GuideFilterSilver", "白银", new Vector2(0.48f, 0.84f), new Vector2(0.57f, 0.89f)),
            new Filter("GuideFilterGold", "黄金", new Vector2(0.58f, 0.84f), new Vector2(0.67f, 0.89f)),
            new Filter("GuideFilterPlatinum", "白金", new Vector2(0.68f, 0.84f), new Vector2(0.78f, 0.89f)),
            new Filter("GuideFilterPrismatic", "棱彩", new Vector2(0.79f, 0.84f), new Vector2(0.89f, 0.89f))
        };
        foreach (var filter in filters)
        {
            CreateButton(panel.transform, filter.Name, filter.Label, filter.Min, filter.Max);
        }

        var list = CreatePanel(panel.transform, "FieldGuideList", new Vector2(0.04f, 0.08f), new Vector2(0.42f, 0.80f), ListColor);
        var scroll = list.AddComponent<ScrollRect>();
        var viewport = CreatePanel(list.transform, "FieldGuideListViewport", Vector2.zero, Vector2.one, Color.clear);
        viewport.GetComponent<Image>().raycastTarget = true;
        var mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        var content = CreateRect(viewport.transform, "FieldGuideListContent", new Vector2(0f, 1f), new Vector2(1f, 1f));
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = content.GetComponent<RectTransform>();
        scroll.horizontal = false;
        scroll.vertical = true;

        var preview = CreatePanel(panel.transform, "FieldGuideCardPreview", new Vector2(0.47f, 0.48f), new Vector2(0.68f, 0.78f), new Color(0.12f, 0.14f, 0.18f, 1f));
        var previewText = CreateText(panel.transform, "FieldGuideCardPreviewText", string.Empty, new Vector2(0.47f, 0.42f), new Vector2(0.68f, 0.47f), 18, TextAnchor.MiddleCenter, Color.white);
        var detailTitle = CreateText(panel.transform, "FieldGuideDetailTitle", string.Empty, new Vector2(0.71f, 0.72f), new Vector2(0.96f, 0.80f), 24, TextAnchor.MiddleLeft, Color.white);
        var detailBody = CreateText(panel.transform, "FieldGuideDetailBody", string.Empty, new Vector2(0.47f, 0.08f), new Vector2(0.96f, 0.40f), 18, TextAnchor.UpperLeft, new Color(0.86f, 0.9f, 0.94f, 1f));

        var serialized = new SerializedObject(controller.GetComponent<MonoBehaviour>());
        SetReference(serialized, "fieldGuidePanel", panel);
        SetReference(serialized, "fieldGuideClose", close.gameObject);
        SetReference(serialized, "guideFilterAll", panel.transform.Find("GuideFilterAll").gameObject);
        SetReference(serialized, "guideFilterAtk", panel.transform.Find("GuideFilterAtk").gameObject);
        SetReference(serialized, "guideFilterDef", panel.transform.Find("GuideFilterDef").gameObject);
        SetReference(serialized, "guideFilterHeal", panel.transform.Find("GuideFilterHeal").gameObject);
        SetReference(serialized, "guideFilterSilver", panel.transform.Find("GuideFilterSilver").gameObject);
        SetReference(serialized, "guideFilterGold", panel.transform.Find("GuideFilterGold").gameObject);
        SetReference(serialized, "guideFilterPlatinum", panel.transform.Find("GuideFilterPlatinum").gameObject);
        SetReference(serialized, "guideFilterPrismatic", panel.transform.Find("GuideFilterPrismatic").gameObject);
        SetReference(serialized, "fieldGuideCardPreview", preview);
        SetReference(serialized, "fieldGuideList", list);
        SetReference(serialized, "fieldGuideListViewport", viewport);
        SetReference(serialized, "fieldGuideListContent", content);
        SetReference(serialized, "fieldGuideCardPreviewText", previewText.gameObject);
        SetReference(serialized, "fieldGuideDetailTitle", detailTitle.gameObject);
        SetReference(serialized, "fieldGuideDetailBody", detailBody.gameObject);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        panel.SetActive(false);
        EditorSceneManager.MarkSceneDirty(panel.scene);
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
    }

    private static void SetReference(SerializedObject serialized, string name, Object value)
    {
        serialized.FindProperty(name).objectReferenceValue = value;
    }

    private static GameObject FindSceneObject(string name)
    {
        var objects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (var i = 0; i < objects.Length; i++)
        {
            if (objects[i].name == name && objects[i].scene.IsValid())
            {
                return objects[i];
            }
        }

        return null;
    }

    private static GameObject CreatePanel(Transform parent, string name, Vector2 min, Vector2 max, Color color)
    {
        var obj = CreateRect(parent, name, min, max);
        var image = obj.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
        return obj;
    }

    private static Button CreateButton(Transform parent, string name, string label, Vector2 min, Vector2 max)
    {
        var obj = CreatePanel(parent, name, min, max, new Color(0.18f, 0.22f, 0.28f, 1f));
        var button = obj.AddComponent<Button>();
        button.targetGraphic = obj.GetComponent<Image>();
        CreateText(obj.transform, name + "_Label", label, new Vector2(0.08f, 0.10f), new Vector2(0.92f, 0.90f), 18, TextAnchor.MiddleCenter, Color.white);
        return button;
    }

    private static Text CreateText(Transform parent, string name, string value, Vector2 min, Vector2 max, int size, TextAnchor alignment, Color color)
    {
        var obj = CreateRect(parent, name, min, max);
        var text = obj.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 10;
        text.resizeTextMaxSize = size;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject CreateRect(Transform parent, string name, Vector2 min, Vector2 max)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        SetRect(obj.GetComponent<RectTransform>(), min, max);
        return obj;
    }

    private static void SetRect(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
    }

    private static void ClearChildren(Transform parent)
    {
        while (parent.childCount > 0)
        {
            Object.DestroyImmediate(parent.GetChild(0).gameObject);
        }
    }

    private readonly struct Filter
    {
        public readonly string Name;
        public readonly string Label;
        public readonly Vector2 Min;
        public readonly Vector2 Max;

        public Filter(string name, string label, Vector2 min, Vector2 max)
        {
            Name = name;
            Label = label;
            Min = min;
            Max = max;
        }
    }
}
