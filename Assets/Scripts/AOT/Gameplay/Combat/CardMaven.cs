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
            GameAudioService.Instance?.PlaySfx(GameSfx.Card);
            await DealAsync(dst, label, color, size, sprite, onComplete);
        });
    }

    private async Task DealAsync(RectTransform dst, string label, Color color, Vector2 size, Sprite sprite, Action onComplete)
    {
        if (layer == null)
            Init(transform);

        dst.DOKill();
        dst.localScale = new Vector3(0.82f, 0.82f, 1f);
        onComplete?.Invoke();
        var sequence = DOTween.Sequence();
        var completed = new TaskCompletionSource<bool>();
        sequence.Append(dst.DOScale(Vector3.one, 0.2f).SetEase(Ease.OutBack));
        sequence.OnComplete(() =>
        {
            completed.TrySetResult(true);
        });
        sequence.OnKill(() => completed.TrySetResult(false));
        await completed.Task;
    }
}
