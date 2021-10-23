using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Watcher.WorkerService.Helper
{
    public sealed class ConfigHelper
    {
        /// <summary>
        /// 懒汉单例模式 https://www.cnblogs.com/zhaoshujie/p/14323654.html
        /// </summary>
        private static readonly Lazy<ConfigHelper> lazy = new (() => new ConfigHelper());
        public static ConfigHelper Instance { get => lazy.Value; }
        private ConfigHelper() { }
        /// <summary>
        /// 内部对象
        /// </summary>
        private ConfigModel CM { get; set; }
        /// <summary>
        /// 加载配置文件
        /// </summary>
        public ConfigModel LoadConfig()
        {
            if (!File.Exists(@$"{AppDomain.CurrentDomain.BaseDirectory}\Config.json"))
            {
                //没有的话自己创建
                CM = new ConfigModel()
                {
                    HostName = "",
                    SpeechEnable = true,
                    MonitoredProcesses = new List<ProcessModel>()
                    {
                        new ProcessModel()
                        {
                            Name = "",
                            AlarmValue = 5
                        }
                    },
                    MonitoredIPs = new List<NetHostModel>() 
                    { 
                        new NetHostModel()
                        {
                            RemoteName = "中心",
                            IP = "31.25.49.170",
                            AlarmValue = 5
                        }
                    },
                    MonitoredPathes = new List<UploadPathModel>() 
                    { 
                        new UploadPathModel()
                        {
                            ParentPath = "",
                            SubPathFormat = "yyyyMMdd",
                            UpPathList = new List<ChildrenPathModel>()
                            {
                                new ChildrenPathModel()
                                {
                                    UploadPath = "",
                                    UserName = "",
                                    PassWord = "",
                                    Homonym = "忽略"
                                },
                            },
                            UpNow = false,
                            Timed = "",
                            CompressFormat = "zip",
                            IsCompressed = true,
                            IsDeleteOldFiles = true,
                            ReserveDays = 3,
                            TimeBase="文件名",
                            FileFilter = ".txt|.gae|.gdj|.gtw|.dox|.docx|.xls|.xlsx|.jhf|.jhfnew|.tle|.pdf|.ppt|.pptx|.rar|.zip|.7z|.jpg|.png|.bmp|.ico|.gif|.fig|.m|.avi|.flv|.xmind"
                        }
                    },
                    MutiCastIP = new MutiCastIPModel()
                    {
                        IP = "228.8.8.8",
                        Port = "9000"
                    },
                };
                //写配置文件
                var configJson = JsonConvert.SerializeObject(CM, Formatting.Indented);
                File.WriteAllText(@$"{AppDomain.CurrentDomain.BaseDirectory}\Config.json", configJson);
            }
            else
            {
                try
                {
                    var configJson = File.ReadAllText(@$"{AppDomain.CurrentDomain.BaseDirectory}\Config.json");
                    CM = JsonConvert.DeserializeObject<ConfigModel>(configJson);
#if TRACE
                    LogHelper.Debug(configJson);
#endif
                }
                catch
                {
                    LogHelper.Fatal("配置文件Json格式错误！");
                    return null;
                }
            }
            //对配置文件内容进行单项检查
            Utils.CheckConfigModel(CM);
            return CM;
        }
    }

    /// <summary>
    /// 配置文件模型
    /// </summary>
    public class ConfigModel
    {
        /// <summary>
        /// 运行监视器的主机名称
        /// </summary>
        [JsonProperty("本机名称")] public string HostName { get; set; }
        /// <summary>
        /// 语音提示
        /// </summary>
        [JsonProperty("语音提示")] public bool SpeechEnable { get; set; }
        /// <summary>
        /// 被监视的进程名称的列表
        /// </summary>
        [JsonProperty("监视进程")] public List<ProcessModel> MonitoredProcesses { get; set; }
        /// <summary>
        /// 被监视的IP地址列表
        /// </summary>
        [JsonProperty("监视通信")] public List<NetHostModel> MonitoredIPs { get; set; }
        /// <summary>
        /// 被监视的路径列表
        /// </summary>
        [JsonProperty("监视路径")] public List<UploadPathModel> MonitoredPathes { get; set; }
        /// <summary>
        /// 组播地址
        /// </summary>
        [JsonProperty("消息组播地址")] public MutiCastIPModel MutiCastIP { get; set; }
    }

    /// <summary>
    /// 被监视的进程模型
    /// </summary>
    public class ProcessModel
    {
        /// <summary>
        /// 被监视的进程名称
        /// </summary>
        [JsonProperty("进程名称")] public string Name { get; set; }
        /// <summary>
        /// 告警门限
        /// </summary>
        [JsonProperty("告警门限")] public int AlarmValue { get; set; }
        /// <summary>
        /// 配置是否有效
        /// </summary>
        [JsonIgnore] public bool IsVaild { get; set; } = true;
    }

    /// <summary>
    /// 被监视的远程主机模型
    /// </summary>
    public class NetHostModel
    {
        /// <summary>
        /// 通信主机名称
        /// </summary>
        [JsonProperty("通信主机名称")] public string RemoteName { get; set; }
        /// <summary>
        /// 通信主机的IP地址
        /// </summary>
        [JsonProperty("通信主机IP")] public string IP { get; set; }
        /// <summary>
        /// 告警门限
        /// </summary>
        [JsonProperty("告警门限")] public int AlarmValue { get; set; }
        /// <summary>
        /// 配置是否有效
        /// </summary>
        [JsonIgnore] public bool IsVaild { get; set; } = true;
    }

    /// <summary>
    /// 被监视的路径参数模型
    /// </summary>
    public class UploadPathModel
    {
        /// <summary>
        /// 监视路径地址
        /// </summary>
        [JsonProperty("监视路径地址")] public string ParentPath { get; set; }
        /// <summary>
        /// 子路径文件夹命名的格式
        /// </summary>
        [JsonProperty("监视路径子文件夹名称格式")] public string SubPathFormat { get; set; }
        /// <summary>
        /// 文件上传路径列表
        /// </summary>        
        [JsonProperty("文件上传路径列表")] public List<ChildrenPathModel> UpPathList { get; set; }
        /// <summary>
        /// 有新文件立即上传
        /// </summary>
        [JsonProperty("立即上传")] public bool UpNow { get; set; }
        /// <summary>
        /// 定时上传
        /// </summary>
        [JsonProperty("定时上传")] public string Timed { get; set; }
        /// <summary>
        /// 是否需要压缩
        /// </summary>
        [JsonProperty("需要压缩")] public bool IsCompressed { get; set; }
        /// <summary>
        /// 是否删除上传后的文件
        /// </summary>
        [JsonProperty("上传后删除文件")] public bool IsDeleteOldFiles { get; set; }
        /// <summary>
        /// 数据保留（天）
        /// </summary>
        [JsonProperty("数据保留（天）")] public int ReserveDays { get; set; }
        /// <summary>
        /// 时间基准
        /// </summary>
        [JsonProperty("时间基准")] public string TimeBase { get; set; }
        /// <summary>
        /// 是否删除上传后的文件
        /// </summary>
        [JsonProperty("压缩格式")] public string CompressFormat { get; set; }
        /// <summary>
        /// 文件过滤
        /// </summary>
        [JsonProperty("文件过滤")] public string FileFilter { get; set; }
        /// <summary>
        /// 配置是否有效
        /// </summary>
        [JsonIgnore] public bool IsVaild { get; set; } = true;
        /// <summary>
        /// 标识是否正在上传
        /// </summary>
        [JsonIgnore] public object _lock = new();
        private bool inUpload = false;
        [JsonIgnore] public bool InUpload
        {
            get
            {
                lock (_lock) return inUpload;
            }
            set
            {
                lock (_lock) inUpload = value;
            }
        }
    }

    /// <summary>
    /// 文件上传路径子模型
    /// </summary>
    public class ChildrenPathModel
    {
        /// <summary>
        /// 文件上传地址
        /// </summary>
        [JsonProperty("文件上传地址")] public string UploadPath { get; set; }
        /// <summary>
        /// 用户名
        /// </summary>
        [JsonProperty("用户名")] public string UserName { get; set; }
        /// <summary>
        /// 密码
        /// </summary>
        [JsonProperty("密码")] public string PassWord { get; set; }
        /// <summary>
        /// 同名文件处理
        /// </summary>
        [JsonProperty("同名文件处理")] public string Homonym { get; set; }
    }

    /// <summary>
    /// 组播地址配置
    /// </summary>
    public class MutiCastIPModel
    {
        /// <summary>
        /// 组播IP地址
        /// </summary>
        [JsonProperty("组播IP地址")] public string IP { get; set; }
        /// <summary>
        /// 组播端口
        /// </summary>
        [JsonProperty("组播端口")] public string Port { get; set; }
        /// <summary>
        /// 配置是否有效
        /// </summary>
        [JsonIgnore] public bool IsVaild { get; set; } = true;
    }
}
