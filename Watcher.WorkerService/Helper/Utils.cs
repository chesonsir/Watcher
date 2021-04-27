using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpCompress.Writers;
using SharpCompress.Common;
using SharpCompress.Readers;
using Watcher.WorkerService.Model;
using System.Net.NetworkInformation;
using System.Net;
using System.Net.Sockets;
using Watcher.WorkerService.Protocol;

namespace Watcher.WorkerService.Helper
{
    class Utils
    {
        /// <summary>
        /// 检查配置文件内容是否正确
        /// </summary>
        public static void CheckConfigModel(ConfigModel cm)
        {
            cm.MonitoredProcesses.ForEach(model =>
            {
                model.IsVaild = !string.IsNullOrEmpty(model.Name);
            });

            cm.MonitoredPathes.ForEach(model =>
            {
                model.UpPathList.ForEach(child =>
                {
                    if (!IsHostPath(child.UploadPath) && !IsFtp(child.UploadPath)) model.IsVaild = LogHelper.Error(@$"{child.UploadPath}:路径格式错误！");
                });
                if (!Directory.Exists(model.ParentPath)) model.IsVaild = LogHelper.Error($"{model.ParentPath}:路径不存在！");
                if (!IsDateFormat(model.SubPathFormat))
                {
                    LogHelper.Warn($"{model.SubPathFormat}:子文件夹名称格式应当包含日期！");
                    model.SubPathFormat = "";
                    model.TimeBase = "自动"; //SubPathFormat不正确就不能按照文件名来解析时间了
                }
                if (!IsTime(model.Timed))
                {
                    LogHelper.Warn($"{model.Timed}:时间格式错误！");
                    model.Timed = "";
                }
                model.CompressFormat = model.CompressFormat.ToLower();
                if (model.CompressFormat != "rar" && model.CompressFormat != "zip") model.CompressFormat = "";
            });

            cm.MonitoredIPs.ForEach(model =>
            {
                if (!IsIP(model.IP, "8000")) model.IsVaild = LogHelper.Error($"{model.IP}:通信主机IP格式错误！");
            });

            if (!IsIP(cm.MutiCastIP.IP, cm.MutiCastIP.Port)) cm.MutiCastIP.IsVaild = LogHelper.Error($"组播地址格式不正确！");
        }

        /// <summary>
        /// 检查是否是共享文件夹路径 https://blog.csdn.net/yuxuac/article/details/8243121
        /// </summary>
        public static bool IsHostPath(string path)
        {
            var regexPattern = @"^\s*([a-zA-Z]:\\|\\\\)([^\^\/:*?""<>|]+\\)*([^\^\/:*?""<>|]+)$";
            var regex = new Regex(regexPattern);
            return regex.IsMatch(path);
        }

        /// <summary>
        /// 检查字符串是否年月日的格式信息
        /// </summary>
        public static bool IsDateFormat(string format)
        {
            if (format.Contains("yyyy") && format.Contains("MM") && format.Contains("dd")) return true;
            return false;
        }

        /// <summary>
        /// 是否为ip
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        public static bool IsIP(string ip, string port)
        {
            if (Regex.IsMatch(ip, @"^((2[0-4]\d|25[0-5]|[01]?\d\d?)\.){3}(2[0-4]\d|25[0-5]|[01]?\d\d?)$")
                && int.TryParse(port, out int result))
            {
                if (result < 65535)
                    return true;
                else
                    return false;
            }
            return false;
        }

        /// <summary>
        /// 是否为时间型字符串
        /// </summary>
        /// <param name="source">时间字符串(15:00:00)</param>
        /// <returns></returns>
        public static bool IsTime(string time)
        {
            return Regex.IsMatch(time, @"^((20|21|22|23|[0-1]?\d)(:|：)[0-5]?\d(:|：)[0-5]?\d)$");
        }

        /// <summary>
        /// 是否为Ftp路径
        /// </summary>
        public static bool IsFtp(string ftp) => ftp.ToLower().StartsWith("ftp://");

        /// <summary>
        /// 检查文件是否被占用
        /// </summary>
        /// <param name="fileName">文件路径</param>
        public static bool IsFileInUse(string fileName)
        {
            bool inUse = true;
            FileStream fs = null;
            try
            {
                fs = new FileStream(fileName, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                inUse = false;
            }
            catch { }
            finally
            {
                if (fs != null) fs.Close();
            }
            return inUse; //true 表示正在使用，false没有使用
        }


        #region 共享文件夹操作
        /// <summary>
        /// 尝试连接共享文件夹1次
        /// </summary>
        /// <returns></returns>
        public static bool ConnectToSharedFolder(string filename, string Name, string Pwd)
        {
            try
            {
                bool status = false;
                int ljcs = 0;
                do
                {
                    status = ConnectState(filename, Name, Pwd);
                    ljcs++;
                    if (ljcs >= 1)
                        break;
                }
                while (!status);

                if (!status)
                    LogHelper.Error($"连接远程电脑{filename}失败，请检查地址或名称是否正确");
                return status;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"{filename}:{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 连接远程共享文件夹
        /// </summary>
        /// <param name="path">远程共享文件夹的路径</param>
        /// <param name="userName">用户名</param>
        /// <param name="passWord">密码</param>
        /// <returns></returns>
        public static bool ConnectState(string path, string userName, string passWord)
        {
            bool Flag = false;
            var proc = new Process();
            try
            {
                proc.StartInfo.FileName = "cmd.exe";
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.RedirectStandardInput = true;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.RedirectStandardError = true;
                proc.StartInfo.CreateNoWindow = true;
                proc.Start();
                //string dosLine = "net use " + path + " " + passWord + " /user:" + userName;
                string dosLine;
                if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(passWord))
                    dosLine = $"net use {path}";
                else
                    dosLine = $"net use {path} {passWord} /user:{userName}";
                proc.StandardInput.WriteLine(dosLine);
                proc.StandardInput.WriteLine("exit");
                while (!proc.HasExited)
                {
                    proc.WaitForExit(1000);
                }
                string errormsg = proc.StandardError.ReadToEnd();
                proc.StandardError.Close();
                if (string.IsNullOrEmpty(errormsg))
                {
                    Flag = true;
                }
                else
                {
                    throw new Exception(errormsg);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"{path}:{ex.Message}");
                throw;
            }
            finally
            {
                proc.Close();
                proc.Dispose();
            }
            return Flag;
        }

        /// <summary>
        /// 向远程文件夹保存本地内容，或者从远程文件夹下载文件到本地
        /// </summary>
        /// <param name="src">要保存的文件的路径，如果保存文件到共享文件夹，这个路径就是本地文件路径如：@"D:\1.avi"</param>
        /// <param name="dst">保存文件的路径，不含名称及扩展名</param>
        /// <param name="fileName">保存文件的名称以及扩展名</param>
        public static void Transport(string src, string dst, string fileName)
        {
            FileStream inFileStream = new FileStream(src, FileMode.Open);
            if (!Directory.Exists(dst))
            {
                Directory.CreateDirectory(dst);
            }
            dst += fileName;
            FileStream outFileStream = new FileStream(dst, FileMode.OpenOrCreate);

            byte[] buf = new byte[inFileStream.Length];

            int byteCount;

            while ((byteCount = inFileStream.Read(buf, 0, buf.Length)) > 0)
            {
                outFileStream.Write(buf, 0, byteCount);
            }

            inFileStream.Flush();
            inFileStream.Close();
            outFileStream.Flush();
            outFileStream.Close();
        }
        /// <summary>
        /// 复制文件夹
        /// </summary>
        /// <param name="SourcePath"></param>
        /// <param name="DestinationPath"></param>
        /// <param name="overwriteexisting"></param>
        /// <returns></returns>
        public static void CopyDirectory(string SourcePath, string DestinationPath, bool overwriteexisting)
        {
            try
            {
                SourcePath = SourcePath.EndsWith(@"\") ? SourcePath : SourcePath + @"\";
                DestinationPath = DestinationPath.EndsWith(@"\") ? DestinationPath : DestinationPath + @"\";
                if (Directory.Exists(SourcePath))
                {
                    if (Directory.Exists(DestinationPath) == false)
                    {
                        Directory.CreateDirectory(DestinationPath);
                    }
                    foreach (string fls in Directory.GetFiles(SourcePath))
                    {
                        FileInfo flinfo = new FileInfo(fls);
                        flinfo.CopyTo(DestinationPath + flinfo.Name, overwriteexisting);
                    }
                    foreach (string drs in Directory.GetDirectories(SourcePath))
                    {
                        DirectoryInfo drinfo = new DirectoryInfo(drs);
                        CopyDirectory(drs, DestinationPath + drinfo.Name, overwriteexisting);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"{SourcePath}:{ex.Message}");
                throw;
            }
        }
        /// <summary>
        /// 递归删除文件夹及目录文件
        /// </summary>
        /// <param name="dir"></param>
        public static void DeleteFolder(string dir)
        {
            if (Directory.Exists(dir))
            {
                var toDelete = Directory.GetFileSystemEntries(dir);
                foreach (string d in toDelete)
                {
                    if (File.Exists(d))
                        File.Delete(d);
                    else
                        DeleteFolder(d);
                }
                Directory.Delete(dir, true); 
            }
        }
        #endregion

        #region 文件压缩和解压
        /// <summary>
        /// 压缩文件/文件夹
        /// </summary>
        /// <param name="filePath">需要压缩的文件/文件夹路径</param>
        /// <param name="zipPath">压缩文件路径（zip后缀）</param>
        /// <param name="filterExtenList">需要过滤的文件后缀名</param>
        public static void CompressionFile(string filePath, string zipPath, List<string> filterExtenList = null)
        {
            try
            {
                using var zip = File.Create(zipPath);
                var option = new WriterOptions(CompressionType.Deflate)
                {
                    ArchiveEncoding = new ArchiveEncoding()
                    {
                        Default = Encoding.UTF8
                    }
                };
                using var zipWriter = WriterFactory.Open(zip, ArchiveType.Zip, option);
                if (Directory.Exists(filePath))
                {
                    //添加文件夹
                    zipWriter.WriteAll(filePath, "*",
                      (path) => filterExtenList == null || !filterExtenList.Any(d => Path.GetExtension(path).Contains(d, StringComparison.OrdinalIgnoreCase)), SearchOption.AllDirectories);
                }
                else if (File.Exists(filePath))
                {
                    zipWriter.Write(Path.GetFileName(filePath), filePath);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"{filePath}:{ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 解压文件
        /// </summary>
        /// <param name="zipPath">压缩文件路径</param>
        /// <param name="dirPath">解压到文件夹路径</param>
        /// <param name="password">密码</param>
        public static void DeCompressionFile(string zipPath, string dirPath, string password = "")
        {
            if (!File.Exists(zipPath))
            {
                LogHelper.Error($"{zipPath}:zipPath压缩文件不存在");
                throw new ArgumentNullException("zipPath压缩文件不存在");
            }
            Directory.CreateDirectory(dirPath);
            try
            {
                using Stream stream = File.OpenRead(zipPath);
                var option = new ReaderOptions()
                {
                    ArchiveEncoding = new SharpCompress.Common.ArchiveEncoding()
                    {
                        Default = Encoding.UTF8
                    }
                };
                if (!string.IsNullOrWhiteSpace(password))
                {
                    option.Password = password;
                }

                var reader = ReaderFactory.Open(stream, option);
                while (reader.MoveToNextEntry())
                {
                    if (reader.Entry.IsDirectory)
                    {
                        Directory.CreateDirectory(Path.Combine(dirPath, reader.Entry.Key));
                    }
                    else
                    {
                        //创建父级目录，防止Entry文件,解压时由于目录不存在报异常
                        var file = Path.Combine(dirPath, reader.Entry.Key);
                        Directory.CreateDirectory(Path.GetDirectoryName(file));
                        reader.WriteEntryToFile(file);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"{zipPath}:{ex.Message}");
                throw;
            }
        }
        #endregion


        #region 通信
        /// <summary>
        /// 用于检查IP地址或域名是否可以使用TCP/IP协议访问(使用Ping命令),true表示Ping成功,false表示Ping失败 
        /// </summary>
        /// <param name="strIpOrDName">输入参数,表示IP地址或域名</param>
        /// <returns></returns>
        public static bool PingIpOrDomainName(string strIpOrDName)
        {
            try
            {
                Ping objPingSender = new();
                PingOptions objPinOptions = new();
                objPinOptions.DontFragment = true;
                string data = "";
                byte[] buffer = Encoding.UTF8.GetBytes(data);
                int intTimeout = 120;
                PingReply objPinReply = objPingSender.Send(strIpOrDName, intTimeout, buffer, objPinOptions);
                string strInfo = objPinReply.Status.ToString();
                if (strInfo == "Success")
                    return true;
                else
                    return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 异步数据发送函数
        /// </summary>
        /// <param name="buf">数据字节</param>
        /// <param name="sendPoint">目的地址</param>
        /// <param name="messageID">消息识别ID</param>
        /// <returns></returns>
        public static async Task<int> Send(byte[] buf, IPEndPoint sendPoint, ushort messageID)
        {
            //新建对象
            UdpClient sendUdpClient = new UdpClient();
            List<byte> sendBytes = new List<byte>();
            //添加字头
            sendBytes.AddRange(new byte[] { 0xff, 0xff });
            //添加消息头
            MSG_PROTOCOL_HEADER msgHead = new();
            msgHead.usLen = (ushort)(buf.Length + 12 + 1);//加上消息头长度12个字节和校验和1个字节
            msgHead.usMessageID = messageID;
            byte[] headBuf = StructToBytes(msgHead);
            //计算校验和
            byte sum = 0;
            foreach (byte b in headBuf)
                sum -= b;
            foreach (byte b in buf)
                sum -= b;
            //添加消息头、数据段和校验和
            sendBytes.AddRange(headBuf);
            sendBytes.AddRange(buf);
            sendBytes.Add(sum);
            //异步发送数据
            int ti = await sendUdpClient.SendAsync(sendBytes.ToArray(), sendBytes.Count, sendPoint);
            sendBytes.Clear();
            return ti;
        }

        /// <summary>
        /// 结构体转byte数组
        /// </summary>
        /// <param name="structObj">要转换的结构体</param>
        /// <returns>转换后的byte数组</returns>
        public static byte[] StructToBytes(object structObj)
        {
            //得到结构体的大小
            int size = Marshal.SizeOf(structObj);
            //创建byte数组
            byte[] bytes = new byte[size];
            //分配结构体大小的内存空间
            IntPtr structPtr = Marshal.AllocHGlobal(size);
            //将结构体拷到分配好的内存空间
            Marshal.StructureToPtr(structObj, structPtr, false);
            //从内存空间拷到byte数组
            Marshal.Copy(structPtr, bytes, 0, size);
            //释放内存空间
            Marshal.FreeHGlobal(structPtr);
            //返回byte数组
            return bytes;
        }

        /// <summary>
        /// byte数组转结构体
        /// </summary>
        /// <param name="bytes">byte数组</param>
        /// <param name="type">结构体类型</param>
        /// <returns>转换后的结构体</returns>
        public static object BytesToStruct(byte[] bytes, Type type)
        {
            int size = Marshal.SizeOf(type);
            //byte数组长度小于结构体的大小
            if (size > bytes.Length)
            {
                //返回空
                return null;
            }
            //分配结构体大小的内存空间
            IntPtr structPtr = Marshal.AllocHGlobal(size);
            //将byte数组拷到分配好的内存空间
            Marshal.Copy(bytes, 0, structPtr, size);
            //将内存空间转换为目标结构体
            object obj = Marshal.PtrToStructure(structPtr, type);
            //释放内存空间
            Marshal.FreeHGlobal(structPtr);
            //返回结构体
            return obj;
        }
        #endregion
    }
}
