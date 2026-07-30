using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

    /**
     * 可序列化执行的动画队列
     * 每个动画是一个异步任务，入队后自动开始串行执行，前一个完成后下一个才开始
     * 支持通过 CancellationToken 取消整个队列，取消时队列内剩余项会被清空
     */
    public sealed class AnimationQueue
    {
        // 存储待执行的异步动画委托，FIFO 顺序
        private readonly Queue<Func<CancellationToken, Task>> animations = new();

        // 标记当前是否正在 Drain 执行中，防止并发 Drain
        private bool isPlaying;

        /// <summary>
        /// 当前队列中等待执行的动画数量
        /// </summary>
        public int Count => animations.Count;

        /// <summary>
        /// 将一个动画异步操作入队
        /// </summary>
        /// <param name="animation">异步动画委托，接收取消指令，返回任务对象</param>
        public void Enqueue(Func<CancellationToken, Task> animation)
        {
            // 空委托直接忽略
            if (animation == null)
                return;

            animations.Enqueue(animation);

            // 没有 Drain 在执行时自动启动 Drain 循环
            if (!isPlaying)
                _ = DrainAsync(CancellationToken.None);
        }

        /// <summary>
        /// 串行消费队列中的所有动画，直到队列为空或被取消
        /// 已在 Drain 的调用会直接返回
        /// </summary>
        /// <param name="cancellationToken">用于取消剩余动画的指令</param>
        public async Task DrainAsync(CancellationToken cancellationToken)
        {
            // 防止多个 Drain 并发执行
            if (isPlaying)
                return;

            isPlaying = true;
            try
            {
                // 逐项消费队列
                while (animations.Count > 0)
                {
                    // 对外部取消请求作出响应
                    cancellationToken.ThrowIfCancellationRequested();

                    var animation = animations.Dequeue();
                    try
                    {
                        await animation(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // 动画自身或外部触发了取消 -> 清空剩余队列并向上传播取消信号
                        animations.Clear();
                        throw;
                    }
                    catch (Exception exception)
                    {
                        // 单个动画执行异常只记日志，不影响队列中其他动画的执行
                        Debug.LogException(exception);
                    }
                }
            }
            finally
            {
                // 无论正常完成、异常或取消，都释放 Drain 锁
                isPlaying = false;
            }
        }

        /// <summary>
        /// 清空队列中所有尚未执行的动画
        /// </summary>
        public void Clear()
        {
            animations.Clear();
        }
    }
