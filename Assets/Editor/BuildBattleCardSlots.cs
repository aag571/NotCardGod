using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class BuildBattleCardSlots
{
    [MenuItem("Tools/Battle/Build Card Slots")]
    public static void Build()
    {
        if (EditorSceneManager.GetActiveScene().name != "Battle")
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity");
        }

        var controller = FindSceneObject("BattleSceneController");
        if (controller == null)
        {
            Debug.LogError("BattleSceneController was not found in the Battle scene.");
            return;
        }

        var baseHand = FindSceneObject("BaseHandPanel");
        var skillHand = FindSceneObject("SkillHandPanel");
        var opponentBaseHand = FindSceneObject("OpponentBaseHandPanel");
        var opponentSkillHand = FindSceneObject("OpponentSkillHandPanel");
        var baseSlots = BuildSlots(baseHand.transform, "BaseCardSlot", 10);
        var skillSlots = BuildSlots(skillHand.transform, "SkillCardSlot", 12);
        var opponentBaseSlots = BuildSlots(opponentBaseHand.transform, "OpponentBaseCardSlot", 10);
        var opponentSkillSlots = BuildSlots(opponentSkillHand.transform, "OpponentSkillCardSlot", 12);

        var serialized = new SerializedObject(controller.GetComponent<MonoBehaviour>());
        SetArray(serialized, "baseCardSlots", baseSlots);
        SetArray(serialized, "skillCardSlots", skillSlots);
        SetArray(serialized, "opponentBaseCardSlots", opponentBaseSlots);
        SetArray(serialized, "opponentSkillCardSlots", opponentSkillSlots);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(controller.scene);
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
    }

    private static GameObject[] BuildSlots(Transform parent, string prefix, int count)
    {
        var slots = new GameObject[count];
        for (var i = 0; i < count; i++)
        {
            var name = $"{prefix}{i + 1}";
            var slot = parent.Find(name);
            if (slot == null)
            {
                var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                obj.layer = 5;
                obj.transform.SetParent(parent, false);
                slot = obj.transform;
            }

            var rect = slot.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(150f, 210f);
            rect.anchoredPosition = Vector2.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;

            var image = slot.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;
            var button = slot.GetComponent<Button>();
            button.targetGraphic = image;
            slots[i] = slot.gameObject;
        }

        return slots;
    }

    private static void SetArray(SerializedObject serialized, string name, GameObject[] values)
    {
        var property = serialized.FindProperty(name);
        property.arraySize = values.Length;
        for (var i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
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
}
