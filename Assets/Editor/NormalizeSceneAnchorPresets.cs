using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class NormalizeSceneAnchorPresets
{
    // Scene-safe, repeatable anchor normalization entry point.
    private const string RequestPath = "Library/NormalizeSceneAnchors.request";
    private const string ReportPath = "Library/NormalizeSceneAnchors.report.txt";
    private const string RemoveBattleGuideRequestPath = "Library/RemoveBattleStaticFieldGuide.request";
    private const string RemoveBattleGuideReportPath = "Library/RemoveBattleStaticFieldGuide.report.txt";
    private const string BuildBattleCurtainRequestPath = "Library/BuildBattleLoadingCurtain.request";
    private const string BuildBattleCurtainReportPath = "Library/BuildBattleLoadingCurtain.report.txt";

    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/Boot.unity",
        "Assets/Scenes/Loding.unity",
        "Assets/Scenes/Splash.unity",
        "Assets/Scenes/Main.unity",
        "Assets/Scenes/Battle.unity"
    };

    static NormalizeSceneAnchorPresets()
    {
        EditorApplication.delayCall += RunRequestedNormalization;
        EditorApplication.delayCall += RunRequestedBattleGuideRemoval;
        EditorApplication.delayCall += RunRequestedBattleCurtainBuild;
    }

    [MenuItem("Tools/NotCardGod/UI/Normalize All Scene Anchor Presets")]
    public static void NormalizeAllScenes()
    {
        var originalSetup = EditorSceneManager.GetSceneManagerSetup();
        var normalizedCount = 0;
        var aspectCount = 0;

        try
        {
            for (var sceneIndex = 0; sceneIndex < ScenePaths.Length; sceneIndex++)
            {
                var scenePath = ScenePaths[sceneIndex];
                if (!File.Exists(scenePath))
                    continue;

                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var roots = scene.GetRootGameObjects();
                for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                {
                    var rects = roots[rootIndex].GetComponentsInChildren<RectTransform>(true);
                    for (var rectIndex = 0; rectIndex < rects.Length; rectIndex++)
                    {
                        var rect = rects[rectIndex];
                        if (rect.parent is RectTransform)
                        {
                            NormalizeRectTransform(rect);
                            normalizedCount++;
                        }

                        if (ProtectImageAspect(rect.gameObject))
                            aspectCount++;
                    }
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
            EditorSceneManager.SaveOpenScenes();
        }

        File.WriteAllText(ReportPath,
            $"normalizedRectTransforms={normalizedCount}{Environment.NewLine}" +
            $"protectedAspectImages={aspectCount}{Environment.NewLine}" +
            $"completedUtc={DateTime.UtcNow:O}{Environment.NewLine}");
        AssetDatabase.SaveAssets();
        Debug.Log($"[UI Anchor Presets] 完成：RectTransform={normalizedCount}, PreserveAspect={aspectCount}");
    }

    private static void RunRequestedNormalization()
    {
        if (!File.Exists(RequestPath))
            return;

        File.Delete(RequestPath);
        NormalizeAllScenes();
    }

    private static void RunRequestedBattleGuideRemoval()
    {
        if (!File.Exists(RemoveBattleGuideRequestPath))
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        File.Delete(RemoveBattleGuideRequestPath);
        var originalSetup = EditorSceneManager.GetSceneManagerSetup();
        var removed = 0;
        var clearedGameAssetSprites = 0;
        try
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity", OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var images = roots[rootIndex].GetComponentsInChildren<Image>(true);
                for (var imageIndex = 0; imageIndex < images.Length; imageIndex++)
                {
                    var image = images[imageIndex];
                    if (image.sprite == null)
                        continue;
                    var spritePath = AssetDatabase.GetAssetPath(image.sprite);
                    if (!spritePath.StartsWith("Assets/GameAssets/", StringComparison.OrdinalIgnoreCase))
                        continue;
                    image.sprite = null;
                    EditorUtility.SetDirty(image);
                    clearedGameAssetSprites++;
                }
                var transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (var transformIndex = transforms.Length - 1; transformIndex >= 0; transformIndex--)
                {
                    var target = transforms[transformIndex].gameObject;
                    if (!string.Equals(target.name, "FieldGuidePanel", StringComparison.Ordinal))
                        continue;
                    UnityEngine.Object.DestroyImmediate(target);
                    removed++;
                }
            }
            if (removed > 0 || clearedGameAssetSprites > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
        }

        File.WriteAllText(RemoveBattleGuideReportPath,
            $"removedFieldGuideInstances={removed}{Environment.NewLine}" +
            $"clearedGameAssetSprites={clearedGameAssetSprites}{Environment.NewLine}" +
            $"completedUtc={DateTime.UtcNow:O}{Environment.NewLine}");
        AssetDatabase.SaveAssets();
    }

    private static void RunRequestedBattleCurtainBuild()
    {
        if (!File.Exists(BuildBattleCurtainRequestPath) || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        File.Delete(BuildBattleCurtainRequestPath);
        var originalSetup = EditorSceneManager.GetSceneManagerSetup();
        var created = false;
        try
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity", OpenSceneMode.Single);
            var canvas = GameObject.Find("BattleCanvas");
            if (canvas == null)
                throw new InvalidOperationException("BattleCanvas was not found.");
            var curtain = GameObject.Find("BattleLoadingCurtain");
            if (curtain == null)
            {
                curtain = new GameObject("BattleLoadingCurtain", typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(Image), typeof(CanvasGroup));
                curtain.transform.SetParent(canvas.transform, false);
                created = true;
            }

            var rect = curtain.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = curtain.GetComponent<Image>();
            image.sprite = null;
            image.color = Color.black;
            image.raycastTarget = true;
            var group = curtain.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
            curtain.SetActive(true);
            curtain.transform.SetAsLastSibling();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
        }
        File.WriteAllText(BuildBattleCurtainReportPath,
            $"created={created}{Environment.NewLine}completedUtc={DateTime.UtcNow:O}{Environment.NewLine}");
        AssetDatabase.SaveAssets();
    }

    private static void NormalizeRectTransform(RectTransform rect)
    {
        var oldAnchorMin = rect.anchorMin;
        var oldAnchorMax = rect.anchorMax;
        var newAnchorMin = oldAnchorMin;
        var newAnchorMax = oldAnchorMax;
        var objectName = rect.gameObject.name;

        if (IsFullStretch(objectName))
        {
            newAnchorMin = Vector2.zero;
            newAnchorMax = Vector2.one;
        }
        else if (IsCenteredModal(objectName))
        {
            newAnchorMin = new Vector2(0.5f, 0.5f);
            newAnchorMax = newAnchorMin;
        }
        else
        {
            ResolveAxis(oldAnchorMin.x, oldAnchorMax.x, objectName, true,
                out newAnchorMin.x, out newAnchorMax.x);
            ResolveAxis(oldAnchorMin.y, oldAnchorMax.y, objectName, false,
                out newAnchorMin.y, out newAnchorMax.y);
        }

        if (oldAnchorMin == newAnchorMin && oldAnchorMax == newAnchorMax)
            return;

        var parent = (RectTransform)rect.parent;
        var parentSize = parent.rect.size;
        var oldOffsetMin = rect.offsetMin;
        var oldOffsetMax = rect.offsetMax;
        var lowerLeft = Vector2.Scale(oldAnchorMin, parentSize) + oldOffsetMin;
        var upperRight = Vector2.Scale(oldAnchorMax, parentSize) + oldOffsetMax;

        Undo.RecordObject(rect, "Normalize Anchor Preset");
        rect.anchorMin = newAnchorMin;
        rect.anchorMax = newAnchorMax;
        rect.offsetMin = lowerLeft - Vector2.Scale(newAnchorMin, parentSize);
        rect.offsetMax = upperRight - Vector2.Scale(newAnchorMax, parentSize);
        EditorUtility.SetDirty(rect);
    }

    private static void ResolveAxis(
        float oldMin,
        float oldMax,
        string objectName,
        bool horizontal,
        out float newMin,
        out float newMax)
    {
        var span = oldMax - oldMin;
        var center = (oldMin + oldMax) * 0.5f;
        var shouldStretch = span >= 0.65f ||
                            horizontal && IsHorizontalBar(objectName) ||
                            !horizontal && IsVerticalRegion(objectName);

        if (shouldStretch)
        {
            newMin = 0f;
            newMax = 1f;
            return;
        }

        var fixedAnchor = center < 0.33f ? 0f : center > 0.67f ? 1f : 0.5f;
        newMin = fixedAnchor;
        newMax = fixedAnchor;
    }

    private static bool IsFullStretch(string objectName)
    {
        return ContainsAny(objectName,
            "Background", "Mask", "Overlay", "SafeArea", "Viewport", "Content", "Fill Area");
    }

    private static bool IsCenteredModal(string objectName)
    {
        if (!objectName.EndsWith("Panel", StringComparison.OrdinalIgnoreCase))
            return false;

        return !ContainsAny(objectName,
            "BaseHand", "SkillHand", "Battlefield", "Result", "Explain", "Player", "Opponent");
    }

    private static bool IsHorizontalBar(string objectName)
    {
        return ContainsAny(objectName,
            "TopBar", "BottomBar", "Header", "Footer", "Progress", "Slider", "HandPanel", "ButtonGroup");
    }

    private static bool IsVerticalRegion(string objectName)
    {
        return ContainsAny(objectName, "Scroll", "ListContent");
    }

    private static bool ProtectImageAspect(GameObject target)
    {
        var image = target.GetComponent<Image>();
        if (image == null || image.sprite == null || image.preserveAspect)
            return false;

        if (!ContainsAny(target.name,
                "Card", "Avatar", "Photo", "Portrait", "Icon", "Logo", "Preview", "Gem", "Shield", "sheild"))
            return false;

        Undo.RecordObject(image, "Protect UI Image Aspect");
        image.preserveAspect = true;
        EditorUtility.SetDirty(image);
        return true;
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        for (var index = 0; index < tokens.Length; index++)
        {
            if (value.IndexOf(tokens[index], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }
}
