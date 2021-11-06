using FluentFTP;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Watcher.WorkerService.Helper;
using Watcher.WorkerService.Model;
using Watcher.WorkerService.Protocol;
using static Watcher.WorkerService.Helper.Utils;

namespace Watcher.WorkerService
{
    public class Worker : BackgroundService
    {
        ConfigModel CM;//配置文件
        ConcurrentDictionary<string, CountAndState> IPState;//通信状态字典工具，Concurrent代表线程安全
        ConcurrentDictionary<string, CountAndState> ProcessState;//进程状态字典工具
        readonly ConcurrentDictionary<string, Timer> tDic = new();//子线程没了，计时就被器回收了，所以要存放一下
        IPEndPoint multiCastEnd;//组播地址
        //[SupportedOSPlatform("windows")]
        //readonly SpeechSynthesizer speech = new();//只在windows下支持                                             
        FileSystemWatcher configWatcher;  //配置文件监视器

        public Worker() { }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Run(() =>
            {
                Init();
                //监视配置文件，随时更改参数，随时响应
                configWatcher = new FileSystemWatcher(AppDomain.CurrentDomain.BaseDirectory, "Config.json");
                configWatcher.Changed += new FileSystemEventHandler(ConfigChanged);
                configWatcher.EnableRaisingEvents = true;
                configWatcher.NotifyFilter = NotifyFilters.LastWrite;
            }, stoppingToken);
        }

        /// <summary>
        /// 初始化
        /// </summary>
        void Init()
        {
            //读取配置文件
            CM = ConfigHelper.Instance.LoadConfig() ?? CM; //配置文件修改后，存在LoadConfig读取错误的情况
            if (CM == null) return;
            //通信和进程状态初始化
            IPState = new ConcurrentDictionary<string, CountAndState>();
            ProcessState = new ConcurrentDictionary<string, CountAndState>();
            //组播初始化
            multiCastEnd = new IPEndPoint(IPAddress.Parse(CM.MutiCastIP.IP), int.Parse(CM.MutiCastIP.Port));
            //监视初始化
            CM.MonitoredIPs.ForEach(model => IPState[model.IP] = new CountAndState(-1, null, model.AlarmValue, model.RemoteName));
            CM.MonitoredProcesses.ForEach(model => ProcessState[model.Name] = new CountAndState(-1, null, model.AlarmValue));
            CM.MonitoredPathes.Where(model => !model.UpNow).ToList().ForEach(model => SetTask(model));
            CM.MonitoredPathes.Where(model => model.UpNow).ToList().ForEach(model => FileWatcherInit(model));
            //每秒执行一次计数操作
            tDic["每秒执行"] = new Timer(TimeEvent, null, 1000, 1000);
        }

        /// <summary>
        /// 配置文件修改后重新加载
        /// </summary>
        void ConfigChanged(object source, FileSystemEventArgs e)
        {
            if (IsFileInUse(e.FullPath)) return;//防止Onchanged多次触发
            //防抖，防止事件重复调用，这里只是延时触发而已
            new DelayAction().Debounce(2000, null, new Action(() =>
            {
                tDic.Values.ToList().ForEach(t => t.Dispose());
                Init();
            }));
        }

        #region 状态检查任务区域
        /// <summary>
        /// 每秒调用
        /// </summary>
        /// <param name="state">UploadPathModel</param>
        private async void TimeEvent(object state)
        {
#if TRACE
            LogHelper.Debug("running");
#endif
            await WatchIPCheck();
            await MonitorProcessCheck();

            //await Send(new byte[] { 0x0f }, multiCastEnd, (ushort)WATCHER.HEARTBEAT);//发送心跳信息
            SendHeartBeat();
        }

        /// <summary>
        /// 循环检查IP字典中的IP是否在线
        /// </summary>
        private Task WatchIPCheck()
        {
            return Task.Run(() =>
            {
                //异步循环
                Parallel.ForEach(IPState.Keys, keyIP =>
                {
                    if (!IPState.ContainsKey(keyIP)) return;//配置文件修改后，存在这样的情况
                    if (!PingIpOrDomainName(keyIP))
                    {
                        IPState[keyIP].Count++;
                        if (IPState[keyIP].Count == IPState[keyIP].AlarmValue && IPState[keyIP].State != "OffLine")
                        {
                            IPState[keyIP].State = "OffLine";
                            IPState[keyIP].Count = -1;
                            LogHelper.Error($"{keyIP} {IPState[keyIP].Info}离线！");
                            SendLog(new LogModelForWatcher()
                            {
                                Description = $"{IPState[keyIP].Info}离线！",
                                Type = LogType.警告,
                                ShowGrowl = true,
                                IsSpeak = true
                            });
                        }
                    }
                    else
                    {
                        IPState[keyIP].Count = 0;
                        if (IPState[keyIP].State != "OnLine")
                        {
                            IPState[keyIP].State = "OnLine";
                            LogHelper.Info($"{keyIP} {IPState[keyIP].Info}上线！");
                            SendLog(new LogModelForWatcher()
                            {
                                Description = $"{IPState[keyIP].Info}上线！",
                                Type = LogType.信息,
                                ShowGrowl = true
                            });
                        }
                    }
                });
            });
        }

        /// <summary>
        /// 循环检查进程字典中的进程是否运行正在
        /// </summary>
        private Task MonitorProcessCheck()
        {
            return Task.Run(() =>
            {
                //异步循环
                Parallel.ForEach(ProcessState.Keys, keyProcess =>
                {
                    if (!ProcessState.ContainsKey(keyProcess)) return;//配置文件修改后，存在这样的情况
                    try
                    {
                        Process[] procs = Process.GetProcessesByName(keyProcess);
                        if (!procs[0].Responding)
                        {
                            ProcessState[keyProcess].Count++;
                            if (ProcessState[keyProcess].Count == ProcessState[keyProcess].AlarmValue && ProcessState[keyProcess].State != "Dead")
                            {
                                ProcessState[keyProcess].State = "Dead";
                                ProcessState[keyProcess].Count = -1;
                                LogHelper.Error($"\"{keyProcess}\"进程无响应！");
                                SendLog(new LogModelForWatcher()
                                {
                                    Description = $"\"{keyProcess}\"进程无响应！",
                                    Type = LogType.错误,
                                    ShowGrowl = true,
                                    IsSpeak = true
                                });
                            }
                        }
                        else
                        {
                            ProcessState[keyProcess].Count = -1;
                            if (ProcessState[keyProcess].State != "Running")
                            {
                                ProcessState[keyProcess].State = "Running";
                                LogHelper.Info($"\"{keyProcess}\"运行正常！");
                                SendLog(new LogModelForWatcher()
                                {
                                    Description = $"\"{keyProcess}\"运行正常！",
                                    Type = LogType.信息,
                                    ShowGrowl = true
                                });
                            }
                        }
                    }
                    catch
                    {
                        if (ProcessState[keyProcess].State != "Exception")
                        {
                            ProcessState[keyProcess].State = "Exception";
                            ProcessState[keyProcess].Count = -1;
                            LogHelper.Warn($"\"{keyProcess}\"进程不存在！");
                            SendLog(new LogModelForWatcher()
                            {
                                Description = $"\"{keyProcess}\"进程不存在！",
                                Type = LogType.警告,
                                ShowGrowl = true
                            });
                        }
                    }
                });
            });
        }
        #endregion

        #region 立即上传任务区域
        /// <summary>
        /// 文件夹监视器初始化
        /// </summary>
        /// <param name="toWatchPath">监视文件夹的路径</param>
        ConcurrentDictionary<FileSystemWatcher, UploadPathModel> UploadList = new();
        ConcurrentDictionary<UploadPathModel, List<ChildrenPathModel>> FtpList = new();
        ConcurrentDictionary<UploadPathModel, List<ChildrenPathModel>> ShareFoldsList = new();
        public void FileWatcherInit(UploadPathModel model)
        {
            // 文件夹监视器
            FileSystemWatcher fileWatcher;
            if (!Directory.Exists(model.ParentPath))
            {
                SendLog(new LogModelForWatcher()
                {
                    Description = $"监视路径\"{model.ParentPath}\"不存在！",
                    Type = LogType.警告,
                    ShowGrowl = true
                });
                return;
            }
            fileWatcher = new FileSystemWatcher(model.ParentPath, "*.*");
            fileWatcher.Created += new FileSystemEventHandler(OnCreated);
            fileWatcher.EnableRaisingEvents = true;
            fileWatcher.IncludeSubdirectories = true;
            fileWatcher.NotifyFilter = NotifyFilters.Attributes
                | NotifyFilters.CreationTime | NotifyFilters.DirectoryName
                | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
            UploadList[fileWatcher] = model;
            //必须要有一个暂存列表，FtpList[model]，ShareFoldsList[model]没有初始化不能直接添加对象
            var fl = new List<ChildrenPathModel>();
            var sl = new List<ChildrenPathModel>();
            model.UpPathList.ForEach(child =>
            {
                if (IsFtp(child.UploadPath))
                    fl.Add(child);
                else
                    sl.Add(child);
            });
            FtpList[model] = fl;
            ShareFoldsList[model] = sl;
        }
        /// <summary>
        /// 监视的文件夹有新建文件事件时触发
        /// </summary>
        private ConcurrentQueue<OperTimeStamp> UpLoadedFiles = new();//缓存已经上传了的包含文件操作时间戳的文件对象
        private ConcurrentQueue<string> filePaths = new();
        private DelayAction da = new();
        private int filesCount = 0;  //文件数量计数
        /// <summary>
        /// 生产者，消费者模式解决文件上传问题
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        private void OnCreated(object source, FileSystemEventArgs e)
        {
            /*            if (UpLoadedFiles.Any(up => up.FileName == e.Name)) return;
                            filePaths.Enqueue(e.FullPath);
                        UpLoadedFiles.Enqueue(new OperTimeStamp(DateTime.Now, e.Name, e.FullPath));*/
            da.Debounce(5000, null, () =>
            {
                SendLog(new LogModelForWatcher()
                {
                    Description = $"检测到新文件，数量{filesCount}",
                    Type = LogType.成功,
                    ShowGrowl = true,
                    IsSpeak = CM.SpeechEnable
                });
                filesCount = 0;
            });
            var watcher = (FileSystemWatcher)source;
            var model = UploadList[watcher];
            UploadNowToShare(e.FullPath, model);
            filesCount++;
            //UploadNowToFtpV2(e.FullPath, model);  //上传到ftp太慢了，考虑使用MongoDB来代替
        }
        private void InUpload(UploadPathModel model)
        {
            if (filePaths.Count > 0)
            {
                model.InUpload = true;
                filePaths.TryDequeue(out string path);
                //await Task.Run(() => FileUpload(path, model));
                UploadNowToFtpV1(path, model);
                InUpload(model);
            }
            else
            {
                model.InUpload = false;
                return;
            }
        }
        /// <summary>
        /// 立即上传至FTP
        /// </summary>
        private void UploadNowToFtpV1(string path, UploadPathModel model)
        {
            var newFile = new FileInfo(path);
            var td = DateTime.Now;
            if (model.FileFilter.Contains(newFile.Extension.ToLower()) && !newFile.Name.StartsWith("~$")//修改word和excel时会出现带~$符号的临时文件
                && newFile.Extension != "")
            {
                try
                {
                    FtpList[model].ForEach(async ftp =>
                    {
                        //if (!ftp.IsConnected)
                        //{
                            //发起连接登录
                            //await ftp.ConnectAsync();
                            //启用UTF8传输
                            //var result = ftp.Execute("OPTS UTF8 ON");
                            //if (!result.Code.Equals("200") && !result.Code.Equals("202"))
                            //ftp.Encoding = Encoding.GetEncoding("ISO-8859-1");


                            /*                            var nowD = td.ToString("yyyy-MM-dd") + "/";
                                                        if (!ftp.DirectoryExistsAsync(nowD).Result)
                                                            await ftp.CreateDirectoryAsync(nowD);*/
                        //}
                        //await ftp.UploadFileAsync(newFile.FullName, $"ftp/{td.ToString("yyyy-MM-dd")}/{newFile.Name}", createRemoteDir: true);
                        //await ftp.DisconnectAsync();
                        //ftp.Dispose();
                        /*                        var Watch = new Stopwatch();
                                                Watch.Start();
                                                while (newFile.Exists)
                                                {
                                                    if (IsFileInUse(path) == false)//IsFileInUse报错时返回null,被注释掉了
                                                    {
                                                        try
                                                        {
                                                            //上传文件
                                                            ftp.UpLoadFile(newFile.FullName, newFile.Name);
                                                        }
                                                        catch
                                                        {
                                                            SendLog(new LogModelForWatcher()
                                                            {
                                                                Description = $"{newFile.Name}上传失败！",
                                                                Type = LogType.警告,
                                                                ShowGrowl = true,
                                                                IsSpeak = true
                                                            });
                                                        }
                                                        break;
                                                    }

                                                    Thread.Sleep(500);
                                                    if (Watch.ElapsedMilliseconds / 1e3 > 60) //超时10分钟放弃，防止死循环并警告
                                                    {
                                                        SendLog(new LogModelForWatcher()
                                                        {
                                                            Description = $"{newFile.Name}上传失败！",
                                                            Type = LogType.警告,
                                                            ShowGrowl = true,
                                                            IsSpeak = true
                                                        });
                                                        break;
                                                    }
                                                }
                                                Watch.Stop();*/
                    });

                    //UpLoadedFiles.Enqueue(new OperTimeStamp(td, newFile.Name, newFile.FullName));
                    if (UpLoadedFiles.Count > 1000)
                    {
                        if (UpLoadedFiles.TryDequeue(out OperTimeStamp deItem))
                            SendLog(new LogModelForWatcher()
                            {
                                Description = "文件队列操作失败！",
                                Type = LogType.警告,
                                ShowGrowl = true
                            });
                    }

                    filesCount++;
                }
                catch (Exception error)
                {
                    SendLog(new LogModelForWatcher()
                    {
                        Description = error.ToString(),
                        Type = LogType.警告,
                        ShowGrowl = true
                    });
                }
            }
        }
        private void UploadNowToFtpV2(string path, UploadPathModel model)
        {
            FtpList[model].ForEach(async child =>
            {
                var ftp = new FtpClient(child.UploadPath, child.UserName, child.PassWord)
                {
                    EncryptionMode = FtpEncryptionMode.None,
                    DataConnectionType = FtpDataConnectionType.PASV,
                    Encoding = Encoding.UTF8
                };
                var newFile = new FileInfo(path);
                var td = DateTime.Now;
                if (model.FileFilter.Contains(newFile.Extension.ToLower()) && !newFile.Name.StartsWith("~$")//修改word和excel时会出现带~$符号的临时文件
                    && newFile.Extension != "")
                {
                    if (!ftp.IsConnected)
                    {
                        //发起连接登录
                        await ftp.ConnectAsync();
                        //启用UTF8传输
                        var result = ftp.Execute("OPTS UTF8 ON");
                        if (!result.Code.Equals("200") && !result.Code.Equals("202"))
                            ftp.Encoding = Encoding.GetEncoding("ISO-8859-1");
                    }
                    await ftp.UploadFileAsync(newFile.FullName, $"ftp/{td.ToString("yyyy-MM-dd")}/{newFile.Name}", createRemoteDir: true);
                    await ftp.DisconnectAsync();
                    ftp.Dispose();
                }
            });
        }
        /// <summary>
        /// 立即上传至共享文件夹
        /// </summary>
        private void UploadNowToShare(string path, UploadPathModel model)
        {
            var newFile = new FileInfo(path);
            var nowD = $"{DateTime.Now:yyyy-MM-dd}_{CM.HostName}/";
            Task.Run(() =>
            {
                ShareFoldsList[model].ForEach(child =>
                {
                    if (!ConnectToSharedFolder(child.UploadPath, child.UserName, child.PassWord))
                    {
                        LogHelper.Error($"{child.UploadPath}:共享文件夹连接失败！");
                        return;
                    }
                    var filePath = @$"{child.UploadPath}\{nowD}\";
                    if (!Directory.Exists(filePath))
                        Directory.CreateDirectory(filePath);

                    newFile.CopyTo(@$"{filePath}{newFile.Name}");

/*                    var Watch = new Stopwatch();
                    Watch.Start();
                    while (newFile.Exists)
                    {
                        if (IsFileInUse(path) == false)//IsFileInUse报错时返回null,被注释掉了
                        {
                            try
                            {
                                //上传文件
                                newFile.CopyTo(@$"{filePath}{newFile.Name}");
                            }
                            catch
                            {
                                SendLog(new LogModelForWatcher()
                                {
                                    Description = $"{newFile.Name}上传失败！",
                                    Type = LogType.警告,
                                    ShowGrowl = true,
                                    IsSpeak = true
                                });
                            }
                            break;
                        }

                        Thread.Sleep(500);
                        if (Watch.ElapsedMilliseconds / 1e3 > 60) //超时10分钟放弃，防止死循环并警告
                        {
                            SendLog(new LogModelForWatcher()
                            {
                                Description = $"{newFile.Name}上传失败！",
                                Type = LogType.警告,
                                ShowGrowl = true,
                                IsSpeak = true
                            });
                            break;
                        }
                    }
                    Watch.Stop();*/
                });
            });
        }
        #endregion

        #region 定时任务区域
        /// <summary>
        /// 判断并设定任务
        /// </summary>
        /// <param name="model"></param>
        private void SetTask(UploadPathModel model)
        {
            if (!model.IsVaild || string.IsNullOrEmpty(model.Timed)) return; //参数无效，直接返回，暂时只做定时任务

            DateTime.TryParse(model.Timed.Replace('：', ':'), out DateTime fixTime); //替换中文冒号

            var now = DateTime.Now;
            var oneOClock = DateTime.Today.Add(fixTime.TimeOfDay); //加上固定时刻，例如18:00:00

            if (now > oneOClock)
            {
                oneOClock = oneOClock.AddDays(1.0);
            }
            var msUntilFour = (int)(oneOClock - now).TotalMilliseconds;
            //回调函数，回调函数的参数，延迟执行（0表示立即执行），Timeout.Infinite表示非周期执行，只执行1次
            tDic[model.ParentPath] = new Timer(DoAtFixClock, model, msUntilFour, Timeout.Infinite);
        }

        /// <summary>
        /// 定时任务
        /// </summary>
        private async void DoAtFixClock(object state)
        {
            //执行功能
            var model = (UploadPathModel)state;
            var shareTask = UpToShareFolder(model);
            var ftpTask = UpToFtp(model);

            await Task.WhenAll(shareTask, ftpTask); //等待所有任务完成
            await DeleteOld(model, shareTask.Result.Union(ftpTask.Result).ToList()); //对已成功上传的文件取并集

            //日志发送
            if (ftpTask.Result.Count > 0) 
            {
                SendLog(new LogModelForWatcher()
                {
                    IsSpeak = true,
                    Type = LogType.成功,
                    ShowGrowl = true,
                    Description = $"已上传FTP{ftpTask.Result.Count}项文件"
                });
            }
            if (shareTask.Result.Count > 0) 
            {
                SendLog(new LogModelForWatcher()
                {
                    IsSpeak = true,
                    Type = LogType.成功,
                    ShowGrowl = true,
                    Description = $"已上传共享文件夹{shareTask.Result.Count}项文件"
                });

            }

            //再次设定
            SetTask(model);
        }

        /// <summary>
        /// 上传至共享文件夹
        /// </summary>
        /// <returns>上传成功的路径列表</returns>
        private static Task<List<string>> UpToShareFolder(UploadPathModel model)
        {
            return Task.Run(() =>
            {
                var successUps = new List<string>();
                model.UpPathList.ForEach(child =>
                {
                    if (IsFtp(child.UploadPath)) return;
                    if (!ConnectToSharedFolder(child.UploadPath, child.UserName, child.PassWord))
                    {
                        LogHelper.Error($"{child.UploadPath}:共享文件夹连接失败！");
                        return;
                    }
                    //处理文件夹
                    var root = new DirectoryInfo(model.ParentPath);
                    foreach (var dic in root.GetDirectories())
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(model.SubPathFormat) &&
                                !DateTime.TryParseExact(dic.Name, model.SubPathFormat, null,
                                                        System.Globalization.DateTimeStyles.None,
                                                        out DateTime dicTime)) continue; //设定了子文件夹的格式的话就不处理格式不匹配的文件夹
                            if (dic.Name.StartsWith('~')) continue; //以~开头的默认为临时文件，不上传
                            if (model.IsCompressed)
                            {
                                var filePath = @$"{child.UploadPath}\{dic.Name}.{model.CompressFormat}";
                                if (File.Exists(filePath))
                                {
                                    if (child.Homonym == "忽略") continue;
                                    if (child.Homonym == "保留")
                                    {
                                        var count = 1;
                                        filePath = @$"{child.UploadPath}\{dic.Name}({count}).{model.CompressFormat}";
                                        while (File.Exists(filePath)) filePath = @$"{child.UploadPath}\{dic.Name}({count++}).{model.CompressFormat}";
                                    }
                                }
                                CompressionFile(dic.FullName, filePath);
                            }
                            else
                            {
                                var direPath = @$"{child.UploadPath}\{dic.Name}";
                                if (Directory.Exists(direPath))
                                {
                                    if (child.Homonym == "忽略") continue;
                                    if (child.Homonym == "保留")
                                    {
                                        var count = 1;
                                        direPath = @$"{child.UploadPath}\{dic.Name}({count})";
                                        while (Directory.Exists(direPath)) direPath = @$"{child.UploadPath}\{dic.Name}({count++})";
                                    }
                                }
                                CopyDirectory(dic.FullName, direPath, true);
                            }
                            LogHelper.Info($"{dic.FullName}:上传成功！({child.UploadPath})");
                            successUps.Add(dic.FullName);
                        }
                        catch
                        {
                            LogHelper.Warn($"{dic.FullName}:上传失败！({child.UploadPath})");
                        }
                    }
                    //处理单个文件
                    foreach (var fls in root.GetFiles())
                    {
                        try
                        {
                            var name = Path.GetFileNameWithoutExtension(fls.Name);
                            var ext = Path.GetExtension(fls.Name);
                            if (!string.IsNullOrEmpty(model.SubPathFormat) &&
                                !DateTime.TryParseExact(name, model.SubPathFormat,
                                                        null, System.Globalization.DateTimeStyles.None,
                                                        out DateTime dicTime)) continue; //设定了子文件夹的格式的话就不处理格式不匹配的文件夹
                            if (name.StartsWith('~')) continue; //以~开头的默认为临时文件，不上传
                            var filePath = @$"{child.UploadPath}\{fls.Name}";
                            if (File.Exists(filePath))
                            {
                                if (child.Homonym == "忽略") continue;
                                if (child.Homonym == "保留")
                                {
                                    var count = 1;
                                    var newPath = @$"{child.UploadPath}\{name}({count}).{ext}";
                                    while (File.Exists(newPath)) newPath = @$"{child.UploadPath}\{name}({count++}).{ext}";
                                    filePath = newPath;
                                }
                            }
                            fls.CopyTo(filePath, true);
                            LogHelper.Info($"{fls.FullName}:上传成功！({child.UploadPath})");
                            successUps.Add(fls.FullName);
                        }
                        catch
                        {
                            LogHelper.Warn($"{fls.FullName}:上传失败！({child.UploadPath})");
                        }
                    }
                });
                return successUps;
            });
        }

        /// <summary>
        /// 上传至Ftp
        /// </summary>
        /// <returns>上传成功的路径列表</returns>
        public static Task<List<string>> UpToFtp(UploadPathModel model)
        {
            return Task.Run(() =>
            {
                var successUps = new List<string>();
                model.UpPathList.ForEach(child =>
                {
                    if (!IsFtp(child.UploadPath)) return;
                    try
                    {
                        var ftp = new FtpHelper(child.UploadPath, child.UserName, child.PassWord);
                        //处理文件夹
                        var root = new DirectoryInfo(model.ParentPath);
                        foreach (var dic in root.GetDirectories())
                        {
                            if (!string.IsNullOrEmpty(model.SubPathFormat) &&
                                !DateTime.TryParseExact(dic.Name, model.SubPathFormat, null,
                                                        System.Globalization.DateTimeStyles.None,
                                                        out DateTime dicTime)) continue; //设定了子文件夹的格式的话就不处理格式不匹配的文件夹
                            if (dic.Name.StartsWith('~')) continue; //以~开头的默认为临时文件，不上传
                            if (model.IsCompressed)
                            {
                                var filePath = $"{dic.Name}.{model.CompressFormat}";
                                var compressPath = @$"{Directory.GetParent(dic.FullName).FullName}\~temp";
                                //确定上传文件本地压缩后暂存的路径
                                var count = 0;
                                while (Directory.Exists(compressPath)) compressPath = @$"{Directory.GetParent(dic.FullName).FullName}\~temp{count++}";
                                if (ftp.FileExist(filePath))
                                {
                                    if (child.Homonym == "忽略") continue;
                                    if (child.Homonym == "保留")
                                    {
                                        //确定上传文件在ftp中的名称
                                        count = 1;
                                        filePath = $"{dic.Name}({count}).{model.CompressFormat}";
                                        while (ftp.FileExist(filePath)) filePath = $"{dic.Name}({count++}).{model.CompressFormat}";
                                    }
                                    else
                                    {
                                        ftp.Delete(filePath);
                                    }
                                }
                                Directory.CreateDirectory(compressPath); //这一句无务必放到这里
                                CompressionFile(dic.FullName, @$"{compressPath}\{filePath}");
                                ftp.UpLoadFile(@$"{compressPath}\{filePath}");
                                DeleteFolder(compressPath);
                            }
                            else
                            {
                                var direPath = dic.Name;
                                if (ftp.DirectoryExist(direPath))
                                {
                                    if (child.Homonym == "忽略") continue;
                                    if (child.Homonym == "保留")
                                    {
                                        var count = 1;
                                        direPath = $"{dic.Name}({count})";
                                        while (ftp.DirectoryExist(direPath)) direPath = $"{dic.Name}({count++})";
                                    }
                                    else
                                    {
                                        ftp.RemoveDirectory(direPath); //覆盖
                                    }
                                }
                                ftp.UpLoadDirectory(dic.FullName, direPath);
                            }
                            LogHelper.Info($"{dic.FullName}:上传成功！({child.UploadPath})");
                            successUps.Add(dic.FullName);
                        }
                        //处理单个文件
                        foreach (var fls in root.GetFiles())
                        {
                            var name = Path.GetFileNameWithoutExtension(fls.Name);
                            var ext = Path.GetExtension(fls.Name);
                            if (!string.IsNullOrEmpty(model.SubPathFormat) &&
                                !DateTime.TryParseExact(name, model.SubPathFormat,
                                                        null, System.Globalization.DateTimeStyles.None,
                                                        out DateTime dicTime)) continue; //设定了子文件夹的格式的话就不处理格式不匹配的文件夹
                            if (name.StartsWith('~')) continue; //以~开头的默认为临时文件，不上传
                            var filePath = fls.Name;
                            if (ftp.FileExist(filePath))
                            {
                                if (child.Homonym == "忽略") continue;
                                if (child.Homonym == "保留")
                                {
                                    var count = 1;
                                    filePath = $"{name}({count}).{ext}";
                                    while (ftp.FileExist(filePath)) filePath = $"{name}({count++}).{ext}";
                                }
                                else
                                {
                                    ftp.Delete(filePath); //覆盖
                                }
                            }
                            ftp.UpLoadFile(fls.FullName, filePath);
                            LogHelper.Info($"{fls.FullName}:上传成功！({child.UploadPath})");
                            successUps.Add(fls.FullName);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Error($"{ex.Message}({child.UploadPath})");
                    }
                });
                return successUps;
            });
        }

        /// <summary>
        /// 删除已上传的文件
        /// </summary>
        private static Task DeleteOld(UploadPathModel model, List<string> doDeletes)
        {
            return Task.Run(() =>
            {
                if (!model.IsDeleteOldFiles) return;
                //处理文件夹
                var root = new DirectoryInfo(model.ParentPath);
                foreach (var dic in root.GetDirectories())
                {
                    try
                    {
                        if (!doDeletes.Contains(dic.FullName)) continue;
                        var canParse = DateTime.TryParseExact(dic.Name, model.SubPathFormat, null, System.Globalization.DateTimeStyles.None, out DateTime dicTime);
                        switch (model.TimeBase)
                        {
                            case "文件名":
                                break;
                            case "创建时间":
                                dicTime = dic.CreationTime;
                                break;
                            default: //"自动"
                                if (!canParse) dicTime = dic.CreationTime;
                                break;
                        }
                        if (dicTime == DateTime.MinValue) continue; //文件名解析不出来的话就是最小值，不处理
                        if (dicTime <= DateTime.Today.AddDays(-model.ReserveDays))
                        {
                            DeleteFolder(dic.FullName);
                            LogHelper.Info($"{dic.FullName}:删除成功！");
                        }
                    }
                    catch
                    {
                        LogHelper.Warn($"{dic.FullName}:删除失败！");
                    }
                }
                //处理文件
                foreach (var fls in root.GetFiles())
                {
                    try
                    {
                        if (!doDeletes.Contains(fls.FullName)) continue;
                        var fileName = Path.GetFileNameWithoutExtension(fls.Name);
                        var canParse = DateTime.TryParseExact(fileName, model.SubPathFormat, null, System.Globalization.DateTimeStyles.None, out DateTime flsTime);
                        switch (model.TimeBase)
                        {
                            case "文件名":
                                break;
                            case "创建时间":
                                flsTime = fls.CreationTime;
                                break;
                            default: //"自动"
                                if (!canParse) flsTime = fls.CreationTime;
                                break;
                        }
                        if (flsTime == DateTime.MinValue) continue; //文件名解析不出来的话就是最小值
                        if (flsTime <= DateTime.Today.AddDays(-model.ReserveDays))
                        {
                            if (!File.Exists(fls.FullName)) return;
                            File.Delete(fls.FullName);
                            LogHelper.Info($"{fls.FullName}:删除成功！");
                        }
                    }
                    catch
                    {
                        LogHelper.Warn($"{fls.FullName}:删除失败！");
                    }
                }
            });
        }
        #endregion

        /// <summary>
        /// 发送心跳信息
        /// </summary>
        async void SendHeartBeat()
        {
            var log = new LogModelForWatcher();
            log.Name = CM.HostName;
            log.LogTime = DateTime.Now;
            log.wType = WriteType.监视器;
            log.Description = "HeartBeat";
            string json = JsonHelper.SerializeObject(log);
            await Send(Encoding.UTF8.GetBytes(json), multiCastEnd, (ushort)WATCHER.HEARTBEAT);
        }

        /// <summary>
        /// 发送日志
        /// </summary>
        public async void SendLog(LogModelForWatcher log)
        {
            log.Name = CM.HostName;
            log.LogTime = DateTime.Now;
            log.wType = WriteType.监视器;
            //语音提示,仅windows
            if (!CM.SpeechEnable) log.IsSpeak = false;
            //if (log.IsSpeak && RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) speech.SpeakAsync(log.Description);
            //组播日志
            LogHelper.Info($"发送日志：{log.Description}");
            string json = JsonHelper.SerializeObject(log);
            await Send(Encoding.UTF8.GetBytes(json), multiCastEnd, (ushort)WATCHER.WENUM_WATCHER_INFO);
        }
    }
}
