using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Watcher.WorkerService.Model
{
    /// <summary>
    /// 状态指示类
    /// </summary>
    public class CountAndState
    {
        public CountAndState(int count, string state, int alarmValue, string info = "")
        {
            this.Count = count;
            this.State = state;
            this.AlarmValue = alarmValue;
            this.Info = info;
        }
        /// <summary>
        /// 状态计数
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// 状态指示
        /// </summary>
        public string State { get; set; }

        /// <summary>
        /// 告警门限
        /// </summary>
        public int AlarmValue { get; set; }

        /// <summary>
        /// 告警信息
        /// </summary>
        public string Info { get; set; }
    }
}
