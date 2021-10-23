using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        监视器,
        本机
    }
    public class LogModelForWatcher: IEquatable<LogModelForWatcher>
    {
        /// <summary>
        /// 主机名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 日志类型
        /// </summary>
        public LogType Type { get; set; }
        /// <summary>
        /// 通信类型
        /// </summary>
        public WriteType wType { get; set; }
        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime LogTime { get; set; }
        /// <summary>
        /// 日志内容
        /// </summary>
        public string Description { get; set; }
        /// <summary>
        /// 是否语音告警
        /// </summary>
        public bool IsSpeak { get; set; }
        /// <summary>
        /// 是否侧边提示
        /// </summary>
        public bool ShowGrowl { get; set; }
        /// <summary>
        /// 是否检查日志重复
        /// </summary>
        public bool CheckDuplicate { get; set; } = true;

        public LogModelForWatcher()
        {
        }
        public LogModelForWatcher(LogType lt, WriteType wt, string description, bool isSpeak, bool showGrowl)
        {
            Type = lt; wType = wt; Description = description; IsSpeak = isSpeak; ShowGrowl = showGrowl;
        }
        public LogModelForWatcher(LogType lt, string description)
        {
            Type = lt; wType = WriteType.本机; Description = description; IsSpeak = false; ShowGrowl = true;
        }
        public LogModelForWatcher(LogType lt, string description, bool isSpeak)
        {
            Type = lt; wType = WriteType.本机; Description = description; IsSpeak = isSpeak; ShowGrowl = true;
        }
        public LogModelForWatcher(LogType lt, string description, bool isSpeak, bool showGrowl, bool checkDuplicate)
        {
            Type = lt; wType = WriteType.本机; Description = description; IsSpeak = isSpeak; ShowGrowl = showGrowl; CheckDuplicate = checkDuplicate;
        }
        public LogModelForWatcher(LogType lt, string description, bool isSpeak, bool showGrowl)
        {
            Type = lt; wType = WriteType.本机; Description = description; IsSpeak = isSpeak; ShowGrowl = showGrowl;
        }

        // 实际调用的Equals方法
        public bool Equals(LogModelForWatcher other)
        {
            if (other == null) return false;
            return Type == other.Type && wType == other.wType && Description == $"{other.wType}:{other.Description}" && IsSpeak == other.IsSpeak && ShowGrowl == other.ShowGrowl;
        }

        // 重写Equals方法
        public override bool Equals(object obj)
        {
            if (obj == null) return false;

            if (!(obj is LogModelForWatcher log))
                return false;
            else
                return Equals(log);
        }

        // 此方法必须一起重写
        public override int GetHashCode()
        {
            return Description.GetHashCode() ^ Type.GetHashCode();
        }
    }
}
