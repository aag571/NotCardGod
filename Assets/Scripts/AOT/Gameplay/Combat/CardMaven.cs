using System;
using System.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

public sealed class CardMaven : MonoBehaviour
{
    private RectTransform rt;
    private Transform layer;
    private readonly AnimationQueue animationQueue = new AnimationQueue();

    public void Init(Transform fxLayer)
    {
        rt = GetComponent<RectTransform>();
        layer = fxLayer != null ? fxLayer : transform;
    }

    public void Deal(RectTransform dst, string label, Color color, Vector2 size, Sprite sprite, Action onComplete = null)
    {
        if (dst == null)
            return;

        animationQueue.Enqueue(async _ =>
        {
            await DealAsync(dst, label, color, size, sprite);
            onComplete?.Invoke();
        });
    }

    private async Task DealAsync(RectTransform dst, string label, Color color, Vector2 size, Sprite sprite)
    {
        if (layer == null)
            Init(transform);

        var fx = CardFx.Make(layer, "DealFx", label, color, size, sprite);
        fx.rt.position = rt != null ? rt.position : transform.position;
        fx.rt.localRotation = Quaternion.identity;
        fx.rt.localScale = Vector3.one * 0.36f;
        fx.rt.SetAsLastSibling();
        fx.SetAlpha(1f);

        var sequence = DOTween.Sequence();
        sequence.Join(fx.rt.DOMove(dst.position, 0.2f).SetEase(Ease.OutCubic));
        sequence.Join(fx.rt.DOScale(1f, 0.2f).SetEase(Ease.OutBack));
        var completed = new TaskCompletionSource<bool>();
        sequence.OnComplete(() =>
        {
            CardFx.Release(fx);
            completed.TrySetResult(true);
        });
        await completed.Task;
    }
}
