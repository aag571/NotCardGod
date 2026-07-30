using System;
using System.Collections.Generic;

    /// <summary>
    /// 提供轻量对象池，减少频繁创建和销毁带来的开销。
    /// </summary>
    public sealed class SimpleObjectPool<T> where T : class
    {
        // 当前缓存的未使用对象。
        private readonly Stack<T> cachedItems = new Stack<T>();

        // 对象池为空时用来创建新对象的工厂方法。
        private readonly Func<T> createFunc;

        // 对象被取出时执行的回调，可用于重置启用状态。
        private readonly Action<T> onGet;

        // 对象回到池中前执行的回调，可用于清理状态。
        private readonly Action<T> onRelease;

        // 池中最多保留的未使用对象数量。
        private readonly int maxSize;

        /// <summary>
        /// 创建一个对象池。
        /// </summary>
        /// <param name="createFunc">对象不足时的创建方法。</param>
        /// <param name="onGet">对象取出时执行的回调。</param>
        /// <param name="onRelease">对象回收时执行的回调。</param>
        /// <param name="maxSize">最多缓存多少个未使用对象。</param>
        public SimpleObjectPool(Func<T> createFunc, Action<T> onGet = null, Action<T> onRelease = null, int maxSize = 64)
        {
            this.createFunc = createFunc ?? throw new ArgumentNullException(nameof(createFunc));
            this.onGet = onGet;
            this.onRelease = onRelease;
            this.maxSize = Math.Max(1, maxSize);
        }

        // 当前池中空闲对象数量。
        public int CountInactive => cachedItems.Count;

        /// <summary>
        /// 处理 Get 对应的运行时流程。
        /// </summary>
        public T Get()
        {
            var item = cachedItems.Count > 0 ? cachedItems.Pop() : createFunc();
            onGet?.Invoke(item);
            return item;
        }

        /// <summary>
        /// 把对象放回池中。
        /// null 对象或超过容量的对象会被忽略。
        /// </summary>
        /// <param name="item">需要回收的对象。</param>
        public void Release(T item)
        {
            if (item == null || cachedItems.Count >= maxSize)
            {
                return;
            }

            onRelease?.Invoke(item);
            cachedItems.Push(item);
        }

        /// <summary>
        /// 处理 Clear 对应的运行时流程。
        /// </summary>
        public void Clear()
        {
            cachedItems.Clear();
        }
    }
