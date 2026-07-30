using System;

    /// <summary>
    /// 提供 Unix 时间戳和本地日期判断工具。
    /// </summary>
    public static class UnixTimeUtility
    {
        // Unix 时间戳的 UTC 起点，所有毫秒换算都以它为基准。
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// 处理 NowMilliseconds 对应的运行时流程。
        /// </summary>
        public static long NowMilliseconds()
        {
            return ToMilliseconds(DateTime.UtcNow);
        }

        /// <summary>
        /// 把指定时间转换为 Unix 毫秒时间戳。
        /// </summary>
        /// <param name="utcTime">需要转换的时间；如果不是 UTC，会先转换为 UTC。</param>
        public static long ToMilliseconds(DateTime utcTime)
        {
            return (long)(utcTime.ToUniversalTime() - UnixEpoch).TotalMilliseconds;
        }

        /// <summary>
        /// 把 Unix 毫秒时间戳转换回 UTC 时间。
        /// </summary>
        /// <param name="unixMilliseconds">从 1970-01-01 UTC 到目标时间经过的毫秒数。</param>
        public static DateTime FromMilliseconds(long unixMilliseconds)
        {
            return UnixEpoch.AddMilliseconds(unixMilliseconds);
        }
    }
