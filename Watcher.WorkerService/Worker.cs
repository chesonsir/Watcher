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
        ConfigModel CM;//�����ļ�
        ConcurrentDictionary<string, CountAndState> IPState;//ͨ��״̬�ֵ乤�ߣ�Concurrent�����̰߳�ȫ
        ConcurrentDictionary<string, CountAndState> ProcessState;//����״̬�ֵ乤��
        readonly ConcurrentDictionary<string, Timer> tDic = new();//���߳�û�ˣ���ʱ�ͱ��������ˣ�����Ҫ���һ��
        IPEndPoint multiCastEnd;//�鲥��ַ
        //[SupportedOSPlatform("windows")]
        //readonly SpeechSynthesizer speech = new();//ֻ��windows��֧��                                             
        FileSystemWatcher configWatcher;  //�����ļ�������

        public Worker() { }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Run(() =>
            {
                Init();
                //���������ļ�����ʱ���Ĳ�������ʱ��Ӧ
                configWatcher = new FileSystemWatcher(AppDomain.CurrentDomain.BaseDirectory, "Config.json");
                configWatcher.Changed += new FileSystemEventHandler(ConfigChanged);
                configWatcher.EnableRaisingEvents = true;
                configWatcher.NotifyFilter = NotifyFilters.LastWrite;
            }, stoppingToken);
        }

        /// <summary>
        /// ��ʼ��
        /// </summary>
        void Init()
        {
            //��ȡ�����ļ�
            CM = ConfigHelper.Instance.LoadConfig() ?? CM; //�����ļ��޸ĺ󣬴���LoadConfig��ȡ��������
            if (CM == null) return;
            //ͨ�źͽ���״̬��ʼ��
            IPState = new ConcurrentDictionary<string, CountAndState>();
            ProcessState = new ConcurrentDictionary<string, CountAndState>();
            //�鲥��ʼ��
            multiCastEnd = new IPEndPoint(IPAddress.Parse(CM.MutiCastIP.IP), int.Parse(CM.MutiCastIP.Port));
            //���ӳ�ʼ��
            CM.MonitoredIPs.ForEach(model => IPState[model.IP] = new CountAndState(-1, null, model.AlarmValue, model.RemoteName));
            CM.MonitoredProcesses.ForEach(model => ProcessState[model.Name] = new CountAndState(-1, null, model.AlarmValue));
            CM.MonitoredPathes.Where(model => !model.UpNow).ToList().ForEach(model => SetTask(model));
            CM.MonitoredPathes.Where(model => model.UpNow).ToList().ForEach(model => FileWatcherInit(model));
            //ÿ��ִ��һ�μ�������
            tDic["ÿ��ִ��"] = new Timer(TimeEvent, null, 1000, 1000);
        }

        /// <summary>
        /// �����ļ��޸ĺ����¼���
        /// </summary>
        void ConfigChanged(object source, FileSystemEventArgs e)
        {
            if (IsFileInUse(e.FullPath)) return;//��ֹOnchanged��δ���
            //��������ֹ�¼��ظ����ã�����ֻ����ʱ��������
            new DelayAction().Debounce(2000, null, new Action(() =>
            {
                tDic.Values.ToList().ForEach(t => t.Dispose());
                Init();
            }));
        }

        #region ״̬�����������
        /// <summary>
        /// ÿ�����
        /// </summary>
        /// <param name="state">UploadPathModel</param>
        private async void TimeEvent(object state)
        {
#if TRACE
            LogHelper.Debug("running");
#endif
            await WatchIPCheck();
            await MonitorProcessCheck();

            //await Send(new byte[] { 0x0f }, multiCastEnd, (ushort)WATCHER.HEARTBEAT);//����������Ϣ
            SendHeartBeat();
        }

        /// <summary>
        /// ѭ�����IP�ֵ��е�IP�Ƿ�����
        /// </summary>
        private Task WatchIPCheck()
        {
            return Task.Run(() =>
            {
                //�첽ѭ��
                Parallel.ForEach(IPState.Keys, keyIP =>
                {
                    if (!IPState.ContainsKey(keyIP)) return;//�����ļ��޸ĺ󣬴������������
                    if (!PingIpOrDomainName(keyIP))
                    {
                        IPState[keyIP].Count++;
                        if (IPState[keyIP].Count == IPState[keyIP].AlarmValue && IPState[keyIP].State != "OffLine")
                        {
                            IPState[keyIP].State = "OffLine";
                            IPState[keyIP].Count = -1;
                            LogHelper.Error($"{keyIP} {IPState[keyIP].Info}���ߣ�");
                            SendLog(new LogModelForWatcher()
                            {
                                Description = $"{IPState[keyIP].Info}���ߣ�",
                                Type = LogType.����,
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
                            LogHelper.Info($"{keyIP} {IPState[keyIP].Info}���ߣ�");
                            SendLog(new LogModelForWatcher()
                            {
                                Description = $"{IPState[keyIP].Info}���ߣ�",
                                Type = LogType.��Ϣ,
                                ShowGrowl = true
                            });
                        }
                    }
                });
            });
        }

        /// <summary>
        /// ѭ���������ֵ��еĽ����Ƿ���������
        /// </summary>
        private Task MonitorProcessCheck()
        {
            return Task.Run(() =>
            {
                //�첽ѭ��
                Parallel.ForEach(ProcessState.Keys, keyProcess =>
                {
                    if (!ProcessState.ContainsKey(keyProcess)) return;//�����ļ��޸ĺ󣬴������������
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
                                LogHelper.Error($"\"{keyProcess}\"��������Ӧ��");
                                SendLog(new LogModelForWatcher()
                                {
                                    Description = $"\"{keyProcess}\"��������Ӧ��",
                                    Type = LogType.����,
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
                                LogHelper.Info($"\"{keyProcess}\"����������");
                                SendLog(new LogModelForWatcher()
                                {
                                    Description = $"\"{keyProcess}\"����������",
                                    Type = LogType.��Ϣ,
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
                            LogHelper.Warn($"\"{keyProcess}\"���̲����ڣ�");
                            SendLog(new LogModelForWatcher()
                            {
                                Description = $"\"{keyProcess}\"���̲����ڣ�",
                                Type = LogType.����,
                                ShowGrowl = true
                            });
                        }
                    }
                });
            });
        }
        #endregion

        #region �����ϴ���������
        /// <summary>
        /// �ļ��м�������ʼ��
        /// </summary>
        /// <param name="toWatchPath">�����ļ��е�·��</param>
        ConcurrentDictionary<FileSystemWatcher, UploadPathModel> UploadList = new();
        ConcurrentDictionary<UploadPathModel, List<ChildrenPathModel>> FtpList = new();
        ConcurrentDictionary<UploadPathModel, List<ChildrenPathModel>> ShareFoldsList = new();
        public void FileWatcherInit(UploadPathModel model)
        {
            // �ļ��м�����
            FileSystemWatcher fileWatcher;
            if (!Directory.Exists(model.ParentPath))
            {
                SendLog(new LogModelForWatcher()
                {
                    Description = $"����·��\"{model.ParentPath}\"�����ڣ�",
                    Type = LogType.����,
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
            //����Ҫ��һ���ݴ��б�FtpList[model]��ShareFoldsList[model]û�г�ʼ������ֱ����Ӷ���
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
        /// ���ӵ��ļ������½��ļ��¼�ʱ����
        /// </summary>
        private ConcurrentQueue<OperTimeStamp> UpLoadedFiles = new();//�����Ѿ��ϴ��˵İ����ļ�����ʱ������ļ�����
        private ConcurrentQueue<string> filePaths = new();
        private DelayAction da = new();
        private int filesCount = 0;  //�ļ���������
        /// <summary>
        /// �����ߣ�������ģʽ����ļ��ϴ�����
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
                    Description = $"��⵽���ļ�������{filesCount}",
                    Type = LogType.�ɹ�,
                    ShowGrowl = true,
                    IsSpeak = CM.SpeechEnable
                });
                filesCount = 0;
            });
            var watcher = (FileSystemWatcher)source;
            var model = UploadList[watcher];
            UploadNowToShare(e.FullPath, model);
            filesCount++;
            //UploadNowToFtpV2(e.FullPath, model);  //�ϴ���ftp̫���ˣ�����ʹ��MongoDB������
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
        /// �����ϴ���FTP
        /// </summary>
        private void UploadNowToFtpV1(string path, UploadPathModel model)
        {
            var newFile = new FileInfo(path);
            var td = DateTime.Now;
            if (model.FileFilter.Contains(newFile.Extension.ToLower()) && !newFile.Name.StartsWith("~$")//�޸�word��excelʱ����ִ�~$���ŵ���ʱ�ļ�
                && newFile.Extension != "")
            {
                try
                {
                    FtpList[model].ForEach(async ftp =>
                    {
                        //if (!ftp.IsConnected)
                        //{
                            //�������ӵ�¼
                            //await ftp.ConnectAsync();
                            //����UTF8����
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
                                                    if (IsFileInUse(path) == false)//IsFileInUse����ʱ����null,��ע�͵���
                                                    {
                                                        try
                                                        {
                                                            //�ϴ��ļ�
                                                            ftp.UpLoadFile(newFile.FullName, newFile.Name);
                                                        }
                                                        catch
                                                        {
                                                            SendLog(new LogModelForWatcher()
                                                            {
                                                                Description = $"{newFile.Name}�ϴ�ʧ�ܣ�",
                                                                Type = LogType.����,
                                                                ShowGrowl = true,
                                                                IsSpeak = true
                                                            });
                                                        }
                                                        break;
                                                    }

                                                    Thread.Sleep(500);
                                                    if (Watch.ElapsedMilliseconds / 1e3 > 60) //��ʱ10���ӷ�������ֹ��ѭ��������
                                                    {
                                                        SendLog(new LogModelForWatcher()
                                                        {
                                                            Description = $"{newFile.Name}�ϴ�ʧ�ܣ�",
                                                            Type = LogType.����,
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
                                Description = "�ļ����в���ʧ�ܣ�",
                                Type = LogType.����,
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
                        Type = LogType.����,
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
                if (model.FileFilter.Contains(newFile.Extension.ToLower()) && !newFile.Name.StartsWith("~$")//�޸�word��excelʱ����ִ�~$���ŵ���ʱ�ļ�
                    && newFile.Extension != "")
                {
                    if (!ftp.IsConnected)
                    {
                        //�������ӵ�¼
                        await ftp.ConnectAsync();
                        //����UTF8����
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
        /// �����ϴ��������ļ���
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
                        LogHelper.Error($"{child.UploadPath}:�����ļ�������ʧ�ܣ�");
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
                        if (IsFileInUse(path) == false)//IsFileInUse����ʱ����null,��ע�͵���
                        {
                            try
                            {
                                //�ϴ��ļ�
                                newFile.CopyTo(@$"{filePath}{newFile.Name}");
                            }
                            catch
                            {
                                SendLog(new LogModelForWatcher()
                                {
                                    Description = $"{newFile.Name}�ϴ�ʧ�ܣ�",
                                    Type = LogType.����,
                                    ShowGrowl = true,
                                    IsSpeak = true
                                });
                            }
                            break;
                        }

                        Thread.Sleep(500);
                        if (Watch.ElapsedMilliseconds / 1e3 > 60) //��ʱ10���ӷ�������ֹ��ѭ��������
                        {
                            SendLog(new LogModelForWatcher()
                            {
                                Description = $"{newFile.Name}�ϴ�ʧ�ܣ�",
                                Type = LogType.����,
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

        #region ��ʱ��������
        /// <summary>
        /// �жϲ��趨����
        /// </summary>
        /// <param name="model"></param>
        private void SetTask(UploadPathModel model)
        {
            if (!model.IsVaild || string.IsNullOrEmpty(model.Timed)) return; //������Ч��ֱ�ӷ��أ���ʱֻ����ʱ����

            DateTime.TryParse(model.Timed.Replace('��', ':'), out DateTime fixTime); //�滻����ð��

            var now = DateTime.Now;
            var oneOClock = DateTime.Today.Add(fixTime.TimeOfDay); //���Ϲ̶�ʱ�̣�����18:00:00

            if (now > oneOClock)
            {
                oneOClock = oneOClock.AddDays(1.0);
            }
            var msUntilFour = (int)(oneOClock - now).TotalMilliseconds;
            //�ص��������ص������Ĳ������ӳ�ִ�У�0��ʾ����ִ�У���Timeout.Infinite��ʾ������ִ�У�ִֻ��1��
            tDic[model.ParentPath] = new Timer(DoAtFixClock, model, msUntilFour, Timeout.Infinite);
        }

        /// <summary>
        /// ��ʱ����
        /// </summary>
        private async void DoAtFixClock(object state)
        {
            //ִ�й���
            var model = (UploadPathModel)state;
            var shareTask = UpToShareFolder(model);
            var ftpTask = UpToFtp(model);

            await Task.WhenAll(shareTask, ftpTask); //�ȴ������������
            await DeleteOld(model, shareTask.Result.Union(ftpTask.Result).ToList()); //���ѳɹ��ϴ����ļ�ȡ����

            //��־����
            if (ftpTask.Result.Count > 0) 
            {
                SendLog(new LogModelForWatcher()
                {
                    IsSpeak = true,
                    Type = LogType.�ɹ�,
                    ShowGrowl = true,
                    Description = $"���ϴ�FTP{ftpTask.Result.Count}���ļ�"
                });
            }
            if (shareTask.Result.Count > 0) 
            {
                SendLog(new LogModelForWatcher()
                {
                    IsSpeak = true,
                    Type = LogType.�ɹ�,
                    ShowGrowl = true,
                    Description = $"���ϴ������ļ���{shareTask.Result.Count}���ļ�"
                });

            }

            //�ٴ��趨
            SetTask(model);
        }

        /// <summary>
        /// �ϴ��������ļ���
        /// </summary>
        /// <returns>�ϴ��ɹ���·���б�</returns>
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
                        LogHelper.Error($"{child.UploadPath}:�����ļ�������ʧ�ܣ�");
                        return;
                    }
                    //�����ļ���
                    var root = new DirectoryInfo(model.ParentPath);
                    foreach (var dic in root.GetDirectories())
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(model.SubPathFormat) &&
                                !DateTime.TryParseExact(dic.Name, model.SubPathFormat, null,
                                                        System.Globalization.DateTimeStyles.None,
                                                        out DateTime dicTime)) continue; //�趨�����ļ��еĸ�ʽ�Ļ��Ͳ������ʽ��ƥ����ļ���
                            if (dic.Name.StartsWith('~')) continue; //��~��ͷ��Ĭ��Ϊ��ʱ�ļ������ϴ�
                            if (model.IsCompressed)
                            {
                                var filePath = @$"{child.UploadPath}\{dic.Name}.{model.CompressFormat}";
                                if (File.Exists(filePath))
                                {
                                    if (child.Homonym == "����") continue;
                                    if (child.Homonym == "����")
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
                                    if (child.Homonym == "����") continue;
                                    if (child.Homonym == "����")
                                    {
                                        var count = 1;
                                        direPath = @$"{child.UploadPath}\{dic.Name}({count})";
                                        while (Directory.Exists(direPath)) direPath = @$"{child.UploadPath}\{dic.Name}({count++})";
                                    }
                                }
                                CopyDirectory(dic.FullName, direPath, true);
                            }
                            LogHelper.Info($"{dic.FullName}:�ϴ��ɹ���({child.UploadPath})");
                            successUps.Add(dic.FullName);
                        }
                        catch
                        {
                            LogHelper.Warn($"{dic.FullName}:�ϴ�ʧ�ܣ�({child.UploadPath})");
                        }
                    }
                    //�������ļ�
                    foreach (var fls in root.GetFiles())
                    {
                        try
                        {
                            var name = Path.GetFileNameWithoutExtension(fls.Name);
                            var ext = Path.GetExtension(fls.Name);
                            if (!string.IsNullOrEmpty(model.SubPathFormat) &&
                                !DateTime.TryParseExact(name, model.SubPathFormat,
                                                        null, System.Globalization.DateTimeStyles.None,
                                                        out DateTime dicTime)) continue; //�趨�����ļ��еĸ�ʽ�Ļ��Ͳ������ʽ��ƥ����ļ���
                            if (name.StartsWith('~')) continue; //��~��ͷ��Ĭ��Ϊ��ʱ�ļ������ϴ�
                            var filePath = @$"{child.UploadPath}\{fls.Name}";
                            if (File.Exists(filePath))
                            {
                                if (child.Homonym == "����") continue;
                                if (child.Homonym == "����")
                                {
                                    var count = 1;
                                    var newPath = @$"{child.UploadPath}\{name}({count}).{ext}";
                                    while (File.Exists(newPath)) newPath = @$"{child.UploadPath}\{name}({count++}).{ext}";
                                    filePath = newPath;
                                }
                            }
                            fls.CopyTo(filePath, true);
                            LogHelper.Info($"{fls.FullName}:�ϴ��ɹ���({child.UploadPath})");
                            successUps.Add(fls.FullName);
                        }
                        catch
                        {
                            LogHelper.Warn($"{fls.FullName}:�ϴ�ʧ�ܣ�({child.UploadPath})");
                        }
                    }
                });
                return successUps;
            });
        }

        /// <summary>
        /// �ϴ���Ftp
        /// </summary>
        /// <returns>�ϴ��ɹ���·���б�</returns>
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
                        //�����ļ���
                        var root = new DirectoryInfo(model.ParentPath);
                        foreach (var dic in root.GetDirectories())
                        {
                            if (!string.IsNullOrEmpty(model.SubPathFormat) &&
                                !DateTime.TryParseExact(dic.Name, model.SubPathFormat, null,
                                                        System.Globalization.DateTimeStyles.None,
                                                        out DateTime dicTime)) continue; //�趨�����ļ��еĸ�ʽ�Ļ��Ͳ������ʽ��ƥ����ļ���
                            if (dic.Name.StartsWith('~')) continue; //��~��ͷ��Ĭ��Ϊ��ʱ�ļ������ϴ�
                            if (model.IsCompressed)
                            {
                                var filePath = $"{dic.Name}.{model.CompressFormat}";
                                var compressPath = @$"{Directory.GetParent(dic.FullName).FullName}\~temp";
                                //ȷ���ϴ��ļ�����ѹ�����ݴ��·��
                                var count = 0;
                                while (Directory.Exists(compressPath)) compressPath = @$"{Directory.GetParent(dic.FullName).FullName}\~temp{count++}";
                                if (ftp.FileExist(filePath))
                                {
                                    if (child.Homonym == "����") continue;
                                    if (child.Homonym == "����")
                                    {
                                        //ȷ���ϴ��ļ���ftp�е�����
                                        count = 1;
                                        filePath = $"{dic.Name}({count}).{model.CompressFormat}";
                                        while (ftp.FileExist(filePath)) filePath = $"{dic.Name}({count++}).{model.CompressFormat}";
                                    }
                                    else
                                    {
                                        ftp.Delete(filePath);
                                    }
                                }
                                Directory.CreateDirectory(compressPath); //��һ������طŵ�����
                                CompressionFile(dic.FullName, @$"{compressPath}\{filePath}");
                                ftp.UpLoadFile(@$"{compressPath}\{filePath}");
                                DeleteFolder(compressPath);
                            }
                            else
                            {
                                var direPath = dic.Name;
                                if (ftp.DirectoryExist(direPath))
                                {
                                    if (child.Homonym == "����") continue;
                                    if (child.Homonym == "����")
                                    {
                                        var count = 1;
                                        direPath = $"{dic.Name}({count})";
                                        while (ftp.DirectoryExist(direPath)) direPath = $"{dic.Name}({count++})";
                                    }
                                    else
                                    {
                                        ftp.RemoveDirectory(direPath); //����
                                    }
                                }
                                ftp.UpLoadDirectory(dic.FullName, direPath);
                            }
                            LogHelper.Info($"{dic.FullName}:�ϴ��ɹ���({child.UploadPath})");
                            successUps.Add(dic.FullName);
                        }
                        //�������ļ�
                        foreach (var fls in root.GetFiles())
                        {
                            var name = Path.GetFileNameWithoutExtension(fls.Name);
                            var ext = Path.GetExtension(fls.Name);
                            if (!string.IsNullOrEmpty(model.SubPathFormat) &&
                                !DateTime.TryParseExact(name, model.SubPathFormat,
                                                        null, System.Globalization.DateTimeStyles.None,
                                                        out DateTime dicTime)) continue; //�趨�����ļ��еĸ�ʽ�Ļ��Ͳ������ʽ��ƥ����ļ���
                            if (name.StartsWith('~')) continue; //��~��ͷ��Ĭ��Ϊ��ʱ�ļ������ϴ�
                            var filePath = fls.Name;
                            if (ftp.FileExist(filePath))
                            {
                                if (child.Homonym == "����") continue;
                                if (child.Homonym == "����")
                                {
                                    var count = 1;
                                    filePath = $"{name}({count}).{ext}";
                                    while (ftp.FileExist(filePath)) filePath = $"{name}({count++}).{ext}";
                                }
                                else
                                {
                                    ftp.Delete(filePath); //����
                                }
                            }
                            ftp.UpLoadFile(fls.FullName, filePath);
                            LogHelper.Info($"{fls.FullName}:�ϴ��ɹ���({child.UploadPath})");
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
        /// ɾ�����ϴ����ļ�
        /// </summary>
        private static Task DeleteOld(UploadPathModel model, List<string> doDeletes)
        {
            return Task.Run(() =>
            {
                if (!model.IsDeleteOldFiles) return;
                //�����ļ���
                var root = new DirectoryInfo(model.ParentPath);
                foreach (var dic in root.GetDirectories())
                {
                    try
                    {
                        if (!doDeletes.Contains(dic.FullName)) continue;
                        var canParse = DateTime.TryParseExact(dic.Name, model.SubPathFormat, null, System.Globalization.DateTimeStyles.None, out DateTime dicTime);
                        switch (model.TimeBase)
                        {
                            case "�ļ���":
                                break;
                            case "����ʱ��":
                                dicTime = dic.CreationTime;
                                break;
                            default: //"�Զ�"
                                if (!canParse) dicTime = dic.CreationTime;
                                break;
                        }
                        if (dicTime == DateTime.MinValue) continue; //�ļ��������������Ļ�������Сֵ��������
                        if (dicTime <= DateTime.Today.AddDays(-model.ReserveDays))
                        {
                            DeleteFolder(dic.FullName);
                            LogHelper.Info($"{dic.FullName}:ɾ���ɹ���");
                        }
                    }
                    catch
                    {
                        LogHelper.Warn($"{dic.FullName}:ɾ��ʧ�ܣ�");
                    }
                }
                //�����ļ�
                foreach (var fls in root.GetFiles())
                {
                    try
                    {
                        if (!doDeletes.Contains(fls.FullName)) continue;
                        var fileName = Path.GetFileNameWithoutExtension(fls.Name);
                        var canParse = DateTime.TryParseExact(fileName, model.SubPathFormat, null, System.Globalization.DateTimeStyles.None, out DateTime flsTime);
                        switch (model.TimeBase)
                        {
                            case "�ļ���":
                                break;
                            case "����ʱ��":
                                flsTime = fls.CreationTime;
                                break;
                            default: //"�Զ�"
                                if (!canParse) flsTime = fls.CreationTime;
                                break;
                        }
                        if (flsTime == DateTime.MinValue) continue; //�ļ��������������Ļ�������Сֵ
                        if (flsTime <= DateTime.Today.AddDays(-model.ReserveDays))
                        {
                            if (!File.Exists(fls.FullName)) return;
                            File.Delete(fls.FullName);
                            LogHelper.Info($"{fls.FullName}:ɾ���ɹ���");
                        }
                    }
                    catch
                    {
                        LogHelper.Warn($"{fls.FullName}:ɾ��ʧ�ܣ�");
                    }
                }
            });
        }
        #endregion

        /// <summary>
        /// ����������Ϣ
        /// </summary>
        async void SendHeartBeat()
        {
            var log = new LogModelForWatcher();
            log.Name = CM.HostName;
            log.LogTime = DateTime.Now;
            log.wType = WriteType.������;
            log.Description = "HeartBeat";
            string json = JsonHelper.SerializeObject(log);
            await Send(Encoding.UTF8.GetBytes(json), multiCastEnd, (ushort)WATCHER.HEARTBEAT);
        }

        /// <summary>
        /// ������־
        /// </summary>
        public async void SendLog(LogModelForWatcher log)
        {
            log.Name = CM.HostName;
            log.LogTime = DateTime.Now;
            log.wType = WriteType.������;
            //������ʾ,��windows
            if (!CM.SpeechEnable) log.IsSpeak = false;
            //if (log.IsSpeak && RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) speech.SpeakAsync(log.Description);
            //�鲥��־
            LogHelper.Info($"������־��{log.Description}");
            string json = JsonHelper.SerializeObject(log);
            await Send(Encoding.UTF8.GetBytes(json), multiCastEnd, (ushort)WATCHER.WENUM_WATCHER_INFO);
        }
    }
}
