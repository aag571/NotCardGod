using System.Collections.Generic;
using System.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

    public sealed class Desk : MonoBehaviour
    {
        private const int ColMax = 5;
        private const int RowMax = 2;
        private const float ColGap = 86f;
        private const float RowGap = 112f;
        private static readonly Color DeskCardColor = new Color32(0x6E, 0x54, 0x38, 0xFF);

        private readonly List<List<RectTransform>> rows = new List<List<RectTransform>>();

        private RectTransform rt;
        private Transform layer;
        private readonly AnimationQueue animationQueue = new AnimationQueue();

        /// <summary>
        /// 初始化组件引用和运行时依赖。
        /// </summary>
        public void Init(Transform fxLayer)
        {
            rt = GetComponent<RectTransform>();
            layer = fxLayer != null ? fxLayer : transform;
        }

        /// <summary>
        /// 执行 Put 对应的运行时流程。
        /// </summary>
        public void Put(RectTransform src, string label, Color color, Vector2 size)
        {
            animationQueue.Enqueue(_ => PutAsync(src, label, color, size));
        }

        private async Task PutAsync(RectTransform src, string label, Color color, Vector2 size)
        {
            if (rt == null)
                Init(layer);
            if (rt == null)
                return;
            if (layer == null)
                layer = transform;
            

            var sequence = DOTween.Sequence();
            sequence.AppendInterval(0.08f);
            var completed = new TaskCompletionSource<bool>();
            sequence.OnComplete(() =>
            {
                completed.TrySetResult(true);
            });
            await completed.Task;
        }
    }