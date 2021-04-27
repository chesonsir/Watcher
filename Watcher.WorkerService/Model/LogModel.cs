using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Watcher.WorkerService.Model
{
    public enum LogType
    {
        信息,
        成功,
        警告,
        错误,
    }
    public enum WriteType
    {
        手动,
        FTP,
        实时数据,
        监视器
    }
    public class LogModel
    {
        /// <summary>
        /// 日志类型
        /// </summary>
        private LogType type;
        public LogType Type
        {
            get => type;
            set => type = value;
        }
        /// <summary>
        /// 通信类型
        /// </summary>
        private WriteType writeType;
        public WriteType wType
        {
            get => writeType;
            set => writeType = value;
        }
        /// <summary>
        /// 时间戳
        /// </summary>
        private DateTime logTime;
        public DateTime LogTime
        {
            get => logTime;
            set => logTime = value;
        }
        /// <summary>
        /// 日志内容
        /// </summary>
        private string description;
        public string Description
        {
            get => description;
            set => description = value;
        }
        /// <summary>
        /// 是否语音告警
        /// </summary>
        private bool isSpeak;
        public bool IsSpeak
        {
            get => isSpeak;
            set => isSpeak = value;
        }
        /// <summary>
        /// 是否侧边提示
        /// </summary>
        private bool showGrowl;
        public bool ShowGrowl
        {
            get => showGrowl;
            set => showGrowl = value;
        }
        public LogModel()
        {
        }
        public LogModel(LogType lt, WriteType wt, string description, bool isSpeak, bool showGrowl)
        {
            Type = lt; wType = wt; Description = description; IsSpeak = isSpeak; ShowGrowl = showGrowl;
        }
    }
}
